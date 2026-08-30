using System;
using Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// LibGUI shows a first-run "pick a theme" dialog the first time any LibGUI-based mod runs on a
/// client install, gated on <see cref="GuiConfig.Onboarded"/> in the shared <c>ModConfig/libgui.json</c>
/// file (checked at the top of <c>GuiModSystem.StartClientSide</c>, before any dialog can open). New
/// Scribe players find this dialog confusing since it isn't Scribe's own UI and nothing prompted it.
///
/// Since Scribe hard-depends on LibGUI, this pre-seeds that file with <c>Onboarded = true</c> and no
/// theme override (equivalent to what LibGUI itself writes as its own "no config yet" default — see
/// <c>GuiModSystem.LoadThemeConfig</c>'s null branch, which sets <c>ThemeData.Default = new ThemeData()</c>,
/// identical to leaving <see cref="GuiConfig.Theme"/> null here) — before LibGUI's own
/// <c>GuiModSystem.StartClientSide</c> gets a chance to see an empty config and open the dialog.
///
/// Only acts when the file doesn't exist yet. If it already exists — including with
/// <c>Onboarded: false</c>, which normally only happens if someone deliberately edited the file to
/// re-trigger the dialog — this leaves it alone rather than overriding an existing decision.
///
/// A standalone <see cref="ModSystem"/>, not a <see cref="ScribeModSystem"/> partial, for the same
/// reason as <see cref="ScribeHarfBuzzLoadFix"/>: its low <see cref="ExecuteOrder"/> must only affect
/// this one registration.
/// </summary>
public sealed class ScribeSkipLibGuiOnboarding : ModSystem
{
    private const string ConfigFileName = "libgui.json";

    /// <summary>
    /// Lower than the default 0.1 every other mod (including LibGUI's own GuiModSystem) leaves
    /// unmodified, so this writes the config before LibGUI's own StartClientSide checks it.
    /// </summary>
    public override double ExecuteOrder() => -1.0;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        try
        {
            if (api.LoadModConfig<GuiConfig>(ConfigFileName) != null)
            {
                return; // Already decided (onboarded before, or a deliberate reset) -- leave it alone.
            }

            api.StoreModConfig(new GuiConfig { Onboarded = true }, ConfigFileName);
            api.Logger.Notification(
                "[scribe] pre-onboarded LibGUI onto its default theme; skipping the first-run theme " +
                "picker (no previous {0} found)", ConfigFileName);
        }
        catch (Exception ex)
        {
            api.Logger.Warning(
                "[scribe] could not pre-seed LibGUI's onboarding config; the first-run theme picker " +
                "may still appear: {0}", ex.Message);
        }
    }
}
