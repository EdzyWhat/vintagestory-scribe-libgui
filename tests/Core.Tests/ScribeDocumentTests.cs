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
}
