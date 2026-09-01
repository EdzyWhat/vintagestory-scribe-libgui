using Scribe.Core;

namespace Scribe.Core.Tests;

public class ScribeAssignmentStoreTests
{
    private const string Assigner = "assigner-uid";
    private const string Assignee = "assignee-uid";

    private static ScribeAssignmentStore NewStoreWithOneAssignment(out Guid id)
    {
        var store = new ScribeAssignmentStore();
        id = Guid.NewGuid();
        Assert.True(store.TryCreate(id, Assigner, Assignee, "Chop 10 logs", "Year 1, Day 1", out var record));
        Assert.NotNull(record);
        return store;
    }

    // ---- Create ----

    [Fact]
    public void TryCreate_RejectsDuplicateId()
    {
        var store = NewStoreWithOneAssignment(out var id);
        Assert.False(store.TryCreate(id, Assigner, Assignee, "Different text", "Year 1, Day 2", out var record));
        Assert.Null(record);
    }

    [Theory]
    [InlineData("", Assignee, "text")]
    [InlineData(Assigner, "", "text")]
    [InlineData(Assigner, Assignee, "")]
    [InlineData(Assigner, Assignee, "   ")]
    public void TryCreate_RejectsBlankFields(string assigner, string target, string text)
    {
        var store = new ScribeAssignmentStore();
        Assert.False(store.TryCreate(Guid.NewGuid(), assigner, target, text, "Year 1, Day 1", out var record));
        Assert.Null(record);
    }

    [Fact]
    public void TryCreate_ClipsOverlongText()
    {
        var store = new ScribeAssignmentStore();
        string longText = new string('x', ScribeDocumentCodec.MaxTaskTextLength + 500);
        Assert.True(store.TryCreate(Guid.NewGuid(), Assigner, Assignee, longText, "Year 1, Day 1", out var record));
        Assert.Equal(ScribeDocumentCodec.MaxTaskTextLength, record!.Text.Length);
    }

    [Fact]
    public void TryCreate_ItemKindRowAllowsBlankText()
    {
        // A Tracker/Link/Craft row's label lives on TargetItemCode/LinkTarget, not Text (matching every
        // other ScribeBlock of that kind) — assignment-multi-item-creation design.md D12.
        var store = new ScribeAssignmentStore();
        Assert.True(store.TryCreate(Guid.NewGuid(), Assigner, Assignee, "", "Year 1, Day 1", out var record,
            kind: ScribeBlockKind.Tracker, targetItemCode: "game:log-oak", targetQuantity: 10));
        Assert.NotNull(record);
        Assert.Equal(ScribeBlockKind.Tracker, record!.Kind);
        Assert.Equal("game:log-oak", record.TargetItemCode);
        Assert.Equal(10, record.TargetQuantity);
    }

    [Fact]
    public void TryCreate_TaskKindStillRejectsBlankText_EvenWithOtherFieldsSupplied()
    {
        var store = new ScribeAssignmentStore();
        Assert.False(store.TryCreate(Guid.NewGuid(), Assigner, Assignee, "", "Year 1, Day 1", out var record));
        Assert.Null(record);
    }

    [Fact]
    public void TryCreate_CarriesFullBlockShapeForACraftRow()
    {
        var store = new ScribeAssignmentStore();
        Assert.True(store.TryCreate(Guid.NewGuid(), Assigner, Assignee, "", "Year 1, Day 1", out var record,
            kind: ScribeBlockKind.Craft, targetItemCode: "game:ingot-iron", targetQuantity: 4,
            recipeSignature: "recipe-sig-abc", depth: 0));
        Assert.NotNull(record);
        Assert.Equal("recipe-sig-abc", record!.RecipeSignature);
    }

    // ---- Sent / Received filtering ----

    [Fact]
    public void SentAndReceived_FilterByRole()
    {
        var store = NewStoreWithOneAssignment(out _);
        Assert.Single(store.Sent(Assigner));
        Assert.Empty(store.Sent(Assignee));
        Assert.Single(store.Received(Assignee));
        Assert.Empty(store.Received(Assigner));
    }

    [Fact]
    public void SentAndReceived_ShareTheSameObjectAsTryGet()
    {
        var store = NewStoreWithOneAssignment(out var id);
        var sentView = store.Sent(Assigner)[0];
        var receivedView = store.Received(Assignee)[0];
        var direct = store.TryGet(id);
        Assert.Same(direct, sentView);
        Assert.Same(direct, receivedView);
    }

    // ---- Transitions / actor resolution ----

    [Fact]
    public void TryApplyAction_AssigneeCanAccept()
    {
        var store = NewStoreWithOneAssignment(out var id);
        Assert.True(store.TryApplyAction(id, Assignee, ScribeAssignmentAction.Accept));
        Assert.Equal(ScribeAssignmentState.Accepted, store.TryGet(id)!.Assignment!.State);
    }

    [Fact]
    public void TryApplyAction_AssignerCannotAccept()
    {
        var store = NewStoreWithOneAssignment(out var id);
        Assert.False(store.TryApplyAction(id, Assigner, ScribeAssignmentAction.Accept));
        Assert.Equal(ScribeAssignmentState.Unaccepted, store.TryGet(id)!.Assignment!.State);
    }

    [Fact]
    public void TryApplyAction_AssignerCanCancelBeforeAccept()
    {
        var store = NewStoreWithOneAssignment(out var id);
        Assert.True(store.TryApplyAction(id, Assigner, ScribeAssignmentAction.Cancel));
        Assert.Equal(ScribeAssignmentState.Cancelled, store.TryGet(id)!.Assignment!.State);
    }

    [Fact]
    public void TryApplyAction_UnknownIdFails()
    {
        var store = new ScribeAssignmentStore();
        Assert.False(store.TryApplyAction(Guid.NewGuid(), Assignee, ScribeAssignmentAction.Accept));
    }

    [Fact]
    public void TryApplyAction_UninvolvedPlayerFails()
    {
        var store = NewStoreWithOneAssignment(out var id);
        Assert.False(store.TryApplyAction(id, "stranger-uid", ScribeAssignmentAction.Accept));
        Assert.Equal(ScribeAssignmentState.Unaccepted, store.TryGet(id)!.Assignment!.State);
    }

    [Fact]
    public void TryApplyAction_DiscardOnlyLegalAfterAccept()
    {
        var store = NewStoreWithOneAssignment(out var id);
        Assert.False(store.TryApplyAction(id, Assignee, ScribeAssignmentAction.Discard));
        Assert.True(store.TryApplyAction(id, Assignee, ScribeAssignmentAction.Accept));
        Assert.True(store.TryApplyAction(id, Assignee, ScribeAssignmentAction.Discard));
        Assert.Equal(ScribeAssignmentState.Discarded, store.TryGet(id)!.Assignment!.State);
    }

    // ---- Seen ----

    [Fact]
    public void TryMarkSeen_OnlyRecipientCanMark()
    {
        var store = NewStoreWithOneAssignment(out var id);
        Assert.False(store.TryMarkSeen(id, Assigner));
        Assert.False(store.TryGet(id)!.Assignment!.Seen);
        Assert.True(store.TryMarkSeen(id, Assignee));
        Assert.True(store.TryGet(id)!.Assignment!.Seen);
        Assert.False(store.TryMarkSeen(id, Assignee)); // already seen — no-op
    }

    [Fact]
    public void MarkAllSeen_MarksEveryUnseenReceivedAssignment_AndOnlyThoseAddressedToTheCaller()
    {
        var store = new ScribeAssignmentStore();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idOther = Guid.NewGuid();
        const string otherAssignee = "other-assignee-uid";
        Assert.True(store.TryCreate(idA, Assigner, Assignee, "Task A", "Year 1, Day 1", out _));
        Assert.True(store.TryCreate(idB, Assigner, Assignee, "Task B", "Year 1, Day 1", out _));
        Assert.True(store.TryCreate(idOther, Assigner, otherAssignee, "Task C", "Year 1, Day 1", out _));

        Assert.True(store.MarkAllSeen(Assignee));

        Assert.True(store.TryGet(idA)!.Assignment!.Seen);
        Assert.True(store.TryGet(idB)!.Assignment!.Seen);
        Assert.False(store.TryGet(idOther)!.Assignment!.Seen); // addressed to a different player — untouched
    }

    [Fact]
    public void MarkAllSeen_ReturnsFalse_WhenNothingWasUnseen()
    {
        var store = NewStoreWithOneAssignment(out var id);
        Assert.True(store.MarkAllSeen(Assignee));
        Assert.False(store.MarkAllSeen(Assignee)); // already all seen — no-op
    }

    // ---- Round-trip: network list ----

    [Fact]
    public void RoundTrip_SerializeList()
    {
        var store = NewStoreWithOneAssignment(out var id);
        var bytes = ScribeAssignmentStore.SerializeList(store.Received(Assignee));

        Assert.True(ScribeAssignmentStore.TryDeserializeList(bytes, out var restored));
        Assert.NotNull(restored);
        var record = Assert.Single(restored!);
        Assert.Equal(id, record.TaskId);
        Assert.Equal("Chop 10 logs", record.Text);
        Assert.Equal(Assigner, record.Assignment!.AssignerUid);
        Assert.Equal(Assignee, record.Assignment!.TargetPlayerUid);
        Assert.Equal(ScribeAssignmentState.Unaccepted, record.Assignment!.State);
    }

    [Fact]
    public void TryDeserializeList_RejectsNullOrMalformed()
    {
        Assert.False(ScribeAssignmentStore.TryDeserializeList(null, out _));
        Assert.False(ScribeAssignmentStore.TryDeserializeList(new byte[] { 1, 2, 3 }, out _));
    }

    [Fact]
    public void RoundTrip_SerializeList_PreservesRecipeSignatureForACraftRow()
    {
        var store = new ScribeAssignmentStore();
        Assert.True(store.TryCreate(Guid.NewGuid(), Assigner, Assignee, "", "Year 1, Day 1", out _,
            kind: ScribeBlockKind.Craft, targetItemCode: "game:ingot-iron", targetQuantity: 4,
            recipeSignature: "recipe-sig-abc"));
        var bytes = ScribeAssignmentStore.SerializeList(store.Received(Assignee));

        Assert.True(ScribeAssignmentStore.TryDeserializeList(bytes, out var restored));
        var record = Assert.Single(restored!);
        Assert.Equal("recipe-sig-abc", record.RecipeSignature);
    }

    [Fact]
    public void TryDeserializeList_AcceptsAPriorVersionBlob_DefaultingRecipeSignatureToEmpty()
    {
        // Hand-build a v1 blob (no RecipeSignature field) to prove a save/sync predating that field still
        // loads — the whole point of the MinVersion/Version window (design.md D12's serializer note).
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SASN"u8.ToArray());
            w.Write((byte)1); // v1
            w.Write(1); // record count
            w.Write(Guid.NewGuid().ToByteArray());
            w.Write((byte)ScribeBlockKind.Task);
            w.Write("Chop 10 logs");
            w.Write(false); // no TargetItemCode
            w.Write(1); // TargetQuantity
            w.Write(0); // CurrentQuantity
            w.Write(false); // no LinkTarget
            w.Write(false); // no LinkLabel
            w.Write(false); // no LinkDescription
            w.Write(0); // Depth
            // NOTE: no RecipeSignature field here — this IS the v1 shape.
            w.Write(Assigner);
            w.Write(Assignee);
            w.Write((byte)ScribeAssignmentState.Unaccepted);
            w.Write("Year 1, Day 1");
            w.Write(false); // Seen
        }

        Assert.True(ScribeAssignmentStore.TryDeserializeList(ms.ToArray(), out var restored));
        var record = Assert.Single(restored!);
        Assert.Equal("", record.RecipeSignature);
        Assert.Equal("Chop 10 logs", record.Text);
    }

    // ---- Round-trip: v4 per-transition timestamps ----

    [Fact]
    public void RoundTrip_SerializeList_PreservesTransitionTimestamps()
    {
        var store = NewStoreWithOneAssignment(out var id);
        var assignment = store.TryGet(id)!.Assignment!;
        assignment.AcceptedDate = "Year 1, Day 2";
        assignment.CompletedDate = "Year 1, Day 3";
        var bytes = ScribeAssignmentStore.SerializeList(store.Received(Assignee));

        Assert.True(ScribeAssignmentStore.TryDeserializeList(bytes, out var restored));
        var record = Assert.Single(restored!);
        Assert.Equal("Year 1, Day 2", record.Assignment!.AcceptedDate);
        Assert.Equal("Year 1, Day 3", record.Assignment!.CompletedDate);
        Assert.Null(record.Assignment!.DeclinedDate);
        Assert.Null(record.Assignment!.CancelledDate);
        Assert.Null(record.Assignment!.DiscardedDate);
    }

    [Fact]
    public void TryDeserializeList_AcceptsAPriorVersionBlob_DefaultingTransitionTimestampsToNull()
    {
        // A v3 blob (BatchId present, no timestamp fields at all) predates every transition timestamp —
        // those genuinely never happened as far as the record can say, so null is the correct default.
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SASN"u8.ToArray());
            w.Write((byte)3); // v3
            w.Write(1); // record count
            w.Write(Guid.NewGuid().ToByteArray());
            w.Write((byte)ScribeBlockKind.Task);
            w.Write("Chop 10 logs");
            w.Write(false); // no TargetItemCode
            w.Write(1); // TargetQuantity
            w.Write(0); // CurrentQuantity
            w.Write(false); // no LinkTarget
            w.Write(false); // no LinkLabel
            w.Write(false); // no LinkDescription
            w.Write(0); // Depth
            w.Write(""); // RecipeSignature (v2+)
            w.Write(Assigner);
            w.Write(Assignee);
            w.Write((byte)ScribeAssignmentState.Unaccepted);
            w.Write("Year 1, Day 1");
            w.Write(false); // Seen
            w.Write(Guid.NewGuid().ToByteArray()); // BatchId (v3+)
            // NOTE: no timestamp fields here — this IS the v3 shape.
        }

        Assert.True(ScribeAssignmentStore.TryDeserializeList(ms.ToArray(), out var restored));
        var record = Assert.Single(restored!);
        Assert.Null(record.Assignment!.AcceptedDate);
        Assert.Null(record.Assignment!.DeclinedDate);
        Assert.Null(record.Assignment!.CancelledDate);
        Assert.Null(record.Assignment!.DiscardedDate);
        Assert.Null(record.Assignment!.CompletedDate);
    }

    // ---- Round-trip: whole store (savegame) ----

    [Fact]
    public void RoundTrip_SerializeStore_PreservesEveryRecordAndRole()
    {
        var store = new ScribeAssignmentStore();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        Assert.True(store.TryCreate(idA, Assigner, Assignee, "Task A", "Year 1, Day 1", out _));
        Assert.True(store.TryCreate(idB, Assignee, Assigner, "Task B", "Year 1, Day 2", out _));
        Assert.True(store.TryApplyAction(idA, Assignee, ScribeAssignmentAction.Accept));

        var restored = new ScribeAssignmentStore();
        restored.LoadFrom(store.SerializeStore());

        Assert.Equal(ScribeAssignmentState.Accepted, restored.TryGet(idA)!.Assignment!.State);
        Assert.Equal(ScribeAssignmentState.Unaccepted, restored.TryGet(idB)!.Assignment!.State);
        Assert.Single(restored.Sent(Assigner));
        Assert.Single(restored.Received(Assigner)); // idB: Assignee sent to Assigner
    }

    [Fact]
    public void LoadFrom_NullOrMalformedLeavesStoreEmpty()
    {
        var store = NewStoreWithOneAssignment(out _);
        store.LoadFrom(null);
        Assert.Empty(store.Sent(Assigner));

        var store2 = NewStoreWithOneAssignment(out _);
        store2.LoadFrom(new byte[] { 9, 9, 9 });
        Assert.Empty(store2.Sent(Assigner));
    }
}
