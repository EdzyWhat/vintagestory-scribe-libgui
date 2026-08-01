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
    private readonly ScribeBackdropSpec _backdrop;
    private ScribeDocument _document;
    private HistoryStore _history;
    private ICoreServerAPI? _sapi;
    private IServerPlayer? _player;

    /// <param name="backdrop">The dialog backdrop this host reports via <see cref="BackdropSpec"/>.
    /// Defaults to <see cref="ScribeBackdrops.NotebookPage"/> (the plain Notebook's art); the Clockmaker's
    /// Notebook item passes <see cref="ScribeBackdrops.ClockmakerPage"/> so it draws distinct art even
    /// though both items share this host class.</param>
    public NotebookHost(ItemSlot slot, ScribeBackdropSpec? backdrop = null)
    {
        _slot = slot;
        _backdrop = backdrop ?? ScribeBackdrops.NotebookPage;
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
    /// back to the player's client after mutating the ItemStack. Also records this player's one-time
    /// PickedUp entry (deduplicated per actor, skipped for the crafter) via
    /// <see cref="RecordPickedUpIfNew"/> — reached both from the notebook-opened network handler and
    /// whenever the server resolves the host for a task interaction or death event.</summary>
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

    public ScribeBackdropSpec BackdropSpec => _backdrop;

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
        // The crafter already has a "Crafted by X" entry standing in for their acquisition, so don't
        // also give them a redundant "Picked up" line. Other players still get their own first-pickup
        // entry (deduplicated per actor by TryAddEntry). The Crafted entry stores the crafter's name
        // in ActorName (see ItemScribeNotebook.OnCreatedByCrafting).
        bool isCrafter = _history.Entries.Any(
            e => e.Kind == HistoryEventKind.Crafted && e.ActorName == player.PlayerName);
        if (isCrafter) return;

        var added = _history.TryAddEntry(new HistoryEntry
        {
            Kind       = HistoryEventKind.PickedUp,
            ActorName  = player.PlayerName,
            InGameDate = FormatDate(sapi),
        });
        if (added) Flush();
    }

    /// <summary>Records the one-time PickedUp entry directly on a held notebook's ItemStack — history
    /// ONLY, deliberately never touching the <c>scribeDocument</c> attribute. This is the path used by
    /// the notebook-opened network handler, where the server sees a notebook that was just picked up
    /// and opened but has never synced a document (a notebook's DocId is generated client-side and only
    /// reaches the server on the first edit; crafting writes only <c>scribeHistory</c>). Constructing a
    /// full <see cref="NotebookHost"/> here would be wrong: its ctor stamps a fresh server-random
    /// document onto the stack, which <see cref="ScribeModSystem.OnServerReceivedNotebookSave"/> would
    /// then reject the owner's real edits against (DocId mismatch). Working on the raw history attribute
    /// avoids that entirely. Crafter is suppressed and other players are deduplicated per actor, exactly
    /// like <see cref="RecordPickedUpIfNew"/>. Returns the updated history bytes to push to the client
    /// (so an open dialog can refresh its History tab) when an entry was added, else null.</summary>
    public static byte[]? TryRecordPickedUpOnSlot(ICoreServerAPI sapi, ItemSlot slot, IServerPlayer player)
    {
        if (slot.Itemstack is not { } stack) return null;
        var history = HistoryStore.Deserialize(stack.Attributes.GetBytes("scribeHistory"));

        bool isCrafter = history.Entries.Any(
            e => e.Kind == HistoryEventKind.Crafted && e.ActorName == player.PlayerName);
        if (isCrafter) return null;

        bool added = history.TryAddEntry(new HistoryEntry
        {
            Kind       = HistoryEventKind.PickedUp,
            ActorName  = player.PlayerName,
            InGameDate = FormatDate(sapi),
        });
        if (!added) return null;

        var bytes = history.Serialize();
        stack.Attributes.SetBytes("scribeHistory", bytes);
        slot.MarkDirty();
        return bytes;
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
