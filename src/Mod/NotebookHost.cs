using System;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// <see cref="IScribeDocumentHost"/> adapter for the player-held Notebook item. Wraps the
/// player's held <see cref="ItemSlot"/> and persists the document into
/// <c>ItemStack.Attributes["scribeDocument"]</c> via <see cref="ScribeDocumentAttributes"/>.
///
/// Registered into <see cref="ScribeModSystem"/>'s host registry when the notebook dialog
/// opens (client side) and also during server-side save handling (implicitly via the ItemStack).
/// Owner-only: <see cref="IsLockedByOther"/> always returns false because only one player
/// can hold the item at a time.
/// </summary>
public sealed class NotebookHost : IScribeDocumentHost
{
    private readonly ItemSlot _slot;
    private ScribeDocument _document;
    private HistoryStore _history;
    private ICoreServerAPI? _sapi;
    private IServerPlayer? _player;

    public NotebookHost(ItemSlot slot)
    {
        _slot = slot;
        var stack = slot.Itemstack!;
        if (!ScribeDocumentAttributes.TryReadFrom(stack, out var doc) || doc is null)
        {
            doc = new ScribeDocument();
            ScribeDocumentAttributes.WriteTo(stack, doc);
        }
        _document = doc;
        _history = HistoryStore.Deserialize(stack.Attributes.GetBytes("scribeHistory"));
    }

    /// <summary>Attach server context so write-through operations can push the updated document
    /// back to the player's client after mutating the ItemStack. Also records a PickedUp entry
    /// the first time this player opens the notebook.</summary>
    public void AttachServerContext(ICoreServerAPI sapi, IServerPlayer player)
    {
        _sapi = sapi;
        _player = player;
        RecordPickedUpIfNew(sapi, player);
    }

    public ScribeDocument Document => _document;

    /// <summary>The notebook's history chronicle. Persisted in <c>ItemStack.Attributes["scribeHistory"]</c>
    /// and flushed alongside the document in <see cref="Flush"/>.</summary>
    public HistoryStore History => _history;

    public bool IsLockedByOther(string viewerUid) => false;

    public void ApplyLocalOptimisticEdit(ScribeDocument doc) => _document = doc;

    /// <summary>Replaces the in-memory history store from freshly deserialized bytes pushed by the
    /// server. Used by the client-side network receive path to refresh the History tab.</summary>
    public void ApplyHistoryUpdate(byte[]? historyBytes)
    {
        if (historyBytes is not null)
            _history = HistoryStore.Deserialize(historyBytes);
    }

    public ScribeBackdropSpec BackdropSpec => ScribeBackdrops.LecternPage;

    public ScribeLayout GetLayout(float pixelArtSize) => new ScribeLayout(pixelArtSize, 1160f / 1024f);

    public string DefaultDocumentTitle => "Notebook";

    /// <summary>The Notebook has no guestbook. This property is never called because
    /// <see cref="GuiDialogScribeNotebook"/> does not add a Visitors nav button.</summary>
    public GuestbookStore Guestbook => throw new NotSupportedException("Notebook has no guestbook.");

    // ── Server-side write-through — mutate the in-memory document then persist to the ItemStack ──

    public void SetTaskDoneFromReader(Guid taskId, bool done)
    {
        var block = _document.FindByTaskId(taskId);
        if (block is null || !block.IsTask || block.Done == done) return;
        block.Done = done;
        Flush();
    }

    public bool DeleteTaskFromReader(Guid taskId)
    {
        for (int i = 0; i < _document.Blocks.Count; i++)
        {
            if (_document.Blocks[i].TaskId == taskId && _document.Blocks[i].IsTask)
            {
                _document.DeleteBlock(i);
                Flush();
                return true;
            }
        }
        return false;
    }

    public bool MoveTaskToBottomFromReader(Guid taskId)
    {
        if (!_document.MoveTaskToBottom(taskId)) return false;
        Flush();
        return true;
    }

    public bool SetTaskTextFromReader(Guid taskId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!_document.SetTaskText(taskId, text)) return false;
        Flush();
        return true;
    }

    /// <summary>Writes the document AND history store back to the ItemStack, marks the slot dirty, and
    /// pushes a full sync to the player's client. Public so server-side tools (e.g. the demo seeder)
    /// can persist seeded content through the normal flow — mirrors the already-public
    /// <see cref="FlushHistory"/>.</summary>
    public void Flush()
    {
        if (_slot.Itemstack is not { } stack) return;
        ScribeDocumentAttributes.WriteTo(stack, _document);
        stack.Attributes.SetBytes("scribeHistory", _history.Serialize());
        _slot.MarkDirty();
        // Push the updated document (and history bytes) back to the player's client.
        if (_sapi is not null && _player is not null)
        {
            _sapi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
            {
                DocIdBytes = _document.DocId.ToByteArray(),
                DocumentBytes = ScribeDocumentCodec.Serialize(_document),
                HistoryBytes = _history.Serialize(),
            }, _player);
        }
    }

    /// <summary>Writes only the history store back to the ItemStack and pushes a sync to the client.
    /// Cheaper than <see cref="Flush"/> when only history changed (avoids re-serializing the document).</summary>
    public void FlushHistory()
    {
        if (_slot.Itemstack is not { } stack) return;
        stack.Attributes.SetBytes("scribeHistory", _history.Serialize());
        _slot.MarkDirty();
        if (_sapi is not null && _player is not null)
        {
            _sapi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
            {
                DocIdBytes   = _document.DocId.ToByteArray(),
                HistoryBytes = _history.Serialize(),
                // DocumentBytes intentionally null — client treats null as "no document update"
            }, _player);
        }
    }

    private void RecordPickedUpIfNew(ICoreServerAPI sapi, IServerPlayer player)
    {
        var added = _history.TryAddEntry(new HistoryEntry
        {
            Kind       = HistoryEventKind.PickedUp,
            ActorName  = player.PlayerName,
            InGameDate = FormatDate(sapi),
        });
        if (added) Flush();
    }

    internal static string FormatDate(ICoreServerAPI sapi)
    {
        var cal = sapi.World.Calendar;
        int dayOfMonth = (int)(cal.TotalDays % cal.DaysPerMonth) + 1;
        return $"{dayOfMonth} {Vintagestory.API.Config.Lang.Get("month-" + cal.MonthName)}, Year {cal.Year}";
    }

    /// <summary>Formats the calendar date <paramref name="daysAgo"/> in-game days before now, so seeded
    /// demo History/Guestbook entries span multiple days instead of all reading "today". Mirrors
    /// <see cref="FormatDate"/> but derives month/year/day-of-month from <c>TotalDays - daysAgo</c>
    /// (clamped at 0 so it never underflows into a negative calendar). Display-only; plausibility, not
    /// calendar exactness, is the bar (see design decision 5).</summary>
    internal static string FormatDateDaysAgo(ICoreServerAPI sapi, int daysAgo)
    {
        var cal = sapi.World.Calendar;
        double totalDays = Math.Max(0, cal.TotalDays - daysAgo);
        int monthsPerYear = Math.Max(1, cal.DaysPerYear / cal.DaysPerMonth);
        int dayOfMonth = (int)(totalDays % cal.DaysPerMonth) + 1;
        int monthIndex = (int)(totalDays / cal.DaysPerMonth) % monthsPerYear + 1;
        int year = (int)(totalDays / cal.DaysPerYear) + 1;
        var monthName = (Vintagestory.API.Common.EnumMonth)monthIndex;
        return $"{dayOfMonth} {Vintagestory.API.Config.Lang.Get("month-" + monthName)}, Year {year}";
    }
}
