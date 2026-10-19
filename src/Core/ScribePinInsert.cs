namespace Scribe.Core;

/// <summary>
/// Where a newly pinned task with no pinned parent lands in the player's pin list (see
/// <see cref="ScribePlayerSettings.PinInsert"/>). Distinct from <see cref="ScribeNewTaskInsert"/>, which governs
/// creation inside a document. A subtask attaching under its pinned parent ignores this (see
/// <see cref="ScribePinOrdering.PlaceNewPin"/>).
/// </summary>
/// <remarks>
/// <see cref="Bottom"/> is deliberately the zero value: an old client that predates this setting sends no
/// field, which deserializes as 0, so it lands on the legacy always-append behavior with no remapping.
/// </remarks>
public enum ScribePinInsert : byte
{
    /// <summary>Append after the last existing pin. The default.</summary>
    Bottom = 0,

    /// <summary>Insert at index 0, newest first.</summary>
    Top = 1,
}
