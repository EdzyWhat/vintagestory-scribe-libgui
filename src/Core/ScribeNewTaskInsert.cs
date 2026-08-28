namespace Scribe.Core;

/// <summary>
/// Where a <b>document-level</b> create lands (footer Add, Shift+right-click quick-add, Handbook
/// Add to Scribe). A per-player, client-local preference
/// (<see cref="ScribePlayerSettings.NewTaskInsert"/>). Enter = insert-below-caret does not use
/// this — that gesture stays relative to the focused row.
/// </summary>
public enum ScribeNewTaskInsert : byte
{
    /// <summary>Insert at index 0. Repeated creates stack newest-first. The default.</summary>
    Top = 0,

    /// <summary>Append after the last existing row (the historical Add-button / Handbook behavior).</summary>
    Bottom = 1,
}
