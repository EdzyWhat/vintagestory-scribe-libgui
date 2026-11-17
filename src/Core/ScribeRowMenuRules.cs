namespace Scribe.Core;

/// <summary>The fields of a row-menu entry that decide whether an open menu must redraw: everything but the
/// entry's action, which is a new closure on every resolve.</summary>
public readonly record struct ScribeRowMenuItemKey(
    string LabelLangKey, bool? Checked, bool CloseOnChoose, bool SeparatorBefore, bool Refreshable);

/// <summary>The pure decisions behind the row-menu host: when choosing an entry closes the menu, when an open
/// menu's refreshable entries changed, and when the menu or its date picker closes.</summary>
public static class ScribeRowMenuRules
{
    /// <summary>Whether choosing an entry closes the menu. A toggle entry (one with a checked state) never does.</summary>
    public static bool ClosesOnChoose(bool closeOnChoose, bool? isChecked) => closeOnChoose && isChecked is null;

    /// <summary>Whether the refreshable entries of <paramref name="fresh"/> differ from those the menu is showing
    /// in label, checked state, close-on-choose or separator.</summary>
    public static bool RefreshableEntriesChanged(
        IEnumerable<ScribeRowMenuItemKey> open, IEnumerable<ScribeRowMenuItemKey> fresh) =>
        !Refreshable(open).SequenceEqual(Refreshable(fresh));

    private static IEnumerable<(string, bool?, bool, bool)> Refreshable(IEnumerable<ScribeRowMenuItemKey> items) =>
        items.Where(i => i.Refreshable).Select(i => (i.LabelLangKey, i.Checked, i.CloseOnChoose, i.SeparatorBefore));

    /// <summary>Whether an open menu or picker closes because the dialog's view changed.</summary>
    public static bool ClosesOnViewChange(ScribeRowMenuView? openedIn, ScribeRowMenuView? current) => openedIn != current;

    /// <summary>Whether an open date picker closes this frame: its row is no longer mounted or has scrolled out of
    /// view, or it was opened in the read view and another player now holds the editor lock (so Set would be refused).</summary>
    public static bool PickerShouldClose(bool rowMounted, bool rowScrolledOut, ScribeRowMenuView? openedIn, bool lockedByOther) =>
        !rowMounted || rowScrolledOut || (openedIn == ScribeRowMenuView.Read && lockedByOther);
}
