## Why

In Scribe Settings, clicking a numeric field (e.g. "Window Text Size") and pressing the up/down arrow
steps the value on the FIRST press only. Subsequent presses do nothing to the value — focus has silently
switched to the last-touched editor row in an open Lectern/Notebook/Tablet document, and the arrow keys
now drive that row's caret instead. This is a focus-retention defect that makes arrow-key stepping (a
shipped settings behavior) unreliable the moment a document editor is also open.

The latent focus leak was always present, but was INVISIBLE until `arrow-key-line-caret-nav` (commit
c767ac3, 2026-08-01) made Up/Down live keys inside `ScribeMultilineField`. Previously those keys were
inert in the editor field, so a leaked-to row swallowed the arrow harmlessly. Now the same leak has a
visible symptom: the wrong widget consumes the key.

## What Changes

- After a numeric field is stepped with an arrow key and the settings form's write-through rebuild runs,
  focus SHALL RELIABLY remain on that numeric field across consecutive presses, so up/down steps the value
  on EVERY press (3+ in a row), not just the first.
- The fix targets the re-focus-after-rebuild handshake between `ScribeSettingsContent.NumericField` and
  `ScribeNumericFocusRegistry` (the armed one-shot / auto-focus / `ValueKey` remount). It does NOT change
  `ScribeNumericField.OnFieldKeyDown`, which already correctly marks Up/Down `Handled`.
- No change to the `arrow-key-line-caret-nav` behavior: when an editor row IS genuinely focused, Up/Down
  still move the caret between visual lines exactly as before.
- No change to the +/- step BUTTONS or the numeric field's blur-commit / clamp behavior.

## Capabilities

### New Capabilities
<!-- none — this fixes an existing settings numeric-field behavior -->

### Modified Capabilities
- `settings-tab`: the numeric-entry requirement is strengthened so arrow-key stepping keeps focus on the
  numeric field across consecutive presses (rather than leaking focus to a document editor row after the
  write-through rebuild).

## Impact

- Affected code: `src/Mod/ScribeNumericField.cs` (the `ScribeNumericFocusRegistry` arm/consume machinery
  and the field's `AutoFocus`/`FocusNode` re-request) and `src/Mod/ScribeSettingsContent.cs` (the
  `NumericField` handshake: `onStepped` → `ArmAutoFocus`, `autoFocus` ← `ShouldFocus`, `ValueKey` remount).
  Possibly `src/Mod/ScribeMultilineField.cs` if the fix must have the editor row decline focus it was not
  given.
- No `src/Core/` impact (no document/model/codec change). No network or persistence change. No new
  dependency, no new packet. Purely a client-side focus-retention fix.
- Keyboard-only surface: no visual/layout change; no effect on +/- buttons, blur-commit/clamp, or the
  editor's genuine caret navigation.
