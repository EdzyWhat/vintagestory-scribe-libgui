using Atlas.Api;
using Atlas.XUnit;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// Builder scenarios harvested into world fixtures via `atlas fixture` (see README's
/// "Running the Atlas suite" section for the exact command). Not run as part of the normal
/// `dotnet test` suite -- selected individually by `atlas fixture --scenario &lt;substring&gt;`.
/// Each builder places a lectern, edits its document through the real production path
/// (OnRightClick to acquire the lock, then ApplyEdit), and lets the fixture's graceful
/// teardown persist the save.
/// </summary>
public class FixtureBuilders : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task BuildsLecternWithDocumentFixture()
    {
        var pos = World.Spawn.Offset(2, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var player = await World.JoinPlayer("FixtureBuilder");
        var lectern = World.BlockEntityAt<Scribe.BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(player.Player, wantEditor: true);

        var doc = new ScribeDocument();
        doc.AddTask("Find copper");
        doc.AddTextSection("Left the mine at day 3");
        doc.ToggleTask(0);
        lectern.ApplyEdit(player.Player, ScribeDocumentCodec.Serialize(doc));

        // Pin the (now-done) first task for this player through the real server path, so the
        // persistence fixture also captures a per-player pin that must survive a restart. (Per-player
        // preferences are NOT server state anymore — they're client-local JSON — so there is nothing
        // settings-related to seed into the save here.)
        var mod = World.Api.ModLoader.GetModSystem<Scribe.ScribeModSystem>();
        mod.SetPinForPlayer(player.Player, lectern.Document.DocId, lectern.Document.Blocks[0].TaskId, pinned: true);

        await World.Ticks(2);
    }
}
