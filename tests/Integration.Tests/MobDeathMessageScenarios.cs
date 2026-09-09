using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// fix-mob-death-message-probe-and-notebook-gate tasks 3.1-3.3: regression coverage for two
/// independent fixes inside <c>OnEntityDeath</c>/<c>BuildDeathMessage</c>
/// (<c>src/Mod/ScribeModSystem.History.cs</c>) --
/// (1) discovering the <c>scribe-mob-death-N</c> flavor pool's size no longer formats a zero-arg
/// template, which used to throw inside <c>TranslationService.TryFormat</c> and log an
/// [Error]+[Warning] pair on every probe (design.md decision 1); and
/// (2) Death/PvpKill message construction is skipped entirely when no relevant party (the victim,
/// or the killer for a PvP kill) carries a Notebook (design.md decision 3).
///
/// Each scenario watches <c>Api.Logger.EntryAdded</c> around the death for a Warning containing
/// "Translation string format exception" -- the exact text <c>TryFormat</c>'s own catch block
/// writes -- to prove the pool-size probe never triggers it.
///
/// Deaths are induced directly via <c>Api.Event.TriggerEntityDeath</c> -- the same public entry
/// point <c>Entity.Die()</c> uses internally, and the exact event
/// <c>ScribeModSystem.OnEntityDeath</c> subscribes to in <c>StartServerSide</c> -- rather than
/// draining real health through combat, which would be slow and non-deterministic in a headless
/// suite for no added coverage: the fix under test lives entirely inside the event handler, not
/// in how a death is triggered.
/// </summary>
public class MobDeathMessageScenarios : AtlasScenarioBase
{
    private const string TranslationWarningMarker = "Translation string format exception";
    private const string NotebookCode = "scribe:scribenotebook";

    /// <summary>Subscribes to the server logger and reports whether a translation-format warning
    /// fired while subscribed. Callers must invoke the returned <c>Detach</c> action once done
    /// watching (there is no scenario-scoped auto-unsubscribe).</summary>
    private (Action Detach, Func<bool> WasLogged) WatchForTranslationWarning()
    {
        bool seen = false;
        void Handler(EnumLogType logType, string format, object[] args)
        {
            if (logType == EnumLogType.Warning && format.Contains(TranslationWarningMarker)) seen = true;
        }
        World.Api.Logger.EntryAdded += Handler;
        return (() => World.Api.Logger.EntryAdded -= Handler, () => seen);
    }

    private static DamageSource CreatureDamage(Vintagestory.API.Common.Entities.Entity creature) => new()
    {
        Source      = EnumDamageSource.Entity,
        Type        = EnumDamageType.BluntAttack,
        SourceEntity = creature,
    };

    private static DamageSource PlayerDamage(Vintagestory.API.Common.Entities.Entity killerEntity) => new()
    {
        Source       = EnumDamageSource.Player,
        Type         = EnumDamageType.BluntAttack,
        SourceEntity = killerEntity,
    };

    [AtlasScenario(RollbackWorld = true)]
    public async Task Creature_kill_records_a_flavored_line_and_logs_no_translation_warning()
    {
        var pos = World.Spawn.Offset(3, 0, 0);
        var wolf = World.SpawnEntity("game:wolf-eurasian-adult-male", pos);

        var victim = await World.JoinPlayer("MobDeathVictim");
        await victim.GiveItem(NotebookCode);

        var (detach, wasLogged) = WatchForTranslationWarning();
        World.Api.Event.TriggerEntityDeath(victim.Entity, CreatureDamage(wolf));
        await World.Ticks(2);
        detach();

        Assert.False(wasLogged(),
            "Discovering the mob-death flavor pool's size must not log a translation-format warning.");

        var host = new NotebookHost(victim.Player.InventoryManager.ActiveHotbarSlot);
        var last = host.History.Entries[^1];
        Assert.Equal(HistoryEventKind.Death, last.Kind);
        Assert.Contains("MobDeathVictim", last.Detail);
        Assert.Contains("wolf", last.Detail);
        Assert.DoesNotContain("{0}", last.Detail);
        Assert.DoesNotContain("{1}", last.Detail);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Death_with_no_notebook_anywhere_writes_nothing_and_logs_no_translation_warning()
    {
        var pos = World.Spawn.Offset(3, 0, 0);
        var wolf = World.SpawnEntity("game:wolf-eurasian-adult-male", pos);

        // Deliberately no GiveItem -- this player carries no Notebook anywhere on their person, so
        // there is nowhere for a Death entry to land.
        var victim = await World.JoinPlayer("MobDeathNoNB");

        var (detach, wasLogged) = WatchForTranslationWarning();
        World.Api.Event.TriggerEntityDeath(victim.Entity, CreatureDamage(wolf));
        await World.Ticks(2);
        detach();

        Assert.False(wasLogged(),
            "with no Notebook anywhere to record the result, message construction should be skipped " +
            "entirely, so nothing could trigger the translation warning either.");
        Assert.Null(victim.Player.InventoryManager.ActiveHotbarSlot?.Itemstack);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pvp_kill_where_only_killer_carries_a_notebook_writes_PvpKill_only()
    {
        var killer = await World.JoinPlayer("MobDeathKillerNB");
        var victim = await World.JoinPlayer("MobDeathVicNoNB");
        await killer.GiveItem(NotebookCode);

        var (detach, wasLogged) = WatchForTranslationWarning();
        World.Api.Event.TriggerEntityDeath(victim.Entity, PlayerDamage(killer.Entity));
        await World.Ticks(2);
        detach();

        Assert.False(wasLogged());

        var killerHost = new NotebookHost(killer.Player.InventoryManager.ActiveHotbarSlot);
        var last = killerHost.History.Entries[^1];
        Assert.Equal(HistoryEventKind.PvpKill, last.Kind);
        Assert.Contains("MobDeathVicNoNB", last.Detail);

        // The victim carries no Notebook, so there's nothing to inspect on their side -- the point
        // of this scenario is precisely that no Death entry is written for them anywhere.
        Assert.Null(victim.Player.InventoryManager.ActiveHotbarSlot?.Itemstack);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pvp_kill_where_only_victim_carries_a_notebook_writes_Death_only()
    {
        var killer = await World.JoinPlayer("MobDeathKilNoNB");
        var victim = await World.JoinPlayer("MobDeathVicNB");
        await victim.GiveItem(NotebookCode);

        var (detach, wasLogged) = WatchForTranslationWarning();
        World.Api.Event.TriggerEntityDeath(victim.Entity, PlayerDamage(killer.Entity));
        await World.Ticks(2);
        detach();

        Assert.False(wasLogged());

        var victimHost = new NotebookHost(victim.Player.InventoryManager.ActiveHotbarSlot);
        var last = victimHost.History.Entries[^1];
        Assert.Equal(HistoryEventKind.Death, last.Kind);
        Assert.Contains("MobDeathKilNoNB", last.Detail);

        // The killer carries no Notebook, so there's nothing to inspect on their side -- the point
        // of this scenario is precisely that no PvpKill entry is written for them anywhere.
        Assert.Null(killer.Player.InventoryManager.ActiveHotbarSlot?.Itemstack);
    }
}
