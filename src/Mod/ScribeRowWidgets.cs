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
    /// <summary>The family Scribe's dialog TITLE text is drawn in: "Caudex", the mod's bundled humanist
    /// serif, registered with LibGUI's Skia font registry in
    /// <see cref="ScribeModSystem.RegisterCustomFonts"/> (prove-bundled-font-seam). Only the title (and
    /// in-dialog buttons) uses unscaled Caudex; task-row text uses the player's selected face, laid out
    /// against Caudex's line-box (see <see cref="TextLineHeight"/>). If registration fails the family
    /// falls back to a system face via <c>TextLayoutHelper</c>, so the title still renders.</summary>
    internal const string TitleFontFamily = "Caudex";

    /// <summary>Measured single-line input height at the style's current <em>nominal</em> font size: the
    /// Caudex-pegged "Ag" line height plus the field's top+bottom internal padding — mirroring
    /// <c>ScribeMultilineFieldRender.PerformLayout</c>'s <c>lineCount * lineHeight + PadY*2</c> for one
    /// line. Independent of the selected task font (peg-task-fonts-to-caudex).</summary>
    private static float SingleLineInputHeight(ScribeRowStyle style)
        => TextLineHeight(style.FontSize) + style.FieldPadY * 2f;

    /// <summary>The bare "Ag" line height at <paramref name="fontSize"/> — always Caudex's Skia line-box
    /// (peg-task-fonts-to-caudex), not the selected family's native Y. Used to cap a Tracker/Link icon's
    /// LAYOUT height to a single text line so an oversized (row-height-neutral) icon paints larger without
    /// growing the row (add-tracker-link-tasks 7.11f — see <see cref="ScribeLinkIcon"/>). Read reserved
    /// height and the editor field must agree on this number.</summary>
    public static float TextLineHeight(float fontSize) => ScribeTaskFont.LineHeight(fontSize);

    /// <summary>One line of an item-row NAME: cuneiform's ratio-boosted line on the tablet path, else the
    /// Latin "Ag" line. Used to tell a wrapping name from a single-line one so we can center short names
    /// on the icon without centering a wrapped block (which would lift the first line above the icon).</summary>
    public static float ItemNameLineHeight(ScribeRowStyle style)
        => style.UseCuneiform
            ? style.FontSize * CuneiformMetrics.LineHeightRatio
            : TextLineHeight(style.FontSize);

    /// <summary>Extra downward optical offset for item-row checkbox/grip, in ems of
    /// <see cref="ScribeRowStyle.FontSize"/>. Applied after centering on the icon band so a "smidge"
    /// tracks text size (≈1.5px at 15pt, ≈2.8px on a tablet cuneiform line) instead of a fixed pixel
    /// nudge. Raise/lower this one constant to tune; Task/Note rows stay at geometric center of the
    /// one-line field (set <see cref="TaskControlOpticalNudgeEm"/> if those also read high).</summary>
    internal const float ItemControlOpticalNudgeEm = 0.1f;

    /// <summary>Same optical-offset knob as <see cref="ItemControlOpticalNudgeEm"/>, for Task/Note
    /// rows. 0 keeps them geometrically centered on the one-line field.</summary>
    internal const float TaskControlOpticalNudgeEm = 0f;

    /// <summary>Down-nudge for the drag grip and the task checkbox (both <see cref="ScribeRowStyle.CheckboxSize"/>
    /// tall). On a Task/Note row this centers them on a one-line text field. On an item row the name/stepper
    /// sit in the (taller) icon band, so the same Latin-field formula leaves the controls a smidge high —
    /// center on that icon band instead, then add a font-relative optical offset. Both paths scale with
    /// <see cref="ScribeRowStyle.FontSize"/> (<see cref="ScribeRowStyle.ControlSize"/> / icon size track it).</summary>
    public static float CheckboxAndGripTop(ScribeRowStyle style, bool itemRow = false)
    {
        float centered;
        if (!itemRow)
        {
            centered = MathF.Max(0f, (SingleLineInputHeight(style) - style.CheckboxSize) / 2f);
            return centered + style.FontSize * TaskControlOpticalNudgeEm;
        }

        float iconBand = ScribeRowConstants.ItemIconSize
            * (style.ControlSize / ScribeRowConstants.RowCheckboxSize)
            * 1.1f; // ScribeLinkIcon.ItemIconScale — item rows always show the item icon
        centered = MathF.Max(0f, (iconBand - style.CheckboxSize) / 2f);
        return centered + style.FontSize * ItemControlOpticalNudgeEm;
    }

    /// <summary>Opacity multiplier applied to a muted/disabled checkbox's tick, border, and background
    /// (refine-task-notice-ux 3.5) — matches the 45%-opacity dim <see cref="ScribeInboxRow.BuildAcceptControl"/>
    /// already uses for a disabled Accept button's label, the existing "can't tap this" convention.</summary>
    private const float DisabledCheckboxOpacity = 0.45f;

    /// <summary>Build a task-row completion checkbox, applying the row style's optional tick-color override
    /// (<see cref="ScribeRowStyle.CheckTickColor"/>, refine-chalkboard §11). When it is null (every surface
    /// except the chalkboard) the ambient theme's <c>CheckboxStyle</c> is used unchanged, so the tick keeps
    /// its usual accent. When set, ONLY the tick's <c>CheckColor</c> is overridden — the box background,
    /// border, and focus behavior all stay the resolved theme defaults (we copy <c>Theme.Of(context)</c>'s
    /// style and change one field), so the chalkboard's completed-task tick reads chalk-white to match its
    /// row text without hardcoding white in this shared widget. Used by the read, editor, and pinned rows so
    /// their ticks can't drift.
    ///
    /// <para>A null <paramref name="onChanged"/> already yields an inert (frozen) checkbox — <c>onChanged</c>
    /// is null — but its VISUAL affordance used to be identical to a live one (refine-task-notice-ux: the
    /// Task Notice's read-only rows looked clickable when they weren't). That case now mutes the tick,
    /// border, and background to <see cref="DisabledCheckboxOpacity"/> so "can't click this" is visible
    /// instead of inferred, taking priority over <see cref="ScribeRowStyle.CheckTickColor"/> (a live-only
    /// concern — the two never apply to the same row).</para></summary>
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

    /// <summary>The grip glyph's insets in a row: the vertical centering top-nudge (kept, same as the
    /// checkbox), plus a NEGATIVE right inset that cancels the Row's <see cref="ScribeRowStyle.CheckboxTextGap"/>
    /// which would otherwise sit as a trailing margin between the grip and the next control (§10.4). With
    /// the trailing gap zeroed the grip sits flush against the checkbox and the text column reclaims that
    /// width. Used identically for the editor/pin grips AND the read/frozen grip-column spacers so read and
    /// editor rows stay column-aligned across a view switch.</summary>
    public static EdgeInsets GripInsets(ScribeRowStyle style, bool itemRow = false)
        => EdgeInsets.Only(top: CheckboxAndGripTop(style, itemRow), right: -style.CheckboxTextGap);

    /// <summary>Absolute top offset (from the row's top edge) that centers a floating pin/delete button's
    /// DRAWN box on the one-line input. The button box is <see cref="ScribeRowButton.BoxShrink"/> px
    /// shorter than <see cref="ScribeRowStyle.ControlSize"/>; the input sits <c>RowVerticalPadding</c>
    /// below the row top (the row's own vertical padding), so the button's box centers on the input's
    /// vertical midpoint. Computed so it tracks the font scale.</summary>
    public static float FloatingButtonTop(ScribeRowStyle style)
    {
        float boxHeight = style.ControlSize - ScribeRowButton.BoxShrink;
        float inputCenter = style.RowVerticalPadding + SingleLineInputHeight(style) / 2f;
        return MathF.Max(0f, inputCenter - boxHeight / 2f);
    }
}

// ============================================================================
// Shared per-row control primitives (add-lectern-row-affordances-libgui)
// ============================================================================

/// <summary>A bare (non-interactive) VS icon glyph rendered by registered <c>CustomIcons</c> code via
/// <see cref="VsIcon"/>. Used for the grip, whose pointer handling lives in a wrapping
/// <see cref="GestureDetector"/> rather than the glyph itself.
///
/// <para>NOTE: this deliberately uses <see cref="VsIcon"/> (icon-by-code) rather than LibGUI's
/// <see cref="Icon"/>/<see cref="IconButton"/> (SVG-by-path). <see cref="Icon"/> loads its SVG through
/// <c>SkiaAssetLoader.LoadSvg</c>, which calls <c>Assets.TryGet</c> WITHOUT <c>loadAsset: true</c> — and
/// VS nulls out every non-patched asset's <c>Data</c> after startup, so that path would fail to draw
/// our icons. <see cref="VsIcon"/> routes through <c>IconUtil.DrawIconInt</c> → the mod's self-healing
/// <c>CustomIcons</c> delegate (which re-resolves the asset on demand), which is why the icons were
/// registered that way (see <c>ScribeModSystem.RegisterSvgIcon</c> / VSAPI-NOTES.md).</para></summary>
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

/// <summary>Shared inventory-slot styling for a Scribe writing-station's watermarked/restricted slots
/// (add-inbox-inventory-tab D2) — extracted from <see cref="GuiDialogScribeAssignmentDesk.BuildNoticeSlot"/>
/// so the Assignment Desk's supply/output slots and the Inbox block's own restricted slots render
/// identically (same size, border, background veil, and watermark technique) without duplicating the
/// magic numbers in a third place. The watermark (a <see cref="ScribeVsIconGlyph"/> painted UNDER the
/// slot, muted by the slot's own semi-opaque veil fill on top — see <see cref="GuiDialogScribeScriptorium.BuildWatermarkedSlot"/>
/// for why under-layering beats over-layering) is optional: passing <c>null</c> for
/// <paramref name="watermarkIcon"/> renders the same size/border/background with no glyph at all, for a
/// fully-open slot.</summary>
internal static class ScribeInventorySlotStyle
{
    /// <summary>Slot edge length in pixels — matches <see cref="GuiDialogScribeAssignmentDesk.SlotSize"/>
    /// (and, transitively, the Scriptorium's own inventory slots).</summary>
    public const float SlotSize = 48f;

    /// <summary>The watermark glyph's size as a fraction of <see cref="SlotSize"/> — matches
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

/// <summary>The assignment marker icon for an accepted player-to-player assignment
/// (assignment-state-machine's Accepted state / add-assignment-and-quest-support 9.3), shown after the
/// checkbox on Read, Editor, and Pin Tab rows (assignment-icon-and-tab-defaults moved it from before to
/// after) so an assigned task is recognizable everywhere it renders — a Tablet's rows inherit it for
/// free, since it reuses the same Read/Editor row widgets rather than a per-surface copy. UNLIKE the
/// grip glyph's zero-opacity reserved-width spacer, this column is only added (and only then takes up
/// width/gap) for a row that actually is an accepted assignment — callers must gate the
/// <c>children.Add(...)</c> call on the row's own <c>IsAcceptedAssignment</c> flag rather than call this
/// unconditionally, so an ordinary row's layout is untouched. Dedicated rolled-scroll glyph
/// (<c>scribeassignment</c>, §13.4), replacing the earlier guestbook-person placeholder. Hovering it
/// shows a two-line tooltip (assigner + assigned date; accepted date) when that data is supplied.</summary>
internal static class ScribeAssignedTaskIcon
{
    /// <summary>The full-color raster stamp asset (replacing the earlier rolled-scroll SVG glyph).
    /// Resolved once per dialog rebuild by whichever caller owns <c>modSystem</c>
    /// (<see cref="ScribeModSystem.GetGuiTextureBitmap"/>, self-caching) and passed down as
    /// <c>stampBitmap</c> so this row-widget file stays API-free.</summary>
    public static readonly AssetLocation Asset = new("scribe", "textures/gui/scribe-assigned-stamp.png");

    /// <summary><paramref name="context"/>/<paramref name="currentShade"/> are only used to build the
    /// hover tooltip (via <see cref="ScribeGlobalTint.ShadedTooltip"/>, matching every other row/nav
    /// tooltip's illumination-correct shading), so they're only required when a caller also passes
    /// <paramref name="assignerName"/> — a null <paramref name="assignerName"/> (a legacy pin snapshot
    /// with no provenance yet) renders the plain icon with no tooltip at all, never crashing.</summary>
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

/// <summary>Builds the leading icon for a Tracker/Link row. Normally an <see cref="ItemStackDisplay"/> of the
/// referenced item; but a guide-page Link (a <c>"page:"</c>-prefixed <see cref="ScribeLinkTarget"/>) has no
/// item to draw, so it renders the generic <c>scribebook</c> glyph tinted <paramref name="bookColor"/>
/// instead (add-tracker-link-tasks 7.6). Shared by the read view, editor, Pin Tab, and HUD so a guide-page
/// Link looks identical everywhere. A Tracker or item Link (<paramref name="linkTarget"/> null or a bare
/// collectible code) always takes the item path.
///
/// <para>Two size tweaks from 2026-08-15 playtest feedback, both relative to the caller's nominal
/// <c>iconSize</c>: the item icon grows <see cref="ItemIconScale"/> (7.11f) and the <c>scribebook</c> glyph
/// shrinks to <see cref="BookGlyphScale"/> (7.11e). Crucially the result is rendered ROW-HEIGHT-NEUTRAL —
/// its layout box is capped to a single text line (<paramref name="lineHeight"/>) and the larger art paints
/// centered over it, overflowing above/below without growing the row, so a Tracker/Link row matches a
/// single-line Task/Text row (7.11f — icon rows were previously taller).</para></summary>
internal static class ScribeLinkIcon
{
    /// <summary>Item-icon growth (7.11f): the referenced item reads a touch larger than the nominal row
    /// control size.</summary>
    private const float ItemIconScale = 1.1f;

    /// <summary>Guide-page book-glyph shrink (7.11e): the <c>scribebook</c> glyph was visually heavy at the
    /// full control size, so it renders smaller than the item icon.</summary>
    private const float BookGlyphScale = 0.8f;

    /// <summary>True for a Link whose row has no item to draw an <see cref="ItemStackDisplay"/> for — a
    /// guide-page or quest Link — so it renders the shared book glyph instead (add-assignment-and-quest-support
    /// 10.1: a quest Link is exactly as item-less as a guide-page Link).</summary>
    private static bool IsBookGlyph(string? linkTarget)
        => ScribeLinkTarget.IsGuidePage(linkTarget) || ScribeLinkTarget.IsQuest(linkTarget);

    public static float VisualSize(float iconSize, string? linkTarget)
        => iconSize * (IsBookGlyph(linkTarget) ? BookGlyphScale : ItemIconScale);

    public static Widget Build(ItemStack? stack, string? linkTarget, float iconSize, Vector4 bookColor,
        float lineHeight, bool heightNeutral = true)
    {
        bool guidePage = IsBookGlyph(linkTarget);
        float visual = VisualSize(iconSize, linkTarget);
        Widget art = guidePage
            ? new ScribeVsIconGlyph("scribebook", visual, bookColor)
            : new ItemStackDisplay(stack, width: visual, height: visual, renderSize: 48);
        // Window item rows top-align the icon with the stepper/name, so the icon must occupy its
        // real visual height in layout (otherwise HeightNeutral overflows above the row). HUD still
        // uses the height-neutral wrap so a pin row matches a single text line.
        return heightNeutral ? HeightNeutral(art, visual, lineHeight) : art;
    }

    /// <summary>Wrap a <paramref name="visual"/>-px-square icon so it occupies only <paramref name="lineHeight"/>
    /// of vertical LAYOUT space while painting at full size, centered on that line (add-tracker-link-tasks
    /// 7.11f). A single-child <see cref="Stack"/> is sized by a <see cref="SizedBox"/> spacer to
    /// <c>visual × lineHeight</c>; the icon is a <see cref="Positioned"/> child with both <c>Width</c> and
    /// <c>Height</c> set — which <c>RenderStack</c> forces to exactly that size (min = max) — offset up by half
    /// the overflow so it centers. <c>RenderStack.Paint</c> does not clip, so the top/bottom overflow shows.
    /// If the icon already fits a line (never, at current scales) it is returned unwrapped.</summary>
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
/// Vertically centers a child inside <see cref="BandHeight"/> when the child is still a single line,
/// and leaves a wrapping child top-aligned at its natural height. Item rows stay
/// <c>CrossAxisAlignment.Start</c> so a wrapped name does not float the icon/checkbox into the middle
/// of the block; this wrapper is how a short name still sits on the icon's horizon without growing
/// the row (the band is already the icon's height).
/// </summary>
internal static class ScribeCenterIfShort
{
    /// <summary>Always-center (counter, stepper): treat the child as short so it sits in the icon band.</summary>
    public static Widget InBand(Widget child, float bandHeight)
        => new ScribeCenterIfShortWidget(oneLineHeight: float.MaxValue, bandHeight, child);

    /// <summary>Center when the child is ≤ one line; top-align when it wraps.</summary>
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
        // Use the child's own width. A Row gives non-flex children a bounded MaxWidth equal to the
        // leftover row — claiming that leftover made the stepper/counter as wide as the row and
        // zeroed the icon + name (playtest: only the first control after the checkbox survived).
        // Expanded names already pass a tight width, so child.Size.X is the flex slot in that case.
        float height = (wrapped || BandHeight <= child.Size.Y) ? child.Size.Y : BandHeight;
        child.X = 0f;
        child.Y = height > child.Size.Y ? (height - child.Size.Y) / 2f : 0f;
        Size = Constraints.Constrain(new Vector2(child.Size.X, height));
    }
}

/// <summary>An item NAME label for Tracker/Link/Craft rows, shared by the read and editor views so the
/// referenced item's name renders the same way in both. On the tablet cuneiform path
/// (<see cref="ScribeRowStyle.UseCuneiform"/> + a loaded <see cref="ScribeRowStyle.CuneiformBundle"/>) it draws
/// the name as cuneiform strokes — matching how Task/Text rows route through the cuneiform renderer — so a
/// tablet's Tracker/Link names aren't the lone plain-font holdout (add-transcribe-copy-paste 10.8). The
/// cuneiform strokes now WRAP to width via the shared <see cref="ScribeCuneiformFieldRenderWidget"/> (display-
/// only, SingleLine off — wrap-tablet-item-titles); the old single-line CuneiformText clipped a long name
/// mid-word. Off that path (Lectern/Notebook, or cuneiform disabled/asset-missing) it falls back to a wrapping
/// <see cref="Text"/>, so a name is never blank. The em size and ink track the readable label, and the
/// per-material glow is passed through so the strokes lift off the clay backdrop like the row text does.</summary>
internal static class ScribeItemLabel
{
    public static Widget Build(string label, Vector4 color, ScribeRowStyle style)
    {
        if (style.UseCuneiform && style.CuneiformBundle is { } bundle)
        {
            // Display-only wrapping cuneiform renderer (wrap-tablet-item-titles), mirroring the read-view note
            // usage (ScribeReadContent.cs:458-479). The old CuneiformText is single-line and ignores MaxWidth, so
            // a long item name clipped mid-word on the tablet; ScribeCuneiformFieldRenderWidget with SingleLine
            // left at its default (false) wraps to width like the non-cuneiform Text branch below. No caret/
            // selection (this is a label, not a field): caret/selection are zeroed and hidden. Jitter is seeded
            // from the label so the strokes are deterministic frame-to-frame (no TaskId is available here — the
            // seed only needs to be stable, not unique). Inner pad is 0: the item row applies a right
            // FieldPadX only (the stepper sits flush with the Task field box). Notebook names are a
            // bare Text with no pad. Matching Task-row FieldPadY here dropped tablet names ~6px below
            // the checkbox/stepper horizon (the taller cuneiform line is LineHeightRatio, not extra pad).
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
                strokeWeightScale: style.CuneiformStrokeWeightScale);
        }
        return ScribeTaskFont.OffsetWrap(style.TaskFontFamily, style.FontSize,
            new Text(label, new TextStyle { Color = color, SoftWrap = true }));
    }
}

/// <summary>The Tracker "N / N" have/need counter, shared by the read view, Pin Tab, and HUD so all three
/// render it identically (add-tracker-link-tasks 7.11g/7.11h). Emphasis is INVERTED from the naive reading:
/// an in-progress (unsatisfied) count is the thing you're still working on, so it reads STRONG
/// (<paramref name="strongColor"/> + bold); a satisfied count reads FADED (<paramref name="mutedColor"/>,
/// normal weight) with a faint strikethrough drawn over just the number, marking it done. The strikethrough
/// is a custom thin-line overlay (a <see cref="Positioned"/> <see cref="Container"/> spanning the counter's
/// width, centered on its text line) because LibGUI's <c>TextDecoration</c> has no strikethrough — only
/// <c>None</c>/<c>Underline</c>. The line strikes ONLY the counter: it lives inside this widget, which sizes
/// to the "N / N" text, not the row.</summary>
internal static class ScribeTrackerCounterText
{
    public static Widget Build(int current, int target, bool satisfied, Vector4 strongColor,
        Vector4 mutedColor, float lineHeight, TextStyle? baseStyle = null, System.Func<string, string>? corrupt = null,
        ScribeRowStyle? cuneiform = null)
    {
        string label = $"{current} / {target}";
        if (corrupt != null) label = corrupt(label);

        Vector4 inkColor = satisfied ? mutedColor : strongColor;

        // On the tablet cuneiform path the counter's digits + slash render as cuneiform strokes to match the
        // rest of the tablet (add-transcribe-copy-paste 10.8). Cuneiform is stroke-based, so it carries no
        // bold weight — the strong/muted color distinction (and the satisfied strikethrough below) is what
        // conveys emphasis. Off the path (or bundle not loaded) it stays the readable Text with the inverted
        // strong/bold vs. muted treatment.
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
                strokeWeightScale: cs.CuneiformStrokeWeightScale);
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

/// <summary>A floating per-row action button (delete / pin) with real button chrome: a bordered,
/// solid-background square with theme-derived resting/hover/press fills, wrapping a <see cref="VsIcon"/>
/// glyph. Unlike the earlier bare hover-glyph, this reads as a proper button (2026-07-24 feedback) so it
/// floats legibly ON TOP of the row's text via the row's Stack. Uses <see cref="VsIcon"/> (icon-by-code)
/// for the glyph — NOT LibGUI's <see cref="IconButton"/>/<see cref="Icon"/>, whose SVG-by-path
/// <c>LoadSvg</c> fails on our post-startup-unloaded assets (see <see cref="ScribeVsIconGlyph"/>).</summary>
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
    /// <see cref="BoxShrink"/> px smaller in each dimension (see <see cref="BoxShrink"/>); the glyph is
    /// still sized from this nominal value so shrinking the box doesn't shrink the icon.</summary>
    public float Size { get; }
    public Action OnTap { get; }

    /// <summary>Multiplier applied to the GLYPH only (not the box), so an icon can read a touch larger while
    /// its button box stays the same size as its neighbors (§10.2, the pin +10%). The padding split absorbs
    /// the difference, keeping the glyph centered — mirroring how <see cref="Size"/> vs the drawn box keeps
    /// the icon fixed when the box shrinks. Default 1 = unchanged.</summary>
    public float IconScale { get; }

    /// <summary>Optional drop/inner shadow(s) painted with the button's box (forwarded to
    /// <c>BoxStyle.BoxShadows</c>). Null = no shadow (the per-row delete/pin buttons); the enlarged sidebar
    /// nav buttons pass one so they read as raised chrome over the notebook art (v1-playtest-fixes 5.6).</summary>
    public BoxShadow[]? BoxShadows { get; }

    /// <summary>Optional "active tab" fill color (add-active-tab-nav-colors). Null = the normal neutral
    /// <c>SurfaceHigh</c> resting/hover/press behavior with the passed <see cref="IconColor"/> glyph. When
    /// set, the button reads as the currently-selected tab: its box fills with this color, its glyph is
    /// forced to <see cref="ScribeRowConstants.NavActiveGlyph"/> (cream) for contrast, and hover brightens
    /// the fill by +10 HSV Brightness (via <see cref="ScribeRowConstants.ShiftBrightness"/>). Only the
    /// sidebar nav buttons pass this; every other caller leaves it null and is unchanged.</summary>
    public Vector4? ActiveColor { get; }

    /// <summary>How much smaller (px, each dimension) the button's drawn chrome is than its nominal
    /// <see cref="Size"/> (2026-07-24 feedback: "2px smaller in height, 2px smaller in width"). The icon
    /// glyph is computed from the full <see cref="Size"/> and kept fixed, so this only tightens the
    /// padding/box around it — it shrinks the SKIN, not the SVG. Exposed so the row that lays the buttons
    /// out can position the pin against the actual (shrunk) box width.</summary>
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

        // Solid (opaque) background from the theme's raised-surface tone, brightening resting -> hover ->
        // press so the button reads as interactive. SurfaceHigh is the raised-element tone; nudge it up
        // for hover/press. Kept opaque (W=1) so it fully covers the row text it floats over.
        //
        // ACTIVE TAB (add-active-tab-nav-colors): when ActiveColor is set this button is the current tab,
        // so it fills with the thematic color instead of SurfaceHigh, and its glyph is forced to cream for
        // contrast. Hover brightens the fill by +10 HSV Brightness (reusing ShiftBrightness); press darkens
        // it slightly (-6 V) for the same tactile feedback the neutral path gives via the RGB lift.
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

        // The glyph is sized from the FULL nominal Size so shrinking the box below leaves the icon
        // untouched (2026-07-24 feedback). Shrinking the drawn box by BoxShrink then tightens the padding
        // that surrounds the glyph — the "skin", not the SVG. IconScale (§10.2) then grows just the glyph
        // (the box is unchanged), with the padding split below re-centering it.
        float pad = MathF.Max(3f, Widget.Size * 0.18f); // small padding; glyph fills the rest
        float glyph = (Widget.Size - pad * 2f) * Widget.IconScale;

        // Drawn box is BoxShrink px smaller in each dimension; the padding absorbs the difference so the
        // glyph stays centered at its nominal size. Half the shrink comes off each side of the padding.
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

