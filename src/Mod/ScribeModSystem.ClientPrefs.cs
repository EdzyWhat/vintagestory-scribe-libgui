using System;
using System.Collections.Generic;
using Gui.Rendering;             // SkiaAssetLoader
using Gui.Rendering.Text;        // FontRegistry, FontWeight
using Gui.Sound;                 // ISoundPlayer, SoundPlayer (UI click sound)
using OpenTK.Mathematics;        // Vector4 (backdrop tint)
using Scribe.Core;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

public sealed partial class ScribeModSystem
{
    /// <summary>Toggle the single standalone Scribe settings window open/closed. Called from both the HUD
    /// gear and the Lectern gear so there is exactly ONE settings surface (scribe-themed-toggle); clicking
    /// either gear a second time CLOSES it rather than being a no-op (refine-settings-and-window-chrome).
    /// Lazily builds the dialog on first use and reuses it thereafter. Client-only.</summary>
    public void OpenSettings()
    {
        if (capi is null) return; // client-only
        settingsDialog ??= new ScribeSettingsDialog(capi, this);
        if (settingsDialog.IsOpened()) settingsDialog.TryClose();
        else settingsDialog.TryOpen();
    }

    // ── DEV gearworks live-tuning (.geartune) ──────────────────────────────────────────────────────

    /// <summary>DEV: this player's live gearworks-layout tuning knobs (<see cref="ScribeGearTuning"/>).
    /// Falls back to a fresh default instance if queried before load / on a server, so it is never null —
    /// mirrors <see cref="MySettings"/>.</summary>
    public ScribeGearTuning GearTuning => gearTuning ??= new ScribeGearTuning();

    /// <summary>DEV: mutate the gearworks tuning, persist it, and raise <see cref="GearTuningChanged"/> so
    /// an open Timer tab rebuilds live (mirrors <see cref="UpdateMySettings"/>). Client-only.</summary>
    public void UpdateGearTuning(Action<ScribeGearTuning> mutate)
    {
        if (capi is null) return; // client-only
        var t = GearTuning;
        mutate(t);
        t.Normalized();
        capi.StoreModConfig(t, GearTuningConfigFileName);
        GearTuningChanged?.Invoke();
    }

    /// <summary>DEV: toggle the gearworks-tuning window open/closed (opened by the <c>.geartune</c>
    /// command). Lazily builds + reuses the dialog, mirroring <see cref="OpenSettings"/>.</summary>
    public void OpenGearTuning()
    {
        if (capi is null) return; // client-only
        gearTuningDialog ??= new ScribeGearTuningDialog(capi, this);
        if (gearTuningDialog.IsOpened()) gearTuningDialog.TryClose();
        else gearTuningDialog.TryOpen();
    }

    /// <summary>DEV: register the client-side <c>.geartune</c> command that opens the live gearworks-layout
    /// tuning window. A client command (no privilege needed — it edits only client-local JSON), invoked with
    /// the client dot-prefix.</summary>
    private void RegisterGearTuneCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("geartune")
            .WithDescription("[scribe dev] Open the Timer-tab gearworks live-tuning window.")
            .HandleWith(_ =>
            {
                OpenGearTuning();
                return Vintagestory.API.Common.TextCommandResult.Success();
            });
    }

    /// <summary>DEV: register the client-side <c>.scribelight</c> command — a one-shot readout of the ambient
    /// illumination the GUI shade is derived from at the player's current position (raw light in, held light,
    /// curve output). Used to calibrate <see cref="ScribeBrightnessCurve"/> anchors against real in-game values
    /// (respect-local-illumination — measure-don't-theorize). Client command, dot-prefix.</summary>
    private void RegisterScribeLightCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("scribelight")
            .WithDescription("[scribe dev] Print the ambient light the Scribe GUI shade is derived from here.")
            .HandleWith(_ =>
                Vintagestory.API.Common.TextCommandResult.Success(
                    ScribeAmbientLightSampler.Describe(api, MySettings)));
    }

    /// <summary>Client-side: whether THIS player has pinned the given task, from the server-pushed
    /// cache. The lectern GUI drives its resting pin tint / pin-glyph accent off this. Returns false
    /// before the first push (a safe default — nothing shows as pinned until the server confirms).</summary>
    public bool IsPinnedForMe(Guid docId, Guid taskId) => myPins.Contains((docId, taskId));

    /// <summary>
    /// Client-side: load (once) and return the decoded backdrop bitmap for a dialog backdrop, or
    /// <c>null</c> if the asset is missing/unloadable. When the spec carries a <see cref="ScribeBackdropSpec.Tint"/>
    /// the tint is baked into a cached tinted COPY (leaving the untinted source cached separately, so the
    /// same PNG can back both a plain and a tinted spec). The decoded/derived bitmap is cached and shared
    /// across every dialog open; a caller must NOT dispose it — all entries are disposed in <see cref="Dispose"/>.
    ///
    /// <para>Self-loads via <c>TryGet(loc, loadAsset: true)</c> + <see cref="SKBitmap.Decode(byte[])"/>,
    /// mirroring <see cref="RegisterSvgIcon"/> (~:236): the naive <c>Image</c>/<c>SkiaAssetLoader.LoadBitmap</c>
    /// path calls <c>TryGet(loc)</c> WITHOUT <c>loadAsset: true</c>, so its bytes are null after VS unloads
    /// assets post-startup and the backdrop would silently vanish in normal play. The <c>null</c> result is
    /// cached too, so an unloadable asset logs exactly one warning and repeat opens don't retry the failing
    /// load. Returns null before <see cref="StartClientSide"/> (e.g. server side).</para>
    /// </summary>
    public SKBitmap? GetBackdropBitmap(ScribeBackdropSpec spec)
    {
        if (capi is null) return null; // client-only
        backdropCache ??= new Dictionary<string, SKBitmap?>();

        string key = BackdropCacheKey(spec);
        if (backdropCache.TryGetValue(key, out var cached)) return cached;

        var source = GetBackdropSource(spec.Texture);
        SKBitmap? bmp = spec.Tint is { } tint && source is not null ? BakeTint(source, tint) : source;
        backdropCache[key] = bmp;
        return bmp;
    }

    /// <summary>The cache key for a spec's decoded bitmap (<see cref="GetBackdropBitmap"/>). Keys on both the
    /// asset AND the tint so a plain and a tinted use of the same PNG stay distinct in the cache.</summary>
    private static string BackdropCacheKey(ScribeBackdropSpec spec)
        => spec.Tint is { } v
            ? $"{spec.Texture}|tint={v.X:F3},{v.Y:F3},{v.Z:F3},{v.W:F3}"
            : spec.Texture.ToString();

    /// <summary>Client-side: load (once) and return a decoded GUI texture bitmap by asset location, or
    /// <c>null</c> if the asset is missing/unloadable. Shares the same decode cache, one-warning-per-miss,
    /// and <see cref="Dispose"/> cleanup as the dialog backdrops (<see cref="GetBackdropSource"/>) — a caller
    /// must NOT dispose the returned bitmap. Used for in-GUI rasters that are drawn via a
    /// <c>BoxStyle.Texture</c> Container (e.g. the Timer-tab gearworks), which — like the backdrops — need
    /// the <c>loadAsset: true</c> self-load path to survive VS's post-startup asset unload. Returns null on a
    /// pure server.</summary>
    public SKBitmap? GetGuiTextureBitmap(AssetLocation loc)
    {
        if (capi is null) return null; // client-only
        return GetBackdropSource(loc);
    }

    /// <summary>Client-side: generate (once) and return the procedural Timer-tab escape wheel bitmap
    /// (<see cref="ScribeGearTexture.GreatWheel"/>) — a many-toothed "great wheel" in the small cog's blocky,
    /// uneven, negative-space style. Cached under a synthetic key in the same <c>backdropCache</c> so it is
    /// built at most once and disposed with everything else in <see cref="Dispose"/>; a caller must NOT
    /// dispose it. This is a placeholder to be judged/tuned in-game (add-timer-gearworks art follow-up);
    /// swapping to a real PNG later is a one-line change at the gearworks call site. Returns null on a pure
    /// server.</summary>
    public SKBitmap? GetProceduralGreatWheel()
    {
        if (capi is null) return null; // client-only
        backdropCache ??= new Dictionary<string, SKBitmap?>();
        // DEV .geartune: teeth + tooth-spacing are live-tunable, so key the cache on the (teeth, spacing) combo —
        // each combo generates + caches once and is reused; changing a knob makes a fresh combo regenerate on the
        // next rebuild rather than mutating the shared bitmap. Fold back to ScribeGearTexture.Teeth defaults when
        // the .geartune tool is removed.
        int teeth   = (int)GearTuning.WheelTeeth;
        int spacing = (int)GearTuning.WheelToothSpacing;
        string key = $"__procedural:great-wheel:{teeth}:{spacing}";
        if (backdropCache.TryGetValue(key, out var cached)) return cached;
        var bmp = ScribeGearTexture.GreatWheel(teeth: teeth, toothSpacingRef: spacing);
        // Immutable so Skia caches its GPU upload (same rationale as GetBackdropSource) — the wheel is drawn
        // via a BoxStyle.Texture too, and each (teeth,spacing) combo is generated fresh, never mutated in place.
        bmp?.SetImmutable();
        backdropCache[key] = bmp;
        return bmp;
    }

    /// <summary>Load + decode the raw (untinted) backdrop PNG once, caching it under its plain asset key so
    /// both a tinted and an untinted spec on the same PNG share a single decode. Warns once on a miss.</summary>
    private SKBitmap? GetBackdropSource(AssetLocation loc)
    {
        backdropCache ??= new Dictionary<string, SKBitmap?>();
        string key = loc.ToString();
        if (backdropCache.TryGetValue(key, out var cached)) return cached;

        var asset = capi!.Assets.TryGet(loc, loadAsset: true);
        SKBitmap? bmp = asset?.Data is not null ? SKBitmap.Decode(asset.Data) : null;
        if (bmp is null)
        {
            // Cache the miss so this warns once, not per open/frame (the flat-placeholder path draws instead).
            capi.Logger.Warning("[scribe] backdrop asset {0} not loadable ({1}); using placeholder color",
                loc, asset is null ? "not found" : "Data null or undecodable");
        }
        // Mark immutable so Skia can CACHE this bitmap's GPU texture upload and reuse it across draws/opens.
        // ScribePixelArtBackdrop wraps it once as an SKImage and DrawImages it; on a GPU canvas Skia only
        // caches the upload for an IMMUTABLE image source — a mutable one (SKBitmap.Decode returns mutable) is
        // re-uploaded on every draw because the pixels could change. These backdrop bitmaps are cached and
        // never mutated after decode, so immutability is semantically correct and lets repeat opens reuse the
        // resident texture. NOTE: this is a caching win, NOT the "white flash" fix — the flash was proven
        // (fix-dialog-open-white-flash §4.1, 2026-08-13) to be bound to the dialog OPEN transition, not to the
        // backdrop upload/draw (turning art on in an already-open dialog — the first draw of the session —
        // does not flash). See VSAPI-NOTES "White flash". No GL code added.
        bmp?.SetImmutable();
        backdropCache[key] = bmp;
        return bmp;
    }

    /// <summary>Return a NEW bitmap that is <paramref name="source"/> multiplied by <paramref name="tint"/>,
    /// via an <c>SKColorFilter.CreateBlendMode(..., Modulate)</c> — the same tint primitive LibGUI's icon
    /// renderer uses (<c>RenderIcon</c>). Baked once at load and cached, so the per-frame draw stays the
    /// plain stretch-to-fill texture path (no LibGUI change; <c>BoxStyle</c> has no tint of its own).</summary>
    private static SKBitmap BakeTint(SKBitmap source, Vector4 tint)
    {
        var tinted = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(tinted);
        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateBlendMode(
                new SKColor((byte)(tint.X * 255), (byte)(tint.Y * 255), (byte)(tint.Z * 255), (byte)(tint.W * 255)),
                SKBlendMode.Modulate),
        };
        canvas.DrawBitmap(source, 0, 0, paint);
        // Immutable for the same reason as the decoded source (see GetBackdropSource): lets Skia cache this
        // baked bitmap's GPU upload so the tinted-tablet backdrops don't cold-re-upload on every open.
        tinted.SetImmutable();
        return tinted;
    }

    /// <summary>Asset location of the committed cuneiform glyph-geometry bundle, produced by
    /// <c>glyph-forge/tools/build_glyphs_bundle.py</c>. Filed under <c>textures/</c> because VS only scans
    /// its fixed <c>AssetCategory</c> folders — there is no "fonts"/"glyphs" category (see
    /// <see cref="RegisterSvgIcon"/>). This is stroke GEOMETRY, not a TTF, so it does NOT go through
    /// <c>FontRegistry</c>.</summary>
    private static readonly AssetLocation CuneiformBundleLocation =
        new("scribe", "textures/fonts/cuneiform-glyphs-1.json");

    /// <summary>
    /// Client-side: load (once) and return the parsed cuneiform <see cref="Scribe.Core.Cuneiform.GlyphBundle"/>,
    /// or <c>null</c> if the asset is missing/unparseable. Mirrors <see cref="GetBackdropBitmap"/>: it
    /// self-loads via <c>TryGet(loc, loadAsset: true)</c> so the bytes survive VS's post-startup asset
    /// unload (a plain <c>TryGet</c> nulls <c>.Data</c>), then parses the raw JSON string in Core. The parsed
    /// model is cached and shared across every widget; a <c>null</c> result is cached too (guarded by
    /// <see cref="cuneiformBundleLoaded"/>) so an unparseable asset warns exactly once. Returns null before
    /// <see cref="StartClientSide"/> (e.g. server side).
    /// </summary>
    public Scribe.Core.Cuneiform.GlyphBundle? GetCuneiformBundle()
    {
        if (capi is null) return null; // client-only
        if (cuneiformBundleLoaded) return cuneiformBundle;

        cuneiformBundleLoaded = true;
        var asset = capi.Assets.TryGet(CuneiformBundleLocation, loadAsset: true);
        if (asset?.Data is null)
        {
            capi.Logger.Warning("[scribe] cuneiform glyph bundle {0} not loadable ({1}); cuneiform text will not render",
                CuneiformBundleLocation, asset is null ? "not found" : "Data null");
            cuneiformBundle = null;
            return null;
        }

        try
        {
            cuneiformBundle = Scribe.Core.Cuneiform.GlyphBundle.Parse(asset.ToText());
        }
        catch (Exception ex)
        {
            capi.Logger.Warning("[scribe] cuneiform glyph bundle {0} failed to parse: {1}",
                CuneiformBundleLocation, ex.Message);
            cuneiformBundle = null;
        }

        return cuneiformBundle;
    }

    /// <summary>Client-side: THIS player's HUD/pin preferences (client-local; defaults until
    /// <see cref="StartClientSide"/> loads the config). The HUD reads these directly. Falls back to a
    /// fresh default instance if queried before load (e.g. server side), so it is never null.</summary>
    public ScribePlayerSettings MySettings => mySettings ??= new ScribePlayerSettings();

    /// <summary>Client-side: mutate this player's preferences and persist them to the client-local JSON
    /// config. The HUD/lectern refresh off <see cref="MyPinsChanged"/>, which this fires so an open HUD
    /// re-reads the changed preference (e.g. a collapse toggle) with no network round-trip.</summary>
    public void UpdateMySettings(Action<ScribePlayerSettings> mutate)
    {
        if (capi is null) return; // client-only
        var settings = MySettings;
        mutate(settings);
        settings.Normalized();
        capi.StoreModConfig(settings, HudConfigFileName);
        MyPinsChanged?.Invoke();
    }

    /// <summary>Client-side: the UI sound player a Scribe dialog should install on its <c>BuildOwner</c>
    /// for THIS player's current <see cref="ScribePlayerSettings.MuteUiSounds"/> preference
    /// (scribe-mute-ui-sounds) — the shared no-op <see cref="SilentSoundPlayer"/> when muted, else the
    /// stock LibGUI <see cref="SoundPlayer"/>. The silent player is stateless, so one shared instance is
    /// lazily built and reused across dialogs/rebuilds. Called from each Scribe dialog's ctor and its
    /// settings-change rebuild hook, so a live toggle re-installs the right player without a reopen.</summary>
    public ISoundPlayer GetUiSoundPlayer(ICoreClientAPI capi)
        => MySettings.MuteUiSounds
            ? silentSoundPlayer ??= new SilentSoundPlayer(capi)
            : new SoundPlayer(capi);

    /// <summary>Client-side: THIS player's full pin list (empty until the first push), in server order,
    /// each carrying its <c>LastKnownText</c>/<c>LastKnownDone</c> snapshot. The HUD renders from this;
    /// callers must not mutate it.</summary>
    public IReadOnlyList<ScribePinnedRef> MyPins => myPinList;

    private void OnClientReceivedPinnedSet(ScribePinnedSetMessage message)
    {
        myPins.Clear();
        if (ScribePinCodec.TryDeserializeList(message.PinnedRefBytes, out var pins) && pins is not null)
        {
            foreach (var pin in pins) myPins.Add((pin.OwnerDocId, pin.TaskId));
            myPinList = pins;
        }
        else
        {
            myPinList = Array.Empty<ScribePinnedRef>();
        }
        MyPinsChanged?.Invoke();
    }

}
