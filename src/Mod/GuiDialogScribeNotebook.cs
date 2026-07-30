using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
    private IInventory? _hotbar;

    public GuiDialogScribeNotebook(IScribeDocumentHost host, ICoreClientAPI capi)
        : base(new BlockPos(0), host, capi)
    {
        capi.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
        _hotbar = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (_hotbar != null)
            _hotbar.SlotModified += OnHotbarSlotModified;
    }

    /// <summary>Disable the engine's frame-by-frame range check. Notebooks are not proximity-bound
    /// to a block position, so we never want the dialog to auto-close based on distance.</summary>
    protected override double InteractionRange => double.MaxValue;

    /// <summary>The Notebook grants editor access immediately without a server round-trip — there is
    /// no lock to contend over when only one player can hold an item at a time. Seed the scratch from
    /// the host's current document so existing tasks and title are preserved when entering editor mode.</summary>
    protected override void RequestEditorAccess()
    {
        EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
    }

    /// <summary>No editor lock on a Notebook — nothing to release.</summary>
    protected override void SendReleaseLockPacket() { }

    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs _)
    {
        if (capi.World.Player.Entity.ActiveHandItemSlot?.Itemstack?.Collectible is not ItemScribeNotebook)
            TryClose();
    }

    private void OnHotbarSlotModified(int slotId)
    {
        if (slotId == capi.World.Player.InventoryManager.ActiveHotbarSlotNumber)
            OnActiveSlotChanged(default!);
    }

    public override void OnGuiClosed()
    {
        capi.Event.AfterActiveSlotChanged -= OnActiveSlotChanged;
        if (_hotbar != null)
            _hotbar.SlotModified -= OnHotbarSlotModified;
        base.OnGuiClosed();
    }

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
