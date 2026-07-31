using Scribe.Core;
using Xunit;

namespace Core.Tests;

public class TimerStoreTests
{
    [Fact]
    public void DefaultStore_IsIdle()
    {
        var store = new TimerStore();
        Assert.Equal(TimerStatus.Idle, store.Status);
        Assert.Equal(TimerMode.InGame, store.Mode);
        Assert.Equal("", store.Label);
        Assert.Equal(0.0, store.RemainingSeconds);
    }

    [Fact]
    public void RoundTrip_Idle()
    {
        var store = new TimerStore();
        var back = TimerStore.Deserialize(store.Serialize());
        Assert.Equal(TimerStatus.Idle, back.Status);
        Assert.Equal(TimerMode.InGame, back.Mode);
        Assert.Equal("", back.Label);
        Assert.Equal(0.0, back.RemainingSeconds);
    }

    [Fact]
    public void RoundTrip_Running()
    {
        var store = new TimerStore
        {
            Status = TimerStatus.Running,
            Mode = TimerMode.RealTime,
            Label = "Bread baking",
            RemainingSeconds = 3661.5,
        };
        var back = TimerStore.Deserialize(store.Serialize());
        Assert.Equal(TimerStatus.Running, back.Status);
        Assert.Equal(TimerMode.RealTime, back.Mode);
        Assert.Equal("Bread baking", back.Label);
        Assert.Equal(3661.5, back.RemainingSeconds, precision: 4);
    }

    [Fact]
    public void RoundTrip_Fired()
    {
        var store = new TimerStore
        {
            Status = TimerStatus.Fired,
            Mode = TimerMode.InGame,
            Label = "",
            RemainingSeconds = 0.0,
        };
        var back = TimerStore.Deserialize(store.Serialize());
        Assert.Equal(TimerStatus.Fired, back.Status);
        Assert.Equal(0.0, back.RemainingSeconds);
    }

    [Fact]
    public void RoundTrip_UnicodeLabel()
    {
        var store = new TimerStore
        {
            Status = TimerStatus.Running,
            Label = "Тест 🕐",
            RemainingSeconds = 60,
        };
        var back = TimerStore.Deserialize(store.Serialize());
        Assert.Equal("Тест 🕐", back.Label);
    }

    [Fact]
    public void Deserialize_Null_ReturnsIdle() =>
        Assert.Equal(TimerStatus.Idle, TimerStore.Deserialize(null).Status);

    [Fact]
    public void Deserialize_Empty_ReturnsIdle() =>
        Assert.Equal(TimerStatus.Idle, TimerStore.Deserialize(Array.Empty<byte>()).Status);

    [Fact]
    public void Deserialize_BadMagic_ReturnsIdle()
    {
        var bytes = new byte[] { 0x58, 0x58, 0x58, 0x58, 0x01, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(TimerStatus.Idle, TimerStore.Deserialize(bytes).Status);
    }
}
