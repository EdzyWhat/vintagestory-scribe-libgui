using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.MathTools;

namespace Integration.Tests;

/// <summary>
/// The date a pin snapshot carries: captured when pinning (resolved document, the client's fallback fields, and
/// the server's validation of those), refreshed for every holder when a date changes, and completion of a
/// dated pin.
/// </summary>
public class PinnedTaskDateScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    private async Task<(BlockEntityScribeLectern lectern, Guid docId, Guid taskId, Guid noteId)> SeedLectern(
        ITestPlayer editor, BlockPos pos)
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

    private ScribePinnedRef PinOf(ITestPlayer player, Guid docId, Guid taskId) =>
        Mod.PinStore!.GetPin(player.Player.PlayerUID, docId, taskId)!;

    // ── Capture ─────────────────────────────────────────────────────────────────────────────────────

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pin_OfADatedTask_CapturesTheResolvedDocumentsDate()
    {
        var player = await World.JoinPlayer("PinDate1");
        var (_, docId, taskId, _) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));
        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetDate, 3, 5, 25);

        // The client sends a different date; the resolved document wins.
        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true,
            fallbackDateYear: 9, fallbackDateMonth: 9, fallbackDateDay: 9);

        var pin = PinOf(player, docId, taskId);
        Assert.Equal(ScribeDate.Create(3, 5, 25), pin.Date);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pin_OfAnUndatedTask_HasNoDate()
    {
        var player = await World.JoinPlayer("PinDate2");
        var (_, docId, taskId, _) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        var pin = PinOf(player, docId, taskId);
        Assert.Null(pin.Date);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pin_OfAnUnresolvableTask_TakesTheClientsValidDate_AndAnOldClientSendsNone()
    {
        var player = await World.JoinPlayer("PinDate3");
        var docId = Guid.NewGuid();
        var withDate = Guid.NewGuid();
        var oldClient = Guid.NewGuid();

        Mod.SetPinForPlayer(player.Player, docId, withDate, pinned: true, fallbackText: "Far task",
            fallbackDateYear: 4, fallbackDateMonth: 2, fallbackDateDay: 9);
        Mod.SetPinForPlayer(player.Player, docId, oldClient, pinned: true, fallbackText: "Old client task");

        var dated = PinOf(player, docId, withDate);
        Assert.Equal(ScribeDate.Create(4, 2, 9), dated.Date);
        Assert.Null(PinOf(player, docId, oldClient).Date);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pin_OfAnUnresolvableTask_TreatsAnInvalidOrPartialDateAsUndated()
    {
        var player = await World.JoinPlayer("PinDate4");
        var docId = Guid.NewGuid();
        var partial = Guid.NewGuid();
        var tracker = Guid.NewGuid();

        Mod.SetPinForPlayer(player.Player, docId, partial, pinned: true, fallbackText: "a",
            fallbackDateYear: 5, fallbackDateMonth: 0, fallbackDateDay: 3);
        Mod.SetPinForPlayer(player.Player, docId, tracker, pinned: true, fallbackText: "c",
            fallbackKind: ScribeBlockKind.Tracker, fallbackTargetItemCode: "game:log",
            fallbackDateYear: 4, fallbackDateMonth: 2, fallbackDateDay: 9);

        foreach (var id in new[] { partial, tracker })
        {
            var pin = PinOf(player, docId, id);
            Assert.Null(pin.Date);
        }
    }

    // ── Dating from the Pin Tab ─────────────────────────────────────────────────────────────────────

    [AtlasScenario(RollbackWorld = true)]
    public async Task DateOperationsByIdentity_UpdateTheHoldersPin_AndAreRefusedWhileAnotherPlayerEdits()
    {
        var holder = await World.JoinPlayer("PinTabDateA");
        var pinner = await World.JoinPlayer("PinTabDateB");
        var (lectern, docId, taskId, _) = await SeedLectern(holder, World.Spawn.Offset(5, 0, 0));
        Mod.SetPinForPlayer(pinner.Player, docId, taskId, pinned: true);

        Mod.SetTaskDateForPlayer(holder.Player, docId, taskId, ScribeDateOperation.SetDate, 3, 5, 25);
        Assert.Equal(ScribeDate.Create(3, 5, 25), PinOf(pinner, docId, taskId).Date);

        Mod.SetTaskDateForPlayer(holder.Player, docId, taskId, ScribeDateOperation.SetDate, 4, 1, 2);
        Assert.Equal(ScribeDate.Create(4, 1, 2), PinOf(pinner, docId, taskId).Date);

        // The pinning player does not hold the editor lock, so the change is refused and the pin keeps its date.
        Mod.SetTaskDateForPlayer(pinner.Player, docId, taskId, ScribeDateOperation.ClearDate);
        Assert.Equal(ScribeDate.Create(4, 1, 2), lectern.Document.FindByTaskId(taskId)!.Date);
        Assert.Equal(ScribeDate.Create(4, 1, 2), PinOf(pinner, docId, taskId).Date);

        Mod.SetTaskDateForPlayer(holder.Player, docId, taskId, ScribeDateOperation.ClearDate);
        Assert.Null(PinOf(pinner, docId, taskId).Date);
    }

    // ── Refresh ─────────────────────────────────────────────────────────────────────────────────────

    [AtlasScenario(RollbackWorld = true)]
    public async Task DateChanges_FollowOnEveryHoldersPin_WithoutTouchingTheirTextDoneOrOrder()
    {
        var a = await World.JoinPlayer("PinDateA");
        var b = await World.JoinPlayer("PinDateB");
        var (lectern, docId, taskId, noteId) = await SeedLectern(a, World.Spawn.Offset(5, 0, 0));
        Mod.SetPinForPlayer(a.Player, docId, taskId, pinned: true);
        Mod.SetPinForPlayer(b.Player, docId, noteId, pinned: true);
        Mod.SetPinForPlayer(b.Player, docId, taskId, pinned: true);
        var orderBefore = Mod.PinStore!.Get(b.Player.PlayerUID).Select(p => p.TaskId).ToList();

        // A's snapshot of the text/done differs from B's after B's own pin is nudged.
        Mod.PinStore.SetPinDone(b.Player.PlayerUID, docId, taskId, true);

        Mod.SetTaskDateForPlayer(a.Player, docId, taskId, ScribeDateOperation.SetDate, 3, 5, 25);
        Assert.Equal(ScribeDate.Create(3, 5, 25), PinOf(b, docId, taskId).Date);
        Assert.Equal(ScribeDate.Create(3, 5, 25), PinOf(a, docId, taskId).Date);

        Mod.SetTaskDateForPlayer(a.Player, docId, taskId, ScribeDateOperation.SetDate, 4, 1, 2);
        Assert.Equal(ScribeDate.Create(4, 1, 2), PinOf(b, docId, taskId).Date);

        Mod.SetTaskDateForPlayer(a.Player, docId, taskId, ScribeDateOperation.ClearDate);
        Assert.Null(PinOf(b, docId, taskId).Date);

        // B's done snapshot (set above) and text are B's own; the refresh left them alone.
        Assert.True(PinOf(b, docId, taskId).LastKnownDone);
        Assert.Equal("Pay the toll", PinOf(b, docId, taskId).LastKnownText);
        Assert.Equal(orderBefore, Mod.PinStore.Get(b.Player.PlayerUID).Select(p => p.TaskId).ToList());
        Assert.False(lectern.Document.FindByTaskId(taskId)!.Done);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task DateChange_ReachesAnOfflineHoldersStoredPin()
    {
        var a = await World.JoinPlayer("PinDateOnl");
        var (_, docId, taskId, _) = await SeedLectern(a, World.Spawn.Offset(5, 0, 0));
        const string offlineUid = "offline-holder-uid";
        Mod.PinStore!.SetPin(offlineUid, docId, taskId, 1, "Pay the toll", false);

        Mod.SetTaskDateForPlayer(a.Player, docId, taskId, ScribeDateOperation.SetDate, 2, 2, 2);

        Assert.Equal(ScribeDate.Create(2, 2, 2), Mod.PinStore.GetPin(offlineUid, docId, taskId)!.Date);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task EditorSave_RefreshesOtherHoldersPinDates()
    {
        var a = await World.JoinPlayer("PinDateEdA");
        var b = await World.JoinPlayer("PinDateEdB");
        var (lectern, docId, taskId, _) = await SeedLectern(a, World.Spawn.Offset(5, 0, 0));
        Mod.SetPinForPlayer(b.Player, docId, taskId, pinned: true);

        Assert.True(ScribeDocumentCodec.TryDeserialize(ScribeDocumentCodec.Serialize(lectern.Document), out var doc));
        doc!.TrySetTaskDate(taskId, ScribeDate.Create(7, 12, 31));
        Assert.True(lectern.ApplyEdit(a.Player, ScribeDocumentCodec.Serialize(doc)));

        var pin = PinOf(b, docId, taskId);
        Assert.Equal(ScribeDate.Create(7, 12, 31), pin.Date);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task ActingPlayersSavedEdit_RefreshesTheirPinDate_AndLeavesAnotherHoldersTextAlone()
    {
        var a = await World.JoinPlayer("PinDateAct1");
        var b = await World.JoinPlayer("PinDateAct2");
        var (lectern, docId, taskId, _) = await SeedLectern(a, World.Spawn.Offset(5, 0, 0));
        Mod.SetPinForPlayer(a.Player, docId, taskId, pinned: true);
        Mod.SetPinForPlayer(b.Player, docId, taskId, pinned: true);

        Assert.True(ScribeDocumentCodec.TryDeserialize(ScribeDocumentCodec.Serialize(lectern.Document), out var doc));
        doc!.Blocks[0].Text = "Pay the toll twice";
        doc.TrySetTaskDate(taskId, ScribeDate.Create(1, 1, 1));
        Assert.True(lectern.ApplyEdit(a.Player, ScribeDocumentCodec.Serialize(doc)));

        Assert.Equal(ScribeDate.Create(1, 1, 1), PinOf(a, docId, taskId).Date);
        Assert.Equal("Pay the toll twice", PinOf(a, docId, taskId).LastKnownText);
        Assert.Equal(ScribeDate.Create(1, 1, 1), PinOf(b, docId, taskId).Date);
        Assert.Equal("Pay the toll", PinOf(b, docId, taskId).LastKnownText);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task DateRefresh_LeavesAPinWhoseTaskIsGoneAlone()
    {
        var a = await World.JoinPlayer("PinDateGone");
        var (lectern, docId, taskId, _) = await SeedLectern(a, World.Spawn.Offset(5, 0, 0));
        var goneTask = Guid.NewGuid();
        Mod.PinStore!.SetPin(a.Player.PlayerUID, docId, goneTask, 1, "Gone", false, date: ScribeDate.Create(5, 5, 5));

        var changed = Mod.PinStore.RefreshDatesForDocument(docId, lectern.Document);

        Assert.Empty(changed);
        var pin = PinOf(a, docId, goneTask);
        Assert.False(pin.Orphaned);
        Assert.Equal(ScribeDate.Create(5, 5, 5), pin.Date);
        Assert.NotNull(PinOf(a, docId, goneTask));
        _ = taskId;
    }

    // ── Completion ──────────────────────────────────────────────────────────────────────────────────

    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_ADatedRepeatingTask_UnderDelete_DeletesIt_ResolvedOrNot()
    {
        var player = await World.JoinPlayer("PinDateDel");
        var (lectern, docId, taskId, _) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));
        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetDate, 1, 1, 1);
        Mod.SetTaskDateForPlayer(player.Player, docId, taskId, ScribeDateOperation.SetRepeat, repeat: true);
        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);
        var unresolvedDoc = Guid.NewGuid();
        var unresolved = Guid.NewGuid();
        Mod.SetPinForPlayer(player.Player, unresolvedDoc, unresolved, pinned: true, fallbackText: "Yearly",
            fallbackDateYear: 1, fallbackDateMonth: 1, fallbackDateDay: 1);

        Mod.CompleteTaskForPlayer(player.Player, docId, taskId, ScribeCompletionPolicy.Delete);
        Mod.CompleteTaskForPlayer(player.Player, unresolvedDoc, unresolved, ScribeCompletionPolicy.Delete);

        Assert.Null(lectern.Document.FindByTaskId(taskId));
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
        Assert.False(Mod.PinStore.IsPinned(player.Player.PlayerUID, unresolvedDoc, unresolved));
    }

    // ── Status classification ───────────────────────────────────────────────────────────────────────

    [AtlasScenario]
    public void Classification_AgainstTheWorldsToday_ForEachStatus()
    {
        var world = World.Api.World;
        var today = ScribeWorldCalendar.Today(world);
        var shape = ScribeWorldCalendar.Shape(world);
        var tomorrow = shape.FromOrdinal(shape.ToOrdinal(today) + 1);
        var yesterday = shape.FromOrdinal(Math.Max(0, shape.ToOrdinal(today) - 1));

        Assert.Equal(ScribeDateStatusKind.DueToday, ScribeDateRules.Classify(today, false, true, today, shape)!.Value.Kind);
        Assert.Equal(ScribeDateStatusKind.Upcoming, ScribeDateRules.Classify(tomorrow, false, true, today, shape)!.Value.Kind);
        if (shape.ToOrdinal(today) > 0)
            Assert.Equal(ScribeDateStatusKind.Overdue, ScribeDateRules.Classify(yesterday, false, true, today, shape)!.Value.Kind);
        Assert.Equal(ScribeDateStatusKind.Done, ScribeDateRules.Classify(today, true, true, today, shape)!.Value.Kind);
    }
}
