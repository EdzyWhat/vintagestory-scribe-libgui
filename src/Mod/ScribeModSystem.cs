using System;
using System.Collections.Generic;
using System.Linq;
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
/// Mod entry point. Registers block/item classes and the network channel, and owns the per-player
/// pin layer (server pin store + persistence, pin/complete handlers, per-player push). Display and
/// behavior preferences are client-local JSON (<see cref="MySettings"/>), not server state.
/// </summary>
public sealed partial class ScribeModSystem : ModSystem
{
    public const string NetworkChannelName = "scribe";

    /// <summary>Savegame key for the persisted pin store.</summary>
    private const string PinStoreSaveKey   = "scribe:pins:v1";
    private const string TimerStoreSaveKey = "scribe:timer:v1";

    private const string AssignmentStoreSaveKey = "scribe:assignments:v1";

    private const string QuestDecisionStoreSaveKey = "scribe:questdecisions:v1";

    private const string PendingHistoryStoreSaveKey = "scribe:pendinghistory:v1";

    /// <summary>Client-local JSON holding all of this player's Scribe preferences (completion policy, HUD layout, font scales); per-player, cross-world, never server-synced.</summary>
    public const string HudConfigFileName = "scribe-hud-config.json";

    /// <summary>DEV-ONLY client-local JSON for the <c>.geartune</c> gearworks-layout knobs (<see cref="ScribeGearTuning"/>); kept apart from player settings.</summary>
    public const string GearTuningConfigFileName = "scribe-gear-tuning.json";

    /// <summary>DEV-ONLY client-local JSON for the <c>.boxtune</c> block-box knobs (<see cref="ScribeBoxTuning"/>); kept apart from player settings.</summary>
    public const string BoxTuningConfigFileName = "scribe-box-tuning.json";

    /// <summary>Client-local JSON for author-facing visual-tuning knobs (<see cref="ScribeVisualTuning"/>), optionally editable via the shared <c>configlib-patches.json</c> manifest. Not a player preference file.</summary>
    public const string VisualTuningConfigFileName = "scribe-visual-tuning.json";

    private ICoreClientAPI? capi;
    private ICoreServerAPI? sapi;

    /// <summary>Shared no-op UI sound player, built lazily while the mute preference is on. See <see cref="GetUiSoundPlayer"/>.</summary>
    private SilentSoundPlayer? silentSoundPlayer;

    /// <summary>Server-side pin/settings store. Null on a pure client.</summary>
    private ScribePinStore? pinStore;

    /// <summary>Server-side player-to-player assignment store. Null on a pure client.</summary>
    private ScribeAssignmentStore? assignmentStore;

    /// <summary>Server-side per-player quest-decision ledger. Null on a pure client.</summary>
    private ScribeQuestDecisionStore? questDecisionStore;

    /// <summary>Client cache of THIS player's quest-decision ledger, pushed by the server; lets the prompt-suppress check avoid a round trip.</summary>
    private readonly ScribeQuestDecisionSet myQuestDecisions = new();

    /// <summary>Server-side reflection bridge to CarryOn so carried containers join history recording. Null on a pure client.</summary>
    private CarryOnBridge? carryOnBridge;

    /// <summary>Server-side store of Death entries queued for a document evicted from the dying player's inventory mid-dispatch; flushed by <see cref="OnHistoryScanTick"/> once the document is seen in a carried slot. Null on a pure client.</summary>
    private PendingHistoryStore? pendingHistoryStore;

    public PendingHistoryStore? PendingHistoryStore => pendingHistoryStore;

    /// <summary>Server-side per-player timer store. Keyed by PlayerUID. Null on a pure client.</summary>
    private Dictionary<string, TimerStore>? timerStores;

    /// <summary>Server-side ticker id for the 1-second timer countdown. 0 when not registered.</summary>
    private long timerTickListenerId;
    private long timerDisplayTickId;

    /// <summary>Client cache of THIS player's pins, keyed by (docId, taskId) for <see cref="IsPinnedForMe"/>.</summary>
    private readonly HashSet<(Guid, Guid)> myPins = new();

    /// <summary>Client cache of THIS player's pin list in server order (the HUD reads it; the lectern only needs <see cref="myPins"/>).</summary>
    private IReadOnlyList<ScribePinnedRef> myPinList = Array.Empty<ScribePinnedRef>();

    /// <summary>Optimistic pin overlay keyed by (OwnerDocId, TaskId): non-null = pin requested but unconfirmed (display snapshot); null = removal requested. Needed because in singleplayer the embedded server is paused while the Handbook pauses the client. Cleared per key by <see cref="ReconcileOptimisticPins"/>.</summary>
    private readonly Dictionary<(Guid, Guid), ScribePinnedRef?> optimisticPinOverlay = new();

    /// <summary>Insertion order of <see cref="optimisticPinOverlay"/> keys; <see cref="RebuildDisplayPinList"/> replays adds in this order because a child's placement depends on its unconfirmed parent already being in the list.</summary>
    private readonly List<(Guid, Guid)> optimisticPinOrder = new();

    /// <summary>Placement inputs captured at optimistic-add time so <see cref="RebuildDisplayPinList"/> can call <see cref="ScribePinOrdering.PlaceNewPin"/> as the server will. Unused for removals.</summary>
    private readonly Dictionary<(Guid, Guid), (ScribeDocument? Source, ScribePinInsert InsertEdge)> optimisticPinPlan = new();

    /// <summary><see cref="myPinList"/> with <see cref="optimisticPinOverlay"/> applied; recomputed on input change because <c>MyPins</c> is read many times per frame.</summary>
    private IReadOnlyList<ScribePinnedRef> displayPinList = Array.Empty<ScribePinnedRef>();

    /// <summary>Client cache of THIS player's sent assignments (Assignment-tab history), from the server push.</summary>
    private IReadOnlyList<ScribeBlock> mySentAssignments = Array.Empty<ScribeBlock>();

    /// <summary>Client cache of THIS player's received assignments (Inbox), from the same push.</summary>
    private IReadOnlyList<ScribeBlock> myReceivedAssignments = Array.Empty<ScribeBlock>();

    /// <summary>This player's sent assignments (Assignment-tab history) — never another player's.</summary>
    public IReadOnlyList<ScribeBlock> MySentAssignments => mySentAssignments;

    /// <summary>This player's received assignments (Inbox) — never another player's.</summary>
    public IReadOnlyList<ScribeBlock> MyReceivedAssignments => myReceivedAssignments;

    /// <summary>True when a received assignment has not been seen yet (<c>Seen</c>); drives the particle indicator and Inbox nav shimmer. Evaluated per read.</summary>
    public bool HasUnseenAssignment => myReceivedAssignments.Any(b => b.Assignment is { Seen: false });

    /// <summary>True when an unseen assignment has also not been delivered as a Task Notice item (see <see cref="ScribeAssignment.NeedsAmbientParticle"/>); the narrower trigger for the world-space block particle.</summary>
    public bool HasUnseenUndeliveredAssignment =>
        myReceivedAssignments.Any(b => b.Assignment is { } a && a.NeedsAmbientParticle);

    /// <summary>Raised on the client when an assignment sync push arrives.</summary>
    public event Action? MyAssignmentsChanged;

    /// <summary>Rebindable hotkey code toggling the pinned-task HUD's collapse state.</summary>
    public const string HudHotkeyCode = "scribepinhud";

    /// <summary>Client-side pinned-task HUD (null on a pure server); self-opens/closes off <see cref="MyPinsChanged"/>, disposed in <see cref="Dispose"/>.</summary>
    private HudScribePins? pinHud;

    /// <summary>Center-screen quest-accept prompt modal (<see cref="Scribe.Core.ScribeQuestAcceptPolicy.PromptPopup"/>), an alternative to <see cref="pinHud"/>'s banner; self-managing. Null on a pure server.</summary>
    private GuiDialogScribeQuestPrompt? questPromptModal;

    /// <summary>The single Scribe settings window (<see cref="OpenSettings"/>), built lazily and reused to keep scroll/focus state. Null until first opened and on a pure server.</summary>
    private ScribeSettingsDialog? settingsDialog;

    /// <summary>Client-local player preferences loaded from <see cref="HudConfigFileName"/>; never server-synced. Non-null after <see cref="StartClientSide"/>.</summary>
    private ScribePlayerSettings? mySettings;

    /// <summary>DEV-ONLY gearworks-layout tuning, persisted to <see cref="GearTuningConfigFileName"/>.</summary>
    private ScribeGearTuning? gearTuning;

    /// <summary>Visual-tuning knobs for the ambient light sampler and assignment particles, persisted to <see cref="VisualTuningConfigFileName"/>.</summary>
    private ScribeVisualTuning? visualTuning;

    /// <summary>Shared particle emitter built from <see cref="visualTuning"/>; used by Inbox-capable block entities and the Task Notice proximity ping. Null on a pure server.</summary>
    private ScribeAssignmentParticleEmitter? particleEmitter;

    /// <summary>DEV gearworks-tuning window (<c>.geartune</c>), built lazily and reused.</summary>
    private ScribeGearTuningDialog? gearTuningDialog;

    /// <summary>DEV-ONLY block-box tuning, persisted to <see cref="BoxTuningConfigFileName"/>.</summary>
    private ScribeBoxTuning? boxTuning;

    /// <summary>DEV box-tuning window (<c>.boxtune</c>), built lazily and reused.</summary>
    private ScribeBoxTuningDialog? boxTuningDialog;

    /// <summary>The playing Clockmaker alarm sound, created on the timer's Fired transition and kept until <see cref="ScribeAlarmSound.IsDone"/>.</summary>
    private ScribeAlarmSound? _activeAlarm;

    /// <summary>Cache of dialog backdrop bitmaps by asset-location string (see <see cref="GetBackdropBitmap"/>). A null value marks a failed load so it is attempted once. Bitmaps are shared across dialogs, which never dispose them; all are disposed in <see cref="Dispose"/>.</summary>
    private Dictionary<string, SKBitmap?>? backdropCache;

    /// <summary>Parsed cuneiform glyph bundle cache (see <see cref="GetCuneiformBundle"/>); <see cref="cuneiformBundleLoaded"/> distinguishes "not yet loaded" from "unavailable" so a bad asset warns once. Managed only, no disposal.</summary>
    private Scribe.Core.Cuneiform.GlyphBundle? cuneiformBundle;
    private bool cuneiformBundleLoaded;

    /// <summary>
    /// Maps each active DocId to its <see cref="IScribeDocumentHost"/>. Lecterns register on Initialize and
    /// unregister on OnBlockRemoved; NotebookHosts on dialog open/close. The server registry is authoritative
    /// for incoming packets.
    /// </summary>
    private readonly Dictionary<Guid, IScribeDocumentHost> _hostRegistry = new();

    /// <summary>Registers a host under its document's <c>DocId</c>.</summary>
    public void RegisterHost(IScribeDocumentHost host) => _hostRegistry[host.Document.DocId] = host;

    /// <summary>Unregisters a host by DocId.</summary>
    public void UnregisterHost(Guid docId) => _hostRegistry.Remove(docId);

    /// <summary>True when a DIFFERENT live placed block is registered under <paramref name="docId"/> — the creative-clone signature (a middle-click pick copies the DocId while the source is still alive). A break then re-place is not a collision, since removal unregistered the id.</summary>
    public bool IsDocIdRegisteredToOtherBlock(Guid docId, BlockPos self) =>
        _hostRegistry.TryGetValue(docId, out var host)
        && host is BlockEntityScribeWritingStation be
        && be.Pos != self;

    /// <summary>Client → server: the player opened the notebook with this DocId, so the server records the one-time PickedUp history entry (see <see cref="OnServerReceivedNotebookOpened"/>). Carries slot identity so the entry lands on the opened book, not the active-hand item. No-op off the client.</summary>
    public void NotifyServerNotebookOpened(Guid docId, ItemSlot slot)
    {
        capi?.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookOpenedMessage
        {
            DocIdBytes = docId.ToByteArray(),
            TargetInventoryId = slot.Inventory?.InventoryID,
            TargetSlotId = slot.Inventory?.GetSlotId(slot) ?? -1,
        });
    }

    /// <summary>Raised on the client when a pin set push arrives.</summary>
    public event Action? MyPinsChanged;

    /// <summary>Client cache of THIS player's timer state; null means none received yet (treat as Idle).</summary>
    public TimerStore? MyTimer { get; private set; }

    /// <summary>The form values of the timer this player last started this session; null before the first start.
    /// Prefills the Notebook's new-timer form. Not persisted.</summary>
    public TimerFormValues? LastStartedTimerForm { get; set; }

    /// <summary>Raised on the client when the server pushes timer state.</summary>
    public event Action? MyTimerChanged;

    /// <summary>Fires once per real second from one shared tick listener. The HUD timer row and Timer tab repaint off it so their whole-second displays flip in the same dispatch; independent 250ms listeners would visibly drift.</summary>
    public event Action? TimerDisplayTick;

    /// <summary>Raised when the standalone settings window opens or closes (from its own lifecycle, so every close route notifies); open lecterns rebuild to recolor their Settings nav button.</summary>
    public event Action? SettingsVisibilityChanged;

    /// <summary>True while the settings window is open; false on a pure server.</summary>
    public bool IsSettingsOpen => settingsDialog?.IsOpened() == true;

    /// <summary>Raises <see cref="SettingsVisibilityChanged"/> on behalf of <see cref="ScribeSettingsDialog"/>.</summary>
    public void NotifySettingsVisibilityChanged() => SettingsVisibilityChanged?.Invoke();

    /// <summary>DEV: raised when a gearworks knob changes so an open Timer tab rebuilds.</summary>
    public event Action? GearTuningChanged;

    /// <summary>DEV: raised when a box-tuning knob changes; the world hitbox reads <see cref="BoxTuning"/> live, so this only lets an open dialog re-seed its fields to the clamped value.</summary>
    public event Action? BoxTuningChanged;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterBlockClass("BlockScribeLectern", typeof(BlockScribeLectern));
        api.RegisterBlockEntityClass("ScribeLectern", typeof(BlockEntityScribeLectern));
        api.RegisterBlockClass("BlockScriptorium", typeof(BlockScriptorium));
        api.RegisterBlockEntityClass("Scriptorium", typeof(BlockEntityScriptorium));
        api.RegisterBlockClass("BlockAssignmentDesk", typeof(BlockAssignmentDesk));
        api.RegisterBlockEntityClass("AssignmentDesk", typeof(BlockEntityAssignmentDesk));
        api.RegisterBlockClass("BlockInbox", typeof(BlockInbox));
        api.RegisterBlockEntityClass("Inbox", typeof(BlockEntityInbox));
        // Chalkboard: wall-mounted cosmetic Lectern variant; names match chalkboard.json.
        api.RegisterBlockClass("BlockChalkboard", typeof(BlockScribeChalkboard));
        api.RegisterBlockEntityClass("Chalkboard", typeof(BlockEntityScribeChalkboard));
        api.RegisterItemClass("ItemScribeNotebook", typeof(ItemScribeNotebook));
        api.RegisterItemClass("ItemClockmakerNotebook", typeof(ItemClockmakerNotebook));
        api.RegisterItemClass("ItemScribeTablet", typeof(ItemScribeTablet));
        api.RegisterItemClass("ItemScribeTaskNotice", typeof(ItemScribeTaskNotice));

        // Message types must be registered in the same order on both sides. Append only, never insert mid-list, so shipped wire packet ids stay unchanged.
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
            .RegisterMessageType<ScribeDeleteHistoryEntryMessage>()
            .RegisterMessageType<ScribeAssignmentActionMessage>()
            .RegisterMessageType<ScribeDeleteAssignmentMessage>()
            .RegisterMessageType<ScribeAssignmentSyncMessage>()
            .RegisterMessageType<ScribeMarkAssignmentsSeenMessage>()
            .RegisterMessageType<ScribeAutoLinkQuestMessage>()
            .RegisterMessageType<ScribeSendAssignmentBatchMessage>()
            .RegisterMessageType<ScribeDeliveryRangeCheckRequestMessage>()
            .RegisterMessageType<ScribeDeliveryRangeCheckReplyMessage>()
            .RegisterMessageType<ScribeTaskNoticeActionMessage>()
            .RegisterMessageType<ScribeTaskNoticeProximityPingMessage>()
            .RegisterMessageType<ScribeSetQuestObjectiveProgressMessage>()
            .RegisterMessageType<ScribeDismissQuestPromptMessage>()
            .RegisterMessageType<ScribeQuestDecisionSetMessage>()
            .RegisterMessageType<ScribeSetReadViewStateMessage>()
            .RegisterMessageType<ScribeKnownPlayersSyncMessage>();
    }

    /// <summary>Server-side pin store accessor. Null on the client.</summary>
    public ScribePinStore? PinStore => pinStore;

    /// <summary>Server-side accessor for the assignment store. Null on the client.</summary>
    public ScribeAssignmentStore? AssignmentStore => assignmentStore;

    public override void StartClientSide(ICoreClientAPI api)
    {
        // HarfBuzz native-load isolation lives in the standalone ScribeHarfBuzzLoadFix ModSystem, not a partial here: a low ExecuteOrder would preempt gui's StartClientSide and break font shaping on every platform.
        base.StartClientSide(api);
        capi = api;

        // Client-local preferences; a missing/corrupt file loads as defaults and Normalized() clamps hand edits.
        mySettings = (api.LoadModConfig<ScribePlayerSettings>(HudConfigFileName) ?? new ScribePlayerSettings()).Normalized();

        gearTuning = (api.LoadModConfig<ScribeGearTuning>(GearTuningConfigFileName) ?? new ScribeGearTuning()).Normalized();
        RegisterGearTuneCommand(api);

        boxTuning = (api.LoadModConfig<ScribeBoxTuning>(BoxTuningConfigFileName) ?? new ScribeBoxTuning()).Normalized();
        RegisterBoxTuneCommand(api);

        visualTuning = api.LoadModConfig<ScribeVisualTuning>(VisualTuningConfigFileName) ?? new ScribeVisualTuning();
        particleEmitter = new ScribeAssignmentParticleEmitter(visualTuning);
        RegisterScribeLightCommand(api);
        RegisterScribeProbeCommand(api);
        RegisterScribeGuardCommand(api);

        RegisterCustomIcons(api);
        RegisterCustomFonts(api);

        // Handbook postfix adding "Add to Scribe" links; client-only, unpatched in Dispose.
        StartHandbookPatch();

        api.Network.GetChannel(NetworkChannelName)
            .SetMessageHandler<ScribeEditDocumentMessage>(OnClientReceivedEditReply)
            .SetMessageHandler<ScribePinnedSetMessage>(OnClientReceivedPinnedSet)
            .SetMessageHandler<ScribeGuestbookSyncMessage>(OnClientReceivedGuestbookSync)
            .SetMessageHandler<ScribeNotebookSaveMessage>(OnClientReceivedNotebookSave)
            .SetMessageHandler<ScribeTimerStateMessage>(OnClientReceivedTimerState)
            .SetMessageHandler<ScribeTranscribeStampMessage>(OnClientReceivedTranscribeStamp)
            .SetMessageHandler<ScribeAssignmentSyncMessage>(OnClientReceivedAssignmentSync)
            .SetMessageHandler<ScribeDeliveryRangeCheckReplyMessage>(OnClientReceivedDeliveryRangeCheckReply)
            .SetMessageHandler<ScribeTaskNoticeProximityPingMessage>(OnClientReceivedTaskNoticeProximityPing)
            .SetMessageHandler<ScribeQuestDecisionSetMessage>(OnClientReceivedQuestDecisionSet)
            .SetMessageHandler<ScribeKnownPlayersSyncMessage>(OnClientReceivedKnownPlayersSync);

        // The HUD subscribes to MyPinsChanged in its ctor and stays closed until there is at least one pin.
        pinHud = new HudScribePins(api, this);

        // Self-manages open/close; must exist even while nothing is pending.
        questPromptModal = new GuiDialogScribeQuestPrompt(api, this);

        // Quest soft auto-detect; a no-op when vsquest is absent.
        StartQuestWatcher(api);

        // One shared 1Hz tick so the HUD timer row and Timer tab repaint in the same dispatch (see TimerDisplayTick).
        timerDisplayTickId = api.World.RegisterGameTickListener(_ => TimerDisplayTick?.Invoke(), 1000);

        // GUIOrOtherControls so it fires with a dialog open; default P.
        api.Input.RegisterHotKey(HudHotkeyCode, Lang.Get("scribe:hotkey-scribepinhud"), GlKeys.P,
            HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler(HudHotkeyCode, _ =>
        {
            pinHud?.ToggleCollapsed();
            return true;
        });
    }

    /// <summary>Disposes the client-side HUD, dialogs, and every cached backdrop bitmap (the only place those are disposed).</summary>
    public override void Dispose()
    {
        DisposeHandbookPatch();
        pinHud?.Dispose();
        pinHud = null;
        questPromptModal?.Dispose();
        questPromptModal = null;
        DisposeQuestWatcher();
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
        boxTuningDialog?.Dispose();
        boxTuningDialog = null;
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
        assignmentStore = new ScribeAssignmentStore();
        questDecisionStore = new ScribeQuestDecisionStore();
        playerLocationStore = new ScribePlayerLocationStore();
        knownPlayersStore = new ScribeKnownPlayersStore();
        pendingHistoryStore = new PendingHistoryStore();
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
        channel.SetMessageHandler<ScribeAssignmentActionMessage>(OnServerReceivedAssignmentAction);
        channel.SetMessageHandler<ScribeDeleteAssignmentMessage>(OnServerReceivedDeleteAssignment);
        channel.SetMessageHandler<ScribeMarkAssignmentsSeenMessage>(OnServerReceivedMarkAssignmentsSeen);
        channel.SetMessageHandler<ScribeAutoLinkQuestMessage>(OnServerReceivedAutoLinkQuest);
        channel.SetMessageHandler<ScribeSendAssignmentBatchMessage>(OnServerReceivedSendAssignmentBatch);
        channel.SetMessageHandler<ScribeDeliveryRangeCheckRequestMessage>(OnServerReceivedDeliveryRangeCheckRequest);
        channel.SetMessageHandler<ScribeTaskNoticeActionMessage>(OnServerReceivedTaskNoticeAction);
        channel.SetMessageHandler<ScribeSetQuestObjectiveProgressMessage>(OnServerReceivedSetQuestObjectiveProgress);
        channel.SetMessageHandler<ScribeDismissQuestPromptMessage>(OnServerReceivedDismissQuestPrompt);
        channel.SetMessageHandler<ScribeSetReadViewStateMessage>(OnServerReceivedSetReadViewState);

        // Persist/load the pin + settings stores with the save game (the WaypointMapLayer pattern).
        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.GameWorldSave += OnGameWorldSave;

        // Initial per-player push once a player is fully in-world, and legacy-pin drain.
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        // Last-known-position capture for the Hybrid range check's offline-target case (Core 1.5).
        api.Event.PlayerDisconnect += OnScribePlayerDisconnect;

        // History hooks; OnHistoryScanTick (10s) refreshes the carried-document snapshot and flushes queued Death entries.
        api.Event.OnEntityDeath += OnEntityDeath;
        api.World.RegisterGameTickListener(OnHistoryScanTick, 10000);

        // Task Notice proximity heartbeat.
        api.World.RegisterGameTickListener(OnTaskNoticeProximityTick, 5000);

        // Timer countdown: 1 s tick for all running/fired player timers.
        timerStores = new Dictionary<string, TimerStore>();
        timerTickListenerId = api.World.RegisterGameTickListener(OnTimerTick, 1000);

        ApplyClockmakerTraitGate(api);

        RegisterSeedCommand(api);
    }

    /// <summary>Server-side <c>[scribe]</c> diagnostic log for the pin/complete round-trip; droppable.</summary>
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
