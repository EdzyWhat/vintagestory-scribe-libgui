## Why

Two reports on the timer. First: after a timer started in the Clockmaker's Notebook fires, reopening the Notebook
and pressing Stop Timer clears the HUD and the alarm, but the Timer tab keeps showing the blinking 00:00 and the
Stop button, and further clicks change nothing. Second (ModDB comment): after a timer has ended or been cancelled,
starting another one does not show it on the HUD until any HUD setting is changed.

## What Changes

- The Timer tab always reflects the live timer when it is opened, so a status change after open (Fired or Running
  to Idle) rebuilds the page back to the new-timer form.
- The HUD is placed on screen every time it opens for a timer, not only after a settings change.
- Add tests for the timer-tab status transitions so the stale case cannot return.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `timer-lifecycle`: the Timer tab shows the cleared state after Stop Timer regardless of when the Notebook was
  opened, and a new timer appears on the HUD after a previous one ended or was cancelled.

## Impact

`src/Mod/GuiDialogClockmakerNotebook.cs` (the status the tab tracks) and `src/Mod/HudScribePins.cs` /
`HudScribePins.Anchor.cs` (anchor cache). Needs a playtest of both repros. The HUD half is a hypothesis to confirm
in game before it is relied on.
