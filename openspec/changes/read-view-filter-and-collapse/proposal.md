## Why

Read View documents grow into a flat scroll of everything ever added — active tasks, finished
ones, notes, and auto-generated Quest/Crafting subtasks all mixed together with no way to narrow
what's on screen. The Assignment Inbox already solved "narrow a long list by state" with filter
pills; Read View has no equivalent, and its subtask groups (Quest objectives, Crafting ingredient
trackers) have no way to collapse out of view once a player just wants the top-level list.

## What Changes

- Add a filter-pill row to Read View (every surface that renders it: Lectern, Notebook,
  Clockmaker's Notebook, Chalkboard, Scriptorium, Assignment Desk, Inbox — **not** Tablet), with
  five categories: All, Active, Completed, Pinned, Other.
  - "Other" covers blocks with no complete-state: `Text` and `QuestObjective` only. `Task`,
    `Tracker`, `Craft`, and `Link` all roll into Active/Completed by their checkbox state.
    "Pinned" is independent of Active/Completed — a pinned+completed row counts under both.
  - Each non-"All" pill shows a bracketed count only when greater than zero, matching the
    Assignment Inbox convention ("Active [3]").
  - A new, dedicated pill color palette, visually complementary to (but distinct from) the
    existing Assignment Inbox chip colors.
  - Each Scribe item/block instance remembers its own last-used filter pill and reopens on it —
    this is new per-instance persistence; filter selection is in-memory-only today.
  - Tablet is explicitly excluded via a new per-surface capability flag on the shared dialog
    base, since Tablet renders through the exact same `ScribeReadContent` as every other surface
    and has no separate code path to hang an exclusion off of today.
- Add a collapse/expand toggle to any Read View row that is the parent of a subtask group (a
  depth-0 row followed by a contiguous depth-1 "owned run" — today, Quest Links with
  `QuestObjective` children and Craft parents with `Tracker` children).
  - Collapsing hides the owned run under its parent; the toggle state persists per subtask group,
    per Scribe item/block instance, using the same mechanism as the filter-pill persistence.
  - Interaction with filter pills: when a filter is active, a row that doesn't match it but
    shares a subtask group with a row that does still renders — at reduced ("shadow") opacity —
    so the group's context isn't lost. This mechanic is a proposed starting point, not settled;
    `design.md` explores it plus documented alternatives, and open questions are flagged for
    another pass with the user before implementation.
  - Same Tablet exclusion as the filter pills (no subtask-group collapse UI on Tablet).

## Capabilities

### New Capabilities
- `read-view-filter-pills`: the All/Active/Completed/Pinned/Other pill row on Read View —
  categorization rules, counts, colors, and per-instance persistence of the last-used pill.
- `read-view-subtask-collapse`: the collapse/expand toggle for a subtask group's owned run on
  Read View, its per-instance persistence, and its rendering interaction with the active filter
  pill (shadow rows for non-matching group members).

### Modified Capabilities
- `scribe-dialog-base`: adds a capability flag distinguishing which dialogs render the new
  filter-pill row and subtask-collapse toggles (all current `ScribeDialogBase` subclasses except
  the Tablet dialog).
- `tablet-dialog`: adds an explicit requirement that the Tablet dialog does NOT render the
  filter-pill row or subtask-collapse toggles, to keep the Tablet's minimal read-view intentional
  rather than an oversight.

## Impact

- `src/Mod/ScribeReadContent.cs` / `ScribeDialogBase.Layout.cs` (`BuildReadContent`): render the
  new pill row and per-row collapse toggle; apply filter + shadow-row logic to the row list.
- `src/Mod/ScribeRowConstants.cs`: new pill color constants.
- `src/Mod/ScribeDialogBase.cs` / `ScribeDialogBase.ViewSwitching.cs`: new capability flag, and
  replumbing the filter-selection field from in-memory-only to persisted.
- `src/Mod/GuiDialogScribeTablet.cs`: opts out of the new flag.
- Block entities that host Read View (`BlockEntityScribeWritingStation` and subclasses,
  `BlockEntityInbox`, `BlockEntityAssignmentDesk`): new `ToTreeAttributes`/`FromTreeAttributes`
  fields for the persisted filter pill and collapse state.
- Portable Scribe items (`ItemScribeNotebook`, `ItemClockmakerNotebook`): new `ItemStack`
  attribute(s) for the same persisted state, following the `ScribeDocumentAttributes.cs` pattern.
- No change to `src/Core/` — this is purely a Mod-layer presentation/persistence feature; the
  underlying `ScribeDocument`/`ScribeBlock` model (including `Depth`) is unchanged.
