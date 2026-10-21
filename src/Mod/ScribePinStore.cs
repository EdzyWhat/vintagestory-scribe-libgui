using Scribe.Core;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// Server-side owner of every player's pin set, plus a live index mapping a document's <c>DocId</c>
/// to the block position that currently hosts it. Mirrors the vanilla <c>WaypointMapLayer</c> pattern
/// (per-player server-authoritative references). <see cref="ScribeModSystem"/> owns persistence I/O and
/// the network push; this type touches neither.
///
/// Invariants:
/// <list type="bullet">
/// <item>The durable reference is <c>(DocId, TaskId)</c>, never a block position.</item>
/// <item>A pin is auto-cleared only when a task vanishes from THAT player's own saved edit
/// (<see cref="ReconcileSnapshotsForActor"/>). Breaking the lectern, a chunk unload, or a resolution miss
/// leaves the pin live with its last-known snapshot.</item>
/// <item>Unpin (<see cref="RemovePin"/>) needs no block resolution.</item>
/// </list>
/// </summary>
public sealed class ScribePinStore
{
    private readonly Dictionary<string, List<ScribePinnedRef>> _pins = new();

    // ---------------- Reads ----------------

    /// <summary>The player's pins (empty if none), read-only.</summary>
    public IReadOnlyList<ScribePinnedRef> Get(string playerUid)
        => _pins.TryGetValue(playerUid, out var list) ? list : (IReadOnlyList<ScribePinnedRef>)Array.Empty<ScribePinnedRef>();

    /// <summary>Whether the player currently has a pin for this task.</summary>
    public bool IsPinned(string playerUid, Guid docId, Guid taskId)
        => Find(playerUid, docId, taskId) is not null;

    /// <summary>The player's pin for this task, or null — exposes its snapshot without a block resolution.</summary>
    public ScribePinnedRef? GetPin(string playerUid, Guid docId, Guid taskId)
        => Find(playerUid, docId, taskId);

    // ---------------- Pin / unpin ----------------

    /// <summary>Adds a pin idempotently, capped at <see cref="ScribePinCodec.MaxPinsPerPlayer"/>.
    /// Returns true if the set changed.</summary>
    public bool SetPin(string playerUid, Guid docId, Guid taskId, double pinnedAtTotalHours, string lastKnownText, bool lastKnownDone,
        ScribeBlockKind kind = ScribeBlockKind.Task, string? linkTarget = null,
        string? targetItemCode = null, int targetQuantity = 1, int currentQuantity = 0, string? linkLabel = null,
        int depth = 0, ScribeDocument? source = null, ScribePinInsert insertEdge = ScribePinInsert.Bottom,
        bool isAcceptedAssignment = false, string assignerUid = "", string assignedDate = "",
        string? acceptedDate = null, double? assignedTimestamp = null, double? acceptedTimestamp = null)
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
            IsAcceptedAssignment = isAcceptedAssignment,
            AssignerUid = assignerUid,
            AssignedDate = assignedDate,
            AcceptedDate = acceptedDate,
            AssignedTimestamp = assignedTimestamp,
            AcceptedTimestamp = acceptedTimestamp,
        };
        ScribePinOrdering.PlaceNewPin(list, pin, source, insertEdge);
        return true;
    }

    /// <summary>Removes the player's pin for this task if present (absent is a no-op). Returns true if the
    /// set changed.</summary>
    public bool RemovePin(string playerUid, Guid docId, Guid taskId)
    {
        if (!_pins.TryGetValue(playerUid, out var list)) return false;
        int removed = list.RemoveAll(p => p.OwnerDocId == docId && p.TaskId == taskId);
        return removed > 0;
    }

    /// <summary>Sets the pin's done-state; the store is authoritative for it and needs no block resolution.
    /// Returns true if <see cref="ScribePinnedRef.LastKnownDone"/> changed.</summary>
    public bool SetPinDone(string playerUid, Guid docId, Guid taskId, bool done)
    {
        if (Find(playerUid, docId, taskId) is not { } pin) return false;
        if (pin.LastKnownDone == done) return false;
        pin.LastKnownDone = done;
        return true;
    }

    /// <summary>The pin's done-state, or null if the player has no such pin.</summary>
    public bool? GetPinDone(string playerUid, Guid docId, Guid taskId)
        => Find(playerUid, docId, taskId)?.LastKnownDone;

    /// <summary>Sets the pin's text snapshot (mirrors <see cref="SetPinDone"/>). Blank text is rejected so
    /// an edit can never blank the snapshot. Returns true if the snapshot changed.</summary>
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
    /// Permutes a player's own pin list into the client-supplied order, addressed by <c>(docId, taskId)</c>.
    /// Unknown and duplicate ids are ignored; pins the client omits keep their relative order at the end,
    /// so a partial or stale order never drops a pin. Returns true only if the order actually changed.
    /// </summary>
    public bool ReorderPins(string playerUid, IReadOnlyList<(Guid DocId, Guid TaskId)> order)
    {
        if (!_pins.TryGetValue(playerUid, out var list) || list.Count == 0) return false;

        var reordered = new List<ScribePinnedRef>(list.Count);
        var taken = new HashSet<(Guid, Guid)>();

        foreach (var (docId, taskId) in order)
        {
            var key = (docId, taskId);
            if (taken.Contains(key)) continue; // duplicate id in the requested order
            var pin = list.FirstOrDefault(p => p.OwnerDocId == docId && p.TaskId == taskId);
            if (pin is null) continue; // unknown id — not held by this player
            reordered.Add(pin);
            taken.Add(key);
        }

        foreach (var pin in list)
        {
            if (taken.Add((pin.OwnerDocId, pin.TaskId))) reordered.Add(pin);
        }

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
    /// Reconciles the acting player's pins into a document after THAT player edited it: refreshes each
    /// snapshot from the document and removes pins whose task is gone. Other players' pins are untouched
    /// (a pin is the owner's own copy, so only their own edit changes it). Returns the acting player's uid
    /// if their set changed, else empty.
    /// </summary>
    public IReadOnlyList<string> ReconcileSnapshotsForActor(string actingPlayerUid, Guid docId, ScribeDocument document)
    {
        if (!_pins.TryGetValue(actingPlayerUid, out var list)) return Array.Empty<string>();

        bool changed = false;
        int removed = list.RemoveAll(p => p.OwnerDocId == docId && document.FindByTaskId(p.TaskId) is null);
        if (removed > 0) changed = true;

        foreach (var pin in list)
        {
            if (pin.OwnerDocId != docId) continue;
            var block = document.FindByTaskId(pin.TaskId);
            if (block is null) continue; // just removed above; defensive
            // Refresh every snapshot field the HUD/Pin Tab render from (Link target and Tracker counts can change while a dialog is open).
            bool blockIsAcceptedAssignment = block.Assignment?.State == ScribeAssignmentState.Accepted;
            string blockAssignerUid = blockIsAcceptedAssignment ? block.Assignment!.AssignerUid : "";
            string blockAssignedDate = blockIsAcceptedAssignment ? block.Assignment!.AssignedDate : "";
            string? blockAcceptedDate = blockIsAcceptedAssignment ? block.Assignment!.AcceptedDate : null;
            double? blockAssignedTimestamp = blockIsAcceptedAssignment ? block.Assignment!.AssignedTimestamp : null;
            double? blockAcceptedTimestamp = blockIsAcceptedAssignment ? block.Assignment!.AcceptedTimestamp : null;
            bool identityDirty = pin.LastKnownText != block.Text || pin.LastKnownDone != block.Done
                || pin.Kind != block.Kind || pin.LinkTarget != block.LinkTarget
                || pin.TargetItemCode != block.TargetItemCode || pin.TargetQuantity != block.TargetQuantity
                || pin.CurrentQuantity != block.CurrentQuantity || pin.LinkLabel != block.LinkLabel
                || pin.Depth != block.Depth || pin.IsAcceptedAssignment != blockIsAcceptedAssignment
                || pin.AssignerUid != blockAssignerUid || pin.AssignedDate != blockAssignedDate
                || pin.AcceptedDate != blockAcceptedDate;
            // The dirty-check uses identity strings so a viewing-locale date cannot desync pins. Timestamps are
            // copied when they differ; AcceptedTimestamp is not persisted on the document, so the pin's value is kept.
            bool timestampsDirty = pin.AssignedTimestamp != (blockAssignedTimestamp ?? pin.AssignedTimestamp)
                || pin.AcceptedTimestamp != (blockAcceptedTimestamp ?? pin.AcceptedTimestamp);
            if (identityDirty || timestampsDirty)
            {
                pin.LastKnownText = block.Text;
                pin.LastKnownDone = block.Done;
                pin.Kind = block.Kind;
                pin.LinkTarget = block.LinkTarget;
                pin.TargetItemCode = block.TargetItemCode;
                pin.TargetQuantity = block.TargetQuantity;
                pin.CurrentQuantity = block.CurrentQuantity;
                pin.LinkLabel = block.LinkLabel;
                pin.Depth = block.Depth;
                pin.IsAcceptedAssignment = blockIsAcceptedAssignment;
                pin.AssignerUid = blockAssignerUid;
                pin.AssignedDate = blockAssignedDate;
                pin.AcceptedDate = blockAcceptedDate;
                pin.AssignedTimestamp = blockIsAcceptedAssignment
                    ? (blockAssignedTimestamp ?? pin.AssignedTimestamp)
                    : null;
                pin.AcceptedTimestamp = blockIsAcceptedAssignment
                    ? (blockAcceptedTimestamp ?? pin.AcceptedTimestamp)
                    : null;
                changed = true;
            }
        }

        return changed ? new[] { actingPlayerUid } : Array.Empty<string>();
    }

    // ---------------- Persistence bridge ----------------

    /// <summary>Serializes the whole pin store to the savegame blob form.</summary>
    public byte[] SerializePins() => ScribePinCodec.SerializeStore(_pins);

    /// <summary>Replaces the pin state from the persisted blob. A null/malformed blob leaves the store
    /// empty rather than throwing. The position index is not persisted; block entities rebuild it.</summary>
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
