using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// fix-death-history-inventory-race tasks 3.2, 4.1, 4.2, 5.1-5.4, 1.5: regression coverage for the
/// Death-recording fallback added to <c>ScribeModSystem.History.cs</c> — when a competing mod's own
/// <c>OnEntityDeath</c> handler evicts a carried Notebook before Scribe's live scan runs, the Death
/// entry is queued against that document's own id (via <see cref="ScribeModSystem.PendingHistoryStore"/>)
/// instead of dropped, and flushed by <c>OnHistoryScanTick</c> the next time that document is seen in
/// a live carried slot — in its correct chronological position, per document id, regardless of who
/// ends up carrying it.
///
/// Eviction is simulated the same way a competing mod's corpse-creation would do it: nulling out the
/// live <c>ItemSlot.Itemstack</c> directly (never touching Scribe's own code), while keeping the
/// original <c>ItemStack</c> reference around so a later scenario can "recover" the exact same
/// document by placing it into a (possibly different) slot — proving the fallback is keyed by
/// document id, not by player.
///
/// <c>OnHistoryScanTick</c> and <c>_lastKnownCarriedDocIds</c> are <c>internal</c> (not <c>private</c>)
/// solely so this suite can drive the periodic scan/flush directly rather than waiting out the real
/// 10s interval, and seed a snapshot without waiting out a real tick — see their own doc comments.
/// </summary>
public class HistoryInventoryRaceScenarios : AtlasScenarioBase
{
    private const string NotebookCode = "scribe:scribenotebook";

    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    private static DamageSource EnvironmentalDamage() => new()
    {
        Source = EnumDamageSource.Fall,
        Type   = EnumDamageType.Gravity,
    };

    private static DamageSource PlayerDamage(Vintagestory.API.Common.Entities.Entity killerEntity) => new()
    {
        Source       = EnumDamageSource.Player,
        Type         = EnumDamageType.BluntAttack,
        SourceEntity = killerEntity,
    };

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pending_entry_flushes_when_document_reappears_in_a_live_carried_slot()
    {
        var player = await World.JoinPlayer("FlushOwner");
        await player.GiveItem(NotebookCode);
        var slot = player.Player.InventoryManager.ActiveHotbarSlot;
        var seed = new NotebookHost(slot);
        seed.Flush(); // persist a real document so its DocId is stable across later re-reads (fix-tablet-notebook-docid-stamp-race: construction alone no longer stamps one)
        var docId = seed.Document.DocId;

        var mod = Mod;
        mod.PendingHistoryStore!.Enqueue(docId, new HistoryEntry
        {
            Kind = HistoryEventKind.Death, Detail = "Queued death.", InGameDate = "Year 1, Day 1", InGameTimestamp = 1,
        });

        mod.OnHistoryScanTick(0f);
        await World.Ticks(1);

        var reopened = new NotebookHost(slot);
        Assert.Contains(reopened.History.Entries, e => e.Detail == "Queued death.");
        Assert.Empty(mod.PendingHistoryStore!.TakeAll(docId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Death_with_notebook_evicted_before_the_live_scan_queues_a_fallback_entry()
    {
        var victim = await World.JoinPlayer("EvictedVictim");
        await victim.GiveItem(NotebookCode);
        var slot = victim.Player.InventoryManager.ActiveHotbarSlot;
        var docId = new NotebookHost(slot).Document.DocId;

        var mod = Mod;
        mod._lastKnownCarriedDocIds[victim.Player.PlayerUID] = new HashSet<Guid> { docId };

        // Simulate a competing mod's own OnEntityDeath handler moving the notebook into a corpse
        // moments before Scribe's own handler runs -- never touching Scribe's code to do it.
        slot.Itemstack = null;
        slot.MarkDirty();

        World.Api.Event.TriggerEntityDeath(victim.Entity, EnvironmentalDamage());
        await World.Ticks(1);

        var queued = mod.PendingHistoryStore!.TakeAll(docId);
        var entry = Assert.Single(queued);
        Assert.Equal(HistoryEventKind.Death, entry.Kind);
        Assert.Contains("EvictedVictim", entry.Detail);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pvp_kill_with_killer_notebook_evicted_before_the_live_scan_writes_and_queues_nothing()
    {
        var killer = await World.JoinPlayer("EvictedKiller");
        var victim = await World.JoinPlayer("EvictKillVictim");
        await killer.GiveItem(NotebookCode);
        var killerSlot = killer.Player.InventoryManager.ActiveHotbarSlot;
        var killerDocId = new NotebookHost(killerSlot).Document.DocId;

        var mod = Mod;
        // Seed a "snapshot" for the KILLER too, mirroring the victim-side scenario above -- the point
        // of this test is that it must have no effect. Nothing evicts a killer's inventory during
        // their own kill, so the fallback never extends to their side (see design.md's Non-Goals).
        mod._lastKnownCarriedDocIds[killer.Player.PlayerUID] = new HashSet<Guid> { killerDocId };
        killerSlot.Itemstack = null;
        killerSlot.MarkDirty();

        World.Api.Event.TriggerEntityDeath(victim.Entity, PlayerDamage(killer.Entity));
        await World.Ticks(1);

        Assert.Empty(mod.PendingHistoryStore!.TakeAll(killerDocId));
        Assert.Null(killerSlot.Itemstack);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Queued_entry_never_lands_on_a_different_notebook_the_player_starts_carrying()
    {
        var player = await World.JoinPlayer("WrongNotebookGrd");
        var mod = Mod;
        var docA = Guid.NewGuid();
        mod.PendingHistoryStore!.Enqueue(docA, new HistoryEntry
        {
            Kind = HistoryEventKind.Death, Detail = "For the evicted document.", InGameDate = "d", InGameTimestamp = 1,
        });

        // A brand-new notebook (document B), unrelated to docA, crafted/given AFTER the queuing.
        await player.GiveItem(NotebookCode);
        var slotB = player.Player.InventoryManager.ActiveHotbarSlot;
        var docB = new NotebookHost(slotB).Document.DocId;
        Assert.NotEqual(docA, docB);

        mod.OnHistoryScanTick(0f);
        await World.Ticks(1);

        var reopenedB = new NotebookHost(slotB);
        Assert.DoesNotContain(reopenedB.History.Entries, e => e.Detail == "For the evicted document.");
        Assert.Single(mod.PendingHistoryStore!.TakeAll(docA)); // still queued, unaffected
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Queued_entry_flushes_into_the_document_even_when_a_different_player_now_carries_it()
    {
        var original = await World.JoinPlayer("OriginalHolder");
        await original.GiveItem(NotebookCode);
        var originalSlot = original.Player.InventoryManager.ActiveHotbarSlot;
        var seed = new NotebookHost(originalSlot);
        seed.Flush(); // persist a real document so its DocId is stable across later re-reads (fix-tablet-notebook-docid-stamp-race: construction alone no longer stamps one)
        var docId = seed.Document.DocId;
        var stack = originalSlot.Itemstack!;
        originalSlot.Itemstack = null;
        originalSlot.MarkDirty();

        var mod = Mod;
        mod.PendingHistoryStore!.Enqueue(docId, new HistoryEntry
        {
            Kind = HistoryEventKind.Death, Detail = "Queued for original holder.", InGameDate = "d", InGameTimestamp = 1,
        });

        var newHolder = await World.JoinPlayer("NewHolder");
        var newSlot = newHolder.Player.InventoryManager.ActiveHotbarSlot;
        newSlot.Itemstack = stack;
        newSlot.MarkDirty();

        mod.OnHistoryScanTick(0f);
        await World.Ticks(1);

        var reopened = new NotebookHost(newSlot);
        Assert.Contains(reopened.History.Entries, e => e.Detail == "Queued for original holder.");
        Assert.Empty(mod.PendingHistoryStore!.TakeAll(docId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Ordinary_death_with_notebook_still_writes_immediately_with_no_queuing()
    {
        var victim = await World.JoinPlayer("OrdinaryDeathVic");
        await victim.GiveItem(NotebookCode);
        var slot = victim.Player.InventoryManager.ActiveHotbarSlot;
        var docId = new NotebookHost(slot).Document.DocId;

        World.Api.Event.TriggerEntityDeath(victim.Entity, EnvironmentalDamage());
        await World.Ticks(1);

        var host = new NotebookHost(slot);
        var last = host.History.Entries[^1];
        Assert.Equal(HistoryEventKind.Death, last.Kind);
        Assert.Empty(Mod.PendingHistoryStore!.TakeAll(docId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Flushed_queued_entry_inserts_at_its_correct_chronological_position()
    {
        var victim = await World.JoinPlayer("ChronoVictim");
        await victim.GiveItem(NotebookCode);
        var slot = victim.Player.InventoryManager.ActiveHotbarSlot;
        var host = new NotebookHost(slot);
        host.Flush(); // persist a real document so its DocId is stable across later re-reads (fix-tablet-notebook-docid-stamp-race: construction alone no longer stamps one)
        var docId = host.Document.DocId;

        // Entries recorded (directly, for determinism) both chronologically before and after the
        // real moment the fallback entry below claims for itself.
        host.History.TryAddEntry(new HistoryEntry { Kind = HistoryEventKind.Manual, Detail = "before", InGameDate = "d", InGameTimestamp = 1, EntryId = Guid.NewGuid() });
        host.History.TryAddEntry(new HistoryEntry { Kind = HistoryEventKind.Manual, Detail = "after",  InGameDate = "d", InGameTimestamp = 3, EntryId = Guid.NewGuid() });
        host.FlushHistory();

        var mod = Mod;
        mod.PendingHistoryStore!.Enqueue(docId, new HistoryEntry
        {
            Kind = HistoryEventKind.Death, Detail = "queued death", InGameDate = "d", InGameTimestamp = 2,
        });

        mod.OnHistoryScanTick(0f);
        await World.Ticks(1);

        var reopened = new NotebookHost(slot);
        // Ignore the incidental PickedUp entry OnHistoryScanTick's own FindCarriedNotebooks walk
        // records the first time it attaches server context to this slot -- not what this scenario
        // is about; it always sorts after "after" (its timestamp is "now", the largest of the four).
        var details = reopened.History.Entries
            .Where(e => e.Kind != HistoryEventKind.PickedUp)
            .Select(e => e.Detail).ToList();
        Assert.Equal(new[] { "before", "queued death", "after" }, details);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Two_same_day_deaths_keep_their_real_relative_order()
    {
        var wolf = World.SpawnEntity("game:wolf-eurasian-adult-male", World.Spawn.Offset(3, 0, 0));
        var victim = await World.JoinPlayer("SameDayVictim");
        var killer = await World.JoinPlayer("SameDayKiller");
        await victim.GiveItem(NotebookCode);
        var slot = victim.Player.InventoryManager.ActiveHotbarSlot;

        var creatureDamage = new DamageSource
        {
            Source = EnumDamageSource.Entity, Type = EnumDamageType.BluntAttack, SourceEntity = wolf,
        };
        World.Api.Event.TriggerEntityDeath(victim.Entity, creatureDamage);
        await World.Ticks(1);
        World.Api.Event.TriggerEntityDeath(victim.Entity, PlayerDamage(killer.Entity));
        await World.Ticks(1);

        var host = new NotebookHost(slot);
        var deaths = host.History.Entries.Where(e => e.Kind == HistoryEventKind.Death).ToList();
        Assert.Equal(2, deaths.Count);
        Assert.Contains("wolf", deaths[0].Detail);
        Assert.Contains("SameDayKiller", deaths[1].Detail);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pending_entry_survives_a_savegame_round_trip()
    {
        var mod = Mod;
        var docId = Guid.NewGuid();
        mod.PendingHistoryStore!.Enqueue(docId, new HistoryEntry
        {
            Kind = HistoryEventKind.Death, Detail = "Survives a restart.", InGameDate = "d", InGameTimestamp = 1,
        });

        // Simulate a save/reload on the SAME instance: serialize exactly like OnGameWorldSave does,
        // then re-hydrate it back through LoadFrom -- the exact call OnSaveGameLoaded makes -- proving
        // the wire format round trips (mirrors ReadViewStatePersistenceScenarios' own convention).
        var bytes = mod.PendingHistoryStore!.SerializeStore();
        var reloaded = new PendingHistoryStore();
        reloaded.LoadFrom(bytes);

        var entry = Assert.Single(reloaded.TakeAll(docId));
        Assert.Equal("Survives a restart.", entry.Detail);
    }
}
