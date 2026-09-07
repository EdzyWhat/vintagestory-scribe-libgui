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

    /// <summary>True when a vanilla (non-LibGUI, non-HUD) dialog's own composer bounds contain the given
    /// raw screen point — the position-aware sibling of <see cref="IsAnyVanillaDialogOpen"/>, added
    /// 2026-09-07 for <c>ScribeDialogBase.OnMouseDown</c>. Checks EVERY open vanilla dialog regardless of
    /// which one (if any) is currently focused: per this project's own confirmed finding (`VSAPI-NOTES.md`,
    /// also documented at <c>ScribeDialogBase.DrawOrder</c>), a vanilla Cairo/GL dialog always paints on top
    /// of a LibGUI/Skia surface wherever they overlap, regardless of focus or `DrawOrder` — so bounds alone,
    /// not focus, decide whether Scribe would be visually underneath at this point. Deliberately NOT gated
    /// on <see cref="IsAnyVanillaDialogOpen"/>'s blanket global check — that check disabled Scribe's mouse
    /// events entirely while ANY vanilla dialog was open anywhere on screen, even one nowhere near the
    /// click, which made a Scribe dialog impossible to click back into focus. This checks the actual click
    /// point instead, so a click that lands on Scribe itself (not inside any vanilla dialog's bounds) is
    /// still accepted and can restore Scribe's focus normally.</summary>
    public static bool IsVanillaDialogAt(ICoreClientAPI capi, int x, int y)
    {
        foreach (var dialog in capi.Gui.OpenedGuis)
        {
            if (dialog.DialogType == EnumDialogType.HUD || dialog is GuiBase) continue;
            foreach (var composer in dialog.Composers.Values)
            {
                if (composer.Bounds.PointInside(x, y)) return true;
            }
        }
        return false;
    }
}
