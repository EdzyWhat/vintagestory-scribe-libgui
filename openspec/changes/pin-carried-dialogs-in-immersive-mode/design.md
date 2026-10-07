## Context

`ScribeDialogBase` derives from LibGUI's `GuiDialogBlockEntityBase`. Its `OnRenderGUI` overwrites `WindowPos` each frame, only when `immersiveMouseMode` is on, by projecting the `Anchor` (`WorldAnchor`) through the camera. `Anchor` is `protected virtual` and documented to return `null` for plain screen-space positioning; the default anchors to `BlockEntityPos + (0.5, 0.75, 0.5)`. The shipped `gui` 3.1.0 matches the 2.0.0 reference clone on this logic (checked with `ilspycmd`).

Notebook and Tablet construct the base with `new BlockPos(0)` because they have no block, so the default anchor is the world origin. See proposal.md for the symptom. The dummy position is also used by the duplicate-open and out-of-range checks, which do not read `Anchor`.

## Goals / Non-Goals

**Goals:**
- Notebook, Tablet, and Clockmaker Notebook are screen-fixed in Immersive Mouse Mode.
- Block dialogs keep floaty behavior.

**Non-Goals:**
- Changing where the carried dialogs first open (they keep LibGUI's default placement).
- Changing block-dialog anchoring, or removing the dummy `BlockPos`.
- Right-edge placement via `ImmersiveWindowPosition`.

## Decisions

- **Override `Anchor` to `null` in `GuiDialogScribeNotebook` and `GuiDialogScribeTablet`.** `GuiDialogClockmakerNotebook` inherits from the Notebook, so it needs no override. Alternative: a `virtual bool FloatsAboveBlock` on `ScribeDialogBase` mapped to `Anchor`. Rejected: more indirection for two call sites, and the override sits next to the `new BlockPos(0)` it compensates for. Revisit if a third carried dialog appears.
- **Leave the default open placement alone.** Without an anchor, `WindowPos` is no longer overwritten, so the window uses the placement it had in non-immersive mode. Alternative: `ImmersiveWindowPosition.RightAnchoredOrCentered` to keep the crosshair clear. Deferred: it is a separate UX choice and not needed to fix the drift.
- **Keep the dummy `BlockPos(0)`.** Removing it would touch duplicate-open and range logic for no benefit here.

## Risks / Trade-offs

- [A `gui` update renames or removes `Anchor`] → compile error, caught at build, not silent.
- [Centered placement covers the crosshair in immersive mode] → acceptable for a window the player opens deliberately; revisit as a follow-up if playtesting dislikes it.
- [Immersive Mouse Mode cannot be tested headless] → manual checks recorded in TESTING.md.
