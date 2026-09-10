using Atlas.Api;
using Atlas.XUnit;
using Scribe;

namespace Integration.Tests;

/// <summary>
/// persist-known-players-for-assignment tasks.md 3.2/4.3: proves the server-side registry every
/// online client's known-players sync gets broadcast from — <see cref="ScribeModSystem.OnPlayerNowPlaying"/>
/// upserting a joining player, and an already-known player staying in the registry once a second
/// player joins (the same snapshot <c>BroadcastKnownPlayers</c> would push to both). Atlas does not
/// round-trip real network packets (see <c>NoticeLifecycleScenarios</c>'s own remarks), so the
/// client-side cached snapshot itself is untestable here — this asserts the server-authoritative data
/// the broadcast is built from, which is what actually answers "does the registry now include the new
/// joiner."
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
