using System;
using System.Collections.Generic;
using Gui.Rendering;             // SkiaAssetLoader
using Gui.Rendering.Text;        // FontRegistry, FontWeight
using Gui.Sound;                 // ISoundPlayer, SoundPlayer (UI click sound)
using Scribe.Core;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

public sealed partial class ScribeModSystem
{
    /// <summary>
    /// Registers the mod's SVG glyphs into the client's icon table. Required: <c>IconUtil.DrawIcon</c> checks
    /// <c>CustomIcons</c> then a hardcoded switch with no default case, so an unregistered code silently draws
    /// nothing (see VSAPI-NOTES.md "Icon-button glyphs"). <c>SvgIconSource</c> flood-recolors the SVG to the
    /// button's Font.Color, so each glyph is a single flat black shape.
    ///
    /// The files must live under <c>textures/</c>: VS only scans its hardcoded <c>AssetCategory</c> folders, so a
    /// bare <c>icons/</c> folder is never loaded (see VSAPI-NOTES.md).
    /// </summary>
    private static void RegisterCustomIcons(ICoreClientAPI api)
    {
        RegisterSvgIcon(api, "scribepin", new AssetLocation("scribe", "textures/icons/pin.svg"));
        RegisterSvgIcon(api, "scribegrip", new AssetLocation("scribe", "textures/icons/grip.svg"));
        RegisterSvgIcon(api, "scribeclose", new AssetLocation("scribe", "textures/icons/close.svg"));
        RegisterSvgIcon(api, "scribeclear", new AssetLocation("scribe", "textures/icons/clear.svg"));
        RegisterSvgIcon(api, "scribeedit", new AssetLocation("scribe", "textures/icons/edit.svg"));
        RegisterSvgIcon(api, "scribegear", new AssetLocation("scribe", "textures/icons/gear.svg"));
        // Hollow-ring gear used only by ScribeHudGearButton; Settings tabs use the solid "scribegear".
        RegisterSvgIcon(api, "scribegearhud", new AssetLocation("scribe", "textures/icons/gear-hud.svg"));
        RegisterSvgIcon(api, "scribecheck", new AssetLocation("scribe", "textures/icons/check.svg"));
        RegisterSvgIcon(api, "scribeguest",   new AssetLocation("scribe", "textures/icons/guestbook.svg"));
        // Same book.svg as scribebook below.
        RegisterSvgIcon(api, "scribehistory", new AssetLocation("scribe", "textures/icons/book.svg"));
        RegisterSvgIcon(api, "scribetimer",   new AssetLocation("scribe", "textures/icons/timer.svg"));
        RegisterSvgIcon(api, "scribeinfo",    new AssetLocation("scribe", "textures/icons/info.svg"));
        RegisterSvgIcon(api, "scribeinventory", new AssetLocation("scribe", "textures/icons/grid.svg"));
        // Guide-page Link icon (no item to draw); also the Scriptorium's Transcribe tab icon.
        RegisterSvgIcon(api, "scribebook",    new AssetLocation("scribe", "textures/icons/book.svg"));
        // Quest Link marker, distinct from the guide-page book glyph.
        RegisterSvgIcon(api, "scribequest",   new AssetLocation("scribe", "textures/icons/quest.svg"));
        // Static VS Quest objective marker; implies no click target.
        RegisterSvgIcon(api, "scribeobjective", new AssetLocation("scribe", "textures/icons/bullseye.svg"));
        // Drag-reorder feedback: grabbed row's grip becomes ◀, the drop row's becomes ▶.
        RegisterSvgIcon(api, "scribetriangleleft",  new AssetLocation("scribe", "textures/icons/triangle-left.svg"));
        RegisterSvgIcon(api, "scribetriangleright", new AssetLocation("scribe", "textures/icons/triangle-right.svg"));
        // Add-kind picker caret: ▲ when closed (the drop-up menu expands upward), ▼ when open.
        RegisterSvgIcon(api, "scribetriangleup",   new AssetLocation("scribe", "textures/icons/triangle-up.svg"));
        RegisterSvgIcon(api, "scribetriangledown", new AssetLocation("scribe", "textures/icons/triangle-down.svg"));
        RegisterSvgIcon(api, "scribeassignment", new AssetLocation("scribe", "textures/icons/scroll.svg"));
        RegisterSvgIcon(api, "scribeinboxarrow", new AssetLocation("scribe", "textures/icons/inbox-arrow.svg"));
        RegisterSvgIcon(api, "scribeplus", new AssetLocation("scribe", "textures/icons/plus-thin.svg"));
    }

    /// <summary>
    /// Registers Scribe's bundled typefaces with LibGUI's <c>FontRegistry</c> (as LibGUI does in
    /// <c>GuiModSystem.LoadFonts</c>) so text can name them via <c>TextStyle.FontFamily</c>. <c>TextLayoutHelper</c>
    /// checks <c>GetCustomTypeface</c> for both measurement and drawing, so only text naming a registered
    /// family changes.
    ///
    /// <para>Fonts live under <c>textures/fonts/</c> because <c>fonts</c> is not a scanned <c>AssetCategory</c>
    /// (see VSAPI-NOTES.md). Unlike the icons, no <c>loadAsset: true</c> re-fetch is needed:
    /// <see cref="SkiaAssetLoader.LoadFont"/> copies the bytes into an <c>SKTypeface</c> at client init, before
    /// <c>UnloadAssets</c> nulls the asset data, and the registry keeps the typeface.</para>
    /// </summary>
    private static void RegisterCustomFonts(ICoreClientAPI api)
    {
        var loader = new SkiaAssetLoader(api);
        // LoadFont lowercases the path, so asset filenames must be lowercase.
        // The bold cut is registered under every non-italic weight: registering a regular face under Normal
        // alongside it makes the resolver pick Normal for a Bold request. The Italic slot holds a real italic
        // cut; if the title renders in the wrong face, suspect that resolver quirk before a bad asset.
        // Families that actually registered, for the default-family fallback below.
        var registeredFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var bold = loader.LoadFont("scribe", "textures/fonts/caudex-bold.ttf");
        var italic = loader.LoadFont("scribe", "textures/fonts/caudex-italic.ttf");
        bool caudexRegistered = bold is not null;
        if (bold is null)
        {
            // LoadFont already logged the failure; the title falls back to the default family.
            api.Logger.Warning("[scribe] bundled font 'Caudex' failed to load; title stays on the default family");
        }
        else
        {
            // GetCustomTypeface is keyed by (family, weight); an unregistered weight falls through to a system
            // font, so Italic falls back to the bold face rather than staying unregistered.
            foreach (var weight in new[] { FontWeight.Normal, FontWeight.SemiBold, FontWeight.Bold })
            {
                FontRegistry.RegisterCustomFont("Caudex", weight, bold);
            }
            FontRegistry.RegisterCustomFont("Caudex", FontWeight.Italic, italic ?? bold);
            registeredFamilies.Add("Caudex");
            api.Logger.Notification("[scribe] bundled font 'Caudex' registered (bold cut for Normal/SemiBold/Bold, italic cut for Italic)");
        }
        if (italic is null)
        {
            api.Logger.Warning("[scribe] bundled font 'Caudex' italic cut failed to load; italic text falls back to the bold cut");
        }

        // Task-text selector faces: single-cut regular TTFs registered under all weights (the resolver may
        // land on any). "Playfair Display" / "Cormorant Unicase" are registered by `gui` already. Family names
        // must match ScribePlayerSettings.KnownTaskFonts exactly. A failed face logs a warning and falls
        // through to the default body font.
        var taskFonts = new (string family, string path)[]
        {
            ("Scapholene", "textures/fonts/scapholene-regular.ttf"),
            ("La Belle Aurore", "textures/fonts/labelleaurore-regular.ttf"),
            ("Noto Sans", "textures/fonts/notosans-regular.ttf"),
            ("Noto Serif", "textures/fonts/notoserif-regular.ttf"),
        };
        foreach (var (family, path) in taskFonts)
        {
            var face = loader.LoadFont("scribe", path);
            if (face is null)
            {
                api.Logger.Warning($"[scribe] bundled task font '{family}' failed to load; it will be unavailable in the font selector");
                continue;
            }
            foreach (var weight in new[] { FontWeight.Normal, FontWeight.SemiBold, FontWeight.Bold, FontWeight.Italic })
            {
                FontRegistry.RegisterCustomFont(family, weight, face);
            }
            registeredFamilies.Add(family);
        }
        api.Logger.Notification("[scribe] bundled task-text fonts registered for the settings font selector");

        // Scribe's default family is the first bundled face that loaded, never an alias of LibGUI's shared
        // "sans-serif" (every gui-dependent mod's unstyled text uses it). If none loaded, use literal "sans-serif".
        string resolvedDefaultFamily = "sans-serif";
        foreach (var family in new[] { "Noto Sans", "Noto Serif", "Scapholene", "La Belle Aurore", "Caudex" })
        {
            if (!registeredFamilies.Contains(family)) continue;
            resolvedDefaultFamily = family;
            break;
        }
        api.Logger.Notification($"[scribe] default family resolved to '{resolvedDefaultFamily}' (no global sans-serif alias)");

        ScribeTaskFont.BuildMetrics(api.Logger, caudexRegistered, resolvedDefaultFamily);
    }

    /// <summary>
    /// Registers one SVG asset under a <c>CustomIcons</c> code, re-resolving the asset on every draw. Required:
    /// capturing the <see cref="IAsset"/> once fails because VS calls <c>AssetManager.UnloadAssets()</c> after
    /// startup, nulling <c>Data</c>, so <c>rasterizeSvg</c> throws and crashes the client mid-compose.
    /// <c>TryGet(loc, loadAsset: true)</c> re-loads on demand; compose is infrequent so the cost is negligible.
    /// See VSAPI-NOTES.md.
    /// </summary>
    private static void RegisterSvgIcon(ICoreClientAPI api, string code, AssetLocation loc)
    {
        api.Gui.Icons.CustomIcons[code] = (ctx, x, y, w, h, rgba) =>
        {
            var asset = api.Assets.TryGet(loc, loadAsset: true);
            if (asset?.Data is null)
            {
                // Draw nothing rather than throw; the warning surfaces packaging mistakes.
                api.Logger.Warning("[scribe] icon '{0}' asset {1} not loadable ({2}); drawing nothing",
                    code, loc, asset is null ? "not found" : "Data null");
                return;
            }
            api.Gui.Icons.SvgIconSource(asset)(ctx, x, y, w, h, rgba);
        };
    }

}
