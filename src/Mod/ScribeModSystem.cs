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
    private const string PinStoreSaveKey   = "scribe:pins:v1";
    private const string TimerStoreSaveKey = "scribe:timer:v1";

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

    /// <summary>Server-side per-player timer store. Keyed by PlayerUID. Null on a pure client.</summary>
    private Dictionary<string, TimerStore>? timerStores;

    /// <summary>Server-side ticker id for the 1-second timer countdown. 0 when not registered.</summary>
    private long timerTickListenerId;
    private long timerDisplayTickId;

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

    /// <summary>Client → server: notify that the player just opened the notebook with this DocId, so
    /// the server can record their one-time PickedUp history entry (see
    /// <see cref="OnServerReceivedNotebookOpened"/>). No-op off the client. Called by both notebook
    /// items' open paths.</summary>
    public void NotifyServerNotebookOpened(Guid docId)
    {
        capi?.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookOpenedMessage
        {
            DocIdBytes = docId.ToByteArray(),
        });
    }

    /// <summary>Raised on the client whenever a fresh pin set push arrives, so an open lectern dialog
    /// (and the HUD) can repaint its per-player pin indicators.</summary>
    public event Action? MyPinsChanged;

    /// <summary>Client-side cache of THIS player's active timer state, populated by the server push.
    /// Null means no timer data has arrived yet (treat as Idle).</summary>
    public TimerStore? MyTimer { get; private set; }

    /// <summary>Raised on the client whenever the server pushes a new timer state, so the HUD and any
    /// open Clockmaker's Notebook dialog can update their timer display.</summary>
    public event Action? MyTimerChanged;

    /// <summary>Fired once per real second on the client from a single shared tick listener. The HUD
    /// timer row and the Notebook Timer tab's countdown both repaint off THIS event (not their own
    /// 250ms interpolation ticks) so their whole-second display advances in the exact same dispatch —
    /// otherwise two independent 250ms listeners detect the second-flip up to 250ms apart, which reads
    /// as visible drift when both are on screen. Each subscriber still interpolates its own remaining
    /// seconds at 250ms for accuracy; this only synchronizes the repaint.</summary>
    public event Action? TimerDisplayTick;

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
        api.RegisterItemClass("ItemClockmakerNotebook", typeof(ItemClockmakerNotebook));

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
            .RegisterMessageType<ScribeNotebookSaveMessage>()
            .RegisterMessageType<ScribeNotebookOpenedMessage>()
            .RegisterMessageType<ScribeSetTimerMessage>()
            .RegisterMessageType<ScribeClearTimerMessage>()
            .RegisterMessageType<ScribeTimerStateMessage>();
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
            .SetMessageHandler<ScribeNotebookSaveMessage>(OnClientReceivedNotebookSave)
            .SetMessageHandler<ScribeTimerStateMessage>(OnClientReceivedTimerState);

        // The pinned-task HUD self-shows once the player's pin set arrives (it subscribes to
        // MyPinsChanged in its ctor), so it can be constructed here regardless of current pin count —
        // it stays closed until there is ≥1 pin. It owns its own subscription + tick; we dispose it.
        pinHud = new HudScribePins(api, this);

        // Single shared 1Hz display tick: drives BOTH the HUD timer row and the Notebook Timer tab
        // countdown so they repaint on the same dispatch (see TimerDisplayTick). One listener, so there
        // is one authoritative second-boundary rather than two out-of-phase 250ms listeners.
        timerDisplayTickId = api.World.RegisterGameTickListener(_ => TimerDisplayTick?.Invoke(), 1000);

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
        if (timerDisplayTickId != 0 && capi is not null)
        {
            capi.World.UnregisterGameTickListener(timerDisplayTickId);
            timerDisplayTickId = 0;
        }
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
        RegisterSvgIcon(api, "scribeguest",   new AssetLocation("scribe", "textures/icons/guestbook.svg"));
        RegisterSvgIcon(api, "scribehistory", new AssetLocation("scribe", "textures/icons/guestbook.svg"));
        RegisterSvgIcon(api, "scribetimer",   new AssetLocation("scribe", "textures/icons/timer.svg"));
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
        channel.SetMessageHandler<ScribeNotebookOpenedMessage>(OnServerReceivedNotebookOpened);
        channel.SetMessageHandler<ScribeSetTimerMessage>(OnServerReceivedSetTimer);
        channel.SetMessageHandler<ScribeClearTimerMessage>(OnServerReceivedClearTimer);

        // Persist/load the pin + settings stores with the save game (the WaypointMapLayer pattern).
        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.GameWorldSave += OnGameWorldSave;

        // Initial per-player push once a player is fully in-world, and legacy-pin drain.
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;

        // History chronicle hooks.
        api.Event.OnEntityDeath += OnEntityDeath;
        api.World.RegisterGameTickListener(OnStormTick, 5000);

        // Timer countdown: 1 s tick for all running/fired player timers.
        timerStores = new Dictionary<string, TimerStore>();
        timerTickListenerId = api.World.RegisterGameTickListener(OnTimerTick, 1000);

        ApplyClockmakerTraitGate(api);

        RegisterSeedCommand(api);
    }

    /// <summary>
    /// Clockmaker's Notebook crafting is gated by the vanilla <c>tinkerer</c> trait via the recipe's
    /// native <c>requiresTrait</c> field (enforced data-only by the survival mod's CharacterSystem).
    /// A server operator can disable that requirement world-wide with the
    /// <c>scribeClockmakerRequiresTrait</c> worldconfig boolean (default: enforced). When disabled we
    /// null out <see cref="GridRecipe.RequiresTrait"/> on the loaded recipe(s) so the game matches for
    /// every player — this is the reliable bypass (a second MatchesGridRecipe handler is last-writer-wins
    /// and cannot dependably override the survival mod's deny). Runs in StartServerSide, which is after
    /// grid recipes register and after World.Config is populated from the savegame.
    /// </summary>
    private static void ApplyClockmakerTraitGate(ICoreServerAPI api)
    {
        // Always pass an explicit default: worlds created before this key existed won't have it baked
        // into the savegame, and GetBool does not consult the registered attribute default at read time.
        bool requireTrait = api.World.Config.GetBool("scribeClockmakerRequiresTrait", true);
        if (requireTrait) return;

        int cleared = 0;
        foreach (var recipe in api.World.GridRecipes)
        {
            if (recipe.Output?.Code?.Path == "scribeclockmakernotebook" && recipe.RequiresTrait is not null)
            {
                recipe.RequiresTrait = null;
                cleared++;
            }
        }

        if (cleared > 0)
        {
            api.Logger.Notification(
                "[scribe] scribeClockmakerRequiresTrait disabled: cleared the tinkerer trait requirement on {0} Clockmaker's Notebook recipe(s).",
                cleared);
        }
    }

    private void OnSaveGameLoaded()
    {
        if (sapi is null || pinStore is null) return;
        var pinBytes = sapi.WorldManager.SaveGame.GetData(PinStoreSaveKey);
        pinStore.LoadFrom(pinBytes);

        if (timerStores is not null)
        {
            timerStores.Clear();
            var timerBytes = sapi.WorldManager.SaveGame.GetData(TimerStoreSaveKey);
            if (timerBytes is not null)
            {
                try
                {
                    using var ms = new System.IO.MemoryStream(timerBytes, writable: false);
                    using var r  = new System.IO.BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        string uid   = r.ReadString();
                        int    len   = r.ReadInt32();
                        byte[] blob  = r.ReadBytes(len);
                        var    store = TimerStore.Deserialize(blob);
                        // Resume Running timers AND fired-but-undismissed timers. A Fired timer carries its
                        // FiredElapsedSeconds (codec v2), so its client-driven auto-disappear resumes the
                        // remaining window rather than restarting a fresh 30 s — see the matching filter in
                        // OnGameWorldSave. (A v1 save never persisted Fired timers, so no legacy Fired blob
                        // with elapsed=0 can flash a full window on load.)
                        if (store.Status == TimerStatus.Running && store.RemainingSeconds > 0)
                            timerStores[uid] = store;
                        else if (store.Status == TimerStatus.Fired)
                            timerStores[uid] = store;
                    }
                }
                catch { /* Malformed — start fresh. */ }
            }
        }
    }

    private void OnGameWorldSave()
    {
        if (sapi is null || pinStore is null) return;
        sapi.WorldManager.SaveGame.StoreData(PinStoreSaveKey, pinStore.SerializePins());

        if (timerStores is not null)
        {
            // Persist Running AND fired-but-undismissed timers. A fired timer is a notification the player
            // may not have acknowledged yet; dropping it on save would silently lose it across a relog.
            // Its FiredElapsedSeconds (codec v2) is persisted with it, so the client-driven auto-disappear
            // resumes the remaining window on rejoin rather than restarting a fresh 30 s
            // (timer-auto-disappear-setting). Idle timers are still dropped.
            var persisted = timerStores
                .Where(kv => kv.Value.Status is TimerStatus.Running or TimerStatus.Fired)
                .ToList();
            if (persisted.Count > 0)
            {
                using var ms = new System.IO.MemoryStream();
                using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    w.Write(persisted.Count);
                    foreach (var (uid, store) in persisted)
                    {
                        var blob = store.Serialize();
                        w.Write(uid);
                        w.Write(blob.Length);
                        w.Write(blob);
                    }
                }
                sapi.WorldManager.SaveGame.StoreData(TimerStoreSaveKey, ms.ToArray());
            }
            else
            {
                sapi.WorldManager.SaveGame.StoreData(TimerStoreSaveKey, System.Array.Empty<byte>());
            }
        }
    }

    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        PushPinsTo(player);
        PushTimerTo(player);
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
            IEnumerable<ItemSlot>? slots;
            try { slots = new List<ItemSlot>(inv); }
            catch { continue; }

            foreach (var slot in slots)
            {
                if (slot is null) continue;
                if (slot.Itemstack?.Collectible is not (ItemScribeNotebook or ItemClockmakerNotebook)) continue;
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
        // so by the time this packet arrives the item should still be there. Both notebook item
        // classes flush through this handler, so accept the Clockmaker's Notebook too — otherwise
        // its task/note edits are silently dropped server-side.
        var slot = fromPlayer.Entity?.ActiveHandItemSlot;
        if (slot?.Itemstack?.Collectible is not (ItemScribeNotebook or ItemClockmakerNotebook)) return;
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

    /// <summary>Client → server: the player opened a Notebook. Resolve the held notebook host, which
    /// (via <see cref="NotebookHost.AttachServerContext"/>) records this player's one-time PickedUp
    /// entry — deduplicated per actor in <c>HistoryStore.TryAddEntry</c>, and skipped for the crafter,
    /// whose Crafted entry already stands in for their acquisition. Opening the dialog is otherwise a
    /// client-only action the server never sees, so without this signal no PickedUp entry is recorded
    /// (the historical gap: the recorder only ever ran on a task pin/complete round-trip or a death).</summary>
    private void OnServerReceivedNotebookOpened(IServerPlayer fromPlayer, ScribeNotebookOpenedMessage message)
    {
        if (sapi is null || !TryReadGuid(message.DocIdBytes, out var docId)) return;
        // TryResolveDocHost scans the player's inventory for the matching notebook and, on a hit,
        // calls AttachServerContext → RecordPickedUpIfNew. We don't need the host here — resolving it
        // is the whole point (the PickedUp side effect).
        TryResolveDocHost(docId, out _, fromPlayer);
    }

    private void OnClientReceivedNotebookSave(ScribeNotebookSaveMessage message)
    {
        if (capi is null || !TryReadGuid(message.DocIdBytes, out var docId)) return;
        if (_hostRegistry.TryGetValue(docId, out var host) && host is NotebookHost notebookHost)
        {
            if (message.DocumentBytes is not null
                && ScribeDocumentCodec.TryDeserialize(message.DocumentBytes, out var doc) && doc is not null)
                notebookHost.ApplyLocalOptimisticEdit(doc);
            if (message.HistoryBytes is not null)
            {
                notebookHost.ApplyHistoryUpdate(message.HistoryBytes);
                // Refresh the History tab if it's currently open.
                if (capi.Gui.OpenedGuis.OfType<GuiDialogScribeNotebook>()
                        .FirstOrDefault(d => d.IsOpened()) is { } dialog)
                    dialog.RefreshHistoryView();
            }
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

    // ── Demo-content seeding (dev/creative tool) ────────────────────────────────────────────────────

    /// <summary>Fictional visitor names for seeded Lectern guestbooks. Kept ≤16 chars each so they read
    /// as plausible player names in screenshots.</summary>
    private static readonly string[] SeedVisitorNames =
    {
        "Alrik", "Brenna", "Cael", "Dagny", "Emeric", "Fenna", "Gorm", "Hilde",
    };

    /// <summary>Sample tasks for a seeded document — a believable mix of done/undone chores and goals.</summary>
    private static readonly (string Text, bool Done)[] SeedTasks =
    {
        ("Smelt copper for the new forge",        true),
        ("Trade furs at the trader up the coast",  true),
        ("Repair the north wall breach",           true),
        ("Plant flax in the east field",           false),
        ("Brew a batch of cheese",                 false),
        ("Find a temporal gear for the mechanism", false),
        ("Stock up on arrows before the storm",    false),
        ("Map the cave system below the cellar",   false),
        ("Tame a pair of hens",                    true),
        ("Cook a meal for the feast",              false),
        ("Reinforce the cellar door",              false),
        ("Chart the road to the ruins",            false),
    };

    /// <summary>Sample note sections for a seeded document.</summary>
    private static readonly string[] SeedNotes =
    {
        "The trader north of here pays best for hides — bring at least a dozen.",
        "Storm season is close. Keep temporal gears and cured meat stocked.",
    };

    /// <summary>Registers the server-side dev command <c>/scribe seed &lt;what&gt; [target]</c>, which
    /// populates a target Notebook or looked-at Lectern with believable demo content (tasks, notes,
    /// History on notebooks, Guestbook on lecterns) for screenshot/video capture. All three stores are
    /// server-authoritative, so this must be a server command. Double-gated: the <c>controlserver</c>
    /// privilege plus an in-handler creative-mode check. History seeds only the Notebook and Guestbook
    /// only the Lectern (they are hosted asymmetrically); inapplicable combinations are skipped and
    /// reported, never errored (design decisions 1–3).</summary>
    private void RegisterSeedCommand(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("scribe")
            .WithDescription("[scribe dev] Scribe developer commands.")
            .RequiresPrivilege(Privilege.controlserver)
            .RequiresPlayer()
            .BeginSubCommand("seed")
                .WithDescription("[scribe dev] Seed demo content into a Notebook or looked-at Lectern. " +
                    "Usage: /scribe seed <tasks|notes|history|guestbook|all> [notebook|lectern]")
                .WithArgs(
                    parsers.WordRange("what", "tasks", "notes", "history", "guestbook", "all"),
                    parsers.OptionalWordRange("target", "notebook", "lectern"))
                .HandleWith(OnSeedCommand)
            .EndSubCommand();
    }

    private TextCommandResult OnSeedCommand(TextCommandCallingArgs args)
    {
        if (sapi is null) return TextCommandResult.Error("Server not ready.");
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("This command must be run by a player.");
        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            return TextCommandResult.Error("/scribe seed is only available in creative mode.");

        string what   = (string)args[0];
        string target = args[1] as string ?? "auto";

        // Resolve the seed target: an explicit lectern/notebook, or auto (looked-at lectern else held notebook).
        var lectern = ResolveLookedAtLectern(player);
        NotebookHost? notebook = null;

        bool useLectern;
        switch (target)
        {
            case "lectern":
                if (lectern is null)
                    return TextCommandResult.Error("Look at a Scribe Lectern to seed it.");
                useLectern = true;
                break;
            case "notebook":
                notebook = FindNotebookInInventory(player);
                if (notebook is null)
                    return TextCommandResult.Error("Hold a Notebook (or Clockmaker's Notebook) to seed it.");
                useLectern = false;
                break;
            default: // auto
                if (lectern is not null)
                {
                    useLectern = true;
                }
                else
                {
                    notebook = FindNotebookInInventory(player);
                    if (notebook is null)
                        return TextCommandResult.Error(
                            "No target: look at a Scribe Lectern, or hold a Notebook, then run /scribe seed again.");
                    useLectern = false;
                }
                break;
        }

        return useLectern
            ? SeedLectern(lectern!, what)
            : SeedNotebook(notebook!, what);
    }

    /// <summary>Auto-target helper: the <see cref="BlockEntityScribeLectern"/> the player is currently
    /// looking at, or null. Mirrors the block-selection lookup in <see cref="BlockScribeLectern"/>.</summary>
    private BlockEntityScribeLectern? ResolveLookedAtLectern(IServerPlayer player)
    {
        var pos = player.CurrentBlockSelection?.Position;
        if (pos is null || sapi is null) return null;
        return sapi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityScribeLectern;
    }

    private TextCommandResult SeedNotebook(NotebookHost host, string what)
    {
        if (what == "guestbook")
            return TextCommandResult.Error("A Notebook has no Guestbook — that is a Lectern feature.");

        bool seedTasks = what is "tasks" or "all";
        // "all" intentionally excludes notes (only the explicit "notes" target seeds them) — a seeded
        // demo document should show tasks, not note sections.
        bool seedNotes = what is "notes";
        bool seedHistory = what is "history" or "all";

        if (seedTasks) SeedDocumentTasks(host.Document);
        if (seedNotes) SeedDocumentNotes(host.Document);
        if (seedHistory) SeedHistory(host.History);

        host.Flush();

        var did = new List<string>();
        if (seedTasks) did.Add("tasks");
        if (seedNotes) did.Add("notes");
        if (seedHistory) did.Add("history");
        return TextCommandResult.Success($"[scribe] Seeded Notebook: {string.Join(", ", did)}.");
    }

    private TextCommandResult SeedLectern(BlockEntityScribeLectern lectern, string what)
    {
        bool seedTasks = what is "tasks" or "all";
        // "all" intentionally excludes notes (only the explicit "notes" target seeds them) — see SeedNotebook.
        bool seedNotes = what is "notes";
        bool seedGuestbook = what is "guestbook" or "all";
        bool wantHistory = what == "history";
        if (wantHistory)
            return TextCommandResult.Error("A Lectern has no History — that is a Notebook feature.");

        if (seedTasks) SeedDocumentTasks(lectern.Document);
        if (seedNotes) SeedDocumentNotes(lectern.Document);
        // Persist the document edits (guestbook seeds itself + marks dirty separately).
        if (seedTasks || seedNotes) lectern.MarkDirty(redrawOnClient: true);
        if (seedGuestbook) SeedGuestbookOn(lectern);

        var did = new List<string>();
        if (seedTasks) did.Add("tasks");
        if (seedNotes) did.Add("notes");
        if (seedGuestbook) did.Add("guestbook");
        return TextCommandResult.Success(
            $"[scribe] Seeded Lectern: {string.Join(", ", did)}. Reopen the lectern to see it.");
    }

    private static void SeedDocumentTasks(Scribe.Core.ScribeDocument doc)
    {
        foreach (var (text, done) in SeedTasks)
        {
            doc.AddTask(text);
            if (done)
            {
                // The task was appended last; flip its done flag via its index.
                doc.ToggleTask(doc.Blocks.Count - 1);
            }
        }
    }

    private static void SeedDocumentNotes(Scribe.Core.ScribeDocument doc)
    {
        foreach (var note in SeedNotes)
            doc.AddTextSection(note);
    }

    /// <summary>Seeds a spread of History entries dated across recent in-game days so the History tab
    /// reads like a lived-in chronicle rather than a single-day dump.</summary>
    private void SeedHistory(Scribe.Core.HistoryStore history)
    {
        if (sapi is null) return;
        string date(int daysAgo) => NotebookHost.FormatDateDaysAgo(sapi, daysAgo);

        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Crafted, InGameDate = date(14),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.PickedUp, ActorName = "Alrik", InGameDate = date(14),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.TemporalStorm, Detail = "Medium", InGameDate = date(11),
        });
        // Every combat entry carries its whole sentence in Detail (ActorName empty) so the History
        // row does not prepend "Name — " and print the name twice — same convention as BossKill and
        // the live OnEntityDeath path. Two PvP entries (bow death, sword kill) showcase both weapon
        // tiers, and two mob deaths (Nightmare Drifter, brown bear) showcase the flavored creature
        // pool with correct variant names. All reuse the live lang keys, so the demo can never drift.
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Detail = SeedMobDeathMessage(victim: "Alrik", creatureCode: "drifter-nightmare"), InGameDate = date(12),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Detail = SeedPvpDeathMessage(killer: "Gorm", weaponTool: "bow", victim: "Alrik"), InGameDate = date(9),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Detail = SeedMobDeathMessage(victim: "Alrik", creatureCode: "bear-brown-adult-male"), InGameDate = date(7),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.BossKill,
            Detail = Lang.Get("scribe:scribe-history-boss-eidolon", "Alrik"), InGameDate = date(6),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.TemporalStorm, Detail = "Heavy", InGameDate = date(4),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.PvpKill,
            Detail = SeedPvpKillMessage(killer: "Alrik", weaponTool: "sword", victim: "Gorm"), InGameDate = date(2),
        });
    }

    /// <summary>Builds a seeded PvP DEATH message (victim-first passive, for a victim's Death entry)
    /// from the same lang keys the live path uses — the <c>scribe:scribe-pvp-verb-tool-&lt;tool&gt;</c>
    /// verb's passive participle assembled into <c>scribe:scribe-pvp-death-message</c> — so demo
    /// content can never drift from real wording. <paramref name="weaponTool"/> is a lowercased
    /// <c>EnumTool</c> name (e.g. "bow"); no live entity is needed since we name the weapon category
    /// directly. Mirrors the live <see cref="VerbParticiple"/> death branch in <c>OnEntityDeath</c>.</summary>
    private static string SeedPvpDeathMessage(string killer, string weaponTool, string victim)
    {
        string verbKey = $"scribe:scribe-pvp-verb-tool-{weaponTool}";
        return Lang.Get("scribe:scribe-pvp-death-message", victim, VerbParticiple(verbKey), killer);
    }

    /// <summary>Builds a seeded PvP KILL message (killer-first active, for a killer's PvpKill entry).
    /// Companion to <see cref="SeedPvpDeathMessage"/>; mirrors the live <see cref="VerbActive"/> kill
    /// branch in <c>OnEntityDeath</c>.</summary>
    private static string SeedPvpKillMessage(string killer, string weaponTool, string victim)
    {
        string verbKey = $"scribe:scribe-pvp-verb-tool-{weaponTool}";
        return Lang.Get("scribe:scribe-pvp-kill-message", killer, VerbActive(verbKey), victim);
    }

    /// <summary>Builds a seeded mob-death message from the same <c>scribe:scribe-mob-death-N</c> pool
    /// the live <see cref="BuildDeathMessage"/> path uses. The creature is named from vanilla's own
    /// <c>prefixandcreature-&lt;code&gt;</c> key (matching <c>Entity.GetPrefixAndCreatureName()</c>),
    /// so the seed reads with the correct variant ("a nightmare drifter", "a brown bear") without a
    /// live entity. The pool index is derived from the creature code so the demo is stable per run.</summary>
    private static string SeedMobDeathMessage(string victim, string creatureCode)
    {
        string creature = Lang.GetMatching($"game:prefixandcreature-{creatureCode}");
        int poolSize = 0;
        while (Lang.Get($"scribe:scribe-mob-death-{poolSize}") != $"scribe:scribe-mob-death-{poolSize}") poolSize++;
        int idx = poolSize > 0 ? Math.Abs(creatureCode.GetHashCode()) % poolSize : 0;
        return Lang.Get($"scribe:scribe-mob-death-{idx}", victim, creature);
    }

    /// <summary>Seeds fictional guestbook visitors (some with short notes) on a lectern, dated across
    /// recent in-game days via the server-only <see cref="BlockEntityScribeLectern.SeedGuestbook"/> seam.</summary>
    private void SeedGuestbookOn(BlockEntityScribeLectern lectern)
    {
        if (sapi is null) return;
        var notes = new[] { "Fine work on the roof!", "Left three loaves in the chest.", null, "Back next season." };
        var entries = new List<(string, string, string?)>();
        for (int i = 0; i < SeedVisitorNames.Length; i++)
        {
            entries.Add((SeedVisitorNames[i], NotebookHost.FormatDateDaysAgo(sapi, (SeedVisitorNames.Length - i) * 2),
                i < notes.Length ? notes[i] : null));
        }
        lectern.SeedGuestbook(entries);
    }

    // ── Timer ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Server → client: push the current timer state to the player.</summary>
    private void PushTimerTo(IServerPlayer player)
    {
        if (sapi is null || timerStores is null) return;
        var store = timerStores.TryGetValue(player.PlayerUID, out var s) ? s : new TimerStore();
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeTimerStateMessage
        {
            Status              = store.Status,
            Mode                = store.Mode,
            Label               = store.Label,
            RemainingSeconds    = store.RemainingSeconds,
            FiredElapsedSeconds = store.FiredElapsedSeconds,
        }, player);
    }

    private void OnServerReceivedSetTimer(IServerPlayer fromPlayer, ScribeSetTimerMessage message)
    {
        if (sapi is null || timerStores is null) return;
        if (message.DurationSeconds <= 0) return;

        timerStores[fromPlayer.PlayerUID] = new TimerStore
        {
            Status           = TimerStatus.Running,
            Mode             = message.Mode,
            Label            = message.Label ?? "",
            RemainingSeconds = message.DurationSeconds,
        };
        PushTimerTo(fromPlayer);
    }

    private void OnServerReceivedClearTimer(IServerPlayer fromPlayer, ScribeClearTimerMessage _)
    {
        if (sapi is null || timerStores is null) return;
        timerStores.Remove(fromPlayer.PlayerUID);
        PushTimerTo(fromPlayer);
    }

    private void OnClientReceivedTimerState(ScribeTimerStateMessage message)
    {
        MyTimer = new TimerStore
        {
            Status              = message.Status,
            Mode                = message.Mode,
            Label               = message.Label ?? "",
            RemainingSeconds    = message.RemainingSeconds,
            FiredElapsedSeconds = message.FiredElapsedSeconds,
        };
        MyTimerChanged?.Invoke();
        // Refresh the Timer tab in any open Clockmaker's Notebook dialog.
        if (capi is not null)
        {
            foreach (var dialog in capi.Gui.OpenedGuis.OfType<GuiDialogClockmakerNotebook>())
                if (dialog.IsOpened()) dialog.RefreshTimerView();
        }
    }

    /// <summary>1-second server tick: decrement running timers and fire at zero. A timer's
    /// <c>RemainingSeconds</c> is stored in the unit the player entered. In RealTime mode it counts down
    /// one-per-real-second; in InGame mode it drains at the world's in-game time rate, so an entered
    /// in-game duration fires when that much in-game time has actually passed (≈30× faster than real time
    /// by default). This also means InGame timers pause exactly when the world does.
    ///
    /// <para>The server does NOT auto-clear a fired timer: the 30 s auto-disappear is governed by the
    /// player's client-local <see cref="ScribePlayerSettings.TimerAutoDisappear"/> preference, which only
    /// the client knows, so the client drives the clear (timer-auto-disappear-setting). The server merely
    /// accumulates <see cref="TimerStore.FiredElapsedSeconds"/> on the fired store so the flash window is
    /// persisted and resumes (not restarts) across a relog.</para></summary>
    private void OnTimerTick(float _)
    {
        if (sapi is null || timerStores is null) return;

        double inGameRate = ScribeTimeRate.InGamePerReal(sapi);

        foreach (var (uid, store) in timerStores)
        {
            var player = sapi.World.PlayerByUid(uid) as IServerPlayer;

            if (store.Status == TimerStatus.Running)
            {
                store.RemainingSeconds -= store.Mode == TimerMode.InGame ? inGameRate : 1.0;
                if (store.RemainingSeconds <= 0)
                {
                    store.RemainingSeconds = 0;
                    store.Status = TimerStatus.Fired;
                    store.FiredElapsedSeconds = 0;
                }
                if (player is not null) PushTimerTo(player);
            }
            else if (store.Status == TimerStatus.Fired)
            {
                // Keep the persisted fired-elapsed advancing (real seconds — the flash window is real-time
                // regardless of the timer's countdown mode). No auto-removal here: the client sends the
                // clear when its "Timer disappears" preference is on and the window elapses.
                store.FiredElapsedSeconds += 1.0;
            }
        }
    }

    // ── History chronicle ────────────────────────────────────────────────────────────────────────

    private bool _stormWasActive;

    /// <summary>Known boss entity code prefixes and the lang key of the narrative line recorded for the
    /// BossKill event. Checked via entity.Code.Path.StartsWith so variant suffixes (-pristine, -corrupted,
    /// etc.) match. The lang string takes the slayer's name as {0} and becomes the entry's Detail (the
    /// whole descriptive sentence), so ActorName is left empty for boss kills — see OnEntityDeath.
    ///
    /// MORE BOSSES ARE EXPECTED. Each boss needs its OWN narrative sentence — the current lines are
    /// boss-specific ("descended into darkness", "climbed the tower"), not a fill-in-the-blank template.
    /// To add one: (1) add a `scribe-history-boss-&lt;name&gt;` key to lang/en.json with the full sentence
    /// and a {0} for the slayer, (2) add a `(prefix, "scribe:scribe-history-boss-&lt;name&gt;")` row here.
    /// No other code changes are needed.</summary>
    private static readonly (string Prefix, string LangKey)[] BossTable =
    {
        ("eidolon", "scribe:scribe-history-boss-eidolon"),
        ("erel",    "scribe:scribe-history-boss-erel"),
    };

    /// <summary>The inventory <see cref="Vintagestory.API.Common.InventoryBase.ClassName"/>s a
    /// notebook counts as "carried on the player's person" for history recording: the hotbar, the
    /// backpack bags, worn character/clothing slots, and the mouse-cursor drag slot (a real held
    /// stack while a GUI is open). Deliberately EXCLUDES the creative inventory
    /// (<c>creativeInvClassName</c>) — it holds infinite *template* stacks, and writing history into
    /// one mutates the template so every future copy carries phantom entries (the observed
    /// "new notebook auto-populates past kills" bug) — as well as the transient <c>ground</c> and
    /// <c>craftinggrid</c> staging inventories, which are not "on your person". Names come from
    /// <see cref="Vintagestory.API.Config.GlobalConstants"/>.</summary>
    private static readonly HashSet<string> CarriedInventoryClasses = new()
    {
        GlobalConstants.hotBarInvClassName,      // "hotbar"
        GlobalConstants.backpackInvClassName,    // "backpack"
        GlobalConstants.characterInvClassName,   // "character"
        GlobalConstants.mousecursorInvClassName, // "mouse"
    };

    /// <summary>Yields a server-attached <see cref="NotebookHost"/> for EVERY Notebook stack the
    /// player is carrying on their person (see <see cref="CarriedInventoryClasses"/>), so a live
    /// history event (death, storm, boss kill) is recorded on ALL of them, not just the first found.
    /// Matches BOTH <see cref="ItemScribeNotebook"/> and its sibling <see cref="ItemClockmakerNotebook"/>
    /// — both carry a document + history store. Scoped to real carried inventories on purpose: the
    /// old "walk InventoriesOrdered, return the first match" logic also walked the CREATIVE inventory
    /// (whose template stacks it then mutated) and the ground/crafting staging inventories, so in a
    /// creative world the killer's real notebook got nothing while a creative-tab template silently
    /// accumulated the kills.</summary>
    private IEnumerable<NotebookHost> FindCarriedNotebooks(IServerPlayer player)
    {
        if (sapi is null) yield break;
        foreach (var inv in player.InventoryManager.InventoriesOrdered)
        {
            if (!CarriedInventoryClasses.Contains(inv.ClassName)) continue;
            foreach (var slot in inv)
            {
                if (slot.Itemstack?.Collectible is not (ItemScribeNotebook or ItemClockmakerNotebook)) continue;
                var host = new NotebookHost(slot);
                host.AttachServerContext(sapi, player);
                yield return host;
            }
        }
    }

    /// <summary>Convenience wrapper: the first carried Notebook, or null. Used where a single target
    /// is wanted (the demo seeder, and the killer-notebook lookup whose PvpKill entry is a single
    /// record). Live recorders that must fan out to every notebook use
    /// <see cref="FindCarriedNotebooks"/> directly.</summary>
    private NotebookHost? FindNotebookInInventory(IServerPlayer player)
        => FindCarriedNotebooks(player).FirstOrDefault();

    private void OnEntityDeath(Vintagestory.API.Common.Entities.Entity entity, Vintagestory.API.Common.DamageSource dmg)
    {
        if (sapi is null) return;

        // ── Boss kill ──
        foreach (var (prefix, langKey) in BossTable)
        {
            if (!entity.Code.Path.StartsWith(prefix)) continue;
            var deathPos = entity.Pos.XYZ;
            foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
            {
                double dist = player.Entity.Pos.XYZ.DistanceTo(deathPos);
                if (dist > 100) continue;
                // Record on EVERY notebook the player carries, not just the first found.
                foreach (var host in FindCarriedNotebooks(player))
                {
                    // The whole descriptive sentence lives in Detail (ActorName empty); the History row
                    // shows Detail alone when ActorName is empty, so no "Name — " prefix is prepended.
                    host.History.TryAddEntry(new Scribe.Core.HistoryEntry
                    {
                        Kind       = Scribe.Core.HistoryEventKind.BossKill,
                        Detail     = Lang.Get(langKey, player.PlayerName),
                        InGameDate = NotebookHost.FormatDate(sapi),
                    });
                    host.FlushHistory();
                }
            }
            return;
        }

        // ── Player death ──
        if (entity is not Vintagestory.API.Common.EntityPlayer ep) return;
        if (ep.Player is not IServerPlayer sp) return;

        // Resolve the attacker via GetCauseEntity() (CauseEntity ?? SourceEntity) so melee kills
        // are attributed — SourceEntity is null for melee, which is the common PvP case. A single
        // "attacker is a different player" predicate drives both the victim's message and the
        // killer's PvpKill entry, so both symptoms are fixed by one condition.
        IServerPlayer? killer = null;
        // Materialize the killer's carried notebooks once so we can both (a) index the generic-verb
        // pool off one of them and (b) record the PvpKill on ALL of them.
        List<NotebookHost> killerHosts = new();
        if (dmg?.GetCauseEntity() is Vintagestory.API.Common.EntityPlayer killerEntity
            && killerEntity.Player is IServerPlayer k && k.PlayerUID != sp.PlayerUID)
        {
            killer      = k;
            killerHosts = FindCarriedNotebooks(k).ToList();
        }

        // A PvP death names the killer with a weapon-aware verb; any other death reconstructs a
        // full narrated sentence (mob-death flavor pool, else vanilla environmental deathmsg). Every
        // branch produces a self-contained sentence that already names the victim, so the entry
        // leaves ActorName empty and puts the whole sentence in Detail — the History row prepends
        // "ActorName — " otherwise, which would print the player's name twice (see the BossKill
        // path, which is empty-ActorName for the same reason).
        //
        // For PvP, each notebook reads from ITS OWN owner's perspective, so the two logs diverge:
        //   • the victim's Death log is victim-first & passive:  "Junkmuffin was slain by Raptor."
        //   • the killer's PvpKill log is killer-first & active: "Raptor slew Junkmuffin."
        // Both come from the same resolved verb key (active verb vs. its passive participle).
        string deathMsg;   // victim-first — the victim's Death entry
        string? killMsg = null; // killer-first — the killer's PvpKill entry (PvP only)
        if (killer is not null)
        {
            // The generic-pool cursor reads the killer's existing PvpKill count; use their first
            // notebook as the reference. (Different carried notebooks may hold different counts, but
            // the verb is cosmetic flavor — one reference is fine, and all get the same final line.)
            string verbKey = ResolvePvpVerbKey((Vintagestory.API.Common.EntityPlayer)dmg!.GetCauseEntity(), dmg, killerHosts.FirstOrDefault());
            deathMsg = Lang.Get("scribe:scribe-pvp-death-message", sp.PlayerName, VerbParticiple(verbKey), killer.PlayerName);
            killMsg  = Lang.Get("scribe:scribe-pvp-kill-message",  killer.PlayerName, VerbActive(verbKey), sp.PlayerName);
        }
        else
        {
            deathMsg = BuildDeathMessage(sp.PlayerName, dmg);
        }

        // Record the Death on EVERY notebook the victim carries, not just the first found.
        foreach (var nbHost in FindCarriedNotebooks(sp))
        {
            nbHost.History.TryAddEntry(new Scribe.Core.HistoryEntry
            {
                Kind       = Scribe.Core.HistoryEventKind.Death,
                Detail     = deathMsg,
                InGameDate = NotebookHost.FormatDate(sapi),
            });
            nbHost.FlushHistory();
        }

        // ── PvP kill — record the killer-first message on every notebook the killer carries ──
        if (killer is not null && killMsg is not null)
        {
            foreach (var killerHost in killerHosts)
            {
                killerHost.History.TryAddEntry(new Scribe.Core.HistoryEntry
                {
                    Kind       = Scribe.Core.HistoryEventKind.PvpKill,
                    Detail     = killMsg,
                    InGameDate = NotebookHost.FormatDate(sapi),
                });
                killerHost.FlushHistory();
            }
        }
    }

    /// <summary>
    /// Resolves the lang KEY of a weapon-aware PvP kill verb by a 3-tier fallback, best signal first
    /// (see design.md): (1) the killer's held-item <c>Collectible.Tool</c> (<c>EnumTool</c>) →
    /// <c>scribe:scribe-pvp-verb-tool-&lt;tool&gt;</c>; (2) else <c>dmg.Type</c> →
    /// <c>scribe:scribe-pvp-verb-damage-&lt;type&gt;</c>; (3) else the generic no-repeat pool
    /// <c>scribe:scribe-pvp-verb-generic-N</c>, indexed off the killer notebook's existing PvpKill
    /// count so successive kills rotate without a <c>Random</c>. Tier 1 is the only accurate signal
    /// for vanilla melee (vanilla hardcodes melee <c>dmg.Type</c> to Blunt).
    ///
    /// Returns the KEY (not the resolved string) so the caller can look up BOTH the active verb
    /// (killer-first kill message) and its passive participle (victim-first death message) via
    /// <see cref="VerbActive"/> / <see cref="VerbParticiple"/>.
    /// </summary>
    private static string ResolvePvpVerbKey(
        Vintagestory.API.Common.EntityPlayer killerEntity,
        Vintagestory.API.Common.DamageSource dmg,
        NotebookHost? killerHost)
    {
        // Tier 1 — weapon category from the killer's currently-held item.
        var tool = killerEntity.RightHandItemSlot?.Itemstack?.Collectible?.Tool;
        if (tool is not null)
        {
            string toolKey = $"scribe:scribe-pvp-verb-tool-{tool.ToString()!.ToLowerInvariant()}";
            if (TryLang(toolKey, out _)) return toolKey;
        }

        // Tier 2 — damage type (catches modded weapons that set a type but no tool).
        string dmgKey = $"scribe:scribe-pvp-verb-damage-{dmg.Type.ToString().ToLowerInvariant()}";
        if (TryLang(dmgKey, out _)) return dmgKey;

        // Tier 3 — generic pool, size discovered by probing upward from -0. Rotate by the killer's
        // existing PvpKill count so the next kill picks a different verb (no immediate repeat).
        int poolSize = 0;
        while (TryLang($"scribe:scribe-pvp-verb-generic-{poolSize}", out _)) poolSize++;
        if (poolSize == 0) return "scribe:scribe-pvp-verb-damage-bluntattack"; // defensive; keys ship with the mod
        int priorKills = killerHost?.History.Entries.Count(e => e.Kind == Scribe.Core.HistoryEventKind.PvpKill) ?? 0;
        return $"scribe:scribe-pvp-verb-generic-{priorKills % poolSize}";
    }

    /// <summary>The active past-tense verb for the killer-first kill message ("Raptor <b>slashed</b>
    /// Junkmuffin") — just the resolved key's own value.</summary>
    private static string VerbActive(string verbKey) => Lang.Get(verbKey);

    /// <summary>The passive participle for the victim-first death message ("Junkmuffin was
    /// <b>slain</b> by Raptor"). Uses a <c>&lt;key&gt;-participle</c> override when one exists, else
    /// falls back to the active verb (correct for "shot"/"slashed"/"bashed"/… which are identical in
    /// both forms; only "slew" → "slain" ships an override).</summary>
    private static string VerbParticiple(string verbKey)
        => TryLang($"{verbKey}-participle", out string participle) ? participle : Lang.Get(verbKey);

    /// <summary>
    /// <see cref="Lang.Get"/> with the key-echo miss check used throughout this file: returns false
    /// (and echoes the key) when no translation exists, so callers can fall through to another tier.
    /// </summary>
    private static bool TryLang(string key, out string value)
    {
        value = Lang.Get(key);
        return value != key;
    }

    private void OnStormTick(float _)
    {
        if (sapi is null) return;
        var stormSys = sapi.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>();
        if (stormSys is null) return;

        bool nowActive = stormSys.StormData.nowStormActive;
        bool rising    = nowActive && !_stormWasActive;
        _stormWasActive = nowActive;

        if (!rising) return;

        string strength = stormSys.StormData.nextStormStrength.ToString();
        string date     = NotebookHost.FormatDate(sapi);

        foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            // Record on EVERY notebook the player carries, not just the first found.
            foreach (var host in FindCarriedNotebooks(player))
            {
                host.History.TryAddEntry(new Scribe.Core.HistoryEntry
                {
                    Kind       = Scribe.Core.HistoryEventKind.TemporalStorm,
                    Detail     = strength,
                    InGameDate = date,
                });
                host.FlushHistory();
            }
        }
    }

    /// <summary>
    /// Builds the Detail sentence for a non-PvP death. When a creature dealt the killing blow
    /// (resolved via <c>GetCauseEntity()</c>, which covers melee), we pick a flavored line from our
    /// own <c>scribe:scribe-mob-death-N</c> pool and name the creature with the entity's own
    /// <c>GetPrefixAndCreatureName()</c> — so it is always the correct variant ("a nightmare
    /// drifter", "a brown bear"), unlike vanilla's <c>deathmsg-drifter-*</c> keys which only exist
    /// for the surface drifter. Environmental deaths (fall/fire/hunger/…) keep vanilla's
    /// <c>deathmsg-{cause}-{N}</c> reconstruction. The returned sentence always names the victim, so
    /// callers store it in Detail with an empty ActorName.
    /// </summary>
    private string BuildDeathMessage(string playerName, Vintagestory.API.Common.DamageSource? dmg)
    {
        if (dmg is null) return $"{playerName} died.";

        // Resolve the attacker via GetCauseEntity() (CauseEntity ?? SourceEntity): SourceEntity is
        // null for melee, so reading it alone drops melee attackers into the "died." fallback.
        var causeEntity = dmg.GetCauseEntity();
        if (causeEntity is not null)
        {
            // Creature kill — flavored line from our pool + the creature's own display name, so every
            // variant reads correctly (vanilla ships bespoke deathmsg keys for almost no creatures).
            string creature = causeEntity.GetPrefixAndCreatureName();
            int poolSize = 0;
            while (Lang.Get($"scribe:scribe-mob-death-{poolSize}") != $"scribe:scribe-mob-death-{poolSize}") poolSize++;
            if (poolSize > 0)
            {
                int idx = sapi!.World.Rand.Next(poolSize);
                return Lang.Get($"scribe:scribe-mob-death-{idx}", playerName, creature);
            }
            return $"{playerName} was slain by {creature}."; // defensive; keys ship with the mod
        }

        // Environmental death — rebuild the vanilla deathmsg-{cause}-{N} string the way vanilla does.
        string cause = dmg.Source.ToString().ToLowerInvariant().Replace("_", "-"); // e.g. "fall", "fire"
        // Try variant counts 1..4 and pick from available. Use a hash of the player name to
        // deterministically pick the same variant as vanilla's random (close enough for a chronicle).
        int hash = Math.Abs(playerName.GetHashCode());
        for (int maxN = 4; maxN >= 1; maxN--)
        {
            string key = $"deathmsg-{cause}-{(hash % maxN) + 1}";
            string msg = Vintagestory.API.Config.Lang.Get(key, playerName);
            if (msg != key) return msg; // Lang.Get returns the key unchanged on a miss
        }
        return $"{playerName} died.";
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
