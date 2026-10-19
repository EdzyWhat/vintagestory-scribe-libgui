using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// The world-scoped known-players registry: every player who has ever connected stays a selectable Assignment Desk target.
/// Populated on <c>OnPlayerNowPlaying</c> and broadcast in full to every online client on each change.
/// </summary>
public sealed partial class ScribeModSystem
{
    /// <summary>Savegame key for the persisted known-players registry.</summary>
    private const string KnownPlayersStoreSaveKey = "scribe:knownplayers:v1";

    /// <summary>Server-side known-players registry. Null on a pure client.</summary>
    private ScribeKnownPlayersStore? knownPlayersStore;

    /// <summary>Server-side accessor for the registry; null on the client.</summary>
    public ScribeKnownPlayersStore? KnownPlayersStore => knownPlayersStore;

    /// <summary>Client-side cache of the latest synced (uid, last-seen name) snapshot; empty until the first sync. Feeds
    /// <see cref="ScribeDialogBase.ComputeAssignmentTargetPlayers"/> so the picker can offer offline known players.</summary>
    private IReadOnlyList<(string Uid, string Name)> myKnownPlayers = System.Array.Empty<(string, string)>();

    /// <summary>This client's cached snapshot; only replaced wholesale by <see cref="OnClientReceivedKnownPlayersSync"/>.</summary>
    public IReadOnlyList<(string Uid, string Name)> MyKnownPlayers => myKnownPlayers;

    /// <summary>Raised on the client when a fresh sync arrives, so an open Assignment Desk repaints its target picker without a reopen.</summary>
    public event System.Action? KnownPlayersChanged;

    /// <summary>Upserts the joining player (refreshing their display name) and broadcasts the full snapshot so an open
    /// Assignment Desk sees the addition live. Called from <c>OnPlayerNowPlaying</c>.</summary>
    private void UpsertKnownPlayerAndBroadcast(IServerPlayer player)
    {
        if (sapi is null || knownPlayersStore is null) return;
        knownPlayersStore.Upsert(player.PlayerUID, player.PlayerName);
        BroadcastKnownPlayers();
    }

    /// <summary>Sends the full registry snapshot to every online player.</summary>
    private void BroadcastKnownPlayers()
    {
        if (sapi is null || knownPlayersStore is null) return;
        var message = new ScribeKnownPlayersSyncMessage { SnapshotBytes = knownPlayersStore.Serialize() };
        foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
            sapi.Network.GetChannel(NetworkChannelName).SendPacket(message, player);
    }

    /// <summary>Replaces the cached snapshot with the broadcast one.</summary>
    private void OnClientReceivedKnownPlayersSync(ScribeKnownPlayersSyncMessage message)
    {
        var store = new ScribeKnownPlayersStore();
        store.LoadFrom(message.SnapshotBytes);
        myKnownPlayers = store.Snapshot();
        KnownPlayersChanged?.Invoke();
    }

    /// <summary>Client-side UID→display-name resolution for every dialog/tooltip: the online player's live name, else this client's
    /// synced <see cref="MyKnownPlayers"/> entry, else the raw uid for a target never seen.</summary>
    public string ResolvePlayerName(string uid)
    {
        if (capi?.World.PlayerByUid(uid)?.PlayerName is { } online) return online;
        foreach (var (knownUid, name) in myKnownPlayers)
            if (knownUid == uid) return name;
        return uid;
    }
}
