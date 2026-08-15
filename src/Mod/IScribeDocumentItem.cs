using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Marker for the player-held item classes that persist a Scribe document on their ItemStack (the
/// Notebook, the Clockmaker's Notebook, and the clay/wax Tablet). The server-side save/pickup/pin/history
/// paths and the dialog's active-slot check gate on this interface rather than an exhaustive
/// <c>is (ItemScribeNotebook or ItemClockmakerNotebook or …)</c> type list, so a new document-bearing
/// item is recognized everywhere just by implementing it — without which its edits are silently dropped
/// server-side (the stack is never written, so a drop/pickup round-trip wipes it).
///
/// <para>Beyond the marker role it exposes one client-side seam, <see cref="OpenScribeDialog"/>, so a
/// caller that only has the slot (the Handbook "Add to Scribe" resolution in
/// <c>ScribeModSystem.AddFromHandbook</c>) can open the item's own Scribe dialog and act on it, without
/// duplicating each item's host-wiring/registration. It exists only to make "is this one of Scribe's
/// document items, and open it?" a single, extensible check.</para>
/// </summary>
public interface IScribeDocumentItem
{
    /// <summary>Open this carried item's Scribe dialog on the client and return it, so a caller can
    /// immediately act on the just-opened surface (the Handbook "Add to Scribe" fallback opens the last-used
    /// Scribe item this way, then appends a Tracker/Link — add-tracker-link-tasks 3.3). Wires and registers
    /// the host exactly as the item's own right-click open does, and records itself as the last-opened Scribe
    /// item. Returns the opened <see cref="ScribeDialogBase"/>, or <c>null</c> if it can't be opened on this
    /// side/state.</summary>
    ScribeDialogBase? OpenScribeDialog(ItemSlot slot, ICoreClientAPI capi);
}
