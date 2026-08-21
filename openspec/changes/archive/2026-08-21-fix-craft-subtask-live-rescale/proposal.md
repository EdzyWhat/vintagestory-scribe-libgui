## Why

A Crafting Task is a parent row (the item to craft) with indented Item Tracker subtasks (its
ingredients). When the player RAISES the parent's target quantity from the editor's +/- stepper,
`SetEditorTrackerTargetQuantity` correctly rescales the ingredient subtasks' target counts in the
scratch document and calls `RebuildBody()` to reconcile the row tree. The **data is right** — but
the ingredient subtask rows do not visually redraw with their new counts. The new numbers only
appear after a redraw is forced by another means (swapping edit↔read view, etc.).

Root cause: the ingredient steppers render through `ScribeNumericField`, which is an **uncontrolled**
widget — it seeds its displayed value from `Widget.Value` in `InitState` only, and has **no
`UpdateWidget`**. The editor reconcile is keyed by `TaskId`, so each ingredient row (and its inner
numeric field) is **REUSED, not remounted**, when the parent target changes. A reused numeric field
never re-reads its (now-rescaled) `Widget.Value`, so it keeps painting the stale count. Its own doc
comment even documents the constraint: "the caller remounts it via a `ValueKey` when the
persisted/clamped value changes." The rescale path does not remount, so nothing re-seeds the field.

This is the same class of bug the project has fixed before for external completions
(`ScribeEditRowState.UpdateWidget` resyncs the optimistic `done` flag when the authoritative value
changes on an in-place reconcile). The numeric field needs the equivalent resync.

## What Changes

- **Give `ScribeNumericField` an `UpdateWidget`** that re-seeds its internal `_currentValue` and
  controller text when the incoming `Widget.Value` changes AND the field does not currently have
  focus. This makes an unfocused numeric field reflect a bound-value change delivered by an in-place
  reconcile — exactly the ingredient subtasks, which are never focused while the player steps the
  parent. The `!HasFocus` gate is load-bearing: it protects the one field the player IS editing (the
  parent's own stepper) from being stomped mid-step, mirroring the focus-preserving discipline the
  editor already uses everywhere.
- No change to the rescale math, the reconcile keying, or any Core code. The fix is confined to the
  GUI numeric-field widget's reuse behavior.

## Capabilities

### New Capabilities
_(none)_

### Modified Capabilities
- `craft-task`: adds the guarantee that ingredient subtask counts redraw live in the editor the
  moment the parent target changes, with no view swap required.

## Impact

- **`src/Mod/ScribeNumericField.cs`**: adds a focus-gated `UpdateWidget` resync. No signature change,
  no new dependency. Every other `ScribeNumericField` caller (the Settings form, the plain Tracker
  stepper) is unaffected: those either remount via `ValueKey` (so `UpdateWidget` never fires) or step
  their own focused field (so the `!HasFocus` gate skips the resync), and an unfocused field whose
  bound value genuinely changed strictly benefits from now reflecting it.
- No Core change, no codec change, no network/persistence change, no VS API surface change.
