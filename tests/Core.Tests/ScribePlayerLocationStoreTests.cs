using Scribe.Core;

namespace Scribe.Core.Tests;

public class ScribePlayerLocationStoreTests
{
    [Fact]
    public void TryGetLastKnown_UnknownPlayer_ReturnsFalse()
    {
        var store = new ScribePlayerLocationStore();
        Assert.False(store.TryGetLastKnown("nobody", out _));
    }

    [Fact]
    public void SetLastKnown_ThenGet_RoundTrips()
    {
        var store = new ScribePlayerLocationStore();
        store.SetLastKnown("player-1", new ScribeWorldPosition(10, 20, 30));

        Assert.True(store.TryGetLastKnown("player-1", out var pos));
        Assert.Equal(10, pos.X);
        Assert.Equal(20, pos.Y);
        Assert.Equal(30, pos.Z);
    }

    [Fact]
    public void SetLastKnown_OverwritesPriorValue()
    {
        var store = new ScribePlayerLocationStore();
        store.SetLastKnown("player-1", new ScribeWorldPosition(10, 20, 30));
        store.SetLastKnown("player-1", new ScribeWorldPosition(1, 2, 3));

        Assert.True(store.TryGetLastKnown("player-1", out var pos));
        Assert.Equal(1, pos.X);
        Assert.Equal(2, pos.Y);
        Assert.Equal(3, pos.Z);
    }

    [Fact]
    public void SetLastKnown_BlankUid_IsNoOp()
    {
        var store = new ScribePlayerLocationStore();
        store.SetLastKnown("", new ScribeWorldPosition(1, 2, 3));
        store.SetLastKnown("   ", new ScribeWorldPosition(1, 2, 3));
        Assert.False(store.TryGetLastKnown("", out _));
        Assert.False(store.TryGetLastKnown("   ", out _));
    }

    [Fact]
    public void RoundTrip_SerializeAndLoad_PreservesEveryPlayer()
    {
        var store = new ScribePlayerLocationStore();
        store.SetLastKnown("player-1", new ScribeWorldPosition(10, 20, 30));
        store.SetLastKnown("player-2", new ScribeWorldPosition(-5, 0, 5.5));

        var restored = new ScribePlayerLocationStore();
        restored.LoadFrom(store.Serialize());

        Assert.True(restored.TryGetLastKnown("player-1", out var pos1));
        Assert.Equal(10, pos1.X);
        Assert.Equal(20, pos1.Y);
        Assert.Equal(30, pos1.Z);

        Assert.True(restored.TryGetLastKnown("player-2", out var pos2));
        Assert.Equal(-5, pos2.X);
        Assert.Equal(0, pos2.Y);
        Assert.Equal(5.5, pos2.Z);
    }

    [Fact]
    public void LoadFrom_NullOrMalformed_LeavesStoreEmpty()
    {
        var store = new ScribePlayerLocationStore();
        store.SetLastKnown("player-1", new ScribeWorldPosition(10, 20, 30));

        store.LoadFrom(null);
        Assert.False(store.TryGetLastKnown("player-1", out _));

        store.SetLastKnown("player-1", new ScribeWorldPosition(10, 20, 30));
        store.LoadFrom(new byte[] { 9, 9, 9 });
        Assert.False(store.TryGetLastKnown("player-1", out _));
    }
}
