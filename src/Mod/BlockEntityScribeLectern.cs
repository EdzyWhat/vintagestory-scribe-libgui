using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
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
public sealed class BlockEntityScribeLectern : BlockEntity, IRotatable, IScribeDocumentHost
{
    private const string DocumentAttributeKey = ScribeDocumentAttributes.DocumentAttributeKey;

    /// <summary>Client-side cached rotated mesh, rebuilt when <see cref="MeshAngleRad"/> changes
    /// (keyed by angle in the object cache, mirroring <c>BlockEntitySign</c>).</summary>
    private MeshData? mesh;

    public ScribeDocument Document { get; private set; } = new();

    /// <summary>Horizontal placement angle (radians) so the lectern's open-book reading face turns
    /// toward the placing player — the vanilla Sign/clutter <c>MeshAngleRad</c> idiom. Set once in
    /// <see cref="BlockScribeLectern.TryPlaceBlock"/>, persisted as "meshAngle", and applied to both
    /// the tesselated mesh (<see cref="OnTesselation"/>) and the collision/selection box (below).
    /// The reused <c>bookshelves/lecturn-book-open</c> shape's authored front is SOUTH (+Z) at angle 0,
    /// so the raw player-facing angle points the book at the player with no per-piece offset (see
    /// VSAPI-NOTES "Block placement orientation").</summary>
    private float meshAngleRad;

    /// <summary>The block's collision/selection box rotated to <see cref="MeshAngleRad"/>, surfaced by
    /// the block's <c>GetCollisionBoxes</c>/<c>GetSelectionBoxes</c> so the hitbox tracks the mesh.
    /// Null until the first angle set (then the block falls back to its un-rotated JSON box).</summary>
    public Cuboidf[]? RotatedBox { get; private set; }

    public float MeshAngleRad
    {
        get => meshAngleRad;
        set
        {
            bool changed = meshAngleRad != value;
            meshAngleRad = value;
            if (Block?.CollisionBoxes is { Length: > 0 } boxes)
            {
                RotatedBox = new[] { boxes[0].RotatedCopy(0f, value * (180f / (float)Math.PI), 0f, new Vec3d(0.5, 0.5, 0.5)) };
            }
            if (changed) MarkDirty(true);
        }
    }

    /// <summary>Server-side only: the UID of the player currently editing, if any.</summary>
    private string? lockHolderUid;

    /// <summary>Client-side mirror of <see cref="lockHolderUid"/>, synced via the block-entity tree
    /// (fix-multiplayer-editor-lock §2.1) so the client can reflect a held lock in its "switch to editor"
    /// affordance WITHOUT a server round-trip. The server keeps <see cref="lockHolderUid"/> authoritative;
    /// this is only read on the client. Null when the lock is free.</summary>
    private string? syncedLockHolderUid;

    /// <summary>Client-side: true when the editor lock is held by a DIFFERENT player than
    /// <paramref name="viewerUid"/> (so this viewer's editor affordance should read as unavailable). False
    /// when the lock is free or held by this viewer themselves. The server refusal remains the
    /// authoritative gate; this only drives the affordance state for the common, already-known case.</summary>
    public bool IsLockedByOther(string viewerUid) =>
        syncedLockHolderUid is not null && syncedLockHolderUid != viewerUid;

    /// <summary>Server-authoritative durable access mode for this lectern (fix-transient-lectern-editor-lock).
    /// Persisted + synced via the tree round-trip. RESERVED / dormant this version: nothing sets it away
    /// from <see cref="LecternAccessMode.Public"/> and the editor-entry gate does not read it, so every
    /// lectern behaves as Public. Kept as a distinct field so the sticky "private/read-only" permission can
    /// be surfaced by a later change without conflating it with the transient editor lock.</summary>
    private LecternAccessMode accessMode = LecternAccessMode.Public;

    /// <summary>Client-side mirror of <see cref="accessMode"/>, updated from the tree in
    /// <see cref="FromTreeAttributes"/>. Unused by any current control (dormant); present so a future
    /// player-facing change reads a synced value rather than adding a new packet.</summary>
    private LecternAccessMode syncedAccessMode = LecternAccessMode.Public;

    /// <summary>Server-side: DocId this block entity has registered in the pin store's live index, so
    /// it can unregister exactly that id on removal even if <see cref="Document"/> was later replaced.
    /// Null until the first server-side register.</summary>
    private Guid? registeredDocId;

    /// <summary>Server-side: true when this block entity loaded a v4 document (no title field), so it
    /// must be marked dirty on first server init to re-save as v5 — otherwise the title defaults every
    /// load instead of persisting.</summary>
    private bool needsV5Resave;

    private GuestbookStore _guestbook = new();

    // ── IScribeDocumentHost explicit implementations ──────────────────────
    ScribeDocument IScribeDocumentHost.Document => Document;
    bool IScribeDocumentHost.IsLockedByOther(string viewerUid) => IsLockedByOther(viewerUid);
    void IScribeDocumentHost.ApplyLocalOptimisticEdit(ScribeDocument doc) => ApplyLocalOptimisticEdit(doc);
    ScribeBackdropSpec IScribeDocumentHost.BackdropSpec => ScribeBackdrops.LecternPage;
    ScribeLayout IScribeDocumentHost.GetLayout(float w) => new ScribeLayout(w, 1160f / 1024f);
    string IScribeDocumentHost.DefaultDocumentTitle => "Lectern";
    GuestbookStore IScribeDocumentHost.Guestbook => _guestbook;
    void IScribeDocumentHost.SetTaskDoneFromReader(Guid taskId, bool done) => SetTaskDoneFromReader(taskId, done);
    bool IScribeDocumentHost.DeleteTaskFromReader(Guid taskId) => DeleteTaskFromReader(taskId);
    bool IScribeDocumentHost.MoveTaskToBottomFromReader(Guid taskId) => MoveTaskToBottomFromReader(taskId);
    bool IScribeDocumentHost.SetTaskTextFromReader(Guid taskId, string text) => SetTaskTextFromReader(taskId, text);

    /// <summary>Client-side: the single LibGUI dialog serving BOTH views (migrate-editor-view-libgui).
    /// Read and editor are internal view states of this one dialog, so switching between them is a
    /// view swap rather than closing one dialog and opening another.</summary>
    private ScribeDialogBase? dialog;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        // Register on both sides so the client registry can route edit replies back to this BE,
        // and the server registry can route incoming save/lock packets to it.
        ModSystem?.RegisterHost(this);

        if (api is ICoreServerAPI sapi)
        {
            sapi.Event.PlayerDisconnect += OnPlayerDisconnect;

            // Clear-on-load leg of the transient-lock guarantee (fix-transient-lectern-editor-lock).
            // The editor lock is server-session-only crash-prevention state, never authoritative
            // across a load: a freshly-loaded block must start editable. FromTreeAttributes only ever
            // writes the *client mirror* (syncedLockHolderUid), so this null is defence-in-depth — it
            // guarantees no code path can leave a loaded block holding a stale server-side lock.
            lockHolderUid = null;

            // Register this document's live DocId → position mapping so pins can resolve it, and
            // re-save a v4-loaded document as v5 so the title field persists.
            RegisterDocInStore();
            if (needsV5Resave)
            {
                needsV5Resave = false;
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

        ModSystem?.UnregisterHost(Document.DocId);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBytes(DocumentAttributeKey, ScribeDocumentCodec.Serialize(Document));
        tree.SetFloat("meshAngle", meshAngleRad);
        // Sync the editor-lock holder so clients can reflect a held lock in their editor affordance
        // (fix-multiplayer-editor-lock §2.1). Empty string = lock free (tree attrs don't store null).
        // This is TRANSIENT session state: it drives the contended-editor affordance on other clients,
        // but is never authoritative across a block load — Initialize clears lockHolderUid on load and
        // FromTreeAttributes only writes the client mirror (fix-transient-lectern-editor-lock).
        tree.SetString("lockHolder", lockHolderUid ?? "");
        // Durable per-lectern access mode (fix-transient-lectern-editor-lock). Dormant this version
        // (always Public), but persisted + synced so a future private/read-only permission needs no
        // save-format change. Stored as the underlying byte.
        tree.SetInt("accessMode", (byte)accessMode);
        tree.SetBytes("guestbook", _guestbook.Serialize());
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        // Placement facing (vanilla Sign default: fall back to the shape's authored rotateY when a
        // pre-orientation lectern has no persisted angle). Goes through the property so the rotated
        // hitbox is rebuilt on load; the client re-tesselates on the following redraw.
        MeshAngleRad = tree.HasAttribute("meshAngle")
            ? tree.GetFloat("meshAngle", 0f)
            : (Block?.Shape?.rotateY ?? 0f) * ((float)Math.PI / 180f);

        // Client mirror of the editor-lock holder (fix-multiplayer-editor-lock §2.1). Empty string (the
        // "lock free" sentinel written above) maps back to null. Read on the client to drive the editor
        // affordance; the server ignores its own synced copy (lockHolderUid stays authoritative).
        var lockHolder = tree.GetString("lockHolder", "");
        syncedLockHolderUid = string.IsNullOrEmpty(lockHolder) ? null : lockHolder;

        // Durable access mode (fix-transient-lectern-editor-lock). Absent key (pre-existing saves) →
        // Public. Both the authoritative field and its client mirror are set from the tree; the field
        // is dormant (never read by the editor gate this version).
        var mode = (LecternAccessMode)(byte)tree.GetInt("accessMode", (int)LecternAccessMode.Public);
        accessMode = mode;
        syncedAccessMode = mode;

        var bytes = tree.GetBytes(DocumentAttributeKey);
        needsV5Resave = ScribeDocumentCodec.IsPriorVersion(bytes);
        Document = ScribeDocumentCodec.TryDeserialize(bytes, out var doc, out _) && doc is not null
            ? doc
            : new ScribeDocument();
        _guestbook = GuestbookStore.Deserialize(tree.GetBytes("guestbook"));

        // A resync may have replaced the document (a different DocId is unusual for a lectern, but the
        // break→replace path can restore a saved doc). Keep the live index pointing at the current one.
        RegisterDocInStore();

        // Re-key the host registry under the now-current DocId (same bug class as ab702d1's
        // ApplyEdit/OnBlockPlaced re-registers, on the path they missed). Each side constructs its
        // Document with its OWN random DocId (ScribeDocument ctor → Guid.NewGuid()); the authoritative
        // DocId only arrives here. For a FRESHLY PLACED lectern the VS lifecycle runs Initialize (which
        // registers under the throwaway random id) BEFORE FromTreeAttributes, so without this the client
        // stays keyed under a dead id, the server's open reply routes to nothing via TryResolveHost, and
        // the dialog never opens — while a chunk-LOADED lectern works because FromTreeAttributes runs
        // first (VintagestoryAPI BlockEntity.Initialize doc-comment: "if this block entity already
        // existed then FromTreeAttributes is called first"), so Initialize already sees the real DocId.
        // No-op when Api isn't set yet (the load-path ordering) — Initialize registers right after.
        ModSystem?.RegisterHost(this);

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
            ModSystem?.RegisterHost(this); // re-register under the restored DocId
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

    /// <summary>
    /// Called from <see cref="BlockScribeLectern.OnBlockInteractStart"/> on whichever side is
    /// running (client immediately for responsiveness, server via the synced interaction).
    /// We only act server-side: decide access and reply to the requesting player.
    /// </summary>
    public void OnRightClick(IPlayer byPlayer, bool wantEditor, bool quickAdd)
    {
        if (Api is not ICoreServerAPI sapi || byPlayer is not IServerPlayer serverPlayer)
        {
            return;
        }

        RequestAccess(sapi, serverPlayer, wantEditor, quickAdd);
    }

    /// <summary>Server-side: handles a mid-session read/editor mode-switch request. Quick-add rides the
    /// message flag (the nav-button switch is never a quick-add, but the recovery re-acquire path is
    /// also never quick-add, so both pass false).</summary>
    public void OnRequestAccess(IServerPlayer fromPlayer, bool wantEditor, bool quickAdd)
    {
        if (Api is not ICoreServerAPI sapi)
        {
            return;
        }

        RequestAccess(sapi, fromPlayer, wantEditor, quickAdd);
    }

    /// <summary>
    /// Shared read/editor access decision, used by both the initial right-click interaction and
    /// the mid-session mode-switch message. Read access is always granted and never touches the
    /// lock. Editor access is granted only if the lock is free or already held by this player;
    /// a refusal still attaches the current document so the requester can fall back to reading
    /// it rather than seeing nothing.
    /// </summary>
    private void RequestAccess(ICoreServerAPI sapi, IServerPlayer byPlayer, bool wantEditor, bool quickAdd)
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
        // Re-sync so other clients see the lock is now held and can disable their editor affordance
        // (fix-multiplayer-editor-lock §2.1). redrawOnClient:false — this only changes lock state, not the
        // document, so no read-view rebuild is needed; the tree attr rides the next block-entity packet.
        MarkDirty();
        SendReply(sapi, byPlayer, granted: true, editorMode: true, refusalReason: null, quickAdd: quickAdd);
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
            ModSystem?.RegisterHost(this); // re-register in case the DocId changed (e.g. a fresh document in tests)
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
    /// Server-side: change a task's text on the authoritative document, addressed by its stable
    /// <see cref="ScribeBlock.TaskId"/> — the write-through target of the identity-addressed pin-edit path
    /// (<see cref="ScribeEditPinnedTaskMessage"/>). Mirrors <see cref="SetTaskDoneFromReader"/>: lock-free
    /// (an always-allowed action any viewer may perform, even while another player holds the editor lock),
    /// mutating the authoritative document in place through the Core <see cref="ScribeDocument.SetTaskText"/>
    /// path so the blank/whitespace-only rejection invariant holds. A blank edit, a TaskId with no matching
    /// (or non-task) block, or a no-op is left unwritten. Returns whether it wrote. Does NOT touch pins — the
    /// caller (<c>ScribeModSystem</c>) reconciles the acting player's snapshot separately; other players'
    /// pins are deliberately left alone (grief-proof, player-owned pins).
    ///
    /// <para>Race caveat (same as <see cref="SetTaskDoneFromReader"/>): because this is lock-free by design,
    /// a concurrent whole-document <see cref="ApplyEdit"/> under the edit lock can clobber this write (and
    /// vice versa). The window is small and last-write-wins, consistent with the done-flag path.</para>
    /// </summary>
    public bool SetTaskTextFromReader(Guid taskId, string text)
    {
        if (Api is not ICoreServerAPI) return false;

        var block = Document.FindByTaskId(taskId);
        if (block is null || !block.IsTask) return false;
        if (block.Text == text) return false;

        if (!Document.SetTaskText(taskId, text)) return false; // rejects blank/whitespace-only
        MarkDirty(redrawOnClient: true);
        return true;
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

    /// <summary>
    /// Server-side: move a task to the BOTTOM of the authoritative document by its stable
    /// <see cref="ScribeBlock.TaskId"/> — the write-through for the <c>Sink</c> completion policy
    /// (scribe-lectern-view-consistency). Lock-free like <see cref="SetTaskDoneFromReader"/>. Returns
    /// whether the task moved (false when the id is unknown or the task is already last). Does NOT touch
    /// pins.
    /// </summary>
    public bool MoveTaskToBottomFromReader(Guid taskId)
    {
        if (Api is not ICoreServerAPI) return false;

        if (Document.MoveTaskToBottom(taskId))
        {
            MarkDirty(redrawOnClient: true);
            return true;
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

    /// <summary>Server-side: releases the lock, e.g. when the editing player closes the GUI or switches to read view.
    /// Idempotent + UID-guarded: only the current holder's own release clears it, so a redundant release
    /// (e.g. release-on-every-close for a player who holds no lock) is a harmless no-op
    /// (fix-transient-lectern-editor-lock).</summary>
    public void ReleaseLock(string playerUid)
    {
        if (lockHolderUid == playerUid)
        {
            lockHolderUid = null;
            // Re-sync so other clients see the lock is free again and re-enable their editor affordance
            // (fix-multiplayer-editor-lock §2.1). Also covers the disconnect path (OnPlayerDisconnect).
            MarkDirty();
        }
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        ReleaseLock(player.PlayerUID);
    }

    private void SendReply(ICoreServerAPI sapi, IServerPlayer toPlayer, bool granted, bool editorMode, string? refusalReason, bool quickAdd = false)
    {
        var reply = new ScribeEditDocumentMessage
        {
            DocIdBytes = Document.DocId.ToByteArray(),
            Granted = granted,
            EditorMode = editorMode,
            RefusalReason = refusalReason,
            QuickAdd = quickAdd,
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
            else
            {
                // Editor access denied while the dialog is already open — e.g. a Back-from-settings that
                // re-requested the editor lock but another player grabbed it first (add-settings-tab round
                // 1). Fall back to the read view so the dialog can't be stranded on a stale view; the error
                // toast above already told the player why. (The save-failed recovery returned earlier.)
                dialog!.EnterReadMode();
            }
            return;
        }

        if (!open)
        {
            OpenDialog(capi);
            // Notify server to record this player as a visitor (fire-and-forget; server deduplicates).
            capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeRecordVisitorMessage
            {
                DocIdBytes = Document.DocId.ToByteArray(),
            });
        }

        if (message.EditorMode)
        {
            dialog!.EnterEditorMode(message.DocumentBytes);
            // Quick-add gesture (Shift+right-click): now that the editor holds the lock and the scratch is
            // seeded, insert a fresh empty task at the top and focus its caret (add-unified-quick-add-
            // interaction). Threaded through the round-trip because the dialog didn't exist at interact time.
            if (message.QuickAdd) dialog!.QuickAddTopTask();
        }
        else
        {
            dialog!.EnterReadMode();
        }
    }

    /// <summary>Sends the current guestbook to a specific client. Called after a new entry is
    /// recorded so the opening player sees their own entry immediately.</summary>
    public void SendGuestbookSync(ICoreServerAPI sapi, IServerPlayer toPlayer)
    {
        sapi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeGuestbookSyncMessage
        {
            DocIdBytes = Document.DocId.ToByteArray(),
            GuestbookBytes = _guestbook.Serialize(),
        }, toPlayer);
    }

    /// <summary>Server-side: record a visitor entry for this player. If a new entry was added, marks
    /// the block dirty and sends the updated guestbook back to the opening client.</summary>
    public void RecordVisitor(ICoreServerAPI sapi, IServerPlayer player)
    {
        var cal     = sapi.World.Calendar;
        int dayOfMonth = (int)(cal.TotalDays % cal.DaysPerMonth) + 1;
        var date = $"{dayOfMonth} {Lang.Get("month-" + cal.MonthName)}, Year {cal.Year}";
        if (_guestbook.TryAddEntry(player.PlayerName, date))
        {
            MarkDirty();
            SendGuestbookSync(sapi, player);
        }
    }

    /// <summary>Server-only: seed fictional guestbook visitors for demo/screenshot capture. Mirrors
    /// <see cref="RecordVisitor"/> (append via <see cref="GuestbookStore.TryAddEntry"/>, optional note via
    /// <see cref="GuestbookStore.TrySetNote"/>), then persists + re-syncs the read view once via
    /// <see cref="BlockEntity.MarkDirty"/>. Each entry is <c>(visitorName, inGameDate, note)</c>; a null/empty
    /// note is skipped. No-op off the server. An open guestbook tab won't repaint live (a dev-tool trade-off —
    /// reopen the lectern to see seeded entries); the read view refreshes via the block-entity packet.</summary>
    public void SeedGuestbook(IEnumerable<(string VisitorName, string InGameDate, string? Note)> entries)
    {
        if (Api is not ICoreServerAPI) return;

        bool changed = false;
        foreach (var (name, date, note) in entries)
        {
            if (_guestbook.TryAddEntry(name, date)) changed = true;
            if (!string.IsNullOrEmpty(note) && _guestbook.TrySetNote(name, date, note)) changed = true;
        }

        if (changed) MarkDirty(redrawOnClient: true);
    }

    /// <summary>Server-side: update the note on the sender's own guestbook entry for the given in-game
    /// day. Addressed by <c>(player name, inGameDate)</c> so a player with several entries edits only the
    /// intended day's note; an unmatched date is a harmless no-op.</summary>
    public void UpdateGuestbookNote(ICoreServerAPI sapi, IServerPlayer player, string inGameDate, string note)
    {
        note = note.Trim();
        if (_guestbook.TrySetNote(player.PlayerName, inGameDate, note))
        {
            MarkDirty();
            SendGuestbookSync(sapi, player);
        }
    }

    /// <summary>Client-side: apply an incoming guestbook sync from the server.</summary>
    public void ApplyGuestbookSync(byte[]? bytes)
    {
        _guestbook = GuestbookStore.Deserialize(bytes);
        dialog?.RefreshGuestbookView();
    }

    /// <summary>Creates and opens the single LibGUI dialog (read view by default). The "switch to
    /// editor" / "done editing" controls drive the view swap and lock request/release from inside the
    /// dialog through the normal server flow.</summary>
    private void OpenDialog(ICoreClientAPI capi)
    {
        dialog = new GuiDialogScribeLecternLibGui(Pos, this, capi);
        dialog.TryOpen();
    }

    /// <summary>Client-side: draw the block shape rotated to <see cref="MeshAngleRad"/> so the open-book
    /// face points the way it was placed. Mirrors <c>BlockEntitySign.OnTesselation</c>: builds the mesh
    /// once per distinct angle via the object cache and feeds it to the chunk mesher (returning true to
    /// suppress the default un-rotated block mesh).</summary>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (Api is not ICoreClientAPI capi || Block?.Shape?.Base is null)
        {
            return base.OnTesselation(mesher, tessThreadTesselator);
        }

        mesh = ObjectCacheUtil.GetOrCreate(Api, "scribelecternmesh-" + Block.Code + "-" + MeshAngleRad, () =>
        {
            var shape = capi.TesselatorManager.GetCachedShape(Block.Shape.Base);
            capi.Tesselator.TesselateShape(Block, shape, out var meshData, new Vec3f(0f, MeshAngleRad * (180f / (float)Math.PI), 0f));
            return meshData;
        });

        mesher.AddMeshData(mesh);
        return true;
    }

    /// <summary>World-edit / schematic rotation parity (vanilla Sign pattern): adjust the stored facing
    /// by the rotation amount so a rotated build keeps the lectern pointing correctly.</summary>
    public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation,
        Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, EnumAxis? flipAxis)
    {
        float angle = tree.GetFloat("meshAngle", 0f) - degreeRotation * ((float)Math.PI / 180f);
        tree.SetFloat("meshAngle", angle);
    }
}
