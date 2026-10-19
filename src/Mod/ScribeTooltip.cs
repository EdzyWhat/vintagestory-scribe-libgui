using Scribe.Core;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>
/// Shared formatting for the title line on the Lectern's look-at info and the Notebook items' hover, so all
/// call sites render the quoting and untitled placeholder identically.
/// </summary>
public static class ScribeTooltip
{
    /// <summary>Formats the localized <c>Title: "&lt;title&gt;"</c> line. A null/whitespace title or the
    /// default <see cref="ScribeDocument.DefaultTitle"/> renders as the <c>(untitled)</c> placeholder.</summary>
    public static string FormatTitleLine(string? rawTitle)
    {
        string title = string.IsNullOrWhiteSpace(rawTitle) || rawTitle == ScribeDocument.DefaultTitle
            ? Lang.Get("scribe:tooltip-title-untitled")
            : rawTitle;
        return Lang.Get("scribe:tooltip-title", title);
    }
}
