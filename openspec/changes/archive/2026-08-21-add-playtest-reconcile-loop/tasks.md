## 1. Reconciler skeleton + submission parsing

- [x] 1.1 Create `.claude/skills/reconcile-playtest/reconcile.py` (stdlib only). Add `--dry-run` and
      `--archive` flags and a repo-root resolver (walk up for `TESTING.md`, mirroring `server.py`'s
      `find_testing_file_upward`).
- [x] 1.2 Load pending `.playtest-submissions/*.json` (top-level files only, not `reviewed/`). Parse
      each into `{submittedAt, generalNotes, generalScreenshots, items:[{fingerprint, taskId, verdict,
      note, actual, expected, screenshots}]}`. Tolerate missing/extra keys.
- [x] 1.3 Build an index of `TESTING.md` items keyed by fingerprint, and a resolver from `taskId`
      (`"<change> <N.M>"`) to the exact `openspec/changes/<change>/tasks.md` box line. Report
      unresolved keys as errors — never fuzzy-match.

## 2. Deterministic verdict propagation

- [x] 2.1 For `verdict: "pass"`: write a `- **Confirmed <date>** (submission <ts>): <note>` line under
      the TESTING.md item (surgical in-place insert after the item, before the next item/heading) and
      flip its `tasks.md` box `[ ]`→`[x]` with a 2-space-indented dated note citing submission + fingerprint.
- [x] 2.2 For `verdict: "fail"`: write a `- **Still broken <date>:** <note>` line under the TESTING.md
      item; leave the tasks.md box `[ ]`.
- [x] 2.3 Idempotency: never insert a duplicate verdict line, never re-flip an already-checked box,
      never uncheck/downgrade an item that already carries a verdict. Multi-submission items resolve to
      the latest `submittedAt`. Re-running is a safe no-op on already-applied items.
- [x] 2.4 Pass `date`/timestamp values in from the CLI/runner (do NOT read a wall clock inside logic
      that must stay testable) so fixture runs are deterministic.

## 3. Triage inbox

- [x] 3.1 For `verdict: null` items and every non-empty `generalNotes`, append an entry to
      `.playtest-submissions/triage-inbox.md`: source submission ts, fingerprint/taskId (items only),
      verbatim text, screenshot paths. Do not write a verdict line or check a box for these.
- [x] 3.2 De-dupe triage entries across re-runs (key on submission ts + fingerprint / a `generalNotes`
      hash) so the inbox is append-safe.

## 4. Ready-to-archive detection + opt-in archive

- [x] 4.1 After propagation, scan in-progress changes (`openspec list --json`) and mark those whose
      tasks.md is now fully checked as ready-to-archive.
- [x] 4.2 With `--archive`, run `openspec archive <name> -y` for each, serialized, capturing per-change
      success/collision into the report; without the flag, only list them.

## 5. Report + submission lifecycle

- [x] 5.1 Emit a machine-readable JSON report (`{applied, readyToArchive, triage, errors}`) and a
      concise human summary to stdout. `--dry-run` produces the report with zero writes.
- [x] 5.2 Move a submission to `reviewed/` only when all its items reached a terminal write AND its
      generalNotes were captured to triage; otherwise leave it pending and list it under triage/errors.
- [x] 5.3 Flag `pass` items whose `note` contains caveat markers ("but", "doesn't", "except", "todo",
      case-insensitive) in the report's summary as "glance" items — without blocking their auto-apply.

## 6. Tests (deterministic core)

- [x] 6.1 Add fixture submissions (a clean pass, a fail, a null-with-note, a partial, a multi-submission
      item, an unresolvable key, a generalNotes-only) and unit tests asserting the exact file edits and
      report for each. Pass a fixed date in.
- [x] 6.2 Regression: dry-run over the existing 95 `reviewed/` submissions and assert the derived
      verdicts match what is already on file in TESTING.md (reality check). Record any mismatches.

## 7. reconcile-playtest skill (agent shell)

- [x] 7.1 Write `.claude/skills/reconcile-playtest/SKILL.md`: run `reconcile.py`, read its JSON report,
      then drive the triage 20% in-terminal — present triage inbox + caveat-flagged passes + ready-to-
      archive list; classify ambiguous verdicts; offer `openspec-propose` for new bug/feature notes;
      offer to run archives. State explicitly that the skill holds NO mechanical logic (command owns it).
- [x] 7.2 Cross-link from the `what-to-test` skill and `TESTING.md` preamble: the loop is
      what-to-test (generate) → play + submit → reconcile-playtest (close + triage). Keep
      `triage-screenshot` as the image-review step it already is.

## 8. Docs + verify

- [x] 8.1 Update the reconcile-playtest README/usage and note the optional (out-of-scope) follow-up in
      `vs-playtest-checklist` (always-capture verdict state; dedicated new-feedback field).
- [x] 8.2 `openspec validate add-playtest-reconcile-loop --strict` passes.
- [x] 8.3 Dogfood once: run `reconcile-playtest` against a real pending submission (or a copied fixture)
      and confirm the mechanical items close, triage is populated, and ready-to-archive is correct.
