using Atlas.Api;
using Atlas.XUnit;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// Builder scenarios harvested into world fixtures via `atlas fixture` (see README's "Running the Atlas
/// suite" section). Not part of the normal `dotnet test` run; selected with
/// `atlas fixture --scenario &lt;substring&gt;`. Each builder places a lectern and edits its document via
/// OnRightClick (acquire the lock) then ApplyEdit; the fixture's graceful teardown persists the save.
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

        lectern!.OnRightClick(player.Player, wantEditor: true, quickAdd: false);

        var doc = new ScribeDocument();
        doc.AddTask("Find copper");
        doc.AddTextSection("Left the mine at day 3");
        doc.ToggleTask(0);
        lectern.ApplyEdit(player.Player, ScribeDocumentCodec.Serialize(doc));

        // Pin the now-done first task through the real server path so the fixture also captures a per-player
        // pin that must survive a restart.
        var mod = World.Api.ModLoader.GetModSystem<Scribe.ScribeModSystem>();
        mod.SetPinForPlayer(player.Player, lectern.Document.DocId, lectern.Document.Blocks[0].TaskId, pinned: true);

        await World.Ticks(2);
    }
}
