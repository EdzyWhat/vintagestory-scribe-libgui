namespace Scribe.Core;

/// <summary>The dialog view a document row sits in when its context menu is requested.</summary>
public enum ScribeRowMenuView
{
    /// <summary>The read view of a document.</summary>
    Read,

    /// <summary>The editor view of a document.</summary>
    Editor,

    /// <summary>The Pin Tab, which lists the player's pinned rows across documents.</summary>
    PinTab,
}

/// <summary>An action a document row's context menu can offer.</summary>
public enum ScribeRowMenuAction
{
    /// <summary>Enter the editor with this row focused.</summary>
    Edit,

    /// <summary>Pin the row.</summary>
    Pin,

    /// <summary>Remove the row's pin.</summary>
    Unpin,

    /// <summary>Delete the row.</summary>
    Delete,
}

/// <summary>
/// Which actions a document row's context menu offers. Every block kind gets the same entries, so the
/// answer depends only on the view, whether the surface is permanently read-only, whether the surface's
/// pin control is live, and whether the row is pinned.
/// </summary>
public static class ScribeRowMenuEntries
{
    /// <summary>
    /// The entries for a row, in menu order. Empty when nothing is allowed, in which case no menu opens.
    /// </summary>
    /// <param name="view">The view the row is in.</param>
    /// <param name="readOnly">True on a permanently read-only surface (a hard or fired Tablet), which offers
    /// neither Edit nor Delete. Only meaningful for <see cref="ScribeRowMenuView.Read"/>.</param>
    /// <param name="pinLive">Whether the surface's pin control is live; Pin and Unpin are offered only then,
    /// except on the Pin Tab, whose Unpin is always available.</param>
    /// <param name="isPinned">Whether the row is currently pinned for the viewing player.</param>
    public static IReadOnlyList<ScribeRowMenuAction> For(
        ScribeRowMenuView view, bool readOnly, bool pinLive, bool isPinned)
    {
        var entries = new List<ScribeRowMenuAction>(3);
        switch (view)
        {
            case ScribeRowMenuView.Read:
                if (!readOnly) entries.Add(ScribeRowMenuAction.Edit);
                if (pinLive) entries.Add(PinOrUnpin(isPinned));
                if (!readOnly) entries.Add(ScribeRowMenuAction.Delete);
                break;
            case ScribeRowMenuView.Editor:
                if (pinLive) entries.Add(PinOrUnpin(isPinned));
                entries.Add(ScribeRowMenuAction.Delete);
                break;
            case ScribeRowMenuView.PinTab:
                entries.Add(ScribeRowMenuAction.Unpin);
                entries.Add(ScribeRowMenuAction.Delete);
                break;
        }
        return entries;
    }

    private static ScribeRowMenuAction PinOrUnpin(bool isPinned) =>
        isPinned ? ScribeRowMenuAction.Unpin : ScribeRowMenuAction.Pin;
}
