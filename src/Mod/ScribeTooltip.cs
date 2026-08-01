using Scribe.Core;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>
/// Shared formatting for the Scribe title line that appears on tooltips — the placed Lectern's
/// look-at info (<c>GetPlacedBlockInfo</c>) and the Notebook items' held/inventory hover
/// (<c>GetHeldItemInfo</c>). Centralized so all three call sites render the quoting and the
/// untitled placeholder identically.
/// </summary>
public static class ScribeTooltip
{
    /// <summary>Formats the localized <c>Title: "&lt;title&gt;"</c> line for a tooltip. A title that is
    /// null/whitespace, or the model default <see cref="ScribeDocument.DefaultTitle"/> (what an
    /// untitled or never-opened document reports), renders with the <c>(untitled)</c> placeholder so
    /// the line is always present and consistent.</summary>
    public static string FormatTitleLine(string? rawTitle)
    {
        string title = string.IsNullOrWhiteSpace(rawTitle) || rawTitle == ScribeDocument.DefaultTitle
            ? Lang.Get("scribe:tooltip-title-untitled")
            : rawTitle;
        return Lang.Get("scribe:tooltip-title", title);
    }
}
