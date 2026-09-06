## 1. Lang key

- [x] 1.1 Add a `scribe-assignment-filter-count` lang key to
  `src/Mod/assets/scribe/lang/en.json` in the form `"{0} ({1})"` (label, count), matching the
  existing `{0} (any variant)`-style placeholder pattern already used for other parenthetical
  suffixes in that file, and verify the JSON stays valid (`python3 -m json.tool` or equivalent).

## 2. Per-category count + chip label

- [x] 2.1 In `ScribeInboxContentState.Build` (`src/Mod/ScribeInboxContent.cs`), compute a
  row count per filter group from `Widget.Rows` using
  `ScribeAssignmentFilterGroups.StatesFor(group)` (the same filtering
  `visibleRows`/`CurrentlyVisibleAssignmentRowIds` already use, but run once per group rather
  than only the active one), and pass each group's count into `BuildFilterChip`.
- [x] 2.2 Update `BuildFilterChip` to accept the count and build its label as: plain
  `Lang.Get(labelKey)` for `ScribeAssignmentFilterGroup.All` or when count is 0; otherwise
  `Lang.Get("scribe:scribe-assignment-filter-count", Lang.Get(labelKey), count)`. Verify by
  reading the changed method that `All` and zero-count paths never reach the count-suffix
  branch.
- [x] 2.3 Build the mod (`dotnet build`) and confirm it compiles with no new warnings from this
  file.

## 3. Manual verification

- [x] 3.1 Restage Debug (`build/restage.sh Debug`) with the game client closed, then relaunch
  and open the Assignment Inbox tab with a mix of New/Accepted/terminal-state assignments;
  confirm each non-"All" chip with at least one row shows its count (e.g. "New (1)"), "All"
  shows no count, and any category with zero rows shows no parentheses.
- [x] 3.2 Repeat the same check on the Sent Assignment History tab (Assignment Desk), confirming
  counts reflect `MySentAssignments` independently of the Inbox tab's counts.
- [x] 3.3 Toggle a chip off (switch the active filter to a different group) and confirm the
  now-inactive chip's count is unchanged, since counts are independent of which chip is active.
