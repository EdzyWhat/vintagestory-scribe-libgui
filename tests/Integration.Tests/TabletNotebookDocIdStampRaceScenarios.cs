using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Integration.Tests;

/// <summary>
/// Regression coverage for a race where a task in the clay tablet is lost when the interface is
/// closed and re-opened while the pinned task remains. Two independent server-side
/// paths stamp a fresh, uncoordinated <c>DocId</c> onto a documentless stack: (1)
/// <see cref="NotebookHost"/>'s constructor writing a placeholder document as a side effect of being
/// constructed, and (2) <c>RecordPickedUpIfNew</c>'s one-time PickedUp-history bookkeeping flushing
/// the document as a side effect of recording history it never touched. Both are reachable from
/// <c>OnHistoryScanTick</c> — a background sweep firing every ~10s for every online player,
/// independent of anything the player is doing — racing the player's OWN client, which mints its own
/// DocId when it opens the same fresh item. Whichever side stamps first "wins"; the loser's client
/// then has every save silently refused by <see cref="ScribeModSystem.OnServerReceivedNotebookSave"/>'s
/// DocId-mismatch guard, with no error surfaced back to the player.
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

        // The sweep still visited and recorded on this stack (proving it isn't skipped just because
        // it has no document; the sweep itself is not guarded)...
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

        // The exact confirmed sequence from the reporter's log: the player opens a brand-new,
        // documentless notebook (constructing a NotebookHost -- this writes nothing),
        // the ambient sweep fires in the same window and records this player's PickedUp entry (this
        // writes only history, not a document either), and only THEN does the
        // player's own client flush its first edit under a DocId it minted itself when it opened.
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
