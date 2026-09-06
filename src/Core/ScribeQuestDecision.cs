using System.Collections.Generic;
using System.Linq;

namespace Scribe.Core;

/// <summary>A player's recorded decision on one quest prompt: they either accepted it via Scribe (linked
/// the quest, or marked its linked task done for a completion-prompt) or explicitly dismissed it.
/// See <see cref="ScribeQuestDecisionSet"/> for the keying.</summary>
public enum ScribeQuestDecision : byte
{
    Accepted = 0,
    Dismissed = 1,
}

/// <summary>One player's decision on a specific quest prompt, keyed by <see cref="Source"/> (the backend
/// quest mod — <see cref="ScribeQuestSource"/>), <see cref="QuestCode"/>, and <see cref="IsCompletion"/>
/// (an accept-prompt and that same quest's later completion-prompt are independent decisions — dismissing
/// one must never suppress the other). Game-agnostic; the Mod layer owns persistence and network sync
/// (fix-quest-prompt-persistence-and-auto-pin).</summary>
public sealed class ScribeQuestDecisionEntry
{
    public string Source { get; set; } = "";
    public string QuestCode { get; set; } = "";
    public bool IsCompletion { get; set; }
    public ScribeQuestDecision Decision { get; set; }
}

/// <summary>One player's whole set of quest-prompt decisions, keyed by
/// <c>(Source, QuestCode, IsCompletion)</c>. Mirrors the shape of a per-player pin list
/// (<see cref="ScribePinnedRef"/> + a plain <c>List</c>) but wraps its own add/lookup/remove so every
/// caller (the Mod-side store, the client's synced cache) shares one correct implementation of the key
/// match. Serialized by <see cref="ScribeQuestDecisionCodec"/>.</summary>
public sealed class ScribeQuestDecisionSet
{
    /// <summary>Hard upper bound on the number of decisions a single player may hold, enforced on every
    /// deserialize so a malformed or hostile payload cannot grow a persisted/synced set without limit.
    /// Generous relative to any realistic vsquest/Progression Framework catalog size.</summary>
    public const int MaxEntriesPerPlayer = 2000;

    private readonly List<ScribeQuestDecisionEntry> _entries = new();

    public IReadOnlyList<ScribeQuestDecisionEntry> Entries => _entries;

    /// <summary>Records (or overwrites) the decision for this exact key. Returns true if the set actually
    /// changed (a new key, or an existing key's decision flipped).</summary>
    public bool Set(string source, string questCode, bool isCompletion, ScribeQuestDecision decision)
    {
        var existing = Find(source, questCode, isCompletion);
        if (existing is not null)
        {
            if (existing.Decision == decision) return false;
            existing.Decision = decision;
            return true;
        }
        if (_entries.Count >= MaxEntriesPerPlayer) return false;
        _entries.Add(new ScribeQuestDecisionEntry
        {
            Source = source,
            QuestCode = questCode,
            IsCompletion = isCompletion,
            Decision = decision,
        });
        return true;
    }

    /// <summary>The recorded decision for this exact key, or null if none has ever been recorded.</summary>
    public ScribeQuestDecision? Lookup(string source, string questCode, bool isCompletion)
        => Find(source, questCode, isCompletion)?.Decision;

    /// <summary>Removes any decision for this exact key. Returns true if one was actually removed.</summary>
    public bool Remove(string source, string questCode, bool isCompletion)
        => _entries.RemoveAll(e => Matches(e, source, questCode, isCompletion)) > 0;

    /// <summary>Drops every recorded decision — used when replacing this set wholesale from a fresh
    /// authoritative push (the client's synced cache never merges; it always reflects the server's last
    /// full send).</summary>
    public void Clear() => _entries.Clear();

    private ScribeQuestDecisionEntry? Find(string source, string questCode, bool isCompletion)
        => _entries.FirstOrDefault(e => Matches(e, source, questCode, isCompletion));

    private static bool Matches(ScribeQuestDecisionEntry e, string source, string questCode, bool isCompletion)
        => e.Source == source && e.QuestCode == questCode && e.IsCompletion == isCompletion;
}
