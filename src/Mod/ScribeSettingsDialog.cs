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
/// The minimal standalone settings window opened by the pinned-task HUD's gear.
/// The HUD is an always-on overlay (<c>EnumDialogType.HUD</c>) with no central content region to swap
/// and may be the only Scribe surface on screen, so — unlike the Lectern, which swaps the settings view
/// into its own central region — the HUD gear hosts the SAME host-agnostic <see cref="ScribeSettingsContent"/>
/// form in this small window. This is the one place a standalone settings window is used; the shared
/// widget means both paths render an identical form.
///
/// <para>Writes are instant: each control calls <c>ScribeModSystem.UpdateMySettings</c>,
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
    /// <see cref="ForceRebuild"/> each edit triggers. Lives for the
    /// dialog's lifetime (reused across gear taps) and is disposed in <see cref="Dispose"/>.</summary>
    private readonly ScribeNumericFocusRegistry numericFocus = new();

    public ScribeSettingsDialog(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;
        // Install the real-or-silent UI sound player for the current mute preference; re-applied in
        // OnMyPinsChanged so toggling the setting in THIS window takes effect on its own buttons live.
        // GuiBase's ctor already installed a real SoundPlayer.
        ApplyUiSoundPreference();
        modSystem.MyPinsChanged += OnMyPinsChanged;
    }

    /// <summary>Swap the LibGUI UI sound player to match this player's <c>MuteUiSounds</c> preference,
    /// mirroring the Lectern dialog. Called from the ctor and on every settings
    /// change so flipping the toggle here re-installs the correct player without a reopen.</summary>
    private void ApplyUiSoundPreference()
        => BuildOwner.SetSoundPlayer(modSystem.GetUiSoundPlayer(capi));

    /// <summary>Stable position/persistence key distinct from the lectern's (see DialogCode).</summary>
    public override string DialogCode => "scribesettings";

    /// <summary>Match <see cref="ScribeDialogBase.DrawOrder"/>'s 0.2 band — this can open on top of a
    /// Lectern/Notebook/Tablet (via their Settings gear), which sits in that band, so the default 0.1
    /// would render this UNDER its own parent.</summary>
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
        // effect on this open window live.
        ApplyUiSoundPreference();
        ForceRebuild();
    }

    protected override Widget Build() =>
        // The standalone settings window is deliberately "the remainder": it is NOT wrapped in Scribe's pixel-art light theme. It inherits the player's
        // global LibGUI theme (the stock dark default unless they set their own via a community
        // libgui.json), so the Pixel-Art Display toggle here governs the Lectern + HUD but not the
        // window it lives in. No explicit WindowFrame colors either — the frame reads ThemeData.Default,
        // which is exactly the global theme we want it to follow.
        new WindowFrame(
            title: Lang.Get("scribe:settings-title"),
            onClose: () => TryClose(),
            fillHeight: true,
            // Paint the theme's default surface behind the form so the inputs sit on a real panel instead of
            // a transparent gap. ThemeData.Default is the player's
            // global LibGUI theme — the same one the WindowFrame chrome follows — so this stays consistent
            // with the window's theme inheritance. fillHeight makes the frame stretch its child, so the Container fills the body.
            child: new Container(
                style: new BoxStyle { Color = ThemeData.Default.ColorScheme.Surface },
                child: new ScribeSettingsContent(
                    settings: modSystem.MySettings,
                    onMutate: modSystem.UpdateMySettings,
                    scrollController: scrollController,
                    focus: numericFocus,
                    onOpenThemePicker: OpenLibGuiThemePicker,
                    // Gates on EITHER backend — the Quest
                    // policy rows are backend-agnostic (ScribeQuestPolicy.cs), so either mod being
                    // installed is enough to show them.
                    showQuestSettings: ScribeQuestCatalog.IsAvailable(capi)
                        || ScribeProgressionFrameworkQuestCatalog.IsAvailable(capi))));

    /// <summary>Runs LibGUI's own `.ui settings` client command,
    /// surfacing its theme picker — otherwise reachable only by a player who already knows that hidden
    /// command exists.
    ///
    /// <para>Why the synthetic caller needs the wildcard privilege (without it the button does nothing; the client log
    /// shows `[scribe] .ui settings -&gt; Error Sorry, you don't have the privilege to use this command`):
    /// `ui`/`settings` never calls <c>RequiresPrivilege</c> anywhere in `Gui.dll`, so
    /// <c>ChatCommandImpl.GetPrivilege()</c> resolves to <c>null</c> and the check falls through to
    /// <see cref="Caller.HasPrivilege"/>, which returns <c>false</c> for a bare <c>null</c> privilege
    /// unless <see cref="Caller.CallerPrivileges"/> grants it via the <c>"*"</c> wildcard. A genuinely
    /// typed chat command gets that wildcard for free (`ChatCommandApi` builds its own local-input
    /// <c>Caller</c> with <c>CallerPrivileges = new[] { "*" }</c>) — a synthetic <c>Caller</c> lacks it
    /// and would fail the check a real typed command passes. Granting the same
    /// wildcard here avoids that; it is not a security concern since this only ever runs in response to
    /// the local player's own click on their own client.</para></summary>
    private void OpenLibGuiThemePicker()
    {
        TryClose();
        capi.ChatCommands.ExecuteUnparsed(".ui settings",
            new TextCommandCallingArgs
            {
                Caller = new Caller { Player = capi.World.Player, CallerPrivileges = new[] { "*" } },
            },
            result => capi.Logger.Notification("[scribe] .ui settings -> {0} {1}", result.Status, result.StatusMessage));
    }

    /// <summary>Notify listeners (the lectern's Settings nav button) that this window just opened, so it
    /// can recolor live.</summary>
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        modSystem.NotifySettingsVisibilityChanged();
    }

    /// <summary>Notify listeners that this window just closed — fires for every close route (gear
    /// re-toggle, title-bar X, Escape) since they all funnel through the base close.</summary>
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
