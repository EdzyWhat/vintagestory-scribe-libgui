using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// Does a lectern's document (and a per-player pin referencing it) survive a genuine save/load round trip?
///
/// The seed comes from a fixture, not an earlier scenario (xUnit gives no execution-order guarantee within a
/// class). "fixtures/lectern.vcdbs" is generated via `atlas fixture` from
/// FixtureBuilders.BuildsLecternWithDocumentFixture (see README's "Running the Atlas suite" section); this class
/// boots from that save and only asserts.
///
/// NOTE: regenerate the fixture whenever the document codec version bumps. Once its stored version drops out
/// of the accepted window the save loads as an empty document (0 blocks) and this scenario fails.
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

        // Per-player preferences are client-local JSON, so only pins are asserted to survive the restart.
    }
}
