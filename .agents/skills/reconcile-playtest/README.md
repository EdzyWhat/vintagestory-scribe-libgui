# reconcile-playtest

The **back-edge** of the playtest loop: turn submitted playtest reports into checked boxes
and a triage queue, deterministically.

```
what-to-test   →   play + submit          →   reconcile-playtest
(generate          (vs-playtest-checklist      (this: close boxes +
 TESTING.md)        writes .playtest-            triage feedback)
                    submissions/<ts>.json)
```

## Parts

- **`reconcile.py`** — model-free Python (stdlib only). Does all the mechanical work; no
  reasoning, no network, no game install. This is the source of truth for the logic.
- **`SKILL.md`** — the thin agent shell. Runs the command, reads its JSON report, and drives
  the judgment ~20% (triage, caveat glances, proposals, archive follow-through). Holds no
  mechanical logic.
- **`test_reconcile.py`** — `unittest` suite: fixture cases (clean pass, fail, null→triage,
  partial, multi-submission, unresolvable key, general-notes, idempotency, dry-run) plus a
  real-repo regression that dry-runs over `.playtest-submissions/reviewed/` and asserts the
  derived verdicts still match TESTING.md.

## Usage

```bash
# From the repo root. Applies terminal-verdict items, captures triage, moves resolved
# submissions to reviewed/, prints a JSON report.
python3 .claude/skills/reconcile-playtest/reconcile.py --json

# Preview only — identical report, zero writes.
python3 .claude/skills/reconcile-playtest/reconcile.py --dry-run

# Also archive changes now at 100% (serialized `openspec archive <name> -y`).
python3 .claude/skills/reconcile-playtest/reconcile.py --archive

# Overrides (rarely needed):
#   --date YYYY-MM-DD   stamp for verdict lines (default: today)
#   --repo DIR          repo root (default: walk up for TESTING.md)
#   --pending-dir DIR   submissions dir (default: <repo>/.playtest-submissions)

# Tests
python3 .claude/skills/reconcile-playtest/test_reconcile.py
```

## What it does (deterministic core)

Reads each pending `.playtest-submissions/*.json`. For each item, keyed off `fingerprint`
(→ the TESTING.md item) and `taskId` = `"<change> <N.M>"` (→ the `openspec/changes/<change>/tasks.md`
box) — **never fuzzy text**:

| verdict | TESTING.md | tasks.md box |
|---|---|---|
| `pass` | `- **Confirmed <date>** (submission <ts>): "<note>"` | flipped `[ ]`→`[x]` + dated, sourced note |
| `fail` | `- **Still broken <date>:** (submission <ts>) "<note>"` | left `[ ]` |
| `null` | *(nothing)* | *(nothing)* → routed to `triage-inbox.md` |

- **Idempotent**: never inserts a duplicate verdict line, never re-flips a box, never
  unchecks/downgrades an item that already carries a verdict; the latest `submittedAt` wins
  for a fingerprint seen in multiple submissions. Re-running is a safe no-op.
- **Triage inbox** (`.playtest-submissions/triage-inbox.md`): every `verdict:null` item and
  every submission's non-empty `generalNotes`, keyed so re-runs don't duplicate.
- **Ready-to-archive**: after propagation, reports in-progress changes whose tasks.md is now
  fully checked. `--archive` runs `openspec archive` for each.
- **Lifecycle**: a submission moves to `reviewed/` only when all its items reached a terminal
  write AND it carries no still-pending `null` item; otherwise it stays pending.
- **Glance flags**: `pass` notes containing a caveat ("but", "doesn't", "except", "todo", …)
  are flagged in the report for a human glance — without blocking the auto-apply.
- **Errors, never guesses**: an unresolvable `fingerprint`/`taskId`, or a `fail` that
  conflicts with an existing Confirmed item, is reported as an error; nothing is applied for
  it. Fix the key and re-run.

## Optional follow-up (out of scope here)

A future enhancement in the sibling **`vs-playtest-checklist`** web app would make some of
this even cleaner, but is **not required** for this tool to work:

- **Always capture a verdict state** — the app currently allows `verdict: null` (unsure);
  those become triage entries. Fine as-is, just noted.
- **A dedicated "new feedback" field** distinct from `generalNotes`, so new-bug/feature
  reports land as structured triage entries directly instead of via free text.

Neither is needed: the current submission JSON already carries enough
(`fingerprint`/`taskId`/`verdict`/`generalNotes`) for this reconciler.
