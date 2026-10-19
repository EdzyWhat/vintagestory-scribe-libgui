using System;
using System.Collections.Generic;
using System.Diagnostics;        // Conditional (DEBUG-only scroll trace)
using System.Linq;
using Gui;                       // GuiDialogBlockEntityBase, WindowConfig
using Gui.Core.Framework;        // RenderObject, RenderProxyBox
using Gui.Core.Layout;           // MainAxisSize
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame, VsIcon, Container, Button
using Gui.Widgets.Events;        // PointerEvent
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key, SingleChildWidget
using Gui.Widgets.Input;         // Checkbox, FocusNode, GestureDetector, MouseRegion, Dropdown, DropdownItem
using Gui.Widgets.Inventory;     // ItemStackDisplay (Tracker/Link item icon)
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, Align, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable, Scrollbar
using OpenTK.Mathematics;        // Vector2
using SkiaSharp;                 // SKBitmap (assigned-task stamp raster)
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // ItemStack (Tracker/Link display item)
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

internal static class ScribeRowControlNudge
{
    /// <summary>Family of the dialog TITLE text: "Caudex", the bundled serif registered in
    /// <see cref="ScribeModSystem.RegisterCustomFonts"/>. Only the title and in-dialog buttons use
    /// unscaled Caudex; task-row text uses the player's face laid out against Caudex's line-box (see
    /// <see cref="TextLineHeight"/>). If registration fails, <c>TextLayoutHelper</c> falls back to a system face.</summary>
    internal const string TitleFontFamily = "Caudex";

    /// <summary>Single-line input height at the style's nominal font size: the Caudex-pegged "Ag" line
    /// height plus the field's top+bottom padding, mirroring <c>ScribeMultilineFieldRender.PerformLayout</c>
    /// for one line. Independent of the selected task font.</summary>
    private static float SingleLineInputHeight(ScribeRowStyle style)
        => TextLineHeight(style.FontSize) + style.FieldPadY * 2f;

    /// <summary>The bare "Ag" line height at <paramref name="fontSize"/>, always Caudex's Skia line-box.
    /// Caps a Tracker/Link icon's LAYOUT height to one text line so an oversized icon paints larger
    /// without growing the row (see <see cref="ScribeLinkIcon"/>). The read view's reserved height and the
    /// editor field must agree on this number.</summary>
    public static float TextLineHeight(float fontSize) => ScribeTaskFont.LineHeight(fontSize);

    /// <summary>One line of an item-row NAME: cuneiform's ratio-boosted line on the tablet path, else the
    /// Latin "Ag" line. Distinguishes a wrapping name from a single-line one, so short names center on the
    /// icon without lifting the first line of a wrapped block above it.</summary>
    public static float ItemNameLineHeight(ScribeRowStyle style)
        => style.UseCuneiform
            ? style.FontSize * CuneiformMetrics.LineHeightRatio * CuneiformMetrics.GlyphDrawScale
            : TextLineHeight(style.FontSize);

    /// <summary>The visual band shared by an item row's icon, short label, and leading controls. Static
    /// VS Quest objectives use their bullseye/item-specific footprint so every caller centers against
    /// the art actually rendered.</summary>
    public static float ItemVisualBandHeight(
        ScribeRowStyle style, string? linkTarget = null,
        bool isStaticVsQuestObjective = false, ItemStack? displayStack = null)
    {
        float iconSize = ScribeRowConstants.ItemIconSize
            * (style.ControlSize / ScribeRowConstants.RowCheckboxSize);
        if (isStaticVsQuestObjective)
            return MathF.Max(
                ItemNameLineHeight(style),
                ScribeLinkIcon.ObjectiveVisualSize(iconSize, displayStack));
        return ScribeLinkTarget.IsQuest(linkTarget)
            ? ItemNameLineHeight(style)
            : ScribeLinkIcon.VisualSize(iconSize, linkTarget);
    }

    /// <summary>Extra downward optical offset for item-row checkbox/grip, in ems of
    /// <see cref="ScribeRowStyle.FontSize"/>, applied after centering on the icon band so it tracks text
    /// size rather than being a fixed pixel nudge.</summary>
    // TEMPORARY EXPERIMENT (Option 2, tools/row-leading-slot-alignment).
    internal const float ItemControlOpticalNudgeEm = 0f;

    /// <summary>Same optical-offset knob as <see cref="ItemControlOpticalNudgeEm"/>, for Task/Note
    /// rows. 0 keeps them geometrically centered on the one-line field.</summary>
    internal const float TaskControlOpticalNudgeEm = 0f;

    /// <summary>Top offset for the drag grip and task checkbox (both
    /// <see cref="ScribeRowStyle.CheckboxSize"/> tall): centers them on the band their row's content
    /// actually centers on, plus a font-relative optical nudge. May go negative when the checkbox exceeds
    /// its band; the control then overflows rather than growing the row.</summary>
    // TEMPORARY EXPERIMENT (Option 2 / Technique B / HeightNeutral, tools/row-leading-slot-alignment).
    // The band per row kind:
    //  - Plain Task/Note: the padded field height (SingleLineInputHeight), since
    //    ScribeMultilineFieldRender PadY-insets the text.
    //  - Quest Link: ItemNameLineHeight (cuneiform-aware on the tablet). Its icon renders in this same
    //    leading slot, so it must agree with the text's band, not the Latin TextLineHeight, or it rides
    //    high against a shorter band on the tablet.
    //  - Tracker/Craft/Link: ScribeLinkIcon.VisualSize (the real per-icon-kind band, item vs book glyph);
    //    centering on bare TextLineHeight would leave the checkbox riding high above the taller icon.
    public static float CheckboxAndGripTop(
        ScribeRowStyle style, bool itemRow = false, string? linkTarget = null,
        bool isStaticVsQuestObjective = false, ItemStack? displayStack = null)
    {
        float band;
        if (!itemRow)
        {
            band = SingleLineInputHeight(style);
        }
        else
        {
            band = ItemVisualBandHeight(style, linkTarget, isStaticVsQuestObjective, displayStack);
        }
        float centered = (band - style.CheckboxSize) / 2f;
        float nudgeEm = itemRow ? ItemControlOpticalNudgeEm : TaskControlOpticalNudgeEm;
        return centered + style.FontSize * nudgeEm;
    }

    /// <summary>Opacity multiplier for a muted/disabled checkbox's tick, border and background; matches
    /// the 45% dim <see cref="ScribeInboxRow.BuildAcceptControl"/> uses for a disabled Accept label.</summary>
    private const float DisabledCheckboxOpacity = 0.45f;

    /// <summary>Builds a task-row completion checkbox. When <see cref="ScribeRowStyle.CheckTickColor"/> is
    /// set, ONLY the tick's <c>CheckColor</c> is overridden on a copy of the theme's style; null leaves
    /// the ambient <c>CheckboxStyle</c> unchanged. Shared by read, editor and pinned rows so their ticks
    /// cannot drift.
    ///
    /// <para>A null <paramref name="onChanged"/> yields an inert checkbox with tick, border and
    /// background muted to <see cref="DisabledCheckboxOpacity"/> (the Task Notice's read-only rows),
    /// taking priority over <see cref="ScribeRowStyle.CheckTickColor"/>.</para></summary>
    public static Checkbox BuildTaskCheckbox(
        BuildContext context, ScribeRowStyle style, bool value, Action<bool>? onChanged)
    {
        CheckboxStyle? resolvedStyle;
        if (onChanged is null)
        {
            var baseStyle = Theme.Of(context).CheckboxStyle;
            resolvedStyle = baseStyle with
            {
                CheckColor = baseStyle.CheckColor with { W = baseStyle.CheckColor.W * DisabledCheckboxOpacity },
                BorderColor = baseStyle.BorderColor with { W = baseStyle.BorderColor.W * DisabledCheckboxOpacity },
                BackgroundColor = baseStyle.BackgroundColor with { W = baseStyle.BackgroundColor.W * DisabledCheckboxOpacity },
            };
        }
        else
        {
            resolvedStyle = style.CheckTickColor is { } tick
                ? Theme.Of(context).CheckboxStyle with { CheckColor = tick }
                : null;
        }

        return new Checkbox(
            value: value,
            onChanged: onChanged,
            size: style.CheckboxSize,
            style: resolvedStyle);
    }

    /// <summary>The row's leading-slot control: the quest-marker icon for a Quest Link row (its checkbox
    /// would mislead, since toggling it does not affect the quest), else the ordinary
    /// <see cref="BuildTaskCheckbox"/>. The icon uses the same <see cref="ScribeRowStyle.CheckboxSize"/>
    /// footprint so the leading column does not reflow, colored by <see cref="ScribeRowStyle.QuestLinkColor"/>
    /// or <see cref="ScribeTheme.QuestLinkAccent"/>. Used by Read, Editor, Pinned and HUD; the
    /// Assignment-stage row's checkbox is a SELECTION control and does not use it.</summary>
    public static Widget BuildLeadingControl(
        BuildContext context, ScribeRowStyle style, string? linkTarget, bool value, Action<bool>? onChanged)
    {
        if (ScribeLinkTarget.IsQuest(linkTarget))
        {
            Vector4 questColor = style.QuestLinkColor ?? ScribeTheme.QuestLinkAccent;
            return new ScribeVsIconGlyph("scribequest", style.CheckboxSize, questColor);
        }

        return BuildTaskCheckbox(context, style, value, onChanged);
    }

    /// <summary>The grip glyph's insets: the vertical top-nudge shared with the checkbox, plus a NEGATIVE
    /// right inset cancelling the Row's <see cref="ScribeRowStyle.CheckboxTextGap"/> so the grip sits flush
    /// against the checkbox. Used identically for editor/pin grips and the read/frozen grip-column spacers
    /// so rows stay column-aligned across a view switch. <paramref name="linkTarget"/> is threaded into
    /// <see cref="CheckboxAndGripTop"/> because the grip shares the leading column with the checkbox/quest
    /// icon and must center on the same band.</summary>
    public static EdgeInsets GripInsets(
        ScribeRowStyle style, bool itemRow = false, string? linkTarget = null,
        bool isStaticVsQuestObjective = false, ItemStack? displayStack = null)
        => EdgeInsets.Only(
            top: CheckboxAndGripTop(style, itemRow, linkTarget, isStaticVsQuestObjective, displayStack),
            right: -style.CheckboxTextGap);

    /// <summary>Absolute top offset (from the row's top edge) centering a floating pin/delete button's
    /// DRAWN box (<see cref="ScribeRowButton.BoxShrink"/> px shorter than
    /// <see cref="ScribeRowStyle.ControlSize"/>) on the row's first text line, which sits
    /// <c>RowVerticalPadding</c> below the top. A plain Task/Note row's first line is the padded input
    /// field (<see cref="SingleLineInputHeight"/>); an item row's (<paramref name="itemRow"/>) is its NAME
    /// at the cuneiform-aware <see cref="ItemNameLineHeight"/>.</summary>
    public static float FloatingButtonTop(ScribeRowStyle style, bool itemRow = false)
    {
        float boxHeight = style.ControlSize - ScribeRowButton.BoxShrink;
        float band = itemRow ? ItemNameLineHeight(style) : SingleLineInputHeight(style);
        float lineCenter = style.RowVerticalPadding + band / 2f;
        return MathF.Max(0f, lineCenter - boxHeight / 2f);
    }
}

// ── Shared per-row control primitives ──

/// <summary>A bare (non-interactive) VS icon glyph drawn by registered <c>CustomIcons</c> code via
/// <see cref="VsIcon"/>; pointer handling lives in a wrapping <see cref="GestureDetector"/>.
///
/// <para>Deliberately <see cref="VsIcon"/> (icon-by-code), not LibGUI's <see cref="Icon"/>/<see cref="IconButton"/>
/// (SVG-by-path): <see cref="Icon"/> loads via <c>SkiaAssetLoader.LoadSvg</c>, which calls
/// <c>Assets.TryGet</c> without <c>loadAsset: true</c>, and VS nulls every non-patched asset's <c>Data</c>
/// after startup, so our icons would fail to draw. <see cref="VsIcon"/> goes through
/// <c>IconUtil.DrawIconInt</c> to the mod's self-healing <c>CustomIcons</c> delegate (see
/// <c>ScribeModSystem.RegisterSvgIcon</c> / VSAPI-NOTES.md).</para></summary>
internal sealed class ScribeVsIconGlyph : StatelessWidget
{
    private readonly string iconName;
    private readonly float size;
    private readonly Vector4 color;

    public ScribeVsIconGlyph(string iconName, float size, Vector4 color)
    {
        this.iconName = iconName;
        this.size = size;
        this.color = color;
    }

    public override Widget Build(BuildContext context) => new VsIcon(iconName, size, color);
}

/// <summary>Shared styling for a writing-station's watermarked/restricted inventory slots, so the
/// Assignment Desk and the Inbox block render identically. The watermark (a <see cref="ScribeVsIconGlyph"/>)
/// is painted UNDER the slot and muted by the slot's semi-opaque veil fill (see
/// <see cref="GuiDialogScribeScriptorium.BuildWatermarkedSlot"/> for why under beats over); a null
/// <paramref name="watermarkIcon"/> renders a fully-open slot with no glyph.</summary>
internal static class ScribeInventorySlotStyle
{
    /// <summary>Slot edge length in pixels; matches <see cref="GuiDialogScribeAssignmentDesk.SlotSize"/>.</summary>
    public const float SlotSize = 48f;

    /// <summary>Watermark glyph size as a fraction of <see cref="SlotSize"/>; matches
    /// <see cref="GuiDialogScribeAssignmentDesk.WatermarkScale"/>.</summary>
    private const float WatermarkScale = 0.66f;

    public static Widget Build(ItemSlot slot, SlotController controller, ColorScheme colors,
        ScribeAmbientLightSampler.Shade shade, string? watermarkIcon)
    {
        Vector4 veilColor = colors.Surface with { W = 0.66f };
        var slotStyle = ItemSlotStyle.Default with { Size = SlotSize, BackgroundColor = veilColor };
        Widget slotWidget = new ScribeDocumentSlot(slot, controller, slotStyle, colors, shade);
        if (watermarkIcon is null) return slotWidget;

        float watermarkGlyph = SlotSize * WatermarkScale;
        float watermarkInset = (SlotSize - watermarkGlyph) / 2f;
        return new Stack(children: new Widget[]
        {
            new Positioned(
                left: watermarkInset, top: watermarkInset, width: watermarkGlyph, height: watermarkGlyph,
                child: new ScribeVsIconGlyph(watermarkIcon, watermarkGlyph, colors.Primary)),
            slotWidget,
        });
    }
}

/// <summary>The marker icon (<c>scribeassignment</c> glyph, or a raster stamp) for an accepted
/// player-to-player assignment, shown after the checkbox on Read, Editor and Pin Tab rows (tablets
/// inherit it via the same row widgets). Unlike the grip's zero-opacity reserved-width spacer, this column
/// takes width only when added, so callers must gate <c>children.Add(...)</c> on the row's
/// <c>IsAcceptedAssignment</c>. Hovering shows a two-line tooltip (assigner + assigned date; accepted
/// date) when that data is supplied.</summary>
internal static class ScribeAssignedTaskIcon
{
    /// <summary>The full-color raster stamp asset, resolved once per dialog rebuild by the caller owning
    /// <c>modSystem</c> (<see cref="ScribeModSystem.GetGuiTextureBitmap"/>) and passed down as
    /// <c>stampBitmap</c> so this file stays API-free.</summary>
    public static readonly AssetLocation Asset = new("scribe", "textures/gui/scribe-assigned-stamp.png");

    /// <summary><paramref name="context"/>/<paramref name="currentShade"/> build the hover tooltip (via
    /// <see cref="ScribeGlobalTint.ShadedTooltip"/>) and are required only with
    /// <paramref name="assignerName"/>; a null name (a legacy pin snapshot with no provenance) renders the
    /// plain icon with no tooltip.</summary>
    public static Widget Build(ScribeRowStyle style, Vector4 color, bool itemRow = false, SKBitmap? stampBitmap = null,
        BuildContext? context = null, ScribeAmbientLightSampler.Shade? currentShade = null,
        string? assignerName = null, string? assignedDate = null, string? acceptedDate = null)
    {
        Widget icon = new Padding(
            EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(style, itemRow)),
            child: stampBitmap is not null
                ? new ScribeRasterIcon(stampBitmap, style.ControlSize)
                : new ScribeVsIconGlyph("scribeassignment", style.ControlSize, color));

        if (assignerName is null || context is null || currentShade is null) return icon;

        var theme = Theme.Of(context.Value);
        var lines = new List<Widget>
        {
            new Text(Lang.Get("scribe:assignment-marker-tooltip-assigned", assignerName, assignedDate ?? ""),
                new TextStyle { FontSize = 13, SoftWrap = true, Color = theme.ColorScheme.OnBackground }),
        };
        if (acceptedDate is not null)
        {
            lines.Add(new Text(Lang.Get("scribe:assignment-marker-tooltip-accepted", acceptedDate),
                new TextStyle { FontSize = 13, SoftWrap = true, Color = theme.ColorScheme.OnBackground }));
        }

        return ScribeGlobalTint.ShadedTooltip(
            child: icon,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Column(spacing: 2f, crossAxisAlignment: CrossAxisAlignment.Start,
                    mainAxisSize: MainAxisSize.Min, children: lines)),
            baseTheme: theme,
            shade: currentShade.Value);
    }
}

/// <summary>An external mod's opaque hover-detail marker, shown after the checkbox/assignment marker on
/// Read, Editor and Pin Tab rows for any block with a non-empty <see cref="Scribe.Core.ScribeBlock.ExtraInfo"/>.
/// Parallel to, not derived from, <see cref="ScribeAssignedTaskIcon"/> (ExtraInfo is an independent
/// field). Never used by the HUD. The tooltip shows <c>extraInfo</c> verbatim since its content is opaque
/// by contract.</summary>
internal static class ScribeExternalInfoIcon
{
    /// <summary>The raster stamp asset for this marker.</summary>
    public static readonly AssetLocation Asset = new("scribe", "textures/gui/scribe-external-stamp.png");

    /// <summary><paramref name="context"/>/<paramref name="currentShade"/> build the hover tooltip and are
    /// required only with a non-empty <paramref name="extraInfo"/>; the caller gates this call on a
    /// non-empty value.</summary>
    public static Widget Build(ScribeRowStyle style, Vector4 color, bool itemRow = false, SKBitmap? stampBitmap = null,
        BuildContext? context = null, ScribeAmbientLightSampler.Shade? currentShade = null,
        string? extraInfo = null)
    {
        Widget icon = new Padding(
            EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(style, itemRow)),
            child: stampBitmap is not null
                ? new ScribeRasterIcon(stampBitmap, style.ControlSize)
                : new ScribeVsIconGlyph("scribeinfo", style.ControlSize, color));

        if (string.IsNullOrEmpty(extraInfo) || context is null || currentShade is null) return icon;

        var theme = Theme.Of(context.Value);
        return ScribeGlobalTint.ShadedTooltip(
            child: icon,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(extraInfo,
                    new TextStyle { FontSize = 13, SoftWrap = true, Color = theme.ColorScheme.OnBackground })),
            baseTheme: theme,
            shade: currentShade.Value);
    }
}

/// <summary>Builds the leading icon for a Tracker/Link row: an <see cref="ItemStackDisplay"/> of the
/// referenced item, or, for a guide-page or quest Link (no item to draw), the <c>scribebook</c> glyph
/// tinted <paramref name="bookColor"/>. Shared by read, editor, Pin Tab and HUD.
///
/// <para>The item icon grows by <see cref="ItemIconScale"/> and the book glyph shrinks to
/// <see cref="BookGlyphScale"/>. The result is ROW-HEIGHT-NEUTRAL: its layout box is capped to one text
/// line (<paramref name="lineHeight"/>) and the larger art paints centered over it, so a Tracker/Link row
/// matches a single-line Task/Text row.</para></summary>
internal static class ScribeLinkIcon
{
    /// <summary>Item-icon growth over the nominal row control size.</summary>
    private const float ItemIconScale = 1.1f;

    /// <summary>Guide-page book-glyph shrink; the glyph is visually heavy at full control size.</summary>
    // TEMPORARY EXPERIMENT (Option 2, tools/row-leading-slot-alignment).
    private const float BookGlyphScale = 0.65f;

    /// <summary>Static VS Quest criterion marker scale; the bullseye's dense outer ring uses the same
    /// compact footprint as the book glyph.</summary>
    private const float ObjectiveGlyphScale = 0.65f;

    /// <summary>True for a Link with no item to draw (guide-page or quest), which renders the book glyph.</summary>
    private static bool IsBookGlyph(string? linkTarget)
        => ScribeLinkTarget.IsGuidePage(linkTarget) || ScribeLinkTarget.IsQuest(linkTarget);

    public static float VisualSize(float iconSize, string? linkTarget)
        => iconSize * (IsBookGlyph(linkTarget) ? BookGlyphScale : ItemIconScale);

    public static float ObjectiveVisualSize(float iconSize, ItemStack? stack)
        => iconSize * (stack is null ? ObjectiveGlyphScale : ItemIconScale);

    public static Widget Build(ItemStack? stack, string? linkTarget, float iconSize, Vector4 bookColor,
        float lineHeight, bool heightNeutral = true)
    {
        bool guidePage = IsBookGlyph(linkTarget);
        float visual = VisualSize(iconSize, linkTarget);
        Widget art = guidePage
            ? new ScribeVsIconGlyph("scribebook", visual, bookColor)
            : new ItemStackDisplay(stack, width: visual, height: visual, renderSize: 48);
        // Window item rows top-align the icon with the stepper/name, so the icon must occupy its real
        // visual height in layout (HeightNeutral would overflow above the row). The HUD wraps it.
        return heightNeutral ? HeightNeutral(art, visual, lineHeight) : art;
    }

    /// <summary>Builds a static VS Quest objective icon: the resolved item when available, else the
    /// dedicated bullseye marker (avoiding the guide-page book metaphor).</summary>
    public static Widget BuildObjective(ItemStack? stack, float iconSize, Vector4 markerColor,
        float lineHeight, bool heightNeutral = true)
    {
        float visual = ObjectiveVisualSize(iconSize, stack);
        Widget art = stack is null
            ? new ScribeVsIconGlyph("scribeobjective", visual, markerColor)
            : new ItemStackDisplay(stack, width: visual, height: visual, renderSize: 48);
        return heightNeutral ? HeightNeutral(art, visual, lineHeight) : art;
    }

    /// <summary>Wraps a <paramref name="visual"/>-px-square icon so it takes only
    /// <paramref name="lineHeight"/> of vertical LAYOUT space while painting full size, centered on the
    /// line. A <see cref="SizedBox"/> spacer sizes a <see cref="Stack"/> to <c>visual × lineHeight</c>; the
    /// icon is a <see cref="Positioned"/> child with both <c>Width</c> and <c>Height</c> set (which
    /// <c>RenderStack</c> forces exactly), offset up by half the overflow. <c>RenderStack.Paint</c> does
    /// not clip, so the overflow shows. An icon that already fits a line is returned unwrapped.</summary>
    private static Widget HeightNeutral(Widget art, float visual, float lineHeight)
    {
        if (visual <= lineHeight) return art;
        float top = (lineHeight - visual) / 2f; // negative — the icon extends above and below the text line
        return new Stack(children: new Widget[]
        {
            new SizedBox(width: visual, height: lineHeight),
            new Positioned(left: 0f, top: top, width: visual, height: visual, child: art),
        });
    }
}

/// <summary>
/// Vertically centers a child inside <see cref="BandHeight"/> when it is a single line, and leaves a
/// wrapping child top-aligned at its natural height. Item rows stay <c>CrossAxisAlignment.Start</c> so a
/// wrapped name does not float the icon into the middle of the block; this lets a short name sit on the
/// icon's horizon without growing the row.
/// </summary>
internal static class ScribeCenterIfShort
{
    /// <summary>Always-center (counter, stepper): treats the child as short.</summary>
    public static Widget InBand(Widget child, float bandHeight)
        => new ScribeCenterIfShortWidget(oneLineHeight: float.MaxValue, bandHeight, child);

    /// <summary>Centers when the child is at most one line; top-aligns when it wraps.</summary>
    public static Widget Name(Widget child, ScribeRowStyle style, float bandHeight)
        => new ScribeCenterIfShortWidget(ScribeRowControlNudge.ItemNameLineHeight(style), bandHeight, child);
}

internal sealed class ScribeCenterIfShortWidget : SingleChildWidget
{
    public ScribeCenterIfShortWidget(float oneLineHeight, float bandHeight, Widget? child = null,
        Gui.Widgets.Framework.Key? key = null) : base(child, key)
    {
        OneLineHeight = oneLineHeight;
        BandHeight = bandHeight;
    }

    public float OneLineHeight { get; }
    public float BandHeight { get; }

    public override RenderObject CreateRenderObject() => new ScribeCenterIfShortRender
    {
        OneLineHeight = OneLineHeight,
        BandHeight = BandHeight,
    };

    public override void UpdateRenderObject(RenderObject renderObject)
    {
        var ro = (ScribeCenterIfShortRender)renderObject;
        ro.OneLineHeight = OneLineHeight;
        ro.BandHeight = BandHeight;
    }
}

internal sealed class ScribeCenterIfShortRender : RenderProxyBox
{
    public float OneLineHeight { get => field; set => SetProperty(ref field, value, relayout: true); }
    public float BandHeight { get => field; set => SetProperty(ref field, value, relayout: true); }

    protected override void PerformLayout()
    {
        if (Children.Count == 0)
        {
            Size = Constraints.Constrain(Vector2.Zero);
            return;
        }

        var child = Children[0];
        child.Layout(Constraints);
        bool wrapped = child.Size.Y > OneLineHeight * 1.4f;
        // Use the child's own width. A Row gives non-flex children a MaxWidth equal to the leftover row;
        // claiming that made the stepper/counter as wide as the row and zeroed the icon + name.
        // Expanded names already pass a tight width, so child.Size.X is the flex slot in that case.
        float height = (wrapped || BandHeight <= child.Size.Y) ? child.Size.Y : BandHeight;
        child.X = 0f;
        child.Y = height > child.Size.Y ? (height - child.Size.Y) / 2f : 0f;
        Size = Constraints.Constrain(new Vector2(child.Size.X, height));
    }
}

/// <summary>An item NAME label for Tracker/Link/Craft rows, shared by read and editor views. On the
/// tablet cuneiform path (<see cref="ScribeRowStyle.UseCuneiform"/> + a loaded
/// <see cref="ScribeRowStyle.CuneiformBundle"/>) it draws cuneiform strokes that WRAP to width via
/// <see cref="ScribeCuneiformFieldRenderWidget"/> (display-only; a single-line CuneiformText would clip
/// a long name mid-word). Otherwise it falls back to a wrapping <see cref="Text"/>, so a name is never blank.</summary>
internal static class ScribeItemLabel
{
    public static Widget Build(string label, Vector4 color, ScribeRowStyle style)
    {
        if (style.UseCuneiform && style.CuneiformBundle is { } bundle)
        {
            // Display-only: caret and selection are zeroed and hidden. Jitter is seeded from the label so
            // strokes are stable frame-to-frame (no TaskId here; the seed only needs stability). Inner pad
            // is 0: the item row applies only a right FieldPadX, and matching Task-row FieldPadY would drop
            // tablet names ~6px below the checkbox/stepper horizon.
            return new ScribeCuneiformFieldRenderWidget(
                text: label,
                caret: 0,
                selectionAnchor: 0,
                hasFocus: false,
                fontSizeEm: style.FontSize,
                inkColor: color,
                caretColor: Vector4.Zero,
                selectionColor: Vector4.Zero,
                bundle: bundle,
                padX: 0f,
                padY: 0f,
                boxColor: Vector4.Zero,
                borderColor: Vector4.Zero,
                borderThickness: 1f,
                cornerRadii: Vector4.One * 4f,
                caretVisible: false,
                jitterStrength: style.CuneiformJitter,
                jitterSeed: label.GetHashCode(),
                rotationDegrees: style.CuneiformRotation,
                glow: style.CuneiformGlow,
                strokeWeightScale: style.CuneiformStrokeWeightScale,
                glyphDrawScale: CuneiformMetrics.GlyphDrawScale,
                caretHeightScale: CuneiformMetrics.CaretHeightScale);
        }
        return ScribeTaskFont.OffsetWrap(style.TaskFontFamily, style.FontSize,
            new Text(label, new TextStyle { Color = color, SoftWrap = true }));
    }
}

/// <summary>The Tracker "N / N" have/need counter, shared by read view, Pin Tab and HUD. Emphasis is
/// INVERTED: an unsatisfied count is still being worked on, so it reads STRONG
/// (<paramref name="strongColor"/> + bold); a satisfied count reads FADED (<paramref name="mutedColor"/>)
/// with a faint strikethrough over just the number. The strikethrough is a custom thin-line overlay
/// because LibGUI's <c>TextDecoration</c> has only <c>None</c>/<c>Underline</c>; it lives inside this
/// widget, which sizes to the "N / N" text, not the row.</summary>
internal static class ScribeTrackerCounterText
{
    public static Widget Build(int current, int target, bool satisfied, Vector4 strongColor,
        Vector4 mutedColor, float lineHeight, TextStyle? baseStyle = null, System.Func<string, string>? corrupt = null,
        ScribeRowStyle? cuneiform = null)
    {
        string label = $"{current} / {target}";
        if (corrupt != null) label = corrupt(label);

        Vector4 inkColor = satisfied ? mutedColor : strongColor;

        // On the tablet cuneiform path the counter renders as cuneiform strokes, which carry no bold
        // weight, so the strong/muted color (and the strikethrough below) conveys emphasis.
        Widget text;
        if (cuneiform is { UseCuneiform: true, CuneiformBundle: { } bundle } cs)
        {
            text = new CuneiformText(
                text: label,
                fontSizeEm: cs.FontSize,
                inkColor: inkColor,
                bundle: bundle,
                jitterStrength: cs.CuneiformJitter,
                rotationDegrees: cs.CuneiformRotation,
                glow: cs.CuneiformGlow,
                strokeWeightScale: cs.CuneiformStrokeWeightScale,
                glyphDrawScale: CuneiformMetrics.GlyphDrawScale);
        }
        else
        {
            TextStyle style = (baseStyle ?? new TextStyle()) with
            {
                Color = inkColor,
                Weight = satisfied ? FontWeight.Normal : FontWeight.Bold,
            };
            text = new Text(label, style);
            if (cuneiform is { } row)
            {
                text = ScribeTaskFont.OffsetWrap(row.TaskFontFamily, row.FontSize, text);
            }
        }
        if (!satisfied) return text; // in-progress: strong, no strike.

        // Satisfied: faint thin line centered on the single text line, spanning the counter's full width.
        float thickness = MathF.Max(1f, lineHeight * 0.06f);
        Vector4 strike = mutedColor with { W = mutedColor.W * 0.6f };
        return new Stack(children: new Widget[]
        {
            text,
            new Positioned(left: 0f, right: 0f, top: lineHeight / 2f - thickness / 2f, height: thickness,
                child: new Container(style: new BoxStyle { Color = strike })),
        });
    }
}

/// <summary>A floating per-row action button (delete / pin): a bordered, solid-background square with
/// theme-derived resting/hover/press fills wrapping a <see cref="VsIcon"/> glyph, so it floats legibly ON
/// TOP of the row's text. Uses <see cref="VsIcon"/> (icon-by-code), not LibGUI's
/// <see cref="IconButton"/>/<see cref="Icon"/>, whose SVG loading fails on post-startup-unloaded assets
/// (see <see cref="ScribeVsIconGlyph"/>).</summary>
internal sealed class ScribeRowButton : StatefulWidget
{
    public ScribeRowButton(string iconName, Vector4 iconColor, float size, Action onTap, float iconScale = 1f, BoxShadow[]? boxShadows = null, Vector4? activeColor = null, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        IconName = iconName;
        IconColor = iconColor;
        Size = size;
        OnTap = onTap;
        IconScale = iconScale;
        BoxShadows = boxShadows;
        ActiveColor = activeColor;
    }

    public string IconName { get; }
    public Vector4 IconColor { get; }
    /// <summary>Nominal control-column side length (matches the grip/checkbox column). The DRAWN box is
    /// <see cref="BoxShrink"/> px smaller per dimension; the glyph is still sized from this value.</summary>
    public float Size { get; }
    public Action OnTap { get; }

    /// <summary>Multiplier on the GLYPH only (not the box), e.g. the pin +10%; the padding split keeps it
    /// centered. Default 1 = unchanged.</summary>
    public float IconScale { get; }

    /// <summary>Optional shadow(s) painted with the box (forwarded to <c>BoxStyle.BoxShadows</c>). Null =
    /// none; the sidebar nav buttons pass one to read as raised chrome.</summary>
    public BoxShadow[]? BoxShadows { get; }

    /// <summary>Optional "active tab" fill. Null = neutral <c>SurfaceHigh</c> behavior with
    /// <see cref="IconColor"/>. When set the box fills with this color, the glyph is forced to
    /// <see cref="ScribeRowConstants.NavActiveGlyph"/> for contrast, and hover brightens the fill by +10
    /// HSV Brightness. Only the sidebar nav buttons pass it.</summary>
    public Vector4? ActiveColor { get; }

    /// <summary>How much smaller (px, each dimension) the drawn chrome is than <see cref="Size"/>. The
    /// glyph stays sized from the full <see cref="Size"/>, so this only tightens the surrounding padding.
    /// Exposed so the row laying out the buttons can position the pin against the actual box width.</summary>
    public const float BoxShrink = 2f;

    public override State CreateState() => new ScribeRowButtonState();
}

internal sealed class ScribeRowButtonState : State<ScribeRowButton>
{
    private bool hovered;
    private bool pressed;

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        // Opaque (W=1) so the button fully covers the row text it floats over. Neutral path: SurfaceHigh
        // lifted for hover/press. ACTIVE TAB path: fills with ActiveColor (hover +10 HSV V, press -6 V)
        // and forces the glyph to cream for contrast.
        Vector4 bg;
        Vector4 glyphColor = Widget.IconColor;
        if (Widget.ActiveColor is Vector4 active)
        {
            float vShift = pressed ? -6f : hovered ? 10f : 0f;
            bg = (vShift == 0f ? active : ScribeRowConstants.ShiftBrightness(active, vShift)) with { W = 1f };
            glyphColor = ScribeRowConstants.NavActiveGlyph;
        }
        else
        {
            Vector4 baseBg = colors.SurfaceHigh with { W = 1f };
            float lift = pressed ? -0.06f : hovered ? 0.10f : 0f;
            bg = new(
                Math.Clamp(baseBg.X + lift, 0f, 1f),
                Math.Clamp(baseBg.Y + lift, 0f, 1f),
                Math.Clamp(baseBg.Z + lift, 0f, 1f),
                1f);
        }

        // The glyph is sized from the FULL nominal Size so shrinking the box leaves the icon untouched;
        // IconScale grows just the glyph, and the padding split re-centers it.
        float pad = MathF.Max(3f, Widget.Size * 0.18f); // small padding; glyph fills the rest
        float glyph = (Widget.Size - pad * 2f) * Widget.IconScale;

        // The drawn box is BoxShrink smaller; the padding absorbs the difference to keep the glyph centered.
        float box = Widget.Size - ScribeRowButton.BoxShrink;
        float drawnPad = MathF.Max(0f, (box - glyph) / 2f);

        return new GestureDetector(
            onTap: _ => Widget.OnTap(),
            onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
            onExit: _ => { if (hovered || pressed) SetState(() => { hovered = false; pressed = false; }); },
            onPress: _ => { if (!pressed) SetState(() => pressed = true); },
            onRelease: _ => { if (pressed) SetState(() => pressed = false); },
            child: new Container(
                style: new BoxStyle
                {
                    Color = bg,
                    Width = box,
                    Height = box,
                    CornerRadius = new Vector4(3f),
                    BorderThickness = 1f,
                    BorderColor = colors.Border,
                    Padding = EdgeInsets.All(drawnPad),
                    BoxShadows = Widget.BoxShadows,
                },
                child: new VsIcon(Widget.IconName, glyph, glyphColor)));
    }
}
