using System.Text;
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
    public void RoundTrip_Fired_PreservesFiredElapsed()
    {
        // A fired timer persists its FiredElapsedSeconds (codec v2) so the client-driven auto-disappear
        // resumes the remaining window on rejoin rather than restarting (timer-auto-disappear-setting).
        var store = new TimerStore
        {
            Status = TimerStatus.Fired,
            Mode = TimerMode.RealTime,
            Label = "Kiln done",
            RemainingSeconds = 0.0,
            FiredElapsedSeconds = 12.5,
        };
        var back = TimerStore.Deserialize(store.Serialize());
        Assert.Equal(TimerStatus.Fired, back.Status);
        Assert.Equal("Kiln done", back.Label);
        Assert.Equal(12.5, back.FiredElapsedSeconds, precision: 4);
    }

    [Fact]
    public void DefaultStore_FiredElapsed_IsZero() =>
        Assert.Equal(0.0, new TimerStore().FiredElapsedSeconds);

    [Fact]
    public void TryDeserialize_V1Bytes_FiredElapsed_DefaultsToZero()
    {
        // Hand-build a byte array in exactly the v1 format (no FiredElapsedSeconds field), per
        // docs/CODEC-MIGRATION.md, and assert the migrated field value — not merely that it parses.
        //   v1 layout: magic "STMR" | version(1) | status | mode | label | remainingSeconds
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes("STMR"));
            w.Write((byte)1);                       // version 1 (prior)
            w.Write((byte)TimerStatus.Running);
            w.Write((byte)TimerMode.RealTime);
            w.Write("Legacy timer");
            w.Write(90.0);
            // No FiredElapsedSeconds in v1.
        }

        var back = TimerStore.Deserialize(ms.ToArray());
        Assert.Equal(TimerStatus.Running, back.Status);
        Assert.Equal(TimerMode.RealTime, back.Mode);
        Assert.Equal("Legacy timer", back.Label);
        Assert.Equal(90.0, back.RemainingSeconds, precision: 4);
        Assert.Equal(0.0, back.FiredElapsedSeconds); // v1 has no field → defaults to 0
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
