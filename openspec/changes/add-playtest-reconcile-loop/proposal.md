## Why

The playtest loop is one-directional and its back half is done by hand. The web app writes
structured submissions (`.playtest-submissions/*.json` — each item report already carries a
`fingerprint`, a `taskId` like `"tablet-text-visibility 5.6"`, a `verdict` of `pass`/`fail`/`null`,
plus notes and screenshots), but nothing propagates those verdicts back: an agent must read each
submission, re-derive the TESTING.md verdict line, hand-flip the matching `tasks.md` checkbox, and
run `openspec archive` one change at a time. This session that meant reconciling ~27 boxes across 9
changes and archiving 12 by hand — the "almost hour-long round trip." ~80% of that work is purely
mechanical (the `taskId` is an exact pointer — no reasoning needed) and should never touch a model;
the remaining ~20% (free-text `verdict:null` items and `generalNotes` new-bug/feature reports) is
the only part that needs judgment, and today it has no structured path into a new proposal or test
item.

## What Changes

- Add a **deterministic reconciler** (`reconcile.py`, model-free Python) that, for every pending
  submission item with a terminal `verdict`, writes/updates its `TESTING.md` verdict line
  (`pass`→**Confirmed**, `fail`→**Still broken**) and — for `pass` — flips the matching `tasks.md`
  checkbox to `[x]` with a dated note citing the submission + fingerprint, keyed off `taskId`/
  `fingerprint` (never fuzzy text-matching).
- The reconciler **detects changes now at 100%** and reports them as ready-to-archive; an opt-in
  flag runs `openspec archive` for each.
- The reconciler routes the judgment ~20% — `verdict:null` items and every submission's
  `generalNotes` — into a **triage inbox** (`.playtest-submissions/triage-inbox.md`) instead of
  silently dropping it, so new bug/feature feedback has a durable home.
- The reconciler emits a **structured report** (machine + human summary) of what it auto-applied
  and what needs triage, and moves fully-resolved submissions to `reviewed/`.
- Add a **`reconcile-playtest` skill** — the thin agent shell that runs the command, reads its
  report, and drives the triage 20% in-terminal: classify ambiguous verdicts, spin `openspec-propose`
  for new-feature/new-bug notes, and offer to run the archives. Deterministic core, agent shell.
- Screenshot sharing and the existing `triage-screenshot` skill are unchanged; `TESTING.md` remains
  the human/git-readable derived view (JSON submissions stay the source of truth — no database).

## Capabilities

### New Capabilities
- `playtest-reconcile`: a model-free reconciler that turns structured playtest submissions into
  TESTING.md verdicts, `tasks.md` checkbox propagation, ready-to-archive detection, and a triage
  inbox for judgment-needing feedback — plus a skill that runs it and drives the remaining triage
  in-terminal.

### Modified Capabilities
<!-- None. The `what-to-test` skill's generation/verdict-recording behavior is unchanged; this adds
     the missing back-edge as a new, separate capability rather than modifying that spec. -->

## Impact

- **New files** (scribe repo): a `reconcile.py` tool (proposed under `.claude/skills/` alongside
  `what-to-test`, or `build/` — decided in design), a `reconcile-playtest` skill, and a generated
  `.playtest-submissions/triage-inbox.md`.
- **Reads/writes**: `.playtest-submissions/*.json` (moves resolved ones to `reviewed/`), `TESTING.md`
  (verdict lines), `openspec/changes/*/tasks.md` (checkbox + note), and shells `openspec archive`.
- **No new runtime/mod dependencies** — Python stdlib only; touches no `src/` code and no game build.
- **No web-app change required** to ship: the current submission JSON already carries enough
  (`fingerprint`/`taskId`/`verdict`/`generalNotes`). A follow-up in the sibling `vs-playtest-checklist`
  app (always capture a verdict state; a dedicated "new feedback" field) is noted as optional and
  out of scope here.
- **Cross-project note**: this is scribe-repo tooling; it does not alter OpenSpec's own archive
  semantics (archiving applies spec deltas and does not gate on checkbox completeness — the
  reconciler's 100% detection is an advisory convenience, not a hard gate).
