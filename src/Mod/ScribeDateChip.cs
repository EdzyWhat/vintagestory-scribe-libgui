using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Basic;         // Text, Padding
using Gui.Widgets.Framework;     // Widget
using Gui.Widgets.Layout;        // SizedBox, Padding
using Scribe.Core;
using Vintagestory.API.Common;   // IWorldAccessor
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>A dated task's chip, formatted by the dialog (where the world and the Date order live): the line of
/// text and the status that picks its color.</summary>
internal readonly record struct ScribeRowChip(string Text, ScribeDateStatusKind Kind);

/// <summary>Builds the chip every surface shows for a dated task, so the wording, plurals and the date-only
/// rule for a checked task have one definition.</summary>
internal static class ScribeRowChips
{
    /// <summary>The chip for a task with <paramref name="date"/>, or null when it is undated or not completable.
    /// The status is relative to the world's today.</summary>
    public static ScribeRowChip? For(IWorldAccessor world, ScribeDate? date, bool done, bool completable, ScribeDateOrder order)
    {
        var shape = ScribeWorldCalendar.Shape(world);
        if (ScribeDateRules.Classify(date, done, completable, ScribeWorldCalendar.Today(world), shape) is not { } status)
            return null;

        var text = ScribeDateChipText.For(status);
        string formatted = ScribeWorldCalendar.Format(world, date!.Value, order);
        string line = text.StatusLangKey is null
            ? Lang.Get("scribe:" + text.TemplateLangKey, formatted)
            : Lang.Get("scribe:" + text.TemplateLangKey,
                Lang.Get("scribe:" + text.StatusLangKey, text.Count), formatted);
        return new ScribeRowChip(line, status.Kind);
    }
}

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
