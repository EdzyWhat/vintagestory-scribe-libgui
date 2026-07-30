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

/// <summary>
/// Mod entry point. Registers the lectern's block/block-entity classes and the network
/// channel used for server-authoritative document edits. Per-side setup (hotkeys, GUI,
/// lock bookkeeping) happens in <see cref="StartClientSide"/>/<see cref="StartServerSide"/>.
///
/// Also owns the per-player pin layer: the server-side <see cref="ScribePinStore"/> (pins +
/// a live DocId→position index), its save-game persistence, the identity-addressed pin/complete
/// handlers, and the per-player push of a player's own pins to their client. The client caches its
/// own pushed set so the lectern GUI can query <see cref="IsPinnedForMe"/>. Per-player display/
/// behavior preferences are NOT server state — they are client-local JSON (<see cref="MySettings"/>).
/// </summary>
public sealed class ScribeModSystem : ModSystem
{
    public const string NetworkChannelName = "scribe";

    /// <summary>Savegame key for the persisted pin store. (Per-player display/behavior preferences are
    /// NOT persisted server-side — they are client-local JSON; see <see cref="HudConfigFileName"/>.)</summary>
    private const string PinStoreSaveKey = "scribe:pins:v1";

    /// <summary>Client-local JSON file holding ALL of this player's Scribe preferences — completion
    /// policy, HUD rows/anchor/offsets/width/collapse, and the HUD/window font-size scales — per-player,
    /// cross-world, never server-synced. As of add-settings-tab this is the SINGLE client-local
    /// preference store: the former <c>scribe-client-config.json</c> row-tuning file was retired and its
    /// one live knob (the font size) folded in here as the two font scales. An existing
    /// <c>scribe-client-config.json</c> on disk is simply left unread (harmless).</summary>
    public const string HudConfigFileName = "scribe-hud-config.json";

    private ICoreClientAPI? capi;
    private ICoreServerAPI? sapi;

    /// <summary>Client-side shared no-op UI sound player (scribe-mute-ui-sounds), lazily built on first
    /// use while the mute preference is on and reused across dialogs (it's stateless). See
    /// <see cref="GetUiSoundPlayer"/>.</summary>
    private SilentSoundPlayer? silentSoundPlayer;

    /// <summary>Server-side pin/settings store. Null on a pure client.</summary>
    private ScribePinStore? pinStore;

    /// <summary>Client-side cache of THIS player's own pins, populated by the server push. Keyed by
    /// (docId, taskId) for O(1) <see cref="IsPinnedForMe"/> lookups from the GUI.</summary>
    private readonly HashSet<(Guid, Guid)> myPins = new();

    /// <summary>Client-side cache of THIS player's full pin list (in server order), populated by the
    /// same push as <see cref="myPins"/>. The HUD consumes this for each pin's text/done snapshot;
    /// the lectern only needs the <see cref="myPins"/> key set for its tint.</summary>
    private IReadOnlyList<ScribePinnedRef> myPinList = Array.Empty<ScribePinnedRef>();

    /// <summary>Rebindable hotkey code that toggles the pinned-task HUD's collapse state (design D6).
    /// Registered client-side; its handler flips the HUD's client-local collapse preference.</summary>
    public const string HudHotkeyCode = "scribepinhud";

    /// <summary>The client-side pinned-task HUD element (null on a pure server). Constructed in
    /// <see cref="StartClientSide"/>; it self-opens/closes off <see cref="MyPinsChanged"/> and owns its
    /// own event/tick lifetime, disposed in <see cref="Dispose"/>.</summary>
    private HudScribePins? pinHud;

    /// <summary>The single standalone Scribe settings window (scribe-themed-toggle pivot 2026-07-25).
    /// There is now ONE settings surface, opened via <see cref="OpenSettings"/> from BOTH the HUD gear and
    /// the Lectern gear (the former in-Lectern settings view was removed). Lazily constructed on first
    /// open and reused across opens (so it keeps its scroll/focus state); disposed in
    /// <see cref="Dispose"/>. Null until first opened, and on a pure server.</summary>
    private ScribeSettingsDialog? settingsDialog;

    /// <summary>Client-side player preferences (completion policy, HUD rows/collapse), persisted as
    /// client-local JSON (<see cref="HudConfigFileName"/>) and loaded in <see cref="StartClientSide"/>;
    /// never server-synced. The Core POCO doubles as the config's serialized shape. Non-null on the
    /// client after <see cref="StartClientSide"/>.</summary>
    private ScribePlayerSettings? mySettings;

    /// <summary>Client-side cache of self-loaded dialog backdrop bitmaps, keyed by asset-location string
    /// (see <see cref="GetBackdropBitmap"/>). Holds a <c>null</c> value for an asset that could not be
    /// loaded so the failing load is attempted — and warned about — exactly once, not per open or per
    /// frame. One immutable bitmap is shared across every dialog open; a dialog NEVER disposes one. All
    /// entries are disposed in <see cref="Dispose"/>. Null on a pure server.</summary>
    private Dictionary<string, SKBitmap?>? backdropCache;

    /// <summary>
    /// Runtime registry that maps each active <see cref="Guid"/> DocId to the
    /// <see cref="IScribeDocumentHost"/> currently hosting it. Lecterns register on
    /// <c>Initialize</c> and unregister on <c>OnBlockRemoved</c>; NotebookHosts register when
    /// the notebook dialog opens and unregister when it closes. Server-only (hosts register on
    /// both sides via the client and server <see cref="BlockEntityScribeLectern"/> paths, but the
    /// server registry is the authoritative one used for incoming packets).
    /// </summary>
    private readonly Dictionary<Guid, IScribeDocumentHost> _hostRegistry = new();

    /// <summary>Registers a host under its document's <c>DocId</c>. Called by
    /// <see cref="BlockEntityScribeLectern.Initialize"/> and <see cref="NotebookHost"/> on dialog open.</summary>
    public void RegisterHost(IScribeDocumentHost host) => _hostRegistry[host.Document.DocId] = host;

    /// <summary>Unregisters a host by DocId. Called by
    /// <see cref="BlockEntityScribeLectern.OnBlockRemoved"/> and <see cref="NotebookHost"/> on dialog close.</summary>
    public void UnregisterHost(Guid docId) => _hostRegistry.Remove(docId);

    /// <summary>Raised on the client whenever a fresh pin set push arrives, so an open lectern dialog
    /// (and the HUD) can repaint its per-player pin indicators.</summary>
    public event Action? MyPinsChanged;

    /// <summary>Raised on the client whenever the standalone settings window opens or closes
    /// (add-active-tab-nav-colors). The settings window is a separate dialog, not a lectern view, so an
    /// open lectern subscribes to this and rebuilds to recolor its Settings nav button live — regardless
    /// of which gear (lectern or HUD) toggled the window. Fired from the dialog's own open/close lifecycle
    /// so every close route (gear re-toggle, X button, Escape) notifies.</summary>
    public event Action? SettingsVisibilityChanged;

    /// <summary>True while the standalone settings window is currently open (add-active-tab-nav-colors).
    /// The lectern reads this at build time to decide whether its Settings nav button shows its active
    /// color. Safe on a pure server (the dialog is null there → false).</summary>
    public bool IsSettingsOpen => settingsDialog?.IsOpened() == true;

    /// <summary>Invoked by <see cref="ScribeSettingsDialog"/> from its open/close lifecycle to raise
    /// <see cref="SettingsVisibilityChanged"/>. Centralized here so the dialog doesn't expose the event
    /// itself.</summary>
    public void NotifySettingsVisibilityChanged() => SettingsVisibilityChanged?.Invoke();

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterBlockClass("BlockScribeLectern", typeof(BlockScribeLectern));
        api.RegisterBlockEntityClass("ScribeLectern", typeof(BlockEntityScribeLectern));
        api.RegisterItemClass("ItemScribeNotebook", typeof(ItemScribeNotebook));

        // All message types must be registered in this same order on both sides. The original four
        // read/edit/lock messages come first (order frozen); the identity-addressed pin layer is
        // APPENDED after them. ScribeToggleTaskMessage (the old position-addressed read-view toggle)
        // was retired in favor of ScribeCompleteTaskMessage — do not re-add it. The Pin Tab's edit/
        // delete/reorder messages (scribe-pin-editor) are APPENDED strictly after the existing ones —
        // never inserted mid-list — so the wire packet ids of the shipped messages are unchanged.
        api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<ScribeEditDocumentMessage>()
            .RegisterMessageType<ScribeReleaseLockMessage>()
            .RegisterMessageType<ScribeRequestAccessMessage>()
            .RegisterMessageType<ScribeSetPinMessage>()
            .RegisterMessageType<ScribeCompleteTaskMessage>()
            .RegisterMessageType<ScribePinnedSetMessage>()
            .RegisterMessageType<ScribeEditPinnedTaskMessage>()
            .RegisterMessageType<ScribeDeleteTaskMessage>()
            .RegisterMessageType<ScribeReorderPinsMessage>()
            .RegisterMessageType<ScribeRecordVisitorMessage>()
            .RegisterMessageType<ScribeGuestbookSyncMessage>()
            .RegisterMessageType<ScribeEditGuestbookNoteMessage>()
            .RegisterMessageType<ScribeNotebookSaveMessage>();
    }

    /// <summary>Server-side accessor for the pin store, so the block entity can register/orphan its
    /// document and refresh snapshots. Null on the client.</summary>
    public ScribePinStore? PinStore => pinStore;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        // Load this player's client-local HUD/pin preferences (per-player, cross-world, never synced).
        // A missing/corrupt file loads as defaults; Normalize() clamps any hand-edited out-of-range value.
        mySettings = (api.LoadModConfig<ScribePlayerSettings>(HudConfigFileName) ?? new ScribePlayerSettings()).Normalized();

        RegisterCustomIcons(api);
        RegisterCustomFonts(api);

        api.Network.GetChannel(NetworkChannelName)
            .SetMessageHandler<ScribeEditDocumentMessage>(OnClientReceivedEditReply)
            .SetMessageHandler<ScribePinnedSetMessage>(OnClientReceivedPinnedSet)
            .SetMessageHandler<ScribeGuestbookSyncMessage>(OnClientReceivedGuestbookSync)
            .SetMessageHandler<ScribeNotebookSaveMessage>(OnClientReceivedNotebookSave);

        // The pinned-task HUD self-shows once the player's pin set arrives (it subscribes to
        // MyPinsChanged in its ctor), so it can be constructed here regardless of current pin count —
        // it stays closed until there is ≥1 pin. It owns its own subscription + tick; we dispose it.
        pinHud = new HudScribePins(api, this);

        RegisterNotebookTuneCommand(api);

        // Rebindable collapse/expand hotkey (design D6). GUIOrOtherControls so it fires even while a
        // dialog is open; default P, no modifiers. The HUD flips its client-local collapse preference.
        api.Input.RegisterHotKey(HudHotkeyCode, Lang.Get("scribe:hotkey-scribepinhud"), GlKeys.P,
            HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler(HudHotkeyCode, _ =>
        {
            pinHud?.ToggleCollapsed();
            return true;
        });
    }

    /// <summary>Toggle the single standalone Scribe settings window open/closed. Called from both the HUD
    /// gear and the Lectern gear so there is exactly ONE settings surface (scribe-themed-toggle pivot
    /// 2026-07-25); clicking either gear a second time now CLOSES it rather than being a no-op
    /// (refine-settings-and-window-chrome). Lazily builds the dialog on first use and reuses it thereafter.
    /// Client-only.</summary>
    public void OpenSettings()
    {
        if (capi is null) return; // client-only
        settingsDialog ??= new ScribeSettingsDialog(capi, this);
        if (settingsDialog.IsOpened()) settingsDialog.TryClose();
        else settingsDialog.TryOpen();
    }

    /// <summary>Dispose the client-side HUD (its own <see cref="MyPinsChanged"/> subscription + tick), the
    /// shared settings window, and every cached backdrop bitmap (the one place a backdrop bitmap is ever
    /// disposed — never a dialog). The server side holds no unmanaged/disposable state of its own here.</summary>
    public override void Dispose()
    {
        pinHud?.Dispose();
        pinHud = null;
        settingsDialog?.Dispose();
        settingsDialog = null;
        if (backdropCache is not null)
        {
            foreach (var bmp in backdropCache.Values) bmp?.Dispose();
            backdropCache.Clear();
            backdropCache = null;
        }
        base.Dispose();
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
        // Guestbook nav icon — placeholder reusing check.svg until a dedicated icon ships.
        RegisterSvgIcon(api, "scribeguest", new AssetLocation("scribe", "textures/icons/check.svg"));
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

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        sapi = api;
        pinStore = new ScribePinStore();

        var channel = api.Network.GetChannel(NetworkChannelName);
        channel.SetMessageHandler<ScribeEditDocumentMessage>(OnServerReceivedEdit);
        channel.SetMessageHandler<ScribeReleaseLockMessage>(OnServerReceivedReleaseLock);
        channel.SetMessageHandler<ScribeRequestAccessMessage>(OnServerReceivedRequestAccess);
        channel.SetMessageHandler<ScribeSetPinMessage>(OnServerReceivedSetPin);
        channel.SetMessageHandler<ScribeCompleteTaskMessage>(OnServerReceivedCompleteTask);
        channel.SetMessageHandler<ScribeEditPinnedTaskMessage>(OnServerReceivedEditPinnedTask);
        channel.SetMessageHandler<ScribeDeleteTaskMessage>(OnServerReceivedDeleteTask);
        channel.SetMessageHandler<ScribeReorderPinsMessage>(OnServerReceivedReorderPins);
        channel.SetMessageHandler<ScribeRecordVisitorMessage>(OnServerReceivedRecordVisitor);
        channel.SetMessageHandler<ScribeEditGuestbookNoteMessage>(OnServerReceivedEditGuestbookNote);
        channel.SetMessageHandler<ScribeNotebookSaveMessage>(OnServerReceivedNotebookSave);

        // Persist/load the pin + settings stores with the save game (the WaypointMapLayer pattern).
        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.GameWorldSave += OnGameWorldSave;

        // Initial per-player push once a player is fully in-world, and legacy-pin drain.
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
    }

    private void OnSaveGameLoaded()
    {
        if (sapi is null || pinStore is null) return;
        var pinBytes = sapi.WorldManager.SaveGame.GetData(PinStoreSaveKey);
        pinStore.LoadFrom(pinBytes);
    }

    private void OnGameWorldSave()
    {
        if (sapi is null || pinStore is null) return;
        sapi.WorldManager.SaveGame.StoreData(PinStoreSaveKey, pinStore.SerializePins());
    }

    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        PushPinsTo(player);
    }

    private void OnClientReceivedEditReply(ScribeEditDocumentMessage message)
    {
        if (capi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
        {
            lectern.HandleServerReply(message);
        }
    }

    private void OnServerReceivedEdit(IServerPlayer fromPlayer, ScribeEditDocumentMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
        {
            if (!lectern.ApplyEdit(fromPlayer, message.DocumentBytes))
            {
                lectern.SendSaveFailedAck(sapi, fromPlayer);
            }
        }
    }

    private void OnServerReceivedReleaseLock(IServerPlayer fromPlayer, ScribeReleaseLockMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
        {
            lectern.ReleaseLock(fromPlayer.PlayerUID);
        }
    }

    private void OnServerReceivedRequestAccess(IServerPlayer fromPlayer, ScribeRequestAccessMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
        {
            lectern.OnRequestAccess(fromPlayer, message.WantEditor);
        }
    }

    /// <summary>
    /// Pin/unpin, addressed by (DocId, TaskId). An UNPIN removes straight from the store with no block
    /// resolution, so it works when the owning lectern is broken or its chunk is unloaded. A PIN
    /// resolves the owning block via the live index only to snapshot the task's text/done from the
    /// server's own authoritative document (never a client-supplied snapshot); if the document can't be
    /// resolved right now the pin is still recorded with an empty snapshot. Lock-free throughout. Only
    /// the affected player is re-pushed.
    /// </summary>
    private void OnServerReceivedSetPin(IServerPlayer fromPlayer, ScribeSetPinMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("set-pin from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        Trace("set-pin received from {0}: pinned={1} doc={2} task={3}", fromPlayer.PlayerName, message.Pinned, docId, taskId);
        SetPinForPlayer(fromPlayer, docId, taskId, message.Pinned, message.SnapshotText, message.SnapshotDone);
    }

    private void OnServerReceivedCompleteTask(IServerPlayer fromPlayer, ScribeCompleteTaskMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("complete-task from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        // The completion policy is a client-local preference carried in the packet; normalize an
        // unknown/hostile byte back to the safe default before applying (Sink).
        var policy = ScribePlayerSettings.NormalizePolicy((ScribeCompletionPolicy)message.Policy);
        Trace("complete-task received from {0}: doc={1} task={2} policy={3}", fromPlayer.PlayerName, docId, taskId, policy);
        CompleteTaskForPlayer(fromPlayer, docId, taskId, policy);
    }

    private void OnServerReceivedEditPinnedTask(IServerPlayer fromPlayer, ScribeEditPinnedTaskMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("edit-pinned from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        Trace("edit-pinned received from {0}: doc={1} task={2}", fromPlayer.PlayerName, docId, taskId);
        EditPinnedTaskForPlayer(fromPlayer, docId, taskId, message.Text ?? "");
    }

    private void OnServerReceivedDeleteTask(IServerPlayer fromPlayer, ScribeDeleteTaskMessage message)
    {
        if (!TryReadGuid(message.DocId, out var docId) || !TryReadGuid(message.TaskId, out var taskId))
        {
            Trace("delete-task from {0}: MALFORMED packet (docId/taskId not 16 bytes) — ignored", fromPlayer.PlayerName);
            return;
        }
        Trace("delete-task received from {0}: doc={1} task={2}", fromPlayer.PlayerName, docId, taskId);
        DeleteTaskForPlayer(fromPlayer, docId, taskId);
    }

    private void OnServerReceivedReorderPins(IServerPlayer fromPlayer, ScribeReorderPinsMessage message)
    {
        // Validate the parallel id lists: both present, equal length, and bounded so a hostile/oversized
        // payload can't drive an unbounded permute. Each entry must be a well-formed 16-byte Guid pair;
        // any malformed/unknown entry is dropped by the store's reorder (unknown ids are ignored).
        var docIds = message.DocIds;
        var taskIds = message.TaskIds;
        if (docIds is null || taskIds is null || docIds.Count != taskIds.Count
            || docIds.Count > ScribePinCodec.MaxPinsPerPlayer)
        {
            Trace("reorder-pins from {0}: MALFORMED packet (null/mismatched/oversized id lists) — ignored", fromPlayer.PlayerName);
            return;
        }

        var order = new List<(Guid, Guid)>(docIds.Count);
        for (int i = 0; i < docIds.Count; i++)
        {
            if (TryReadGuid(docIds[i], out var docId) && TryReadGuid(taskIds[i], out var taskId))
            {
                order.Add((docId, taskId));
            }
        }
        Trace("reorder-pins received from {0}: {1} ordered ids", fromPlayer.PlayerName, order.Count);
        ReorderPinsForPlayer(fromPlayer, order);
    }

    /// <summary>
    /// Server-side pin/unpin, addressed by (DocId, TaskId). An UNPIN removes straight from the store
    /// with no block resolution, so it works when the owning lectern is broken or its chunk is
    /// unloaded. A PIN resolves the owning block via the live index only to snapshot the task's
    /// text/done from the server's own authoritative document (never a client-supplied snapshot); if
    /// the document can't be resolved right now the pin is still recorded with an empty snapshot.
    /// Lock-free throughout. Re-pushes the player when their set changed. Public so the block-entity
    /// layer and the integration suite drive the exact production path, not a copy of it.
    /// </summary>
    public void SetPinForPlayer(IServerPlayer player, Guid docId, Guid taskId, bool pinned,
        string? fallbackText = null, bool fallbackDone = false)
    {
        if (sapi is null || pinStore is null) return;

        bool changed;
        if (pinned)
        {
            string text = fallbackText ?? "";
            bool done = fallbackDone;
            // Prefer the server's own authoritative document when available; fall back to the
            // client-supplied snapshot for items whose host is not registered server-side (e.g. Notebooks).
            if (_hostRegistry.TryGetValue(docId, out var host)
                && host.Document.FindByTaskId(taskId) is { } block)
            {
                text = block.Text;
                done = block.Done;
            }
            changed = pinStore.SetPin(player.PlayerUID, docId, taskId, sapi.World.Calendar.TotalHours, text, done);
        }
        else
        {
            changed = pinStore.RemovePin(player.PlayerUID, docId, taskId);
        }

        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Server-side complete-a-task by identity (the read-view checkbox / HUD checkbox), for a task the
    /// player has pinned. The per-player pin store is authoritative for a pinned task's done-state, so
    /// the flow is store-first with write-through:
    /// <list type="number">
    /// <item><b>Toggle in the store</b> — flip the acting player's pin's done-state (the authoritative
    /// value), so completion works even when the source is unresolvable/destroyed.</item>
    /// <item><b>Write through to the source</b> — when the owning document resolves, set its task's done
    /// to match (reconciling ONLY the acting player; other players' pins are their own copies).</item>
    /// <item><b>Apply the completion policy</b> — <c>Sink</c> keeps the (now-done) pin; <c>Unpin</c>
    /// removes the pin; <c>Delete</c> removes the task from the source (when resolvable) and the pin.
    /// Removal/unpin fires only on a transition INTO done, so unchecking a done task never removes it.</item>
    /// </list>
    /// The <paramref name="policy"/> is the acting player's client-local completion preference, carried
    /// in the completion request and already normalized by the caller; it is no longer server-side
    /// state. Re-pushes the acting player once at the end when their set changed. Public for the same
    /// reason as <see cref="SetPinForPlayer"/> — the block-entity layer and the integration suite drive
    /// the exact production path.
    /// </summary>
    public void CompleteTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId,
        ScribeCompletionPolicy policy = ScribeCompletionPolicy.Sink)
    {
        if (sapi is null || pinStore is null) return;

        // The store owns the pinned task's done-state; toggle from there (not the possibly-gone source).
        bool? current = pinStore.GetPinDone(player.PlayerUID, docId, taskId);
        if (current is null)
        {
            // Not pinned by this player — a plain checkbox on an unpinned document task. Toggle the
            // shared document directly and apply the policy there too (the policy is not limited to
            // pinned tasks — scribe-lectern-view-consistency): Sink→bottom, Delete→remove.
            CompleteUnpinnedTaskAtSource(player, docId, taskId, policy);
            return;
        }
        bool nowDone = !current.Value;

        bool changed = pinStore.SetPinDone(player.PlayerUID, docId, taskId, nowDone);
        Trace("  complete: {0}'s pin on task {1} done {2} -> {3}", player.PlayerName, taskId, current.Value, nowDone);

        // Write through to the shared source document when it resolves (best-effort; a gone source just
        // skips this). Reconciles only the acting player — other pinners keep their own copies.
        bool resolved = TryResolveDocHost(docId, out var docHost, player);
        if (resolved) docHost!.SetTaskDoneFromReader(taskId, nowDone);

        // Apply the completion policy — only on a transition INTO done (unchecking never removes).
        if (nowDone)
        {
            switch (policy)
            {
                case ScribeCompletionPolicy.Unpin:
                    changed |= pinStore.RemovePin(player.PlayerUID, docId, taskId);
                    Trace("  policy Unpin: removed {0}'s pin on task {1}", player.PlayerName, taskId);
                    break;
                case ScribeCompletionPolicy.UnpinSink:
                    // Unpin (stay) + Sink: remove the pin AND move the task to the bottom of the source doc.
                    changed |= pinStore.RemovePin(player.PlayerUID, docId, taskId);
                    if (resolved && docHost!.MoveTaskToBottomFromReader(taskId))
                        Trace("  policy UnpinSink: removed pin and moved task {0} to bottom of source doc {1}", taskId, docId);
                    else
                        Trace("  policy UnpinSink: removed pin on task {0} (source unresolvable for sink)", taskId);
                    break;
                case ScribeCompletionPolicy.Delete:
                    if (resolved && docHost!.DeleteTaskFromReader(taskId))
                        Trace("  policy Delete: removed task {0} from source doc {1}", taskId, docId);
                    else
                        Trace("  policy Delete: source unresolvable for task {0} — pin removed only", taskId);
                    changed |= pinStore.RemovePin(player.PlayerUID, docId, taskId);
                    break;
                case ScribeCompletionPolicy.Sink:
                    // Sink is a REAL document reorder (scribe-lectern-view-consistency): move the task to
                    // the bottom of the shared source, so every viewer — read/editor/pinned view and the
                    // HUD — sees the same order. Keep the (now-done) pin. Best-effort: a gone source just
                    // leaves the pin done in the store (the HUD still sinks it client-side by done-state).
                    if (resolved && docHost!.MoveTaskToBottomFromReader(taskId))
                        Trace("  policy Sink: moved task {0} to bottom of source doc {1}", taskId, docId);
                    break;
                case ScribeCompletionPolicy.Keep:
                default:
                    // Keep leaves the (now-done) pin in place; nothing is removed or reordered.
                    break;
            }
        }

        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Server-side edit-a-pinned-task's-text by identity (the Pin Tab's inline text edit), for a task the
    /// player has pinned. Best-effort write-through, mirroring <see cref="CompleteTaskForPlayer"/>:
    /// <list type="number">
    /// <item><b>Write through to the source</b> — when the owning document resolves, set its task's text
    /// via the lock-free <see cref="BlockEntityScribeLectern.SetTaskTextFromReader"/> (which rejects a
    /// blank edit and reconciles nothing else).</item>
    /// <item><b>Update the pin snapshot</b> — refresh the acting player's pin's last-known text so the edit
    /// is reflected even when the source is unresolvable (snapshot-only degrade).</item>
    /// </list>
    /// Only the acting player is touched (their own pin is their own copy — grief-proof). A blank/
    /// whitespace-only edit is rejected end-to-end and changes nothing. Re-pushes the acting player when
    /// their set changed. Public so the integration suite drives the exact production path.
    /// </summary>
    public void EditPinnedTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId, string text)
    {
        if (sapi is null || pinStore is null) return;
        if (string.IsNullOrWhiteSpace(text)) return; // reject blank/whitespace-only end-to-end

        // Only edit through pins the player actually holds — an edit is a pin action, not a document RPC.
        if (pinStore.GetPinDone(player.PlayerUID, docId, taskId) is null)
        {
            Trace("  edit: {0} has no pin on task {1} — ignored", player.PlayerName, taskId);
            return;
        }

        // Write through to the shared source document when it resolves (best-effort; a gone source just
        // skips this — the snapshot below still updates).
        if (TryResolveDocHost(docId, out var docHost, player)) docHost!.SetTaskTextFromReader(taskId, text);

        // Always refresh the acting player's pin snapshot so the edit shows even if the source is unloaded.
        bool changed = pinStore.SetPinText(player.PlayerUID, docId, taskId, text);
        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Server-side standalone delete-a-task by identity (the Pin Tab's delete control), for a task the
    /// player has pinned. Mirrors the Delete completion policy's write-through, but as a first-class action
    /// independent of any policy: when the owning document resolves, remove the task lock-free via
    /// <see cref="BlockEntityScribeLectern.DeleteTaskFromReader"/>; always remove the acting player's pin
    /// (a safe no-op if it's already gone) and re-push. Snapshot/store-only when the source is unresolvable.
    /// Public so the integration suite drives the exact production path.
    /// </summary>
    public void DeleteTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId)
    {
        if (sapi is null || pinStore is null) return;

        if (TryResolveDocHost(docId, out var docHost, player) && docHost!.DeleteTaskFromReader(taskId))
            Trace("  delete: removed task {0} from source doc {1}", taskId, docId);
        else
            Trace("  delete: source unresolvable for task {0} — pin removed only", taskId);

        bool changed = pinStore.RemovePin(player.PlayerUID, docId, taskId);
        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Server-side reorder of the acting player's own pin list into a client-supplied order. Permutes ONLY
    /// that player's per-player list in <see cref="ScribePinStore"/> (unknown/duplicate ids ignored, omitted
    /// pins preserved), never any document's block order and never another player's list; the store already
    /// persists an ordered list under <c>scribe:pins:v1</c>, so persistence follows on the next world save.
    /// Re-pushes the acting player when the order actually changed. Public so the integration suite drives
    /// the exact production path.
    /// </summary>
    public void ReorderPinsForPlayer(IServerPlayer player, IReadOnlyList<(Guid DocId, Guid TaskId)> order)
    {
        if (sapi is null || pinStore is null) return;
        if (pinStore.ReorderPins(player.PlayerUID, order)) PushPinsTo(player);
    }

    /// <summary>Resolves a docId to an <see cref="IScribeDocumentHost"/>. Checks the registry first
    /// (covers Lecterns, which register server-side on Initialize). If not found, searches the acting
    /// player's inventory for a Notebook whose stored DocId matches and creates a transient host from
    /// that slot — Notebooks are only registered client-side, so the server must find them by scanning.</summary>
    private bool TryResolveDocHost(Guid docId, out IScribeDocumentHost? host,
        IServerPlayer? player = null)
    {
        if (_hostRegistry.TryGetValue(docId, out host)) return true;

        if (player is null || sapi is null) return false;

        foreach (var inv in player.InventoryManager.InventoriesOrdered)
        {
            foreach (var slot in inv)
            {
                if (slot.Itemstack?.Collectible is not ItemScribeNotebook) continue;
                if (!ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc) || doc is null) continue;
                if (doc.DocId != docId) continue;
                var nbHost = new NotebookHost(slot);
                nbHost.AttachServerContext(sapi, player);
                host = nbHost;
                return true;
            }
        }
        return false;
    }

    /// <summary>Completes (toggles) an UNPINNED document task straight on the shared source — a plain
    /// checkbox on a task nobody has pinned. There is no pin, so there is no store involvement, but the
    /// completion policy still applies to the document itself (scribe-lectern-view-consistency): on a
    /// transition INTO done, <c>Sink</c> moves the task to the document bottom and <c>Delete</c> removes
    /// it from the source; <c>Keep</c>/<c>Unpin</c> just toggle (there is nothing to unpin). A no-op when
    /// the source is unresolvable (nothing to toggle without a document).</summary>
    private void CompleteUnpinnedTaskAtSource(IServerPlayer player, Guid docId, Guid taskId,
        ScribeCompletionPolicy policy)
    {
        if (!TryResolveDocHost(docId, out var docHost, player))
        {
            Trace("  complete(unpinned): doc {0} unresolvable — nothing to toggle", docId);
            return;
        }
        var block = docHost!.Document.FindByTaskId(taskId);
        if (block is null || !block.IsTask)
        {
            Trace("  complete(unpinned): task {0} not found in doc {1}", taskId, docId);
            return;
        }
        bool nowDone = !block.Done;
        docHost.SetTaskDoneFromReader(taskId, nowDone);
        Trace("  complete(unpinned): task {0} toggled to done={1}", taskId, nowDone);

        // Apply the policy on the shared document — only on a transition INTO done (unchecking never moves
        // or removes). No pin to unpin here, so Unpin/Keep are plain toggles.
        if (nowDone)
        {
            switch (policy)
            {
                case ScribeCompletionPolicy.Sink:
                case ScribeCompletionPolicy.UnpinSink: // no pin to unpin for an unpinned task — just sink
                    if (docHost.MoveTaskToBottomFromReader(taskId))
                        Trace("  policy Sink(unpinned): moved task {0} to bottom of source doc {1}", taskId, docId);
                    break;
                case ScribeCompletionPolicy.Delete:
                    if (docHost.DeleteTaskFromReader(taskId))
                        Trace("  policy Delete(unpinned): removed task {0} from source doc {1}", taskId, docId);
                    break;
                case ScribeCompletionPolicy.Unpin:
                case ScribeCompletionPolicy.Keep:
                default:
                    break;
            }
        }
    }

    /// <summary>Re-push a single player their own full pin set (server → client). Called on join and
    /// after any change to that player's set. Only ever sends a player their own pins.</summary>
    public void PushPinsTo(IServerPlayer player)
    {
        if (sapi is null || pinStore is null) return;
        var bytes = ScribePinCodec.SerializeList(pinStore.Get(player.PlayerUID));
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribePinnedSetMessage { PinnedRefBytes = bytes }, player);
    }

    /// <summary>Re-push each listed player their own pin set. The block entity calls this after a
    /// snapshot refresh / orphan sweep affecting several players. A uid that isn't a currently-online
    /// player is skipped (their set is already persisted and will sync on their next join).</summary>
    public void PushPinsTo(IReadOnlyList<string> playerUids)
    {
        if (sapi is null) return;
        foreach (var uid in playerUids)
        {
            if (sapi.World.PlayerByUid(uid) is IServerPlayer player) PushPinsTo(player);
        }
    }

    private void OnServerReceivedRecordVisitor(IServerPlayer fromPlayer, ScribeRecordVisitorMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
            lectern.RecordVisitor(sapi, fromPlayer);
    }

    private void OnServerReceivedEditGuestbookNote(IServerPlayer fromPlayer, ScribeEditGuestbookNoteMessage message)
    {
        if (sapi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
            lectern.UpdateGuestbookNote(sapi, fromPlayer, message.Note ?? "");
    }

    private void OnClientReceivedGuestbookSync(ScribeGuestbookSyncMessage message)
    {
        if (capi is null) return;
        if (TryResolveHost(message.DocIdBytes) is BlockEntityScribeLectern lectern)
            lectern.ApplyGuestbookSync(message.GuestbookBytes);
    }

    private void OnServerReceivedNotebookSave(IServerPlayer fromPlayer, ScribeNotebookSaveMessage message)
    {
        if (sapi is null || !TryReadGuid(message.DocIdBytes, out var docId)) return;
        // The dialog closes (and flushes) the moment the notebook leaves the active hand slot,
        // so by the time this packet arrives the item should still be there.
        var slot = fromPlayer.Entity?.ActiveHandItemSlot;
        if (slot?.Itemstack?.Collectible is not ItemScribeNotebook) return;
        // Verify the packet's DocId matches the document already in the stack (if any).
        // A fresh stack with no prior save has no stored DocId yet — allow that write.
        if (ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var existing)
            && existing is not null && existing.DocId != docId)
            return;

        if (ScribeDocumentCodec.TryDeserialize(message.DocumentBytes, out var doc) && doc is not null)
        {
            ScribeDocumentAttributes.WriteTo(slot.Itemstack, doc);
            slot.MarkDirty();
            // Reconcile actor pins so pin snapshots stay fresh after a notebook edit.
            if (pinStore is { } store)
                PushPinsTo(store.ReconcileSnapshotsForActor(fromPlayer.PlayerUID, doc.DocId, doc));
        }

        // Echo back so the dialog's HandleServerReply can update the client's authoritative copy.
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = message.DocIdBytes,
            DocumentBytes = message.DocumentBytes,
        }, fromPlayer);
    }

    private void OnClientReceivedNotebookSave(ScribeNotebookSaveMessage message)
    {
        if (capi is null || !TryReadGuid(message.DocIdBytes, out var docId)) return;
        if (_hostRegistry.TryGetValue(docId, out var host) && host is NotebookHost notebookHost)
        {
            if (ScribeDocumentCodec.TryDeserialize(message.DocumentBytes, out var doc) && doc is not null)
                notebookHost.ApplyLocalOptimisticEdit(doc);
        }
    }

    /// <summary>
    /// Dev-only client command: <c>/scripttf &lt;target&gt; &lt;prop&gt; &lt;value&gt;</c>
    /// Mutates the held Notebook item's model transforms live so rotation/scale/translation
    /// can be tuned in-game. Prints the full current transform block after each change so
    /// the result can be pasted directly into scribenotebook.json.
    ///
    /// <c>target</c>: tp | ground | gui | fp
    /// <c>prop</c>:   rx | ry | rz | tx | ty | tz | scale
    /// </summary>
    private static void RegisterNotebookTuneCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("scripttf")
            .WithDescription("[scribe dev] Tune Notebook item model transforms live. Usage: /scripttf <tp|ground|gui|fp> <rx|ry|rz|tx|ty|tz|scale> <value>")
            .WithArgs(
                api.ChatCommands.Parsers.WordRange("target", "tp", "ground", "gui", "fp"),
                api.ChatCommands.Parsers.WordRange("prop", "rx", "ry", "rz", "tx", "ty", "tz", "scale"),
                api.ChatCommands.Parsers.Float("value"))
            .HandleWith(args =>
            {
                var slot = api.World.Player.InventoryManager.ActiveHotbarSlot;
                if (slot?.Itemstack?.Collectible is not ItemScribeNotebook item)
                    return TextCommandResult.Error("Hold the Notebook in your active hotbar slot first.");

                string target = (string)args[0];
                string prop   = (string)args[1];
                float  value  = (float)args[2];

                var tf = target switch
                {
                    "tp"     => item.TpHandTransform,
                    "ground" => item.GroundTransform,
                    "gui"    => item.GuiTransform,
                    "fp"     => item.FpHandTransform,
                    _        => null,
                };

                if (tf is null)
                    return TextCommandResult.Error($"Unknown target '{target}'.");

                switch (prop)
                {
                    case "rx":    tf.Rotation.X = value; break;
                    case "ry":    tf.Rotation.Y = value; break;
                    case "rz":    tf.Rotation.Z = value; break;
                    case "tx":    tf.Translation.X = value; break;
                    case "ty":    tf.Translation.Y = value; break;
                    case "tz":    tf.Translation.Z = value; break;
                    case "scale": tf.ScaleXYZ.Set(value, value, value); break;
                    default:      return TextCommandResult.Error($"Unknown prop '{prop}'.");
                }

                // Force a re-render of the held item.
                slot.MarkDirty();

                return TextCommandResult.Success(
                    $"[scribe] {target}: rotation=({tf.Rotation.X:0.##}, {tf.Rotation.Y:0.##}, {tf.Rotation.Z:0.##})  " +
                    $"translation=({tf.Translation.X:0.##}, {tf.Translation.Y:0.##}, {tf.Translation.Z:0.##})  " +
                    $"scale={tf.ScaleXYZ.X:0.##}");
            });
    }

    /// <summary>
    /// Server-side diagnostic log for the pin/complete round-trip, prefixed <c>[scribe]</c> at
    /// Notification level so a playtester can read the whole complete-vs-unpin flow back from the server
    /// log without a debug build. Scaffolding for the 7.8 part-d investigation (read-view completion not
    /// landing server-side); once that's confirmed fixed these can be dropped to VerboseDebug or removed.
    /// </summary>
    private void Trace(string format, params object?[] args)
    {
        sapi?.Logger.Notification("[scribe] " + format, args);
    }

    /// <summary>Reads a 16-byte array back into a Guid, defending the wire against a null/short payload
    /// (a malformed packet resolves nothing rather than throwing).</summary>
    private static bool TryReadGuid(byte[]? bytes, out Guid guid)
    {
        if (bytes is { Length: 16 })
        {
            guid = new Guid(bytes);
            return true;
        }
        guid = default;
        return false;
    }

    /// <summary>Resolves a DocId (carried as raw bytes in a packet) to its currently-registered
    /// <see cref="IScribeDocumentHost"/>. Returns null for a malformed/unknown DocId — the caller
    /// should silently discard the packet in that case.</summary>
    private IScribeDocumentHost? TryResolveHost(byte[]? docIdBytes)
    {
        if (!TryReadGuid(docIdBytes, out var docId)) return null;
        _hostRegistry.TryGetValue(docId, out var host);
        return host;
    }
}
