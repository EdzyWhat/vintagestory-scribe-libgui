## 1. Add the opt-in seam + shared dispatch on the dialog base

- [x] 1.1 Added `protected virtual bool EditorRowsOpenLinks => false;` in `ScribeDialogBase.Layout.cs`
      beside the read-view seams, with a doc-comment noting only the tablet (no read view) turns it on.
- [x] 1.2 Extracted the read view's inline `onOpenLink` dispatch into a shared private
      `OpenRowLink(Guid taskId)` helper on `ScribeDialogBase`; `BuildReadContent()` now passes
      `onOpenLink: OpenRowLink` and `BuildEditorContent()` reuses the same helper.
- [x] 1.3 Added the Craft branch: `OpenRowLink` resolves Link → `LinkTarget`, Tracker AND Craft →
      `TargetItemCode` (combined `IsTracker || IsCraft`), mirroring `PinTab.cs` `OnPinOpenLink`.
- [x] 1.4 `BuildEditorContent()` passes `onOpenLink: EditorRowsOpenLinks ? OpenRowLink : null` — non-tablet
      editors get null and render unchanged.

## 2. Thread `onOpenLink` through the editor row and wrap the name label

- [x] 2.1 Added a nullable `System.Action<Guid>? onOpenLink = null` param to `ScribeEditorContent`'s ctor
      (property `OnOpenLink`) and carried it onto `ScribeEditRow` (own `onOpenLink` param + `OnOpenLink`
      property). Absent = null everywhere but the tablet.
- [x] 2.2 In `ScribeEditRowState.BuildItemEditorContent`, when `Widget.OnOpenLink is { } openLink` the name
      label is wrapped in `new GestureDetector(onPress: e => { e.Handled = true; openLink(Widget.Data.TaskId); }, …)`
      — the same shape as `ScribeReadContent.cs:362` — and rendered in the link accent (`style.LinkColor ??
      colors.Primary`) for read-view parity. When null it stays the bare `OnSurface` label. This method is only
      reached by item-kind rows, so plain Task/Note editable text is never wrapped.
- [x] 2.3 The gesture wraps only the `ScribeItemLabel` (in an `Expanded`); the `ScribeNumericField` stepper is
      a separate sibling added earlier in `rowChildren`, so the number remains an independent hit region.
- [x] 2.4 The wrap is font-path-agnostic: `BuildItemEditorContent` has no cuneiform branch — `ScribeItemLabel.Build`
      renders through the row `style`'s font, and the GestureDetector wraps that label widget regardless, so a
      cuneiform tablet's name is clickable.

## 3. Opt the tablet in

- [x] 3.1 In `GuiDialogScribeTablet.cs`, added `protected override bool EditorRowsOpenLinks => true;` with a
      doc-comment noting the tablet has no read view so its always-edit rows must surface link activation directly.

## 4. Build + restage + verify

- [x] 4.1 `dotnet build src/Mod/Mod.csproj` — 0 errors, 0 warnings.
- [x] 4.2 `bash build/restage.sh Debug` — done as one combined restage after all four changes merged to main
      (client confirmed not running); 137 files staged, build 0/0.
- [ ] 4.3 In-game gate: on a WET tablet, click a Link task's name → its Handbook page opens and the
      Link is NOT completed. Click a Tracker and a Craft task's item NAME → the item's Handbook page
      opens. Click the NUMBER on a Tracker/Craft row → the numeric field edits the target (no page
      opens). Confirm plain Task/Note rows still edit text on click (no link hijack).
- [ ] 4.4 In-game gate: repeat 4.3 with cuneiform ON — the name is still clickable and the number
      still edits.
- [ ] 4.5 In-game gate: on a Lectern/Notebook EDITOR view, confirm item-row names are NOT clickable
      (unchanged); their READ view still opens links, and a Craft parent's name in the read view now
      opens its output item's page (was a no-op before).
