using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// 7.6: loading a pre-change (v3) world migrates it forward. "fixtures/lectern-v3.vcdbs" is the
/// world the ORIGINAL FixtureBuilders produced under the v3 codec — a lectern whose document stored
/// the retired shared per-block <c>pinned</c> flag (task 0, "Find copper", pinned) and no per-player
/// pin store. It is checked in as-is and is IRREPLACEABLE: the current codec only writes v4, so this
/// v3 save can never be regenerated. See the README's fixtures note.
///
/// On a fresh v3 load: each lectern deserializes via the codec's v3 path (fresh DocId/TaskIds
/// generated, the old pinned task ids surfaced through the migration seam), is marked dirty so it
/// re-saves as v4 (else the ids regenerate every load and pins can't stick — the design's single most
/// important sequencing detail), and its previously-pinned tasks are drained into the joining
/// player's pin store on the first PlayerNowPlaying. That drain is SINGLE-PLAYER-SCOPED (the v3 flag
/// was shared, not per-player), so this scenario forces MaxClients = 1 before the join — Atlas's
/// default 16-client server would otherwise skip the drain.
///
/// This is a single-boot scenario (no RestartWorld): the migration only happens while the document is
/// still freshly-v3; a restart would consume the v3-ness during reboot with no player online to
/// receive the drain. That pins, once drained, survive a genuine restart is covered separately by
/// PersistenceScenarios (which boots a v4 fixture).
/// </summary>
[AtlasWorld(SaveFile = "fixtures/lectern-v3.vcdbs")]
public class MigrationScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task Loading_a_v3_world_drains_legacy_pins_and_resaves_as_v4()
    {
        // The drain is single-player-scoped; force that before the join triggers PlayerNowPlaying.
        World.Api.Server.Config.MaxClients = 1;

        var pos = World.Spawn.Offset(2, 0, 0);
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        // The v3 document deserialized with a fresh DocId and per-block TaskIds; task 0 ("Find copper")
        // was the previously-pinned one.
        var blocks = lectern!.Document.Blocks;
        Assert.Equal("Find copper", blocks[0].Text);
        var docId = lectern.Document.DocId;
        var copperId = blocks[0].TaskId;

        // Joining fires PlayerNowPlaying → the one-time legacy-pin drain. The previously-pinned task now
        // appears in this player's store and resolves to the loaded document's live TaskId.
        var mod = World.Api.ModLoader.GetModSystem<ScribeModSystem>();
        var player = await World.JoinPlayer("MigrationPlayer");
        Assert.True(mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, copperId));

        // The lectern re-saves as v4: its document now serializes under the current (v4) layout, no
        // longer the prior version — so the freshly-generated ids the pin references will persist.
        Assert.False(ScribeDocumentCodec.IsPriorVersion(ScribeDocumentCodec.Serialize(lectern.Document)));
    }
}
