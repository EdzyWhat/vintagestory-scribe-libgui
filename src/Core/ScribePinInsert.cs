namespace Scribe.Core;

/// <summary>
/// Where a newly pinned task lands in the player's pin list, when it has no pinned-parent
/// relationship (see <see cref="ScribePlayerSettings.PinInsert"/>). Distinct from
/// <see cref="ScribeNewTaskInsert"/>, which governs where a brand-new task is created inside a
/// document's block list — a different concern from where a pin reference lands in the
/// cross-document pin list. A subtask that attaches under its pinned parent's cluster, or that
/// re-parents under a parent pinned later, ignores this setting entirely (see
/// <see cref="ScribePinOrdering.PlaceNewPin"/>).
/// </summary>
/// <remarks>
/// <see cref="Bottom"/> is deliberately the zero value (unlike <see cref="ScribeNewTaskInsert"/>, where
/// <c>Top</c> is 0): pin placement is decided server-side from a byte the client sends over the network
/// (<see cref="ScribeSetPinMessage.PinInsert"/>), and an old client that predates this setting sends no
/// such field at all, which protobuf-net deserializes as the CLR default of the byte — 0. Keeping
/// <c>Bottom</c> at 0 means that old-client case lands on the correct legacy (always-append) behavior
/// by construction, with no separate remapping needed.
/// </remarks>
public enum ScribePinInsert : byte
{
    /// <summary>Append after the last existing pin (the historical, and default, behavior).</summary>
    Bottom = 0,

    /// <summary>Insert at index 0. Repeated pins stack newest-first.</summary>
    Top = 1,
}
