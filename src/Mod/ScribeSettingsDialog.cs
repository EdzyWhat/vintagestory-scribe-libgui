using Gui;                       // GuiBase, WindowConfig
using Gui.Widgets.Basic;         // WindowFrame, Container
using Gui.Widgets.Framework;     // Widget, ThemeData
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Painting;      // BoxStyle
using OpenTK.Mathematics;        // Vector2
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // Caller, TextCommandCallingArgs
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
        // Install the real-or-silent UI sound player for the current mute preference; re-applied in
        // OnMyPinsChanged so toggling the setting in THIS window takes effect on its own buttons live
        // (scribe-mute-ui-sounds). GuiBase's ctor already installed a real SoundPlayer.
        ApplyUiSoundPreference();
        modSystem.MyPinsChanged += OnMyPinsChanged;
    }

    /// <summary>Swap the LibGUI UI sound player to match this player's <c>MuteUiSounds</c> preference
    /// (scribe-mute-ui-sounds), mirroring the Lectern dialog. Called from the ctor and on every settings
    /// change so flipping the toggle here re-installs the correct player without a reopen.</summary>
    private void ApplyUiSoundPreference()
        => BuildOwner.SetSoundPlayer(modSystem.GetUiSoundPlayer(capi));

    /// <summary>Stable position/persistence key distinct from the lectern's (design DialogCode).</summary>
    public override string DialogCode => "scribesettings";

    /// <summary>Match <see cref="ScribeDialogBase.DrawOrder"/>'s 0.2 band — this can open on top of a
    /// Lectern/Notebook/Tablet (via their Settings gear), which now sits in that band, so staying at the
    /// unset 0.1 default would render this UNDER its own parent.</summary>
    public override double DrawOrder => 0.2;

    protected override WindowConfig CreateWindowConfig() => new()
    {
        // Fixed, comfortably-sized settings window (the form scrolls within it if it overflows). Draggable
        // so the player can move it off the HUD; resizable off — the form has a natural width.
        Size = new Vector2(480, 620),
        Draggable = true,
        Resizable = false,
    };

    private void OnMyPinsChanged()
    {
        if (!IsOpened()) return;
        // Re-install the matching sound player in case the mute preference was just toggled, so it takes
        // effect on this open window live (scribe-mute-ui-sounds).
        ApplyUiSoundPreference();
        ForceRebuild();
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
                    focus: numericFocus,
                    onOpenThemePicker: OpenLibGuiThemePicker,
                    showQuestSettings: ScribeQuestCatalog.IsAvailable(capi))));

    /// <summary>Runs LibGUI's own `.ui settings` client command (refine-assignment-desk-inbox-ux D6),
    /// surfacing its theme picker — otherwise reachable only by a player who already knows that hidden
    /// command exists. <see cref="Caller.Player"/>'s setter also sets <c>Type</c>/<c>Entity</c>, matching
    /// how a typed chat command's calling args are populated.
    ///
    /// <para>Closes THIS window first (playtest 2026-08-31: the button "did nothing" — decompiling the
    /// shipped `Gui.dll`'s `.ui settings` handler confirmed the command itself just builds+opens LibGUI's
    /// own <c>SettingsDialog</c> unconditionally; the likely explanation is that dialog opening BEHIND this
    /// still-open Scribe Settings window, since both are separate top-level windows and nothing coordinates
    /// their stacking order). Since this is the only Scribe surface that ever opens the theme picker
    /// (design D6), closing it here can't strand any other Scribe view. The result callback logs to the
    /// client log (not a chat message — this is a diagnostic for the next retest, not a player-facing
    /// notice) so a further-recurrence pinpoints the exact <c>TextCommandResult</c> instead of another
    /// silent "nothing happened".</para></summary>
    private void OpenLibGuiThemePicker()
    {
        TryClose();
        capi.ChatCommands.ExecuteUnparsed(".ui settings",
            new TextCommandCallingArgs { Caller = new Caller { Player = capi.World.Player } },
            result => capi.Logger.Notification("[scribe] .ui settings -> {0} {1}", result.Status, result.StatusMessage));
    }

    /// <summary>Notify listeners (the lectern's Settings nav button) that this window just opened, so it
    /// can recolor live (add-active-tab-nav-colors).</summary>
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        modSystem.NotifySettingsVisibilityChanged();
    }

    /// <summary>Notify listeners that this window just closed — fires for every close route (gear
    /// re-toggle, title-bar X, Escape) since they all funnel through the base close (add-active-tab-nav-colors).</summary>
    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        modSystem.NotifySettingsVisibilityChanged();
    }

    public override void Dispose()
    {
        modSystem.MyPinsChanged -= OnMyPinsChanged;
        scrollController.Dispose();
        numericFocus.Dispose();
        base.Dispose();
    }
}
