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
/// The standalone settings window opened by the pinned-task HUD's gear. The HUD has no central region to
/// swap, so it hosts the same <see cref="ScribeSettingsContent"/> form the Lectern embeds.
///
/// <para>Writes are instant: each control calls <c>ScribeModSystem.UpdateMySettings</c>, which persists and
/// fires <c>MyPinsChanged</c>; this dialog rebuilds on that event.</para>
/// </summary>
public sealed class ScribeSettingsDialog : GuiBase
{
    private readonly ScribeModSystem modSystem;

    /// <summary>Scroll controller owned here so a rebuild doesn't reset the scroll position. The HUD caches
    /// and reuses this dialog, so it is disposed in <see cref="Dispose"/>, NOT in <see cref="OnGuiClosed"/>
    /// (which would leave a disposed controller for the next open).</summary>
    private readonly ScrollController scrollController = new();

    /// <summary>Focus state for the form's numeric fields, so focus survives each edit's
    /// <see cref="ForceRebuild"/>. Disposed in <see cref="Dispose"/>.</summary>
    private readonly ScribeNumericFocusRegistry numericFocus = new();

    public ScribeSettingsDialog(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;
        // GuiBase's ctor already installed a real SoundPlayer; replace it per the mute preference.
        ApplyUiSoundPreference();
        modSystem.MyPinsChanged += OnMyPinsChanged;
    }

    /// <summary>Installs the LibGUI UI sound player matching <c>MuteUiSounds</c>; re-run on every settings
    /// change so toggling takes effect without a reopen.</summary>
    private void ApplyUiSoundPreference()
        => BuildOwner.SetSoundPlayer(modSystem.GetUiSoundPlayer(capi));

    public override string DialogCode => "scribesettings";

    /// <summary>Matches <see cref="ScribeDialogBase.DrawOrder"/>'s 0.2 band: this opens over a
    /// Lectern/Notebook/Tablet, and the default 0.1 would render it UNDER its parent.</summary>
    public override double DrawOrder => 0.2;

    protected override WindowConfig CreateWindowConfig() => new()
    {
        // Draggable so the player can move it off the HUD; not resizable (the form has a natural width).
        Size = new Vector2(480, 620),
        Draggable = true,
        Resizable = false,
    };

    private void OnMyPinsChanged()
    {
        if (!IsOpened()) return;
        ApplyUiSoundPreference();
        ForceRebuild();
    }

    protected override Widget Build() =>
        // Deliberately NOT wrapped in Scribe's theme: the window follows the player's global LibGUI theme
        // (ThemeData.Default), so Pixel-Art Display governs the Lectern + HUD but not this window.
        new WindowFrame(
            title: Lang.Get("scribe:settings-title"),
            onClose: () => TryClose(),
            fillHeight: true,
            // Paints the global theme's surface behind the form; fillHeight makes the Container fill the body.
            child: new Container(
                style: new BoxStyle { Color = ThemeData.Default.ColorScheme.Surface },
                child: new ScribeSettingsContent(
                    settings: modSystem.MySettings,
                    onMutate: modSystem.UpdateMySettings,
                    scrollController: scrollController,
                    focus: numericFocus,
                    onOpenThemePicker: OpenLibGuiThemePicker,
                    // The quest policy rows are backend-agnostic, so either quest mod enables them.
                    showQuestSettings: ScribeQuestCatalog.IsAvailable(capi)
                        || ScribeProgressionFrameworkQuestCatalog.IsAvailable(capi))));

    /// <summary>Runs LibGUI's `.ui settings` client command to surface its theme picker.
    ///
    /// <para>The synthetic caller needs the <c>"*"</c> privilege wildcard, else the button does nothing
    /// ("you don't have the privilege"). `ui settings` never calls <c>RequiresPrivilege</c>, so
    /// <c>GetPrivilege()</c> is <c>null</c> and <see cref="Caller.HasPrivilege"/> returns <c>false</c> unless
    /// <see cref="Caller.CallerPrivileges"/> has the wildcard, which a typed command gets for free. Safe
    /// because it only runs on the local player's own click.</para></summary>
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

    /// <summary>Notifies listeners (the lectern's Settings nav button) so they can recolor live.</summary>
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        modSystem.NotifySettingsVisibilityChanged();
    }

    /// <summary>Notifies listeners on every close route (gear re-toggle, X, Escape).</summary>
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
