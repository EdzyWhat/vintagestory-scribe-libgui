## Why

Four files are 1.0k to 1.9k lines and mix unrelated classes (`HudScribePins.cs` holds the dialog, row widgets,
fade text and a gear button; `GuiDialogClockmakerNotebook.cs` holds the dialog plus a 400-line gear renderer).
Splitting them by responsibility makes each file one idea, with no logic change.

## What Changes

- `GuiDialogClockmakerNotebook.cs`: move `ScribeGearworks` and the timer widgets (`ScribeCountdownText`,
  `ScribeTimerModeRow`, `ScribeBlinkText`, `ScribeTimerIcon`) to their own files.
- `HudScribePins.cs`: move `HudPinRow`/`HudPinsContent` to `HudPinsContent.cs`, `ScribeFadeText` and
  `ScribeHudGearButton` to a widgets file, and split the anchor, tracker-count and storm-corruption regions into
  `HudScribePins.*.cs` partials.
- `ScribeEditorContent.cs`: move `ScribeEditRow` and its state to `ScribeEditRow.cs`.
- `ScribeMultilineField.cs`: move the render objects to their own file and split `ScribeMultilineFieldState`
  into `.Keys`, `.Pointer` and `.Reveal` partials.
- No member is renamed, reordered within its logic, or edited. Same namespace, same accessibility.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
None. Pure file moves (`skip_specs: true`).

## Impact

Four source files shrink; about 3,000 lines relocate to roughly 12 new files. Merge conflicts are possible with
any in-flight edit to those files (the three open changes do not touch them).
