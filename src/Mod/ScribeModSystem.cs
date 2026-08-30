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
using Vintagestory.API.MathTools;   // BlockPos
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
public sealed partial class ScribeModSystem : ModSystem
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

    /// <summary>DEV-ONLY client-local JSON holding the live gearworks-layout tuning knobs
    /// (<see cref="ScribeGearTuning"/>), opened via the <c>.geartune</c> command. Separate from the real
    /// preference file so this throwaway art-tuning aid never touches player settings; delete alongside the
    /// tool when the layout is finalized.</summary>
    public const string GearTuningConfigFileName = "scribe-gear-tuning.json";

    private ICoreClientAPI? capi;
    private ICoreServerAPI? sapi;

    /// <summary>Client-side shared no-op UI sound player (scribe-mute-ui-sounds), lazily built on first
    /// use while the mute preference is on and reused across dialogs (it's stateless). See
    /// <see cref="GetUiSoundPlayer"/>.</summary>
    private SilentSoundPlayer? silentSoundPlayer;

    /// <summary>Server-side pin/settings store. Null on a pure client.</summary>
    private ScribePinStore? pinStore;

    /// <summary>Server-side reflection bridge to the CarryOn mod family, so Notebooks/Tablets inside
    /// a CarryOn-carried container also participate in history recording. Null on a pure client.
    /// See <see cref="CarryOnBridge"/>.</summary>
    private CarryOnBridge? carryOnBridge;

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

    /// <summary>Client-side optimistic pin/unpin overlay, keyed by (OwnerDocId, TaskId): a non-null value is
    /// a pin the player just requested that hasn't been confirmed by the authoritative push yet (its display
    /// snapshot); a null value is a pin the player just requested REMOVED. Exists because pin placement is
    /// server-side (<see cref="ScribePinStore.SetPin"/>), and in singleplayer the embedded server's tick loop
    /// — and therefore its handling of the network message — is paused for as long as the Handbook holds the
    /// client paused (update-pins-1-3-3): without this overlay every pin-consuming surface (HUD, Pin Tab,
    /// dialog row tint) would show nothing until the player unpaused. Cleared per-key once
    /// <see cref="OnClientReceivedPinnedSet"/> confirms the server agrees (see <see cref="ReconcileOptimisticPins"/>).</summary>
    private readonly Dictionary<(Guid, Guid), ScribePinnedRef?> optimisticPinOverlay = new();

    /// <summary>Insertion order of <see cref="optimisticPinOverlay"/>'s keys, so <see cref="RebuildDisplayPinList"/>
    /// replays optimistic pin-ADDs through <see cref="ScribePinOrdering.PlaceNewPin"/> in the order they actually
    /// happened — load-bearing when a parent is pinned and then a child is pinned under it while both are still
    /// unconfirmed, since the child's placement depends on the parent already being in the working list.</summary>
    private readonly List<(Guid, Guid)> optimisticPinOrder = new();

    /// <summary>Per-optimistic-pin placement inputs (the source document + the player's Pin Insert edge),
    /// captured at the moment of the optimistic add so <see cref="RebuildDisplayPinList"/> can call
    /// <see cref="ScribePinOrdering.PlaceNewPin"/> the same way the server will. Not used for an optimistic
    /// remove (no placement to compute).</summary>
    private readonly Dictionary<(Guid, Guid), (ScribeDocument? Source, ScribePinInsert InsertEdge)> optimisticPinPlan = new();

    /// <summary>The list <see cref="MyPins"/> actually returns: <see cref="myPinList"/> with
    /// <see cref="optimisticPinOverlay"/> applied. Recomputed (not derived per-read) whenever either input
    /// changes, since <c>MyPins</c> is read many times per frame across the HUD/Pin Tab/dialog rows.</summary>
    private IReadOnlyList<ScribePinnedRef> displayPinList = Array.Empty<ScribePinnedRef>();

    /// <summary>Rebindable hotkey code that toggles the pinned-task HUD's collapse state (design D6).
    /// Registered client-side; its handler flips the HUD's client-local collapse preference.</summary>
    public const string HudHotkeyCode = "scribepinhud";

    /// <summary>The client-side pinned-task HUD element (null on a pure server). Constructed in
    /// <see cref="StartClientSide"/>; it self-opens/closes off <see cref="MyPinsChanged"/> and owns its
    /// own event/tick lifetime, disposed in <see cref="Dispose"/>.</summary>
    private HudScribePins? pinHud;

    /// <summary>The single standalone Scribe settings window (scribe-themed-toggle).
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

    /// <summary>DEV-ONLY live gearworks-layout tuning (<see cref="ScribeGearTuning"/>), persisted to
    /// <see cref="GearTuningConfigFileName"/> and loaded in <see cref="StartClientSide"/>. Lazily defaulted
    /// so it is never null (mirrors <see cref="mySettings"/>).</summary>
    private ScribeGearTuning? gearTuning;

    /// <summary>The single DEV gearworks-tuning window (<c>.geartune</c>), lazily built + reused; disposed
    /// in <see cref="Dispose"/>. Null until first opened, and on a pure server.</summary>
    private ScribeGearTuningDialog? gearTuningDialog;

    /// <summary>The currently playing Clockmaker Notebook alarm sound, if any. Created when the timer
    /// transitions to Fired; kept alive until it self-reports <see cref="ScribeAlarmSound.IsDone"/> (the
    /// object manages its own fade lifecycle). Disposed and nulled in <see cref="Dispose"/>.</summary>
    private ScribeAlarmSound? _activeAlarm;

    /// <summary>Client-side cache of self-loaded dialog backdrop bitmaps, keyed by asset-location string
    /// (see <see cref="GetBackdropBitmap"/>). Holds a <c>null</c> value for an asset that could not be
    /// loaded so the failing load is attempted — and warned about — exactly once, not per open or per
    /// frame. One immutable bitmap is shared across every dialog open; a dialog NEVER disposes one. All
    /// entries are disposed in <see cref="Dispose"/>. Null on a pure server.</summary>
    private Dictionary<string, SKBitmap?>? backdropCache;

    /// <summary>Client-side cache of the parsed cuneiform glyph bundle (see <see cref="GetCuneiformBundle"/>).
    /// A sentinel <c>false</c> in <see cref="cuneiformBundleLoaded"/> distinguishes "not yet loaded" from
    /// "loaded but unavailable" (a null parse), so a missing/unparseable asset warns exactly once rather
    /// than re-fetching every frame. Not a native/disposable resource (pure managed model), so it needs no
    /// cleanup in <see cref="Dispose"/>. Null/false on a pure server.</summary>
    private Scribe.Core.Cuneiform.GlyphBundle? cuneiformBundle;
    private bool cuneiformBundleLoaded;

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

    /// <summary>True when a DIFFERENT live placed block is already registered under <paramref name="docId"/>.
    /// This is the creative-clone signature: a middle-click pick carries the source block's serialized DocId
    /// onto the copy, so when the copy is placed the source is still alive and registered under that id — a
    /// collision, since the DocId keys this registry (and the pin store). A break→re-place is NOT a collision:
    /// <see cref="BlockEntityScribeWritingStation.OnBlockRemoved"/> unregistered the source first, so the id is
    /// free. <see cref="BlockEntityScribeWritingStation.OnBlockPlaced"/> uses this to decide whether to mint a
    /// fresh identity for the copy.</summary>
    public bool IsDocIdRegisteredToOtherBlock(Guid docId, BlockPos self) =>
        _hostRegistry.TryGetValue(docId, out var host)
        && host is BlockEntityScribeWritingStation be
        && be.Pos != self;

    /// <summary>Client → server: notify that the player just opened the notebook with this DocId, so
    /// the server can record their one-time PickedUp history entry (see
    /// <see cref="OnServerReceivedNotebookOpened"/>). Carries the opened item's slot identity so the
    /// server records the entry on the exact opened book, not the active-hand item (add-tracker-link-tasks
    /// 7.16). No-op off the client. Called by every Scribe item's open path.</summary>
    public void NotifyServerNotebookOpened(Guid docId, ItemSlot slot)
    {
        capi?.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookOpenedMessage
        {
            DocIdBytes = docId.ToByteArray(),
            TargetInventoryId = slot.Inventory?.InventoryID,
            TargetSlotId = slot.Inventory?.GetSlotId(slot) ?? -1,
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

    /// <summary>DEV: raised whenever a gearworks-tuning knob changes in the <c>.geartune</c> window, so an
    /// open Clockmaker's Notebook Timer tab rebuilds its gearworks live off the new values.</summary>
    public event Action? GearTuningChanged;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterBlockClass("BlockScribeLectern", typeof(BlockScribeLectern));
        api.RegisterBlockEntityClass("ScribeLectern", typeof(BlockEntityScribeLectern));
        api.RegisterBlockClass("BlockScriptorium", typeof(BlockScriptorium));
        api.RegisterBlockEntityClass("Scriptorium", typeof(BlockEntityScriptorium));
        // The Chalkboard: a wall-mounted cosmetic variant of the Lectern. The string names match the
        // committed chalkboard.json (class "BlockChalkboard" / entityclass "Chalkboard").
        api.RegisterBlockClass("BlockChalkboard", typeof(BlockScribeChalkboard));
        api.RegisterBlockEntityClass("Chalkboard", typeof(BlockEntityScribeChalkboard));
        api.RegisterItemClass("ItemScribeNotebook", typeof(ItemScribeNotebook));
        api.RegisterItemClass("ItemClockmakerNotebook", typeof(ItemClockmakerNotebook));
        api.RegisterItemClass("ItemScribeTablet", typeof(ItemScribeTablet));

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
            .RegisterMessageType<ScribeTimerStateMessage>()
            .RegisterMessageType<ScribeSetTrackerQuantityMessage>()
            .RegisterMessageType<ScribeTranscribeCopyMessage>()
            .RegisterMessageType<ScribeTranscribeImportMessage>()
            .RegisterMessageType<ScribeTranscribeStampMessage>()
            .RegisterMessageType<ScribeAddHistoryEntryMessage>()
            .RegisterMessageType<ScribeSetHistoryEntryTextMessage>()
            .RegisterMessageType<ScribeDeleteHistoryEntryMessage>();
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

        // DEV: live gearworks-layout tuning (a never-touched file loads as the current baked-in defaults).
        gearTuning = (api.LoadModConfig<ScribeGearTuning>(GearTuningConfigFileName) ?? new ScribeGearTuning()).Normalized();
        RegisterGearTuneCommand(api);
        RegisterScribeLightCommand(api);
        RegisterScribeProbeCommand(api);

        RegisterCustomIcons(api);
        RegisterCustomFonts(api);

        // Client-side Handbook postfix: injects the "Add to Scribe" links onto each item's Handbook page
        // (add-tracker-link-tasks 3.1). Client-only — the Handbook is a client GUI — and unpatched in Dispose.
        StartHandbookPatch();

        api.Network.GetChannel(NetworkChannelName)
            .SetMessageHandler<ScribeEditDocumentMessage>(OnClientReceivedEditReply)
            .SetMessageHandler<ScribePinnedSetMessage>(OnClientReceivedPinnedSet)
            .SetMessageHandler<ScribeGuestbookSyncMessage>(OnClientReceivedGuestbookSync)
            .SetMessageHandler<ScribeNotebookSaveMessage>(OnClientReceivedNotebookSave)
            .SetMessageHandler<ScribeTimerStateMessage>(OnClientReceivedTimerState)
            .SetMessageHandler<ScribeTranscribeStampMessage>(OnClientReceivedTranscribeStamp);

        // The pinned-task HUD self-shows once the player's pin set arrives (it subscribes to
        // MyPinsChanged in its ctor), so it can be constructed here regardless of current pin count —
        // it stays closed until there is ≥1 pin. It owns its own subscription + tick; we dispose it.
        pinHud = new HudScribePins(api, this);

        // Single shared 1Hz display tick: drives BOTH the HUD timer row and the Notebook Timer tab
        // countdown so they repaint on the same dispatch (see TimerDisplayTick). One listener, so there
        // is one authoritative second-boundary rather than two out-of-phase 250ms listeners.
        timerDisplayTickId = api.World.RegisterGameTickListener(_ => TimerDisplayTick?.Invoke(), 1000);

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

    /// <summary>Dispose the client-side HUD (its own <see cref="MyPinsChanged"/> subscription + tick), the
    /// shared settings window, and every cached backdrop bitmap (the one place a backdrop bitmap is ever
    /// disposed — never a dialog). The server side holds no unmanaged/disposable state of its own here.</summary>
    public override void Dispose()
    {
        DisposeHandbookPatch();
        pinHud?.Dispose();
        pinHud = null;
        if (timerDisplayTickId != 0 && capi is not null)
        {
            capi.World.UnregisterGameTickListener(timerDisplayTickId);
            timerDisplayTickId = 0;
        }
        _activeAlarm?.Dispose();
        _activeAlarm = null;
        settingsDialog?.Dispose();
        settingsDialog = null;
        gearTuningDialog?.Dispose();
        gearTuningDialog = null;
        if (backdropCache is not null)
        {
            foreach (var bmp in backdropCache.Values) bmp?.Dispose();
            backdropCache.Clear();
            backdropCache = null;
        }
        base.Dispose();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        sapi = api;
        pinStore = new ScribePinStore();
        carryOnBridge = new CarryOnBridge(api);

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
        channel.SetMessageHandler<ScribeSetTrackerQuantityMessage>(OnServerReceivedSetTrackerQuantity);
        channel.SetMessageHandler<ScribeTranscribeCopyMessage>(OnServerReceivedTranscribeCopy);
        channel.SetMessageHandler<ScribeTranscribeImportMessage>(OnServerReceivedTranscribeImport);
        channel.SetMessageHandler<ScribeAddHistoryEntryMessage>(OnServerReceivedAddHistoryEntry);
        channel.SetMessageHandler<ScribeSetHistoryEntryTextMessage>(OnServerReceivedSetHistoryEntryText);
        channel.SetMessageHandler<ScribeDeleteHistoryEntryMessage>(OnServerReceivedDeleteHistoryEntry);

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
