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

    /// <summary>Give an undated task a date.</summary>
    AddDate,

    /// <summary>Change a dated task's date.</summary>
    ChangeDate,

    /// <summary>Remove a task's date (and its Repeat yearly flag).</summary>
    ClearDate,

    /// <summary>Toggle whether a dated task repeats every year. A toggle entry: the menu stays open.</summary>
    RepeatYearly,

    /// <summary>Delete the row.</summary>
    Delete,
}

/// <summary>
/// Which actions a document row's context menu offers. The answer depends on the view, whether the surface is
/// permanently read-only, whether the surface's pin control is live, whether the row is pinned, and whether
/// the row can be dated and already has a date. The order is Edit, Pin or Unpin, the date group, then Delete.
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
    /// <param name="canDate">Whether the row can be dated here: the surface supports dates, the row is
    /// datable and (in the read view) the document is editable. On the Pin Tab: the dialog supports dates and the
    /// pin can be dated.</param>
    /// <param name="hasDate">Whether the row already has a date: offers Change date, Clear date and Repeat
    /// yearly instead of Add date.</param>
    /// <param name="allowRepeat">Whether Repeat yearly is offered on a dated row. False on the Pin Tab, whose
    /// pins carry no repeat flag.</param>
    public static IReadOnlyList<ScribeRowMenuAction> For(
        ScribeRowMenuView view, bool readOnly, bool pinLive, bool isPinned,
        bool canDate = false, bool hasDate = false, bool allowRepeat = true)
    {
        var entries = new List<ScribeRowMenuAction>(7);
        switch (view)
        {
            case ScribeRowMenuView.Read:
                if (!readOnly) entries.Add(ScribeRowMenuAction.Edit);
                if (pinLive) entries.Add(PinOrUnpin(isPinned));
                if (!readOnly && canDate) AddDateGroup(entries, hasDate, allowRepeat);
                if (!readOnly) entries.Add(ScribeRowMenuAction.Delete);
                break;
            case ScribeRowMenuView.Editor:
                if (pinLive) entries.Add(PinOrUnpin(isPinned));
                if (canDate) AddDateGroup(entries, hasDate, allowRepeat);
                entries.Add(ScribeRowMenuAction.Delete);
                break;
            case ScribeRowMenuView.PinTab:
                entries.Add(ScribeRowMenuAction.Unpin);
                if (canDate) AddDateGroup(entries, hasDate, allowRepeat);
                entries.Add(ScribeRowMenuAction.Delete);
                break;
        }
        return entries;
    }

    private static void AddDateGroup(List<ScribeRowMenuAction> entries, bool hasDate, bool allowRepeat)
    {
        if (!hasDate)
        {
            entries.Add(ScribeRowMenuAction.AddDate);
            return;
        }
        entries.Add(ScribeRowMenuAction.ChangeDate);
        entries.Add(ScribeRowMenuAction.ClearDate);
        if (allowRepeat) entries.Add(ScribeRowMenuAction.RepeatYearly);
    }

    private static ScribeRowMenuAction PinOrUnpin(bool isPinned) =>
        isPinned ? ScribeRowMenuAction.Unpin : ScribeRowMenuAction.Pin;
}
