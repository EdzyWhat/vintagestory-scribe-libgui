using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// Regression coverage for creature-death / PvP History recording: the <c>scribe-mob-death-N</c>
/// flavor pool is discovered without formatting a zero-arg template, Death/PvpKill fact capture is
/// skipped when no relevant party carries a Notebook, and live-schema rows store facts (not a
/// finished English sentence) that <see cref="HistoryDisplay"/> formats in the viewer locale.
///
/// Each scenario watches <c>Api.Logger.EntryAdded</c> around the death for a Warning containing
/// "Translation string format exception" -- the exact text <c>TryFormat</c>'s own catch block
/// writes -- to prove the pool-size probe never triggers it.
///
/// Deaths are induced directly via <c>Api.Event.TriggerEntityDeath</c> -- the same public entry
/// point <c>Entity.Die()</c> uses internally, and the exact event
/// <c>ScribeModSystem.OnEntityDeath</c> subscribes to in <c>StartServerSide</c>.
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
        Assert.Equal(HistorySchema.Live, last.Schema);
        Assert.Equal("MobDeathVictim", last.SubjectName);
        Assert.Contains("wolf", last.RefCode);
        Assert.Equal("", last.Detail);
        Assert.DoesNotContain("{0}", last.RefCode);

        string sentence = HistoryDisplay.Sentence(last);
        Assert.Contains("MobDeathVictim", sentence);
        Assert.Contains("wolf", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{0}", sentence);
        Assert.DoesNotContain("{1}", sentence);

        // A pre-v4 baked Death still shows its stored sentence, not a live re-format.
        var baked = new HistoryEntry
        {
            Kind = HistoryEventKind.Death,
            Schema = HistorySchema.Baked,
            Detail = "Alice fell to her death.",
        };
        Assert.Equal("Alice fell to her death.", HistoryDisplay.Body(baked));
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
        Assert.Equal(HistorySchema.Live, last.Schema);
        Assert.Equal("MobDeathVicNoNB", last.SubjectName);
        Assert.Equal("MobDeathKillerNB", last.OtherName);
        Assert.Equal("", last.Detail);
        Assert.Contains("MobDeathVicNoNB", HistoryDisplay.Sentence(last));

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
        Assert.Equal(HistorySchema.Live, last.Schema);
        Assert.Equal("MobDeathVicNB", last.SubjectName);
        Assert.Equal("MobDeathKilNoNB", last.OtherName);
        Assert.Equal("", last.Detail);
        Assert.Contains("MobDeathKilNoNB", HistoryDisplay.Sentence(last));

        // The killer carries no Notebook, so there's nothing to inspect on their side -- the point
        // of this scenario is precisely that no PvpKill entry is written for them anywhere.
        Assert.Null(killer.Player.InventoryManager.ActiveHotbarSlot?.Itemstack);
    }
}
