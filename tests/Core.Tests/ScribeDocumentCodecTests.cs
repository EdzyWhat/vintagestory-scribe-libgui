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
    public void RoundTrip_PreservesAssignedToUid()
    {
        var original = new ScribeDocument();
        original.AddTask("Find copper");
        original.AddTask("Find tin");
        original.Blocks[1].AssignedToUid = "player-1234";

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Null(restored!.Blocks[0].AssignedToUid);
        Assert.Equal("player-1234", restored.Blocks[1].AssignedToUid);
    }

    [Fact]
    public void RoundTrip_PreservesDocIdAndTaskIds()
    {
        var original = new ScribeDocument();
        original.AddTask("Find copper");
        original.AddTextSection("Tin is rarer than copper.");
        original.AddTask("Find tin");

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(original.DocId, restored!.DocId);
        Assert.Equal(
            original.Blocks.Select(b => b.TaskId),
            restored.Blocks.Select(b => b.TaskId));
    }

    [Fact]
    public void TaskIds_AreStableAcrossMutations()
    {
        // Reordering, inserting, deleting other blocks, and editing text must not change the
        // surviving blocks' ids (an external pin references a task by (DocId, TaskId)).
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        doc.AddTask("C");
        var idA = doc.Blocks[0].TaskId;
        var idB = doc.Blocks[1].TaskId;
        var idC = doc.Blocks[2].TaskId;
        var docId = doc.DocId;

        doc.MoveBlock(0, 2);          // A to the end
        doc.InsertTask(0, "D");       // new block at the front
        doc.SetBlockText(1, "B-edited");
        doc.ToggleTask(1);
        doc.DeleteBlock(doc.Blocks.Count - 1); // delete whatever is last now

        // DocId unchanged; the surviving originals keep their ids; the insert got a distinct one.
        Assert.Equal(docId, doc.DocId);
        var survivingIds = doc.Blocks.Select(b => b.TaskId).ToList();
        Assert.Contains(idB, survivingIds);
        Assert.Contains(idC, survivingIds);
        var inserted = doc.Blocks.Single(b => b.Text == "D");
        Assert.NotEqual(idA, inserted.TaskId);
        Assert.NotEqual(idB, inserted.TaskId);
        Assert.NotEqual(idC, inserted.TaskId);
        // No duplicate ids anywhere.
        Assert.Equal(survivingIds.Count, survivingIds.Distinct().Count());
    }

    [Fact]
    public void TryDeserialize_V3Bytes_Succeeds_AndSurfacesLegacyPinnedIds()
    {
        // Hand-build a v3 payload (the immediately prior format): DocId/TaskId absent, a per-block
        // `pinned` bool present. Two tasks, the second pinned.
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("SCRB"u8.ToArray());
            w.Write((byte)3);
            w.Write(2); // blockCount
            // block 0 — not pinned
            w.Write((byte)ScribeBlockKind.Task);
            w.Write(false); // done
            w.Write(0);     // depth
            w.Write(false); // pinned
            w.Write(false); // hasAssignedToUid
            w.Write("Find copper");
            // block 1 — pinned
            w.Write((byte)ScribeBlockKind.Task);
            w.Write(true);  // done
            w.Write(0);     // depth
            w.Write(true);  // pinned
            w.Write(false); // hasAssignedToUid
            w.Write("Find tin");
        }

        bool ok = ScribeDocumentCodec.TryDeserialize(ms.ToArray(), out ScribeDocument? restored, out var legacyPinned);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Blocks.Count);
        Assert.Equal("Find copper", restored.Blocks[0].Text);
        Assert.True(restored.Blocks[1].Done);
        // A fresh DocId was generated (not the empty Guid) and every block got a fresh TaskId.
        Assert.NotEqual(Guid.Empty, restored.DocId);
        Assert.All(restored.Blocks, b => Assert.NotEqual(Guid.Empty, b.TaskId));
        // Exactly the pinned block's (freshly generated) id is surfaced for migration, and it
        // matches the id now on that block.
        var pinnedId = Assert.Single(legacyPinned);
        Assert.Equal(restored.Blocks[1].TaskId, pinnedId);
    }

    [Fact]
    public void TryDeserialize_V4Bytes_SurfaceNoLegacyPinnedIds()
    {
        var original = new ScribeDocument();
        original.AddTask("Find copper");

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored, out var legacyPinned);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Empty(legacyPinned);
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
    public void TryDeserialize_UnsupportedOlderVersionBytes_FailsSafely()
    {
        // The reader accepts only the current version (4) and the immediately prior one (3).
        // A v2-shaped payload is older than that and must be rejected outright, not misread by
        // reading v3/v4 fields (ids, pinned) that aren't present.
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
        // The hard MaxTextLength cap applies to freeform Text sections (Task blocks clip instead — see
        // the task-clip tests below), so exercise it with a Text section.
        var original = new ScribeDocument();
        original.AddTextSection(new string('a', ScribeDocumentCodec.MaxTextLength));

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
        original.AddTextSection(new string('a', ScribeDocumentCodec.MaxTextLength + 1));

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.False(ok);
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserialize_TaskTextAtTaskCap_IsPreserved()
    {
        var original = new ScribeDocument();
        original.AddTask(new string('a', ScribeDocumentCodec.MaxTaskTextLength));

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.Equal(ScribeDocumentCodec.MaxTaskTextLength, restored!.Blocks[0].Text.Length);
    }

    [Fact]
    public void TryDeserialize_OverLongTaskText_IsClippedNotRejected()
    {
        // The pre-2026-07-26 behavior rejected the whole payload for an over-long block, silently dropping
        // the document. A Task now CLIPS to MaxTaskTextLength and the rest of the document survives.
        var original = new ScribeDocument();
        original.AddTask(new string('a', ScribeDocumentCodec.MaxTaskTextLength + 500));
        original.AddTask("survivor");

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Blocks.Count);
        Assert.Equal(ScribeDocumentCodec.MaxTaskTextLength, restored.Blocks[0].Text.Length);
        Assert.Equal("survivor", restored.Blocks[1].Text);
    }

    [Fact]
    public void TryDeserialize_LongTextSection_IsNotClippedToTaskCap()
    {
        // A freeform Text section between the task cap and the hard cap is kept in full — the task clip
        // must not bleed onto Text blocks.
        var original = new ScribeDocument();
        original.AddTextSection(new string('a', ScribeDocumentCodec.MaxTaskTextLength + 500));

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.Equal(ScribeDocumentCodec.MaxTaskTextLength + 500, restored!.Blocks[0].Text.Length);
    }
}
