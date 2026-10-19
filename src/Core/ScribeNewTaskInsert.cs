namespace Scribe.Core;

/// <summary>
/// Where a <b>document-level</b> create lands (footer Add, Shift+right-click quick-add, Handbook Add to
/// Scribe); see <see cref="ScribePlayerSettings.NewTaskInsert"/>. Enter insert-below stays relative to the focused row.
/// </summary>
public enum ScribeNewTaskInsert : byte
{
    /// <summary>Insert at index 0, newest first. The default.</summary>
    Top = 0,

    /// <summary>Append after the last existing row.</summary>
    Bottom = 1,
}
