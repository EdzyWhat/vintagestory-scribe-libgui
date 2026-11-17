using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Integration.Tests;

/// <summary>
/// The dated-task Mod adapter against a real world calendar: today's date and year numbering, the display
/// formatter versus the server's identity stamp, and the import validator keeping a date when it degrades
/// an unresolved block.
/// </summary>
public class DatedTaskScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public void WorldCalendar_TodayMatchesTheGameCalendar_WithYearsCountedFromOne()
    {
        var world = World.Api.World;
        var cal = world.Calendar;

        var today = ScribeWorldCalendar.Today(world);

        Assert.Equal((int)cal.MonthName, today.Month);
        // Calendar.Year counts whole years elapsed from 0 (it never adds IGameCalendar.StartYear).
        Assert.Equal(cal.Year + 1, today.Year);
        Assert.Equal(cal.DaysPerMonth, ScribeWorldCalendar.Shape(world).DaysPerMonth);
    }

    [AtlasScenario]
    public void FormatDate_ServerStamp_KeepsItsExistingForm()
    {
        var sapi = World.Api;
        var cal = sapi.World.Calendar;
        int dayOfMonth = (int)(cal.TotalDays % cal.DaysPerMonth) + 1;
        string expected = Lang.Get("scribe:date-format", dayOfMonth, Lang.Get("month-" + cal.MonthName), cal.Year);

        Assert.Equal(expected, NotebookHost.FormatDate(sapi));
    }

    [AtlasScenario]
    public void FormatDateFromTimestamp_FirstDay_ShowsYearOne_InEachOrder()
    {
        var world = World.Api.World;
        string january = Lang.Get("month-" + EnumMonth.January);

        Assert.Equal(Lang.Get("scribe:date-format", 1, january, 1),
            NotebookHost.FormatDateFromTimestamp(world, 0, ScribeDateOrder.LanguageDefault));
        Assert.Equal(Lang.Get("scribe:date-format-mdy", 1, january, 1),
            NotebookHost.FormatDateFromTimestamp(world, 0, ScribeDateOrder.MonthDayYear));
        Assert.Equal(Lang.Get("scribe:date-format-ymd", 1, january, 1),
            NotebookHost.FormatDateFromTimestamp(world, 0, ScribeDateOrder.YearMonthDay));
    }

    [AtlasScenario]
    public void ImportValidator_DegradedTracker_KeepsItsDate()
    {
        var doc = new ScribeDocument();
        doc.AddTracker("game:no-such-item-anywhere", 3);
        doc.Blocks[0].Date = ScribeDate.Create(3, 5, 25);
        doc.Blocks[0].RepeatYearly = true;

        var result = ScribeImportValidator.Validate(World.Api.World, doc);

        Assert.Equal(1, result.Degraded);
        var block = result.Document.Blocks[0];
        Assert.Equal(ScribeBlockKind.Task, block.Kind);
        Assert.Equal(ScribeDate.Create(3, 5, 25), block.Date);
        Assert.True(block.RepeatYearly);
    }
}
