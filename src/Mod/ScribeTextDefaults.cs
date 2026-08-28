using Gui.Rendering.Text;          // TextStyle
using Gui.Widgets.Basic.Theming;   // DefaultTextStyle
using Gui.Widgets.Framework;       // Widget

namespace Scribe;

/// <summary>
/// Roots a <b>task-text</b> tab's widget subtree in a <see cref="DefaultTextStyle"/> carrying the
/// player's Task Text Font and window-scaled pegged size, so descendant <c>Text</c> widgets inherit
/// them instead of each threading <c>FontFamily = ScribeTaskFont.Resolve(...)</c> by hand
/// (adopt-libgui-31-improvements). Settings chrome uses <see cref="WrapSettingsChrome"/> instead.
/// 3.1.0's <c>Text.Build</c> does <c>StyleOverride?.Merge(DefaultTextStyle.Of(context))</c>, so a partial
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
    /// <summary>The ancestor style: the resolved Task Text Font family plus the Caudex-matched
    /// <em>layout</em> size (<see cref="ScribeTaskFont.LayoutSize"/> — line-box scale only, no optical).
    /// Stock <c>Text</c> then reports Caudex's line-box; <see cref="ScribeTaskFont.OffsetWrap"/> applies
    /// optical scale and sit as paint-only transforms. Custom painters still draw at
    /// <see cref="ScribeTaskFont.EffectiveSize"/>.</summary>
    /// <param name="taskFontFamily">The player's stored <c>TaskFontFamily</c> (empty = built-in default).</param>
    /// <param name="baseFontSize">The tab's scaled <em>nominal</em> font size (e.g. <c>BaseWindowFontSize * scale</c>).</param>
    public static TextStyle Style(string? taskFontFamily, float baseFontSize) =>
        new()
        {
            FontFamily = ScribeTaskFont.Resolve(taskFontFamily),
            FontSize = ScribeTaskFont.LayoutSize(taskFontFamily, baseFontSize),
        };

    /// <summary>Wraps <paramref name="child"/> in a <see cref="DefaultTextStyle"/> rooted with
    /// <see cref="Style"/>, so the whole subtree inherits the player's font + base size.</summary>
    public static DefaultTextStyle Wrap(string? taskFontFamily, float baseFontSize, Widget child) =>
        new(Style(taskFontFamily, baseFontSize), child);

    /// <summary>Settings chrome: the player's LibGUI default body face at the unscaled settings size.
    /// Does NOT follow Task Text Font or Window Text Size — those knobs live on this form and must not
    /// restyle the form itself (peg-task-fonts-to-caudex playtest).</summary>
    public static DefaultTextStyle WrapSettingsChrome(Widget child) =>
        new(new TextStyle
        {
            FontFamily = ScribeTaskFont.DefaultFamily,
            FontSize = ScribeRowConstants.BaseSettingsFontSize,
        }, child);
}
