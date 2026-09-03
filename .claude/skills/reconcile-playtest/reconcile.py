#!/usr/bin/env python3
"""reconcile.py -- deterministic playtest-submission reconciler (the back-edge of the
playtest loop).

`what-to-test` generates TESTING.md from OpenSpec tasks.md; the vs-playtest-checklist
web app writes structured `.playtest-submissions/<ts>.json` reports on submit. This
tool closes the loop WITHOUT a model: for every pending submission item carrying a
terminal `verdict`, it records the result deterministically --

    verdict "pass" -> a **Confirmed** line under the TESTING.md item, that item's OWN
                      checkbox flipped [ ] -> [x], AND the matching tasks.md checkbox
                      flipped [ ] -> [x] with a dated, sourced note
    verdict "fail" -> a **Still broken** line under the TESTING.md item (box stays [ ])
    verdict null   -> NOT written mechanically; routed to the triage inbox

It keys off `fingerprint` (locates the TESTING.md item) and `taskId` = "<change> <N.M>"
(locates the tasks.md box) -- never fuzzy text matching. Unresolvable keys are reported
as errors, never guessed. All edits are surgical in-place line rewrites, and the tool is
idempotent (re-running is a safe no-op on already-applied items).

Judgment-needing feedback (verdict:null items and every non-empty generalNotes) is
appended to `.playtest-submissions/triage-inbox.md` so it has a durable home. After
propagation the tool reports OpenSpec changes now at 100% as ready-to-archive; `--archive`
runs `openspec archive <name> -y` for each (serialized). The `reconcile-playtest` skill
runs this command, reads its JSON report, and drives the remaining ~20% in-terminal.

Deliberately stdlib-only (mirrors the no-new-deps guardrail and vs-playtest-checklist's
server.py). Regexes below are copied from server.py so the two parse TESTING.md
identically.

Usage:
    python3 reconcile.py [--dry-run] [--archive] [--json]
                         [--repo DIR] [--pending-dir DIR] [--date YYYY-MM-DD]
"""
import argparse
import datetime
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent

# --- TESTING.md grammar (kept byte-for-byte compatible with vs-playtest-checklist/server.py) ---
ITEM_RE = re.compile(r"^- \[( |x)\] `([0-9a-f]{8})` (.*)$")
HEADING_RE = re.compile(r"^## (.+)$")
ANNOTATION_ENTRY_RE = re.compile(r"^\s+- \*\*(.+?)\*\*:?\s*(.*)$")
VERDICT_LEAD_RE = re.compile(r"^(Confirmed|Still broken|Backlogged|Obsolete)\b")
ANNOTATION_KINDS = {
    "Confirmed": "confirmed",
    "Still broken": "broken",
    "Backlogged": "backlog",
    "Obsolete": "obsolete",
}

# tasks.md box: `- [ ] 5.6 ...` (task number is dotted digits at column 0-ish).
TASK_BOX_RE = re.compile(r"^(\s*)- \[( |x)\] (\d+(?:\.\d+)+)\b(.*)$")

# Any `- [ ]`/`- [x]` line in a tasks.md, used only for the "fully checked?" scan.
ANY_BOX_RE = re.compile(r"^\s*- \[( |x)\] ")

# taskId in a submission is "<change> <N.M>" -- change name, a space, then the number.
TASK_ID_RE = re.compile(r"^(.+?)\s+(\d+(?:\.\d+)+)$")

# Caveat markers that flag a `pass` note for a human glance (never blocks auto-apply).
CAVEAT_RE = re.compile(r"\b(but|doesn'?t|except|todo|however|caveat|almost)\b", re.IGNORECASE)

VERDICT_INDENT = "      "  # 6 spaces -- matches existing TESTING.md verdict bullets
TASK_NOTE_INDENT = "  "    # 2 spaces -- matches existing tasks.md reconcile notes

TRIAGE_HEADER = """# Triage inbox

Judgment-needing playtest feedback the deterministic reconciler could NOT close on its
own: every `verdict: null` item and every submission's non-empty general notes. The
`reconcile-playtest` skill drains this -- classify each entry, then either turn it into an
`openspec-propose` change / a new TESTING.md item, or record why it's dropped, and delete
the entry. Entries are keyed (`<!-- key: ... -->`) so re-running the reconciler never
duplicates one.
"""


# --------------------------------------------------------------------------------------
# Repo / submission discovery
# --------------------------------------------------------------------------------------
def find_repo_root(explicit=None):
    """Resolve the scribe repo root. Prefer --repo; else walk up from this script for a
    directory that holds TESTING.md (mirrors server.py's find_testing_file_upward)."""
    if explicit:
        root = Path(explicit).resolve()
        if not (root / "TESTING.md").is_file():
            raise SystemExit(f"--repo {root} has no TESTING.md")
        return root
    current = HERE
    while True:
        if (current / "TESTING.md").is_file():
            return current
        if current.parent == current:
            raise SystemExit("could not locate TESTING.md above " + str(HERE))
        current = current.parent


def load_submissions(pending_dir):
    """Load pending `<ts>.json` reports (top-level only -- never recurses into reviewed/).
    Tolerates missing/extra keys. Returns [{path, stem, submittedAt, generalNotes,
    generalScreenshots, items:[...]}], sorted by filename (chronological)."""
    subs = []
    if not pending_dir.is_dir():
        return subs
    for path in sorted(pending_dir.glob("*.json")):
        try:
            with open(path, encoding="utf-8") as f:
                raw = json.load(f)
        except (OSError, json.JSONDecodeError) as e:
            subs.append({"path": path, "stem": path.stem, "error": f"unreadable: {e}",
                         "items": [], "generalNotes": "", "generalScreenshots": []})
            continue
        items = []
        for it in raw.get("items", []) or []:
            items.append({
                "fingerprint": it.get("fingerprint"),
                "taskId": it.get("taskId"),
                "verdict": it.get("verdict"),
                "note": (it.get("note") or "").strip(),
                "actual": (it.get("actual") or "").strip(),
                "expected": (it.get("expected") or "").strip(),
                "screenshots": it.get("screenshots") or [],
            })
        subs.append({
            "path": path,
            "stem": path.stem,  # filename-style ts, e.g. 2026-08-20T21-32-19
            "submittedAt": raw.get("submittedAt") or path.stem,
            "generalNotes": (raw.get("generalNotes") or "").strip(),
            "generalScreenshots": raw.get("generalScreenshots") or [],
            "items": items,
        })
    return subs


# --------------------------------------------------------------------------------------
# TESTING.md parsing (with surgical insert points)
# --------------------------------------------------------------------------------------
def parse_testing(lines):
    """Parse TESTING.md into an index keyed by fingerprint. Each item records the line
    range of its block and `insert_at` -- the index at which a new verdict bullet should
    be inserted (just after the last non-blank line of the item's block)."""
    items = {}
    cur = None

    def close(item, end_idx):
        e = end_idx
        while e - 1 > item["start"] and lines[e - 1].strip() == "":
            e -= 1
        item["insert_at"] = e
        items[item["fingerprint"]] = item

    for i, line in enumerate(lines):
        if HEADING_RE.match(line):
            if cur:
                close(cur, i)
                cur = None
            continue
        m = ITEM_RE.match(line)
        if m:
            if cur:
                close(cur, i)
            cur = {"fingerprint": m.group(2), "checked": m.group(1) == "x",
                   "start": i, "anno_lines": [], "anno_kinds": []}
            continue
        if cur is None:
            continue
        em = ANNOTATION_ENTRY_RE.match(line)
        if em:
            label = em.group(1).strip()
            vm = VERDICT_LEAD_RE.match(label)
            cur["anno_kinds"].append(ANNOTATION_KINDS[vm.group(1)] if vm else None)
            cur["anno_lines"].append(line)
    if cur:
        close(cur, len(lines))
    return items


def item_has_verdict(item, kind=None):
    kinds = [k for k in item["anno_kinds"] if k]
    if kind is None:
        return bool(kinds)
    return kind in kinds


def item_cites_submission(item, stem):
    needle = f"(submission {stem})"
    return any(needle in ln for ln in item["anno_lines"])


# --------------------------------------------------------------------------------------
# tasks.md resolution
# --------------------------------------------------------------------------------------
def resolve_task_box(repo, change, num):
    """Return (path, lines, box_line_index) for `<change> <num>`, or (path, None, None)
    if the file or the box line can't be found. Never fuzzy-matches."""
    path = repo / "openspec" / "changes" / change / "tasks.md"
    if not path.is_file():
        return path, None, None
    lines = path.read_text(encoding="utf-8").splitlines()
    for i, line in enumerate(lines):
        m = TASK_BOX_RE.match(line)
        if m and m.group(3) == num:
            return path, lines, i
    return path, lines, None


def tasks_fully_checked(lines):
    """True if the tasks.md has at least one box and every box is [x]."""
    seen = False
    for line in lines:
        m = ANY_BOX_RE.match(line)
        if m:
            seen = True
            if m.group(1) != "x":
                return False
    return seen


# --------------------------------------------------------------------------------------
# Triage inbox
# --------------------------------------------------------------------------------------
def triage_key(stem, fingerprint=None, notes_hash=None):
    if fingerprint:
        return f"{stem}|{fingerprint}"
    return f"{stem}|general:{notes_hash}"


def existing_triage_keys(triage_path):
    if not triage_path.is_file():
        return set()
    keys = set()
    for m in re.finditer(r"<!-- key: (.+?) -->", triage_path.read_text(encoding="utf-8")):
        keys.add(m.group(1))
    return keys


def render_triage_entry(stem, kind, text, screenshots, fingerprint=None, task_id=None,
                        notes_hash=None):
    key = triage_key(stem, fingerprint, notes_hash)
    lines = [f"<!-- key: {key} -->", f"## {kind} — submission {stem}"]
    if fingerprint:
        lines.append(f"- **item:** `{fingerprint}`" + (f" *(task {task_id})*" if task_id else ""))
    lines.append(f"- **text:** {text}" if text else "- **text:** *(none)*")
    if screenshots:
        lines.append("- **screenshots:** " + ", ".join(screenshots))
    lines.append("")
    return key, "\n".join(lines) + "\n"


# --------------------------------------------------------------------------------------
# Core reconcile
# --------------------------------------------------------------------------------------
def reconcile(repo, pending_dir, date_str, dry_run, do_archive):
    testing_path = repo / "TESTING.md"
    triage_path = pending_dir / "triage-inbox.md"
    reviewed_dir = pending_dir / "reviewed"

    report = {"applied": [], "readyToArchive": [], "triage": [], "errors": [],
              "glance": [], "movedToReviewed": []}

    subs = load_submissions(pending_dir)
    if not subs:
        return report, {}

    testing_lines = testing_path.read_text(encoding="utf-8").splitlines()
    items_index = parse_testing(testing_lines)

    # Per-fingerprint, the winning submission item is the one from the latest submittedAt.
    winner = {}  # fingerprint -> (submittedAt, sub, item)
    for sub in subs:
        for it in sub["items"]:
            fp = it["fingerprint"]
            if not fp:
                continue
            prev = winner.get(fp)
            if prev is None or sub["submittedAt"] >= prev[0]:
                winner[fp] = (sub["submittedAt"], sub, it)

    # Buffered edits, applied bottom-up after all decisions so indices stay valid.
    testing_inserts = []          # (insert_at, [lines])
    testing_box_flips = []        # [item_start_idx] -- same-length glyph swap, order-independent
    tasks_edits = {}              # path -> {"lines": [...], "ops": [(box_idx, note_line)]}
    triage_appends = []           # rendered entry strings (deduped)
    triage_keys = existing_triage_keys(triage_path)
    # Track, per submission, whether every item reached a terminal outcome (for lifecycle).
    sub_item_terminal = {sub["stem"]: [] for sub in subs}

    def ensure_tasks_entry(path, lines):
        if path not in tasks_edits:
            tasks_edits[path] = {"lines": lines, "ops": []}
        return tasks_edits[path]

    # ---- items ----
    for sub in subs:
        stem = sub["stem"]
        if sub.get("error"):
            report["errors"].append({"submission": stem, "error": sub["error"]})
            continue
        for it in sub["items"]:
            fp = it["fingerprint"]
            verdict = it["verdict"]
            note = it["note"] or it["actual"] or "(no note)"

            # null -> triage, never a mechanical write.
            if verdict not in ("pass", "fail"):
                sub_item_terminal[stem].append(False)
                key, entry = render_triage_entry(
                    stem, "null-verdict item", note, it["screenshots"],
                    fingerprint=fp, task_id=it["taskId"])
                t = {"submission": stem, "fingerprint": fp, "taskId": it["taskId"],
                     "kind": "null-verdict", "note": note}
                report["triage"].append(t)
                if key not in triage_keys:
                    triage_keys.add(key)
                    triage_appends.append(entry)
                continue

            # This fingerprint may appear in several submissions; only the winner acts.
            win_stem = winner[fp][1]["stem"] if fp in winner else stem
            is_winner = (win_stem == stem)

            item = items_index.get(fp)
            if item is None:
                report["errors"].append({"submission": stem, "fingerprint": fp,
                                          "taskId": it["taskId"],
                                          "error": "fingerprint not found in TESTING.md"})
                sub_item_terminal[stem].append(False)
                continue

            if not is_winner:
                # Superseded by a newer submission for the same item -- terminal for
                # lifecycle (nothing to write here), the winner does the writing.
                report["applied"].append({"submission": stem, "fingerprint": fp,
                                          "verdict": verdict, "status": "superseded",
                                          "supersededBy": win_stem})
                sub_item_terminal[stem].append(True)
                continue

            # Idempotency: this exact submission already recorded on the item.
            if item_cites_submission(item, stem):
                report["applied"].append({"submission": stem, "fingerprint": fp,
                                          "verdict": verdict, "status": "already-applied"})
                sub_item_terminal[stem].append(True)
                continue

            if verdict == "pass":
                # Flag caveat-laden passes for a human glance (never blocks the apply).
                if CAVEAT_RE.search(note):
                    report["glance"].append({"submission": stem, "fingerprint": fp,
                                             "taskId": it["taskId"], "note": note})
                # TESTING.md Confirmed line (skip if already Confirmed from another source).
                if not item_has_verdict(item, "confirmed"):
                    line = (f'{VERDICT_INDENT}- **Confirmed {date_str}** '
                            f'(submission {stem}): "{_oneline(note)}"')
                    testing_inserts.append((item["insert_at"], [line]))
                # Confirmed also checks the TESTING.md item's OWN box (previously only the
                # tasks.md box got flipped here, leaving a Confirmed item's box stuck at [ ] --
                # harmless for the app, which keys off the annotation word, but inconsistent
                # with this file's own documented "Confirmed -> [x]" contract).
                if not item["checked"]:
                    testing_box_flips.append(item["start"])
                # tasks.md box flip + sourced note.
                applied_box = _apply_box(repo, it["taskId"], fp, note, date_str, stem,
                                         ensure_tasks_entry, report)
                report["applied"].append({"submission": stem, "fingerprint": fp,
                                          "verdict": "pass", "status": "applied",
                                          "taskId": it["taskId"], "boxFlipped": applied_box})
                sub_item_terminal[stem].append(True)

            else:  # fail
                if item_has_verdict(item, "confirmed") and item["checked"]:
                    # Never downgrade a box/item that already carries a Confirmed verdict.
                    report["errors"].append({
                        "submission": stem, "fingerprint": fp, "taskId": it["taskId"],
                        "error": "fail verdict conflicts with an existing Confirmed item; "
                                 "not downgraded -- needs manual review"})
                    sub_item_terminal[stem].append(False)
                    continue
                line = (f'{VERDICT_INDENT}- **Still broken {date_str}:** '
                        f'(submission {stem}) "{_oneline(note)}"')
                testing_inserts.append((item["insert_at"], [line]))
                report["applied"].append({"submission": stem, "fingerprint": fp,
                                          "verdict": "fail", "status": "applied",
                                          "taskId": it["taskId"], "boxFlipped": False})
                sub_item_terminal[stem].append(True)

    # ---- general notes -> triage ----
    for sub in subs:
        if sub.get("error"):
            continue
        notes = sub["generalNotes"]
        if not notes:
            continue
        nh = hashlib.sha256(notes.encode("utf-8")).hexdigest()[:8]
        key, entry = render_triage_entry(sub["stem"], "general notes", notes,
                                         sub["generalScreenshots"], notes_hash=nh)
        report["triage"].append({"submission": sub["stem"], "kind": "generalNotes",
                                  "note": notes})
        if key not in triage_keys:
            triage_keys.add(key)
            triage_appends.append(entry)

    # ---- write TESTING.md (bottom-up so buffered indices stay valid) ----
    if (testing_inserts or testing_box_flips) and not dry_run:
        new_lines = list(testing_lines)
        # Box flips are same-length glyph swaps at a fixed index -- order-independent relative
        # to the inserts below (which shift everything after their own insert point).
        for idx in testing_box_flips:
            new_lines[idx] = new_lines[idx].replace("- [ ]", "- [x]", 1)
        for insert_at, payload in sorted(testing_inserts, key=lambda x: x[0], reverse=True):
            new_lines[insert_at:insert_at] = payload
        testing_path.write_text("\n".join(new_lines) + "\n", encoding="utf-8")

    # ---- write tasks.md files ----
    modified_tasks = {}  # path -> final lines (for ready-to-archive from in-memory state)
    for path, edit in tasks_edits.items():
        lines = list(edit["lines"])
        for box_idx, note_line in sorted(edit["ops"], key=lambda x: x[0], reverse=True):
            # Flip the box glyph in place, then drop the note directly beneath it.
            lines[box_idx] = lines[box_idx].replace("- [ ]", "- [x]", 1)
            lines[box_idx + 1:box_idx + 1] = [note_line]
        modified_tasks[path] = lines
        if not dry_run:
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    # ---- write triage inbox ----
    if triage_appends and not dry_run:
        if triage_path.is_file():
            existing = triage_path.read_text(encoding="utf-8").rstrip("\n") + "\n\n"
        else:
            existing = TRIAGE_HEADER + "\n"
        triage_path.write_text(existing + "\n".join(triage_appends), encoding="utf-8")

    # ---- ready-to-archive detection ----
    ready = detect_ready_to_archive(repo, modified_tasks)
    report["readyToArchive"] = ready

    # ---- submission lifecycle: move fully-resolved submissions to reviewed/ ----
    for sub in subs:
        stem = sub["stem"]
        if sub.get("error"):
            continue
        flags = sub_item_terminal[stem]
        all_terminal = all(flags) if flags else True  # a no-item submission is trivially done
        # A submission is only "done" when nothing about it still needs judgment: every
        # item reached a terminal write AND no null-verdict items remain pending.
        has_pending_null = any(
            (it["verdict"] not in ("pass", "fail")) for it in sub["items"])
        if all_terminal and not has_pending_null:
            report["movedToReviewed"].append(stem)
            if not dry_run:
                reviewed_dir.mkdir(parents=True, exist_ok=True)
                sub["path"].rename(reviewed_dir / sub["path"].name)

    # ---- opt-in archive ----
    if do_archive and ready and not dry_run:
        for name in ready:
            r = subprocess.run(["openspec", "archive", name, "-y"],
                               cwd=str(repo), capture_output=True, text=True)
            report.setdefault("archived", []).append({
                "change": name, "ok": r.returncode == 0,
                "stderr": (r.stderr or "").strip()[:500]})

    return report, {"testing_inserts": len(testing_inserts),
                    "tasks_files": len(tasks_edits),
                    "triage_new": len(triage_appends)}


def _oneline(text):
    return re.sub(r"\s+", " ", text).strip()


def _apply_box(repo, task_id, fp, note, date_str, stem, ensure_tasks_entry, report):
    """Flip the tasks.md box for task_id (if resolvable & currently unchecked). Returns
    True if a flip was buffered, False otherwise. Records errors for unresolvable keys."""
    if not task_id:
        report["errors"].append({"submission": stem, "fingerprint": fp,
                                 "error": "pass item has no taskId; TESTING.md updated "
                                          "but no tasks.md box to flip"})
        return False
    m = TASK_ID_RE.match(task_id.strip())
    if not m:
        report["errors"].append({"submission": stem, "fingerprint": fp,
                                 "error": f"unparseable taskId {task_id!r}"})
        return False
    change, num = m.group(1), m.group(2)
    path, lines, idx = resolve_task_box(repo, change, num)
    if lines is None:
        report["errors"].append({"submission": stem, "fingerprint": fp, "taskId": task_id,
                                 "error": f"tasks.md not found for change {change!r}"})
        return False
    if idx is None:
        report["errors"].append({"submission": stem, "fingerprint": fp, "taskId": task_id,
                                 "error": f"task {num} not found in {change}/tasks.md"})
        return False
    edit = ensure_tasks_entry(path, lines)
    # Already [x]? Idempotent no-op (but not an error).
    if "- [ ]" not in lines[idx]:
        return False
    # Don't double-buffer the same box.
    if any(op[0] == idx for op in edit["ops"]):
        return False
    note_line = (f'{TASK_NOTE_INDENT}- Confirmed {date_str}: TESTING.md `{fp}` '
                 f'"{_oneline(note)}" (submission {stem})')
    edit["ops"].append((idx, note_line))
    return True


def detect_ready_to_archive(repo, modified_tasks):
    """Scan in-progress OpenSpec changes; return names whose tasks.md is now fully checked.
    Uses this run's in-memory modified tasks.md where present, disk otherwise."""
    ready = []
    try:
        out = subprocess.run(["openspec", "list", "--json"], cwd=str(repo),
                             capture_output=True, text=True, check=True).stdout
        changes = json.loads(out).get("changes", [])
    except (subprocess.CalledProcessError, json.JSONDecodeError, FileNotFoundError):
        return ready
    for ch in changes:
        if ch.get("status") != "in-progress":
            continue
        name = ch["name"]
        path = repo / "openspec" / "changes" / name / "tasks.md"
        if path in modified_tasks:
            lines = modified_tasks[path]
        elif path.is_file():
            lines = path.read_text(encoding="utf-8").splitlines()
        else:
            continue
        if tasks_fully_checked(lines):
            ready.append(name)
    return ready


# --------------------------------------------------------------------------------------
# Reporting
# --------------------------------------------------------------------------------------
def human_summary(report):
    out = []
    applied = [a for a in report["applied"] if a["status"] == "applied"]
    already = [a for a in report["applied"] if a["status"] == "already-applied"]
    superseded = [a for a in report["applied"] if a["status"] == "superseded"]
    out.append(f"Applied: {len(applied)} verdict(s) "
               f"({sum(1 for a in applied if a['verdict']=='pass')} pass, "
               f"{sum(1 for a in applied if a['verdict']=='fail')} fail); "
               f"{len(already)} already-applied; {len(superseded)} superseded.")
    if report["glance"]:
        out.append(f"\n⚠ {len(report['glance'])} pass note(s) carry a caveat -- glance before trusting:")
        for g in report["glance"]:
            out.append(f"   - `{g['fingerprint']}` ({g['taskId']}): \"{_oneline(g['note'])[:120]}\"")
    if report["triage"]:
        out.append(f"\nTriage inbox: {len(report['triage'])} entr(ies) need judgment "
                   f"(null-verdict items + general notes).")
    if report["readyToArchive"]:
        out.append(f"\nReady to archive ({len(report['readyToArchive'])}): "
                   + ", ".join(report["readyToArchive"]))
    if report.get("archived"):
        for a in report["archived"]:
            out.append(f"   archive {a['change']}: {'ok' if a['ok'] else 'FAILED — ' + a['stderr']}")
    if report["movedToReviewed"]:
        out.append(f"\nMoved to reviewed/: {len(report['movedToReviewed'])} submission(s).")
    if report["errors"]:
        out.append(f"\n✖ {len(report['errors'])} error(s) — nothing applied for these:")
        for e in report["errors"]:
            out.append(f"   - {e.get('fingerprint', e.get('submission'))}: {e['error']}")
    return "\n".join(out)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Deterministic playtest-submission reconciler.")
    ap.add_argument("--dry-run", action="store_true",
                    help="produce the report without writing any file")
    ap.add_argument("--archive", action="store_true",
                    help="run `openspec archive <name> -y` for each ready-to-archive change")
    ap.add_argument("--json", action="store_true", help="print the machine-readable report only")
    ap.add_argument("--repo", help="scribe repo root (default: walk up for TESTING.md)")
    ap.add_argument("--pending-dir",
                    help="submissions dir (default: <repo>/.playtest-submissions)")
    ap.add_argument("--date", help="date stamp for verdict lines (default: today); pass a "
                                   "fixed value for deterministic fixture runs")
    args = ap.parse_args(argv)

    repo = find_repo_root(args.repo)
    pending_dir = Path(args.pending_dir).resolve() if args.pending_dir \
        else repo / ".playtest-submissions"
    date_str = args.date or datetime.date.today().isoformat()

    report, _stats = reconcile(repo, pending_dir, date_str, args.dry_run, args.archive)

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        prefix = "[DRY RUN] " if args.dry_run else ""
        print(prefix + human_summary(report))
        print("\n--- JSON report ---")
        print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
