using Scribe.Core;
using Xunit;

namespace Scribe.Core.Tests;

/// <summary>
/// Pins the exact serialized bytes of the per-player pin and quest-decision blobs. Round-trip tests alone pass
/// even if the wire format drifts, and these blobs are written into save games, so a drift would orphan every
/// existing save.
/// </summary>
public class StoreCodecGoldenBytesTests
{
    private static readonly Guid DocId = new("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid TaskId = new("ffeeddcc-bbaa-9988-7766-554433221100");

    private static ScribePinnedRef SamplePin() => new()
    {
        OwnerDocId = DocId,
        TaskId = TaskId,
        PinnedAtTotalHours = 12.5,
        LastKnownText = "Find copper",
        LastKnownDone = true,
        TargetQuantity = 3,
        AssignedTimestamp = 2.25,
    };

    private static ScribeQuestDecisionEntry SampleDecision() => new()
    {
        Source = "pf",
        QuestCode = "seafarer-1",
        IsCompletion = true,
        Decision = ScribeQuestDecision.Dismissed,
    };

    [Fact]
    public void PinList_bytes_are_stable()
        => Assert.Equal(PinListHex, Convert.ToHexString(ScribePinCodec.SerializeList(new[] { SamplePin() })));

    [Fact]
    public void PinStore_bytes_are_stable()
        => Assert.Equal(PinStoreHex, Convert.ToHexString(ScribePinCodec.SerializeStore(
            new Dictionary<string, List<ScribePinnedRef>> { ["player-1"] = new() { SamplePin() } })));

    [Fact]
    public void QuestDecisionList_bytes_are_stable()
        => Assert.Equal(DecisionListHex, Convert.ToHexString(
            ScribeQuestDecisionCodec.SerializeList(new[] { SampleDecision() })));

    [Fact]
    public void QuestDecisionStore_bytes_are_stable()
        => Assert.Equal(DecisionStoreHex, Convert.ToHexString(ScribeQuestDecisionCodec.SerializeStore(
            new Dictionary<string, List<ScribeQuestDecisionEntry>> { ["player-1"] = new() { SampleDecision() } })));

    private const string PinListHex =
        "5350494E090100000033221100554477668899AABBCCDDEEFFCCDDEEFFAABB88997766554433221100000000000000294000010B46696E6420636F7070657200000003000000000000000000000000000000000001000000000000024000";
    private const string PinStoreHex =
        "53505354090100000008706C617965722D310100000033221100554477668899AABBCCDDEEFFCCDDEEFFAABB88997766554433221100000000000000294000010B46696E6420636F7070657200000003000000000000000000000000000000000001000000000000024000";
    private const string DecisionListHex =
        "5351444C01010000000270660A73656166617265722D310101";
    private const string DecisionStoreHex =
        "53514453010100000008706C617965722D31010000000270660A73656166617265722D310101";
}
