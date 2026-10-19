using System;
using Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// LibGUI shows a first-run "pick a theme" dialog unless <see cref="GuiConfig.Onboarded"/> is set in the
/// shared <c>ModConfig/libgui.json</c> (checked at the top of <c>GuiModSystem.StartClientSide</c>), which
/// confuses new Scribe players. This pre-seeds the file with <c>Onboarded = true</c> and no theme override
/// (the same default LibGUI uses when there is no config) before LibGUI checks it.
///
/// Only acts when the file doesn't exist yet; an existing file, even with <c>Onboarded: false</c>, is left alone.
///
/// A standalone <see cref="ModSystem"/>, not a <see cref="ScribeModSystem"/> partial, for the same
/// reason as <see cref="ScribeHarfBuzzLoadFix"/>: its low <see cref="ExecuteOrder"/> must only affect
/// this one registration.
/// </summary>
public sealed class ScribeSkipLibGuiOnboarding : ModSystem
{
    private const string ConfigFileName = "libgui.json";

    /// <summary>Lower than the default 0.1 (LibGUI's GuiModSystem uses it), so this runs first.</summary>
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
