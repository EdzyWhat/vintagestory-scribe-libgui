using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Integration.Tests;

/// <summary>
/// Regression coverage for a race that lost a clay-tablet task when the interface was closed and
/// re-opened while the task stayed pinned. Two server paths stamp an uncoordinated <c>DocId</c> onto a
/// documentless stack: <see cref="NotebookHost"/>'s constructor, and <c>RecordPickedUpIfNew</c>'s history
/// flush. Both run from the periodic <c>OnHistoryScanTick</c> sweep, racing the client's own minted DocId;
/// the loser's saves are then silently refused by <see cref="ScribeModSystem.OnServerReceivedNotebookSave"/>'s
/// DocId-mismatch guard.
/// </summary>
public class TabletNotebookDocIdStampRaceScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    private async Task<(ITestPlayer player, ItemSlot slot)> SeedDocumentlessItem(string playerName, string itemCode)
    {
        var player = await World.JoinPlayer(playerName);
        var hotbar = player.Player.InventoryManager.GetHotbarInventory();
        hotbar[0]!.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", itemCode))!, 1);
        hotbar[0]!.MarkDirty();
        return (player, hotbar[0]!);
    }

    private void FlushNotebookSave(ITestPlayer player, ItemSlot slot, ScribeDocument doc)
    {
        Mod.OnServerReceivedNotebookSave(player.Player, new ScribeNotebookSaveMessage
        {
            DocIdBytes = doc.DocId.ToByteArray(),
            DocumentBytes = ScribeDocumentCodec.Serialize(doc),
            TargetInventoryId = slot.Inventory.InventoryID,
            TargetSlotId = 0,
        });
    }

    // ── constructing a host over a documentless stack writes nothing ──

    [AtlasScenario(RollbackWorld = true)]
    public async Task Constructing_a_notebook_host_over_a_documentless_stack_writes_nothing()
    {
        var (_, slot) = await SeedDocumentlessItem("CtorNoStamp1", "scribenotebook");
        _ = new NotebookHost(slot);
        Assert.False(ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out _));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Constructing_a_tablet_host_over_a_documentless_stack_writes_nothing()
    {
        var (_, slot) = await SeedDocumentlessItem("CtorNoStamp2", "scribetablet-clay-red");
        _ = new TabletHost(slot);
        Assert.False(ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out _));
    }

    // ── the ambient sweep records PickedUp normally, without stamping a document ──

    [AtlasScenario(RollbackWorld = true)]
    public async Task Ambient_sweep_records_pickedup_on_a_documentless_notebook_without_stamping_it()
    {
        var (player, slot) = await SeedDocumentlessItem("SweepNoStamp1", "scribenotebook");

        Mod.OnHistoryScanTick(0f);

        // The sweep still visits and records on this documentless stack (it isn't guarded)...
        var history = HistoryStore.Deserialize(slot.Itemstack!.Attributes.GetBytes("scribeHistory"));
        Assert.Contains(history.Entries, e => e.Kind == HistoryEventKind.PickedUp && e.ActorName == player.Player.PlayerName);
        // ...but recording that entry did not stamp a document as a side effect.
        Assert.False(ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out _));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Ambient_sweep_records_pickedup_on_a_documentless_tablet_without_stamping_it()
    {
        var (player, slot) = await SeedDocumentlessItem("SweepNoStamp2", "scribetablet-clay-red");

        Mod.OnHistoryScanTick(0f);

        var history = HistoryStore.Deserialize(slot.Itemstack!.Attributes.GetBytes("scribeHistory"));
        Assert.Contains(history.Entries, e => e.Kind == HistoryEventKind.PickedUp && e.ActorName == player.Player.PlayerName);
        Assert.False(ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out _));
    }

    // ── the end-to-end race: a save after the ambient sweep is accepted ──

    [AtlasScenario(RollbackWorld = true)]
    public async Task Notebook_save_after_the_ambient_sweep_races_it_and_is_accepted()
    {
        var (player, slot) = await SeedDocumentlessItem("RaceFixed1", "scribenotebook");

        // Confirmed sequence from the reporter's log: the player opens a brand-new documentless notebook
        // (NotebookHost writes nothing), the ambient sweep records PickedUp (history only), and only THEN
        // the player's client flushes its first edit under a DocId it minted when it opened.
        var clientDoc = new ScribeDocument();
        clientDoc.AddTask("Find copper");
        var clientDocId = clientDoc.DocId;

        Mod.OnHistoryScanTick(0f);
        FlushNotebookSave(player, slot, clientDoc);

        // Nothing has stamped a different DocId onto the stack, so the save is not refused for a
        // DocId mismatch and lands.
        Assert.True(ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out var onStack));
        Assert.Equal(clientDocId, onStack!.DocId);
        Assert.Contains(onStack.Blocks, b => b.Text == "Find copper");
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Tablet_save_after_the_ambient_sweep_races_it_and_is_accepted()
    {
        var (player, slot) = await SeedDocumentlessItem("RaceFixed2", "scribetablet-clay-red");

        var clientDoc = new ScribeDocument();
        clientDoc.AddTask("Find copper");
        var clientDocId = clientDoc.DocId;

        Mod.OnHistoryScanTick(0f);
        FlushNotebookSave(player, slot, clientDoc);

        Assert.True(ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out var onStack));
        Assert.Equal(clientDocId, onStack!.DocId);
        Assert.Contains(onStack.Blocks, b => b.Text == "Find copper");
    }
}
