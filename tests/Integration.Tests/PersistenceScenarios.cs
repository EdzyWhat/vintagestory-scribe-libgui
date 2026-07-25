using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// Task 4.3b: does a lectern's document (and a per-player pin referencing it) survive a genuine
/// save/load round trip?
///
/// Per the wiki's own guidance, the seed must come from a fixture, not an earlier scenario
/// method in the same class (xUnit gives no execution-order guarantee within a class, so a
/// seed-then-restart pair can flip order and fail intermittently). "fixtures/lectern.vcdbs"
/// is generated once via `atlas fixture` from FixtureBuilders.BuildsLecternWithDocumentFixture
/// (see README's "Running the Atlas suite" section for the exact command). This class boots
/// straight from that pre-seeded save and only asserts -- there is no seeding scenario here
/// to race against, so RestartWorld genuinely proves persistence rather than relying on order.
///
/// NOTE: the fixture must be REGENERATED for add-pinned-task-foundation — the pre-change save
/// stored the retired per-block `pinned` flag (v3 codec) and no pin store; the rebuilt fixture is
/// a v4 world whose document carries stable ids and whose pin lives in the per-player store. Run
/// the `atlas fixture` command in the README against the updated FixtureBuilders before this runs.
/// </summary>
[AtlasWorld(SaveFile = "fixtures/lectern.vcdbs")]
public class PersistenceScenarios : AtlasScenarioBase
{
    [AtlasScenario(RestartWorld = true)]
    public async Task Lectern_document_and_pin_survive_a_server_restart()
    {
        var pos = World.Spawn.Offset(2, 0, 0);

        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        var blocks = lectern!.Document.Blocks;
        Assert.Equal(2, blocks.Count);
        Assert.Equal("Find copper", blocks[0].Text);
        Assert.True(blocks[0].Done);
        Assert.Equal("Left the mine at day 3", blocks[1].Text);

        // The per-player pin seeded in the fixture (FixtureBuilder pinned the first task) survives the
        // restart: the store reloaded it from the save game and it still resolves to the same task.
        var mod = World.Api.ModLoader.GetModSystem<ScribeModSystem>();
        var player = await World.JoinPlayer("FixtureBuilder");
        Assert.True(mod.PinStore!.IsPinned(player.Player.PlayerUID, lectern.Document.DocId, blocks[0].TaskId));

        // (Per-player preferences are no longer server/save-game state — they're client-local JSON —
        // so there is nothing settings-related to assert surviving the restart here. Pins persist;
        // preferences live with the client.)
    }
}
