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
    /// <summary>Toggle the single standalone Scribe settings window (shared by the HUD gear and the Lectern gear;
    /// a second click closes it). Lazily built and reused. Client-only.</summary>
    public void OpenSettings()
    {
        if (capi is null) return; // client-only
        settingsDialog ??= new ScribeSettingsDialog(capi, this);
        if (settingsDialog.IsOpened()) settingsDialog.TryClose();
        else settingsDialog.TryOpen();
    }

    // ── DEV gearworks live-tuning (.geartune) ──────────────────────────────────────────────────────

    /// <summary>DEV: live gearworks-layout tuning knobs; never null (default instance before load / on a server).</summary>
    public ScribeGearTuning GearTuning => gearTuning ??= new ScribeGearTuning();

    /// <summary>DEV: mutate + persist the gearworks tuning and raise <see cref="GearTuningChanged"/> so an open Timer tab rebuilds. Client-only.</summary>
    public void UpdateGearTuning(Action<ScribeGearTuning> mutate)
    {
        if (capi is null) return; // client-only
        var t = GearTuning;
        mutate(t);
        t.Normalized();
        capi.StoreModConfig(t, GearTuningConfigFileName);
        GearTuningChanged?.Invoke();
    }

    /// <summary>DEV: toggle the <c>.geartune</c> window. Lazily built and reused.</summary>
    public void OpenGearTuning()
    {
        if (capi is null) return; // client-only
        gearTuningDialog ??= new ScribeGearTuningDialog(capi, this);
        if (gearTuningDialog.IsOpened()) gearTuningDialog.TryClose();
        else gearTuningDialog.TryOpen();
    }

    /// <summary>DEV: register the client-side <c>.geartune</c> command (edits only client-local JSON, so no privilege).</summary>
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

    // ── DEV block-box live-tuning (.boxtune) ────────────────────────────────────────────────────────

    /// <summary>DEV: live block-box tuning knobs; never null. The server-side fallback is load-bearing: a dedicated
    /// server never calls <see cref="StartClientSide"/>, so it always resolves the untouched default box.</summary>
    public ScribeBoxTuning BoxTuning => boxTuning ??= new ScribeBoxTuning();

    /// <summary>DEV: mutate + persist the box tuning and raise <see cref="BoxTuningChanged"/> so an open <c>.boxtune</c> window re-seeds. Client-only.</summary>
    public void UpdateBoxTuning(Action<ScribeBoxTuning> mutate)
    {
        if (capi is null) return; // client-only
        var t = BoxTuning;
        mutate(t);
        t.Normalized();
        capi.StoreModConfig(t, BoxTuningConfigFileName);
        BoxTuningChanged?.Invoke();
    }

    /// <summary>DEV: toggle the <c>.boxtune</c> window. Lazily built and reused.</summary>
    public void OpenBoxTuning()
    {
        if (capi is null) return; // client-only
        boxTuningDialog ??= new ScribeBoxTuningDialog(capi, this);
        if (boxTuningDialog.IsOpened()) boxTuningDialog.TryClose();
        else boxTuningDialog.TryOpen();
    }

    /// <summary>DEV: register the client-side <c>.boxtune</c> command.</summary>
    private void RegisterBoxTuneCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("boxtune")
            .WithDescription("[scribe dev] Open the Inbox/Scriptorium/Assignment Desk collision-box live-tuning window.")
            .HandleWith(_ =>
            {
                OpenBoxTuning();
                return Vintagestory.API.Common.TextCommandResult.Success();
            });
    }

    /// <summary>DEV: register the client-side <c>.scribelight</c> command, a one-shot readout of the ambient light the GUI shade
    /// derives from at the player's position. Used to calibrate <see cref="ScribeBrightnessCurve"/> anchors.</summary>
    private void RegisterScribeLightCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("scribelight")
            .WithDescription("[scribe dev] Print the ambient light the Scribe GUI shade is derived from here.")
            .HandleWith(_ =>
                Vintagestory.API.Common.TextCommandResult.Success(
                    ScribeAmbientLightSampler.Describe(api, MySettings)));
    }

    /// <summary>DEV: register the client-side <c>.scribeprobe [code]</c> command, a one-shot dump of the crafting-recipe
    /// variants the probe derives for the held item (or a supplied code). Reads only the live registry.</summary>
    private void RegisterScribeProbeCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("scribeprobe")
            .WithDescription("[scribe dev] Dump the crafting-recipe variants/signatures the probe derives for the held item (or a given code).")
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("code"))
            .HandleWith(args =>
            {
                string? code = args[0] as string;
                ItemStack? stack = !string.IsNullOrEmpty(code)
                    ? ScribeItemRef.ResolveStack(api.World, code)
                    : api.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                return Vintagestory.API.Common.TextCommandResult.Success(
                    ScribeCraftRecipeProbe.Describe(api, stack));
            });
    }

    /// <summary>DEV: register the client-side <c>.scribeguard</c> command, a one-shot readout of
    /// <see cref="ScribeVanillaDialogGuard.IsAnyVanillaDialogOpen"/>.</summary>
    private void RegisterScribeGuardCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("scribeguard")
            .WithDescription("[scribe dev] Print whether a vanilla (non-LibGUI) dialog is currently open.")
            .HandleWith(_ =>
                Vintagestory.API.Common.TextCommandResult.Success(
                    $"[scribe] IsAnyVanillaDialogOpen: {ScribeVanillaDialogGuard.IsAnyVanillaDialogOpen(api)}"));
    }

    /// <summary>Client-side: whether THIS player has pinned the task: the optimistic overlay if a pin/unpin is in flight,
    /// else the server-pushed cache. False until the server confirms.</summary>
    public bool IsPinnedForMe(Guid docId, Guid taskId)
        => optimisticPinOverlay.TryGetValue((docId, taskId), out var overlay)
            ? overlay is not null
            : myPins.Contains((docId, taskId));

    /// <summary>
    /// Client-side: load (once) and return the decoded backdrop bitmap, or <c>null</c> if the asset is missing/unloadable
    /// or on the server. A <see cref="ScribeBackdropSpec.Tint"/> is baked into a separately cached tinted copy. Cached and
    /// shared across dialog opens; callers must NOT dispose it (<see cref="Dispose"/> does).
    /// Self-loads via <c>TryGet(loc, loadAsset: true)</c> because a plain <c>TryGet</c> returns null bytes once VS unloads
    /// assets post-startup. The <c>null</c> result is cached too, so an unloadable asset warns once.
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

    /// <summary>Cache key on both asset AND tint, so plain and tinted uses of one PNG stay distinct.</summary>
    private static string BackdropCacheKey(ScribeBackdropSpec spec)
        => spec.Tint is { } v
            ? $"{spec.Texture}|tint={v.X:F3},{v.Y:F3},{v.Z:F3},{v.W:F3}"
            : spec.Texture.ToString();

    /// <summary>Client-side: load (once) and return a decoded GUI texture bitmap by asset location, or <c>null</c> if
    /// missing/unloadable or on a pure server. Shares the backdrop cache and warning behavior; callers must NOT dispose it.</summary>
    public SKBitmap? GetGuiTextureBitmap(AssetLocation loc)
    {
        if (capi is null) return null; // client-only
        return GetBackdropSource(loc);
    }

    /// <summary>Client-side: generate (once) the procedural Timer-tab escape wheel bitmap (<see cref="ScribeGearTexture.GreatWheel"/>),
    /// cached in <c>backdropCache</c> under a synthetic key. Callers must NOT dispose it. Null on a pure server.</summary>
    public SKBitmap? GetProceduralGreatWheel()
    {
        if (capi is null) return null; // client-only
        backdropCache ??= new Dictionary<string, SKBitmap?>();
        // Teeth and spacing are live-tunable: key on the combo so a changed knob generates a fresh bitmap instead of mutating the shared one.
        int teeth   = (int)GearTuning.WheelTeeth;
        int spacing = (int)GearTuning.WheelToothSpacing;
        string key = $"__procedural:great-wheel:{teeth}:{spacing}";
        if (backdropCache.TryGetValue(key, out var cached)) return cached;
        var bmp = ScribeGearTexture.GreatWheel(teeth: teeth, toothSpacingRef: spacing);
        // Immutable so Skia caches the GPU upload (see GetBackdropSource).
        bmp?.SetImmutable();
        backdropCache[key] = bmp;
        return bmp;
    }

    /// <summary>Load + decode the raw (untinted) backdrop PNG once, so tinted and untinted specs share one decode. Warns once on a miss.</summary>
    private SKBitmap? GetBackdropSource(AssetLocation loc)
    {
        backdropCache ??= new Dictionary<string, SKBitmap?>();
        string key = loc.ToString();
        if (backdropCache.TryGetValue(key, out var cached)) return cached;

        var asset = capi!.Assets.TryGet(loc, loadAsset: true);
        SKBitmap? bmp = asset?.Data is not null ? SKBitmap.Decode(asset.Data) : null;
        if (bmp is null)
        {
            // Cache the miss so this warns once, not per open/frame.
            capi.Logger.Warning("[scribe] backdrop asset {0} not loadable ({1}); using placeholder color",
                loc, asset is null ? "not found" : "Data null or undecodable");
        }
        // Immutable so Skia caches the GPU texture upload: on a GPU canvas a mutable source (SKBitmap.Decode
        // returns mutable) is re-uploaded on every draw. Unrelated to the dialog-open "white flash".
        bmp?.SetImmutable();
        backdropCache[key] = bmp;
        return bmp;
    }

    /// <summary>Return a NEW bitmap of <paramref name="source"/> multiplied by <paramref name="tint"/> (Modulate color filter,
    /// as LibGUI's icon renderer does). Baked once at load so the per-frame draw stays a plain texture path.</summary>
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
        // Immutable for the same reason as the decoded source (see GetBackdropSource).
        tinted.SetImmutable();
        return tinted;
    }

    /// <summary>Asset location of the cuneiform glyph-geometry bundle (from <c>glyph-forge/tools/build_glyphs_bundle.py</c>).
    /// Under <c>textures/</c> because VS has no fonts/glyphs asset category. Stroke geometry, not a TTF, so it bypasses <c>FontRegistry</c>.</summary>
    private static readonly AssetLocation CuneiformBundleLocation =
        new("scribe", "textures/fonts/cuneiform-glyphs-1.json");

    /// <summary>
    /// Client-side: load (once) and return the parsed cuneiform <see cref="Scribe.Core.Cuneiform.GlyphBundle"/>, or <c>null</c>
    /// if missing/unparseable or on the server. Self-loads with <c>loadAsset: true</c> (see <see cref="GetBackdropBitmap"/>);
    /// a <c>null</c> result is cached via <see cref="cuneiformBundleLoaded"/> so it warns once.
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

    /// <summary>Client-side: THIS player's HUD/pin preferences; never null (defaults before load / on a server).</summary>
    public ScribePlayerSettings MySettings => mySettings ??= new ScribePlayerSettings();

    /// <summary>Author-facing visual-tuning knobs (client-local); never null.</summary>
    public ScribeVisualTuning VisualTuning => visualTuning ??= new ScribeVisualTuning();

    /// <summary>The shared ambient-particle emitter, built once in <see cref="StartClientSide"/> from <see cref="VisualTuning"/>.</summary>
    public ScribeAssignmentParticleEmitter ParticleEmitter =>
        particleEmitter ??= new ScribeAssignmentParticleEmitter(VisualTuning);

    /// <summary>Client-side: mutate + persist this player's preferences, then fire <see cref="MyPinsChanged"/> so an open HUD
    /// re-reads them with no network round-trip.</summary>
    public void UpdateMySettings(Action<ScribePlayerSettings> mutate)
    {
        if (capi is null) return; // client-only
        var settings = MySettings;
        mutate(settings);
        settings.Normalized();
        capi.StoreModConfig(settings, HudConfigFileName);
        MyPinsChanged?.Invoke();
    }

    /// <summary>Client-side: the UI sound player a dialog installs on its <c>BuildOwner</c>: the shared stateless
    /// <see cref="SilentSoundPlayer"/> when <see cref="ScribePlayerSettings.MuteUiSounds"/>, else the stock <see cref="SoundPlayer"/>.
    /// Called from each dialog's ctor and settings-change rebuild hook so a live toggle takes effect without a reopen.</summary>
    public ISoundPlayer GetUiSoundPlayer(ICoreClientAPI capi)
        => MySettings.MuteUiSounds
            ? silentSoundPlayer ??= new SilentSoundPlayer(capi)
            : new SoundPlayer(capi);

    /// <summary>Client-side: THIS player's pin list to DISPLAY, in display order (empty until the first push): the server-pushed
    /// set with in-flight optimistic changes applied, so every consumer sees them identically. Callers must not mutate it.</summary>
    public IReadOnlyList<ScribePinnedRef> MyPins => displayPinList;

    /// <summary>Client-side: record an optimistic pin/unpin ahead of the server round-trip so <see cref="MyPins"/>/<see cref="IsPinnedForMe"/>
    /// reflect it immediately. Pinning (<paramref name="snapshotIfPinned"/> non-null) is placed via <see cref="ScribePinOrdering.PlaceNewPin"/>,
    /// the same rule the server applies, using <paramref name="source"/> (for pinned-parent clustering) and <paramref name="insertEdge"/>.
    /// Unpinning just hides the existing entry.</summary>
    public void SetOptimisticPin(Guid docId, Guid taskId, ScribePinnedRef? snapshotIfPinned,
        ScribeDocument? source, ScribePinInsert insertEdge)
    {
        var key = (docId, taskId);
        if (!optimisticPinOverlay.ContainsKey(key)) optimisticPinOrder.Add(key);
        optimisticPinOverlay[key] = snapshotIfPinned;
        if (snapshotIfPinned is not null) optimisticPinPlan[key] = (source, insertEdge);
        else optimisticPinPlan.Remove(key);

        RebuildDisplayPinList();
        MyPinsChanged?.Invoke();
    }

    /// <summary>Recompute <see cref="displayPinList"/> from <see cref="myPinList"/> with the optimistic overlay applied.</summary>
    private void RebuildDisplayPinList()
    {
        if (optimisticPinOverlay.Count == 0)
        {
            displayPinList = myPinList;
            return;
        }

        var list = new List<ScribePinnedRef>(myPinList);
        list.RemoveAll(p => optimisticPinOverlay.ContainsKey((p.OwnerDocId, p.TaskId)));

        // Replay in original order (not dictionary order): PlaceNewPin must see a parent placed before attaching its child.
        foreach (var key in optimisticPinOrder)
        {
            if (optimisticPinOverlay[key] is not { } snapshot) continue; // an optimistic remove — already dropped above
            var (source, insertEdge) = optimisticPinPlan.TryGetValue(key, out var plan) ? plan : default;
            ScribePinOrdering.PlaceNewPin(list, snapshot, source, insertEdge);
        }

        displayPinList = list;
    }

    /// <summary>Drop each optimistic entry the authoritative push now agrees with, leaving only still-in-flight requests.</summary>
    private void ReconcileOptimisticPins()
    {
        if (optimisticPinOverlay.Count == 0) return;
        for (int i = optimisticPinOrder.Count - 1; i >= 0; i--)
        {
            var key = optimisticPinOrder[i];
            bool serverHasIt = myPins.Contains(key);
            bool weWantPinned = optimisticPinOverlay[key] is not null;
            if (serverHasIt != weWantPinned) continue; // still in flight — keep it

            optimisticPinOverlay.Remove(key);
            optimisticPinPlan.Remove(key);
            optimisticPinOrder.RemoveAt(i);
        }
    }

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
        ReconcileOptimisticPins();
        RebuildDisplayPinList();
        MyPinsChanged?.Invoke();
    }

}
