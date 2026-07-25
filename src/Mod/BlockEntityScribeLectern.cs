using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// Holds this lectern's <see cref="ScribeDocument"/>, keyed by block position. Persistence
/// and sync go through <see cref="ToTreeAttributes"/>/<see cref="FromTreeAttributes"/> (the
/// vanilla Sign pattern). Editor-view edits are server-authoritative: the client never mutates
/// its local document directly — it sends a request, the server applies it and calls
/// <see cref="BlockEntity.MarkDirty"/> to persist and re-sync. Read view is lock-free and live:
/// anyone can look at the document at any time, even while another player is editing it.
/// Only one player may hold the editor lock at a time (server-tracked, released on close/
/// mode-switch/disconnect).
/// </summary>
public sealed class BlockEntityScribeLectern : BlockEntity
{
    private const string DocumentAttributeKey = ScribeDocumentAttributes.DocumentAttributeKey;

    public ScribeDocument Document { get; private set; } = new();

    /// <summary>Server-side only: the UID of the player currently editing, if any.</summary>
    private string? lockHolderUid;

    /// <summary>Server-side: DocId this block entity has registered in the pin store's live index, so
    /// it can unregister exactly that id on removal even if <see cref="Document"/> was later replaced.
    /// Null until the first server-side register.</summary>
    private Guid? registeredDocId;

    /// <summary>Server-side: task ids the codec surfaced as previously-pinned when a v3 document was
    /// loaded, awaiting a one-time drain into a player's pin store (see
    /// <see cref="TakeLegacyPinnedTaskIds"/>). Empty for a v4 document.</summary>
    private IReadOnlyList<Guid> legacyPinnedTaskIds = System.Array.Empty<Guid>();

    /// <summary>Server-side: true when this block entity loaded a v3 document (no persisted ids), so
    /// it must be marked dirty on first server init to re-save as v4 — otherwise its generated
    /// DocId/TaskIds regenerate every load and pins can't stick (the single most important sequencing
    /// detail in the design).</summary>
    private bool needsV4Resave;

    /// <summary>Client-side: the single LibGUI dialog serving BOTH views (migrate-editor-view-libgui).
    /// Read and editor are internal view states of this one dialog, so switching between them is a
    /// view swap rather than closing one dialog and opening another.</summary>
    private GuiDialogScribeLecternLibGui? dialog;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api is ICoreServerAPI sapi)
        {
            sapi.Event.PlayerDisconnect += OnPlayerDisconnect;

            // Register this document's live DocId → position mapping so pins can resolve it, and
            // re-save a v3-loaded document as v4 so its freshly-generated ids persist (else they
            // regenerate every load and pins can't stick).
            RegisterDocInStore();
            if (needsV4Resave)
            {
                needsV4Resave = false;
                MarkDirty();
            }
        }
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (Api is ICoreServerAPI sapi)
        {
            sapi.Event.PlayerDisconnect -= OnPlayerDisconnect;

            // The block was removed (broken/replaced/exchanged). Forget the live position so the
            // document is unresolvable until a re-place re-registers it — but do NOT clear the pins:
            // breaking a lectern to relocate it drops an item carrying the document, and OnBlockPlaced
            // restores the same DocId so the pins resolve again (see that method + the
            // break→re-place integration scenario). A pin is removed only by the OWNER's own action —
            // their completion policy, or deleting the task in their own edit (see
            // ScribePinStore.ReconcileSnapshotsForActor) — never on block removal or a chunk unload.
            // Snapshots keep the pin renderable on the HUD meanwhile.
            if (PinStore is { } store && registeredDocId is { } docId)
            {
                store.UnregisterDoc(docId);
            }
            registeredDocId = null;
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBytes(DocumentAttributeKey, ScribeDocumentCodec.Serialize(Document));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        var bytes = tree.GetBytes(DocumentAttributeKey);
        // A v3 document had no persisted ids: the codec generates fresh ones and surfaces which tasks
        // were pinned so they can be migrated. Remember both so Initialize re-saves as v4 (ids stick)
        // and the mod system can drain the legacy pins on the owner's next join.
        needsV4Resave = ScribeDocumentCodec.IsPriorVersion(bytes);
        Document = ScribeDocumentCodec.TryDeserialize(bytes, out var doc, out var legacyPinned) && doc is not null
            ? doc
            : new ScribeDocument();
        legacyPinnedTaskIds = legacyPinned;

        // A resync may have replaced the document (a different DocId is unusual for a lectern, but the
        // break→replace path can restore a saved doc). Keep the live index pointing at the current one.
        RegisterDocInStore();

        // Reflect an authoritative resync in the open dialog. RefreshReadView rebuilds the read view
        // from the now-current Document; it is a no-op while the dialog is in editor mode (the editor
        // edits a private scratch copy that an external resync must not clobber).
        dialog?.RefreshReadView();
    }

    /// <summary>Restores a document carried on a placed item stack (break→re-place), so the same
    /// content and ids come back and pins reattach. Empty-doc fallback when the stack carries none
    /// (a freshly-crafted lectern). Server-authoritative; the client gets it via the normal resync.</summary>
    public override void OnBlockPlaced(ItemStack? byItemStack)
    {
        base.OnBlockPlaced(byItemStack);

        if (Api is not ICoreServerAPI) return;
        if (byItemStack is not null && ScribeDocumentAttributes.TryReadFrom(byItemStack, out var doc) && doc is not null)
        {
            Document = doc;
            RegisterDocInStore();
            MarkDirty();
        }
    }

    /// <summary>Server-side: (re)point the pin store's live index at this document's current position.
    /// No-op on the client or before the store exists. Unregisters a stale prior mapping if the
    /// document's DocId changed under us (break→replace restoring a different saved doc).</summary>
    private void RegisterDocInStore()
    {
        if (PinStore is not { } store) return;
        if (registeredDocId is { } prior && prior != Document.DocId)
        {
            store.UnregisterDoc(prior);
        }
        registeredDocId = Document.DocId;
        store.RegisterDoc(Document.DocId, Pos);
    }

    /// <summary>Server-side accessor for the mod system's pin store (null on the client or before the
    /// server side started).</summary>
    private ScribePinStore? PinStore => ModSystem?.PinStore;

    private ScribeModSystem? ModSystem => Api?.ModLoader.GetModSystem<ScribeModSystem>();

    /// <summary>Hands off (and clears) the v3 legacy-pinned task ids for a one-time migration drain.
    /// Returns empty after the first call or for a v4 document.</summary>
    public IReadOnlyList<Guid> TakeLegacyPinnedTaskIds()
    {
        var ids = legacyPinnedTaskIds;
        legacyPinnedTaskIds = System.Array.Empty<Guid>();
        return ids;
    }

    /// <summary>
    /// Called from <see cref="BlockScribeLectern.OnBlockInteractStart"/> on whichever side is
    /// running (client immediately for responsiveness, server via the synced interaction).
    /// We only act server-side: decide access and reply to the requesting player.
    /// </summary>
    public void OnRightClick(IPlayer byPlayer, bool wantEditor)
    {
        if (Api is not ICoreServerAPI sapi || byPlayer is not IServerPlayer serverPlayer)
        {
            return;
        }

        RequestAccess(sapi, serverPlayer, wantEditor);
    }

    /// <summary>Server-side: handles a mid-session read/editor mode-switch request.</summary>
    public void OnRequestAccess(IServerPlayer fromPlayer, bool wantEditor)
    {
        if (Api is not ICoreServerAPI sapi)
        {
            return;
        }

        RequestAccess(sapi, fromPlayer, wantEditor);
    }

    /// <summary>
    /// Shared read/editor access decision, used by both the initial right-click interaction and
    /// the mid-session mode-switch message. Read access is always granted and never touches the
    /// lock. Editor access is granted only if the lock is free or already held by this player;
    /// a refusal still attaches the current document so the requester can fall back to reading
    /// it rather than seeing nothing.
    /// </summary>
    private void RequestAccess(ICoreServerAPI sapi, IServerPlayer byPlayer, bool wantEditor)
    {
        if (!wantEditor)
        {
            SendReply(sapi, byPlayer, granted: true, editorMode: false, refusalReason: null);
            return;
        }

        if (lockHolderUid is not null && lockHolderUid != byPlayer.PlayerUID)
        {
            SendReply(sapi, byPlayer, granted: false, editorMode: true, refusalReason: "scribe:scribe-gui-locked");
            return;
        }

        lockHolderUid = byPlayer.PlayerUID;
        SendReply(sapi, byPlayer, granted: true, editorMode: true, refusalReason: null);
    }

    /// <summary>
    /// Server-side: applies a client-submitted document edit (an editor-view autosave tick) and
    /// re-syncs everyone. Returns whether the edit was applied, so the caller can ack failure
    /// (e.g. the sender's lock was lost) back to the client.
    /// </summary>
    public bool ApplyEdit(IServerPlayer fromPlayer, byte[]? documentBytes)
    {
        if (fromPlayer.PlayerUID != lockHolderUid)
        {
            return false;
        }

        if (documentBytes is not null && ScribeDocumentCodec.TryDeserialize(documentBytes, out var doc) && doc is not null)
        {
            Document = doc;
            RegisterDocInStore(); // an edit never changes the DocId, but keep the index authoritative
            MarkDirty(redrawOnClient: true);
            // Only the editing player's own pins reconcile to their edit (grief-proof, player-owned).
            ReconcileActorPins(fromPlayer.PlayerUID);
        }

        return true;
    }

    /// <summary>
    /// Client-side: optimistically updates the locally-cached document immediately after this
    /// player flushes their own editor-view edit, so switching to read view right afterward
    /// doesn't briefly show the pre-edit content while the authoritative resync is still in
    /// flight. Safe because it mirrors exactly what was just sent; the real resync (or a
    /// save-failed ack, on the rare lock-loss edge case) supersedes it moments later regardless.
    /// </summary>
    public void ApplyLocalOptimisticEdit(ScribeDocument doc)
    {
        Document = doc;
    }

    /// <summary>
    /// Server-side: set a task's done state to an explicit value on the authoritative document,
    /// addressed by its stable <see cref="ScribeBlock.TaskId"/> — the write-through target of the
    /// identity-addressed completion path (<see cref="ScribeCompleteTaskMessage"/>). Unlike
    /// <see cref="ApplyEdit"/> this does NOT require the editor lock: ticking a task off is an
    /// always-allowed action any viewer may perform, even while another player holds the lock. Mutates
    /// the authoritative document in place (not a client-submitted copy), so it only ever changes the
    /// one flag and cannot clobber a concurrent editor's in-flight text edits beyond that. A TaskId with
    /// no matching (or non-task) block is a no-op. Does NOT touch pins — the caller (<c>ScribeModSystem</c>)
    /// owns the acting player's pin done-state (the store is authoritative) and reconciles it separately;
    /// other players' pins are deliberately left alone (grief-proof, player-owned pins).
    /// </summary>
    public void SetTaskDoneFromReader(Guid taskId, bool done)
    {
        if (Api is not ICoreServerAPI) return;

        var block = Document.FindByTaskId(taskId);
        if (block is null || !block.IsTask) return;
        if (block.Done == done) return;

        block.Done = done;
        MarkDirty(redrawOnClient: true);
    }

    /// <summary>
    /// Server-side: delete a task from the authoritative document by its stable
    /// <see cref="ScribeBlock.TaskId"/> — the write-through for the <c>Delete</c> completion policy.
    /// Lock-free like <see cref="SetTaskDoneFromReader"/>. Returns whether a task was removed. Does NOT
    /// touch pins (the caller reconciles the acting player's pin; others' pins are untouched).
    /// </summary>
    public bool DeleteTaskFromReader(Guid taskId)
    {
        if (Api is not ICoreServerAPI) return false;

        for (int i = 0; i < Document.Blocks.Count; i++)
        {
            if (Document.Blocks[i].TaskId == taskId && Document.Blocks[i].IsTask)
            {
                Document.DeleteBlock(i);
                MarkDirty(redrawOnClient: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>Server-side: after THIS player saves an edit, reconcile only the acting player's pins
    /// into the edited document (grief-proof: a pin is the owner's own copy — only their own edit
    /// changes/removes it), then re-push that player.</summary>
    private void ReconcileActorPins(string actingPlayerUid)
    {
        if (PinStore is { } store && ModSystem is { } mod)
        {
            mod.PushPinsTo(store.ReconcileSnapshotsForActor(actingPlayerUid, Document.DocId, Document));
        }
    }

    /// <summary>Server-side: releases the lock, e.g. when the editing player closes the GUI or switches to read view.</summary>
    public void ReleaseLock(string playerUid)
    {
        if (lockHolderUid == playerUid)
        {
            lockHolderUid = null;
        }
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        ReleaseLock(player.PlayerUID);
    }

    private void SendReply(ICoreServerAPI sapi, IServerPlayer toPlayer, bool granted, bool editorMode, string? refusalReason)
    {
        var reply = new ScribeEditDocumentMessage
        {
            PosX = Pos.X,
            PosY = Pos.Y,
            PosZ = Pos.Z,
            Granted = granted,
            EditorMode = editorMode,
            RefusalReason = refusalReason,
            DocumentBytes = ScribeDocumentCodec.Serialize(Document),
        };

        sapi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(reply, toPlayer);
    }

    /// <summary>
    /// Server-side: sends a save-acknowledgment after an editor-view autosave tick, reusing the
    /// same message shape as an open/mode-switch reply. Only sent on failure (the happy path
    /// needs no ack — the player already sees their own edits in their scratch copy).
    /// </summary>
    public void SendSaveFailedAck(ICoreServerAPI sapi, IServerPlayer toPlayer)
    {
        SendReply(sapi, toPlayer, granted: false, editorMode: true, refusalReason: "scribe:scribe-gui-save-failed");
    }

    /// <summary>
    /// Client-side: handles the server's reply to an open request or a mid-session mode-switch. A
    /// single LibGUI dialog serves both views (migrate-editor-view-libgui), so a grant opens the
    /// dialog if needed and swaps its internal view mode; there is no second dialog to close. A
    /// post-autosave failure ack (editor already open, refused) surfaces the error and stays put.
    /// </summary>
    public void HandleServerReply(ScribeEditDocumentMessage message)
    {
        if (Api is not ICoreClientAPI capi)
        {
            return;
        }

        if (ScribeDocumentCodec.TryDeserialize(message.DocumentBytes, out var doc) && doc is not null)
        {
            Document = doc;
        }

        bool open = dialog is not null && dialog.IsOpened();

        if (!message.Granted)
        {
            // Editor access refused (someone else holds the lock, or an autosave arrived lock-less).
            // Surface the error; if nothing is open yet (a fresh right-click), fall back to the read
            // view so the requester still sees the document.
            capi.TriggerIngameError(this, "scribe-lectern-locked", Lang.Get(message.RefusalReason ?? "scribe:scribe-gui-locked"));

            // A save-failed ack (autosave rejected because our lock was lost) is recoverable: ask the
            // open editor to re-request the lock and re-flush its unsaved edit rather than silently
            // dropping it (task 8.6). Bounded internally so a lock held elsewhere can't spin forever.
            if (open && message.RefusalReason == "scribe:scribe-gui-save-failed"
                && dialog!.HandleSaveFailed())
            {
                return;
            }

            if (!open)
            {
                OpenDialog(capi);
                dialog!.EnterReadMode();
            }
            return;
        }

        if (!open)
        {
            OpenDialog(capi);
        }

        if (message.EditorMode)
        {
            dialog!.EnterEditorMode(message.DocumentBytes);
        }
        else
        {
            dialog!.EnterReadMode();
        }
    }

    /// <summary>Creates and opens the single LibGUI dialog (read view by default). The "switch to
    /// editor" / "done editing" controls drive the view swap and lock request/release from inside the
    /// dialog through the normal server flow.</summary>
    private void OpenDialog(ICoreClientAPI capi)
    {
        dialog = new GuiDialogScribeLecternLibGui(Pos, this, capi);
        dialog.TryOpen();
    }
}
