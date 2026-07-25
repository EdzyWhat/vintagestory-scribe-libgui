using System;
using System.Collections.Generic;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
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

    /// <summary>Client-side player preferences (completion policy, HUD rows/collapse), persisted as
    /// client-local JSON (<see cref="HudConfigFileName"/>) and loaded in <see cref="StartClientSide"/>;
    /// never server-synced. The Core POCO doubles as the config's serialized shape. Non-null on the
    /// client after <see cref="StartClientSide"/>.</summary>
    private ScribePlayerSettings? mySettings;

    /// <summary>Raised on the client whenever a fresh pin set push arrives, so an open lectern dialog
    /// (and the HUD) can repaint its per-player pin indicators.</summary>
    public event Action? MyPinsChanged;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterBlockClass("BlockScribeLectern", typeof(BlockScribeLectern));
        api.RegisterBlockEntityClass("ScribeLectern", typeof(BlockEntityScribeLectern));

        // All message types must be registered in this same order on both sides. The original four
        // read/edit/lock messages come first (order frozen); the identity-addressed pin layer is
        // APPENDED after them. ScribeToggleTaskMessage (the old position-addressed read-view toggle)
        // was retired in favor of ScribeCompleteTaskMessage — do not re-add it.
        api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<ScribeEditDocumentMessage>()
            .RegisterMessageType<ScribeReleaseLockMessage>()
            .RegisterMessageType<ScribeRequestAccessMessage>()
            .RegisterMessageType<ScribeSetPinMessage>()
            .RegisterMessageType<ScribeCompleteTaskMessage>()
            .RegisterMessageType<ScribePinnedSetMessage>();
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

        api.Network.GetChannel(NetworkChannelName)
            .SetMessageHandler<ScribeEditDocumentMessage>(OnClientReceivedEditReply)
            .SetMessageHandler<ScribePinnedSetMessage>(OnClientReceivedPinnedSet);

        // The pinned-task HUD self-shows once the player's pin set arrives (it subscribes to
        // MyPinsChanged in its ctor), so it can be constructed here regardless of current pin count —
        // it stays closed until there is ≥1 pin. It owns its own subscription + tick; we dispose it.
        pinHud = new HudScribePins(api, this);

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

    /// <summary>Dispose the client-side HUD (its own <see cref="MyPinsChanged"/> subscription + tick).
    /// The server side holds no unmanaged/disposable state of its own here.</summary>
    public override void Dispose()
    {
        pinHud?.Dispose();
        pinHud = null;
        base.Dispose();
    }

    /// <summary>Client-side: whether THIS player has pinned the given task, from the server-pushed
    /// cache. The lectern GUI drives its resting pin tint / pin-glyph accent off this. Returns false
    /// before the first push (a safe default — nothing shows as pinned until the server confirms).</summary>
    public bool IsPinnedForMe(Guid docId, Guid taskId) => myPins.Contains((docId, taskId));

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
        DrainLegacyPinsFor(player);
        PushPinsTo(player);
    }

    /// <summary>
    /// One-time, single-player-scoped drain of v3 documents' previously-pinned tasks into this
    /// player's store (design Migration Plan). For each loaded lectern that deserialized from v3, its
    /// codec-surfaced legacy-pinned ids are migrated; the lectern was also marked dirty on load so it
    /// re-saves as v4. Runs on every join but is idempotent — <see cref="ScribePinStore.SetPin"/> is a
    /// no-op for an already-present pin, so re-draining an already-migrated document adds nothing.
    /// Scoped to single-player because the v3 flag was shared, not per-player (a multiplayer world's
    /// v3 pins can't be attributed to one player — an explicit non-goal).
    /// </summary>
    private void DrainLegacyPinsFor(IServerPlayer player)
    {
        if (sapi is null || pinStore is null) return;
        if (sapi.Server.Config.MaxClients > 1) return; // single-player scope only

        double totalHours = sapi.World.Calendar.TotalHours;
        foreach (var lectern in EnumerateLoadedLecterns())
        {
            var legacy = lectern.TakeLegacyPinnedTaskIds();
            if (legacy.Count == 0) continue;
            pinStore.MigrateLegacyPins(player.PlayerUID, lectern.Document.DocId, legacy, lectern.Document, totalHours);
        }
        PushPinsTo(player);
    }

    private IEnumerable<BlockEntityScribeLectern> EnumerateLoadedLecterns()
    {
        // The live position index holds exactly the loaded lecterns' documents; resolve each back to
        // its block entity. (A document whose chunk is unloaded isn't in the index and has no legacy
        // ids to drain until it loads and re-registers.)
        if (sapi is null || pinStore is null) yield break;
        foreach (var docId in pinStore.KnownDocIds())
        {
            if (pinStore.TryResolvePos(docId, out var pos)
                && sapi.World.BlockAccessor.GetBlockEntity<BlockEntityScribeLectern>(pos) is { } lectern)
            {
                yield return lectern;
            }
        }
    }

    private void OnClientReceivedEditReply(ScribeEditDocumentMessage message)
    {
        if (capi is null) return;
        if (TryGetLectern(capi.World, message.PosX, message.PosY, message.PosZ) is { } lectern)
        {
            lectern.HandleServerReply(message);
        }
    }

    private void OnServerReceivedEdit(IServerPlayer fromPlayer, ScribeEditDocumentMessage message)
    {
        if (sapi is null) return;
        if (TryGetLectern(sapi.World, message.PosX, message.PosY, message.PosZ) is { } lectern)
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
        if (TryGetLectern(sapi.World, message.PosX, message.PosY, message.PosZ) is { } lectern)
        {
            lectern.ReleaseLock(fromPlayer.PlayerUID);
        }
    }

    private void OnServerReceivedRequestAccess(IServerPlayer fromPlayer, ScribeRequestAccessMessage message)
    {
        if (sapi is null) return;
        if (TryGetLectern(sapi.World, message.PosX, message.PosY, message.PosZ) is { } lectern)
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
        SetPinForPlayer(fromPlayer, docId, taskId, message.Pinned);
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

    /// <summary>
    /// Server-side pin/unpin, addressed by (DocId, TaskId). An UNPIN removes straight from the store
    /// with no block resolution, so it works when the owning lectern is broken or its chunk is
    /// unloaded. A PIN resolves the owning block via the live index only to snapshot the task's
    /// text/done from the server's own authoritative document (never a client-supplied snapshot); if
    /// the document can't be resolved right now the pin is still recorded with an empty snapshot.
    /// Lock-free throughout. Re-pushes the player when their set changed. Public so the block-entity
    /// layer and the integration suite drive the exact production path, not a copy of it.
    /// </summary>
    public void SetPinForPlayer(IServerPlayer player, Guid docId, Guid taskId, bool pinned)
    {
        if (sapi is null || pinStore is null) return;

        bool changed;
        if (pinned)
        {
            string text = "";
            bool done = false;
            if (pinStore.TryResolvePos(docId, out var pos)
                && sapi.World.BlockAccessor.GetBlockEntity<BlockEntityScribeLectern>(pos) is { } lectern
                && lectern.Document.FindByTaskId(taskId) is { } block)
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
            // Not pinned by this player — this is a plain read-view checkbox on an unpinned document
            // task. Toggle the shared document directly (legacy behavior); no store/policy involvement.
            CompleteUnpinnedTaskAtSource(player, docId, taskId);
            return;
        }
        bool nowDone = !current.Value;

        bool changed = pinStore.SetPinDone(player.PlayerUID, docId, taskId, nowDone);
        Trace("  complete: {0}'s pin on task {1} done {2} -> {3}", player.PlayerName, taskId, current.Value, nowDone);

        // Write through to the shared source document when it resolves (best-effort; a gone source just
        // skips this). Reconciles only the acting player — other pinners keep their own copies.
        bool resolved = TryResolveLectern(docId, out var lectern);
        if (resolved) lectern!.SetTaskDoneFromReader(taskId, nowDone);

        // Apply the completion policy — only on a transition INTO done (unchecking never removes).
        if (nowDone)
        {
            switch (policy)
            {
                case ScribeCompletionPolicy.Unpin:
                    changed |= pinStore.RemovePin(player.PlayerUID, docId, taskId);
                    Trace("  policy Unpin: removed {0}'s pin on task {1}", player.PlayerName, taskId);
                    break;
                case ScribeCompletionPolicy.Delete:
                    if (resolved && lectern!.DeleteTaskFromReader(taskId))
                        Trace("  policy Delete: removed task {0} from source doc {1}", taskId, docId);
                    else
                        Trace("  policy Delete: source unresolvable for task {0} — pin removed only", taskId);
                    changed |= pinStore.RemovePin(player.PlayerUID, docId, taskId);
                    break;
                case ScribeCompletionPolicy.Sink:
                case ScribeCompletionPolicy.Keep:
                default:
                    // Non-destructive policies: keep the (now-done) pin. The HUD applies the client-side
                    // difference — Sink mutes + sinks it to the bottom; Keep leaves it in place. The
                    // server does not distinguish them (nothing is removed for either).
                    break;
            }
        }

        if (changed) PushPinsTo(player);
    }

    /// <summary>Resolves a docId to its currently-hosting lectern block entity via the live index, if
    /// one is loaded. Returns false (and null) when the source is unloaded or destroyed — the
    /// completion path then relies on the store alone.</summary>
    private bool TryResolveLectern(Guid docId, out BlockEntityScribeLectern? lectern)
    {
        lectern = null;
        if (sapi is null || pinStore is null) return false;
        if (!pinStore.TryResolvePos(docId, out var pos)) return false;
        lectern = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityScribeLectern>(pos);
        return lectern is not null;
    }

    /// <summary>Completes (toggles) an UNPINNED document task straight on the shared source — the plain
    /// read-view checkbox on a task nobody has pinned. No store or completion-policy involvement (there
    /// is no pin). A no-op when the source is unresolvable (nothing to toggle without a document).</summary>
    private void CompleteUnpinnedTaskAtSource(IServerPlayer player, Guid docId, Guid taskId)
    {
        if (!TryResolveLectern(docId, out var lectern))
        {
            Trace("  complete(unpinned): doc {0} unresolvable — nothing to toggle", docId);
            return;
        }
        var block = lectern!.Document.FindByTaskId(taskId);
        if (block is null || !block.IsTask)
        {
            Trace("  complete(unpinned): task {0} not found in doc {1}", taskId, docId);
            return;
        }
        lectern.SetTaskDoneFromReader(taskId, !block.Done);
        Trace("  complete(unpinned): task {0} toggled to done={1}", taskId, !block.Done);
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

    private static BlockEntityScribeLectern? TryGetLectern(IWorldAccessor world, int x, int y, int z)
    {
        var pos = new BlockPos(x, y, z);
        return world.BlockAccessor.GetBlockEntity<BlockEntityScribeLectern>(pos);
    }
}
