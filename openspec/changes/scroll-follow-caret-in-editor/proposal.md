## Why

When an editor row (task or note) grows taller than the scroll viewport, every typed
character bounces the scroll position between the top and bottom of that row, making a
long row nearly impossible to edit. The cause is that the editor scrolls the focused row
*element* into view (via `Scrollable.EnsureVisible`), which for a row taller than the
viewport satisfies both of `EnsureVisible`'s guards (`itemTop < viewTop` **and**
`itemBottom > viewBottom`) and ping-pongs the offset each keystroke. The fix is to follow
the **caret**, not the whole row — the way every real text editor behaves.

## What Changes

- Editor scroll-into-view follows the **caret rect**, not the focused row element: on
  typing and on keyboard navigation, the view scrolls only when the caret would fall
  outside the viewport, and then only the minimum needed to bring it back into view
  (top-align if the caret is above the view, bottom-align if below). A caret already
  inside the viewport causes no scroll.
- A row **taller than the viewport** no longer bounces: because scrolling is driven by the
  caret's position (a single line-height rect) rather than the row's full height, the
  two-guard oscillation cannot occur.
- The caret's local geometry (top offset + line height), already computed inside the
  editable field's render object for painting, is **exposed** through the existing internal
  `IScribeEditableTextRender` seam so the dialog can compute the caret's content-space Y.
- Keyboard navigation (arrow keys, Tab / Shift+Tab, Enter-advance/retreat) rides the same
  caret-follow path, so moving the caret off-screen scrolls it back into view.

## Capabilities

### New Capabilities
<!-- none — this refines existing editor scroll behavior -->

### Modified Capabilities
- `lectern-gui-shell`: the "Editor rows are editable multi-line LibGUI widgets" requirement
  currently says the focused row "stays in view as it grows." Its scroll-into-view behavior
  is refined to be **caret-following** (scroll only to keep the caret visible, minimally),
  and to specify the taller-than-viewport case (no per-keystroke bounce) and
  keyboard-navigation caret-follow.

## Impact

- `src/Mod/ScribeMultilineField.cs` — expose the caret rect (local top + line height) on
  the render object via the internal `IScribeEditableTextRender` interface
  (`ScribeCuneiformField.cs` implements the same interface, so the cuneiform field gets the
  same method). Layout-order caveat: the caret rect is only valid after a `PerformLayout`.
- `src/Mod/ScribeDialogBase.Lifecycle.cs` — replace the `pendingEnsureVisible` →
  `Scrollable.EnsureVisible(element)` block with a caret-based ensure-visible that resolves
  the focused field's text render object, computes the caret's content-space Y, and calls
  `sharedScrollController.JumpTo`/`AnimateTo` only when the caret is outside the viewport.
- `src/Mod/ScribeDialogBase.Editor.cs` — no behavior change to the six `pendingEnsureVisible
  = true` sites; they continue to request an ensure-visible on text change and navigation,
  now serviced by the caret-based path.
- No `src/Core/` change, no new dependency, no `gui` fork (uses `ScrollController`'s public
  `Offset`/`JumpTo`/`AnimateTo`/`MaxScrollExtent`). GUI-layer only, verified in-game.
