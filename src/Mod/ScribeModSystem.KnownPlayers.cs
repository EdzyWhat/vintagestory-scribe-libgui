using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// The world-scoped known-players registry (persist-known-players-for-assignment): every player who
/// has ever connected stays a selectable Assignment Desk target, regardless of current online state.
/// Populated on <c>OnPlayerNowPlaying</c> (wired from <c>ScribeModSystem.ServerLifecycle.cs</c>) and
/// broadcast in full to every online client on each change — see design.md Decision 3 for why this is
/// the channel's first broadcast-shaped message rather than a per-player push.
/// </summary>
public sealed partial class ScribeModSystem
{
    /// <summary>Savegame key for the persisted known-players registry (<see cref="ScribeKnownPlayersStore"/>).</summary>
    private const string KnownPlayersStoreSaveKey = "scribe:knownplayers:v1";

    /// <summary>Server-side known-players registry. Null on a pure client.</summary>
    private ScribeKnownPlayersStore? knownPlayersStore;

    /// <summary>Server-side accessor for the known-players registry, mirroring <see cref="PlayerLocationStore"/>'s
    /// exposure pattern. Null on the client.</summary>
    public ScribeKnownPlayersStore? KnownPlayersStore => knownPlayersStore;

    /// <summary>Client-side cache of the latest synced known-players snapshot (uid, last-seen display
    /// name), populated by the server broadcast. Empty until the first sync arrives. Consulted by
    /// <see cref="ScribeDialogBase.ComputeAssignmentTargetPlayers"/> to offer offline previously-known
    /// players in the Assignment Desk's target picker.</summary>
    private IReadOnlyList<(string Uid, string Name)> myKnownPlayers = System.Array.Empty<(string, string)>();

    /// <summary>This client's cached known-players snapshot — never mutated directly by the picker,
    /// only replaced wholesale by <see cref="OnClientReceivedKnownPlayersSync"/>.</summary>
    public IReadOnlyList<(string Uid, string Name)> MyKnownPlayers => myKnownPlayers;

    /// <summary>Raised on the client whenever a fresh known-players sync arrives, so an open Assignment
    /// Desk dialog's Create Assignments tab can repaint its target picker with the newly-known player —
    /// the spec's "no reopen required" scenario.</summary>
    public event System.Action? KnownPlayersChanged;

    /// <summary>Upserts the joining player into the registry (refreshing their display name in case it
    /// changed) and broadcasts the updated full snapshot to every online client, so an already-open
    /// Assignment Desk dialog observes the addition live without a reopen (spec's "brand-new player is
    /// assignable immediately" scenario). Called from <c>OnPlayerNowPlaying</c>.</summary>
    private void UpsertKnownPlayerAndBroadcast(IServerPlayer player)
    {
        if (sapi is null || knownPlayersStore is null) return;
        knownPlayersStore.Upsert(player.PlayerUID, player.PlayerName);
        BroadcastKnownPlayers();
    }

    /// <summary>Sends the full registry snapshot to every currently online player (design.md Decision 3).</summary>
    private void BroadcastKnownPlayers()
    {
        if (sapi is null || knownPlayersStore is null) return;
        var message = new ScribeKnownPlayersSyncMessage { SnapshotBytes = knownPlayersStore.Serialize() };
        foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
            sapi.Network.GetChannel(NetworkChannelName).SendPacket(message, player);
    }

    /// <summary>Replaces the client's cached known-players snapshot with the freshly-broadcast one.</summary>
    private void OnClientReceivedKnownPlayersSync(ScribeKnownPlayersSyncMessage message)
    {
        var store = new ScribeKnownPlayersStore();
        store.LoadFrom(message.SnapshotBytes);
        myKnownPlayers = store.Snapshot();
        KnownPlayersChanged?.Invoke();
    }
}
