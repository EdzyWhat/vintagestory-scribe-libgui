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
    public void RoundTrip_PreservesTrackerAndLinkFields()
    {
        var original = new ScribeDocument();
        original.AddTracker("game:ingot-copper", 8);
        original.SetTrackerCurrentQuantity(original.Blocks[0].TaskId, 3);
        original.AddLink("game:ingot-tin");
        original.AddGuideLink("craftinginfo-knapping", "Knapping"); // v7: guide-page link with a label
        original.AddTask("a plain task"); // its tracker/link fields default and must round-trip too

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);

        var tracker = restored!.Blocks[0];
        Assert.Equal(ScribeBlockKind.Tracker, tracker.Kind);
        Assert.Equal("game:ingot-copper", tracker.TargetItemCode);
        Assert.Equal(8, tracker.TargetQuantity);
        Assert.Equal(3, tracker.CurrentQuantity);
        Assert.Null(tracker.LinkTarget);
        Assert.Null(tracker.LinkLabel);

        var link = restored.Blocks[1];
        Assert.Equal(ScribeBlockKind.Link, link.Kind);
        Assert.Equal("game:ingot-tin", link.LinkTarget);
        Assert.Null(link.TargetItemCode);
        Assert.Null(link.LinkLabel); // an item Link resolves its name live — no stored label

        var guideLink = restored.Blocks[2];
        Assert.Equal(ScribeBlockKind.Link, guideLink.Kind);
        Assert.Equal("page:craftinginfo-knapping", guideLink.LinkTarget);
        Assert.Equal("Knapping", guideLink.LinkLabel); // v7: guide-page label persists
        Assert.Null(guideLink.TargetItemCode);

        var task = restored.Blocks[3];
        Assert.Equal(ScribeBlockKind.Task, task.Kind);
        Assert.Null(task.TargetItemCode);
        Assert.Equal(1, task.TargetQuantity); // default
        Assert.Equal(0, task.CurrentQuantity);
        Assert.Null(task.LinkTarget);
        Assert.Null(task.LinkLabel);
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
    public void TryDeserialize_V3Bytes_FailsSafely()
    {
        // v3 is older than MinVersion (5), so it is not accepted. Hand-build a valid
        // v3 payload and confirm it is rejected rather than misread.
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("SCRB"u8.ToArray());
            w.Write((byte)3);
            w.Write(1); // blockCount
            w.Write((byte)ScribeBlockKind.Task);
            w.Write(false); // done
            w.Write(0);     // depth
            w.Write(false); // pinned
            w.Write(false); // hasAssignedToUid
            w.Write("Find copper");
        }

        bool ok = ScribeDocumentCodec.TryDeserialize(ms.ToArray(), out ScribeDocument? restored);

        Assert.False(ok);
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserialize_V5Bytes_Succeeds_AndDefaultsTrackerLinkFields()
    {
        // v5 is the OLDEST accepted version (MinVersion) — it is the last SHIPPED layout and must stay
        // readable via progressive reads. It has a title but no per-block Tracker/Link fields, so those
        // must default (ApplyPreV6Defaults). The legacy pinned-task migration list is always empty (a
        // v3→v4 concern).
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(new byte[] { (byte)'S', (byte)'C', (byte)'R', (byte)'B' });
            w.Write((byte)5);
            w.Write(Guid.NewGuid().ToByteArray()); // DocId
            w.Write(1); // blockCount
            w.Write(Guid.NewGuid().ToByteArray()); // TaskId
            w.Write((byte)ScribeBlockKind.Task);
            w.Write(false); // done
            w.Write(0);     // depth
            w.Write(false); // hasAssignedToUid
            w.Write("Find copper");
            // v5 has NO Tracker/Link per-block fields here — that's the point.
            w.Write("Old Notes"); // title (v5 has it)
        }
        byte[] v5Bytes = ms.ToArray();

        bool ok = ScribeDocumentCodec.TryDeserialize(v5Bytes, out ScribeDocument? restored, out var legacyPinned);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal("Old Notes", restored!.Title);
        var block = restored.Blocks[0];
        Assert.Equal("Find copper", block.Text);
        Assert.Null(block.TargetItemCode);
        Assert.Equal(1, block.TargetQuantity); // defaulted (≥1 invariant)
        Assert.Equal(0, block.CurrentQuantity);
        Assert.Null(block.LinkTarget);
        Assert.Null(block.LinkLabel); // v7 field — defaulted for a v5 blob
        Assert.Equal("", block.RecipeSignature); // v8 field — defaulted for a v5 blob
        Assert.Empty(legacyPinned);
    }

    [Fact]
    public void TryDeserialize_V6Bytes_Succeeds_AndDefaultsLinkLabel()
    {
        // v6 carried the Tracker/Link item fields but NOT the v7 LinkLabel. A hand-built v6 blob must
        // read via progressive reads: its Tracker/Link fields round-trip, and LinkLabel defaults null.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(new byte[] { (byte)'S', (byte)'C', (byte)'R', (byte)'B' });
            w.Write((byte)6);
            w.Write(docId.ToByteArray());
            w.Write(1); // blockCount
            w.Write(taskId.ToByteArray());
            w.Write((byte)ScribeBlockKind.Link);
            w.Write(false); // done
            w.Write(0);     // depth
            w.Write(false); // hasAssignedToUid
            w.Write("");    // text (Link carries empty text)
            // v6 Tracker/Link fields:
            w.Write(false); // hasTargetItemCode
            w.Write(1);     // targetQuantity
            w.Write(0);     // currentQuantity
            w.Write(true);  // hasLinkTarget
            w.Write("game:ingot-tin"); // linkTarget
            // v6 has NO LinkLabel field here — that's the point.
            w.Write("V6 Notes"); // title
        }

        bool ok = ScribeDocumentCodec.TryDeserialize(ms.ToArray(), out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal("V6 Notes", restored!.Title);
        var link = restored.Blocks[0];
        Assert.Equal(ScribeBlockKind.Link, link.Kind);
        Assert.Equal("game:ingot-tin", link.LinkTarget);
        Assert.Null(link.LinkLabel); // v7 field — defaulted for a v6 blob
        Assert.Equal("", link.RecipeSignature); // v8 field — defaulted for a v6 blob
    }

    [Fact]
    public void TryDeserialize_V7Bytes_Succeeds_AndDefaultsRecipeSignature()
    {
        // v7 carried Tracker/Link fields and LinkLabel but NOT the v8 RecipeSignature. A hand-built
        // v7 blob must read via progressive reads: its v7 fields round-trip, and RecipeSignature
        // defaults empty.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(new byte[] { (byte)'S', (byte)'C', (byte)'R', (byte)'B' });
            w.Write((byte)7);
            w.Write(docId.ToByteArray());
            w.Write(1); // blockCount
            w.Write(taskId.ToByteArray());
            w.Write((byte)ScribeBlockKind.Link);
            w.Write(false); // done
            w.Write(0);     // depth
            w.Write(false); // hasAssignedToUid
            w.Write("");    // text (Link carries empty text)
            // v6 Tracker/Link fields:
            w.Write(false); // hasTargetItemCode
            w.Write(1);     // targetQuantity
            w.Write(0);     // currentQuantity
            w.Write(true);  // hasLinkTarget
            w.Write("page:craftinginfo-knapping"); // linkTarget
            // v7 LinkLabel:
            w.Write(true);  // hasLinkLabel
            w.Write("Knapping");
            // v7 has NO RecipeSignature field here — that's the point.
            w.Write("V7 Notes"); // title
        }

        bool ok = ScribeDocumentCodec.TryDeserialize(ms.ToArray(), out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal("V7 Notes", restored!.Title);
        var link = restored.Blocks[0];
        Assert.Equal(ScribeBlockKind.Link, link.Kind);
        Assert.Equal("page:craftinginfo-knapping", link.LinkTarget);
        Assert.Equal("Knapping", link.LinkLabel);
        Assert.Equal("", link.RecipeSignature); // v8 field — defaulted for a v7 blob
    }

    [Fact]
    public void TryDeserialize_V4Bytes_FailsSafely()
    {
        // v4 is older than MinVersion (5), so it stays out of the accepted window [v5, v8]. A
        // well-formed v4 payload must be rejected outright rather than misread.
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(new byte[] { (byte)'S', (byte)'C', (byte)'R', (byte)'B' });
            w.Write((byte)4);
            w.Write(Guid.NewGuid().ToByteArray()); // DocId
            w.Write(1); // blockCount
            w.Write(Guid.NewGuid().ToByteArray()); // TaskId
            w.Write((byte)ScribeBlockKind.Task);
            w.Write(false); // done
            w.Write(0);     // depth
            w.Write(false); // hasAssignedToUid
            w.Write("Find copper");
            // no title field — v4 ends here
        }

        bool ok = ScribeDocumentCodec.TryDeserialize(ms.ToArray(), out ScribeDocument? restored);

        Assert.False(ok);
        Assert.Null(restored);
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
        // The reader accepts any version in [MinVersion=5, Version=8] (progressive reads).
        // A v2-shaped payload is older than that and must be rejected outright, not misread by
        // reading v5+ fields (ids, title, tracker/link/label) that aren't present.
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
    public void TryDeserialize_OverTextLengthCap_IsClippedNotRejected()
    {
        // add-note-kind-picker §8.2: an over-long freeform note used to reject the WHOLE document (a
        // data-loss trap once notes became user-creatable). It now CLIPS to MaxTextLength — matching the
        // Task clip backstop — and any following blocks survive.
        var original = new ScribeDocument();
        original.AddTextSection(new string('a', ScribeDocumentCodec.MaxTextLength + 500));
        original.AddTask("survivor");

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Blocks.Count);
        Assert.Equal(ScribeDocumentCodec.MaxTextLength, restored.Blocks[0].Text.Length);
        Assert.Equal("survivor", restored.Blocks[1].Text);
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

    [Fact]
    public void Serialize_Deserialize_Title_RoundTrips()
    {
        var original = new ScribeDocument();
        original.Title = "Stone Age Notes";
        original.AddTask("Find flint");

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal("Stone Age Notes", restored!.Title);
    }

    [Fact]
    public void TryDeserialize_CurrentVersion_WhitespaceTitle_SuppliesDefaultTitle()
    {
        // A document serialized with a whitespace-only title (e.g. player cleared it before the
        // blur-normalize fired) must deserialize to the default title, not whitespace.
        var original = new ScribeDocument();
        original.Title = "   "; // whitespace — normalize-on-blur should prevent this, codec clips as backstop

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out ScribeDocument? restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(ScribeDocument.DefaultTitle, restored!.Title);
    }
}
