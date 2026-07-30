using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Notebook item's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>.
/// Differences from the Lectern:
/// <list type="bullet">
/// <item>No Guestbook tab (no <see cref="GetExtraNavButtons"/> override).</item>
/// <item>Editor access is always granted without a server round-trip (no lock contention on items).</item>
/// <item>Saves use <see cref="ScribeNotebookSaveMessage"/> instead of <see cref="ScribeEditDocumentMessage"/>.</item>
/// <item>Lock-release is a no-op (no editor lock on items).</item>
/// <item>Proximity auto-close disabled via <see cref="InteractionRange"/> override.</item>
/// </list>
/// </summary>
public sealed class GuiDialogScribeNotebook : ScribeDialogBase
{
    public GuiDialogScribeNotebook(IScribeDocumentHost host, ICoreClientAPI capi)
        : base(new BlockPos(0), host, capi)
    {
    }

    /// <summary>Disable the engine's frame-by-frame range check. Notebooks are not proximity-bound
    /// to a block position, so we never want the dialog to auto-close based on distance.</summary>
    protected override double InteractionRange => double.MaxValue;

    /// <summary>The Notebook grants editor access immediately without a server round-trip — there is
    /// no lock to contend over when only one player can hold an item at a time.</summary>
    protected override void RequestEditorAccess()
    {
        EnterEditorMode(null); // null = seed scratch from host.Document (the current authoritative copy)
    }

    /// <summary>No editor lock on a Notebook — nothing to release.</summary>
    protected override void SendReleaseLockPacket() { }

    /// <summary>Notebook saves use <see cref="ScribeNotebookSaveMessage"/> so the server can write
    /// directly into the player's held ItemStack rather than routing through a block entity.</summary>
    protected override void SendFlushPacket(byte[] documentBytes)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = host.Document.DocId.ToByteArray(),
            DocumentBytes = documentBytes,
        });
    }
}
