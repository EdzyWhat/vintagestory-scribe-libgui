using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// Loading a pre-v4 (v3) world: the codec no longer accepts v3 bytes (PriorVersion = 4), so the
/// lectern falls back to a fresh empty document. No pin drain occurs. "fixtures/lectern-v3.vcdbs"
/// is the world the original FixtureBuilders produced under the v3 codec — it is checked in as-is
/// and is IRREPLACEABLE. See the README's fixtures note.
/// </summary>
[AtlasWorld(SaveFile = "fixtures/lectern-v3.vcdbs")]
public class MigrationScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task Loading_a_v3_world_produces_an_empty_document_and_no_pins()
    {
        var pos = World.Spawn.Offset(2, 0, 0);
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        // v3 bytes are rejected by the codec (PriorVersion = 4); the lectern falls back to a fresh
        // empty document rather than attempting a migration.
        Assert.Empty(lectern!.Document.Blocks);

        // No pins are drained — there is nothing to migrate from a rejected document.
        var mod = World.Api.ModLoader.GetModSystem<ScribeModSystem>();
        var player = await World.JoinPlayer("MigrationPlayer");
        Assert.Empty(mod.PinStore!.Get(player.Player.PlayerUID));

        // The document is current-version bytes (freshly-default document, not the v3 save).
        Assert.False(ScribeDocumentCodec.IsPriorVersion(ScribeDocumentCodec.Serialize(lectern.Document)));
    }
}
