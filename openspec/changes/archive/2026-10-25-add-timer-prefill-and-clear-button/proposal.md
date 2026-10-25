## Why

After a timer ends or is stopped, the Clockmaker's Notebook Timer tab comes back blank, so restarting or continuing a similar timer means retyping the name and duration. Players also have no quick way to wipe the form once it holds old values.

## What Changes

- The Timer tab's new-timer form prefills with the most recent timer's name, hours, minutes, seconds and mode whenever no timer is active, however the previous timer ended (fired and dismissed, auto-cleared, or stopped while running).
- The prefill is held client-side for the session only; it is not saved in player settings and resets when the game restarts.
- A circle-X clear button on the new-timer form empties the name and duration and returns the mode to the player's preferred mode.
- The HUD is unchanged: the fired row stays tap-to-clear and gets no button, since reusing the values means opening the Notebook anyway.

## Capabilities

### New Capabilities

### Modified Capabilities
- `timer-lifecycle`: the idle Timer tab form prefills from the last started timer, and offers a clear button for the form fields.

## Impact

- `src/Core`: a small record for the form values.
- `src/Mod`: `ScribeModSystem` holds the last-started values; `GuiDialogClockmakerNotebook` seeds the form from them and renders the clear button.
- No network, savegame or settings-schema changes.
