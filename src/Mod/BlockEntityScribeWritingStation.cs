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
/// Shared base for Scribe's placed writing-station block entities (Lectern, Scriptorium, ...).
/// Persistence follows the vanilla Sign pattern. Edits are server-authoritative: the client sends a
/// request and the server applies it and calls <see cref="BlockEntity.MarkDirty"/>. Read view is lock-free;
/// only one player holds the editor lock (server-tracked, released on close/mode-switch/disconnect).
/// <para>Subclasses supply only per-block identity/config; everything else is shared here.</para>
/// </summary>
public abstract class BlockEntityScribeWritingStation : BlockEntity, IRotatable, IScribeDocumentHost
{
    private const string DocumentAttributeKey = ScribeDocumentAttributes.DocumentAttributeKey;

    // ── Per-block config supplied by the concrete subclass ────────────────

    /// <summary>The GUI page backdrop for this block's dialog (e.g. the Lectern illustration).</summary>
    protected abstract ScribeBackdropSpec PageBackdrop { get; }

    /// <summary>The dialog page's art aspect ratio (height / width), used to size the layout.</summary>
    protected abstract float PageAspect { get; }

    /// <summary>Optional per-block override of the layout proportions; null uses
    /// <see cref="ScribeLayoutProportions.Default"/>.</summary>
    protected virtual ScribeLayoutProportions? LayoutProportions => null;

    /// <summary>Optional FIXED facing angle (radians) for a wall-mounted subclass; null keeps the
    /// player-facing angle stored at placement. Applied in <see cref="Initialize"/> because the custom
    /// mesh path in <see cref="OnTesselation"/> ignores the shape's <c>rotateYByType</c>.</summary>
    protected virtual float? WallMountAngleRad => null;

    /// <summary>Optional live-tunable <see cref="ScribeBoxTuning"/> target for this instance's
    /// collision/selection box; null reads the baked <c>Block.CollisionBoxes[0]</c>/<c>SelectionBoxes[0]</c>.</summary>
    protected virtual ScribeBoxTuningTarget? TuningTarget => null;

    /// <summary>Lang key for the document's fallback title when the player clears the title and saves
    /// (e.g. <c>"scribe:doctitle-lectern"</c>).</summary>
    protected abstract string DefaultDocumentTitleKey { get; }

    /// <summary>Object-cache key prefix for this block's rotated mesh. MUST be distinct per block type
    /// so the Lectern and Scriptorium never collide in the shared mesh cache.</summary>
    protected abstract string MeshCacheKeyPrefix { get; }

    /// <summary>Per-tier document cap reported through <see cref="IScribeDocumentHost.Policy"/>; default
    /// Unlimited. The explicit interface member delegates here because a bare <c>Policy</c> property on a
    /// subclass would NOT re-map the interface's default member (see <c>NotebookHost.Policy</c>).</summary>
    protected virtual ScribeDocumentPolicy HostPolicy => ScribeDocumentPolicy.Unlimited;

    /// <summary>Create the concrete LibGUI dialog for this block (client-side). The two views (read /
    /// editor) are internal states of the returned dialog; a subclass may add its own nav buttons.</summary>
    protected abstract ScribeDialogBase CreateDialog(ICoreClientAPI capi);

    /// <summary>Client-side rotated mesh, rebuilt when <see cref="MeshAngleRad"/> changes.</summary>
    private MeshData? mesh;

    public ScribeDocument Document { get; private set; } = new();

    /// <summary>Horizontal placement angle (radians) turning the reading face toward the placing player.
    /// Persisted as "meshAngle"; applied to the mesh and the collision/selection boxes. The shape's authored
    /// front is SOUTH (+Z) at angle 0 (see VSAPI-NOTES "Block placement orientation").</summary>
    private float meshAngleRad;

    /// <summary>The COLLISION box rotated to <see cref="MeshAngleRad"/>. Computed live so
    /// <see cref="ScribeBoxTuning"/> changes apply immediately. Uses the tuning box when
    /// <see cref="TuningTarget"/> is set and <see cref="ScribeBoxTuning.HasCollisionBox"/> is true, else
    /// <c>Block.CollisionBoxes[0]</c>. Null when the block has no collision box (e.g. the walk-through Chalkboard).</summary>
    public Cuboidf[]? RotatedBox => Rotated(TuningTarget is { } t && ScribeBoxTuning.HasCollisionBox(t)
        ? ModSystem?.BoxTuning.CollisionBoxFor(t)
        : (Block?.CollisionBoxes is { Length: > 0 } c ? c[0] : null));

    /// <summary>The SELECTION box rotated to <see cref="MeshAngleRad"/>. Separate from
    /// <see cref="RotatedBox"/> so a block can have a selection slab without collision (the Chalkboard).
    /// Computed live; a tuned target uses the same box for both. Null falls back to the un-rotated JSON box.</summary>
    public Cuboidf[]? RotatedSelectionBox => Rotated(TuningTarget is { } t
        ? ModSystem?.BoxTuning.CollisionBoxFor(t)
        : (Block?.SelectionBoxes is { Length: > 0 } s ? s[0] : null));

    /// <summary>Rotates a source box to <see cref="MeshAngleRad"/>; null in, null out.</summary>
    private Cuboidf[]? Rotated(Cuboidf? source)
    {
        if (source is null) return null;
        float deg = meshAngleRad * (180f / (float)Math.PI);
        return new[] { source.RotatedCopy(0f, deg, 0f, new Vec3d(0.5, 0.5, 0.5)) };
    }

    public float MeshAngleRad
    {
        get => meshAngleRad;
        set
        {
            bool changed = meshAngleRad != value;
            meshAngleRad = value;
            if (changed) MarkDirty(true);
        }
    }

    /// <summary>Server-side only: the UID of the player currently editing, if any.</summary>
    private string? lockHolderUid;

    /// <summary>Client-side mirror of <see cref="lockHolderUid"/>, synced via the tree to drive the editor
    /// affordance without a round-trip. Null when the lock is free.</summary>
    private string? syncedLockHolderUid;

    /// <summary>Client-side: true when the editor lock is held by a player other than
    /// <paramref name="viewerUid"/>. Only drives affordance state; the server refusal is authoritative.</summary>
    public bool IsLockedByOther(string viewerUid) =>
        syncedLockHolderUid is not null && syncedLockHolderUid != viewerUid;

    /// <summary>Durable access mode, persisted and synced. Dormant: nothing sets it away from
    /// <see cref="ScribeAccessMode.Public"/> and the editor-entry gate does not read it.</summary>
    private ScribeAccessMode accessMode = ScribeAccessMode.Public;

    /// <summary>Client-side mirror of <see cref="accessMode"/>. Dormant: no control reads it.</summary>
    private ScribeAccessMode syncedAccessMode = ScribeAccessMode.Public;

    /// <summary>Server-side: DocId registered in the pin store's live index, so exactly that id is
    /// unregistered on removal. Null until the first register.</summary>
    private Guid? registeredDocId;

    /// <summary>Server-side: true when a v4 document (no title field) was loaded and must be re-saved as v5
    /// on first server init.</summary>
    private bool needsV5Resave;

    private GuestbookStore _guestbook = new();

    /// <summary>Persisted Read View filter pill (a <see cref="ReadViewFilterCategory"/> as byte); default All (0).</summary>
    private byte readViewFilterCategory;

    /// <summary>Persisted collapsed subtask-group parent TaskIds; empty by default.</summary>
    private HashSet<Guid> collapsedGroupIds = new();

    /// <summary>Sample interval (ms) for the particle indicator's periodic check.</summary>
    private const int AssignmentParticleTickIntervalMs = 1500;

    // ── IScribeDocumentHost explicit implementations ──────────────────────
    ScribeDocument IScribeDocumentHost.Document => Document;
    bool IScribeDocumentHost.IsLockedByOther(string viewerUid) => IsLockedByOther(viewerUid);
    void IScribeDocumentHost.ApplyLocalOptimisticEdit(ScribeDocument doc) => ApplyLocalOptimisticEdit(doc);
    ScribeBackdropSpec IScribeDocumentHost.BackdropSpec => PageBackdrop;
    ScribeDocumentPolicy IScribeDocumentHost.Policy => HostPolicy;
    ScribeLayout IScribeDocumentHost.GetLayout(float w) => new ScribeLayout(w, PageAspect, LayoutProportions);
    string IScribeDocumentHost.DefaultDocumentTitle => Lang.Get(DefaultDocumentTitleKey);
    GuestbookStore IScribeDocumentHost.Guestbook => _guestbook;
    void IScribeDocumentHost.SetTaskDoneFromReader(Guid taskId, bool done) => SetTaskDoneFromReader(taskId, done);
    bool IScribeDocumentHost.DeleteTaskFromReader(Guid taskId) => DeleteTaskFromReader(taskId);
    void IScribeDocumentHost.PersistFromReader() => PersistFromReader();
    bool IScribeDocumentHost.MoveTaskToBottomFromReader(Guid taskId) => MoveTaskToBottomFromReader(taskId);
    bool IScribeDocumentHost.SetTaskTextFromReader(Guid taskId, string text) => SetTaskTextFromReader(taskId, text);
    bool IScribeDocumentHost.SetTrackerCurrentQuantityFromReader(Guid taskId, int qty) => SetTrackerCurrentQuantityFromReader(taskId, qty);
    bool IScribeDocumentHost.SetQuestObjectiveProgressFromReader(Guid taskId, int qty) => SetQuestObjectiveProgressFromReader(taskId, qty);
    byte IScribeDocumentHost.ReadViewFilterCategory => readViewFilterCategory;
    IReadOnlyCollection<Guid> IScribeDocumentHost.CollapsedGroupIds => collapsedGroupIds;
    void IScribeDocumentHost.SetReadViewStateFromReader(byte filterCategory, IReadOnlyCollection<Guid> collapsedIds) =>
        SetReadViewStateFromReader(filterCategory, collapsedIds);

    /// <summary>Client-side: the single dialog serving both read and editor views.</summary>
    private ScribeDialogBase? dialog;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        // Register on both sides so each side's registry can route packets to this BE.
        ModSystem?.RegisterHost(this);

        if (api is ICoreServerAPI sapi)
        {
            sapi.Event.PlayerDisconnect += OnPlayerDisconnect;

            // The editor lock is session-only state: a freshly-loaded block must start unlocked.
            // (FromTreeAttributes only writes the client mirror.)
            lockHolderUid = null;

            // Register DocId -> position for pins; re-save a v4-loaded document as v5.
            RegisterDocInStore();
            if (needsV5Resave)
            {
                needsV5Resave = false;
                MarkDirty();
            }
        }

        // Wall-mounted subclasses (Chalkboard) take a fixed facing from their `side` variant. Set here
        // because Initialize runs on both fresh placement and chunk load (after FromTreeAttributes on load).
        if (api is ICoreClientAPI capi)
        {
            if (WallMountAngleRad is float wallAngle)
            {
                MeshAngleRad = wallAngle;
            }

            // Unseen-assignment particle indicator. The BE's own RegisterGameTickListener is cleaned up
            // automatically on removal/unload.
            RegisterGameTickListener(OnAssignmentParticleTick, AssignmentParticleTickIntervalMs);
        }
    }

    /// <summary>Whether the previous tick's proximity+unseen check was true, so a false-to-true edge
    /// requests a seed burst. Client-side, transient.</summary>
    private bool assignmentParticlesWereActive;

    /// <summary>Client-side periodic check: spawns a mote batch when the local player has an unseen
    /// received assignment and is within <see cref="ScribeAssignmentParticleEmitter.DetectionRadius"/>.
    /// Local-only; edge-detects activation to request a seed burst.</summary>
    private void OnAssignmentParticleTick(float dt)
    {
        if (Api is not ICoreClientAPI capi) return;

        var emitter = ModSystem?.ParticleEmitter;
        bool active = emitter is not null && ModSystem is { HasUnseenUndeliveredAssignment: true };
        if (active)
        {
            var player = capi.World.Player?.Entity;
            active = player is not null
                && Pos.DistanceTo(player.Pos.X, player.Pos.Y, player.Pos.Z) <= emitter!.DetectionRadius;
        }

        if (!active)
        {
            assignmentParticlesWereActive = false;
            return;
        }

        emitter!.SpawnAt(capi, Pos, seedBurst: !assignmentParticlesWereActive);
        assignmentParticlesWereActive = true;
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved(); // unregisters the particle tick listener (UnregisterAllTickListeners)

        if (Api is ICoreServerAPI sapi)
        {
            sapi.Event.PlayerDisconnect -= OnPlayerDisconnect;

            // Forget the live position, but do NOT clear pins: breaking a block to relocate it drops an
            // item carrying the document, and OnBlockPlaced restores the same DocId. A pin is removed only
            // by its owner's action (see ScribePinStore.ReconcileSnapshotsForActor), never on block removal.
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
        // Editor-lock holder, synced so clients can show a held lock. Empty string = free.
        // Transient: never authoritative across load.
        tree.SetString("lockHolder", lockHolderUid ?? "");
        // Durable access mode (dormant; always Public), stored as the underlying byte.
        tree.SetInt("accessMode", (byte)accessMode);
        tree.SetBytes("guestbook", _guestbook.Serialize());
        // Per-instance viewer-preference state; absent keys default on load.
        tree.SetInt("readViewFilter", readViewFilterCategory);
        tree.SetBytes("readViewCollapsed", ScribeReadViewFilter.SerializeGuidSet(collapsedGroupIds));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        // A wall-mounted subclass derives its angle from its `side` variant, so honor that first: the
        // persisted `meshAngle` is 0 for a wall block (Initialize sets it client-side only) and the board
        // would snap back to 0 on the next MarkDirty. Floor stations use the persisted angle, or the
        // shape's authored rotateY for pre-orientation blocks. Goes through the property so the rotated
        // hitbox is rebuilt.
        MeshAngleRad = WallMountAngleRad
            ?? (tree.HasAttribute("meshAngle")
                ? tree.GetFloat("meshAngle", 0f)
                : (Block?.Shape?.rotateY ?? 0f) * ((float)Math.PI / 180f));

        // Client mirror of the editor-lock holder; empty string (lock free) maps to null. The server
        // keeps lockHolderUid authoritative.
        var lockHolder = tree.GetString("lockHolder", "");
        syncedLockHolderUid = string.IsNullOrEmpty(lockHolder) ? null : lockHolder;

        // Absent key -> Public. The field is dormant.
        var mode = (ScribeAccessMode)(byte)tree.GetInt("accessMode", (int)ScribeAccessMode.Public);
        accessMode = mode;
        syncedAccessMode = mode;

        // Absent keys default to All / fully expanded.
        readViewFilterCategory = (byte)tree.GetInt("readViewFilter", 0);
        collapsedGroupIds = ScribeReadViewFilter.DeserializeGuidSet(tree.GetBytes("readViewCollapsed"));

        var bytes = tree.GetBytes(DocumentAttributeKey);
        needsV5Resave = ScribeDocumentCodec.IsPriorVersion(bytes);
        Document = ScribeDocumentCodec.TryDeserialize(bytes, out var doc, out _) && doc is not null
            ? doc
            : new ScribeDocument();
        _guestbook = GuestbookStore.Deserialize(tree.GetBytes("guestbook"));

        // A resync may have replaced the document; keep the live index pointing at the current one.
        RegisterDocInStore();

        // Re-key the host registry under the now-current DocId. Each side's Document starts with a random
        // DocId; the authoritative one only arrives here. For a freshly placed block Initialize runs BEFORE
        // FromTreeAttributes, so without this the client stays keyed under a dead id and the server's open
        // reply never resolves a host. No-op when Api isn't set yet (load path); Initialize registers then.
        ModSystem?.RegisterHost(this);

        // Reflect a resync in the open dialog. No-op in editor mode: the editor's private scratch copy
        // must not be clobbered.
        dialog?.RefreshReadView();

        // Repaint an open inventory tab (Scriptorium only) after a synced slot change; no-op for other dialogs.
        dialog?.RefreshInventoryView();

        // Same for the Inbox block's own Inbox Inventory tab.
        dialog?.RefreshInboxInventoryView();
    }

    /// <summary>Restores a document carried on a placed item stack (break→re-place) so content, ids and
    /// pins come back; empty document if the stack carries none. Server-side.</summary>
    public override void OnBlockPlaced(ItemStack? byItemStack)
    {
        base.OnBlockPlaced(byItemStack);

        if (Api is not ICoreServerAPI) return;
        if (byItemStack is not null && ScribeDocumentAttributes.TryReadFrom(byItemStack, out var doc) && doc is not null)
        {
            Document = doc;

            // Creative middle-click CLONE stamps the source's DocId on the copy's stack, like break→re-place.
            // Two live blocks under one id would break host-registry and pin-store routing, so mint a fresh
            // identity when the id is still registered to a different live block (OnBlockRemoved already
            // unregistered the source on break→re-place). The copy keeps content but starts with no pins.
            if (ModSystem?.IsDocIdRegisteredToOtherBlock(Document.DocId, Pos) == true)
            {
                Document.ReassignNewDocId();
            }

            RegisterDocInStore();
            ModSystem?.RegisterHost(this); // re-register under the restored (or freshly-minted) DocId
            MarkDirty();
        }
    }

    /// <summary>Server-side: points the pin store's live index at this document's current position,
    /// unregistering a stale prior DocId. No-op on the client or before the store exists.</summary>
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

    /// <summary>Server-side accessor for the pin store; null on the client.</summary>
    private ScribePinStore? PinStore => ModSystem?.PinStore;

    protected ScribeModSystem? ModSystem => Api?.ModLoader.GetModSystem<ScribeModSystem>();

    /// <summary>
    /// Called from <see cref="BlockScribeWritingStation.OnBlockInteractStart"/> on either side; acts
    /// server-side only: decides access and replies to the requesting player.
    /// </summary>
    public void OnRightClick(IPlayer byPlayer, bool wantEditor, bool quickAdd)
    {
        if (Api is not ICoreServerAPI sapi || byPlayer is not IServerPlayer serverPlayer)
        {
            return;
        }

        RequestAccess(sapi, serverPlayer, wantEditor, quickAdd);
    }

    /// <summary>Server-side: handles a mid-session read/editor mode-switch request (never a quick-add
    /// from the nav button or recovery path, so those pass false).</summary>
    public void OnRequestAccess(IServerPlayer fromPlayer, bool wantEditor, bool quickAdd)
    {
        if (Api is not ICoreServerAPI sapi)
        {
            return;
        }

        RequestAccess(sapi, fromPlayer, wantEditor, quickAdd);
    }

    /// <summary>
    /// Shared read/editor access decision. Read is always granted and never touches the lock; editor is
    /// granted only if the lock is free or already held by this player. A refusal still attaches the
    /// document so the requester can read it.
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
        // Re-sync so other clients see the lock held.
        MarkDirty();
        SendReply(sapi, byPlayer, granted: true, editorMode: true, refusalReason: null, quickAdd: quickAdd);
    }

    /// <summary>
    /// Server-side: applies an editor autosave and re-syncs everyone. Returns false if the sender does
    /// not hold the lock, so the caller can ack failure.
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
            // The editor's checkbox relies on this whole-document flush to persist Done.
            ModSystem?.NotifyDoneAssignmentsInDocument(doc);
        }

        return true;
    }

    /// <summary>
    /// Client-side: optimistically adopts the document just flushed so switching to read view doesn't
    /// show pre-edit content; the authoritative resync supersedes it.
    /// </summary>
    public void ApplyLocalOptimisticEdit(ScribeDocument doc)
    {
        Document = doc;
    }

    /// <summary>
    /// Server-side: sets a task's done state by stable <see cref="ScribeBlock.TaskId"/> (target of
    /// <see cref="ScribeCompleteTaskMessage"/>). Unlike <see cref="ApplyEdit"/> it does NOT require the
    /// editor lock: ticking a task off is allowed while another player edits. Mutates only that flag in
    /// place; an unknown or non-task id is a no-op. Does NOT touch pins (the caller reconciles the acting
    /// player's; other players' pins are left alone).
    /// </summary>
    public void SetTaskDoneFromReader(Guid taskId, bool done)
    {
        if (Api is not ICoreServerAPI) return;

        var block = Document.FindByTaskId(taskId);
        if (block is null || !block.IsCompletable) return;
        if (block.Done == done) return;

        block.Done = done;
        MarkDirty(redrawOnClient: true);
    }

    /// <summary>
    /// Server-side: changes a task's text by stable <see cref="ScribeBlock.TaskId"/> (target of
    /// <see cref="ScribeEditPinnedTaskMessage"/>). Lock-free like <see cref="SetTaskDoneFromReader"/>; goes
    /// through <see cref="ScribeDocument.SetTaskText"/> so blank text is rejected. Returns whether it wrote.
    /// Does NOT touch pins.
    ///
    /// <para>Race: a concurrent whole-document <see cref="ApplyEdit"/> under the edit lock can clobber this
    /// write and vice versa (last write wins).</para>
    /// </summary>
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
    /// Server-side: sets a Tracker's <see cref="ScribeBlock.CurrentQuantity"/> by stable
    /// <see cref="ScribeBlock.TaskId"/>. Lock-free like <see cref="SetTaskDoneFromReader"/>. Routes through
    /// <see cref="ScribeDocument.SetTrackerCurrentQuantity"/> (>= 0 clamp, overflow above target preserved).
    /// Returns whether the value changed; a non-Tracker or unknown id is unwritten. Does NOT touch pins.
    /// </summary>
    public bool SetTrackerCurrentQuantityFromReader(Guid taskId, int qty)
    {
        if (Api is not ICoreServerAPI) return false;

        var block = Document.FindByTaskId(taskId);
        if (block is null || !block.IsTracker) return false;
        if (block.CurrentQuantity == Math.Max(0, qty)) return false;

        if (!Document.SetTrackerCurrentQuantity(taskId, qty)) return false;
        MarkDirty(redrawOnClient: true);
        return true;
    }

    /// <summary>
    /// Server-side: sets a QuestObjective's <see cref="ScribeBlock.CurrentQuantity"/> by stable
    /// <see cref="ScribeBlock.TaskId"/>. Lock-free; routed through
    /// <see cref="ScribeDocument.SetQuestObjectiveProgress"/> (clamps into <c>[0, TargetQuantity]</c>, no
    /// visible overflow). A no-op, unknown id, or non-QuestObjective block is left unwritten. Does NOT touch pins.
    /// </summary>
    public bool SetQuestObjectiveProgressFromReader(Guid taskId, int qty)
    {
        if (Api is not ICoreServerAPI) return false;

        var block = Document.FindByTaskId(taskId);
        if (block is null || !block.IsQuestObjective) return false;
        int clamped = Math.Clamp(qty, 0, block.TargetQuantity);
        if (block.CurrentQuantity == clamped) return false;

        if (!Document.SetQuestObjectiveProgress(taskId, qty)) return false;
        MarkDirty(redrawOnClient: true);
        return true;
    }

    /// <summary>
    /// Server-side: deletes a task by stable <see cref="ScribeBlock.TaskId"/> (the <c>Delete</c> completion
    /// policy). Lock-free; returns whether a task was removed. Does NOT touch pins.
    /// </summary>
    public bool DeleteTaskFromReader(Guid taskId)
    {
        if (Api is not ICoreServerAPI) return false;

        for (int i = 0; i < Document.Blocks.Count; i++)
        {
            if (Document.Blocks[i].TaskId == taskId && Document.Blocks[i].IsCompletable)
            {
                Document.DeleteBlock(i);
                MarkDirty(redrawOnClient: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Server-side: overwrites the persisted Read View filter pill and collapsed-group set. Lock-free
    /// viewer preference, shared by every viewer.
    /// </summary>
    public void SetReadViewStateFromReader(byte filterCategory, IReadOnlyCollection<Guid> collapsedGroupIds)
    {
        if (Api is not ICoreServerAPI) return;

        readViewFilterCategory = filterCategory;
        this.collapsedGroupIds = new HashSet<Guid>(collapsedGroupIds);
        MarkDirty(redrawOnClient: true);
    }

    /// <summary>Server-side: persist after a Core mutation already applied to <see cref="Document"/>.</summary>
    public void PersistFromReader()
    {
        if (Api is not ICoreServerAPI) return;
        MarkDirty(redrawOnClient: true);
    }

    /// <summary>
    /// Server-side: moves a task to the BOTTOM by stable <see cref="ScribeBlock.TaskId"/> (the <c>Sink</c>
    /// completion policy). Lock-free; false when the id is unknown or the task is already last. Does NOT touch pins.
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

    /// <summary>Server-side: reconciles only the acting player's pins into the edited document
    /// (grief-proof: only the owner's own edit changes a pin), then re-pushes that player.</summary>
    private void ReconcileActorPins(string actingPlayerUid)
    {
        if (PinStore is { } store && ModSystem is { } mod)
        {
            mod.PushPinsTo(store.ReconcileSnapshotsForActor(actingPlayerUid, Document.DocId, Document));
        }
    }

    /// <summary>Server-side: releases the lock. UID-guarded and idempotent: only the holder's release clears it.</summary>
    public void ReleaseLock(string playerUid)
    {
        if (lockHolderUid == playerUid)
        {
            lockHolderUid = null;
            // Re-sync so other clients re-enable their editor affordance (also covers disconnect).
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
    /// Server-side: save-failure ack after an editor autosave, using the open/mode-switch reply shape.
    /// The happy path needs no ack.
    /// </summary>
    public void SendSaveFailedAck(ICoreServerAPI sapi, IServerPlayer toPlayer)
    {
        SendReply(sapi, toPlayer, granted: false, editorMode: true, refusalReason: "scribe:scribe-gui-save-failed");
    }

    /// <summary>
    /// Client-side: handles the server's reply to an open or mode-switch request. A grant opens the
    /// dialog if needed and swaps its view mode; a post-autosave failure ack surfaces the error and stays put.
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
            // Editor access refused (lock held elsewhere, or a lock-less autosave). Surface the error;
            // if nothing is open yet, fall back to the read view.
            capi.TriggerIngameError(this, "scribe-writing-station-locked", Lang.Get(message.RefusalReason ?? "scribe:scribe-gui-locked"));

            // A save-failed ack is recoverable: ask the open editor to re-request the lock and re-flush
            // its edit (bounded internally).
            if (open && message.RefusalReason == "scribe:scribe-gui-save-failed"
                && dialog!.HandleSaveFailed())
            {
                return;
            }

            if (!open)
            {
                OpenDialog(capi);
                dialog!.EnterGrantedView();
            }
            else
            {
                // Editor access denied while open (e.g. another player grabbed the lock): fall back to the
                // dialog's granted view so it isn't stranded on a stale one.
                dialog!.EnterGrantedView();
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
            // Quick-add (Shift+right-click): insert a fresh empty task at the top and focus its caret.
            // Threaded through the round-trip because the dialog didn't exist at interact time.
            if (message.QuickAdd) dialog!.QuickAddTopTask();
        }
        else
        {
            dialog!.EnterGrantedView();
        }
    }

    /// <summary>Sends the current guestbook to a specific client.</summary>
    public void SendGuestbookSync(ICoreServerAPI sapi, IServerPlayer toPlayer)
    {
        sapi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeGuestbookSyncMessage
        {
            DocIdBytes = Document.DocId.ToByteArray(),
            GuestbookBytes = _guestbook.Serialize(),
        }, toPlayer);
    }

    /// <summary>Server-side: records a visitor entry; if new, marks dirty and syncs the guestbook to that client.</summary>
    public void RecordVisitor(ICoreServerAPI sapi, IServerPlayer player)
    {
        var date = NotebookHost.FormatDate(sapi);
        if (_guestbook.TryAddEntry(player.PlayerName, date, sapi.World.Calendar.TotalDays))
        {
            MarkDirty();
            SendGuestbookSync(sapi, player);
        }
    }

    /// <summary>Server-only: seeds fictional guestbook visitors for demo/screenshot capture. Each entry is
    /// <c>(visitorName, inGameDate, note, timestamp)</c>; null/empty notes are skipped. An open guestbook
    /// tab won't repaint live.</summary>
    public void SeedGuestbook(IEnumerable<(string VisitorName, string InGameDate, string? Note, double InGameTimestamp)> entries)
    {
        if (Api is not ICoreServerAPI) return;

        bool changed = false;
        foreach (var (name, date, note, timestamp) in entries)
        {
            if (_guestbook.TryAddEntry(name, date, timestamp)) changed = true;
            if (!string.IsNullOrEmpty(note) && _guestbook.TrySetNote(name, date, note)) changed = true;
        }

        if (changed) MarkDirty(redrawOnClient: true);
    }

    /// <summary>Server-side: updates the note on the sender's own guestbook entry for the given in-game
    /// day; an unmatched date is a no-op.</summary>
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

    /// <summary>Creates and opens the single dialog (read view by default).</summary>
    private void OpenDialog(ICoreClientAPI capi)
    {
        dialog = CreateDialog(capi);
        dialog.TryOpen();
    }

    /// <summary>Client-side: draws the shape rotated to <see cref="MeshAngleRad"/> (mirrors
    /// <c>BlockEntitySign.OnTesselation</c>), cached per angle. Returns true to suppress the default mesh.</summary>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (Api is not ICoreClientAPI capi || Block?.Shape?.Base is null)
        {
            return base.OnTesselation(mesher, tessThreadTesselator);
        }

        mesh = ObjectCacheUtil.GetOrCreate(Api, MeshCacheKeyPrefix + "-" + Block.Code + "-" + MeshAngleRad, () =>
        {
            var shape = capi.TesselatorManager.GetCachedShape(Block.Shape.Base);
            capi.Tesselator.TesselateShape(Block, shape, out var meshData, new Vec3f(0f, MeshAngleRad * (180f / (float)Math.PI), 0f));
            return meshData;
        });

        mesher.AddMeshData(mesh);
        return true;
    }

    /// <summary>World-edit / schematic rotation parity (vanilla Sign pattern): adjusts the stored facing.</summary>
    public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation,
        Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, EnumAxis? flipAxis)
    {
        float angle = tree.GetFloat("meshAngle", 0f) - degreeRotation * ((float)Math.PI / 180f);
        tree.SetFloat("meshAngle", angle);
    }
}
