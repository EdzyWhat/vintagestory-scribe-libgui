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
/// Shared base for Scribe's placed writing-station block entities (the Lectern and the Scriptorium).
/// Holds this block's <see cref="ScribeDocument"/>, keyed by block position. Persistence and sync go
/// through <see cref="ToTreeAttributes"/>/<see cref="FromTreeAttributes"/> (the vanilla Sign pattern).
/// Editor-view edits are server-authoritative: the client never mutates its local document directly —
/// it sends a request, the server applies it and calls <see cref="BlockEntity.MarkDirty"/> to persist
/// and re-sync. Read view is lock-free and live: anyone can look at the document at any time, even
/// while another player is editing it. Only one player may hold the editor lock at a time
/// (server-tracked, released on close/mode-switch/disconnect).
///
/// <para>Subclasses supply only per-block identity/config: the GUI page backdrop and aspect, the
/// document's fallback title, the mesh-cache key prefix, and the concrete dialog to open. Everything
/// else — the document, mesh-angle placement, editor lock, guestbook, pin-store registration, and the
/// tree round-trip — is shared here so the two blocks cannot drift apart.</para>
/// </summary>
public abstract class BlockEntityScribeWritingStation : BlockEntity, IRotatable, IScribeDocumentHost
{
    private const string DocumentAttributeKey = ScribeDocumentAttributes.DocumentAttributeKey;

    // ── Per-block config supplied by the concrete subclass ────────────────

    /// <summary>The GUI page backdrop for this block's dialog (e.g. the Lectern illustration).</summary>
    protected abstract ScribeBackdropSpec PageBackdrop { get; }

    /// <summary>The dialog page's art aspect ratio (height / width), used to size the layout.</summary>
    protected abstract float PageAspect { get; }

    /// <summary>Optional per-block override of the layout column/band proportions. Null (default) uses
    /// <see cref="ScribeLayoutProportions.Default"/> — the Lectern/Scriptorium v1 split. A subclass whose
    /// backdrop art frames the content differently (e.g. the Chalkboard's wood-framed slate) overrides this
    /// to keep the tasks column and title/button bands within its own art. See <see cref="ScribeLayout"/>
    /// for what each fraction controls.</summary>
    protected virtual ScribeLayoutProportions? LayoutProportions => null;

    /// <summary>Optional FIXED facing angle (radians) for a wall-mounted subclass (the Chalkboard),
    /// derived from its <c>side</c> block variant. Null (default) keeps the free-standing Lectern/Scriptorium
    /// behaviour, where the angle is the player-facing value stored at placement. This is applied in
    /// <see cref="Initialize"/> because the custom rotated-mesh path in <see cref="OnTesselation"/> loads the
    /// shape by <c>Base</c> only and so ignores the block shape's own <c>rotateYByType</c> — the placed mesh
    /// rotates ONLY by <see cref="MeshAngleRad"/>, which is 0 for a wall block until we set it here.</summary>
    protected virtual float? WallMountAngleRad => null;

    /// <summary>Lang key for the document's fallback title when the player clears the title and saves
    /// (e.g. <c>"scribe:doctitle-lectern"</c>).</summary>
    protected abstract string DefaultDocumentTitleKey { get; }

    /// <summary>Object-cache key prefix for this block's rotated mesh. MUST be distinct per block type
    /// so the Lectern and Scriptorium never collide in the shared mesh cache.</summary>
    protected abstract string MeshCacheKeyPrefix { get; }

    /// <summary>The per-tier document cap this block reports through <see cref="IScribeDocumentHost.Policy"/>
    /// (refine-chalkboard). Default is <see cref="ScribeDocumentPolicy.Unlimited"/> so the Lectern/Scriptorium
    /// stay uncapped exactly as before. A capped block (the Chalkboard) overrides this seam; the explicit
    /// interface member below delegates to it, because — as with the other host members — a bare <c>Policy</c>
    /// property on a subclass would NOT re-map the interface's default member (see the note on
    /// <c>NotebookHost.Policy</c>). The dialog consults <see cref="ScribeDocumentPolicy.CanAdd"/> before adding
    /// a task, so overriding this seam is all a block needs to enforce a cap.</summary>
    protected virtual ScribeDocumentPolicy HostPolicy => ScribeDocumentPolicy.Unlimited;

    /// <summary>Create the concrete LibGUI dialog for this block (client-side). The two views (read /
    /// editor) are internal states of the returned dialog; a subclass may add its own nav buttons.</summary>
    protected abstract ScribeDialogBase CreateDialog(ICoreClientAPI capi);

    /// <summary>Client-side cached rotated mesh, rebuilt when <see cref="MeshAngleRad"/> changes
    /// (keyed by angle in the object cache, mirroring <c>BlockEntitySign</c>).</summary>
    private MeshData? mesh;

    public ScribeDocument Document { get; private set; } = new();

    /// <summary>Horizontal placement angle (radians) so the block's reading face turns toward the
    /// placing player — the vanilla Sign/clutter <c>MeshAngleRad</c> idiom. Set once in
    /// <see cref="BlockScribeWritingStation.TryPlaceBlock"/>, persisted as "meshAngle", and applied to
    /// both the tesselated mesh (<see cref="OnTesselation"/>) and the collision/selection box (below).
    /// The reused shape's authored front is SOUTH (+Z) at angle 0, so the raw player-facing angle points
    /// the block at the player with no per-piece offset (see VSAPI-NOTES "Block placement orientation").</summary>
    private float meshAngleRad;

    /// <summary>The block's COLLISION box rotated to <see cref="MeshAngleRad"/>, surfaced by the block's
    /// <c>GetCollisionBoxes</c> so the solid hitbox tracks the mesh. Null when the block has no collision
    /// box (e.g. the walk-through wall Chalkboard) — the block then falls back to its un-rotated JSON box
    /// (which for the Chalkboard is none).</summary>
    public Cuboidf[]? RotatedBox { get; private set; }

    /// <summary>The block's SELECTION box rotated to <see cref="MeshAngleRad"/>, surfaced by
    /// <c>GetSelectionBoxes</c>. Tracked separately from <see cref="RotatedBox"/> so a painting-style block
    /// can have a thin selection slab WITHOUT a collision box: the floor stations set both to the same box
    /// (selection defaults to collision in their JSON), while the Chalkboard rotates only its slab
    /// selection box and leaves collision null (walk-through). Null falls back to the un-rotated JSON box.</summary>
    public Cuboidf[]? RotatedSelectionBox { get; private set; }

    public float MeshAngleRad
    {
        get => meshAngleRad;
        set
        {
            bool changed = meshAngleRad != value;
            meshAngleRad = value;
            float deg = value * (180f / (float)Math.PI);
            if (Block?.CollisionBoxes is { Length: > 0 } cboxes)
            {
                RotatedBox = new[] { cboxes[0].RotatedCopy(0f, deg, 0f, new Vec3d(0.5, 0.5, 0.5)) };
            }
            if (Block?.SelectionBoxes is { Length: > 0 } sboxes)
            {
                RotatedSelectionBox = new[] { sboxes[0].RotatedCopy(0f, deg, 0f, new Vec3d(0.5, 0.5, 0.5)) };
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

    /// <summary>Server-authoritative durable access mode for this block (fix-transient-lectern-editor-lock).
    /// Persisted + synced via the tree round-trip. RESERVED / dormant this version: nothing sets it away
    /// from <see cref="ScribeAccessMode.Public"/> and the editor-entry gate does not read it, so every
    /// block behaves as Public. Kept as a distinct field so the sticky "private/read-only" permission can
    /// be surfaced by a later change without conflating it with the transient editor lock.</summary>
    private ScribeAccessMode accessMode = ScribeAccessMode.Public;

    /// <summary>Client-side mirror of <see cref="accessMode"/>, updated from the tree in
    /// <see cref="FromTreeAttributes"/>. Unused by any current control (dormant); present so a future
    /// player-facing change reads a synced value rather than adding a new packet.</summary>
    private ScribeAccessMode syncedAccessMode = ScribeAccessMode.Public;

    /// <summary>Server-side: DocId this block entity has registered in the pin store's live index, so
    /// it can unregister exactly that id on removal even if <see cref="Document"/> was later replaced.
    /// Null until the first server-side register.</summary>
    private Guid? registeredDocId;

    /// <summary>Server-side: true when this block entity loaded a v4 document (no title field), so it
    /// must be marked dirty on first server init to re-save as v5 — otherwise the title defaults every
    /// load instead of persisting.</summary>
    private bool needsV5Resave;

    private GuestbookStore _guestbook = new();

    /// <summary>Sample interval (ms) for the particle indicator's periodic check, mirroring
    /// <see cref="ScribeAmbientLightSampler"/>'s periodic-sample precedent rather than a per-frame
    /// check (design.md Decision 9) — playtest-tunable, not final.</summary>
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

        // Wall-mounted subclasses (Chalkboard) take a fixed facing from their `side` variant. Set it here
        // rather than at placement: Initialize runs on BOTH fresh placement and chunk load (and, per the
        // API doc-comment, always AFTER FromTreeAttributes on load), so this is the one point that fixes the
        // angle in every path. The custom rotated-mesh path ignores the shape's rotateYByType, so without
        // this a wall block would render at angle 0 (facing one fixed cardinal) regardless of the wall it is
        // on. The setter is a no-op when the value is unchanged.
        if (api is ICoreClientAPI capi)
        {
            if (WallMountAngleRad is float wallAngle)
            {
                MeshAngleRad = wallAngle;
            }

            // Unseen-assignment ambient particle indicator (§8.4) — every subclass of this base is one
            // of the five Inbox-capable blocks design.md Decision 9 scopes it to, so registering it once
            // here covers all of them with no per-block duplication. Uses the BlockEntity's OWN
            // RegisterGameTickListener (not capi.Event's) so OnBlockRemoved/OnBlockUnloaded's inherited
            // UnregisterAllTickListeners() cleans it up automatically — no manual bookkeeping needed.
            RegisterGameTickListener(OnAssignmentParticleTick, AssignmentParticleTickIntervalMs);
        }
    }

    /// <summary>Tracks whether the previous tick's proximity+unseen-assignment check was true, so the
    /// next tick that turns it true again after being false can tell it's a fresh entry (see
    /// <see cref="OnAssignmentParticleTick"/>'s <c>seedBurst</c> — playtest feedback 2026-08-31).
    /// Client-side, transient, per-instance only — never persisted.</summary>
    private bool assignmentParticlesWereActive;

    /// <summary>Client-side periodic check (§8.4): if the local player has an unseen received assignment
    /// AND is within <see cref="ScribeAssignmentParticleEmitter.DetectionRadius"/> of this block, spawn
    /// this tick's mote batch. Player-specific and local-only — never touches the server or any other
    /// client (design.md Decision 9). The gate has no memory of its own, so a one-tick-late accrual to
    /// steady-state density read as a slow "build-up" (playtest feedback 2026-08-31); tracking the
    /// trigger's own true/false edge here lets the first active tick request a seed burst instead.</summary>
    private void OnAssignmentParticleTick(float dt)
    {
        if (Api is not ICoreClientAPI capi) return;

        bool active = ModSystem is { HasUnseenAssignment: true };
        if (active)
        {
            var player = capi.World.Player?.Entity;
            active = player is not null
                && Pos.DistanceTo(player.Pos.X, player.Pos.Y, player.Pos.Z) <= ScribeAssignmentParticleEmitter.DetectionRadius;
        }

        if (!active)
        {
            assignmentParticlesWereActive = false;
            return;
        }

        ScribeAssignmentParticleEmitter.SpawnAt(capi, Pos, seedBurst: !assignmentParticlesWereActive);
        assignmentParticlesWereActive = true;
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved(); // unregisters the §8.4 particle tick listener (UnregisterAllTickListeners)

        if (Api is ICoreServerAPI sapi)
        {
            sapi.Event.PlayerDisconnect -= OnPlayerDisconnect;

            // The block was removed (broken/replaced/exchanged). Forget the live position so the
            // document is unresolvable until a re-place re-registers it — but do NOT clear the pins:
            // breaking a block to relocate it drops an item carrying the document, and OnBlockPlaced
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
        // Durable per-block access mode (fix-transient-lectern-editor-lock). Dormant this version
        // (always Public), but persisted + synced so a future private/read-only permission needs no
        // save-format change. Stored as the underlying byte.
        tree.SetInt("accessMode", (byte)accessMode);
        tree.SetBytes("guestbook", _guestbook.Serialize());
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        // Placement facing. A wall-mounted subclass (Chalkboard) derives its angle purely from its `side`
        // variant, so honor that FIRST — the persisted `meshAngle` is always 0 for a wall block (Initialize
        // sets the angle client-side only, so the server serializes 0), and every interaction round-trips a
        // block-entity packet through here; without this the board would snap back to angle 0 on the next
        // MarkDirty after placement. Floor stations fall back to the vanilla Sign default: the persisted
        // angle, or the shape's authored rotateY when a pre-orientation block has none. Goes through the
        // property so the rotated hitbox is rebuilt on load; the client re-tesselates on the following redraw.
        MeshAngleRad = WallMountAngleRad
            ?? (tree.HasAttribute("meshAngle")
                ? tree.GetFloat("meshAngle", 0f)
                : (Block?.Shape?.rotateY ?? 0f) * ((float)Math.PI / 180f));

        // Client mirror of the editor-lock holder (fix-multiplayer-editor-lock §2.1). Empty string (the
        // "lock free" sentinel written above) maps back to null. Read on the client to drive the editor
        // affordance; the server ignores its own synced copy (lockHolderUid stays authoritative).
        var lockHolder = tree.GetString("lockHolder", "");
        syncedLockHolderUid = string.IsNullOrEmpty(lockHolder) ? null : lockHolder;

        // Durable access mode (fix-transient-lectern-editor-lock). Absent key (pre-existing saves) →
        // Public. Both the authoritative field and its client mirror are set from the tree; the field
        // is dormant (never read by the editor gate this version).
        var mode = (ScribeAccessMode)(byte)tree.GetInt("accessMode", (int)ScribeAccessMode.Public);
        accessMode = mode;
        syncedAccessMode = mode;

        var bytes = tree.GetBytes(DocumentAttributeKey);
        needsV5Resave = ScribeDocumentCodec.IsPriorVersion(bytes);
        Document = ScribeDocumentCodec.TryDeserialize(bytes, out var doc, out _) && doc is not null
            ? doc
            : new ScribeDocument();
        _guestbook = GuestbookStore.Deserialize(tree.GetBytes("guestbook"));

        // A resync may have replaced the document (a different DocId is unusual for a placed block, but
        // the break→replace path can restore a saved doc). Keep the live index pointing at the current one.
        RegisterDocInStore();

        // Re-key the host registry under the now-current DocId (same bug class as ab702d1's
        // ApplyEdit/OnBlockPlaced re-registers, on the path they missed). Each side constructs its
        // Document with its OWN random DocId (ScribeDocument ctor → Guid.NewGuid()); the authoritative
        // DocId only arrives here. For a FRESHLY PLACED block the VS lifecycle runs Initialize (which
        // registers under the throwaway random id) BEFORE FromTreeAttributes, so without this the client
        // stays keyed under a dead id, the server's open reply routes to nothing via TryResolveHost, and
        // the dialog never opens — while a chunk-LOADED block works because FromTreeAttributes runs
        // first (VintagestoryAPI BlockEntity.Initialize doc-comment: "if this block entity already
        // existed then FromTreeAttributes is called first"), so Initialize already sees the real DocId.
        // No-op when Api isn't set yet (the load-path ordering) — Initialize registers right after.
        ModSystem?.RegisterHost(this);

        // Reflect an authoritative resync in the open dialog. RefreshReadView rebuilds the read view
        // from the now-current Document; it is a no-op while the dialog is in editor mode (the editor
        // edits a private scratch copy that an external resync must not clobber).
        dialog?.RefreshReadView();

        // Likewise repaint an open inventory tab (Scriptorium only) after a synced slot change, so a second
        // client viewing the same block sees the moved item. A no-op for the non-inventory views and for the
        // Lectern/Notebook/Tablet dialogs, which never enter the inventory view (add-scriptorium-inventory).
        dialog?.RefreshInventoryView();
    }

    /// <summary>Restores a document carried on a placed item stack (break→re-place), so the same
    /// content and ids come back and pins reattach. Empty-doc fallback when the stack carries none
    /// (a freshly-crafted block). Server-authoritative; the client gets it via the normal resync.</summary>
    public override void OnBlockPlaced(ItemStack? byItemStack)
    {
        base.OnBlockPlaced(byItemStack);

        if (Api is not ICoreServerAPI) return;
        if (byItemStack is not null && ScribeDocumentAttributes.TryReadFrom(byItemStack, out var doc) && doc is not null)
        {
            Document = doc;

            // Creative middle-click CLONE: the pick stamps the SOURCE block's DocId onto the copy's stack
            // (BlockScribeWritingStation.OnPickBlock), just like a break→re-place. The two cases are otherwise
            // identical here, and are distinguished only by whether the source is still alive: on a clone the
            // original is still placed and registered under this DocId, so keeping it would put TWO live blocks
            // under one id — and the DocId keys both the host registry and the pin store, so the copy's open /
            // editor-lock / pin traffic would resolve to the original and interaction breaks on both (the exact
            // "can't open the block after copying it" symptom). A break→re-place is NOT a collision: OnBlockRemoved
            // unregistered the source first. So mint a fresh identity for the copy only when the id is still
            // taken by a different live block. (The copy keeps the source's title/task CONTENT — a real duplicate
            // — but gets its own identity, so it starts with no pins of its own.)
            if (ModSystem?.IsDocIdRegisteredToOtherBlock(Document.DocId, Pos) == true)
            {
                Document.ReassignNewDocId();
            }

            RegisterDocInStore();
            ModSystem?.RegisterHost(this); // re-register under the restored (or freshly-minted) DocId
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
    /// Called from <see cref="BlockScribeWritingStation.OnBlockInteractStart"/> on whichever side is
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
        if (block is null || !block.IsCompletable) return;
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
    /// Server-side: set a Tracker's live <see cref="ScribeBlock.CurrentQuantity"/> on the authoritative
    /// document by its stable <see cref="ScribeBlock.TaskId"/> — the write-through for the client count
    /// engine (add-tracker-link-tasks D5). Lock-free like <see cref="SetTaskDoneFromReader"/> (updating a
    /// derived carried-inventory count is an always-allowed viewer action, not an editor edit). Routes
    /// through the Core <see cref="ScribeDocument.SetTrackerCurrentQuantity"/> op so the
    /// ≥ 0 clamp holds (overflow above the target is preserved, 7.14), and returns whether the value
    /// actually changed. A no-op, an unknown TaskId, or a non-Tracker block is left unwritten. Does NOT
    /// touch pins.
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
            if (Document.Blocks[i].TaskId == taskId && Document.Blocks[i].IsCompletable)
            {
                Document.DeleteBlock(i);
                MarkDirty(redrawOnClient: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>Server-side: persist after a Core mutation already applied to <see cref="Document"/>.</summary>
    public void PersistFromReader()
    {
        if (Api is not ICoreServerAPI) return;
        MarkDirty(redrawOnClient: true);
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
            capi.TriggerIngameError(this, "scribe-writing-station-locked", Lang.Get(message.RefusalReason ?? "scribe:scribe-gui-locked"));

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
                dialog!.EnterGrantedView();
            }
            else
            {
                // Editor access denied while the dialog is already open — e.g. a Back-from-settings that
                // re-requested the editor lock but another player grabbed it first (add-settings-tab round
                // 1). Fall back to the dialog's own granted view so it can't be stranded on a stale view;
                // the error toast above already told the player why. (The save-failed recovery returned
                // earlier.)
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
            // Quick-add gesture (Shift+right-click): now that the editor holds the lock and the scratch is
            // seeded, insert a fresh empty task at the top and focus its caret (add-unified-quick-add-
            // interaction). Threaded through the round-trip because the dialog didn't exist at interact time.
            if (message.QuickAdd) dialog!.QuickAddTopTask();
        }
        else
        {
            dialog!.EnterGrantedView();
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
        var date = NotebookHost.FormatDate(sapi);
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
    /// reopen the block to see seeded entries); the read view refreshes via the block-entity packet.</summary>
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

    /// <summary>Creates and opens the single LibGUI dialog (read view by default) via the subclass's
    /// <see cref="CreateDialog"/>. The "switch to editor" / "done editing" controls drive the view swap
    /// and lock request/release from inside the dialog through the normal server flow.</summary>
    private void OpenDialog(ICoreClientAPI capi)
    {
        dialog = CreateDialog(capi);
        dialog.TryOpen();
    }

    /// <summary>Client-side: draw the block shape rotated to <see cref="MeshAngleRad"/> so the reading
    /// face points the way it was placed. Mirrors <c>BlockEntitySign.OnTesselation</c>: builds the mesh
    /// once per distinct angle via the object cache and feeds it to the chunk mesher (returning true to
    /// suppress the default un-rotated block mesh).</summary>
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

    /// <summary>World-edit / schematic rotation parity (vanilla Sign pattern): adjust the stored facing
    /// by the rotation amount so a rotated build keeps the block pointing correctly.</summary>
    public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation,
        Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, EnumAxis? flipAxis)
    {
        float angle = tree.GetFloat("meshAngle", 0f) - degreeRotation * ((float)Math.PI / 180f);
        tree.SetFloat("meshAngle", angle);
    }
}
