using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the per-player quest-decision ledger's Core-side pieces: the ScribeQuestDecisionSet's
// add/lookup/remove semantics, and the ScribeQuestDecisionCodec's list/store round-trip (network sync +
// save-game persistence use the same bytes, so both must round-trip exactly and reject malformed input
// safely — mirrors ScribePinCodecTests).
public class ScribeQuestDecisionTests
{
    [Fact]
    public void Lookup_WithNoDecision_ReturnsNull()
    {
        var set = new ScribeQuestDecisionSet();
        Assert.Null(set.Lookup(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false));
    }

    [Fact]
    public void Set_ThenLookup_RoundTrips()
    {
        var set = new ScribeQuestDecisionSet();
        Assert.True(set.Set(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false, ScribeQuestDecision.Accepted));
        Assert.Equal(ScribeQuestDecision.Accepted, set.Lookup(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false));
    }

    [Fact]
    public void Set_IsIndependentPerIsCompletion()
    {
        // An accept-prompt decision and that same quest's completion-prompt decision are independent keys
        // (quest-auto-detect's "Decisions are independent per prompt kind" scenario).
        var set = new ScribeQuestDecisionSet();
        set.Set(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false, ScribeQuestDecision.Dismissed);
        Assert.Null(set.Lookup(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: true));
    }

    [Fact]
    public void Set_IsIndependentPerSource()
    {
        // A code collision between backends must never alias — matches the pin-store Link matching's own
        // source-aware key.
        var set = new ScribeQuestDecisionSet();
        set.Set(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false, ScribeQuestDecision.Accepted);
        Assert.Null(set.Lookup(ScribeQuestSource.ProgressionFramework, "quest-freeghost", isCompletion: false));
    }

    [Fact]
    public void Set_OverwritesAnExistingDecision()
    {
        var set = new ScribeQuestDecisionSet();
        set.Set(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false, ScribeQuestDecision.Dismissed);
        bool changed = set.Set(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false, ScribeQuestDecision.Accepted);
        Assert.True(changed);
        Assert.Equal(ScribeQuestDecision.Accepted, set.Lookup(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false));
    }

    [Fact]
    public void Set_SameDecisionTwice_ReportsNoChange()
    {
        var set = new ScribeQuestDecisionSet();
        set.Set(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false, ScribeQuestDecision.Accepted);
        bool changed = set.Set(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false, ScribeQuestDecision.Accepted);
        Assert.False(changed);
    }

    [Fact]
    public void Remove_DropsTheDecision()
    {
        var set = new ScribeQuestDecisionSet();
        set.Set(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false, ScribeQuestDecision.Accepted);
        Assert.True(set.Remove(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false));
        Assert.Null(set.Lookup(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false));
    }

    [Fact]
    public void Remove_Absent_ReturnsFalse()
    {
        var set = new ScribeQuestDecisionSet();
        Assert.False(set.Remove(ScribeQuestSource.VsQuest, "quest-freeghost", isCompletion: false));
    }

    // ---- SQDL: list round-trip ----

    [Fact]
    public void List_RoundTrip_PreservesAllFields()
    {
        var entries = new List<ScribeQuestDecisionEntry>
        {
            new() { Source = ScribeQuestSource.VsQuest, QuestCode = "quest-freeghost", IsCompletion = false, Decision = ScribeQuestDecision.Accepted },
            new() { Source = ScribeQuestSource.ProgressionFramework, QuestCode = "pf:foo", IsCompletion = true, Decision = ScribeQuestDecision.Dismissed },
        };

        var bytes = ScribeQuestDecisionCodec.SerializeList(entries);
        Assert.True(ScribeQuestDecisionCodec.TryDeserializeList(bytes, out var roundTripped));
        Assert.NotNull(roundTripped);
        Assert.Equal(entries.Count, roundTripped!.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(entries[i].Source, roundTripped[i].Source);
            Assert.Equal(entries[i].QuestCode, roundTripped[i].QuestCode);
            Assert.Equal(entries[i].IsCompletion, roundTripped[i].IsCompletion);
            Assert.Equal(entries[i].Decision, roundTripped[i].Decision);
        }
    }

    [Fact]
    public void List_RoundTrip_Empty()
    {
        var bytes = ScribeQuestDecisionCodec.SerializeList(new List<ScribeQuestDecisionEntry>());
        Assert.True(ScribeQuestDecisionCodec.TryDeserializeList(bytes, out var roundTripped));
        Assert.Empty(roundTripped!);
    }

    [Fact]
    public void List_Deserialize_Null_ReturnsFalse()
    {
        Assert.False(ScribeQuestDecisionCodec.TryDeserializeList(null, out var entries));
        Assert.Null(entries);
    }

    [Fact]
    public void List_Deserialize_Malformed_ReturnsFalseNotThrows()
    {
        var garbage = new byte[] { 1, 2, 3 };
        Assert.False(ScribeQuestDecisionCodec.TryDeserializeList(garbage, out var entries));
        Assert.Null(entries);
    }

    [Fact]
    public void List_Deserialize_WrongMagic_ReturnsFalse()
    {
        var bytes = ScribeQuestDecisionCodec.SerializeStore(new Dictionary<string, List<ScribeQuestDecisionEntry>>());
        Assert.False(ScribeQuestDecisionCodec.TryDeserializeList(bytes, out var entries));
        Assert.Null(entries);
    }

    // ---- SQDS: store round-trip ----

    [Fact]
    public void Store_RoundTrip_PreservesEveryPlayer()
    {
        var store = new Dictionary<string, List<ScribeQuestDecisionEntry>>
        {
            ["player-a"] = new()
            {
                new() { Source = ScribeQuestSource.VsQuest, QuestCode = "quest-freeghost", IsCompletion = false, Decision = ScribeQuestDecision.Accepted },
            },
            ["player-b"] = new()
            {
                new() { Source = ScribeQuestSource.ProgressionFramework, QuestCode = "pf:foo", IsCompletion = true, Decision = ScribeQuestDecision.Dismissed },
            },
        };

        var bytes = ScribeQuestDecisionCodec.SerializeStore(store);
        Assert.True(ScribeQuestDecisionCodec.TryDeserializeStore(bytes, out var roundTripped));
        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped!.Count);
        Assert.Equal(ScribeQuestDecision.Accepted, roundTripped["player-a"][0].Decision);
        Assert.Equal(ScribeQuestDecision.Dismissed, roundTripped["player-b"][0].Decision);
    }

    [Fact]
    public void Store_Deserialize_Null_ReturnsFalse()
    {
        Assert.False(ScribeQuestDecisionCodec.TryDeserializeStore(null, out var store));
        Assert.Null(store);
    }

    [Fact]
    public void Store_Deserialize_Malformed_ReturnsFalseNotThrows()
    {
        var garbage = new byte[] { 9, 9, 9, 9, 9 };
        Assert.False(ScribeQuestDecisionCodec.TryDeserializeStore(garbage, out var store));
        Assert.Null(store);
    }
}
