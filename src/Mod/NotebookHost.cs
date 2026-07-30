using System;
using Scribe.Core;
using Vintagestory.API.Common;
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
    }

    /// <summary>Attach server context so write-through operations can push the updated document
    /// back to the player's client after mutating the ItemStack.</summary>
    public void AttachServerContext(ICoreServerAPI sapi, IServerPlayer player)
    {
        _sapi = sapi;
        _player = player;
    }

    public ScribeDocument Document => _document;

    public bool IsLockedByOther(string viewerUid) => false;

    public void ApplyLocalOptimisticEdit(ScribeDocument doc) => _document = doc;

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

    private void Flush()
    {
        if (_slot.Itemstack is not { } stack) return;
        ScribeDocumentAttributes.WriteTo(stack, _document);
        _slot.MarkDirty();
        // Push the updated document back to the player's client so their dialog and read view
        // reflect the change (e.g. a deleted or moved task). Mirrors the Lectern's MarkDirty(redrawOnClient:true).
        if (_sapi is not null && _player is not null)
        {
            _sapi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
            {
                DocIdBytes = _document.DocId.ToByteArray(),
                DocumentBytes = ScribeDocumentCodec.Serialize(_document),
            }, _player);
        }
    }
}
