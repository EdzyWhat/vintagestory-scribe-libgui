using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for serializing a document to bytes and back.
// The same codec is used for both world persistence and network sync,
// so the round-trip must be exact and malformed input must fail safely.
public class ScribeDocumentCodecTests
{
    [Fact]
    public void RoundTrip_PreservesBlockOrderKindsTextAndDoneFlags()
    {
        var original = new ScribeDocument();
        original.AddTask("Find copper");
        original.AddTextSection("Tin is rarer than copper.");
        original.AddTask("Find tin");
        original.AddTask("Build a forge");
        original.ToggleTask(3); // mark "Build a forge" done

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(
            original.Blocks.Select(b => (b.Kind, b.Text, b.Done, b.Depth)),
            restored!.Blocks.Select(b => (b.Kind, b.Text, b.Done, b.Depth)));
    }

    [Fact]
    public void RoundTrip_PreservesPinnedAndAssignedToUid()
    {
        var original = new ScribeDocument();
        original.AddTask("Find copper");
        original.AddTask("Find tin");
        original.TogglePinned(0); // pinned, AssignedToUid still null
        original.Blocks[1].AssignedToUid = "player-1234";

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.True(restored!.Blocks[0].Pinned);
        Assert.Null(restored.Blocks[0].AssignedToUid);
        Assert.False(restored.Blocks[1].Pinned);
        Assert.Equal("player-1234", restored.Blocks[1].AssignedToUid);
    }

    [Fact]
    public void RoundTrip_EmptyDocument()
    {
        var original = new ScribeDocument();

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Empty(restored!.Blocks);
    }

    [Fact]
    public void TryDeserialize_EmptyBytes_FailsSafely()
    {
        bool ok = ScribeDocumentCodec.TryDeserialize(Array.Empty<byte>(), out ScribeDocument? restored);

        Assert.False(ok);
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserialize_EarlierVersionBytes_FailsSafely()
    {
        // Hand-build a v2-shaped payload (no Pinned/AssignedToUid fields) to simulate bytes
        // written before this version bump -- the codec must reject it outright, not
        // misread the missing fields as defaults.
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("SCRB"u8.ToArray());
            w.Write((byte)2);
            w.Write(1); // blockCount
            w.Write((byte)ScribeBlockKind.Task);
            w.Write(false); // done
            w.Write(0); // depth
            w.Write("Old-format task"); // text
        }

        bool ok = ScribeDocumentCodec.TryDeserialize(ms.ToArray(), out ScribeDocument? restored);

        Assert.False(ok);
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserialize_MalformedBytes_FailsSafely()
    {
        var garbage = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0x7A };

        bool ok = ScribeDocumentCodec.TryDeserialize(garbage, out ScribeDocument? restored);

        Assert.False(ok);
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserialize_AtBlockCountCap_Succeeds()
    {
        var original = new ScribeDocument();
        for (int i = 0; i < ScribeDocumentCodec.MaxBlocks; i++) original.AddTask($"task {i}");

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(ScribeDocumentCodec.MaxBlocks, restored!.Blocks.Count);
    }

    [Fact]
    public void TryDeserialize_OverBlockCountCap_FailsSafely()
    {
        // Serialize is uncapped (it trusts an in-memory document); a payload over MaxBlocks can only
        // arrive from a hostile/buggy client, so the read path must reject it.
        var original = new ScribeDocument();
        for (int i = 0; i < ScribeDocumentCodec.MaxBlocks + 1; i++) original.AddTask($"task {i}");

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.False(ok);
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserialize_AtTextLengthCap_Succeeds()
    {
        var original = new ScribeDocument();
        original.AddTask(new string('a', ScribeDocumentCodec.MaxTextLength));

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(ScribeDocumentCodec.MaxTextLength, restored!.Blocks[0].Text.Length);
    }

    [Fact]
    public void TryDeserialize_OverTextLengthCap_FailsSafely()
    {
        var original = new ScribeDocument();
        original.AddTask(new string('a', ScribeDocumentCodec.MaxTextLength + 1));

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.False(ok);
        Assert.Null(restored);
    }
}
