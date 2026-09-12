using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// add-external-mod-task-api tasks.md §5/§6: end-to-end coverage of the public
/// <see cref="ScribeModSystem.TryCreateExternalTask"/> entry point — the title/bodyText/extraInfo mapping,
/// the silent last-opened-or-first-writeable target resolution, and the three failure reasons (no Scribe
/// item, all locked, target full), against a real player and real carried items.
/// </summary>
public class ExternalTaskApiScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    private async Task<(ITestPlayer player, ItemSlot slot)> SeedCarriedItem(string playerName, string itemCode, int hotbarIndex = 0)
    {
        var player = await World.JoinPlayer(playerName);
        var hotbar = player.Player.InventoryManager.GetHotbarInventory();
        hotbar[hotbarIndex]!.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", itemCode))!, 1);
        hotbar[hotbarIndex]!.MarkDirty();
        return (player, hotbar[hotbarIndex]!);
    }

    private static ScribeDocument ReadDoc(ItemSlot slot)
    {
        Assert.True(ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out var doc));
        return doc!;
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task TitleBodyAndExtraInfo_CreatesTaskWithNestedNoteAndExtraInfoOnTheTitle()
    {
        var (player, slot) = await SeedCarriedItem("ExtTaskFull1", "scribenotebook");

        bool ok = Mod.TryCreateExternalTask(player.Player, "Look at the notice board", "Bring 3 logs", "posted by NoticeBoard");

        Assert.True(ok);
        var doc = ReadDoc(slot);
        Assert.Equal(2, doc.Blocks.Count);
        Assert.Equal(ScribeBlockKind.Task, doc.Blocks[0].Kind);
        Assert.Equal("Look at the notice board", doc.Blocks[0].Text);
        Assert.Equal("posted by NoticeBoard", doc.Blocks[0].ExtraInfo);
        Assert.Equal(ScribeBlockKind.Text, doc.Blocks[1].Kind);
        Assert.Equal("Bring 3 logs", doc.Blocks[1].Text);
        Assert.Equal(1, doc.Blocks[1].Depth);
        Assert.Null(doc.Blocks[1].ExtraInfo);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task TitleOnly_CreatesSingleTopLevelTask()
    {
        var (player, slot) = await SeedCarriedItem("ExtTaskFull2", "scribenotebook");

        bool ok = Mod.TryCreateExternalTask(player.Player, "Look at the notice board", null, null);

        Assert.True(ok);
        var doc = ReadDoc(slot);
        Assert.Single(doc.Blocks);
        Assert.Equal(ScribeBlockKind.Task, doc.Blocks[0].Kind);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task BodyOnly_NoTitle_PromotesToStandaloneTopLevelNote()
    {
        var (player, slot) = await SeedCarriedItem("ExtTaskFull3", "scribenotebook");

        bool ok = Mod.TryCreateExternalTask(player.Player, null, "Bring 3 logs", "from NoticeBoard");

        Assert.True(ok);
        var doc = ReadDoc(slot);
        Assert.Single(doc.Blocks);
        Assert.Equal(ScribeBlockKind.Text, doc.Blocks[0].Kind);
        Assert.Equal(0, doc.Blocks[0].Depth);
        Assert.Equal("from NoticeBoard", doc.Blocks[0].ExtraInfo);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task BothTitleAndBodyEmpty_ReturnsTrueAndCreatesNothing()
    {
        var (player, slot) = await SeedCarriedItem("ExtTaskFull4", "scribenotebook");

        bool ok = Mod.TryCreateExternalTask(player.Player, null, null, null);

        Assert.True(ok);
        Assert.False(ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out _));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task LastOpenedItem_PreferredWhenCarriedAndWriteable()
    {
        var player = await World.JoinPlayer("ExtTaskLastOpen1");
        var hotbar = player.Player.InventoryManager.GetHotbarInventory();
        hotbar[0]!.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", "scribenotebook"))!, 1);
        hotbar[0]!.MarkDirty();
        hotbar[1]!.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", "scribenotebook"))!, 1);
        hotbar[1]!.MarkDirty();

        // Both books are documentless so far; write a seed document onto the SECOND one and mark it
        // as the tracked last-opened item, so resolution must skip the first (default) writeable slot.
        var secondDoc = new ScribeDocument();
        ScribeDocumentAttributes.WriteTo(hotbar[1]!.Itemstack!, secondDoc);
        hotbar[1]!.MarkDirty();
        Mod.NoteServerScribeItemOpened(player.Player.PlayerUID, secondDoc.DocId);

        bool ok = Mod.TryCreateExternalTask(player.Player, "Look at the notice board", null, null);

        Assert.True(ok);
        Assert.False(ScribeDocumentAttributes.TryReadFrom(hotbar[0]!.Itemstack!, out _));
        var doc = ReadDoc(hotbar[1]!);
        Assert.Single(doc.Blocks);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task LastOpenedItem_NotCarried_FallsBackToFirstWriteable()
    {
        var (player, slot) = await SeedCarriedItem("ExtTaskLastOpen2", "scribenotebook");

        // Track a DocId that belongs to no carried item at all.
        Mod.NoteServerScribeItemOpened(player.Player.PlayerUID, Guid.NewGuid());

        bool ok = Mod.TryCreateExternalTask(player.Player, "Look at the notice board", null, null);

        Assert.True(ok);
        var doc = ReadDoc(slot);
        Assert.Single(doc.Blocks);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task OnlyLockedItemsCarried_FailsAndCreatesNoTask()
    {
        // A hardened tablet: IsSlotWriteable is false, so this is the "all locked" resolution path.
        var (player, slot) = await SeedCarriedItem("ExtTaskLocked1", "scribetablet-clay-red-hard");

        bool ok = Mod.TryCreateExternalTask(player.Player, "Look at the notice board", null, null);

        Assert.False(ok);
        Assert.False(ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out _));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task NoScribeItemCarried_FailsAndCreatesNoTask()
    {
        var player = await World.JoinPlayer("ExtTaskNoItem1");
        var hotbar = player.Player.InventoryManager.GetHotbarInventory();
        hotbar[0]!.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("game", "stick"))!, 1);
        hotbar[0]!.MarkDirty();

        bool ok = Mod.TryCreateExternalTask(player.Player, "Look at the notice board", null, null);

        Assert.False(ok);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task TargetDocumentFull_FailsAndDoesNotOverflow()
    {
        // A wet clay tablet reports ScribeDocumentPolicy.Tablet (MaxBlocks = 15). Seed it already at
        // the cap so the external add has no room, and confirm it's refused rather than silently
        // overflowing past the cap.
        var (player, slot) = await SeedCarriedItem("ExtTaskFullCap1", "scribetablet-clay-red");

        var seeded = new ScribeDocument();
        for (int i = 0; i < ScribeDocumentPolicy.StandardMaxBlocks; i++)
            seeded.AddTask($"Task {i}");
        ScribeDocumentAttributes.WriteTo(slot.Itemstack!, seeded);
        slot.MarkDirty();

        bool ok = Mod.TryCreateExternalTask(player.Player, "One task too many", null, null);

        Assert.False(ok);
        var doc = ReadDoc(slot);
        Assert.Equal(ScribeDocumentPolicy.StandardMaxBlocks, doc.Blocks.Count);
        Assert.DoesNotContain(doc.Blocks, b => b.Text == "One task too many");
    }
}
