using System;
using System.Collections.Generic;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// Mod entry point. Registers the lectern's block/block-entity classes and the network
/// channel used for server-authoritative document edits. Per-side setup (hotkeys, GUI,
/// lock bookkeeping) happens in <see cref="StartClientSide"/>/<see cref="StartServerSide"/>.
///
/// Also owns the per-player pin layer: the server-side <see cref="ScribePinStore"/> (pins +
/// settings + a live DocId→position index), its save-game persistence, the identity-addressed
/// pin/complete handlers, and the per-player push of a player's own pins/settings to their client.
/// The client caches its own pushed set so the lectern GUI can query <see cref="IsPinnedForMe"/>.
/// </summary>
public sealed class ScribeModSystem : ModSystem
{
    public const string NetworkChannelName = "scribe";
    public const string ClientConfigFileName = "scribe-client-config.json";

    /// <summary>Savegame keys for the persisted pin store and settings store.</summary>
    private const string PinStoreSaveKey = "scribe:pins:v1";
    private const string SettingsStoreSaveKey = "scribe:settings:v1";

    private ICoreClientAPI? capi;
    private ICoreServerAPI? sapi;

    /// <summary>Server-side pin/settings store. Null on a pure client.</summary>
    private ScribePinStore? pinStore;

    /// <summary>Client-side cache of THIS player's own pins, populated by the server push. Keyed by
    /// (docId, taskId) for O(1) <see cref="IsPinnedForMe"/> lookups from the GUI.</summary>
    private readonly HashSet<(Guid, Guid)> myPins = new();

    /// <summary>Client-side cache of THIS player's settings (defaults until the first push).</summary>
    private ScribePlayerSettings mySettings = new();

    /// <summary>Raised on the client whenever a fresh pin set or settings push arrives, so an open
    /// lectern dialog can repaint its per-player pin indicators. </summary>
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
            .RegisterMessageType<ScribePinnedSetMessage>()
            .RegisterMessageType<ScribePlayerSettingsMessage>();
    }

    /// <summary>Server-side accessor for the pin store, so the block entity can register/orphan its
    /// document and refresh snapshots. Null on the client.</summary>
    public ScribePinStore? PinStore => pinStore;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        RegisterCustomIcons(api);

        api.Network.GetChannel(NetworkChannelName)
            .SetMessageHandler<ScribeEditDocumentMessage>(OnClientReceivedEditReply)
            .SetMessageHandler<ScribePinnedSetMessage>(OnClientReceivedPinnedSet)
            .SetMessageHandler<ScribePlayerSettingsMessage>(OnClientReceivedPlayerSettings);
    }

    /// <summary>Client-side: whether THIS player has pinned the given task, from the server-pushed
    /// cache. The lectern GUI drives its resting pin tint / pin-glyph accent off this. Returns false
    /// before the first push (a safe default — nothing shows as pinned until the server confirms).</summary>
    public bool IsPinnedForMe(Guid docId, Guid taskId) => myPins.Contains((docId, taskId));

    /// <summary>Client-side: THIS player's current settings (defaults until the first server push).</summary>
    public ScribePlayerSettings MySettings => mySettings;

    private void OnClientReceivedPinnedSet(ScribePinnedSetMessage message)
    {
        myPins.Clear();
        if (ScribePinCodec.TryDeserializeList(message.PinnedRefBytes, out var pins) && pins is not null)
        {
            foreach (var pin in pins) myPins.Add((pin.OwnerDocId, pin.TaskId));
        }
        MyPinsChanged?.Invoke();
    }

    private void OnClientReceivedPlayerSettings(ScribePlayerSettingsMessage message)
    {
        if (ScribePinCodec.TryDeserializeSettings(message.SettingsBytes, out var settings) && settings is not null)
        {
            mySettings = settings;
            MyPinsChanged?.Invoke();
        }
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
        var settingsBytes = sapi.WorldManager.SaveGame.GetData(SettingsStoreSaveKey);
        pinStore.LoadFrom(pinBytes, settingsBytes);
    }

    private void OnGameWorldSave()
    {
        if (sapi is null || pinStore is null) return;
        sapi.WorldManager.SaveGame.StoreData(PinStoreSaveKey, pinStore.SerializePins());
        sapi.WorldManager.SaveGame.StoreData(SettingsStoreSaveKey, pinStore.SerializeSettings());
    }

    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        DrainLegacyPinsFor(player);
        PushPinsTo(player);
        PushSettingsTo(player);
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
        Trace("complete-task received from {0}: doc={1} task={2}", fromPlayer.PlayerName, docId, taskId);
        CompleteTaskForPlayer(fromPlayer, docId, taskId);
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
    /// Server-side complete-a-task by identity (the read-view checkbox / future HUD checkbox). Two
    /// distinct phases, split so each is independently observable in the log (7.8 part d diagnosis):
    /// <list type="number">
    /// <item><b>Complete</b> — resolve the document via the live index and toggle its done flag
    /// lock-free (<see cref="CompleteTaskStep"/>).</item>
    /// <item><b>Conditional unpin</b> — per the completing player's CompleteUnpins setting, remove their
    /// pin, but only when the task is now DONE (unchecking a completed task must not also unpin), or
    /// unconditionally when the target is orphaned/unresolvable so "check it off and it leaves my list"
    /// stays uniform whether or not the source still exists (<see cref="ConditionalUnpinStep"/>).</item>
    /// </list>
    /// Public for the same reason as <see cref="SetPinForPlayer"/> — the block-entity layer and the
    /// integration suite drive the exact production path.
    /// </summary>
    public void CompleteTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId)
    {
        if (sapi is null || pinStore is null) return;

        bool resolved = CompleteTaskStep(player, docId, taskId);
        ConditionalUnpinStep(player, docId, taskId, resolved);
    }

    /// <summary>
    /// Phase 1 of completion: toggle the task's done flag on the authoritative document, if it resolves.
    /// Returns whether the owning document was resolvable right now (true) or is orphaned/unloaded
    /// (false) — the caller uses that to decide the unpin rule. Toggling also refreshes pin snapshots
    /// and re-pushes affected players (see <see cref="BlockEntityScribeLectern.ToggleTaskByIdFromReader"/>).
    /// </summary>
    private bool CompleteTaskStep(IServerPlayer player, Guid docId, Guid taskId)
    {
        if (sapi is null || pinStore is null) return false;

        if (!pinStore.TryResolvePos(docId, out var pos))
        {
            Trace("  complete: doc {0} not in live index (orphaned/unloaded) — nothing to toggle", docId);
            return false;
        }
        if (sapi.World.BlockAccessor.GetBlockEntity<BlockEntityScribeLectern>(pos) is not { } lectern)
        {
            Trace("  complete: doc {0} indexed at {1} but no lectern BE there — nothing to toggle", docId, pos);
            return false;
        }

        bool before = lectern.Document.FindByTaskId(taskId)?.Done ?? false;
        lectern.ToggleTaskByIdFromReader(taskId);
        var after = lectern.Document.FindByTaskId(taskId);
        if (after is null)
        {
            Trace("  complete: task {0} not found in doc {1} — no toggle applied", taskId, docId);
        }
        else
        {
            Trace("  complete: task {0} done {1} -> {2}", taskId, before, after.Done);
        }
        return true;
    }

    /// <summary>
    /// Phase 2 of completion: remove the completing player's pin per their CompleteUnpins setting. When
    /// the document resolved, unpin only if the task is now DONE (so unchecking a done task does not
    /// unpin). When it did NOT resolve (orphaned/unloaded), unpin unconditionally — actioning an
    /// unreachable task's checkbox is defined as "clear it from my list". Re-pushes the player only when
    /// their set actually changed.
    /// </summary>
    private void ConditionalUnpinStep(IServerPlayer player, Guid docId, Guid taskId, bool resolved)
    {
        if (sapi is null || pinStore is null) return;

        if (resolved)
        {
            bool wantsUnpin = pinStore.GetSettings(player.PlayerUID).CompleteUnpins;
            bool nowDone = pinStore.TryResolvePos(docId, out var pos)
                && sapi.World.BlockAccessor.GetBlockEntity<BlockEntityScribeLectern>(pos) is { } lectern
                && lectern.Document.FindByTaskId(taskId) is { Done: true };
            if (!wantsUnpin || !nowDone)
            {
                Trace("  unpin: skipped (completeUnpins={0}, nowDone={1})", wantsUnpin, nowDone);
                return;
            }
        }

        if (pinStore.RemovePin(player.PlayerUID, docId, taskId))
        {
            Trace("  unpin: removed {0}'s pin on task {1}{2}", player.PlayerName, taskId, resolved ? "" : " (orphaned target)");
            PushPinsTo(player);
        }
        else
        {
            Trace("  unpin: {0} had no pin on task {1} — nothing removed", player.PlayerName, taskId);
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

    /// <summary>Re-push a single player their own settings (server → client).</summary>
    public void PushSettingsTo(IServerPlayer player)
    {
        if (sapi is null || pinStore is null) return;
        var bytes = ScribePinCodec.SerializeSettings(pinStore.GetSettings(player.PlayerUID));
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribePlayerSettingsMessage { SettingsBytes = bytes }, player);
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
