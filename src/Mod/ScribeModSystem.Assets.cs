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
    /// Registers the mod's custom SVG glyphs into the client's icon table so they can be drawn by
    /// code string like any built-in icon. This is REQUIRED, not optional: <c>IconUtil.DrawIcon</c>
    /// looks a code up in <c>CustomIcons</c> first, then falls through a switch of hardcoded built-in
    /// names -- with NO default case, so an unregistered code (e.g. "scribepin") silently draws
    /// nothing (see VSAPI-NOTES.md "Icon-button glyphs"). <c>SvgIconSource</c> wraps an asset path
    /// as a renderer that flood-recolors the whole SVG to the button's Font.Color at draw time, so
    /// each glyph is authored as a single flat black shape (assets/scribe/textures/icons/*.svg).
    ///
    /// The files MUST live under the <c>textures/</c> category: VS only scans assets under its 16
    /// hardcoded <c>AssetCategory</c> codes (blocktypes, textures, sounds, ... -- there is no
    /// "icons" category), so a file under a bare <c>icons/</c> folder is never loaded and TryGet
    /// returns null. Vanilla stores every SVG icon at <c>textures/icons/</c> (e.g.
    /// game:textures/icons/copy.svg) -- we match that. (Learned the hard way; see VSAPI-NOTES.md.)
    /// </summary>
    private static void RegisterCustomIcons(ICoreClientAPI api)
    {
        RegisterSvgIcon(api, "scribepin", new AssetLocation("scribe", "textures/icons/pin.svg"));
        RegisterSvgIcon(api, "scribegrip", new AssetLocation("scribe", "textures/icons/grip.svg"));
        RegisterSvgIcon(api, "scribeclose", new AssetLocation("scribe", "textures/icons/close.svg"));
        RegisterSvgIcon(api, "scribeedit", new AssetLocation("scribe", "textures/icons/edit.svg"));
        RegisterSvgIcon(api, "scribegear", new AssetLocation("scribe", "textures/icons/gear.svg"));
        RegisterSvgIcon(api, "scribecheck", new AssetLocation("scribe", "textures/icons/check.svg"));
        RegisterSvgIcon(api, "scribeguest",   new AssetLocation("scribe", "textures/icons/guestbook.svg"));
        RegisterSvgIcon(api, "scribehistory", new AssetLocation("scribe", "textures/icons/guestbook.svg"));
        RegisterSvgIcon(api, "scribetimer",   new AssetLocation("scribe", "textures/icons/timer.svg"));
        RegisterSvgIcon(api, "scribeinfo",    new AssetLocation("scribe", "textures/icons/info.svg"));
        // Guide-page Link icon (add-tracker-link-tasks 7.6): a guide/explainer page has no item to draw, so
        // its Link row shows this generic open-book glyph instead of an ItemStackDisplay.
        RegisterSvgIcon(api, "scribebook",    new AssetLocation("scribe", "textures/icons/book.svg"));
        // Drag-reorder feedback glyphs (replace-drag-wash-with-grip-arrows): the grabbed row's grip
        // becomes ◀ and the prospective drop row's grip becomes ▶, replacing the old row-background washes.
        RegisterSvgIcon(api, "scribetriangleleft",  new AssetLocation("scribe", "textures/icons/triangle-left.svg"));
        RegisterSvgIcon(api, "scribetriangleright", new AssetLocation("scribe", "textures/icons/triangle-right.svg"));
        // Add-kind picker caret (add-note-kind-picker D1): the footer's segmented add button carries a caret
        // that opens/closes a floating drop-up menu of kinds. It points ▲ when closed (the menu expands
        // UPWARD, over the scroll body) and flips ▼ when open (tap again to collapse).
        RegisterSvgIcon(api, "scribetriangleup",   new AssetLocation("scribe", "textures/icons/triangle-up.svg"));
        RegisterSvgIcon(api, "scribetriangledown", new AssetLocation("scribe", "textures/icons/triangle-down.svg"));
    }

    /// <summary>
    /// Registers Scribe's bundled typeface(s) with LibGUI's Skia font registry so the mod's own text can
    /// name them via <c>TextStyle.FontFamily</c> (prove-bundled-font-seam). This mirrors how LibGUI itself
    /// bundles + registers its faces in <c>GuiModSystem.LoadFonts</c>: load the <c>.ttf</c> asset bytes to an
    /// <c>SKTypeface</c> via <see cref="SkiaAssetLoader.LoadFont"/>, then hand it to
    /// <c>FontRegistry.RegisterCustomFont(family, weight, typeface)</c>. Once registered,
    /// <c>TextLayoutHelper</c> resolves that family for BOTH measurement and drawing (it checks
    /// <c>GetCustomTypeface</c> before any system-font fallback), so no per-surface draw override is needed
    /// and the scoping is inherent — only text whose family names "Caudex" changes.
    ///
    /// <para>Asset path note: the font lives under <c>textures/fonts/</c>, NOT a bare <c>fonts/</c> folder.
    /// <c>fonts</c> is not one of VS's scanned <c>AssetCategory</c> codes (confirmed by decompile — LibGUI
    /// only loads its own <c>assets/gui/fonts/</c> by doing an extra <c>AddModOrigin("gui","fonts")</c> +
    /// <c>Assets.Reload</c> dance first). Filing it under the already-scanned <c>textures</c> category
    /// (the same one our SVG icons use) avoids that dance entirely. Unlike the icons, we do NOT need a
    /// <c>loadAsset: true</c> re-fetch guard: <see cref="SkiaAssetLoader.LoadFont"/> reads the bytes into an
    /// <c>SKTypeface</c> HERE at client init, before <c>UnloadAssets</c> nulls the asset data, and the
    /// typeface (not the asset) is what the registry keeps. See VSAPI-NOTES.md (§LibGUI).</para>
    /// </summary>
    private static void RegisterCustomFonts(ICoreClientAPI api)
    {
        var loader = new SkiaAssetLoader(api);
        // AssetLocation is lowercased by LoadFont (path.ToLower()), so the asset filename must be lowercase.
        // We ship ONLY the bold cut: Caudex has a single consumer — the lectern dialog title, which requests
        // FontWeight.Bold — so there is no regular Caudex text to preserve. Loading a real regular alongside
        // it (registered under Normal) turned out to render REGULAR for the Bold title in-game: the shipped
        // `gui` mod's font resolution effectively picked the Normal-weight face despite the Bold request
        // (loading was confirmed fine — both faces distinct, weight 400 vs 700 — so the mismatch is in the
        // resolver, not the assets). Registering the ONE bold face under EVERY weight sidesteps that
        // ambiguity entirely: whatever weight the resolver lands on, it returns the bold cut. This mirrors
        // the earlier all-weights registration (commit 8b1fb14) but with the real bold TTF instead of the
        // regular. If a future surface needs regular Caudex, ship the regular under its own family name (or
        // a distinct alias) rather than reintroducing a Normal-weight registration here.
        var bold = loader.LoadFont("scribe", "textures/fonts/caudex-bold.ttf");
        if (bold is null)
        {
            // Missing/corrupt asset: LoadFont already logged the failure. Leave Scribe's text on its
            // current family (sans-serif) rather than crashing — the mod stays fully usable without the face.
            api.Logger.Warning("[scribe] bundled font 'Caudex' failed to load; title stays on the default family");
            return;
        }
        // FontRegistry.GetCustomTypeface is keyed by (family, weight) and returns null on a miss — a weight
        // with no registration falls through to a system font. Register the bold cut under all four weights
        // so every lookup resolves to it.
        foreach (var weight in new[] { FontWeight.Normal, FontWeight.SemiBold, FontWeight.Bold, FontWeight.Italic })
        {
            FontRegistry.RegisterCustomFont("Caudex", weight, bold);
        }
        api.Logger.Notification("[scribe] bundled font 'Caudex' (bold cut) registered under all weights for the lectern dialog title");

        // Task-text font selector faces (v1-release-checklist §6): the player picks one of these for the
        // Lectern's task/note rows. Each is a single-cut regular TTF registered under ALL weights (same
        // reasoning as Caudex above — the resolver may land on any weight, so map them all to the one face).
        // "Playfair Display" / "Cormorant Unicase" are NOT here: the LibGUI (`gui`) dependency already
        // registers those, so the selector can offer them at zero asset cost. Family names must match
        // ScribePlayerSettings.KnownTaskFonts exactly. A missing/corrupt face logs one warning and is simply
        // absent from the resolver (its selector option then falls through to the default body font).
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
        }
        api.Logger.Notification("[scribe] bundled task-text fonts registered for the settings font selector");
    }

    /// <summary>
    /// Registers one SVG asset under a <c>CustomIcons</c> code, re-resolving the asset on every draw
    /// instead of capturing it once. This is REQUIRED: the obvious <c>CustomIcons[code] =
    /// api.Gui.Icons.SvgIconSource(asset)</c> captures the <see cref="IAsset"/> and re-reads its
    /// <c>.Data</c> at draw time -- but VS calls <c>AssetManager.UnloadAssets()</c> after startup,
    /// which sets <c>Data = null</c> on every non-patched asset (confirmed by decompile), so the
    /// captured asset's bytes are gone by the first draw and <c>rasterizeSvg</c> throws
    /// <c>ArgumentNullException("Asset Data is null. Is the asset loaded?")</c>, crashing the client
    /// mid-compose. <c>AssetManager.TryGet(loc, loadAsset: true)</c> re-loads an unloaded asset on
    /// demand (<c>if (!value.IsLoaded() &amp;&amp; loadAsset) value.Origin.TryLoadAsset(value)</c>),
    /// so re-resolving by <see cref="AssetLocation"/> inside the delegate self-heals. Compose is
    /// infrequent (not per-frame), so the re-fetch cost is negligible. See VSAPI-NOTES.md.
    /// </summary>
    private static void RegisterSvgIcon(ICoreClientAPI api, string code, AssetLocation loc)
    {
        api.Gui.Icons.CustomIcons[code] = (ctx, x, y, w, h, rgba) =>
        {
            var asset = api.Assets.TryGet(loc, loadAsset: true);
            if (asset?.Data is null)
            {
                // Missing/unloadable asset: draw nothing rather than throw. Logged once-ish so a
                // packaging mistake is visible without spamming (compose runs on open/recompose).
                api.Logger.Warning("[scribe] icon '{0}' asset {1} not loadable ({2}); drawing nothing",
                    code, loc, asset is null ? "not found" : "Data null");
                return;
            }
            api.Gui.Icons.SvgIconSource(asset)(ctx, x, y, w, h, rgba);
        };
    }

}
