## Why

Link, Tracker, and Craft task rows are meant to be clickable — the row's item name opens the
referenced Handbook page. On the Lectern/Notebook/Scriptorium this works because those surfaces
have a distinct **read** view whose rows wrap the name in a click-to-open gesture. The **Tablet**
has no read/edit split: an editable (wet) tablet renders the *editor* rows, and editor rows never
wired up the click-to-open affordance. So on a tablet a Link task can't be opened at all, and a
Tracker/Craft name is dead. This already violates the shipped `link-task` requirement that "a Link
task behaves as a hyperlink from every surface (Scriptorium, Lectern, Notebook, Tablet)".

## What Changes

- On the **Tablet's always-edit view only**, make item-row names click-to-open the Handbook:
  - **Link** rows — the whole name label opens the Link's Handbook page (Link text is never
    editable, so there is no conflicting edit target).
  - **Tracker / Craft** rows — the **name label** opens the target item's Handbook page, while the
    existing inline **numeric +/- field** (which is how a tablet already edits the target quantity)
    keeps handling clicks on the number. Two hit regions that already sit side by side in the
    editor row; only the name label gains a gesture.
- Scope this strictly to the tablet: add a `ScribeDialogBase` seam (default off) that the tablet
  overrides on, and pass the editor path an `onOpenLink` dispatch only when it is on. The
  Lectern/Notebook editor views stay non-clickable (they have their own read view for that).
- **Companion bug fix:** the read-view `onOpenLink` dispatch handles `IsLink` and `IsTracker` but
  not `IsCraft`, so a Craft parent's name renders as a link yet clicking it silently no-ops. Add
  the `IsCraft → TargetItemCode` branch (mirroring the Pin-tab dispatch, which already handles
  Craft) so Craft rows open their output item's page — on the tablet and on the existing read
  views alike.

## Capabilities

### New Capabilities
<!-- None: reuses the existing Handbook-open plumbing (ScribeItemRef.OpenHandbookPage) and the
     read view's onOpenLink dispatch; no new capability introduced. -->

### Modified Capabilities
- `tablet-dialog`: Add a requirement that the tablet's always-edit central region activates
  item-row links — a Link label opens its Handbook page; a Tracker/Craft name label opens the
  target item's Handbook page while the numeric target-quantity field continues to handle the
  number. Scoped to the tablet; Lectern/Notebook editors are unaffected. This makes the tablet
  finally conform to the existing `link-task` cross-surface hyperlink requirement.

## Impact

- `src/Mod/ScribeEditorContent.cs` — add an optional `onOpenLink` (a nullable
  `System.Action<Guid>`) to `ScribeEditorContent`'s ctor and `ScribeEditRow`; in
  `BuildItemEditorContent` wrap the `ScribeItemLabel` in the same `GestureDetector →
  onOpenLink(TaskId)` the read row uses (`ScribeReadContent.cs:362`). For Tracker/Craft, wrap
  only the name label, leaving the inline numeric field's hit region intact.
- `src/Mod/ScribeDialogBase.Layout.cs` — add a `protected virtual bool EditorRowsOpenLinks =>
  false;` seam; in `BuildEditorContent()` pass the same `onOpenLink` dispatch used by
  `BuildReadContent()` (lines 682-687) only when the seam is on. Add the `IsCraft → TargetItemCode`
  branch to BOTH dispatches.
- `src/Mod/GuiDialogScribeTablet.cs` — override `EditorRowsOpenLinks => true`.
- No `src/Core/` changes; no new dependencies; no persistence/format change. The Handbook-open
  plumbing (`ScribeItemRef.OpenHandbookPage`, the `"handbook"` link protocol) is reused unchanged.
