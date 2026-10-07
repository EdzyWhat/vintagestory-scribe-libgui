## Why

With Vintage Story's Immersive Mouse Mode enabled, the Notebook and Clay Tablet windows slide across the screen as the player looks around, until they stick at a screen edge. They behave as if pinned to a compass bearing rather than a point on the screen, which makes them unusable while the mouse is also steering the camera.

Cause: every Scribe dialog inherits LibGUI's "floaty" positioning, which in immersive mode re-projects a world-space anchor to the window position every frame. For block dialogs that anchor is the real block, which is intended. The carried-item dialogs have no block, so they pass a dummy `BlockPos(0)` and end up anchored to the world origin.

## What Changes

- Notebook, Clay Tablet, and (by inheritance) Clockmaker Notebook dialogs opt out of world-anchored positioning, so in Immersive Mouse Mode they keep a fixed screen position and can be dragged.
- Block dialogs (Lectern, Chalkboard, Scriptorium, Assignment Desk, Inbox) are unchanged and keep floating above their block.
- Task Notice already derives from `GuiBase` directly and is unaffected.
- No change when Immersive Mouse Mode is off.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `scribe-dialog-base`: adds a requirement that carried-item dialogs are not world-anchored in Immersive Mouse Mode, while block dialogs are.

## Impact

- `src/Mod/GuiDialogScribeNotebook.cs`, `src/Mod/GuiDialogScribeTablet.cs`: one `Anchor` override each. `GuiDialogClockmakerNotebook` inherits it.
- No save-format, network, or dependency changes. Behavior depends on the shipped `gui` 3.1.0 `GuiDialogBlockEntityBase.Anchor` virtual (verified present by decompile).
- `TESTING.md`: new manual in-game checks (Immersive Mouse Mode cannot be exercised by Core/Atlas tests).
