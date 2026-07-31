using Gui.Rendering.Text;          // TextStyle
using Gui.Widgets.Basic.Theming;   // DefaultTextStyle
using Gui.Widgets.Framework;       // Widget

namespace Scribe;

/// <summary>
/// Roots a tab's widget subtree in a <see cref="DefaultTextStyle"/> carrying the player's Task Text Font
/// and window-scaled base size, so descendant <c>Text</c> widgets inherit them instead of each threading
/// <c>FontFamily = ScribeTaskFont.Resolve(...)</c> by hand (adopt-libgui-31-improvements). 3.1.0's
/// <c>Text.Build</c> does <c>StyleOverride?.Merge(DefaultTextStyle.Of(context))</c>, so a partial
/// per-widget override (e.g. just <c>Color</c>) inherits family + size from this ancestor.
///
/// <para><b>Merge landmine (verified from the shipped <c>Gui.dll</c>):</b> <c>override.Merge(base)</c>
/// takes each of the override's fields only when it differs from <c>default(TextStyle)</c>, else inherits
/// from <c>base</c>. So a widget that sets a field to its DEFAULT value (<c>SoftWrap = false</c>,
/// <c>Align = <zero></c>, <c>FontSize = 0</c>) would silently inherit instead. To keep every per-widget
/// non-default override winning, this ancestor carries ONLY <see cref="TextStyle.FontFamily"/> and a base
/// <see cref="TextStyle.FontSize"/> — never <c>SoftWrap</c>/<c>Align</c>/etc. See design.md for the
/// field-by-field confirmation.</para>
/// </summary>
internal static class ScribeTextDefaults
{
    /// <summary>The ancestor style: the resolved Task Text Font family plus the tab's window-scaled base
    /// size, every other field left at default so descendant overrides always win. Reuses
    /// <see cref="ScribeTaskFont.Resolve"/> for the empty-default → built-in-body-face mapping.</summary>
    /// <param name="taskFontFamily">The player's stored <c>TaskFontFamily</c> (empty = built-in default).</param>
    /// <param name="baseFontSize">The tab's scaled base font size (e.g. <c>BaseWindowFontSize * scale</c>).</param>
    public static TextStyle Style(string? taskFontFamily, float baseFontSize) =>
        new() { FontFamily = ScribeTaskFont.Resolve(taskFontFamily), FontSize = baseFontSize };

    /// <summary>Wraps <paramref name="child"/> in a <see cref="DefaultTextStyle"/> rooted with
    /// <see cref="Style"/>, so the whole subtree inherits the player's font + base size.</summary>
    public static DefaultTextStyle Wrap(string? taskFontFamily, float baseFontSize, Widget child) =>
        new(Style(taskFontFamily, baseFontSize), child);
}
