using Scribe.Core;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// Server-side owner of every player's pin set, plus a live index mapping a document's <c>DocId</c>
/// to the block position that currently hosts it. (Per-player display/behavior preferences are NOT
/// held here — they are client-local JSON, never server state.) Mirrors the vanilla
/// <c>WaypointMapLayer</c> pattern (per-player server-authoritative references persisted with the
/// save game); the <see cref="ScribeModSystem"/> owns persistence I/O and the network push, calling
/// into this store and pushing whichever players a mutation reports as affected.
///
/// Design invariants (see the add-pinned-task-foundation change):
/// <list type="bullet">
/// <item>The durable reference is <c>(DocId, TaskId)</c>, never a block position. The
/// <see cref="_docPositions"/> index is only a runtime resolver for snapshotting a pin's text/done;
/// an index miss means "unresolvable right now," never "deleted".</item>
/// <item>A pin is <b>auto-cleared</b> — removed from the owner's set — only on a TRUE task deletion:
/// a task vanishing from THAT player's own saved edit (<see cref="ReconcileSnapshotsForActor"/>).
/// Breaking the lectern block is
/// NOT a deletion (the dropped item carries the document, and a re-place restores it), and neither is
/// a chunk unload or a resolution miss — in those cases the pin stays live-but-unresolvable and the
/// last-known snapshot keeps it renderable. (The add-pinned-task-hud change replaced the earlier
/// soft-orphan-and-keep behavior, so nothing uncompletable lingers on the HUD.)</item>
/// <item>Unpin (<see cref="RemovePin"/>) needs no block resolution, so it works when the owning
/// lectern is broken or unloaded.</item>
/// </list>
/// This type touches no network or persistence API directly (it takes/returns plain data), so its
/// logic is straightforward to exercise from the Atlas integration suite.
/// </summary>
public sealed class ScribePinStore
{
    private readonly Dictionary<string, List<ScribePinnedRef>> _pins = new();
    private readonly Dictionary<Guid, BlockPos> _docPositions = new();

    // ---------------- Reads ----------------

    /// <summary>The player's pins (empty list if they have none). Returned read-only; mutate via the
    /// methods below.</summary>
    public IReadOnlyList<ScribePinnedRef> Get(string playerUid)
        => _pins.TryGetValue(playerUid, out var list) ? list : (IReadOnlyList<ScribePinnedRef>)Array.Empty<ScribePinnedRef>();

    /// <summary>Whether the player currently has a pin for this task.</summary>
    public bool IsPinned(string playerUid, Guid docId, Guid taskId)
        => Find(playerUid, docId, taskId) is not null;

    /// <summary>The uids of every player who has at least one pin referencing this document. Used to
    /// fan a snapshot/orphan update out to exactly the affected players.</summary>
    public IReadOnlyList<string> PlayersPinning(Guid docId)
    {
        var result = new List<string>();
        foreach (var (uid, list) in _pins)
        {
            if (list.Any(p => p.OwnerDocId == docId)) result.Add(uid);
        }
        return result;
    }

    // ---------------- Live DocId → position index ----------------

    /// <summary>Records that <paramref name="docId"/> is currently hosted at <paramref name="pos"/>.
    /// Called from the block entity's server-side Initialize. Overwrites any prior mapping (a document
    /// lives in exactly one block at a time).</summary>
    public void RegisterDoc(Guid docId, BlockPos pos) => _docPositions[docId] = pos.Copy();

    /// <summary>Forgets a document's position (its block was removed). Does NOT touch pins — orphaning
    /// is a separate explicit call — so a re-place can re-register and pins resolve again.</summary>
    public void UnregisterDoc(Guid docId) => _docPositions.Remove(docId);

    /// <summary>The DocIds currently registered as loaded (a snapshot copy, safe to iterate while the
    /// caller resolves and mutates). Used by the legacy-pin drain to walk loaded lecterns.</summary>
    public IReadOnlyList<Guid> KnownDocIds() => _docPositions.Keys.ToList();

    /// <summary>Resolves a document to its current hosting position, if one is loaded/known. A miss is
    /// "unresolvable right now" (e.g. the chunk is unloaded), never a deletion.</summary>
    public bool TryResolvePos(Guid docId, out BlockPos pos)
    {
        if (_docPositions.TryGetValue(docId, out var p))
        {
            pos = p;
            return true;
        }
        pos = default!;
        return false;
    }

    // ---------------- Pin / unpin ----------------

    /// <summary>Adds a pin for the player, idempotently (a second pin of the same task is a no-op).
    /// Captures the supplied last-known snapshot and pinned-time. Enforces
    /// <see cref="ScribePinCodec.MaxPinsPerPlayer"/> so a runaway/hostile caller can't grow the set
    /// without limit. Returns true if the set changed.</summary>
    public bool SetPin(string playerUid, Guid docId, Guid taskId, double pinnedAtTotalHours, string lastKnownText, bool lastKnownDone,
        ScribeBlockKind kind = ScribeBlockKind.Task, string? linkTarget = null,
        string? targetItemCode = null, int targetQuantity = 1, int currentQuantity = 0, string? linkLabel = null,
        int depth = 0, ScribeDocument? source = null, ScribePinInsert insertEdge = ScribePinInsert.Bottom)
    {
        var list = _pins.TryGetValue(playerUid, out var existing) ? existing : _pins[playerUid] = new List<ScribePinnedRef>();
        if (list.Any(p => p.OwnerDocId == docId && p.TaskId == taskId)) return false; // idempotent
        if (list.Count >= ScribePinCodec.MaxPinsPerPlayer) return false;

        var pin = new ScribePinnedRef
        {
            OwnerDocId = docId,
            TaskId = taskId,
            PinnedAtTotalHours = pinnedAtTotalHours,
            Orphaned = false,
            LastKnownText = lastKnownText,
            LastKnownDone = lastKnownDone,
            Kind = kind,
            LinkTarget = linkTarget,
            TargetItemCode = targetItemCode,
            TargetQuantity = targetQuantity,
            CurrentQuantity = currentQuantity,
            LinkLabel = linkLabel,
            Depth = depth,
        };
        ScribePinOrdering.PlaceNewPin(list, pin, source, insertEdge);
        return true;
    }

    /// <summary>Removes the player's pin for this task if present. Needs no block resolution, so it
    /// succeeds when the owning block is broken or unloaded. Removing an absent pin is a safe no-op.
    /// Returns true if the set changed.</summary>
    public bool RemovePin(string playerUid, Guid docId, Guid taskId)
    {
        if (!_pins.TryGetValue(playerUid, out var list)) return false;
        int removed = list.RemoveAll(p => p.OwnerDocId == docId && p.TaskId == taskId);
        return removed > 0;
    }

    /// <summary>Sets the completed state of the player's pin for this task, since the store is
    /// authoritative for a pinned task's done-state (see <see cref="ScribePinStore"/> invariants). Needs
    /// no block resolution, so completing works when the owning block is broken or unloaded. A no-op
    /// (returns false) when the player has no such pin or its state already matches; returns true if the
    /// pin's <see cref="ScribePinnedRef.LastKnownDone"/> changed.</summary>
    public bool SetPinDone(string playerUid, Guid docId, Guid taskId, bool done)
    {
        if (Find(playerUid, docId, taskId) is not { } pin) return false;
        if (pin.LastKnownDone == done) return false;
        pin.LastKnownDone = done;
        return true;
    }

    /// <summary>The completed state of the player's pin for this task, or null if they have no such
    /// pin. Used by the completion op to decide the next done-state (toggle) authoritatively from the
    /// store rather than the possibly-unresolvable source document.</summary>
    public bool? GetPinDone(string playerUid, Guid docId, Guid taskId)
        => Find(playerUid, docId, taskId)?.LastKnownDone;

    /// <summary>Sets the last-known text snapshot of the player's pin for this task — the snapshot-only
    /// half of the identity-addressed edit path, so a pin edit is reflected even when the owning document
    /// is unresolvable (mirrors <see cref="SetPinDone"/>). Blank/whitespace-only text is REJECTED (returns
    /// false, snapshot unchanged) so a pin edit can never blank the snapshot. A no-op (returns false) when
    /// the player has no such pin or its text already matches; returns true if the snapshot changed.</summary>
    public bool SetPinText(string playerUid, Guid docId, Guid taskId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (Find(playerUid, docId, taskId) is not { } pin) return false;
        if (pin.LastKnownText == text) return false;
        pin.LastKnownText = text;
        return true;
    }

    // ---------------- Reorder (per-player pin list) ----------------

    /// <summary>
    /// Permute a player's own pin list into the client-supplied order, addressed by pin identity
    /// <c>(docId, taskId)</c>. Pins are moved to match <paramref name="order"/>; any id in
    /// <paramref name="order"/> that the player doesn't hold (unknown), and any duplicate id, is ignored,
    /// and any pin the client OMITS is preserved at the end in its current relative order (so a partial or
    /// stale order can never drop a pin). Reorders ONLY this player's per-player list — never any
    /// document's block order and never another player's list. Returns true if the order actually changed
    /// (so the caller re-pushes/persists only on a real change).
    /// </summary>
    public bool ReorderPins(string playerUid, IReadOnlyList<(Guid DocId, Guid TaskId)> order)
    {
        if (!_pins.TryGetValue(playerUid, out var list) || list.Count == 0) return false;

        var reordered = new List<ScribePinnedRef>(list.Count);
        var taken = new HashSet<(Guid, Guid)>();

        // First, the pins named in the client order (skipping unknown ids and duplicates).
        foreach (var (docId, taskId) in order)
        {
            var key = (docId, taskId);
            if (taken.Contains(key)) continue; // duplicate id in the requested order
            var pin = list.FirstOrDefault(p => p.OwnerDocId == docId && p.TaskId == taskId);
            if (pin is null) continue; // unknown id — not held by this player
            reordered.Add(pin);
            taken.Add(key);
        }

        // Then any pins the client omitted, kept in their current relative order so nothing is dropped.
        foreach (var pin in list)
        {
            if (taken.Add((pin.OwnerDocId, pin.TaskId))) reordered.Add(pin);
        }

        // No-op when the resulting order matches the current one (reference-equal per slot).
        bool changed = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (!ReferenceEquals(list[i], reordered[i])) { changed = true; break; }
        }
        if (!changed) return false;

        _pins[playerUid] = reordered;
        return true;
    }

    // ---------------- Snapshot reconcile (acting player only) ----------------

    /// <summary>
    /// Reconcile the <b>acting player's</b> pins into a document after THAT player edited it — the
    /// grief-proof rule (see <see cref="ScribePinStore"/> invariants + the add-pinned-task-hud change):
    /// a pin is the player's own copy, so only the player's own edit may change or remove it. For each
    /// of <paramref name="actingPlayerUid"/>'s pins into <paramref name="docId"/>: refresh its
    /// text/done snapshot from the authoritative document, and REMOVE it if the player deleted that task
    /// (its <c>TaskId</c> is gone from the document). Other players' pins into the same document are
    /// deliberately untouched — their copies only change from their own actions. Returns the acting
    /// player's uid if their set changed (so the caller re-pushes just them), else empty.
    /// </summary>
    public IReadOnlyList<string> ReconcileSnapshotsForActor(string actingPlayerUid, Guid docId, ScribeDocument document)
    {
        if (!_pins.TryGetValue(actingPlayerUid, out var list)) return Array.Empty<string>();

        bool changed = false;
        // Remove the actor's pins whose task the actor deleted from this doc.
        int removed = list.RemoveAll(p => p.OwnerDocId == docId && document.FindByTaskId(p.TaskId) is null);
        if (removed > 0) changed = true;

        // Refresh the surviving pins' snapshots from the authoritative document.
        foreach (var pin in list)
        {
            if (pin.OwnerDocId != docId) continue;
            var block = document.FindByTaskId(pin.TaskId);
            if (block is null) continue; // just removed above; defensive
            // Refresh the snapshot fields the HUD/Pin Tab render from — text/done, the Kind + LinkTarget the
            // Link hyperlink needs (a Link's target can be edited, so keep it in sync — add-tracker-link-tasks 5.5),
            // and the Tracker's target item + have/need counts, which change as the player edits the target
            // quantity or collects/drops items while a dialog is open (add-tracker-link-tasks 7.8).
            if (pin.LastKnownText != block.Text || pin.LastKnownDone != block.Done
                || pin.Kind != block.Kind || pin.LinkTarget != block.LinkTarget
                || pin.TargetItemCode != block.TargetItemCode || pin.TargetQuantity != block.TargetQuantity
                || pin.CurrentQuantity != block.CurrentQuantity || pin.LinkLabel != block.LinkLabel
                || pin.Depth != block.Depth)
            {
                pin.LastKnownText = block.Text;
                pin.LastKnownDone = block.Done;
                pin.Kind = block.Kind;
                pin.LinkTarget = block.LinkTarget;
                pin.TargetItemCode = block.TargetItemCode;
                pin.TargetQuantity = block.TargetQuantity;
                pin.CurrentQuantity = block.CurrentQuantity;
                pin.LinkLabel = block.LinkLabel;
                // Keep the pinned subtask depth in sync so a grip-tap depth change on the source reflects
                // in the HUD/Pin Tab indent (add-crafting-tasks / task-subtasks 5.1).
                pin.Depth = block.Depth;
                changed = true;
            }
        }

        return changed ? new[] { actingPlayerUid } : Array.Empty<string>();
    }

    // ---------------- Persistence bridge ----------------

    /// <summary>Serializes the whole pin store to the savegame blob form.</summary>
    public byte[] SerializePins() => ScribePinCodec.SerializeStore(_pins);

    /// <summary>Replaces the in-memory pin state from the persisted blob (called on world load). A
    /// null/malformed blob leaves the store empty rather than throwing — a corrupt save degrades to
    /// "no pins" instead of crashing the load. The live position index is NOT persisted; it is rebuilt
    /// as block entities initialize.</summary>
    public void LoadFrom(byte[]? pinBytes)
    {
        _pins.Clear();

        if (ScribePinCodec.TryDeserializeStore(pinBytes, out var pins) && pins is not null)
        {
            foreach (var (uid, list) in pins) _pins[uid] = list;
        }
    }

    private ScribePinnedRef? Find(string playerUid, Guid docId, Guid taskId)
        => _pins.TryGetValue(playerUid, out var list)
            ? list.FirstOrDefault(p => p.OwnerDocId == docId && p.TaskId == taskId)
            : null;
}
