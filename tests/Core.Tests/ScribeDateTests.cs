using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the dated-task Core: the date value, the calendar shape, status and rollover rules, the
// block's date fields, and the Date order template key (dated-task spec).
public class ScribeDateTests
{
    private static ScribeDate D(int year, int month, int day) => ScribeDate.Create(year, month, day);

    private static ScribeBlock Task(ScribeDate? date, bool done = false, bool repeat = false) =>
        new(ScribeBlockKind.Task, "Task", done: done, date: date, repeatYearly: repeat);

    // ---- ScribeDate ----

    [Theory]
    [InlineData(0, 5, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 13, 1)]
    [InlineData(1, 5, 0)]
    [InlineData(-3, 5, 1)]
    [InlineData(ScribeDate.MaxYear + 1, 5, 1)]
    [InlineData(1, 5, ScribeDate.MaxDay + 1)]
    public void TryCreate_RejectsOutOfRangeParts(int year, int month, int day)
    {
        Assert.False(ScribeDate.TryCreate(year, month, day, out _));
    }

    [Fact]
    public void TryCreate_AcceptsADayPastAnyMonthEnd()
    {
        Assert.True(ScribeDate.TryCreate(3, 5, 25, out var date));
        Assert.Equal(25, date.Day);
    }

    [Fact]
    public void TextForm_RoundTrips()
    {
        var date = D(3, 5, 25);
        Assert.Equal("3-5-25", date.ToString());
        Assert.True(ScribeDate.TryParse("3-5-25", out var parsed));
        Assert.Equal(date, parsed);
        Assert.False(ScribeDate.TryParse("3-5", out _));
        Assert.False(ScribeDate.TryParse("-3-5-1", out _));
        Assert.False(ScribeDate.TryParse("3-x-1", out _));
    }

    // ---- Calendar shape ----

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(30)]
    public void UniformShape_EveryMonthHasTheWorldsDays(int daysPerMonth)
    {
        IScribeCalendarShape shape = new UniformCalendarShape(daysPerMonth);
        for (int month = 1; month <= 12; month++)
            Assert.Equal(daysPerMonth, shape.DaysInMonth(2, month));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(30)]
    public void UniformShape_OrdinalMatchesTheGeneralLoop_AndRoundTrips(int daysPerMonth)
    {
        var uniform = new UniformCalendarShape(daysPerMonth);
        IScribeCalendarShape general = new LoopOnlyShape(daysPerMonth);
        foreach (var date in new[] { D(1, 1, 1), D(1, 12, daysPerMonth), D(2, 1, 1), D(7, 6, 2), D(3, 5, 25) })
        {
            int ordinal = ((IScribeCalendarShape)uniform).ToOrdinal(date);
            Assert.Equal(general.ToOrdinal(date), ordinal);
            Assert.Equal(((IScribeCalendarShape)uniform).Clamp(date), uniform.FromOrdinal(ordinal));
        }
        Assert.Equal(0, ((IScribeCalendarShape)uniform).ToOrdinal(D(1, 1, 1)));
    }

    [Fact]
    public void Clamp_ShortMonthShowsTheLastDay_AndLeavesTheStoredDateAlone()
    {
        IScribeCalendarShape shape = new UniformCalendarShape(9);
        var stored = D(3, 5, 25);

        Assert.Equal(D(3, 5, 9), shape.Clamp(stored));
        Assert.Equal(25, stored.Day);
        Assert.Equal(D(3, 5, 4), shape.Clamp(D(3, 5, 4)));
    }

    // ---- Status ----

    private static readonly IScribeCalendarShape Nine = new UniformCalendarShape(9);

    [Fact]
    public void Classify_OverdueCount()
    {
        var status = ScribeDateRules.Classify(Task(D(3, 5, 2)), D(3, 5, 5), Nine);
        Assert.Equal(new ScribeDateStatus(ScribeDateStatusKind.Overdue, 3), status);
    }

    [Fact]
    public void Classify_CheckedPastTaskIsDone()
    {
        var status = ScribeDateRules.Classify(Task(D(3, 5, 2), done: true), D(3, 5, 5), Nine);
        Assert.Equal(new ScribeDateStatus(ScribeDateStatusKind.Done, 0), status);
    }

    [Fact]
    public void Classify_DueToday()
    {
        var status = ScribeDateRules.Classify(Task(D(3, 5, 5)), D(3, 5, 5), Nine);
        Assert.Equal(new ScribeDateStatus(ScribeDateStatusKind.DueToday, 0), status);
    }

    [Fact]
    public void Classify_UpcomingCount()
    {
        var status = ScribeDateRules.Classify(Task(D(3, 6, 1)), D(3, 5, 5), Nine);
        Assert.Equal(new ScribeDateStatus(ScribeDateStatusKind.Upcoming, 5), status);
    }

    [Fact]
    public void Classify_UpcomingAcrossAYearBoundary()
    {
        var status = ScribeDateRules.Classify(Task(D(3, 1, 1)), D(2, 12, 9), Nine);
        Assert.Equal(new ScribeDateStatus(ScribeDateStatusKind.Upcoming, 1), status);
    }

    [Fact]
    public void Classify_ClampedDayIsDueToday()
    {
        var status = ScribeDateRules.Classify(Task(D(3, 5, 25)), D(3, 5, 9), Nine);
        Assert.Equal(new ScribeDateStatus(ScribeDateStatusKind.DueToday, 0), status);
    }

    [Fact]
    public void Classify_UndatedTextAndQuestObjectiveHaveNoStatus()
    {
        var today = D(3, 5, 5);
        Assert.Null(ScribeDateRules.Classify(Task(null), today, Nine));
        var note = new ScribeBlock(ScribeBlockKind.Text, "Note", date: D(3, 5, 1));
        Assert.Null(ScribeDateRules.Classify(note, today, Nine));
        var objective = new ScribeBlock(ScribeBlockKind.QuestObjective, "", date: D(3, 5, 1));
        Assert.Null(ScribeDateRules.Classify(objective, today, Nine));
    }

    [Fact]
    public void Classify_DatedTrackerHasAStatus()
    {
        var tracker = new ScribeBlock(ScribeBlockKind.Tracker, "", targetItemCode: "game:log", date: D(3, 5, 1));
        Assert.Equal(ScribeDateStatusKind.Overdue, ScribeDateRules.Classify(tracker, D(3, 5, 5), Nine)!.Value.Kind);
    }

    // ---- Next occurrence ----

    [Fact]
    public void NextOccurrence_RollsOverAfterTheDatePasses()
    {
        var block = Task(D(3, 5, 5), done: true, repeat: true);
        Assert.Equal(D(4, 5, 5), ScribeDateRules.NextOccurrence(block, D(3, 5, 6), Nine));
    }

    [Fact]
    public void NextOccurrence_CheckedEarlyOrOnTheDay_WaitsForTheDateToPass()
    {
        Assert.Null(ScribeDateRules.NextOccurrence(Task(D(3, 5, 5), done: true, repeat: true), D(3, 5, 2), Nine));
        Assert.Null(ScribeDateRules.NextOccurrence(Task(D(3, 5, 5), done: true, repeat: true), D(3, 5, 5), Nine));
    }

    [Fact]
    public void NextOccurrence_LateRolloverLandsOnTheAnniversary()
    {
        var block = Task(D(3, 5, 5), done: true, repeat: true);
        var next = ScribeDateRules.NextOccurrence(block, D(4, 5, 5), Nine);

        Assert.Equal(D(4, 5, 5), next);
        var rolled = Task(next, done: false, repeat: true);
        Assert.Equal(ScribeDateStatusKind.DueToday, ScribeDateRules.Classify(rolled, D(4, 5, 5), Nine)!.Value.Kind);
    }

    [Fact]
    public void NextOccurrence_SeveralYearsSkipped()
    {
        var block = Task(D(3, 5, 5), done: true, repeat: true);
        Assert.Equal(D(7, 5, 5), ScribeDateRules.NextOccurrence(block, D(6, 6, 1), Nine));
    }

    [Fact]
    public void NextOccurrence_UncheckedPastDateStaysOverdue()
    {
        var block = Task(D(3, 5, 5), done: false, repeat: true);
        Assert.Null(ScribeDateRules.NextOccurrence(block, D(3, 5, 6), Nine));
        Assert.Equal(new ScribeDateStatus(ScribeDateStatusKind.Overdue, 1), ScribeDateRules.Classify(block, D(3, 5, 6), Nine));
    }

    [Fact]
    public void NextOccurrence_CarriesTheStoredDay()
    {
        var block = Task(D(3, 5, 25), done: true, repeat: true);
        var next = ScribeDateRules.NextOccurrence(block, D(3, 6, 1), Nine);

        Assert.Equal(D(4, 5, 25), next);
        Assert.Equal(D(4, 5, 9), Nine.Clamp(next!.Value));
    }

    [Fact]
    public void NextOccurrence_NonRepeatingOrNonCompletable_DoesNotRollOver()
    {
        var today = D(3, 5, 6);
        Assert.Null(ScribeDateRules.NextOccurrence(Task(D(3, 5, 5), done: true, repeat: false), today, Nine));
        var note = new ScribeBlock(ScribeBlockKind.Text, "Note", done: true, date: D(3, 5, 5), repeatYearly: true);
        Assert.Null(ScribeDateRules.NextOccurrence(note, today, Nine));
    }

    // ---- Block fields ----

    [Fact]
    public void ClearingTheDate_KeepsTheTaskId_AndClearsTheRepeatFlag()
    {
        var block = Task(D(3, 5, 5), repeat: true);
        var id = block.TaskId;

        block.Date = null;

        Assert.Equal(id, block.TaskId);
        Assert.False(block.RepeatYearly);
        block.Date = D(4, 1, 1);
        Assert.False(block.RepeatYearly);
    }

    [Fact]
    public void RepeatYearly_IsIgnoredWithoutADate()
    {
        var block = Task(null);
        block.RepeatYearly = true;
        Assert.False(block.RepeatYearly);
        Assert.False(new ScribeBlock(ScribeBlockKind.Task, "x", repeatYearly: true).RepeatYearly);
    }

    [Fact]
    public void CloneAndAppend_KeepTheDateAndRepeatFlag()
    {
        var source = new ScribeDocument();
        source.AddTask("Festival");
        source.Blocks[0].Date = D(3, 5, 5);
        source.Blocks[0].RepeatYearly = true;

        var clone = source.CloneWithNewIdentity();
        Assert.NotEqual(source.Blocks[0].TaskId, clone.Blocks[0].TaskId);
        Assert.Equal(D(3, 5, 5), clone.Blocks[0].Date);
        Assert.True(clone.Blocks[0].RepeatYearly);

        var target = new ScribeDocument();
        target.AddTask("Existing");
        target.AppendClonedBlocksFrom(source);
        Assert.Equal(D(3, 5, 5), target.Blocks[1].Date);
        Assert.True(target.Blocks[1].RepeatYearly);
    }

    // ---- Date order ----

    [Theory]
    [InlineData(ScribeDateOrder.LanguageDefault, "date-format")]
    [InlineData(ScribeDateOrder.DayMonthYear, "date-format-dmy")]
    [InlineData(ScribeDateOrder.MonthDayYear, "date-format-mdy")]
    [InlineData(ScribeDateOrder.YearMonthDay, "date-format-ymd")]
    [InlineData((ScribeDateOrder)99, "date-format")]
    public void TemplateKey_PicksTheKeyForEachOrder(ScribeDateOrder order, string key)
    {
        Assert.Equal(key, ScribeDateText.TemplateKey(order));
    }

    /// <summary>A shape that uses only the interface's general ordinal loop.</summary>
    private sealed class LoopOnlyShape(int daysPerMonth) : IScribeCalendarShape
    {
        public int DaysInMonth(int year, int month) => daysPerMonth;
    }
}
