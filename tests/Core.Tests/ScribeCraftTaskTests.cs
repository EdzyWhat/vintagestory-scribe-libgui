using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the Craft task kind (add-crafting-tasks): the recipe-bound composite generator's model
// fields, its persistence across all three codecs, the one-level subtask depth, the batch math, and the
// loose self-healing ingredient reconciliation. All pure-Core (no game install).
public class ScribeCraftTaskTests
{
    // ---- model fields + clamps ----

    [Fact]
    public void Craft_CarriesOutputTargetAndRecipeBinding()
    {
        var doc = new ScribeDocument();
        var id = doc.AddCraft("game:plank-aged", 16, "game:plank-aged|SBS|3x2");

        var craft = doc.FindByTaskId(id);
        Assert.NotNull(craft);
        Assert.Equal(ScribeBlockKind.Craft, craft!.Kind);
        Assert.Equal("game:plank-aged", craft.TargetItemCode);
        Assert.Equal(16, craft.TargetQuantity);
        Assert.Equal(0, craft.CurrentQuantity);
        Assert.Equal("game:plank-aged|SBS|3x2", craft.RecipeSignature);
        Assert.True(craft.IsCraft);
    }

    [Fact]
    public void Craft_TargetQuantity_ClampsToAtLeastOne()
    {
        var doc = new ScribeDocument();
        var id = doc.AddCraft("game:plank-aged", 0, "sig");
        Assert.Equal(1, doc.FindByTaskId(id)!.TargetQuantity);

        var id2 = doc.AddCraft("game:plank-aged", -5, "sig");
        Assert.Equal(1, doc.FindByTaskId(id2)!.TargetQuantity);
    }

    [Fact]
    public void Depth_ClampsToOneLevel()
    {
        var block = new ScribeBlock(ScribeBlockKind.Tracker, "", depth: 5);
        Assert.Equal(1, block.Depth); // clamped down to the single supported level

        block.Depth = -3;
        Assert.Equal(0, block.Depth);

        block.Depth = 1;
        Assert.Equal(1, block.Depth);
    }

    [Theory]
    [InlineData(ScribeBlockKind.Tracker, true)]
    [InlineData(ScribeBlockKind.Craft, true)]
    [InlineData(ScribeBlockKind.Task, false)]
    [InlineData(ScribeBlockKind.Text, false)]
    [InlineData(ScribeBlockKind.Link, false)]
    public void IsCarriedCountTracked_TrueForTrackerAndCraftOnly(ScribeBlockKind kind, bool expected)
    {
        var block = new ScribeBlock(kind, "");
        Assert.Equal(expected, block.IsCarriedCountTracked);
    }

    // ---- batch math ----

    [Theory]
    [InlineData(16, 4, 4)]   // exactly divisible
    [InlineData(5, 4, 2)]    // remainder rounds up
    [InlineData(1, 4, 1)]    // target below output-per-craft still needs one craft
    [InlineData(8, 1, 8)]    // one output per craft
    [InlineData(0, 4, 1)]    // non-positive target clamps to 1 → 1 craft
    [InlineData(10, 0, 10)]  // non-positive output-per-craft clamps to 1
    public void CraftsNeeded_CeilBoundaries(int target, int perCraft, int expected)
    {
        Assert.Equal(expected, ScribeCraftMath.CraftsNeeded(target, perCraft));
    }

    // ---- reconcile: generate + self-heal ----

    [Fact]
    public void Reconcile_GeneratesOneTrackerChildPerIngredientAtBatchQuantity()
    {
        var doc = new ScribeDocument();
        // 16 target, yields 4 per craft → craftsNeeded 4.
        var id = doc.AddCraft("game:plank-aged", 16, "sig");
        int crafts = ScribeCraftMath.CraftsNeeded(16, 4);

        var ok = doc.ReconcileCraftIngredients(id,
            new[]
            {
                new ScribeCraftIngredient("game:log-oak", 2),
                new ScribeCraftIngredient("game:resin", 1),
            },
            notes: Array.Empty<string>(),
            craftsNeeded: crafts);

        Assert.True(ok);
        Assert.Equal(3, doc.Blocks.Count); // parent + 2 children
        var oak = doc.Blocks[1];
        var resin = doc.Blocks[2];
        Assert.Equal(ScribeBlockKind.Tracker, oak.Kind);
        Assert.Equal(1, oak.Depth);
        Assert.Equal("game:log-oak", oak.TargetItemCode);
        Assert.Equal(8, oak.TargetQuantity);  // 2 × 4
        Assert.Equal(4, resin.TargetQuantity); // 1 × 4
        Assert.Equal(1, resin.Depth);
    }

    [Fact]
    public void Reconcile_RescalesExistingChildrenInPlace_PreservingProgressAndId()
    {
        var doc = new ScribeDocument();
        var id = doc.AddCraft("game:plank-aged", 4, "sig"); // yields 4 → 1 craft
        doc.ReconcileCraftIngredients(id,
            new[] { new ScribeCraftIngredient("game:log-oak", 2) },
            Array.Empty<string>(), ScribeCraftMath.CraftsNeeded(4, 4));

        var childId = doc.Blocks[1].TaskId;
        doc.SetTrackerCurrentQuantity(childId, 3); // simulate carried progress

        // Player raises the target so crafts double (8 target, yields 4 → 2 crafts).
        doc.Blocks[0].TargetQuantity = 8;
        doc.ReconcileCraftIngredients(id,
            new[] { new ScribeCraftIngredient("game:log-oak", 2) },
            Array.Empty<string>(), ScribeCraftMath.CraftsNeeded(8, 4));

        Assert.Equal(2, doc.Blocks.Count); // still just parent + the one child (no duplicate)
        Assert.Equal(childId, doc.Blocks[1].TaskId);       // same row, not recreated
        Assert.Equal(4, doc.Blocks[1].TargetQuantity);     // 2 per-craft × 2 crafts, rescaled in place
        Assert.Equal(3, doc.Blocks[1].CurrentQuantity);    // live progress preserved
    }

    [Fact]
    public void Reconcile_RecreatesDeletedChild_LeavesOthersAndNeverDeletes()
    {
        var doc = new ScribeDocument();
        var id = doc.AddCraft("game:plank-aged", 4, "sig");
        var ingredients = new[]
        {
            new ScribeCraftIngredient("game:log-oak", 2),
            new ScribeCraftIngredient("game:resin", 1),
        };
        doc.ReconcileCraftIngredients(id, ingredients, Array.Empty<string>(), 1);
        Assert.Equal(3, doc.Blocks.Count);

        // Player deletes the oak child (index 1).
        doc.DeleteBlock(1, out _);
        Assert.Equal(2, doc.Blocks.Count);

        // Re-edit target → reconcile recreates the missing oak child, leaves resin alone.
        doc.ReconcileCraftIngredients(id, ingredients, Array.Empty<string>(), 1);
        Assert.Equal(3, doc.Blocks.Count);
        Assert.Contains(doc.Blocks, b => b.IsTracker && b.TargetItemCode == "game:log-oak");
        Assert.Single(doc.Blocks, b => b.IsTracker && b.TargetItemCode == "game:resin"); // not duplicated
    }

    [Fact]
    public void Reconcile_PreservesPlayerAddedRowsInTheRun_AndNeverNestsDeeper()
    {
        // Build a Craft parent with an existing oak child and a player-added depth-1 note in the owned run.
        var parent = new ScribeBlock(ScribeBlockKind.Craft, "",
            targetItemCode: "game:plank-aged", targetQuantity: 4, recipeSignature: "sig");
        var doc = new ScribeDocument();
        doc.ReplaceBlocks(new[]
        {
            parent,
            new ScribeBlock(ScribeBlockKind.Tracker, "", depth: 1, targetItemCode: "game:log-oak", targetQuantity: 2),
            new ScribeBlock(ScribeBlockKind.Text, "buy an axe first", depth: 1),
        });
        int before = doc.Blocks.Count;

        doc.ReconcileCraftIngredients(parent.TaskId,
            new[] { new ScribeCraftIngredient("game:log-oak", 2) }, Array.Empty<string>(), 1);

        Assert.Equal(before, doc.Blocks.Count); // nothing deleted, nothing duplicated
        Assert.Contains(doc.Blocks, b => b.Kind == ScribeBlockKind.Text && b.Text == "buy an axe first");
        Assert.All(doc.Blocks, b => Assert.True(b.Depth <= 1)); // never a depth-2 row
    }

    [Fact]
    public void Reconcile_LiquidNoteBecomesNonCountingTextRow_NotADuplicate()
    {
        var doc = new ScribeDocument();
        var id = doc.AddCraft("game:poultice-linen-honey-sulfur", 4, "sig");
        var notes = new[] { "Requires 0.25 L honey" };

        doc.ReconcileCraftIngredients(id,
            new[] { new ScribeCraftIngredient("game:linen-*", 1) }, notes, 1);
        doc.ReconcileCraftIngredients(id, // second pass must not re-add the note
            new[] { new ScribeCraftIngredient("game:linen-*", 1) }, notes, 1);

        var noteRows = doc.Blocks.Where(b => b.Kind == ScribeBlockKind.Text && b.Text == "Requires 0.25 L honey").ToList();
        Assert.Single(noteRows);
        Assert.Equal(1, noteRows[0].Depth);
    }

    [Fact]
    public void Reconcile_ReturnsFalse_WhenNoCraftWithThatId()
    {
        var doc = new ScribeDocument();
        doc.AddTask("not a craft");
        var ok = doc.ReconcileCraftIngredients(Guid.NewGuid(),
            new[] { new ScribeCraftIngredient("game:log-oak", 2) }, Array.Empty<string>(), 1);
        Assert.False(ok);
    }

    // ---- codec round-trips ----

    [Fact]
    public void BinaryCodec_RoundTripsCraftWithRecipeSignatureAndDepth()
    {
        var original = new ScribeDocument();
        var id = original.AddCraft("game:plank-aged", 16, "game:plank-aged|SBS|3x2");
        original.SetTrackerCurrentQuantity(id, 5);
        original.ReconcileCraftIngredients(id,
            new[] { new ScribeCraftIngredient("game:log-oak", 2) }, Array.Empty<string>(), 4);

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out var restored);

        Assert.True(ok);
        var craft = restored!.Blocks[0];
        Assert.Equal(ScribeBlockKind.Craft, craft.Kind);
        Assert.Equal("game:plank-aged", craft.TargetItemCode);
        Assert.Equal(16, craft.TargetQuantity);
        Assert.Equal(5, craft.CurrentQuantity);
        Assert.Equal("game:plank-aged|SBS|3x2", craft.RecipeSignature);

        var child = restored.Blocks[1];
        Assert.Equal(ScribeBlockKind.Tracker, child.Kind);
        Assert.Equal(1, child.Depth);          // subtask depth survives
        Assert.Equal("", child.RecipeSignature); // non-Craft reads empty
    }

    [Fact]
    public void JsonCodec_RoundTripsCraftBinding()
    {
        var original = new ScribeDocument();
        original.AddCraft("game:plank-aged", 12, "game:plank-aged|SBS|3x2");

        string json = ScribeDocumentJsonCodec.Serialize(original);
        bool ok = ScribeDocumentJsonCodec.TryDeserialize(json, out var restored);

        Assert.True(ok);
        var craft = restored!.Blocks[0];
        Assert.Equal(ScribeBlockKind.Craft, craft.Kind);
        Assert.Equal("game:plank-aged", craft.TargetItemCode);
        Assert.Equal(12, craft.TargetQuantity);
        Assert.Equal("game:plank-aged|SBS|3x2", craft.RecipeSignature);
    }

    [Fact]
    public void TsvCodec_RoundTripsCraftBinding_WithAndWithoutSignature()
    {
        var original = new ScribeDocument();
        original.AddCraft("game:plank-aged", 12, "game:plank-aged|SBS|3x2"); // with signature
        original.AddCraft("game:something", 3, "");                          // no signature

        string tsv = ScribeDocumentTsvCodec.Serialize(original);
        bool ok = ScribeDocumentTsvCodec.TryDeserialize(tsv, out var restored);

        Assert.True(ok);
        var withSig = restored!.Blocks[0];
        Assert.Equal(ScribeBlockKind.Craft, withSig.Kind);
        Assert.Equal("game:plank-aged", withSig.TargetItemCode);
        Assert.Equal(12, withSig.TargetQuantity);
        Assert.Equal("game:plank-aged|SBS|3x2", withSig.RecipeSignature);

        var noSig = restored.Blocks[1];
        Assert.Equal(ScribeBlockKind.Craft, noSig.Kind);
        Assert.Equal("game:something", noSig.TargetItemCode);
        Assert.Equal(3, noSig.TargetQuantity);
        Assert.Equal("", noSig.RecipeSignature);
    }

    [Fact]
    public void TsvCodec_ParsesCraftTokenAndDepth()
    {
        // A hand-authored table (loose import): craft row with a signature in Special and a depth-1 child.
        string tsv =
            "Type\tDone\tText\tSpecial\tCount\tDepth\n" +
            "craft\t\t\tgame:plank-aged,game:plank-aged|SBS|3x2\t8\t0\n" +
            "tracker\t\t\tgame:log-oak\t16\t1\n";

        bool ok = ScribeDocumentTsvCodec.TryDeserialize(tsv, out var doc);

        Assert.True(ok);
        Assert.Equal(2, doc!.Blocks.Count);
        Assert.Equal(ScribeBlockKind.Craft, doc.Blocks[0].Kind);
        Assert.Equal("game:plank-aged", doc.Blocks[0].TargetItemCode);
        Assert.Equal("game:plank-aged|SBS|3x2", doc.Blocks[0].RecipeSignature);
        Assert.Equal(8, doc.Blocks[0].TargetQuantity);
        Assert.Equal(1, doc.Blocks[1].Depth);
    }
}
