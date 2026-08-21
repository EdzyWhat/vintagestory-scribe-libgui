## Context

The playtest loop today: `what-to-test` generates `TESTING.md` from OpenSpec `tasks.md`; the
`vs-playtest-checklist` web app (`server.py`) parses `TESTING.md` into JSON, renders it as tabs, and
on submit writes a `<timestamp>.json` report into `.playtest-submissions/` (never touching
`TESTING.md` directly — that file's verdicts are agent-written from evidence). A reviewing agent then
reads each submission, writes the verdict lines into `TESTING.md`, and moves the submission to
`reviewed/`. Separately (and, until now, entirely by hand) someone flips the corresponding `tasks.md`
checkboxes and archives completed changes.

The submission JSON is already structured. Each `items[]` entry carries:
`fingerprint` (8-hex item code), `taskId` (e.g. `"tablet-text-visibility 5.6"` → change + task
number), `verdict` (`"pass"` | `"fail"` | `null`), `note`, `actual`, `expected`, `screenshots[]`.
The submission also carries top-level `generalNotes` (free-text) and `generalScreenshots[]`.

Because `openspec archive` applies spec deltas and does **not** gate on checkbox completeness, the
`tasks.md` checkboxes are a human/tracking projection — safe to write mechanically from verdicts.

Established conventions this must respect: the four verdict states (**Confirmed** / **Still broken** /
**Backlogged** / **Obsolete**) whose bold lead word the web app parses as the source of truth; the
strict `^[0-9a-f]{8}$` item-code regex; retention-on-regeneration keyed by fingerprint.

## Goals / Non-Goals

**Goals:**
- Turn the mechanical ~80% of reconciliation (terminal-verdict items) into a fast, model-free script
  that cannot misfile (keys off `taskId`/`fingerprint`, not text).
- Propagate `pass` verdicts to `tasks.md` checkboxes with dated, sourced notes — the missing back-edge.
- Detect changes that are now fully verified and offer/one-shot their `openspec archive`.
- Give judgment-needing feedback (`verdict:null` items, `generalNotes`) a durable triage inbox instead
  of leaving it buried in JSON.
- Keep the human in-terminal for the ~20%: a skill runs the script and drives triage/proposals/archive
  follow-through with full context.

**Non-Goals:**
- No database — JSON submissions stay the source of truth; `TESTING.md` stays a derived view.
- No change to OpenSpec archive semantics; 100%-detection is advisory, not a gate.
- No change required to the `vs-playtest-checklist` web app to ship (a verdict-state-always +
  new-feedback-field enhancement there is a noted optional follow-up).
- Not replacing `what-to-test` (generation) or `triage-screenshot` (image review) — this is the
  complementary back-edge.
- No autonomous/unattended runs in v1 (the reconciler is invoked on demand); a hook/cron trigger is a
  later composition of the same command.

## Decisions

### Decision 1: Deterministic command + agent skill, not one agent
`reconcile.py` is pure Python (stdlib only), does all terminal-verdict work, and prints a structured
report. A `reconcile-playtest` skill runs it, reads the report, and handles only the flagged 20%.
- *Why:* the mechanical path is exactly where an LLM is slow, costly, and error-prone (the very
  round-trip we're removing); the judgment path is where terminal context is wanted. Splitting on that
  seam gives instant+accurate mechanics and human-in-terminal judgment. The report is the interface.
- *Alternative considered:* one headless agent does everything — rejected (spends tokens on trivial
  flips, can misfile, and pulls judgment away from the terminal the user wants to act in).

### Decision 2: Key off `taskId` + `fingerprint`, never fuzzy text
`fingerprint` locates the `TESTING.md` item; `taskId` (`"<change> <N.M>"`) locates the exact
`tasks.md` box. No text-similarity matching (the class of bug that vacuums a malformed item into its
neighbor). Items whose `taskId`/`fingerprint` don't resolve are reported as errors, never guessed.

### Decision 3: Verdict → state mapping is fixed and conservative
`pass` → **Confirmed** (box `[x]`, dated note citing submission ts + fingerprint + the item's `note`).
`fail` → **Still broken** (box stays `[ ]`, verdict line recorded). `null` → NOT auto-written; routed
to triage. **Multi-submission items:** the latest submission (by `submittedAt`) wins, matching the
existing "last verdict decides" rule. The script only ever moves a box `[ ]`→`[x]` on a fresh `pass`;
it never unchecks or downgrades a box already carrying a verdict (idempotent, append-safe).

### Decision 4: Triage inbox is a generated markdown queue
`verdict:null` items and every non-empty `generalNotes` become entries in
`.playtest-submissions/triage-inbox.md` (source submission + fingerprint/taskId + verbatim text +
screenshot paths). The skill drains it: classify → `openspec-propose` (new bug/feature) or add a new
`TESTING.md` item, then clear the entry. This is the structured path new feedback lacked.

### Decision 5: Report shape + submission lifecycle
`reconcile.py` emits both a human summary and machine-readable JSON: `{applied:[...],
readyToArchive:[...], triage:[...], errors:[...]}`. A submission is moved to `reviewed/` only when all
its items reached a terminal write AND its `generalNotes` were captured to triage; otherwise it stays
pending and is listed under `errors`/`triage` so nothing is silently consumed. `--archive` opt-in
runs `openspec archive <name> -y` for each `readyToArchive` (serialized, to surface spec collisions).

### Decision 6: Location + no-clobber writes
Tool lives at `.claude/skills/reconcile-playtest/reconcile.py` (peer of `what-to-test`, sharing
`next-id.py` conventions). All file edits are surgical line rewrites (find the box/item by key, edit in
place) — never a full-file rebuild — so hand edits and unrelated items are untouched. A `--dry-run`
prints the report without writing.

## Risks / Trade-offs

- **A `pass` verdict on a genuinely-wrong item auto-checks a box** → Mitigation: the note records the
  submission + verbatim player note, so it's auditable and reversible; `fail`/`null` never auto-check;
  the skill surfaces `pass` items whose note contains caveat markers (e.g. "but", "doesn't work") for a
  human glance rather than blind trust.
- **Item-code / taskId drift** (a `tasks.md` renumber after the item was minted) → Mitigation: resolve
  strictly and report unresolved keys as errors; never guess. The reconciler is re-runnable, so a fixed
  key reconciles on the next pass.
- **Partial submissions** (some items terminal, some null) → Mitigation: apply the terminal ones, keep
  the submission pending, list the rest under triage; only move to `reviewed/` when fully drained.
- **Concurrent edits** (two sessions, or a hand edit mid-run) → Mitigation: surgical idempotent writes
  + `--dry-run`; the script re-reads current file state each run and no-ops already-applied boxes.
- **Skill drift from the deterministic core** → Mitigation: the skill owns zero mechanical logic; it
  only interprets the report and drives triage — the command is the single source of mechanics, unit-
  testable on fixture submissions.

## Migration Plan

1. Ship `reconcile.py` + fixtures; validate on the existing 95 `reviewed/` submissions in `--dry-run`
   (they should reconcile to the verdicts already on file — a regression check against reality).
2. Add the `reconcile-playtest` skill wrapping it.
3. Document the loop (what-to-test = generate; play + submit; reconcile-playtest = close + triage) in
   the skill README and `TESTING.md` preamble.
- **Rollback:** the tool is additive and `--dry-run`-gated; deleting it restores the manual flow. No
  data migration (JSON + md unchanged in shape).

## Open Questions

- Exact caveat-marker heuristic for flagging suspicious `pass` notes — start with a small keyword list
  ("but", "doesn't", "except", "todo"), refine from real submissions; never blocks the auto-apply, only
  adds a "glance" flag to the report.
- Whether the optional `vs-playtest-checklist` enhancement (always-capture verdict state; dedicated
  new-feedback field that writes triage entries directly) is worth a follow-up proposal in that repo —
  deferred; the current JSON is sufficient for v1.
