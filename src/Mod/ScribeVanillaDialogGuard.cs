using System.Linq;
using Gui;                       // GuiBase
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>"Is a vanilla (non-LibGUI) dialog open" checks. Vanilla Cairo/GL dialogs always paint over
/// LibGUI/Skia surfaces regardless of <c>DrawOrder</c>, while click dispatch
/// (<c>GuiManager.OnMouseDown</c>) walks <c>LoadedGuis</c> in list order, a separate ordering. Scribe can't
/// win that click, so callers decline to contest it rather than arbitrate z-order (not achievable without
/// patching the dispatch loop). Read-only. "Vanilla" is defined by exclusion (not LibGUI, not HUD), so it
/// covers foreign mod dialogs too.</summary>
public static class ScribeVanillaDialogGuard
{
    /// <summary>True when an open dialog is neither a LibGUI (<see cref="GuiBase"/>) window nor a HUD
    /// overlay (<see cref="EnumDialogType.HUD"/>).</summary>
    public static bool IsAnyVanillaDialogOpen(ICoreClientAPI capi) =>
        capi.Gui.OpenedGuis.Any(dialog => dialog.DialogType != EnumDialogType.HUD && dialog is not GuiBase);

    /// <summary>True when any vanilla dialog's composer bounds contain the raw screen point; the
    /// position-aware sibling of <see cref="IsAnyVanillaDialogOpen"/>, used by <c>ScribeDialogBase.OnMouseDown</c>.
    /// Checks every open vanilla dialog regardless of focus, because a vanilla dialog always paints over
    /// Scribe where they overlap (see VSAPI-NOTES.md and <c>ScribeDialogBase.DrawOrder</c>). Deliberately not
    /// gated on the blanket check, which would disable Scribe's mouse events whenever ANY vanilla dialog was
    /// open; a click on Scribe itself is still accepted and can restore its focus.</summary>
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
