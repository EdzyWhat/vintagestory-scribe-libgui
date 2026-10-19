using Atlas.Api;
using Atlas.XUnit;
using Scribe;

namespace Integration.Tests;

/// <summary>
/// Server-side known-players registry: <see cref="ScribeModSystem.OnPlayerNowPlaying"/> upserts a joining
/// player, and an already-known player stays once a second player joins (the snapshot
/// <c>BroadcastKnownPlayers</c> pushes). Atlas does not round-trip network packets, so this asserts the
/// server-authoritative data the broadcast is built from, not the client-side cache.
/// </summary>
public class KnownPlayersScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    [AtlasScenario(RollbackWorld = true)]
    public async Task PlayerJoin_UpsertsIntoKnownPlayersRegistry()
    {
        var player = await World.JoinPlayer("KnownPlyrJoin");

        var snapshot = Mod.KnownPlayersStore!.Snapshot();
        Assert.Contains((player.Player.PlayerUID, "KnownPlyrJoin"), snapshot);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task SecondPlayerJoining_LeavesTheFirstStillInTheRegistry()
    {
        var first = await World.JoinPlayer("KnownPlyrFirst");
        var second = await World.JoinPlayer("KnownPlyrSecnd");

        var snapshot = Mod.KnownPlayersStore!.Snapshot();
        Assert.Contains((first.Player.PlayerUID, "KnownPlyrFirst"), snapshot);
        Assert.Contains((second.Player.PlayerUID, "KnownPlyrSecnd"), snapshot);
    }
}
