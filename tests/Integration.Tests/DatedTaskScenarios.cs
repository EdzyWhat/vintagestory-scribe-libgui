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

    // ── Server date operations and completion ──────────────────────────────────────────

    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    /// <summary>Places a lectern holding a Task, a Note and a second Task, written by <paramref name="editor"/>
    /// (who then holds the editor lock).</summary>
    private async Task<(BlockEntityScribeLectern lectern, Guid docId, Guid taskId, Guid noteId)> SeedLectern(
        ITestPlayer editor, Vintagestory.API.MathTools.BlockPos pos)
    {
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(editor.Player, wantEditor: true, quickAdd: false);
        var doc = new ScribeDocument();
        doc.AddTask("Pay the toll");
        doc.ReplaceBlocks(doc.Blocks.Append(new ScribeBlock(ScribeBlockKind.Text, "Remember the toll")).ToList());
        Assert.True(lectern.ApplyEdit(editor.Player, ScribeDocumentCodec.Serialize(doc)));
        return (lectern, lectern.Document.DocId, lectern.Document.Blocks[0].TaskId, lectern.Document.Blocks[1].TaskId);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task SetDate_Clear_AndRepeat_ApplyToADatableTask_KeepingThePin()
    {
        var player = await World.JoinPlayer("DateOps1");
        var (lectern, docId, taskId, _) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));
        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetDate, 3, 5, 25);
        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetRepeat, repeat: true);
        var block = lectern.Document.FindByTaskId(taskId)!;
        Assert.Equal(ScribeDate.Create(3, 5, 25), block.Date);
        Assert.True(block.RepeatYearly);

        // Changing the date keeps the repeat flag.
        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetDate, 4, 1, 2);
        block = lectern.Document.FindByTaskId(taskId)!;
        Assert.Equal(ScribeDate.Create(4, 1, 2), block.Date);
        Assert.True(block.RepeatYearly);

        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.ClearDate);
        block = lectern.Document.FindByTaskId(taskId)!;
        Assert.Null(block.Date);
        Assert.False(block.RepeatYearly);
        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task SetDate_IsRefused_ForNotesUnknownTasksInvalidDatesAndUndatedRepeat()
    {
        var player = await World.JoinPlayer("DateOps2");
        var (lectern, docId, taskId, noteId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.SetTaskDateForPlayer(player.Player, docId, noteId, ScribeDateOperation.SetDate, 1, 1, 1);
        Mod.SetTaskDateForPlayer(player.Player, docId, Guid.NewGuid(), ScribeDateOperation.SetDate, 1, 1, 1);
        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetDate, 0, 13, 300);
        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetRepeat, repeat: true);

        Assert.Null(lectern.Document.FindByTaskId(noteId)!.Date);
        var task = lectern.Document.FindByTaskId(taskId)!;
        Assert.Null(task.Date);
        Assert.False(task.RepeatYearly);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task SetDate_IsRefused_WhileAnotherPlayerHoldsTheEditorLock()
    {
        var holder = await World.JoinPlayer("DateHolder");
        var other = await World.JoinPlayer("DateOther");
        var (lectern, docId, taskId, _) = await SeedLectern(holder, World.Spawn.Offset(5, 0, 0));

        Mod.SetTaskDateForPlayer(other.Player, docId, taskId, ScribeDateOperation.SetDate, 2, 2, 2);

        Assert.Null(lectern.Document.FindByTaskId(taskId)!.Date);

        // The holder is not "another player" to themselves.
        Mod.SetTaskDateForPlayer(holder.Player, docId, taskId, ScribeDateOperation.SetDate, 2, 2, 2);
        Assert.Equal(ScribeDate.Create(2, 2, 2), lectern.Document.FindByTaskId(taskId)!.Date);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task EditorSave_KeepsADateAndRepeatFlag()
    {
        var player = await World.JoinPlayer("DateOps4");
        var (lectern, _, taskId, _) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Assert.True(ScribeDocumentCodec.TryDeserialize(ScribeDocumentCodec.Serialize(lectern.Document), out var doc));
        doc!.TrySetTaskDate(taskId, ScribeDate.Create(7, 12, 31));
        doc.TrySetTaskRepeat(taskId, true);
        Assert.True(lectern.ApplyEdit(player.Player, ScribeDocumentCodec.Serialize(doc)));

        var block = lectern.Document.FindByTaskId(taskId)!;
        Assert.Equal(ScribeDate.Create(7, 12, 31), block.Date);
        Assert.True(block.RepeatYearly);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task CompletingARepeatingTask_UnderDelete_DeletesIt_AndLeavesTheOtherTasks()
    {
        var player = await World.JoinPlayer("DateRepeat1");
        var (lectern, docId, taskId, noteId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));
        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);
        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetDate, 1, 1, 1);
        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetRepeat, repeat: true);

        Mod.CompleteTaskForPlayer(player.Player, docId, taskId, ScribeCompletionPolicy.Delete);

        Assert.Null(lectern.Document.FindByTaskId(taskId));
        Assert.NotNull(lectern.Document.FindByTaskId(noteId));
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    [AtlasScenario]
    public void ChipColors_AreFourDistinctValues()
    {
        var colors = new[]
        {
            ScribeRowConstants.DateChipDue, ScribeRowConstants.DateChipOverdue,
            ScribeRowConstants.DateChipUpcoming, ScribeRowConstants.DateChipDone,
        };
        Assert.Equal(4, colors.Distinct().Count());
        Assert.Equal(ScribeRowConstants.DateChipOverdue, ScribeRowConstants.DateChipColor(ScribeDateStatusKind.Overdue));
    }
}
