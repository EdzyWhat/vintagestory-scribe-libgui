using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the human-readable JSON export/import lane (the lossless clipboard codec).
// Mirrors the WHEN/THEN scenarios in the scriptorium-import-export spec.
public class ScribeDocumentJsonCodecTests
{
    private static ScribeDocument SampleDocument()
    {
        var doc = new ScribeDocument { Title = "My List" };
        doc.AddTask("Chop wood");
        doc.AddTextSection("Tin is rarer than copper.");
        doc.AddTracker("game:ingot-copper", 8);
        doc.AddLink("game:pickaxe-copper");
        doc.AddGuideLink("craftinginfo-knapping", "Knapping");
        doc.AddQuestLink("vsquest:quest-freeghost", "Free the Ghost", "Plant 8 flowers nearby.");
        doc.ToggleTask(0); // "Chop wood" done
        return doc;
    }

    [Fact]
    public void RoundTrip_PreservesKindsTextDoneDepthAndReferences()
    {
        var original = SampleDocument();

        string json = ScribeDocumentJsonCodec.Serialize(original);
        bool ok = ScribeDocumentJsonCodec.TryDeserialize(json, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal("My List", restored!.Title);
        Assert.Equal(
            original.Blocks.Select(b => (b.Kind, b.Text, b.Done, b.Depth)),
            restored.Blocks.Select(b => (b.Kind, b.Text, b.Done, b.Depth)));

        var tracker = restored.Blocks[2];
        Assert.Equal("game:ingot-copper", tracker.TargetItemCode);
        Assert.Equal(8, tracker.TargetQuantity);

        var itemLink = restored.Blocks[3];
        Assert.Equal("game:pickaxe-copper", itemLink.LinkTarget);
        Assert.Null(itemLink.LinkLabel);

        var guideLink = restored.Blocks[4];
        Assert.Equal("page:craftinginfo-knapping", guideLink.LinkTarget);
        Assert.Equal("Knapping", guideLink.LinkLabel);
        Assert.Null(guideLink.LinkDescription);

        var questLink = restored.Blocks[5];
        Assert.Equal("quest:vsquest:quest-freeghost", questLink.LinkTarget);
        Assert.Equal("Free the Ghost", questLink.LinkLabel);
        Assert.Equal("Plant 8 flowers nearby.", questLink.LinkDescription);
    }

    [Fact]
    public void Serialize_OmitsIdentityAssignmentAndLiveCount()
    {
        var doc = SampleDocument();
        doc.SetTrackerCurrentQuantity(doc.Blocks[2].TaskId, 3); // live count that must NOT be exported
        doc.Blocks[0].AssignedToUid = "player-1234";

        string json = ScribeDocumentJsonCodec.Serialize(doc);

        Assert.DoesNotContain("taskId", json, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docId", json, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("assignedToUid", json, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("currentQuantity", json, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_MintsFreshTaskIds_NeverCarriesIdentity()
    {
        var original = SampleDocument();
        string json = ScribeDocumentJsonCodec.Serialize(original);

        ScribeDocumentJsonCodec.TryDeserialize(json, out var a);
        ScribeDocumentJsonCodec.TryDeserialize(json, out var b);

        // Two imports of the same payload share no task identity (so an import can never resurrect a pin).
        var idsA = a!.Blocks.Select(x => x.TaskId).ToHashSet();
        foreach (var block in b!.Blocks)
            Assert.DoesNotContain(block.TaskId, idsA);
        Assert.NotEqual(a.DocId, b.DocId);
    }

    [Fact]
    public void Deserialize_ToleratesNewerVersionAndIgnoresUnknownFields()
    {
        string json = """
        { "v": 99, "title": "Future", "blocks": [
            { "kind": "task", "text": "still works", "done": true, "depth": 0, "somethingNew": 42 }
        ] }
        """;

        bool ok = ScribeDocumentJsonCodec.TryDeserialize(json, out var doc);

        Assert.True(ok);
        Assert.Equal("Future", doc!.Title);
        Assert.Single(doc.Blocks);
        Assert.Equal("still works", doc.Blocks[0].Text);
        Assert.True(doc.Blocks[0].Done);
    }

    [Fact]
    public void Deserialize_UnknownKind_DegradesToTask()
    {
        string json = """{ "v": 1, "blocks": [ { "kind": "map", "text": "somewhere" } ] }""";

        bool ok = ScribeDocumentJsonCodec.TryDeserialize(json, out var doc);

        Assert.True(ok);
        Assert.Equal(ScribeBlockKind.Task, doc!.Blocks[0].Kind);
        Assert.Equal("somewhere", doc.Blocks[0].Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ not: valid")]
    [InlineData("{ \"title\": \"no version marker\", \"blocks\": [] }")] // foreign JSON: no "v" → rejected
    public void Deserialize_MalformedOrForeign_ReturnsFalse(string? json)
    {
        bool ok = ScribeDocumentJsonCodec.TryDeserialize(json, out var doc);

        Assert.False(ok);
        Assert.Null(doc);
    }

    [Fact]
    public void Deserialize_ClipsOverlongTextToCaps()
    {
        string longTask = new string('a', ScribeDocumentCodec.MaxTaskTextLength + 50);
        string longNote = new string('b', ScribeDocumentCodec.MaxTextLength + 50);
        string json = $$"""
        { "v": 1, "blocks": [
            { "kind": "task", "text": "{{longTask}}" },
            { "kind": "note", "text": "{{longNote}}" }
        ] }
        """;

        ScribeDocumentJsonCodec.TryDeserialize(json, out var doc);

        Assert.Equal(ScribeDocumentCodec.MaxTaskTextLength, doc!.Blocks[0].Text.Length);
        Assert.Equal(ScribeDocumentCodec.MaxTextLength, doc.Blocks[1].Text.Length);
    }

    [Fact]
    public void Deserialize_EnforcesBlockCap()
    {
        var blocks = string.Join(",", Enumerable.Range(0, ScribeDocumentCodec.MaxBlocks + 25)
            .Select(_ => "{ \"kind\": \"task\", \"text\": \"x\" }"));
        string json = $"{{ \"v\": 1, \"blocks\": [ {blocks} ] }}";

        ScribeDocumentJsonCodec.TryDeserialize(json, out var doc);

        Assert.Equal(ScribeDocumentCodec.MaxBlocks, doc!.Blocks.Count);
    }

    [Fact]
    public void Deserialize_BlankTitle_DefaultsToUntitled()
    {
        string json = """{ "v": 1, "title": "   ", "blocks": [] }""";

        ScribeDocumentJsonCodec.TryDeserialize(json, out var doc);

        Assert.Equal(ScribeDocument.DefaultTitle, doc!.Title);
    }
}
