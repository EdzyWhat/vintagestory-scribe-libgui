using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the document as an ordered sequence of blocks (tasks and text sections).
// Each test maps to a WHEN/THEN scenario in the task-note-document spec.
public class ScribeDocumentTests
{
    // --- Document structure ---

    [Fact]
    public void NewDocument_IsEmpty()
    {
        var doc = new ScribeDocument();

        Assert.Empty(doc.Blocks);
    }

    [Fact]
    public void Blocks_PreserveInsertionOrder()
    {
        var doc = new ScribeDocument();

        doc.AddTask("First");
        doc.AddTextSection("A note");
        doc.AddTask("Third");

        Assert.Equal(
            new[] { ("First", ScribeBlockKind.Task), ("A note", ScribeBlockKind.Text), ("Third", ScribeBlockKind.Task) },
            doc.Blocks.Select(b => (b.Text, b.Kind)));
    }

    // --- Add blocks ---

    [Fact]
    public void AddTask_AddsAnIncompleteTaskBlock()
    {
        var doc = new ScribeDocument();

        bool ok = doc.AddTask("Find copper");

        Assert.True(ok);
        var block = Assert.Single(doc.Blocks);
        Assert.Equal(ScribeBlockKind.Task, block.Kind);
        Assert.Equal("Find copper", block.Text);
        Assert.False(block.Done);
    }

    [Fact]
    public void AddTextSection_AddsATextBlock()
    {
        var doc = new ScribeDocument();

        bool ok = doc.AddTextSection("Copper is south of the ridge");

        Assert.True(ok);
        var block = Assert.Single(doc.Blocks);
        Assert.Equal(ScribeBlockKind.Text, block.Kind);
        Assert.Equal("Copper is south of the ridge", block.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void AddTask_AcceptsBlankText_StoredVerbatim(string blank)
    {
        // A new task now starts empty (the player types into it); the model no longer rejects
        // blank task text. Removing an abandoned empty task is the editing layer's job.
        var doc = new ScribeDocument();

        Assert.True(doc.AddTask(blank));
        Assert.Single(doc.Blocks);
        Assert.Equal(ScribeBlockKind.Task, doc.Blocks[0].Kind);
        Assert.Equal(blank, doc.Blocks[0].Text);
    }

    [Fact]
    public void AddTask_StoresTextVerbatim_WithoutTrimming()
    {
        // The model stores task text verbatim; whitespace normalization is the editing layer's job
        // (GuiDialogScribeLectern.NormalizeRowOnCommit), not the domain model's.
        var doc = new ScribeDocument();

        doc.AddTask("  Find copper  ");

        Assert.Equal("  Find copper  ", doc.Blocks[0].Text);
    }

    [Fact]
    public void AddTextSection_AllowsBlankText()
    {
        // A text section may be empty (e.g. a spacer the player is about to fill in).
        var doc = new ScribeDocument();

        bool ok = doc.AddTextSection("");

        Assert.True(ok);
        Assert.Equal("", doc.Blocks[0].Text);
    }

    // --- Edit block text ---

    [Fact]
    public void SetBlockText_ChangesTextAndKeepsDoneFlag()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");
        doc.ToggleTask(0); // now done

        bool ok = doc.SetBlockText(0, "Find tin");

        Assert.True(ok);
        Assert.Equal("Find tin", doc.Blocks[0].Text);
        Assert.True(doc.Blocks[0].Done);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public void SetBlockText_AcceptsBlankForTask_StoredVerbatim(string blank)
    {
        // Clearing a task to empty must succeed (the field writes through on every keystroke), so
        // the task can go transiently empty while the player edits it. The editing layer removes a
        // task left empty on blur; the model just stores the value.
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");

        Assert.True(doc.SetBlockText(0, blank));
        Assert.Equal(blank, doc.Blocks[0].Text);
    }

    [Fact]
    public void SetBlockText_StoresTaskTextVerbatim_WithoutTrimming()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");

        // The model stores task text verbatim -- surrounding whitespace (incl. a trailing newline
        // from a just-typed Shift+Enter) is preserved so the live in-place editor's row can grow to
        // fit it. Trimming happens later, on commit, in the editing layer.
        doc.SetBlockText(0, "  Find tin\n");

        Assert.Equal("  Find tin\n", doc.Blocks[0].Text);
    }

    [Fact]
    public void SetBlockText_AllowsBlankForTextSection()
    {
        var doc = new ScribeDocument();
        doc.AddTextSection("something");

        bool ok = doc.SetBlockText(0, "");

        Assert.True(ok);
        Assert.Equal("", doc.Blocks[0].Text);
    }

    // --- Edit task text by stable identity (SetTaskText) ---

    [Fact]
    public void SetTaskText_ByPresentId_ChangesTextAndKeepsDoneFlag()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");
        doc.ToggleTask(0); // now done
        var id = doc.Blocks[0].TaskId;

        bool ok = doc.SetTaskText(id, "Find tin");

        Assert.True(ok);
        Assert.Equal("Find tin", doc.Blocks[0].Text);
        Assert.True(doc.Blocks[0].Done);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void SetTaskText_RejectsBlankOrWhitespace_LeavesTaskUnchanged(string blank)
    {
        // Unlike SetBlockText (which stores blanks verbatim), the identity-addressed pin edit honors
        // the content invariant: a blank/whitespace-only edit is rejected and the task is untouched, so
        // a pin edit can never blank a task out.
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");
        var id = doc.Blocks[0].TaskId;

        Assert.False(doc.SetTaskText(id, blank));
        Assert.Equal("Find copper", doc.Blocks[0].Text);
    }

    [Fact]
    public void SetTaskText_OnAbsentId_IsNoOp()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");

        Assert.False(doc.SetTaskText(Guid.NewGuid(), "Find tin"));
        Assert.Equal("Find copper", doc.Blocks[0].Text);
    }

    [Fact]
    public void SetTaskText_OnTextSectionId_Fails()
    {
        var doc = new ScribeDocument();
        doc.AddTextSection("not a task");
        var id = doc.Blocks[0].TaskId;

        Assert.False(doc.SetTaskText(id, "changed"));
        Assert.Equal("not a task", doc.Blocks[0].Text);
    }

    [Fact]
    public void SetTaskText_LeavesOtherBlocksUntouched()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        doc.AddTextSection("note");
        var idB = doc.Blocks[1].TaskId;

        Assert.True(doc.SetTaskText(idB, "B-edited"));
        Assert.Equal(new[] { "A", "B-edited", "note" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void SetTaskText_StoresTextVerbatim_WithoutTrimming()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");
        var id = doc.Blocks[0].TaskId;

        // Non-blank text is stored verbatim (surrounding whitespace preserved) — the same no-trim rule
        // as SetBlockText; only fully-blank input is rejected.
        doc.SetTaskText(id, "  Find tin  ");

        Assert.Equal("  Find tin  ", doc.Blocks[0].Text);
    }

    // --- Toggle completion (tasks only) ---

    [Fact]
    public void ToggleTask_IncompleteBecomesComplete()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Build a forge");

        Assert.True(doc.ToggleTask(0));
        Assert.True(doc.Blocks[0].Done);
    }

    [Fact]
    public void ToggleTask_CompleteBecomesIncomplete()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Build a forge");
        doc.ToggleTask(0);

        doc.ToggleTask(0);

        Assert.False(doc.Blocks[0].Done);
    }

    [Fact]
    public void ToggleTask_OnTextSection_Fails()
    {
        var doc = new ScribeDocument();
        doc.AddTextSection("not a task");

        Assert.False(doc.ToggleTask(0));
    }

    // --- Stable identifiers ---

    [Fact]
    public void NewDocument_HasNonEmptyDocId()
    {
        var doc = new ScribeDocument();

        Assert.NotEqual(Guid.Empty, doc.DocId);
    }

    [Fact]
    public void AddedBlocks_GetDistinctNonEmptyTaskIds()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        doc.AddTextSection("note");

        Assert.All(doc.Blocks, b => Assert.NotEqual(Guid.Empty, b.TaskId));
        Assert.Equal(3, doc.Blocks.Select(b => b.TaskId).Distinct().Count());
    }

    [Fact]
    public void FindByTaskId_ReturnsMatchingBlock()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        var id = doc.Blocks[1].TaskId;

        var found = doc.FindByTaskId(id);

        Assert.NotNull(found);
        Assert.Equal("B", found!.Text);
    }

    [Fact]
    public void FindByTaskId_ReturnsNullWhenAbsent()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");

        Assert.Null(doc.FindByTaskId(Guid.NewGuid()));
    }

    // --- Delete ---

    [Fact]
    public void DeleteBlock_RemovesByIndexAndPreservesOrder()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        doc.AddTask("C");

        Assert.True(doc.DeleteBlock(1));
        Assert.Equal(new[] { "A", "C" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void DeleteBlock_ReportsRemovedTaskId()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        var idB = doc.Blocks[1].TaskId;

        bool ok = doc.DeleteBlock(1, out Guid? deletedTaskId);

        Assert.True(ok);
        Assert.Equal(idB, deletedTaskId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void DeleteBlock_AtInvalidIndex_ReportsNoRemoval(int badIndex)
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");

        bool ok = doc.DeleteBlock(badIndex, out Guid? deletedTaskId);

        Assert.False(ok);
        Assert.Null(deletedTaskId);
        Assert.Single(doc.Blocks);
    }

    // --- Insert ---

    [Fact]
    public void InsertTask_InsertsUnderCurrentAndShiftsRest()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("C");

        bool ok = doc.InsertTask(1, "B"); // new task under index 0 (the "A" row)

        Assert.True(ok);
        Assert.Equal(new[] { "A", "B", "C" }, doc.Blocks.Select(b => b.Text));
        Assert.True(doc.Blocks[1].IsTask);
        Assert.False(doc.Blocks[1].Done);
    }

    [Fact]
    public void InsertTask_AtCount_Appends()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");

        Assert.True(doc.InsertTask(doc.Blocks.Count, "B"));
        Assert.Equal(new[] { "A", "B" }, doc.Blocks.Select(b => b.Text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InsertTask_AcceptsBlankText_StoredVerbatim(string blank)
    {
        // Enter=insert-below and "Add task" both seed an empty task now; InsertTask must accept it.
        var doc = new ScribeDocument();
        doc.AddTask("A");

        Assert.True(doc.InsertTask(1, blank));
        Assert.Equal(2, doc.Blocks.Count);
        Assert.Equal(ScribeBlockKind.Task, doc.Blocks[1].Kind);
        Assert.Equal(blank, doc.Blocks[1].Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)] // one block present → count is 1, so 2 is past the append slot
    public void InsertTask_OnInvalidIndex_FailsSafely(int badIndex)
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");

        Assert.False(doc.InsertTask(badIndex, "B"));
        Assert.Single(doc.Blocks);
    }

    // --- Reorder ---

    [Fact]
    public void MoveBlock_ReordersWithinList()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        doc.AddTask("C");

        bool ok = doc.MoveBlock(0, 2); // move A to the end

        Assert.True(ok);
        Assert.Equal(new[] { "B", "C", "A" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void MoveBlock_UpwardReorders()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        doc.AddTask("C");

        doc.MoveBlock(2, 0); // move C to the front

        Assert.Equal(new[] { "C", "A", "B" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void MoveBlock_SamePosition_IsNoOpSuccess()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");

        Assert.True(doc.MoveBlock(1, 1));
        Assert.Equal(new[] { "A", "B" }, doc.Blocks.Select(b => b.Text));
    }

    // --- MoveTaskToBottom (Sink completion) ---

    [Fact]
    public void MoveTaskToBottom_MovesToEnd_PreservingOthersOrder()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        doc.AddTask("C");
        var idA = doc.Blocks[0].TaskId;

        bool ok = doc.MoveTaskToBottom(idA);

        Assert.True(ok);
        Assert.Equal(new[] { "B", "C", "A" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void MoveTaskToBottom_AlreadyLast_IsNoOpFalse()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");
        doc.AddTask("B");
        var idB = doc.Blocks[1].TaskId;

        // Already last: nothing to do, reported as false, order unchanged.
        Assert.False(doc.MoveTaskToBottom(idB));
        Assert.Equal(new[] { "A", "B" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void MoveTaskToBottom_UnknownId_FailsSafely()
    {
        var doc = new ScribeDocument();
        doc.AddTask("A");

        Assert.False(doc.MoveTaskToBottom(Guid.NewGuid()));
        Assert.Equal(new[] { "A" }, doc.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void MoveTaskToBottom_MovesPastTextSections()
    {
        var doc = new ScribeDocument();
        doc.AddTask("task1");
        doc.AddTextSection("a note");
        doc.AddTask("task2");
        var id1 = doc.Blocks[0].TaskId;

        Assert.True(doc.MoveTaskToBottom(id1));
        // task1 sinks to the very end, below the note and task2.
        Assert.Equal(new[] { "a note", "task2", "task1" }, doc.Blocks.Select(b => b.Text));
    }

    // --- Out-of-range safety ---

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]  // empty document: index 0 is already out of range
    [InlineData(5)]
    public void Operations_OnInvalidIndex_FailSafely(int badIndex)
    {
        var doc = new ScribeDocument();

        Assert.False(doc.SetBlockText(badIndex, "x"));
        Assert.False(doc.ToggleTask(badIndex));
        Assert.False(doc.DeleteBlock(badIndex));
        Assert.False(doc.MoveBlock(badIndex, 0));
        Assert.False(doc.MoveBlock(0, badIndex));
        Assert.Empty(doc.Blocks);
    }

    // --- Tracker & Link kinds ---

    [Fact]
    public void AddTracker_AddsATrackerBlockWithItemAndTarget()
    {
        var doc = new ScribeDocument();

        bool ok = doc.AddTracker("game:ingot-copper", 8);

        Assert.True(ok);
        var block = Assert.Single(doc.Blocks);
        Assert.Equal(ScribeBlockKind.Tracker, block.Kind);
        Assert.True(block.IsTracker);
        Assert.Equal("game:ingot-copper", block.TargetItemCode);
        Assert.Equal(8, block.TargetQuantity);
        Assert.Equal(0, block.CurrentQuantity);
        Assert.False(block.Done);
    }

    [Fact]
    public void AddLink_AddsALinkBlockWithTarget()
    {
        var doc = new ScribeDocument();

        bool ok = doc.AddLink("game:ingot-copper");

        Assert.True(ok);
        var block = Assert.Single(doc.Blocks);
        Assert.Equal(ScribeBlockKind.Link, block.Kind);
        Assert.True(block.IsLink);
        Assert.Equal("game:ingot-copper", block.LinkTarget);
        Assert.False(block.Done);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AddTracker_ClampsTargetQuantityToAtLeastOne(int badTarget)
    {
        var doc = new ScribeDocument();

        doc.AddTracker("game:stick", badTarget);

        Assert.Equal(1, doc.Blocks[0].TargetQuantity);
    }

    [Fact]
    public void Tracker_CurrentQuantity_ClampsToNonNegative_ButAllowsOverflow()
    {
        var block = new ScribeBlock(ScribeBlockKind.Tracker, "", targetItemCode: "game:stick", targetQuantity: 5);

        block.CurrentQuantity = -3;
        Assert.Equal(0, block.CurrentQuantity); // still floored at 0

        block.CurrentQuantity = 99;
        Assert.Equal(99, block.CurrentQuantity); // NOT capped at the target — overflow is meaningful (7.14)

        block.CurrentQuantity = 3;
        Assert.Equal(3, block.CurrentQuantity);
    }

    [Fact]
    public void Tracker_LoweringTarget_LeavesCurrentUntouched()
    {
        var block = new ScribeBlock(ScribeBlockKind.Tracker, "", targetItemCode: "game:stick", targetQuantity: 10);
        block.CurrentQuantity = 8;

        block.TargetQuantity = 4; // target drops below current

        Assert.Equal(4, block.TargetQuantity);
        Assert.Equal(8, block.CurrentQuantity); // current is the raw carried count; lowering the target no longer re-clamps it (7.14)
    }

    [Fact]
    public void SetTrackerCurrentQuantity_ByTaskId_UpdatesWithoutUpperClamp()
    {
        var doc = new ScribeDocument();
        doc.AddTracker("game:ingot-copper", 8);
        var id = doc.Blocks[0].TaskId;

        Assert.True(doc.SetTrackerCurrentQuantity(id, 3));
        Assert.Equal(3, doc.Blocks[0].CurrentQuantity);

        Assert.True(doc.SetTrackerCurrentQuantity(id, 100)); // overflow preserved, not capped at the target (7.14)
        Assert.Equal(100, doc.Blocks[0].CurrentQuantity);
    }

    [Fact]
    public void SetTrackerCurrentQuantity_OnNonTrackerOrMissingId_FailsSafely()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Not a tracker");
        var taskId = doc.Blocks[0].TaskId;

        Assert.False(doc.SetTrackerCurrentQuantity(taskId, 5));   // id belongs to a Task
        Assert.False(doc.SetTrackerCurrentQuantity(Guid.NewGuid(), 5)); // no such id
    }

    [Fact]
    public void TrackerAndLink_GetDistinctTaskIds_AndPreserveOrder()
    {
        var doc = new ScribeDocument();
        doc.AddTask("plain");
        doc.AddTracker("game:ingot-copper", 8);
        doc.AddLink("game:ingot-tin");

        Assert.Equal(
            new[] { ScribeBlockKind.Task, ScribeBlockKind.Tracker, ScribeBlockKind.Link },
            doc.Blocks.Select(b => b.Kind));
        var ids = doc.Blocks.Select(b => b.TaskId).ToList();
        Assert.Equal(3, ids.Distinct().Count());
    }

    // --- CompletableCount (Transcribe overwrite "N tasks" prompt) ---

    [Fact]
    public void CompletableCount_CountsTaskTrackerLink_NotText()
    {
        var doc = new ScribeDocument();
        doc.AddTask("a");
        doc.AddTextSection("just a note");
        doc.AddTracker("game:ingot-copper", 8);
        doc.AddLink("game:ingot-tin");
        doc.AddTextSection("another note");

        Assert.Equal(3, doc.CompletableCount); // Task + Tracker + Link; the two Text sections don't count
    }

    [Fact]
    public void CompletableCount_IsZero_ForEmptyOrTextOnlyDocument()
    {
        var empty = new ScribeDocument();
        Assert.Equal(0, empty.CompletableCount);

        var textOnly = new ScribeDocument();
        textOnly.AddTextSection("prose");
        Assert.Equal(0, textOnly.CompletableCount);
    }

    // --- TaskCount (diagnostic task-only tally; NOT the cap measure) ---
    // Distinct from CompletableCount (Task+Tracker+Link) and from BlockCount (all kinds). TaskCount counts
    // ONLY Task-kind blocks. Since refine-chalkboard §12 the tier cap counts ALL blocks (BlockCount), so
    // TaskCount is no longer the cap/capacity measure — it survives only for diagnostics and this coverage.

    [Fact]
    public void TaskCount_CountsTaskKindOnly_NotTrackerLinkOrText()
    {
        var doc = new ScribeDocument();
        doc.AddTask("a");
        doc.AddTask("b");
        doc.AddTextSection("just a note");
        doc.AddTracker("game:ingot-copper", 8);
        doc.AddLink("game:ingot-tin");

        Assert.Equal(2, doc.TaskCount); // only the two Task blocks; tracker/link/text excluded
    }

    [Fact]
    public void TaskCount_IsZero_ForEmptyOrTaskFreeDocument()
    {
        var empty = new ScribeDocument();
        Assert.Equal(0, empty.TaskCount);

        var noTasks = new ScribeDocument();
        noTasks.AddTextSection("prose");
        noTasks.AddTracker("game:stick", 4);
        noTasks.AddLink("game:ingot-tin");
        Assert.Equal(0, noTasks.TaskCount);
    }

    // --- BlockCount (the tier-cap measure: "N of anything", refine-chalkboard §12) ---
    // Every kind counts equally — tasks, notes, trackers, links, craft parents — so a finite tier
    // (Tablet, Chalkboard: MaxBlocks = 10) is full at 10 blocks of ANY mix and refuses an 11th of any kind.

    [Fact]
    public void BlockCount_CountsEveryKind()
    {
        var doc = new ScribeDocument();
        doc.AddTask("a");
        doc.AddTextSection("just a note");
        doc.AddTracker("game:ingot-copper", 8);
        doc.AddLink("game:ingot-tin");

        Assert.Equal(4, doc.BlockCount);          // task + note + tracker + link all count
        Assert.Equal(doc.Blocks.Count, doc.BlockCount); // tracks the underlying list exactly
    }

    [Fact]
    public void BlockCount_IsZero_ForEmptyDocument()
    {
        Assert.Equal(0, new ScribeDocument().BlockCount);
    }

    [Fact]
    public void FiniteCap_CountsMixedKinds_RefusesEleventhOfAnyKind()
    {
        // A finite tier caps at 10 blocks of ANY kind. Fill to 10 with a deliberate mix, then confirm
        // the policy (fed BlockCount) refuses the 11th regardless of what kind it would be.
        var policy = ScribeDocumentPolicy.Tablet; // MaxBlocks = 10
        var doc = new ScribeDocument();
        doc.AddTask("t1");
        doc.AddTextSection("n1");
        doc.AddTracker("game:ingot-copper", 8);
        doc.AddLink("game:ingot-tin");
        doc.AddTask("t2");
        doc.AddTextSection("n2");
        doc.AddTask("t3");
        doc.AddLink("game:stick");
        doc.AddTextSection("n3");
        doc.AddTracker("game:plank", 4);

        Assert.Equal(10, doc.BlockCount);
        Assert.False(policy.CanAdd(doc.BlockCount)); // full: an 11th of ANY kind is refused
        Assert.True(policy.CanHold(doc.BlockCount));  // exactly 10 is a legal Transcribe destination
    }

    // --- CloneWithNewIdentity (Transcribe copy primitive) ---

    [Fact]
    public void CloneWithNewIdentity_CopiesContentVerbatim()
    {
        var source = new ScribeDocument { Title = "Shopping list" };
        source.AddTask("Find copper");
        source.ToggleTask(0); // done = true
        source.AddTextSection("Remember the flux");
        source.AddTracker("game:ingot-copper", 8);
        source.SetTrackerCurrentQuantity(source.Blocks[2].TaskId, 3);
        source.AddLink("game:ingot-tin");
        source.AddGuideLink("craftinginfo-knapping", "Knapping");

        var copy = source.CloneWithNewIdentity();

        Assert.Equal(source.Title, copy.Title);
        Assert.Equal(source.Blocks.Count, copy.Blocks.Count);
        for (int i = 0; i < source.Blocks.Count; i++)
        {
            var a = source.Blocks[i];
            var b = copy.Blocks[i];
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.Text, b.Text);
            Assert.Equal(a.Done, b.Done);
            Assert.Equal(a.Depth, b.Depth);
            Assert.Equal(a.AssignedToUid, b.AssignedToUid);
            Assert.Equal(a.TargetItemCode, b.TargetItemCode);
            Assert.Equal(a.TargetQuantity, b.TargetQuantity);
            Assert.Equal(a.CurrentQuantity, b.CurrentQuantity);
            Assert.Equal(a.LinkTarget, b.LinkTarget);
            Assert.Equal(a.LinkLabel, b.LinkLabel);
        }
    }

    [Fact]
    public void CloneWithNewIdentity_MintsAllNewGuids()
    {
        var source = new ScribeDocument();
        source.AddTask("one");
        source.AddTracker("game:ingot-copper", 8);
        source.AddLink("game:ingot-tin");

        var copy = source.CloneWithNewIdentity();

        // Fresh DocId...
        Assert.NotEqual(source.DocId, copy.DocId);
        // ...and a fresh TaskId for every block (none shared with the source).
        var sourceIds = source.Blocks.Select(b => b.TaskId).ToHashSet();
        foreach (var block in copy.Blocks)
            Assert.DoesNotContain(block.TaskId, sourceIds);
        // The copy's own ids are all distinct too.
        Assert.Equal(copy.Blocks.Count, copy.Blocks.Select(b => b.TaskId).Distinct().Count());
    }

    [Fact]
    public void CloneWithNewIdentity_LeavesSourceUnchanged()
    {
        var source = new ScribeDocument { Title = "Original" };
        source.AddTask("keep me");
        var originalDocId = source.DocId;
        var originalTaskId = source.Blocks[0].TaskId;

        var copy = source.CloneWithNewIdentity();

        // Editing the copy must not touch the source (fully independent).
        copy.SetBlockText(0, "changed on the copy");
        copy.AddTask("only on the copy");

        Assert.Equal(originalDocId, source.DocId);
        Assert.Equal("Original", source.Title);
        var block = Assert.Single(source.Blocks);
        Assert.Equal("keep me", block.Text);
        Assert.Equal(originalTaskId, block.TaskId);
    }

    // --- AppendClonedBlocksFrom (Transcribe "append" copy mode) ---

    [Fact]
    public void AppendClonedBlocksFrom_AddsSourceBlocksToEnd_KeepingTargetsOwn()
    {
        var target = new ScribeDocument { Title = "My list" };
        target.AddTask("existing A");
        target.AddTask("existing B");

        var source = new ScribeDocument { Title = "Other list" };
        source.AddTask("incoming C");
        source.AddTextSection("incoming note");

        target.AppendClonedBlocksFrom(source);

        // Target keeps its title and its own two blocks, with the source's two appended AFTER them, in order.
        Assert.Equal("My list", target.Title);
        Assert.Equal(
            new[] { "existing A", "existing B", "incoming C", "incoming note" },
            target.Blocks.Select(b => b.Text));
        Assert.Equal(ScribeBlockKind.Text, target.Blocks[3].Kind);
    }

    [Fact]
    public void AppendClonedBlocksFrom_MintsNewTaskIds_AndKeepsTargetDocId()
    {
        var target = new ScribeDocument();
        target.AddTask("existing");
        var targetDocId = target.DocId;
        var existingId = target.Blocks[0].TaskId;

        var source = new ScribeDocument();
        source.AddTask("incoming one");
        source.AddTracker("game:ingot-copper", 8);

        target.AppendClonedBlocksFrom(source);

        // Append never changes the target's identity or its existing block's id (unlike an overwrite clone).
        Assert.Equal(targetDocId, target.DocId);
        Assert.Equal(existingId, target.Blocks[0].TaskId);
        // The appended blocks carry FRESH ids — none shared with the source or with the pre-existing block.
        var sourceIds = source.Blocks.Select(b => b.TaskId).ToHashSet();
        foreach (var appended in target.Blocks.Skip(1))
        {
            Assert.DoesNotContain(appended.TaskId, sourceIds);
            Assert.NotEqual(existingId, appended.TaskId);
        }
        // Every id in the resulting document is distinct.
        Assert.Equal(target.Blocks.Count, target.Blocks.Select(b => b.TaskId).Distinct().Count());
    }

    [Fact]
    public void AppendClonedBlocksFrom_LeavesSourceUnchanged()
    {
        var source = new ScribeDocument { Title = "Source" };
        source.AddTask("keep me");
        var sourceDocId = source.DocId;
        var sourceTaskId = source.Blocks[0].TaskId;

        var target = new ScribeDocument();
        target.AppendClonedBlocksFrom(source);

        // Editing the appended copy in the target must not reach back into the source.
        target.SetBlockText(0, "changed on the target");

        Assert.Equal(sourceDocId, source.DocId);
        var block = Assert.Single(source.Blocks);
        Assert.Equal("keep me", block.Text);
        Assert.Equal(sourceTaskId, block.TaskId);
    }

    [Fact]
    public void AppendClonedBlocksFrom_OntoEmptyTarget_CopiesContentWithFreshIds()
    {
        var target = new ScribeDocument();
        var source = new ScribeDocument();
        source.AddTask("only task");

        target.AppendClonedBlocksFrom(source);

        var block = Assert.Single(target.Blocks);
        Assert.Equal("only task", block.Text);
        Assert.NotEqual(source.Blocks[0].TaskId, block.TaskId);
    }
}
