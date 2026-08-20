## 1. Add the opt-in seam + shared dispatch on the dialog base

- [ ] 1.1 In `src/Mod/ScribeDialogBase.Layout.cs`, add `protected virtual bool EditorRowsOpenLinks
      => false;` beside the other nav/theme seams, with a doc-comment noting it opts a surface's
      EDITOR rows into read-style click-to-open (only the tablet, which has no read view, turns it
      on).
- [ ] 1.2 Extract the read view's `onOpenLink` dispatch (currently inline at `BuildReadContent()`
      lines ~682-687) into a single shared lambda/helper used by both `BuildReadContent()` and
      `BuildEditorContent()`, so Link/Tracker/Craft resolve identically on every path.
- [ ] 1.3 Add the missing Craft branch to that dispatch: `else if (block?.IsCraft == true)
      ScribeItemRef.OpenHandbookPage(capi, block.TargetItemCode);` (mirrors
      `ScribeDialogBase.PinTab.cs` `OnPinOpenLink`). Confirm Link → `LinkTarget`, Tracker/Craft →
      `TargetItemCode`.
- [ ] 1.4 In `BuildEditorContent()`, pass the shared dispatch to `ScribeEditorContent` as its
      `onOpenLink` ONLY when `EditorRowsOpenLinks` is true; otherwise pass `null` so every existing
      (non-tablet) editor renders unchanged.

## 2. Thread `onOpenLink` through the editor row and wrap the name label

- [ ] 2.1 In `src/Mod/ScribeEditorContent.cs`, add a nullable `System.Action<Guid>? onOpenLink`
      parameter to `ScribeEditorContent`'s ctor and carry it onto `ScribeEditRow` (qualify
      `System.Action` — the VS API also defines `Action`). Default/absent = null.
- [ ] 2.2 In `ScribeEditRowState.BuildItemEditorContent` (~lines 862-863), when `onOpenLink != null`
      AND the row is an item kind (Link/Tracker/Craft), wrap the `ScribeItemLabel` in a
      `GestureDetector(onPress: e => { e.Handled = true; Widget.OnOpenLink(Widget.Data.TaskId); })`
      — the same shape the read row uses (`ScribeReadContent.cs:362`). When `onOpenLink == null` or
      the row is a plain Task/Note, keep the current bare `Expanded` (no gesture) so editable text
      rows are untouched.
- [ ] 2.3 Confirm the gesture wraps ONLY the name label, never the sibling `ScribeNumericField`
      (the Tracker/Craft `+/-` stepper), so the number stays an independent hit region that edits
      the target quantity.
- [ ] 2.4 Confirm the wrap holds on the cuneiform render path too (the label widget gets the
      gesture regardless of which font path builds it), so cuneiform tablets are clickable.

## 3. Opt the tablet in

- [ ] 3.1 In `src/Mod/GuiDialogScribeTablet.cs`, override `protected override bool
      EditorRowsOpenLinks => true;` with a doc-comment noting the tablet has no read view, so its
      always-edit rows must surface link activation directly.

## 4. Build + restage + verify

- [ ] 4.1 `dotnet build src/Mod/Mod.csproj` — 0 errors, 0 warnings.
- [ ] 4.2 `bash build/restage.sh Debug` (only while the client is NOT running).
- [ ] 4.3 In-game gate: on a WET tablet, click a Link task's name → its Handbook page opens and the
      Link is NOT completed. Click a Tracker and a Craft task's item NAME → the item's Handbook page
      opens. Click the NUMBER on a Tracker/Craft row → the numeric field edits the target (no page
      opens). Confirm plain Task/Note rows still edit text on click (no link hijack).
- [ ] 4.4 In-game gate: repeat 4.3 with cuneiform ON — the name is still clickable and the number
      still edits.
- [ ] 4.5 In-game gate: on a Lectern/Notebook EDITOR view, confirm item-row names are NOT clickable
      (unchanged); their READ view still opens links, and a Craft parent's name in the read view now
      opens its output item's page (was a no-op before).
