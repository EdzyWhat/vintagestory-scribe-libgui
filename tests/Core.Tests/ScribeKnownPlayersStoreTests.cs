using Scribe.Core;

namespace Scribe.Core.Tests;

public class ScribeKnownPlayersStoreTests
{
    [Fact]
    public void Upsert_ThenSnapshot_ContainsTheEntry()
    {
        var store = new ScribeKnownPlayersStore();
        store.Upsert("player-1", "Junkmuffin");

        var snapshot = store.Snapshot();
        Assert.Contains(("player-1", "Junkmuffin"), snapshot);
    }

    [Fact]
    public void Upsert_ExistingUid_OverwritesRatherThanDuplicating()
    {
        var store = new ScribeKnownPlayersStore();
        store.Upsert("player-1", "OldName");
        store.Upsert("player-1", "NewName");

        var snapshot = store.Snapshot();
        Assert.Single(snapshot, e => e.Uid == "player-1");
        Assert.Contains(("player-1", "NewName"), snapshot);
    }

    [Fact]
    public void Upsert_BlankUid_IsNoOp()
    {
        var store = new ScribeKnownPlayersStore();
        store.Upsert("", "Nobody");
        store.Upsert("   ", "Nobody");
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void RoundTrip_SerializeAndLoad_PreservesEveryEntry()
    {
        var store = new ScribeKnownPlayersStore();
        store.Upsert("player-1", "RaptorKhan");
        store.Upsert("player-2", "Junkmuffin");

        var restored = new ScribeKnownPlayersStore();
        restored.LoadFrom(store.Serialize());

        var snapshot = restored.Snapshot();
        Assert.Contains(("player-1", "RaptorKhan"), snapshot);
        Assert.Contains(("player-2", "Junkmuffin"), snapshot);
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public void LoadFrom_NullOrMalformed_LeavesStoreEmpty()
    {
        var store = new ScribeKnownPlayersStore();
        store.Upsert("player-1", "RaptorKhan");

        store.LoadFrom(null);
        Assert.Empty(store.Snapshot());

        store.Upsert("player-1", "RaptorKhan");
        store.LoadFrom(new byte[] { 9, 9, 9 });
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void Contains_ReturnsTrueAfterUpsert_FalseForUnknownUid()
    {
        var store = new ScribeKnownPlayersStore();
        store.Upsert("player-1", "RaptorKhan");

        Assert.True(store.Contains("player-1"));
        Assert.False(store.Contains("player-2"));
    }
}
