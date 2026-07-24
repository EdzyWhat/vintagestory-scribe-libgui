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
    private const string DocumentAttributeKey = "scribeDocument";

    public ScribeDocument Document { get; private set; } = new();

    /// <summary>Server-side only: the UID of the player currently editing, if any.</summary>
    private string? lockHolderUid;

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
        }
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (Api is ICoreServerAPI sapi)
        {
            sapi.Event.PlayerDisconnect -= OnPlayerDisconnect;
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
        Document = ScribeDocumentCodec.TryDeserialize(bytes, out var doc) && doc is not null
            ? doc
            : new ScribeDocument();

        // Reflect an authoritative resync in the open dialog. RefreshReadView rebuilds the read view
        // from the now-current Document; it is a no-op while the dialog is in editor mode (the editor
        // edits a private scratch copy that an external resync must not clobber).
        dialog?.RefreshReadView();
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
            MarkDirty(redrawOnClient: true);
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
    /// Server-side: toggles a single task's done state on behalf of a read-view viewer. Unlike
    /// <see cref="ApplyEdit"/> this does NOT require the editor lock — ticking a task off is an
    /// always-allowed action any viewer may perform, even while another player holds the lock
    /// (see <see cref="ScribeToggleTaskMessage"/>). Mutates the authoritative document in place
    /// (not a client-submitted copy), so it only ever changes the one flag and cannot clobber a
    /// concurrent editor's in-flight text edits beyond that. A bad or non-task index is a no-op
    /// (<see cref="ScribeDocument.ToggleTask"/> returns false), in which case nothing is persisted.
    /// </summary>
    public void ToggleTaskFromReader(int index)
    {
        if (Api is not ICoreServerAPI) return;

        if (Document.ToggleTask(index))
        {
            MarkDirty(redrawOnClient: true);
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
