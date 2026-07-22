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
    public void AddTask_RejectsBlankText(string blank)
    {
        var doc = new ScribeDocument();

        Assert.False(doc.AddTask(blank));
        Assert.Empty(doc.Blocks);
    }

    [Fact]
    public void AddTask_TrimsSurroundingWhitespace()
    {
        var doc = new ScribeDocument();

        doc.AddTask("  Find copper  ");

        Assert.Equal("Find copper", doc.Blocks[0].Text);
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
    public void SetBlockText_RejectsBlankForTask(string blank)
    {
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");

        Assert.False(doc.SetBlockText(0, blank));
        Assert.Equal("Find copper", doc.Blocks[0].Text);
    }

    [Fact]
    public void SetBlockText_TrimsTaskByDefault()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");

        doc.SetBlockText(0, "  Find tin\n");

        // Default (committed edit) trims surrounding whitespace incl. a trailing newline.
        Assert.Equal("Find tin", doc.Blocks[0].Text);
    }

    [Fact]
    public void SetBlockText_PreservesTrailingNewlineWhenNotTrimming()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");

        // The live in-place editor passes trimTask: false so a just-typed trailing newline
        // (Shift+Enter at line end) survives long enough for the row to grow (task 4.6).
        bool ok = doc.SetBlockText(0, "Find tin\n", trimTask: false);

        Assert.True(ok);
        Assert.Equal("Find tin\n", doc.Blocks[0].Text);
    }

    [Fact]
    public void SetBlockText_StillRejectsWhitespaceOnlyTask_WhenNotTrimming()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");

        // The blank/whitespace-only guard holds regardless of trimTask -- a task can't become
        // all-blank even on the live edit path.
        Assert.False(doc.SetBlockText(0, "\n", trimTask: false));
        Assert.Equal("Find copper", doc.Blocks[0].Text);
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

    // --- Toggle pinned (tasks only) ---

    [Fact]
    public void TogglePinned_UnpinnedBecomesPinned()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Build a forge");

        Assert.True(doc.TogglePinned(0));
        Assert.True(doc.Blocks[0].Pinned);
    }

    [Fact]
    public void TogglePinned_PinnedBecomesUnpinned()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Build a forge");
        doc.TogglePinned(0);

        doc.TogglePinned(0);

        Assert.False(doc.Blocks[0].Pinned);
    }

    [Fact]
    public void TogglePinned_OnTextSection_Fails()
    {
        var doc = new ScribeDocument();
        doc.AddTextSection("not a task");

        Assert.False(doc.TogglePinned(0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    public void TogglePinned_OnInvalidIndex_FailsSafely(int badIndex)
    {
        var doc = new ScribeDocument();

        Assert.False(doc.TogglePinned(badIndex));
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
