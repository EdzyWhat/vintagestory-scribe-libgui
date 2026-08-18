using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the fixed-column TSV export/import lane. Mirrors the WHEN/THEN scenarios in the
// scriptorium-import-export spec: fixed columns, title-as-row, integer Depth, comma-packed Special,
// row-position sequence, loose/tolerant import, and escaping.
public class ScribeDocumentTsvCodecTests
{
    private static ScribeDocument SampleDocument()
    {
        var doc = new ScribeDocument { Title = "My List" };
        doc.AddTask("Chop wood");
        doc.AddTextSection("A free note.");
        doc.AddTracker("game:ingot-copper", 8);
        doc.AddLink("game:pickaxe-copper");
        doc.AddGuideLink("craftinginfo-knapping", "Knapping");
        doc.ToggleTask(0); // "Chop wood" done
        return doc;
    }

    [Fact]
    public void RoundTrip_PreservesKindsTextDoneAndReferences()
    {
        var original = SampleDocument();

        string tsv = ScribeDocumentTsvCodec.Serialize(original);
        bool ok = ScribeDocumentTsvCodec.TryDeserialize(tsv, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal("My List", restored!.Title);

        // Row position is the sequence — order is preserved with no order column.
        Assert.Equal(
            original.Blocks.Select(b => (b.Kind, b.Done)),
            restored.Blocks.Select(b => (b.Kind, b.Done)));

        Assert.Equal("Chop wood", restored.Blocks[0].Text);
        Assert.True(restored.Blocks[0].Done);

        var tracker = restored.Blocks[2];
        Assert.Equal(ScribeBlockKind.Tracker, tracker.Kind);
        Assert.Equal("game:ingot-copper", tracker.TargetItemCode);
        Assert.Equal(8, tracker.TargetQuantity);

        var itemLink = restored.Blocks[3];
        Assert.Equal("game:pickaxe-copper", itemLink.LinkTarget);

        var guideLink = restored.Blocks[4];
        Assert.Equal("page:craftinginfo-knapping", guideLink.LinkTarget);
        Assert.Equal("Knapping", guideLink.LinkLabel);
    }

    [Fact]
    public void Serialize_HasFixedHeaderAndLeadingTitleRow()
    {
        var doc = SampleDocument();

        string tsv = ScribeDocumentTsvCodec.Serialize(doc);
        var lines = tsv.Split('\n');

        Assert.Equal("Type\tDone\tText\tSpecial\tCount\tDepth", lines[0]);
        Assert.StartsWith("title\t", lines[1]);
        Assert.Contains("My List", lines[1]);
    }

    [Fact]
    public void Import_TitleRow_SetsTitle_AbsentTitleRow_DefaultsTitle()
    {
        string withTitle = "Type\tDone\tText\tSpecial\tCount\tDepth\ntitle\t\tShared Plan\t\t\t\ntask\t\tDo the thing\t\t\t0\n";
        string noTitle = "Type\tDone\tText\tSpecial\tCount\tDepth\ntask\t\tDo the thing\t\t\t0\n";

        ScribeDocumentTsvCodec.TryDeserialize(withTitle, out var a);
        ScribeDocumentTsvCodec.TryDeserialize(noTitle, out var b);

        Assert.Equal("Shared Plan", a!.Title);
        Assert.Single(a.Blocks); // the title row produced no block
        Assert.Equal(ScribeDocument.DefaultTitle, b!.Title); // Mod apply layer leaves target title unchanged
        Assert.Single(b.Blocks);
    }

    [Fact]
    public void Import_ReadsIntegerDepthColumn()
    {
        string tsv = "Type\tDone\tText\tSpecial\tCount\tDepth\n"
            + "task\t\tCraft an anvil\t\t\t0\n"
            + "tracker\t\t\tgame:ingot-iron\t20\t1\n";

        ScribeDocumentTsvCodec.TryDeserialize(tsv, out var doc);

        Assert.Equal(0, doc!.Blocks[0].Depth);
        Assert.Equal(1, doc.Blocks[1].Depth); // a child under the craft — loose grouping, still a valid tracker
        Assert.Equal(ScribeBlockKind.Tracker, doc.Blocks[1].Kind);
        Assert.Equal(20, doc.Blocks[1].TargetQuantity);
    }

    [Fact]
    public void Import_UnknownKind_DegradesToTask()
    {
        string tsv = "Type\tDone\tText\tSpecial\tCount\tDepth\nmap\t\tGo here\t100,64,-200,star,red\t\t0\n";

        ScribeDocumentTsvCodec.TryDeserialize(tsv, out var doc);

        Assert.Single(doc!.Blocks);
        Assert.Equal(ScribeBlockKind.Task, doc.Blocks[0].Kind); // degrade, don't reject (loose tenet)
        Assert.Equal("Go here", doc.Blocks[0].Text);
    }

    [Fact]
    public void Import_ToleratesUnknownTrailingColumnsAndReordering()
    {
        // Header in a different order, with an extra unknown trailing column.
        string tsv = "Text\tType\tCount\tSpecial\tDone\tDepth\tNotes\n"
            + "Chop wood\ttask\t\t\tx\t0\tignored\n"
            + "\ttracker\t8\tgame:ingot-copper\t\t0\talso ignored\n";

        bool ok = ScribeDocumentTsvCodec.TryDeserialize(tsv, out var doc);

        Assert.True(ok);
        Assert.Equal("Chop wood", doc!.Blocks[0].Text);
        Assert.True(doc.Blocks[0].Done);
        Assert.Equal(ScribeBlockKind.Tracker, doc.Blocks[1].Kind);
        Assert.Equal("game:ingot-copper", doc.Blocks[1].TargetItemCode);
        Assert.Equal(8, doc.Blocks[1].TargetQuantity);
    }

    [Fact]
    public void Import_MissingColumns_Default()
    {
        // Only Type and Text present; Done/Special/Count/Depth all missing → defaults.
        string tsv = "Type\tText\ntask\tSolo task\n";

        ScribeDocumentTsvCodec.TryDeserialize(tsv, out var doc);

        var block = doc!.Blocks[0];
        Assert.Equal("Solo task", block.Text);
        Assert.False(block.Done);
        Assert.Equal(0, block.Depth);
    }

    [Fact]
    public void Import_NoTypeColumn_ReturnsFalse()
    {
        // Random tab text with no recognizable header is not a Scribe table.
        string tsv = "Name\tAge\nAlice\t30\nBob\t25\n";

        bool ok = ScribeDocumentTsvCodec.TryDeserialize(tsv, out var doc);

        Assert.False(ok);
        Assert.Null(doc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_EmptyInput_ReturnsFalse(string? tsv)
    {
        Assert.False(ScribeDocumentTsvCodec.TryDeserialize(tsv, out _));
    }

    [Fact]
    public void RoundTrip_TextWithTabsNewlinesAndQuotes()
    {
        var doc = new ScribeDocument { Title = "Edge cases" };
        doc.AddTask("has\ttab and\nnewline and \"quotes\"");
        doc.AddTextSection("  leading/trailing spaces  ");

        string tsv = ScribeDocumentTsvCodec.Serialize(doc);
        ScribeDocumentTsvCodec.TryDeserialize(tsv, out var restored);

        Assert.Equal("has\ttab and\nnewline and \"quotes\"", restored!.Blocks[0].Text);
        Assert.Equal("  leading/trailing spaces  ", restored.Blocks[1].Text);
    }

    [Fact]
    public void RoundTrip_FormulaLikeTaskText_ImportsAsLiteral()
    {
        var doc = new ScribeDocument();
        doc.AddTask("=1+1");

        string tsv = ScribeDocumentTsvCodec.Serialize(doc);

        // Exported cell is defanged (won't evaluate in a spreadsheet)...
        Assert.Contains("'=1+1", tsv);
        // ...but re-imports as the original literal text.
        ScribeDocumentTsvCodec.TryDeserialize(tsv, out var restored);
        Assert.Equal("=1+1", restored!.Blocks[0].Text);
    }

    [Fact]
    public void Import_MintsFreshTaskIds()
    {
        var doc = SampleDocument();
        string tsv = ScribeDocumentTsvCodec.Serialize(doc);

        ScribeDocumentTsvCodec.TryDeserialize(tsv, out var a);
        ScribeDocumentTsvCodec.TryDeserialize(tsv, out var b);

        var idsA = a!.Blocks.Select(x => x.TaskId).ToHashSet();
        foreach (var block in b!.Blocks)
            Assert.DoesNotContain(block.TaskId, idsA);
    }

    [Fact]
    public void Import_EnforcesBlockCap()
    {
        var sb = new System.Text.StringBuilder("Type\tDone\tText\tSpecial\tCount\tDepth\n");
        for (int i = 0; i < ScribeDocumentCodec.MaxBlocks + 25; i++)
            sb.Append("task\t\trow\t\t\t0\n");

        ScribeDocumentTsvCodec.TryDeserialize(sb.ToString(), out var doc);

        Assert.Equal(ScribeDocumentCodec.MaxBlocks, doc!.Blocks.Count);
    }
}
