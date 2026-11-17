using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Basic;         // Text, Padding
using Gui.Widgets.Framework;     // Widget
using Gui.Widgets.Layout;        // SizedBox, Padding
using Scribe.Core;

namespace Scribe;

/// <summary>A dated task's chip, formatted by the dialog (where the world and the Date order live): the line of
/// text and the status that picks its color.</summary>
internal readonly record struct ScribeRowChip(string Text, ScribeDateStatusKind Kind);

/// <summary>Builds the one steady line a read or editor row shows under its task text for a dated task. Always
/// returns a widget (an empty box without a chip) so the row's column keeps the same structure when a date is
/// added or removed and the text field beside it is never remounted.</summary>
internal static class ScribeDateChipView
{
    private const float SizeScale = 0.82f;
    private const float BottomGap = 2f;

    public static Widget Build(ScribeRowChip? chip, ScribeRowStyle style)
    {
        if (chip is not { } c) return new SizedBox(height: 0f);

        var textStyle = new TextStyle { Color = ScribeRowConstants.DateChipColor(c.Kind), SoftWrap = true };
        return new Padding(
            EdgeInsets.Only(left: style.FieldPadX, bottom: BottomGap),
            child: ScribeTaskFont.OffsetWrap(style.TaskFontFamily, style.FontSize * SizeScale, new Text(c.Text, textStyle)));
    }
}
