## Why

`refine-row-affordance-visuals` landed the Notion-style row affordances and playtested green, but
the follow-up report asked for a second, more opinionated pass: pin and delete read as two loose
buttons rather than one grouped control; the drag grip still wears button chrome; a click on
pin/delete gives no visual acknowledgement (so the tester couldn't even confirm the click routed);
the buttons shrink too small at the low end of the text-size range and aren't square; and the
ruling still carries internal padding. Separately, "pinned" is a fully-modeled, persisted Core
concept whose editor toggle is still a logging stub and whose only visual cue is hidden until you
hover the row — so a pinned task looks identical to an unpinned one at rest.

## What Changes

- Group pin + delete into a single bordered button group with a thin ink divider between them,
  instead of two separately-outlined buttons; hit-testing stays per-icon so each still routes to its
  own action.
- Make the drag grip a bare SVG (no parchment fill, no outline) sized to at least the checkbox
  height, via a no-chrome draw path on the shared button — pin/delete keep their chrome.
- Add a transient pressed/depressed visual state (a low-opacity light overlay clipped to the button)
  shown while a pin/delete button is held, giving click feedback and making the stub-fire observable.
- Make pin and delete square (equal width AND height) and add a minimum on-screen button size so
  they stay legible at the smallest text-size setting.
- Remove the ruling's internal top/bottom padding so the drawn line hugs the row content, while
  preserving the symmetric focused-input margin; the padding stays a tunable knob.
- Wire the editor pin toggle to real persistence (mirroring the done-toggle path) and add a
  server-authoritative pin-sync message so pinning works from the read view and across clients.
- Add an always-visible indicator that a task is pinned (visible without hovering, in both views),
  implemented as two config-selectable variants so the final look can be chosen in-game.

## Capabilities

### New Capabilities
<!-- none — all behavior extends the existing lectern GUI capability -->

### Modified Capabilities
- `lectern-gui-shell`: the pin/delete affordances become a grouped, divided, square control with a
  pressed-state and a minimum size; the drag handle renders chrome-less; the ruling loses its
  internal padding; the pin-toggle affordance now actually persists and syncs (amends "Task rows
  expose a pin-toggle affordance" — the stub becomes real) and a pinned task carries an
  always-visible indicator independent of hover (amends "Row icons are hover-conditional").

## Impact

- Code (Mod, GUI layer): `src/Mod/ScribeBlockRowCell.cs` (`ScribeHoverIconButton` — no-chrome draw
  flag, pressed-state overlay + mouse-down/up, always-visible-when-pinned exemption), `src/Mod/
  RowTextLayout.cs` (grouped pin/delete geometry, divider, square sizing, min-size floor), `src/Mod/
  ScribeRowElement.cs` (ruling padding, optional row-level pinned accent), `src/Mod/
  GuiDialogScribeLectern.cs` (real `OnEditViewTogglePin`, grip bounds, read-view pin send),
  `src/Mod/ScribeClientConfig.cs` (pressed-overlay, min-button-size, pinned-indicator knobs).
- Code (Mod, sync): new `ScribeTogglePinMessage` mirroring `ScribeToggleTaskMessage`; a
  `TogglePinFromReader` on `BlockEntityScribeLectern`; registration/handler in `ScribeModSystem`.
- Core: no new model work — `ScribeBlock.Pinned`, `ScribeDocument.TogglePinned`, and codec v3
  serialization already exist. Adds `tests/Core.Tests` coverage for the pin toggle/round-trip if not
  already present (Core-only, no VS API).
- No new dependencies; vanilla `VintagestoryAPI` only. `src/Core/` gains no VS API reference.
