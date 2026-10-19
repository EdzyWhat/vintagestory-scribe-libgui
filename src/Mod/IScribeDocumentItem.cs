using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Marker for player-held item classes that persist a Scribe document on their ItemStack (Notebook,
/// Clockmaker's Notebook, Tablet). Server save/pickup/pin/history paths and the dialog's active-slot check
/// gate on this interface; an item that lacks it has its edits silently dropped server-side.
/// It also exposes <see cref="OpenScribeDialog"/> so a slot-only caller (Handbook "Add to Scribe") can open
/// the item's dialog without duplicating host-wiring.
/// </summary>
public interface IScribeDocumentItem
{
    /// <summary>Opens this carried item's Scribe dialog on the client, wired and registered like the item's own
    /// right-click open, and records it as the last-opened Scribe item. Returns the dialog, or <c>null</c> if
    /// it can't be opened on this side/state.</summary>
    ScribeDialogBase? OpenScribeDialog(ItemSlot slot, ICoreClientAPI capi);

    /// <summary>Whether the stored document is editable right now (a hardened or fired tablet is read-only).
    /// The Handbook "Add to Scribe" resolver skips non-writeable items. Default <c>true</c>.</summary>
    bool IsSlotWriteable(ItemSlot slot) => true;

    /// <summary>The capacity/editability policy for the item in <paramref name="slot"/> (item-level counterpart
    /// of <see cref="IScribeDocumentHost.Policy"/>). Transcribe refuses a target unless
    /// <c>DocumentPolicy(target).CanHold(sourceBlockCount)</c>. Defaults to Unlimited; only
    /// <see cref="ItemScribeTablet"/> overrides it.</summary>
    Scribe.Core.ScribeDocumentPolicy DocumentPolicy(ItemSlot slot) => Scribe.Core.ScribeDocumentPolicy.Unlimited;
}
