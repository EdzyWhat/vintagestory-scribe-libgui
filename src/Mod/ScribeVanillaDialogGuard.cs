using System.Linq;
using Gui;                       // GuiBase
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>Reusable "is a vanilla (non-LibGUI) dialog currently open" check
/// (fix-libgui-click-draw-order-mismatch). Vanilla Cairo/GL dialogs always paint on top of
/// LibGUI/Skia surfaces regardless of <c>DrawOrder</c>, while the engine's click dispatch
/// (<c>GuiManager.OnMouseDown</c>) walks <c>LoadedGuis</c> in plain list order — a separate,
/// unrelated ordering. Scribe can't win that click, so callers use this guard to decline to
/// contest it (e.g. skip opening a new modal-style surface) rather than attempt z-order
/// arbitration, which isn't achievable without patching the engine's dispatch loop.
/// Read-only: never closes, focuses, or otherwise mutates any dialog it inspects.
/// "Vanilla" is defined by exclusion (not LibGUI, not HUD) rather than an allow-list, so it
/// stays correct against any future foreign mod dialog without needing an update.</summary>
public static class ScribeVanillaDialogGuard
{
    /// <summary>True when at least one currently-open dialog is neither a LibGUI (<see
    /// cref="GuiBase"/>) window nor a HUD-type overlay (<see cref="EnumDialogType.HUD"/>) — i.e.
    /// a vanilla Cairo/GL dialog such as the base-game Handbook, Inventory, a chest, or a
    /// foreign mod's own dialog.</summary>
    public static bool IsAnyVanillaDialogOpen(ICoreClientAPI capi) =>
        capi.Gui.OpenedGuis.Any(dialog => dialog.DialogType != EnumDialogType.HUD && dialog is not GuiBase);
}
