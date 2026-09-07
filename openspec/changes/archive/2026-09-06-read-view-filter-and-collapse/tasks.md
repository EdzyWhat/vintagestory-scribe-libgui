## 1. Capability flag + Tablet exclusion

- [x] 1.1 Add a `SupportsFilterPills` (naming per design.md) virtual property to `ScribeDialogBase`
      defaulting to `true`, and verify every existing subclass compiles unchanged (no override
      needed except Tablet).
- [x] 1.2 Override the flag to `false` on `GuiDialogScribeTablet`, and verify (by reading
      `BuildReadContent`) that the flag alone is sufficient to skip both the pill row and any
      collapse toggles later added in this change.

## 2. Filter category matching logic

- [x] 2.1 Add a Mod-layer helper (e.g. a `ReadViewFilterCategory` enum: All, Active, Completed,
      Pinned, Other) and a pure function mapping a `ScribeBlock` + its pin state to the set of
      categories it matches, per `read-view-filter-pills`'s block-kind-mapping requirement.
      Verify with a focused set of Mod-layer/Integration test cases covering all six
      `ScribeBlockKind` values and the pinned/unpinned cross-product.
- [x] 2.2 Verify the Pinned category is evaluated independently (a pinned+completed row matches
      both) via a test case exercising that combination.

## 3. Filter-pill UI

- [x] 3.1 Add the five filter-pill color constants to `ScribeRowConstants.cs`, aliasing
      `NavActiveSettings` (All), `NavActiveRead` (Completed), `NavActivePinned` (Active),
      `NavActiveTranscribe` (Pinned), and `NavActiveGuestbook` (Other), per design.md.
- [x] 3.2 Build the filter-pill row widget (reusing the Assignment Inbox's `BuildFilterChip`
  - Confirmed 2026-09-06: TESTING.md `000000a0` "(no note)" (submission 2026-09-06T16-48-19)
      shape: radio-select, bracketed count only when > 0) and wire it into
      `ScribeDialogBase.Layout.cs`'s `BuildReadContent`, gated on the capability flag from §1.
      Verify in-game: opening a supporting surface shows all five pills with one active.
- [x] 3.3 Wire pill selection to re-filter the rendered row list using §2's matching function.
  - Confirmed 2026-09-06: TESTING.md `000000a0` "(no note)" (submission 2026-09-06T16-48-19)
      Verify in-game: selecting each pill narrows the list to the expected rows.

## 4. Filter-pill persistence

- [x] 4.1 Add a persisted filter-pill field to `BlockEntityScribeWritingStation` (and confirm it's
      inherited by Lectern/Chalkboard/Scriptorium) via `ToTreeAttributes`/`FromTreeAttributes`
      (int/byte cast of the enum, mirroring `accessMode`), plus the same field on
      `BlockEntityInbox` and `BlockEntityAssignmentDesk`. Verify with an Integration.Tests
      persistence-round-trip case (save/reload preserves the selected pill).
- [x] 4.2 Add the same persisted state to `ItemScribeNotebook`/`ItemClockmakerNotebook` via an
      `ItemStack` attribute helper following `ScribeDocumentAttributes.cs`'s pattern. Verify with
      an Integration.Tests case round-tripping the attribute through serialization.
- [x] 4.3 On dialog open, read the persisted pill (defaulting to All when absent) instead of the
  - Confirmed 2026-09-06: TESTING.md `000000a1` "(no note)" (submission 2026-09-06T16-48-19)
      current in-memory-only `assignmentFilterGroup`-style field; on pill change, write it back
      immediately. Verify in-game: reopening a Notebook/Lectern/Chalkboard resumes on its
      last-selected pill; two different instances remember independently.

## 5. Subtask-group collapse toggle

- [x] 5.1 Add a helper that identifies each depth-0 row's contiguous depth-1 "owned run" (per
      `task-subtasks`) from the row list already being rendered. Verify with a test covering a
      Quest Link + QuestObjective run, a Craft parent + Tracker run, and a plain Task with no run.
- [x] 5.2 Add the collapse-toggle control to any Read View row with a non-empty owned run, gated on
  - Confirmed 2026-09-06: TESTING.md `000000a2` "(no note)" (submission 2026-09-06T16-48-19)
      the same capability flag from §1. Verify in-game: the toggle appears only on Quest/Craft
      parents, never on plain rows, and never on a Tablet.
- [x] 5.3 Wire the toggle to hide/show its owned run's rows, preserving order. Verify in-game:
  - Confirmed 2026-09-06: TESTING.md `000000a2` "(no note)" (submission 2026-09-06T16-48-19)
      collapsing hides the children, expanding restores them in the same order.

## 6. Shadow-row filtering interaction

- [x] 6.1 Implement group-level filter visibility: a group renders if any member (parent or child)
      matches the active pill; otherwise the whole group is hidden. Verify with a test case per
      `read-view-subtask-collapse`'s "group with one matching child" and "group with no matching
      members" scenarios.
- [x] 6.2 Implement per-row shadow opacity (~50%) for a visible group's non-matching members,
  - Confirmed 2026-09-06: TESTING.md `000000a3` "(no note)" (submission 2026-09-06T16-48-19)
      independent of collapse state, with shadow rows remaining fully interactive. Verify in-game:
      checking off, pinning, and editing a shadow row all still work.
- [x] 6.3 Ensure a collapsed group's parent still surfaces (at its own normal-or-shadow opacity)
  - Confirmed 2026-09-06: TESTING.md `000000a4` "(no note)" (submission 2026-09-06T16-48-19)
      whenever any hidden child matches the active filter. Verify in-game with a collapsed group
      containing exactly one matching child.
- [x] 6.4 Ensure the All pill applies no shadow opacity to any row. Verify in-game.
  - Confirmed 2026-09-06: TESTING.md `000000a3` "(no note)" (submission 2026-09-06T16-48-19)
- [x] 6.5 Ensure a pill's bracketed count (from §3) only counts individually-matching rows, never
      shadow rows. Verify with a test case matching `read-view-subtask-collapse`'s
      "shadow sibling does not inflate the count" scenario.

## 7. Collapse-state persistence

- [x] 7.1 Add persisted collapse state (keyed by each group's parent block's stable id, not list
      position) to the same block-entity and `ItemStack` locations as §4, defaulting to fully
      expanded when absent. Verify with an Integration.Tests persistence-round-trip case.
- [x] 7.2 Verify in-game: collapsing a group, closing, and reopening the document resumes that
  - Confirmed 2026-09-06: TESTING.md `000000a5` "(no note)" (submission 2026-09-06T16-48-19)
      group collapsed while other groups stay expanded; reordering blocks so a collapsed group's
      parent moves position still shows it collapsed at its new position.

## 8. Cross-surface verification

- [x] 8.1 Manually verify both features end-to-end on every supporting surface (Lectern, Notebook,
  - Confirmed 2026-09-06: TESTING.md `000000a6` "(no note)" (submission 2026-09-06T16-48-19)
      Clockmaker's Notebook, Chalkboard, Scriptorium, Assignment Desk, Inbox) and confirm the
      Tablet shows neither feature, per `what-to-test`.
- [x] 8.2 Run the full `Core.Tests` and `Integration.Tests` suites and confirm all pass.
