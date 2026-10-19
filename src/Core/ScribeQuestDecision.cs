using System.Collections.Generic;
using System.Linq;

namespace Scribe.Core;

/// <summary>A player's recorded decision on one quest prompt: accepted via Scribe or explicitly dismissed.
/// See <see cref="ScribeQuestDecisionSet"/> for the keying.</summary>
public enum ScribeQuestDecision : byte
{
    Accepted = 0,
    Dismissed = 1,
}

/// <summary>One player's decision on a quest prompt, keyed by <see cref="Source"/> (see
/// <see cref="ScribeQuestSource"/>), <see cref="QuestCode"/>, and <see cref="IsCompletion"/> (an
/// accept-prompt and the same quest's completion-prompt are independent decisions). Game-agnostic.</summary>
public sealed class ScribeQuestDecisionEntry
{
    public string Source { get; set; } = "";
    public string QuestCode { get; set; } = "";
    public bool IsCompletion { get; set; }
    public ScribeQuestDecision Decision { get; set; }
}

/// <summary>One player's set of quest-prompt decisions, keyed by <c>(Source, QuestCode, IsCompletion)</c>.
/// Wraps add/lookup/remove so the Mod-side store and the client's synced cache share one key match.
/// Serialized by <see cref="ScribeQuestDecisionCodec"/>.</summary>
public sealed class ScribeQuestDecisionSet
{
    /// <summary>Hard cap on decisions per player, enforced on every deserialize against hostile payloads.</summary>
    public const int MaxEntriesPerPlayer = 2000;

    private readonly List<ScribeQuestDecisionEntry> _entries = new();

    public IReadOnlyList<ScribeQuestDecisionEntry> Entries => _entries;

    /// <summary>Records or overwrites the decision for this key. Returns true if the set changed.</summary>
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

    /// <summary>Drops every decision; the client's synced cache is replaced wholesale, never merged.</summary>
    public void Clear() => _entries.Clear();

    private ScribeQuestDecisionEntry? Find(string source, string questCode, bool isCompletion)
        => _entries.FirstOrDefault(e => Matches(e, source, questCode, isCompletion));

    private static bool Matches(ScribeQuestDecisionEntry e, string source, string questCode, bool isCompletion)
        => e.Source == source && e.QuestCode == questCode && e.IsCompletion == isCompletion;
}
