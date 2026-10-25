using Scribe.Core;
using Xunit;

namespace Scribe.Core.Tests;

public class TimerViewTrackerTests
{
    [Theory]
    [InlineData(TimerStatus.Fired, TimerStatus.Idle, true)]    // opened on a fired timer, then Stop
    [InlineData(TimerStatus.Running, TimerStatus.Idle, true)]  // opened mid-run, then Stop
    [InlineData(TimerStatus.Idle, TimerStatus.Running, true)]
    [InlineData(TimerStatus.Running, TimerStatus.Fired, true)]
    [InlineData(TimerStatus.Running, TimerStatus.Running, false)]
    [InlineData(TimerStatus.Idle, TimerStatus.Idle, false)]
    public void TryAdvance_RebuildsOnlyOnStatusChange(TimerStatus seed, TimerStatus next, bool expected)
    {
        var tracker = new TimerViewTracker(seed);
        Assert.Equal(expected, tracker.TryAdvance(next));
    }

    [Fact]
    public void TryAdvance_RepeatedStatusRebuildsOnce()
    {
        var tracker = new TimerViewTracker();
        Assert.True(tracker.TryAdvance(TimerStatus.Running));
        Assert.False(tracker.TryAdvance(TimerStatus.Running));
        Assert.Equal(TimerStatus.Running, tracker.Status);
    }
}
