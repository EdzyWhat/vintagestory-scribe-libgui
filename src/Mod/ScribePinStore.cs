using Scribe.Core;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// Server-side owner of every player's pin set and Scribe settings, plus a live index mapping a
/// document's <c>DocId</c> to the block position that currently hosts it. Mirrors the vanilla
/// <c>WaypointMapLayer</c> pattern (per-player server-authoritative references persisted with the
/// save game); the <see cref="ScribeModSystem"/> owns persistence I/O and the network push, calling
/// into this store and pushing whichever players a mutation reports as affected.
///
/// Design invariants (see the add-pinned-task-foundation change):
/// <list type="bullet">
/// <item>The durable reference is <c>(DocId, TaskId)</c>, never a block position. The
/// <see cref="_docPositions"/> index is only a runtime resolver for snapshotting a pin's text/done;
/// an index miss means "unresolvable right now," never "deleted".</item>
/// <item>Orphaning is soft and happens ONLY on explicit permanent-deletion signals
/// (<see cref="OrphanAll"/> from block removal, or a task vanishing in <see cref="RefreshSnapshots"/>)
/// — never on chunk unload or a resolution miss.</item>
/// <item>Unpin (<see cref="RemovePin"/>) needs no block resolution, so it works when the owning
/// lectern is broken or unloaded.</item>
/// </list>
/// This type touches no network or persistence API directly (it takes/returns plain data), so its
/// logic is straightforward to exercise from the Atlas integration suite.
/// </summary>
public sealed class ScribePinStore
{
    private readonly Dictionary<string, List<ScribePinnedRef>> _pins = new();
    private readonly Dictionary<string, ScribePlayerSettings> _settings = new();
    private readonly Dictionary<Guid, BlockPos> _docPositions = new();

    // ---------------- Reads ----------------

    /// <summary>The player's pins (empty list if they have none). Returned read-only; mutate via the
    /// methods below.</summary>
    public IReadOnlyList<ScribePinnedRef> Get(string playerUid)
        => _pins.TryGetValue(playerUid, out var list) ? list : (IReadOnlyList<ScribePinnedRef>)Array.Empty<ScribePinnedRef>();

    /// <summary>The player's settings, or a fresh default instance (complete-to-unpin on) if they
    /// have never changed one. Never returns null.</summary>
    public ScribePlayerSettings GetSettings(string playerUid)
        => _settings.TryGetValue(playerUid, out var s) ? s : new ScribePlayerSettings();

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
    public bool SetPin(string playerUid, Guid docId, Guid taskId, double pinnedAtTotalHours, string lastKnownText, bool lastKnownDone)
    {
        var list = _pins.TryGetValue(playerUid, out var existing) ? existing : _pins[playerUid] = new List<ScribePinnedRef>();
        if (list.Any(p => p.OwnerDocId == docId && p.TaskId == taskId)) return false; // idempotent
        if (list.Count >= ScribePinCodec.MaxPinsPerPlayer) return false;

        list.Add(new ScribePinnedRef
        {
            OwnerDocId = docId,
            TaskId = taskId,
            PinnedAtTotalHours = pinnedAtTotalHours,
            Orphaned = false,
            LastKnownText = lastKnownText,
            LastKnownDone = lastKnownDone,
        });
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

    // ---------------- Settings ----------------

    /// <summary>Replaces the player's settings. Returns true (the caller re-pushes the owner).</summary>
    public bool SetSettings(string playerUid, ScribePlayerSettings settings)
    {
        _settings[playerUid] = settings;
        return true;
    }

    // ---------------- Snapshot refresh + soft-orphan ----------------

    /// <summary>
    /// After a document is edited, refresh every pin (across all players) that references it: update
    /// the last-known text/done snapshot from the authoritative document, and soft-orphan any pin
    /// whose task has vanished from the document (a saved deletion). Never un-orphans and never
    /// touches pins for other documents. Returns the uids whose set changed, so the caller re-pushes
    /// exactly those players.
    /// </summary>
    public IReadOnlyList<string> RefreshSnapshots(Guid docId, ScribeDocument document)
    {
        var affected = new List<string>();
        foreach (var (uid, list) in _pins)
        {
            bool changed = false;
            foreach (var pin in list)
            {
                if (pin.OwnerDocId != docId) continue;

                var block = document.FindByTaskId(pin.TaskId);
                if (block is null)
                {
                    // The task is gone from a saved edit → soft-orphan (keep the last-known snapshot).
                    if (!pin.Orphaned) { pin.Orphaned = true; changed = true; }
                }
                else if (pin.LastKnownText != block.Text || pin.LastKnownDone != block.Done)
                {
                    pin.LastKnownText = block.Text;
                    pin.LastKnownDone = block.Done;
                    changed = true;
                }
            }
            if (changed) affected.Add(uid);
        }
        return affected;
    }

    /// <summary>
    /// Soft-orphan every pin (across all players) referencing this document — the block was broken or
    /// removed. Keeps each pin and its last-known snapshot; the player clears it themselves. Returns
    /// the uids whose set changed.
    /// </summary>
    public IReadOnlyList<string> OrphanAll(Guid docId)
    {
        var affected = new List<string>();
        foreach (var (uid, list) in _pins)
        {
            bool changed = false;
            foreach (var pin in list)
            {
                if (pin.OwnerDocId == docId && !pin.Orphaned) { pin.Orphaned = true; changed = true; }
            }
            if (changed) affected.Add(uid);
        }
        return affected;
    }

    // ---------------- v3 legacy-pin migration ----------------

    /// <summary>
    /// One-time migration of a v3 document's previously-pinned tasks into a player's store (the v3
    /// <c>pinned</c> flag was shared, not per-player; single-player scope only — see the design's
    /// Migration Plan). Each id that still resolves in <paramref name="document"/> is pinned with a
    /// snapshot taken from it; ids that no longer resolve are skipped (nothing to reference).
    /// </summary>
    public void MigrateLegacyPins(string playerUid, Guid docId, IEnumerable<Guid> pinnedTaskIds, ScribeDocument document, double totalHours)
    {
        foreach (var taskId in pinnedTaskIds)
        {
            var block = document.FindByTaskId(taskId);
            if (block is null) continue;
            SetPin(playerUid, docId, taskId, totalHours, block.Text, block.Done);
        }
    }

    // ---------------- Persistence bridge ----------------

    /// <summary>Serializes the whole pin store to the savegame blob form.</summary>
    public byte[] SerializePins() => ScribePinCodec.SerializeStore(_pins);

    /// <summary>Serializes the whole settings store to the savegame blob form.</summary>
    public byte[] SerializeSettings() => ScribePinCodec.SerializeSettingsStore(_settings);

    /// <summary>Replaces the in-memory pin + settings state from persisted blobs (called on world
    /// load). A null/malformed blob leaves that half empty rather than throwing — a corrupt save
    /// degrades to "no pins" instead of crashing the load. The live position index is NOT persisted;
    /// it is rebuilt as block entities initialize.</summary>
    public void LoadFrom(byte[]? pinBytes, byte[]? settingsBytes)
    {
        _pins.Clear();
        _settings.Clear();

        if (ScribePinCodec.TryDeserializeStore(pinBytes, out var pins) && pins is not null)
        {
            foreach (var (uid, list) in pins) _pins[uid] = list;
        }
        if (ScribePinCodec.TryDeserializeSettingsStore(settingsBytes, out var settings) && settings is not null)
        {
            foreach (var (uid, s) in settings) _settings[uid] = s;
        }
    }

    private ScribePinnedRef? Find(string playerUid, Guid docId, Guid taskId)
        => _pins.TryGetValue(playerUid, out var list)
            ? list.FirstOrDefault(p => p.OwnerDocId == docId && p.TaskId == taskId)
            : null;
}
