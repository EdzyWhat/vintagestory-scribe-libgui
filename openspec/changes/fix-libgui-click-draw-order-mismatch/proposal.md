## Why

Scribe's dialogs can consume a mouse click that visually belongs to a vanilla (non-LibGUI) dialog
covering them — e.g. Progression Framework's Ledger, the base-game Handbook, Inventory, or a chest.
This is a known, already-diagnosed limitation (`VSAPI-NOTES.md`, 2026-08-27 spike + 2026-08-29
follow-up): vanilla Cairo/GL dialogs always paint on top of LibGUI/Skia surfaces regardless of
`DrawOrder` (a separate render-pipeline-ordering fact, not fixable within LibGUI 3.1.0 — confirmed
not worth re-attempting), while `GuiManager.OnMouseDown` (decompiled `VintagestoryLib.dll`) dispatches
clicks by walking `LoadedGuis` in plain list order and stopping at the first dialog that consumes the
event — a list-order property, unrelated to what's actually painted on top. Scribe was raised to
`DrawOrder => 0.2` on 2026-08-29 for LibGUI-vs-LibGUI stacking parity (PlayerInvUI), which incidentally
puts it in click contention with vanilla dialogs it will never out-paint.

## What Changes

- A new reusable check: whether a vanilla (non-LibGUI, non-HUD) dialog is currently open —
  `capi.Gui.OpenedGuis` filtered to `DialogType == Dialog` and NOT a `Gui.GuiBase` (LibGUI) instance.
- Scribe surfaces use this check as a guard to decline consuming a click (or opening a new modal-style
  surface) whenever a vanilla dialog is open, rather than attempting to win against it. This does NOT
  restore correct z-order arbitration (not achievable without patching the engine's global dispatch
  loop, explicitly out of scope — see design.md) — it eliminates the concrete hazard (Scribe stealing
  a click meant for something covering it) rather than the abstract one (perfect stacking).
- No change to `DrawOrder`, LibGUI's compositing, or the engine's render/dispatch pipeline.

## Capabilities

### New Capabilities
- `vanilla-dialog-guard`: a standalone, reusable "is a vanilla (non-LibGUI) dialog currently open"
  check, usable from any Scribe surface — a `ScribeDialogBase` subclass or a HUD element alike (Change
  4's Popup notification is a HUD-driven surface, not a dialog subclass, so this can't live inside
  `ScribeDialogBase` itself).

## Impact

- New small static helper (e.g. `ScribeVanillaDialogGuard`, `src/Mod/`): the guard check.
- Used by `rework-quest-accept-notification-styles` (Change 4, this session) for its Popup
  notification style — this change's guard is a prerequisite for that one, not the other way around.
- No `src/Core/` changes.
