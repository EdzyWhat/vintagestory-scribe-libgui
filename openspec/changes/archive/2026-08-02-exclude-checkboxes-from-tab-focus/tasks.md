## 1. Implement the allow-list focus-traversal policy

- [x] 1.1 Add a small `FocusTraversalPolicy` subclass (e.g. `ScribeFieldOnlyTraversalPolicy` in its own
      file under `src/Mod/`) whose `GetTraversalOrder(Element root, FocusManager)` returns ONLY the
      dialog's own field focus nodes, in row order — never checkbox nodes. It SHALL source its ordered
      list from the dialog's live `editorFocusNodes` (editor mode) or the pin rows' `pinFocusNodes`
      (Pinned view), filtered to nodes that are currently mounted/live, so it tracks row add/remove/
      reorder without a second bookkeeping path. Give it a way to read the current view mode + node
      lists from the dialog (constructor delegate/reference).
- [x] 1.2 In `ScribeDialogBase` (where the dialog/`FocusManager` is set up — see `TryOpen`/init path),
      assign `FocusManager.TraversalPolicy = new ScribeFieldOnlyTraversalPolicy(...)`. Confirm the
      Settings window (separate dialog, separate `FocusManager`) is unaffected.
- [x] 1.3 Update the stale in-code comment at `src/Mod/ScribeDialogBase.Editor.cs:132-135` (which claims
      "the checkbox isn't `IFocusable`" — no longer true in 3.1.0) to reflect that the checkbox IS
      focusable in 3.1.0 and is deliberately excluded from Tab order via the custom traversal policy.

## 2. Verify commit-on-Tab still fires

- [x] 2.1 Confirm (reading the code + the field's `EditorAdvanceFrom`/`EditorRetreatFrom` path) that
      moving focus via the policy still commits the current row before the next field focuses — the
      3.1.0 `GuiBase.OnKeyDown` Tab interception must not swallow the commit. `RequestFocus` blurs the
      old node first, so the blur-commit should still run; if it does not, wire the commit into the
      focus-change path. No `src/Core/` change.

## 3. Build + notes

- [x] 3.1 `dotnet build src/Mod/Mod.csproj -c Debug` — zero new warnings/errors.
- [x] 3.2 Add a `## LibGUI` note to `VSAPI-NOTES.md`: in 3.1.0 `CheckboxState : FocusableState` (every
      checkbox auto-owns a `FocusNode`) and `GuiBase.OnKeyDown` runs `FocusManager.FocusNext/Previous`
      over `TraversalPolicy`; the `Checkbox` widget exposes no per-instance traversal knob, so excluding
      it from Tab requires a custom `FocusTraversalPolicy` (allow-list of the dialog's own field nodes).
      Also note the `reference/vslibgui/` clone is stale (pre-3.1.0) and the vendored `src/Mod/lib/Gui.dll`
      is the ground truth for focus/traversal.

## 4. Restage + verification

- [x] 4.1 `bash build/restage.sh Debug`, then fully quit + relaunch the game.
- [x] 4.2 Manual in-game (Editor view): open the Lectern editor with several task rows; press Tab
      repeatedly — focus advances one ROW per Tab, landing in each row's text field and never on a
      checkbox. Shift+Tab retreats the same way. Confirm the row's edit still commits as focus leaves it.
- [x] 4.3 Manual in-game (Pinned view): with several pinned tasks, Tab/Shift+Tab through the Pin Tab —
      focus moves field-to-field, never onto a checkbox.
- [x] 4.4 Manual in-game (regression guard): confirm Enter (new task below), Shift+Enter (line break),
      Esc (commit + close), mouse-clicking a checkbox to toggle done, and empty-row removal all still
      behave as before.
- [x] 4.5 `openspec validate exclude-checkboxes-from-tab-focus --strict` passes.
- [x] 4.6 Update `TESTING.md` with the new in-game Tab-traversal items (Editor + Pinned).
