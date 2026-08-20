## 1. Fix — focus-gated resync in ScribeNumericField

- [x] 1.1 Add `public override void UpdateWidget(ScribeNumericField oldWidget)` to
      `ScribeNumericFieldState` in `src/Mod/ScribeNumericField.cs`. Call `base.UpdateWidget(oldWidget)`,
      then: when `Math.Abs(Widget.Value - oldWidget.Value) > 0.0001f` AND `!_focusNode.HasFocus`,
      set `_currentValue = Widget.Value` and rewrite the controller text
      (`_controller.Value = new TextEditingValue(text, TextSelection.Collapsed(text.Length))`) to the
      new value. (Done: focus-gated re-seed added, mirrors ScribeEditRowState.UpdateWidget's done resync.)
- [x] 1.2 Document WHY the `!HasFocus` gate is load-bearing (protects the parent stepper the player is
      actively editing) and that this only affects unfocused fields whose bound value changed on an
      in-place reconcile. (Done: XML doc comment on UpdateWidget explains the ingredient-subtask live
      redraw and the focus gate.)

## 2. Build

- [x] 2.1 `dotnet build src/Mod/Mod.csproj -c Debug` → 0 warnings / 0 errors.
      (Done: vendored LibGUI DLLs copied into src/Mod/lib/ first so the `gui` ref resolves; build clean.)

## 3. Notes

- [x] 3.1 Append a LibGUI note to `VSAPI-NOTES.md`: an uncontrolled StatefulWidget seeded only in
      `InitState` will NOT reflect a bound-value change delivered by an in-place reconcile (reuse via
      `UpdateWidget`) — it needs an explicit focus-gated re-seed in `UpdateWidget`, or a `ValueKey`
      remount. (Done.)

## 4. In-game verification (GATE — main session restages + tests)

- [ ] 4.1 Open a Crafting Task in the editor. Raise the parent's target with the +/- stepper. Confirm
      the ingredient subtask counts redraw with their new values IMMEDIATELY, with no view swap.
- [ ] 4.2 Confirm the parent stepper keeps focus and keeps stepping smoothly (no caret loss / no snap)
      across repeated + presses.
- [ ] 4.3 Confirm the plain (non-Craft) Tracker stepper and the Settings numeric fields behave exactly
      as before (no regression).
