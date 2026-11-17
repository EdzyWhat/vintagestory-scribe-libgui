using Scribe.Core;
using static Scribe.Core.ScribeRowMenuAction;

namespace Scribe.Core.Tests;

// Tests for dating plain tasks: menu entry gating, the document date operations, the date picker's stepping
// rules, the chip text mapping and completion of dated tasks (notebook-task-dating spec).
public class ScribeDatingTests
{
    private static ScribeDate D(int year, int month, int day) => ScribeDate.Create(year, month, day);

    private static ScribeBlock Task(string text, int depth = 0, ScribeDate? date = null, bool repeat = false, bool done = false) =>
        new(ScribeBlockKind.Task, text, done: done, depth: depth, date: date, repeatYearly: repeat);

    private static ScribeDocument DocOf(params ScribeBlock[] blocks)
    {
        var doc = new ScribeDocument();
        doc.ReplaceBlocks(blocks);
        return doc;
    }

    private static IEnumerable<string> Texts(ScribeDocument doc) => doc.Blocks.Select(b => b.Text);

    // ---- Datable ----

    [Fact]
    public void IsDatable_OnlyAPlainTaskWithoutAnAssignment()
    {
        Assert.True(ScribeDateRules.IsDatable(Task("t")));
        Assert.False(ScribeDateRules.IsDatable(new ScribeBlock(ScribeBlockKind.Text, "n")));
        Assert.False(ScribeDateRules.IsDatable(new ScribeBlock(ScribeBlockKind.Tracker, "", targetItemCode: "game:log")));
        var assigned = Task("t");
        assigned.Assignment = new ScribeAssignment("uid", "Name");
        Assert.False(ScribeDateRules.IsDatable(assigned));
    }

    // ---- Menu entries ----

    [Theory]
    [InlineData(ScribeRowMenuView.Read, false, false, new[] { Edit, Pin, AddDate, Delete })]
    [InlineData(ScribeRowMenuView.Read, false, true, new[] { Edit, Pin, ChangeDate, ClearDate, RepeatYearly, Delete })]
    [InlineData(ScribeRowMenuView.Editor, false, false, new[] { Pin, AddDate, Delete })]
    [InlineData(ScribeRowMenuView.Editor, false, true, new[] { Pin, ChangeDate, ClearDate, RepeatYearly, Delete })]
    public void CanDate_OffersTheDateGroupBetweenPinAndDelete(
        ScribeRowMenuView view, bool readOnly, bool hasDate, ScribeRowMenuAction[] expected)
    {
        Assert.Equal(expected,
            ScribeRowMenuEntries.For(view, readOnly, pinLive: true, isPinned: false, canDate: true, hasDate: hasDate));
    }

    [Fact]
    public void CannotDate_OffersNoDateEntries()
    {
        Assert.Equal(new[] { Edit, Pin, Delete },
            ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: false, pinLive: true, isPinned: false, canDate: false, hasDate: true));
    }

    [Fact]
    public void ReadOnly_NeverOffersDateEntries()
    {
        Assert.Equal(new[] { Pin },
            ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: true, pinLive: true, isPinned: false, canDate: true, hasDate: false));
    }

    [Theory]
    [InlineData(false, new[] { Unpin, AddDate, Delete })]
    [InlineData(true, new[] { Unpin, ChangeDate, ClearDate, Delete })]
    public void PinTab_CanDate_OffersTheDateGroupBetweenUnpinAndDelete(bool hasDate, ScribeRowMenuAction[] expected)
    {
        Assert.Equal(expected, ScribeRowMenuEntries.For(ScribeRowMenuView.PinTab, readOnly: false, pinLive: true,
            isPinned: true, canDate: true, hasDate: hasDate, allowRepeat: false));
    }

    [Fact]
    public void PinTab_CannotDate_OffersOnlyUnpinAndDelete()
    {
        Assert.Equal(new[] { Unpin, Delete },
            ScribeRowMenuEntries.For(ScribeRowMenuView.PinTab, readOnly: false, pinLive: true, isPinned: true,
                canDate: false, hasDate: true, allowRepeat: false));
    }

    [Fact]
    public void AllowRepeatFalse_OmitsRepeatYearlyEverywhere()
    {
        Assert.Equal(new[] { Edit, Pin, ChangeDate, ClearDate, Delete },
            ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: false, pinLive: true, isPinned: false,
                canDate: true, hasDate: true, allowRepeat: false));
    }

    [Fact]
    public void DateEntriesStayWhenPinIsNotLive()
    {
        Assert.Equal(new[] { Edit, AddDate, Delete },
            ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: false, pinLive: false, isPinned: false, canDate: true, hasDate: false));
    }

    // ---- Document operations ----

    [Fact]
    public void SetDate_KeepsIdentityAndRepeatFlag()
    {
        var doc = DocOf(Task("Festival", date: D(3, 5, 5), repeat: true));
        var id = doc.Blocks[0].TaskId;

        Assert.True(doc.TrySetTaskDate(id, D(4, 6, 7)));

        Assert.Equal(id, doc.Blocks[0].TaskId);
        Assert.Equal(D(4, 6, 7), doc.Blocks[0].Date);
        Assert.True(doc.Blocks[0].RepeatYearly);
        Assert.False(doc.TrySetTaskDate(id, D(4, 6, 7)));
    }

    [Fact]
    public void ClearDate_DropsTheRepeatFlagAndKeepsIdentity()
    {
        var doc = DocOf(Task("Festival", date: D(3, 5, 5), repeat: true));
        var id = doc.Blocks[0].TaskId;

        Assert.True(doc.TryClearTaskDate(id));

        Assert.Equal(id, doc.Blocks[0].TaskId);
        Assert.Null(doc.Blocks[0].Date);
        Assert.False(doc.Blocks[0].RepeatYearly);
        Assert.False(doc.TryClearTaskDate(id));
    }

    [Fact]
    public void SetRepeat_NeedsADate()
    {
        var doc = DocOf(Task("Plain"), Task("Dated", date: D(1, 1, 1)));

        Assert.False(doc.TrySetTaskRepeat(doc.Blocks[0].TaskId, true));
        Assert.False(doc.Blocks[0].RepeatYearly);
        Assert.True(doc.TrySetTaskRepeat(doc.Blocks[1].TaskId, true));
        Assert.True(doc.Blocks[1].RepeatYearly);
        Assert.False(doc.TrySetTaskRepeat(doc.Blocks[1].TaskId, true));
        Assert.True(doc.TrySetTaskRepeat(doc.Blocks[1].TaskId, false));
    }

    [Fact]
    public void Operations_RefuseNonTasksAssignedTasksAndUnknownIds()
    {
        var note = new ScribeBlock(ScribeBlockKind.Text, "n");
        var assigned = Task("a");
        assigned.Assignment = new ScribeAssignment("uid", "Name");
        var doc = DocOf(note, assigned);

        Assert.False(doc.TrySetTaskDate(note.TaskId, D(1, 1, 1)));
        Assert.False(doc.TrySetTaskDate(assigned.TaskId, D(1, 1, 1)));
        Assert.False(doc.TrySetTaskDate(Guid.NewGuid(), D(1, 1, 1)));
        Assert.False(doc.TryClearTaskDate(Guid.NewGuid()));
        Assert.False(doc.TrySetTaskRepeat(Guid.NewGuid(), true));
        Assert.Null(note.Date);
        Assert.Null(assigned.Date);
    }

    // ---- Date picker ----

    private static readonly IScribeCalendarShape Nine = new UniformCalendarShape(9);

    [Fact]
    public void Picker_Reset_ReturnsToTheGivenDate_ClampingTheDay()
    {
        var model = new ScribeDatePickerModel(Nine, D(7, 2, 3));

        model.Reset(D(3, 5, 25));
        Assert.Equal(D(3, 5, 9), model.ToDate());
    }

    private sealed class UnevenShape : IScribeCalendarShape
    {
        public int DaysInMonth(int year, int month) => month == 2 ? 5 : 9;
    }

    [Fact]
    public void Picker_DayStopsAtOneAndAtTheMonthLength()
    {
        var model = new ScribeDatePickerModel(Nine, D(3, 5, 1));
        Assert.False(model.StepDay(-1));
        Assert.Equal(1, model.Day);

        var last = new ScribeDatePickerModel(Nine, D(3, 5, 9));
        Assert.False(last.StepDay(1));
        Assert.Equal(9, last.Day);
        Assert.Equal(5, last.Month);
        Assert.True(last.StepDay(-1));
        Assert.Equal(8, last.Day);
    }

    [Fact]
    public void Picker_MonthCarriesIntoTheYear()
    {
        var model = new ScribeDatePickerModel(Nine, D(3, 12, 4));
        Assert.True(model.StepMonth(1));
        Assert.Equal((4, 1), (model.Year, model.Month));
        Assert.True(model.StepMonth(-1));
        Assert.Equal((3, 12), (model.Year, model.Month));
    }

    [Fact]
    public void Picker_MonthBlockedAtTheEnds()
    {
        var first = new ScribeDatePickerModel(Nine, D(1, 1, 1));
        Assert.False(first.StepMonth(-1));
        Assert.Equal(D(1, 1, 1), first.ToDate());

        var last = new ScribeDatePickerModel(Nine, D(ScribeDate.MaxYear, 12, 1));
        Assert.False(last.StepMonth(1));
        Assert.Equal(D(ScribeDate.MaxYear, 12, 1), last.ToDate());
    }

    [Fact]
    public void Picker_YearDoesNotWrap()
    {
        var first = new ScribeDatePickerModel(Nine, D(1, 5, 5));
        Assert.False(first.StepYear(-1));
        Assert.Equal(1, first.Year);

        var last = new ScribeDatePickerModel(Nine, D(ScribeDate.MaxYear, 5, 5));
        Assert.False(last.StepYear(1));
        Assert.Equal(ScribeDate.MaxYear, last.Year);
    }

    [Fact]
    public void Picker_StartingYearAboveTheMaximumStartsAtTheMaximum()
    {
        var tooFar = ScribeDate.TryCreate(ScribeDate.MaxYear + 1, 5, 5, out var date) ? date : default;
        Assert.Equal(default, tooFar);
        var model = new ScribeDatePickerModel(Nine, D(ScribeDate.MaxYear, 5, 5));
        Assert.Equal(ScribeDate.MaxYear, model.Year);
    }

    [Fact]
    public void Picker_StartingDayPastTheMonthEndIsClamped()
    {
        var model = new ScribeDatePickerModel(Nine, D(3, 5, 25));
        Assert.Equal(9, model.Day);
        Assert.Equal(D(3, 5, 9), model.ToDate());
    }

    [Fact]
    public void Picker_UnevenShape_DayClampsDownOnAMonthChangeAndDoesNotReturn()
    {
        var model = new ScribeDatePickerModel(new UnevenShape(), D(3, 1, 9));
        Assert.True(model.StepMonth(1));
        Assert.Equal((2, 5), (model.Month, model.Day));
        Assert.True(model.StepMonth(-1));
        Assert.Equal((1, 5), (model.Month, model.Day));
    }

    // ---- Chip text ----

    [Theory]
    [InlineData(ScribeDateStatusKind.DueToday, 0, "scribe-datestatus-today", "scribe-datechip")]
    [InlineData(ScribeDateStatusKind.Upcoming, 1, "scribe-datestatus-upcoming-one", "scribe-datechip")]
    [InlineData(ScribeDateStatusKind.Upcoming, 3, "scribe-datestatus-upcoming-other", "scribe-datechip")]
    [InlineData(ScribeDateStatusKind.Overdue, 1, "scribe-datestatus-overdue-one", "scribe-datechip")]
    [InlineData(ScribeDateStatusKind.Overdue, 2, "scribe-datestatus-overdue-other", "scribe-datechip")]
    [InlineData(ScribeDateStatusKind.Done, 0, null, "scribe-datechip-done")]
    public void ChipText_MapsEachStatus(ScribeDateStatusKind kind, int days, string? statusKey, string template)
    {
        var text = ScribeDateChipText.For(new ScribeDateStatus(kind, days));

        Assert.Equal(statusKey, text.StatusLangKey);
        Assert.Equal(template, text.TemplateLangKey);
        Assert.Equal(kind is ScribeDateStatusKind.Upcoming or ScribeDateStatusKind.Overdue ? days : 0, text.Count);
    }

    [Fact]
    public void ChipText_ClampedStoredDayIsDueToday()
    {
        var status = ScribeDateRules.Classify(Task("t", date: D(3, 5, 25)), D(3, 5, 9), Nine);
        Assert.Equal("scribe-datestatus-today", ScribeDateChipText.For(status!.Value).StatusLangKey);
    }

    // ---- Completion ignores the date and repeat flag ----

    private static readonly ScribeDate Day = D(3, 5, 5);

    private static ScribeCompletion.LocalOutcome Complete(
        ScribeDocument doc, int index, ScribeCompletionPolicy policy,
        ScribeSubtaskBehavior behavior = ScribeSubtaskBehavior.Bound) =>
        ScribeCompletion.ApplyLocal(doc, doc.Blocks[index].TaskId, policy, behavior);

    [Fact]
    public void Delete_DeletesADatedRepeatingTask_AndLeavesOtherTasks()
    {
        var doc = DocOf(Task("Festival", date: Day, repeat: true), Task("Festival", date: D(3, 6, 5), repeat: true), Task("Other"));

        var outcome = Complete(doc, 0, ScribeCompletionPolicy.Delete);

        Assert.Equal(new[] { "Festival", "Other" }, Texts(doc));
        Assert.True(outcome.ShouldRemovePin);
        Assert.Single(outcome.DeletedTaskIds);
    }

    [Fact]
    public void Delete_DeletesADatedTask_WithoutRepeat()
    {
        var doc = DocOf(Task("Plain", date: Day), Task("Other"));
        Complete(doc, 0, ScribeCompletionPolicy.Delete);
        Assert.Equal(new[] { "Other" }, Texts(doc));
    }

    [Fact]
    public void Clipboard_JsonRoundTrip_KeepsDateAndRepeatFlag()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Pay the toll");
        var id = doc.Blocks[0].TaskId;
        doc.TrySetTaskDate(id, ScribeDate.Create(3, 5, 25));
        doc.TrySetTaskRepeat(id, true);

        Assert.True(ScribeDocumentJsonCodec.TryDeserialize(ScribeDocumentJsonCodec.Serialize(doc), out var back));

        Assert.Equal(ScribeDate.Create(3, 5, 25), back!.Blocks[0].Date);
        Assert.True(back.Blocks[0].RepeatYearly);
    }

    [Fact]
    public void ClearingADate_DoesNotTouchTheTaskIdentity()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Pay the toll");
        var id = doc.Blocks[0].TaskId;
        doc.TrySetTaskDate(id, ScribeDate.Create(1, 1, 1));

        Assert.True(doc.TryClearTaskDate(id));

        Assert.Equal(id, doc.Blocks[0].TaskId);
    }
}
