using System;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// <see cref="IScribeDocumentHost"/> adapter for the player-held Notebook item. Wraps the held
/// <see cref="ItemSlot"/> and persists the document into the ItemStack via <see cref="ScribeDocumentAttributes"/>.
/// Owner-only: <see cref="IsLockedByOther"/> is always false.
/// </summary>
public class NotebookHost : IScribeDocumentHost, IHistoryRecordable
{
    private readonly ItemSlot _slot;
    private readonly ScribeBackdropSpec _backdrop;
    private ScribeDocument _document;
    private HistoryStore _history;
    private ICoreServerAPI? _sapi;
    private IServerPlayer? _player;
    private byte _readViewFilterCategory;
    private HashSet<Guid> _collapsedGroupIds;

    /// <param name="backdrop">Dialog backdrop; defaults to <see cref="ScribeBackdrops.NotebookPage"/>. The
    /// Clockmaker's Notebook passes <see cref="ScribeBackdrops.ClockmakerPage"/>.</param>
    public NotebookHost(ItemSlot slot, ScribeBackdropSpec? backdrop = null)
    {
        _slot = slot;
        _backdrop = backdrop ?? ScribeBackdrops.NotebookPage;
        var stack = slot.Itemstack!;
        // A documentless stack gets an in-memory placeholder only, NOT written back: writing here would race
        // the ambient OnHistoryScanTick sweep for who mints the real DocId first, leaving the client proposing
        // a DocId the server never adopted. OnServerReceivedNotebookSave's bootstrap branch is the only place
        // a fresh DocId is persisted.
        _document = ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null
            ? doc
            : new ScribeDocument();
        _history = HistoryStore.Deserialize(stack.Attributes.GetBytes("scribeHistory"));
        // Read View state; absent attributes default to All / fully-expanded.
        _readViewFilterCategory = (byte)stack.Attributes.GetInt("scribeReadViewFilter", 0);
        _collapsedGroupIds = ScribeReadViewFilter.DeserializeGuidSet(stack.Attributes.GetBytes("scribeReadViewCollapsed"));
    }

    /// <summary>Attaches server context so write-through can push the updated document to the client, and
    /// records this player's one-time PickedUp entry via <see cref="RecordPickedUpIfNew"/>.</summary>
    public void AttachServerContext(ICoreServerAPI sapi, IServerPlayer player)
    {
        _sapi = sapi;
        _player = player;
        RecordPickedUpIfNew(sapi, player);
    }

    public ScribeDocument Document => _document;

    /// <summary>The <c>InventoryID</c> of the bound slot, so a client save packet names the EXACT slot edited.
    /// Null when the slot has no resolvable inventory (the server then falls back to the active hand).</summary>
    public string? SlotInventoryId => _slot.Inventory?.InventoryID;

    /// <summary>The slot index within <see cref="SlotInventoryId"/>, or -1 when unresolvable.</summary>
    public int SlotId => _slot.Inventory?.GetSlotId(_slot) ?? -1;

    /// <summary>The notebook's history chronicle, persisted in <c>scribeHistory</c> and flushed with the document.</summary>
    public HistoryStore History => _history;

    public bool IsLockedByOther(string viewerUid) => false;

    public void ApplyLocalOptimisticEdit(ScribeDocument doc) => _document = doc;

    /// <summary>Replaces the in-memory history from bytes pushed by the server (client receive path).</summary>
    public void ApplyHistoryUpdate(byte[]? historyBytes)
    {
        if (historyBytes is not null)
            _history = HistoryStore.Deserialize(historyBytes);
    }

    public ScribeBackdropSpec BackdropSpec => _backdrop;

    /// <summary><c>virtual</c> so a subclass (the tablet) can re-proportion its dialog; the base uses default
    /// proportions and the notebook art's 1160/1024 aspect.</summary>
    public virtual ScribeLayout GetLayout(float pixelArtSize) => new ScribeLayout(pixelArtSize, 1160f / 1024f);

    public virtual string DefaultDocumentTitle => Lang.Get("scribe:doctitle-notebook");

    /// <summary>The notebook tier is uncapped. Declared <c>virtual</c> here so <see cref="TabletHost"/> can
    /// override it: a bare member on the subclass would not re-map the interface, which is fixed at the
    /// declaring type.</summary>
    public virtual ScribeDocumentPolicy Policy => ScribeDocumentPolicy.Unlimited;

    /// <summary>Never called: <see cref="GuiDialogScribeNotebook"/> adds no Visitors nav button.</summary>
    public GuestbookStore Guestbook => throw new NotSupportedException("Notebook has no guestbook.");

    // Server-side write-through: mutate the in-memory document, then persist to the ItemStack

    public void SetTaskDoneFromReader(Guid taskId, bool done)
    {
        var block = _document.FindByTaskId(taskId);
        if (block is null || !block.IsCompletable || block.Done == done) return;
        block.Done = done;
        Flush();
    }

    public bool DeleteTaskFromReader(Guid taskId)
    {
        for (int i = 0; i < _document.Blocks.Count; i++)
        {
            if (_document.Blocks[i].TaskId == taskId && _document.Blocks[i].IsCompletable)
            {
                _document.DeleteBlock(i);
                Flush();
                return true;
            }
        }
        return false;
    }

    public void PersistFromReader() => Flush();

    public byte ReadViewFilterCategory => _readViewFilterCategory;
    public IReadOnlyCollection<Guid> CollapsedGroupIds => _collapsedGroupIds;

    /// <summary>Overwrites the persisted Read View filter pill + collapsed-group set. Writes straight to the
    /// ItemStack without pushing a <see cref="ScribeNotebookSaveMessage"/>: the sender already applied it
    /// optimistically and the holder is the only viewer.</summary>
    public void SetReadViewStateFromReader(byte filterCategory, IReadOnlyCollection<Guid> collapsedGroupIds)
    {
        _readViewFilterCategory = filterCategory;
        _collapsedGroupIds = new HashSet<Guid>(collapsedGroupIds);
        if (_slot.Itemstack is not { } stack) return;
        stack.Attributes.SetInt("scribeReadViewFilter", filterCategory);
        stack.Attributes.SetBytes("scribeReadViewCollapsed", ScribeReadViewFilter.SerializeGuidSet(_collapsedGroupIds));
        _slot.MarkDirty();
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

    /// <summary>Sets a Tracker's live <see cref="ScribeBlock.CurrentQuantity"/>. Persists only on a real change;
    /// an unknown id or non-Tracker returns false without flushing.</summary>
    public bool SetTrackerCurrentQuantityFromReader(Guid taskId, int qty)
    {
        var block = _document.FindByTaskId(taskId);
        if (block is null || !block.IsTracker) return false;
        if (block.CurrentQuantity == Math.Max(0, qty)) return false;
        if (!_document.SetTrackerCurrentQuantity(taskId, qty)) return false;
        Flush();
        return true;
    }

    /// <summary>Sets a QuestObjective's live <see cref="ScribeBlock.CurrentQuantity"/>, clamped into
    /// <c>[0, TargetQuantity]</c> (unlike a Tracker's overflow). An unknown id or non-QuestObjective returns
    /// false without flushing.</summary>
    public bool SetQuestObjectiveProgressFromReader(Guid taskId, int qty)
    {
        var block = _document.FindByTaskId(taskId);
        if (block is null || !block.IsQuestObjective) return false;
        int clamped = Math.Clamp(qty, 0, block.TargetQuantity);
        if (block.CurrentQuantity == clamped) return false;
        if (!_document.SetQuestObjectiveProgress(taskId, qty)) return false;
        Flush();
        return true;
    }

    /// <summary>Writes the document AND history back to the ItemStack, marks the slot dirty, and pushes a full
    /// sync to the client. Public so server-side tools (e.g. the demo seeder) can persist through the normal flow.</summary>
    public void Flush()
    {
        if (_slot.Itemstack is not { } stack) return;
        ScribeDocumentAttributes.WriteTo(stack, _document);
        stack.Attributes.SetBytes("scribeHistory", _history.Serialize());
        _slot.MarkDirty();
        // Push the updated document and history back to the player's client.
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

    /// <summary>Writes only the history back and syncs; cheaper than <see cref="Flush"/>.</summary>
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

    /// <summary>Server-side: creates a Manual history entry authored by <paramref name="player"/> (the packet
    /// SENDER, never a client-claimed identity) with the client-minted <paramref name="entryId"/>. A no-op if
    /// the store rejects it.</summary>
    public void AddManualEntry(ICoreServerAPI sapi, IServerPlayer player, Guid entryId, string text)
    {
        var added = _history.TryAddEntry(new HistoryEntry
        {
            Kind            = HistoryEventKind.Manual,
            ActorName       = player.PlayerName,
            Detail          = text,
            InGameDate      = FormatDate(sapi),
            InGameTimestamp = sapi.World.Calendar.TotalDays,
            EntryId         = entryId,
        });
        if (added) FlushHistory();
    }

    /// <summary>Server-side: updates a Manual entry's text, authorized by matching the player's name against
    /// the stored author. Silently no-ops on an author mismatch or unknown id.</summary>
    public void SetManualEntryText(IServerPlayer player, Guid entryId, string text)
    {
        if (_history.TrySetManualEntryText(entryId, player.PlayerName, text)) FlushHistory();
    }

    /// <summary>Server-side: deletes a Manual entry, authorized like <see cref="SetManualEntryText"/>.</summary>
    public void DeleteManualEntry(IServerPlayer player, Guid entryId)
    {
        if (_history.TryDeleteManualEntry(entryId, player.PlayerName)) FlushHistory();
    }

    private void RecordPickedUpIfNew(ICoreServerAPI sapi, IServerPlayer player)
    {
        // The crafter's "Crafted by X" entry already stands in for acquisition, so skip the "Picked up" line.
        // Other players are deduplicated per actor by TryAddEntry.
        bool isCrafter = _history.Entries.Any(
            e => e.Kind == HistoryEventKind.Crafted && e.ActorName == player.PlayerName);
        if (isCrafter) return;

        var added = _history.TryAddEntry(new HistoryEntry
        {
            Kind            = HistoryEventKind.PickedUp,
            ActorName       = player.PlayerName,
            InGameDate      = FormatDate(sapi),
            InGameTimestamp = sapi.World.Calendar.TotalDays,
        });
        // FlushHistory, not Flush: Flush would also persist _document, stamping a fresh DocId on a stack that
        // had none (e.g. from the OnHistoryScanTick sweep) and racing the client to mint the real one.
        if (added) FlushHistory();
    }

    /// <summary>Records the one-time PickedUp entry directly on a held notebook's ItemStack: history ONLY, never
    /// the document attribute (a notebook's DocId reaches the server only on first edit). Avoids constructing
    /// a full host. Crafter is suppressed and others deduplicated, like <see cref="RecordPickedUpIfNew"/>.
    /// Returns the updated history bytes when an entry was added, else null.</summary>
    public static byte[]? TryRecordPickedUpOnSlot(ICoreServerAPI sapi, ItemSlot slot, IServerPlayer player)
    {
        if (slot.Itemstack is not { } stack) return null;
        var history = HistoryStore.Deserialize(stack.Attributes.GetBytes("scribeHistory"));

        bool isCrafter = history.Entries.Any(
            e => e.Kind == HistoryEventKind.Crafted && e.ActorName == player.PlayerName);
        if (isCrafter) return null;

        bool added = history.TryAddEntry(new HistoryEntry
        {
            Kind            = HistoryEventKind.PickedUp,
            ActorName       = player.PlayerName,
            InGameDate      = FormatDate(sapi),
            InGameTimestamp = sapi.World.Calendar.TotalDays,
        });
        if (!added) return null;

        var bytes = history.Serialize();
        stack.Attributes.SetBytes("scribeHistory", bytes);
        slot.MarkDirty();
        return bytes;
    }

    internal static string FormatDate(ICoreServerAPI sapi) => FormatDate(sapi.World);

    /// <summary>Client-side date formatter, used to preview a pending Manual entry draft before the server stamps it.</summary>
    internal static string FormatDate(IWorldAccessor world)
    {
        var cal = world.Calendar;
        int dayOfMonth = (int)(cal.TotalDays % cal.DaysPerMonth) + 1;
        return FormatCalendarDate(dayOfMonth, cal.MonthName, cal.Year);
    }

    /// <summary>Builds the in-game date string through the localizable <c>scribe:date-format</c> template
    /// ({0}=day, {1}=localized month, {2}=year). Every history/guestbook date stamp routes through here.</summary>
    internal static string FormatCalendarDate(int dayOfMonth, EnumMonth monthName, int year)
        => Lang.Get("scribe:date-format", dayOfMonth, Lang.Get("month-" + monthName), year);

    /// <summary>Formats a stored <c>Calendar.TotalDays</c> timestamp in the viewing client's locale
    /// (clamped at 0; same math as <see cref="FormatDateDaysAgo"/>).</summary>
    internal static string FormatDateFromTimestamp(IWorldAccessor world, double totalDays)
    {
        var cal = world.Calendar;
        totalDays = Math.Max(0, totalDays);
        int monthsPerYear = Math.Max(1, cal.DaysPerYear / cal.DaysPerMonth);
        int dayOfMonth = (int)(totalDays % cal.DaysPerMonth) + 1;
        int monthIndex = (int)(totalDays / cal.DaysPerMonth) % monthsPerYear + 1;
        int year = (int)(totalDays / cal.DaysPerYear) + 1;
        var monthName = (EnumMonth)monthIndex;
        return FormatCalendarDate(dayOfMonth, monthName, year);
    }

    /// <summary>Formats the date <paramref name="daysAgo"/> in-game days before now (clamped at 0), so seeded
    /// demo entries span multiple days. Display-only.</summary>
    internal static string FormatDateDaysAgo(ICoreServerAPI sapi, int daysAgo)
        => FormatDateFromTimestamp(sapi.World, CalendarTotalDaysAgo(sapi, daysAgo));

    /// <summary>The raw <c>Calendar.TotalDays</c> <paramref name="daysAgo"/> days before now (clamped at 0): the
    /// sortable counterpart of <see cref="FormatDateDaysAgo"/> for seeded entries.</summary>
    internal static double CalendarTotalDaysAgo(ICoreServerAPI sapi, int daysAgo)
        => Math.Max(0, sapi.World.Calendar.TotalDays - daysAgo);
}
