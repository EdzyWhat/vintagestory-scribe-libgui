using Gui;                       // GuiBase, WindowConfig
using Gui.Widgets.Basic;         // WindowFrame, Container
using Gui.Widgets.Framework;     // Widget, ThemeData
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Painting;      // BoxStyle
using OpenTK.Mathematics;        // Vector2
using Vintagestory.API.Client;
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// The minimal standalone settings window opened by the pinned-task HUD's gear (add-settings-tab D2).
/// The HUD is an always-on overlay (<c>EnumDialogType.HUD</c>) with no central content region to swap
/// and may be the only Scribe surface on screen, so — unlike the Lectern, which swaps the settings view
/// into its own central region — the HUD gear hosts the SAME host-agnostic <see cref="ScribeSettingsContent"/>
/// form in this small window. This is the one place a standalone settings window is used; the shared
/// widget means both paths render an identical form.
///
/// <para>Writes are instant (design D3): each control calls <c>ScribeModSystem.UpdateMySettings</c>,
/// which persists and fires <c>MyPinsChanged</c>; this dialog rebuilds on that event so the form
/// re-renders from the clamped value (live preview) and the HUD behind it updates simultaneously.</para>
/// </summary>
public sealed class ScribeSettingsDialog : GuiBase
{
    private readonly ScribeModSystem modSystem;

    /// <summary>Dialog-owned scroll controller for the form, so a live write-through rebuild doesn't
    /// reset the scroll position (mirrors the lectern's dialog-owned controllers). This dialog instance
    /// is reused across gear taps (the HUD caches it), so the controller is disposed once in
    /// <see cref="Dispose"/> — NOT in <see cref="OnGuiClosed"/>, which fires on every close and would
    /// leave a disposed controller for the next open.</summary>
    private readonly ScrollController scrollController = new();

    /// <summary>Host-owned focus state for the form's numeric fields, so focus survives the write-through
    /// <see cref="ForceRebuild"/> each edit triggers (scribe-settings-followups focus fix). Lives for the
    /// dialog's lifetime (reused across gear taps) and is disposed in <see cref="Dispose"/>.</summary>
    private readonly ScribeNumericFocusRegistry numericFocus = new();

    public ScribeSettingsDialog(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;
        modSystem.MyPinsChanged += OnMyPinsChanged;
    }

    /// <summary>Stable position/persistence key distinct from the lectern's (design DialogCode).</summary>
    public override string DialogCode => "scribesettings";

    protected override WindowConfig CreateWindowConfig() => new()
    {
        // Fixed, comfortably-sized settings window (the form scrolls within it if it overflows). Draggable
        // so the player can move it off the HUD; resizable off — the form has a natural width.
        Size = new Vector2(420, 480),
        Draggable = true,
        Resizable = false,
    };

    private void OnMyPinsChanged()
    {
        if (IsOpened()) ForceRebuild();
    }

    protected override Widget Build() =>
        // The standalone settings window is deliberately "the remainder" (scribe-themed-toggle pivot
        // 2026-07-25): it is NOT wrapped in Scribe's pixel-art light theme. It inherits the player's
        // global LibGUI theme (the stock dark default unless they set their own via a community
        // libgui.json), so the Pixel-Art Display toggle here governs the Lectern + HUD but not the
        // window it lives in. No explicit WindowFrame colors either — the frame reads ThemeData.Default,
        // which is exactly the global theme we want it to follow.
        new WindowFrame(
            title: Lang.Get("scribe:settings-title"),
            onClose: () => TryClose(),
            fillHeight: true,
            // Paint the theme's default surface behind the form so the inputs sit on a real panel instead of
            // a transparent gap (refine-settings-and-window-chrome D5). ThemeData.Default is the player's
            // global LibGUI theme — the same one the WindowFrame chrome follows — so this stays consistent
            // with the window's theme inheritance (unchanged from scribe-themed-toggle); only the body fill
            // is added. fillHeight makes the frame stretch its child, so the Container fills the body.
            child: new Container(
                style: new BoxStyle { Color = ThemeData.Default.ColorScheme.Surface },
                child: new ScribeSettingsContent(
                    settings: modSystem.MySettings,
                    onMutate: modSystem.UpdateMySettings,
                    scrollController: scrollController,
                    focus: numericFocus)));

    public override void Dispose()
    {
        modSystem.MyPinsChanged -= OnMyPinsChanged;
        scrollController.Dispose();
        numericFocus.Dispose();
        base.Dispose();
    }
}
