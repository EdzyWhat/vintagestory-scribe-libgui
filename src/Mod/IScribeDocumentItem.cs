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

    /// <summary>Whether this carried item can currently RECEIVE a Handbook-originated append — i.e. its
    /// stored document is editable right now. Notebooks are always writeable (the default); a Tablet is
    /// writeable only while wet — a hardened or fired tablet is read-only. The Handbook "Add to Scribe"
    /// resolver (<c>ScribeModSystem.AddFromHandbook</c>) skips non-writeable carried items and moves on to
    /// the next one, so a Tracker/Link never lands on (and silently no-ops against) a read-only tablet
    /// (add-tracker-link-tasks feedback 6.2). Default <c>true</c> for the always-writeable items.</summary>
    bool IsSlotWriteable(ItemSlot slot) => true;

    /// <summary>The document-capacity/editability policy for the item currently in <paramref name="slot"/>
    /// — the item-level counterpart of <see cref="IScribeDocumentHost.Policy"/>. The Transcribe copy uses
    /// it to validate a target: a copy is refused unless <c>DocumentPolicy(target).CanHold(sourceTaskCount)</c>,
    /// which in one check rejects both a read-only target (a hardened/fired tablet — <c>ReadOnly</c>) and one
    /// too small to hold the source's tasks (a wet tablet at its cap). Defaults to
    /// <see cref="Scribe.Core.ScribeDocumentPolicy.Unlimited"/> — the always-writeable, uncapped tiers
    /// (Notebook, Clockmaker's, picked-up Lectern/Scriptorium) need no override; only <see cref="ItemScribeTablet"/>
    /// reports a finite/read-only policy from its live clay state.</summary>
    Scribe.Core.ScribeDocumentPolicy DocumentPolicy(ItemSlot slot) => Scribe.Core.ScribeDocumentPolicy.Unlimited;
}
