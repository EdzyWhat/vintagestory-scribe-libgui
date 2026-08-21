# Design — fix-craft-subtask-live-rescale

## The mechanism (confirmed by reading the code)

1. Editor rows are built in `ScribeDialogBase.Layout.cs::BuildEditorContent`, which maps each
   scratch block to a fresh `ScribeEditRowData` (carrying `TargetQuantity: b.TargetQuantity`). Each
   `ScribeEditRow` is keyed `new ValueKey<Guid>(b.TaskId)` (`ScribeEditorContent.cs:436`).
2. `SetEditorTrackerTargetQuantity` (`ScribeDialogBase.Editor.cs:51`) writes the parent's new target,
   calls `ReconcileCraftFromSignature` (rescales child `TargetQuantity` in scratch — owned by another
   lane, untouched here), then `RebuildBody()`.
3. `RebuildBody()` marks the persistent body State dirty; next frame LibGUI **reconciles** the tree.
   Because rows are keyed by `TaskId`, every row Element+State is **reused** (`UpdateWidget`), not
   remounted. The child rows' `ScribeEditRowData.TargetQuantity` is now the rescaled value.
4. Inside a reused child row, `BuildItemEditorContent` (`ScribeEditorContent.cs:848`) builds a **new**
   `ScribeNumericField` instance with `initialValue: Widget.Data.TargetQuantity` (the new count). But
   the field has **no key**, sits at a stable position in the reused row, so its State is reused too.
5. `ScribeNumericFieldState` seeds `_currentValue` and its `TextEditingController` from `Widget.Value`
   in `InitState` **only**. With no `UpdateWidget`, the reused field ignores the new `Widget.Value`
   and keeps painting the old number. → the stale-count bug.

A view swap fixes it because a view switch is a `ForceRebuild` → fresh tree → the field remounts →
`InitState` re-seeds from the new value.

## The fix

Add `UpdateWidget(ScribeNumericField oldWidget)` to `ScribeNumericFieldState`:

- If `Widget.Value` differs from `oldWidget.Value` (the bound value actually changed on this
  reconcile) AND `!_focusNode.HasFocus` (this is not the field the player is editing), re-seed
  `_currentValue = Widget.Value` and rewrite the controller text to match.
- Otherwise do nothing (a focused field is mid-edit; a value that didn't change needs no work).

This mirrors the existing `ScribeEditRowState.UpdateWidget` resync of the optimistic `done` flag:
same "reconcile reuses the row, so re-seed from the authoritative value, gated on it actually
changing" shape.

## Why the focus gate is correct and sufficient

- **Parent stepper (the focused field the player is holding):** `Adjust` calls
  `_focusNode.RequestFocus()` before `onChanged` → `RebuildBody`, so during the reconcile the parent
  field `HasFocus == true` and the resync is skipped. (Its `_currentValue`/text are already the new
  value from its own `Adjust`, so even without the gate it would be a no-op — the gate is the robust
  guarantee.)
- **Ingredient children (unfocused):** `HasFocus == false`, their `Widget.Value` changed → they
  re-seed and redraw. This is the target behavior.
- **Type-and-blur on the parent:** on blur `OnFocusChanged` commits and fires `onChanged` →
  `RebuildBody`; by reconcile time the parent lost focus, but its `Widget.Value` now equals its
  committed `_currentValue`, so the change guard makes the parent a no-op while children re-seed.

## Callers that are unaffected

- **Settings form:** remounts each field via a `ValueKey` whose value moves with the bound value, so
  a changed value produces a NEW element → `InitState` (not `UpdateWidget`) → unchanged. The stepped
  field also holds focus, so the gate would skip it anyway.
- **Plain (non-Craft) Tracker stepper:** its target's only editor mutation is its own stepper; while
  the player steps it, it holds focus → gate skips. No other path changes its bound value, so
  `UpdateWidget` finds no change → no-op. Byte-identical to today.

## Non-goals / boundaries

- No change to `ReconcileCraftFromSignature`, the rescale math, `ScribeCraftRecipeProbe`,
  `ScribeItemRef`, `ScribeTrackerCounter`, or any Core code.
- No `ValueKey` remount of the ingredient steppers (that would drop focus if a child were ever
  focused, and is heavier than needed). The `UpdateWidget` resync is the minimal, focus-safe fix.
