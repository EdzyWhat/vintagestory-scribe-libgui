using Scribe.Core;

namespace Scribe;

/// <summary>
/// Server-side owner of every player's quest-decision ledger — mirrors <see cref="ScribePinStore"/>'s
/// shape (a per-player collection, a serialize/deserialize bridge to a Core codec, no network/persistence
/// I/O of its own). Persisted with the save game and synced to its owning player
/// (fix-quest-prompt-persistence-and-auto-pin Decision 1); the <see cref="ScribeModSystem"/> owns the
/// actual save-game read/write and the network push, calling into this store.
/// </summary>
public sealed class ScribeQuestDecisionStore
{
    private readonly Dictionary<string, ScribeQuestDecisionSet> _sets = new();

    /// <summary>The player's decisions (empty list if they have none).</summary>
    public IReadOnlyList<ScribeQuestDecisionEntry> Get(string playerUid)
        => _sets.TryGetValue(playerUid, out var set) ? set.Entries : Array.Empty<ScribeQuestDecisionEntry>();

    /// <summary>The player's recorded decision for this exact key, or null if none was ever recorded.</summary>
    public ScribeQuestDecision? Lookup(string playerUid, string source, string questCode, bool isCompletion)
        => _sets.TryGetValue(playerUid, out var set) ? set.Lookup(source, questCode, isCompletion) : null;

    /// <summary>Records (or overwrites) the player's decision for this exact key. Returns true if the
    /// player's set actually changed (so the caller re-pushes only on a real change).</summary>
    public bool Set(string playerUid, string source, string questCode, bool isCompletion, ScribeQuestDecision decision)
    {
        var set = _sets.TryGetValue(playerUid, out var existing) ? existing : _sets[playerUid] = new ScribeQuestDecisionSet();
        return set.Set(source, questCode, isCompletion, decision);
    }

    // ---------------- Persistence bridge ----------------

    /// <summary>Serializes the whole store to the savegame blob form.</summary>
    public byte[] SerializeStore()
    {
        var asDict = new Dictionary<string, List<ScribeQuestDecisionEntry>>(_sets.Count);
        foreach (var (uid, set) in _sets) asDict[uid] = new List<ScribeQuestDecisionEntry>(set.Entries);
        return ScribeQuestDecisionCodec.SerializeStore(asDict);
    }

    /// <summary>Serializes one player's decision list to the network-push blob form.</summary>
    public byte[] SerializeList(string playerUid) => ScribeQuestDecisionCodec.SerializeList(Get(playerUid));

    /// <summary>Replaces the in-memory state from the persisted blob (called on world load). A
    /// null/malformed blob leaves the store empty rather than throwing — a corrupt save degrades to
    /// "no recorded decisions" instead of crashing the load.</summary>
    public void LoadFrom(byte[]? bytes)
    {
        _sets.Clear();
        if (!ScribeQuestDecisionCodec.TryDeserializeStore(bytes, out var store) || store is null) return;

        foreach (var (uid, entries) in store)
        {
            var set = new ScribeQuestDecisionSet();
            foreach (var e in entries) set.Set(e.Source, e.QuestCode, e.IsCompletion, e.Decision);
            _sets[uid] = set;
        }
    }
}
