using Scribe.Core;

namespace Scribe;

/// <summary>
/// Server-side owner of every player's quest-decision ledger; mirrors <see cref="ScribePinStore"/>. It does
/// no I/O: <see cref="ScribeModSystem"/> owns the save-game read/write and the network push.
/// </summary>
public sealed class ScribeQuestDecisionStore
{
    private readonly Dictionary<string, ScribeQuestDecisionSet> _sets = new();

    public IReadOnlyList<ScribeQuestDecisionEntry> Get(string playerUid)
        => _sets.TryGetValue(playerUid, out var set) ? set.Entries : Array.Empty<ScribeQuestDecisionEntry>();

    /// <summary>The player's recorded decision for this exact key, or null if none was ever recorded.</summary>
    public ScribeQuestDecision? Lookup(string playerUid, string source, string questCode, bool isCompletion)
        => _sets.TryGetValue(playerUid, out var set) ? set.Lookup(source, questCode, isCompletion) : null;

    /// <summary>Records (or overwrites) the decision for this key. Returns true only on a real change, so
    /// callers re-push only then.</summary>
    public bool Set(string playerUid, string source, string questCode, bool isCompletion, ScribeQuestDecision decision)
    {
        var set = _sets.TryGetValue(playerUid, out var existing) ? existing : _sets[playerUid] = new ScribeQuestDecisionSet();
        return set.Set(source, questCode, isCompletion, decision);
    }

    // ---------------- Persistence bridge ----------------

    public byte[] SerializeStore()
    {
        var asDict = new Dictionary<string, List<ScribeQuestDecisionEntry>>(_sets.Count);
        foreach (var (uid, set) in _sets) asDict[uid] = new List<ScribeQuestDecisionEntry>(set.Entries);
        return ScribeQuestDecisionCodec.SerializeStore(asDict);
    }

    public byte[] SerializeList(string playerUid) => ScribeQuestDecisionCodec.SerializeList(Get(playerUid));

    /// <summary>Replaces the in-memory state from the persisted blob. A null/malformed blob leaves the store
    /// empty rather than crashing the load.</summary>
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
