## Context

The server discards a timer when it is cleared, and the pushed state carries only label, mode and remaining seconds, so the original duration is not recoverable after the fact. The form's values (`_pendingLabel`, hours, minutes, seconds, mode) live on the Notebook dialog instance and are lost when it closes.

## Goals / Non-Goals

**Goals:**
- Prefill the form from the last started timer for the session, regardless of how that timer ended.
- A form-level clear button.

**Non-Goals:**
- Persisting across restarts, or any HUD button.
- Changing the server, network messages or savegame format.

## Decisions

- **Capture when the timer is started, not when it is cleared.** The form already holds exactly the values that were sent, and every end path (dismiss, auto-clear, stop, relog of the dialog) then needs no hook. Capturing at clear would need the duration, which the client no longer has.
- **Hold the values on `ScribeModSystem`**, which outlives any one dialog, as a small Core record `TimerFormValues` (label, hours, minutes, seconds, mode). Plain fields, no persistence, so it resets with the process.
- **Seed the form in the dialog constructor**, falling back to today's defaults (empty, zeros, preferred mode) when nothing has been started.
- **Clear button resets only the dialog's form fields**, not the remembered values, so it cannot lose what the next open would prefill. Alternative considered: also wiping the remembered values; rejected because an accidental press would then lose the data the player wanted back.
- **Core owns the reset rule** (`Blank(preferredMode)`) so it is unit-testable without the game.

## Risks / Trade-offs

- A timer started before a mod update or from another client leaves nothing to prefill until the next start; acceptable for a session-only feature.
- The form shows the remembered values even if the player edited then abandoned the form, since only started timers are remembered. This matches "previous timer's values".
