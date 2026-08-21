## ADDED Requirements

### Requirement: Deterministic reconciliation of terminal-verdict submission items

The system SHALL provide a model-free reconciler that reads pending
`.playtest-submissions/*.json` reports and, for each item with a terminal `verdict`, records the
result deterministically. It SHALL locate the target `TESTING.md` item by the item's `fingerprint`
and the target `tasks.md` checkbox by its `taskId` (`"<change> <N.M>"`), and SHALL NOT use fuzzy
text matching. It SHALL make surgical in-place edits (never a full-file rebuild).

The mapping SHALL be: `verdict: "pass"` → a **Confirmed** verdict line under the `TESTING.md` item
AND the matching `tasks.md` box flipped to `[x]` with a dated note citing the submission timestamp
and fingerprint; `verdict: "fail"` → a **Still broken** verdict line under the `TESTING.md` item
with the box left `[ ]`; `verdict: null` → NOT written mechanically (routed to triage per the triage
requirement).

The reconciler SHALL be idempotent: it SHALL only move a checkbox `[ ]`→`[x]` on a fresh `pass`, and
SHALL never uncheck or downgrade a box or item that already carries a verdict. When an item has
reports from multiple submissions, the latest by `submittedAt` SHALL decide its state.

#### Scenario: A pass verdict propagates to both files

- **WHEN** the reconciler processes a pending submission item with `verdict: "pass"`, a valid
  `fingerprint`, and `taskId: "add-crafting-tasks 10.4"`
- **THEN** it writes a `- **Confirmed <date>** (submission <ts>): <note>` line under that
  `TESTING.md` item and flips `- [ ] 10.4` to `- [x] 10.4` in `openspec/changes/add-crafting-tasks/tasks.md`
  with a dated note citing the submission and fingerprint

#### Scenario: A fail verdict records without checking the box

- **WHEN** the reconciler processes an item with `verdict: "fail"`
- **THEN** it writes a `- **Still broken <date>:** …` line under the `TESTING.md` item and leaves the
  corresponding `tasks.md` checkbox unchecked

#### Scenario: Already-applied items are no-ops

- **WHEN** the reconciler re-runs over a submission whose items are already reflected as verdicts on
  file
- **THEN** it makes no changes to those items and reports them as already-applied rather than
  duplicating verdict lines or re-flipping boxes

#### Scenario: Unresolvable keys are reported, never guessed

- **WHEN** an item's `fingerprint` matches no `TESTING.md` item, or its `taskId` matches no `tasks.md`
  box
- **THEN** the reconciler records an error for that item, applies nothing for it, and does not
  fall back to text matching

### Requirement: Ready-to-archive detection

After propagating verdicts, the reconciler SHALL identify every in-progress OpenSpec change whose
`tasks.md` now has all checkboxes checked, and SHALL report them as ready-to-archive. An opt-in flag
SHALL run `openspec archive <name> -y` for each such change, serialized so any spec-delta collision
surfaces per change. Archiving SHALL NOT run without the opt-in flag.

#### Scenario: A change reaching 100% is surfaced

- **WHEN** the reconciler's verdict propagation causes a change's last unchecked box to become checked
- **THEN** that change name appears in the report's ready-to-archive list

#### Scenario: Archiving is opt-in

- **WHEN** the reconciler runs without the archive flag
- **THEN** it reports ready-to-archive changes but does not archive any; **WHEN** run with the flag,
  it archives each listed change one at a time

### Requirement: Triage inbox for judgment-needing feedback

The reconciler SHALL route feedback that requires human/agent judgment into a durable triage queue at
`.playtest-submissions/triage-inbox.md` rather than dropping it. This SHALL include every item with
`verdict: null` and every submission's non-empty `generalNotes`. Each triage entry SHALL record the
source submission timestamp, the fingerprint/taskId (for item entries), the verbatim text, and any
screenshot paths.

#### Scenario: A null-verdict item is queued for triage

- **WHEN** the reconciler processes an item with `verdict: null` and a descriptive note
- **THEN** it appends a triage-inbox entry with the note, fingerprint, taskId, and submission
  reference, and does not write a verdict line or check any box for it

#### Scenario: General notes are captured

- **WHEN** a submission carries non-empty `generalNotes`
- **THEN** the reconciler appends those notes to the triage inbox with the submission reference and
  any general screenshots

### Requirement: Structured report and submission lifecycle

The reconciler SHALL emit both a human-readable summary and a machine-readable JSON report with the
keys `applied`, `readyToArchive`, `triage`, and `errors`. It SHALL support a dry-run mode that
produces the report without writing any file. A submission SHALL be moved to
`.playtest-submissions/reviewed/` only when all of its items reached a terminal write AND its
`generalNotes` were captured to triage; otherwise it SHALL remain pending and be listed under
`triage` or `errors`.

#### Scenario: Dry-run writes nothing

- **WHEN** the reconciler runs in dry-run mode
- **THEN** it prints the full report but makes no edits to `TESTING.md`, any `tasks.md`, or the
  submissions directory

#### Scenario: Partially-resolved submission stays pending

- **WHEN** a submission has some terminal-verdict items and some `verdict: null` items
- **THEN** the reconciler applies the terminal items, queues the null items to triage, and leaves the
  submission in the pending directory (not moved to `reviewed/`)

### Requirement: reconcile-playtest skill drives the remaining triage in-terminal

The system SHALL provide a `reconcile-playtest` skill that runs the reconciler command, reads its
report, and handles the judgment ~20% interactively: it SHALL surface the triage inbox and any
`pass` items whose note contains caveat markers for a human glance, help classify ambiguous verdicts,
route new bug/feature feedback into an OpenSpec proposal or a new `TESTING.md` item, and offer to run
the ready-to-archive archives. The skill SHALL contain no mechanical reconciliation logic of its own —
that lives solely in the command.

#### Scenario: Skill run closes the mechanical work and presents triage

- **WHEN** the user invokes the `reconcile-playtest` skill after a playtest session with pending
  submissions
- **THEN** the mechanical pass/fail items are reconciled by the command, and the skill presents the
  triage inbox, any caveat-flagged passes, and the ready-to-archive list for the user to act on in
  the terminal

#### Scenario: New feature feedback becomes a proposal

- **WHEN** the triage inbox contains a `generalNotes` entry describing a new bug or feature
- **THEN** the skill offers to turn it into an OpenSpec proposal (via openspec-propose) or a new
  `TESTING.md` item, and clears the triage entry once handled
