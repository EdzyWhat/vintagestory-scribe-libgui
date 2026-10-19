using Gui.Rendering.Text;          // TextStyle
using Gui.Widgets.Basic.Theming;   // DefaultTextStyle
using Gui.Widgets.Framework;       // Widget

namespace Scribe;

/// <summary>
/// Roots a <b>task-text</b> tab's subtree in a <see cref="DefaultTextStyle"/> carrying the player's Task Text
/// Font and window-scaled size, so descendant <c>Text</c> widgets inherit them (settings chrome uses
/// <see cref="WrapSettingsChrome"/> instead). <c>Text.Build</c> does
/// <c>StyleOverride?.Merge(DefaultTextStyle.Of(context))</c>, so a partial override (e.g. just <c>Color</c>)
/// inherits family + size from here.
///
/// <para><b>Merge landmine (verified from the shipped <c>Gui.dll</c>):</b> <c>override.Merge(base)</c> takes an
/// override field only when it differs from <c>default(TextStyle)</c>, so a widget setting a field to its
/// DEFAULT value (<c>SoftWrap = false</c>, <c>FontSize = 0</c>) would silently inherit. Hence this ancestor
/// carries ONLY <see cref="TextStyle.FontFamily"/> and a base <see cref="TextStyle.FontSize"/>.</para>
/// </summary>
internal static class ScribeTextDefaults
{
    /// <summary>The ancestor style: the resolved Task Text Font family plus the Caudex-matched <em>layout</em>
    /// size (<see cref="ScribeTaskFont.LayoutSize"/>, line-box scale only). <see cref="ScribeTaskFont.OffsetWrap"/>
    /// applies optical scale and sit as paint-only transforms; custom painters draw at
    /// <see cref="ScribeTaskFont.EffectiveSize"/>.</summary>
    /// <param name="taskFontFamily">The stored <c>TaskFontFamily</c> (empty = built-in default).</param>
    /// <param name="baseFontSize">The tab's scaled <em>nominal</em> font size.</param>
    public static TextStyle Style(string? taskFontFamily, float baseFontSize) =>
        new()
        {
            FontFamily = ScribeTaskFont.Resolve(taskFontFamily),
            FontSize = ScribeTaskFont.LayoutSize(taskFontFamily, baseFontSize),
        };

    /// <summary>Wraps <paramref name="child"/> in a <see cref="DefaultTextStyle"/> rooted with <see cref="Style"/>.</summary>
    public static DefaultTextStyle Wrap(string? taskFontFamily, float baseFontSize, Widget child) =>
        new(Style(taskFontFamily, baseFontSize), child);

    /// <summary>Settings chrome: Scribe's own bundled default face (<see cref="ScribeTaskFont.DefaultFamily"/>)
    /// at the unscaled settings size. Does NOT follow Task Text Font or Window Text Size — those knobs
    /// live on this form and must not restyle the form itself.</summary>
    public static DefaultTextStyle WrapSettingsChrome(Widget child) =>
        new(new TextStyle
        {
            FontFamily = ScribeTaskFont.DefaultFamily,
            FontSize = ScribeRowConstants.BaseSettingsFontSize,
        }, child);

    /// <summary>Roots a surface that does NOT follow the Task Text Font (HUD chrome, dev-tuning dialogs,
    /// Task Notice / quest popups) in <see cref="ScribeTaskFont.DefaultFamily"/>, instead of LibGUI's
    /// <c>"sans-serif"</c> sentinel (a platform-dependent system-font lookup; see VSAPI-NOTES.md "Custom TTF
    /// fonts in the GUI"). Carries ONLY <c>FontFamily</c>; descendants set their own <c>FontSize</c>.</summary>
    public static DefaultTextStyle WrapChrome(Widget child) =>
        new(new TextStyle { FontFamily = ScribeTaskFont.DefaultFamily }, child);
}
