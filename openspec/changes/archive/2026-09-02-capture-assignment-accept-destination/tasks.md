## 1. Core: model + store versioning

- [x] 1.1 Add `public string? AcceptedIntoLabel { get; set; }` to `ScribeAssignment`
      (`src/Core/ScribeAssignment.cs`), documented alongside `AcceptedDate` (set once, at
      Accept-placement time; null when placement never happened).
- [x] 1.2 Copy it forward in `ScribeAssignment.Clone()`, alongside the other four
      transition-date fields.
- [x] 1.3 Bump `ScribeAssignmentStore.Version` 4 → 5 (`src/Core/ScribeAssignmentStore.cs`),
      with a version-history comment matching the existing v2/v3/v4 style (what v5 adds,
      why a pre-v5 blob's default is correct — not lossy).
- [x] 1.4 In `WriteRecordList`, add `WriteOptionalString(w, assignment.AcceptedIntoLabel);
      // v5+` immediately after the existing `CompletedDate` write.
- [x] 1.5 In `TryReadRecordList`, add `AcceptedIntoLabel = ReadOptionalString(r, version),`
      to the assignment object-initializer, immediately after `CompletedDate`.
      **Note:** `ReadOptionalString` previously hardcoded its `version >= 4` gate; added a
      `minVersion` parameter (default 4) so this v5+ field can gate on 5 without a
      duplicate helper.
- [x] 1.6 `tests/Core.Tests/ScribeAssignmentStoreTests.cs`: add a round-trip test that sets
      `AcceptedIntoLabel`, serializes via `SerializeStore`/`SerializeList`, and confirms it
      survives `LoadFrom`/`TryDeserializeList`.
- [x] 1.7 Add a backward-compat test: hand-construct (or reuse an existing pre-v5 fixture
      pattern from the v4 tests) a v4-shaped blob with no label bytes, confirm it still
      loads and the resulting assignment's `AcceptedIntoLabel` is `null`.

## 2. Mod: shared label-formatting helper

- [x] 2.1 Move `FormatCandidateLabel` (`ScribeDialogBase.ViewSwitching.cs:519-528`) into
      `ScribeInboxContent.cs`, next to `ScribeAcceptCandidate`, as an `internal static`
      method (e.g. `ScribeAssignmentDestinationLabel.Format(ItemStack stack)` or a static
      method directly on `ScribeAcceptCandidate` — pick whichever reads more naturally
      once it's moved, no functional difference). Keep its exact behavior: `<Type>
      "<Title>"` via the `scribe-assignment-candidate-label` lang key, falling back to the
      bare item name when the stack has no document or still carries
      `ScribeDocument.DefaultTitle`. Implemented as `ScribeAssignmentDestinationLabel.Format`.
- [x] 2.2 Update `ComputeAcceptCandidates`'s call site (`ScribeDialogBase.ViewSwitching.cs`)
      to call the moved/renamed helper — no behavior change, just the new location.
      **Note:** there was only ONE call site, not two as originally scoped — the tasks.md
      draft over-counted before the code was actually read.

## 3. Mod: capture the label at Accept-placement time

- [x] 3.1 In `TryPlaceAcceptedAssignment` (`src/Mod/ScribeModSystem.Assignment.cs`), compute
      `record.Assignment!.AcceptedIntoLabel = ScribeAssignmentDestinationLabel.Format(slot.Itemstack!);`
      — set directly on the store's canonical `record.Assignment` (the same object
      `StampTransitionDate` already mutated earlier in the method), so it flows through the
      existing sync path unconditionally.
      **Deviation from this task's original wording:** placed AFTER the capacity check, not
      before it as originally drafted — design.md's own Risk/Mitigation explicitly requires
      an "Accepted but unplaced" assignment (EITHER early-return branch, including a
      no-capacity target) to stay label-less; setting it before the capacity check would
      have violated that and contradicted task 3.2 below. Followed design.md over the
      literal tasks.md wording.
- [x] 3.2 Confirm (by inspection — no separate line needed) that the label is NOT set when
      the method returns early on an ineligible/no-capacity target (the two existing
      early-`return` branches above the resolution point) — per design.md Decision/Risk,
      "Accepted but unplaced" stays label-less. Confirmed: both early returns (unresolvable
      target; no-capacity target) happen before the label-capture line.
- [x] 3.3 Confirm `placed.Assignment!.Clone()` (unchanged) now carries `AcceptedIntoLabel`
      into the placed copy for free, now that `Clone()` copies it (task 1.2) — no code
      change needed here, just verify by inspection. Confirmed.

## 4. Mod: display on the Inbox row

- [x] 4.1 Add `AcceptedIntoLabel` to `ScribeInboxRowData` (`ScribeInboxContent.cs`).
- [x] 4.2 Thread `b.Assignment.AcceptedIntoLabel` into the `ScribeInboxRowData` constructed
      in `BuildInboxContent` (`ScribeDialogBase.ViewSwitching.cs`). **Not** threaded into
      `ComputeSentAssignmentRows`/Sent Assignment History — design.md's stated scope is
      Inbox-only for this change (Assigner-side mirroring flagged as a future follow-up).
- [x] 4.3 In `ScribeInboxContent.cs`'s expanded-row rendering, branch on
      `data.AcceptedIntoLabel`: when non-null, render via the new
      `scribe-assignment-accepted-into-on` key (label, date); when null, keep the existing
      `scribe-assignment-accepted-on` (date-only) rendering unchanged.
- [x] 4.4 Add `"scribe-assignment-accepted-into-on": "Accepted into {0} — {1}"` to
      `src/Mod/assets/scribe/lang/en.json`, alongside the existing
      `scribe-assignment-accepted-on` key.

## 5. Tests

- [x] 5.1 Manual test: as the Assigner, send a task; as the Assignee, Accept it into a
  - Confirmed 2026-09-01: TESTING.md `0000007b` "Works. YOu get visibility on the Inbox, but not in the Sent Assignment History, which is the correct behavior!" (submission 2026-09-01T18-15-36)
      Notebook you've titled (e.g. "Book of Nick") — expand the row in your Inbox, confirm
      it reads "Accepted into Notebook \"Book of Nick\" — <date>".
- [x] 5.2 Manual test: Accept a task into a Notebook that still has its default
  - Confirmed 2026-09-01: TESTING.md `0000007c` "(no note)" (submission 2026-09-01T18-15-36)
      (never-renamed) title — confirm the line falls back to the bare item name (e.g.
      "Accepted into Notebook — <date>"), not the literal default title text.
- [x] 5.3 Manual test: confirm an assignment Accepted before this update (pre-existing save
  - Confirmed 2026-09-01: TESTING.md `0000007d` "(no note)" (submission 2026-09-01T18-15-36)
      data, if available) still renders its Accepted line as date-only, with no error or
      placeholder text.
- [x] 5.4 Manual test: confirm the Read/Editor-row tooltip and Pin Tab still show
  - Confirmed 2026-09-01: TESTING.md `0000007e` "(no note)" (submission 2026-09-01T18-15-36)
      date-only for an accepted assignment (unchanged scope — no destination text there).
- [x] 5.5 Restage both client and server builds together before testing (store version
      bump — a mismatched pair reading/writing different versions could misparse).
      - Confirmed 2026-09-01: playtest submission 2026-09-01T18-15-36 exercised the
        restaged build (all 5.1-5.4 verdicts passed against it).
