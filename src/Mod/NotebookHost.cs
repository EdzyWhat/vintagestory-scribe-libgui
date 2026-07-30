using System;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// <see cref="IScribeDocumentHost"/> adapter for the player-held Notebook item. Wraps the
/// player's held <see cref="ItemSlot"/> and persists the document into
/// <c>ItemStack.Attributes["scribeDocument"]</c> via <see cref="ScribeDocumentAttributes"/>.
///
/// Registered into <see cref="ScribeModSystem"/>'s host registry when the notebook dialog
/// opens; unregistered when the dialog closes (any route: ESC, X button, item dropped).
/// Owner-only: <see cref="IsLockedByOther"/> always returns false because only one player
/// can hold the item at a time.
/// </summary>
public sealed class NotebookHost : IScribeDocumentHost
{
    private ScribeDocument _document;

    public NotebookHost(ItemSlot slot)
    {
        var stack = slot.Itemstack!;
        if (!ScribeDocumentAttributes.TryReadFrom(stack, out var doc) || doc is null)
        {
            doc = new ScribeDocument();
            ScribeDocumentAttributes.WriteTo(stack, doc);
        }
        _document = doc;
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
}
