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

    /// <summary>Client-side: whether THIS player has pinned the given task, from the server-pushed
    /// cache. The lectern GUI drives its resting pin tint / pin-glyph accent off this. Returns false
    /// before the first push (a safe default — nothing shows as pinned until the server confirms).</summary>
    public bool IsPinnedForMe(Guid docId, Guid taskId) => myPins.Contains((docId, taskId));

    /// <summary>
    /// Client-side: load (once) and return the decoded backdrop bitmap for a dialog backdrop, or
    /// <c>null</c> if the asset is missing/unloadable. The decoded bitmap is cached and shared across
    /// every dialog open; a caller must NOT dispose it — all entries are disposed in <see cref="Dispose"/>.
    ///
    /// <para>Self-loads via <c>TryGet(loc, loadAsset: true)</c> + <see cref="SKBitmap.Decode(byte[])"/>,
    /// mirroring <see cref="RegisterSvgIcon"/> (~:236): the naive <c>Image</c>/<c>SkiaAssetLoader.LoadBitmap</c>
    /// path calls <c>TryGet(loc)</c> WITHOUT <c>loadAsset: true</c>, so its bytes are null after VS unloads
    /// assets post-startup and the backdrop would silently vanish in normal play. The <c>null</c> result is
    /// cached too, so an unloadable asset logs exactly one warning and repeat opens don't retry the failing
    /// load. Returns null before <see cref="StartClientSide"/> (e.g. server side).</para>
    /// </summary>
    public SKBitmap? GetBackdropBitmap(AssetLocation loc)
    {
        if (capi is null) return null; // client-only
        backdropCache ??= new Dictionary<string, SKBitmap?>();

        string key = loc.ToString();
        if (backdropCache.TryGetValue(key, out var cached)) return cached;

        var asset = capi.Assets.TryGet(loc, loadAsset: true);
        SKBitmap? bmp = asset?.Data is not null ? SKBitmap.Decode(asset.Data) : null;
        if (bmp is null)
        {
            // Cache the miss so this warns once, not per open/frame (the flat-placeholder path draws instead).
            capi.Logger.Warning("[scribe] backdrop asset {0} not loadable ({1}); using placeholder color",
                loc, asset is null ? "not found" : "Data null or undecodable");
        }
        backdropCache[key] = bmp;
        return bmp;
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
