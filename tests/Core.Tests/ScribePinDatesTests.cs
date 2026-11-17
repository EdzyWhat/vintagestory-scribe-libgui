using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the date a pin snapshot carries: the plain-value status overload, the pin predicate and the
// day-change rebuild rule.
public class ScribePinDatesTests
{
    private static readonly IScribeCalendarShape Shape = new UniformCalendarShape(30);
    private static readonly ScribeDate Today = ScribeDate.Create(2, 6, 15);

    private static ScribePinnedRef Pin(ScribeBlockKind kind = ScribeBlockKind.Task, ScribeDate? date = null,
        bool orphaned = false) => new()
    {
        Kind = kind, Date = date, Orphaned = orphaned,
    };

    [Theory]
    [InlineData(false, 15, ScribeDateStatusKind.DueToday, 0)]
    [InlineData(false, 18, ScribeDateStatusKind.Upcoming, 3)]
    [InlineData(false, 10, ScribeDateStatusKind.Overdue, 5)]
    [InlineData(true, 10, ScribeDateStatusKind.Done, 0)]
    public void Classify_PlainValues_MatchTheBlockOverload(bool done, int day, ScribeDateStatusKind kind, int days)
    {
        var date = ScribeDate.Create(2, 6, day);
        var block = new ScribeBlock(ScribeBlockKind.Task, "t", done: done, date: date);

        var plain = ScribeDateRules.Classify(date, done, completable: true, Today, Shape);

        Assert.Equal(new ScribeDateStatus(kind, days), plain);
        Assert.Equal(ScribeDateRules.Classify(block, Today, Shape), plain);
    }

    [Fact]
    public void Classify_PlainValues_NullForUndatedOrNotCompletable()
    {
        Assert.Null(ScribeDateRules.Classify(null, false, true, Today, Shape));
        Assert.Null(ScribeDateRules.Classify(Today, false, false, Today, Shape));
    }

    [Fact]
    public void PinPredicate_ShowsOnlyALiveDatedTask()
    {
        var d = ScribeDate.Create(3, 5, 25);
        Assert.True(ScribeDateRules.PinShowsDate(Pin(date: d)));
        Assert.False(ScribeDateRules.PinShowsDate(Pin()));
        Assert.False(ScribeDateRules.PinShowsDate(Pin(date: d, orphaned: true)));
        Assert.False(ScribeDateRules.PinShowsDate(Pin(ScribeBlockKind.Tracker, d)));
        Assert.False(ScribeDateRules.PinShowsDate(Pin(ScribeBlockKind.Text, d)));
    }

    [Fact]
    public void ShouldRebuildForDayChange_ForwardAndBackward_OnlyWithADatedPin()
    {
        var yesterday = ScribeDate.Create(2, 6, 14);
        Assert.True(ScribeDateRules.ShouldRebuildForDayChange(yesterday, Today, anyDatedDisplayed: true));
        Assert.True(ScribeDateRules.ShouldRebuildForDayChange(Today, yesterday, anyDatedDisplayed: true));
        Assert.False(ScribeDateRules.ShouldRebuildForDayChange(Today, Today, anyDatedDisplayed: true));
        Assert.False(ScribeDateRules.ShouldRebuildForDayChange(yesterday, Today, anyDatedDisplayed: false));
    }
}
