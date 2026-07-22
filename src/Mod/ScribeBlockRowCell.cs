using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// A small draggable glyph for a row's reorder handle. Plain static text
/// (<c>AddStaticText</c>) is rendered but never dispatched mouse events by the composer, so
/// reorder mode needs a minimal interactive element instead.
/// </summary>
public sealed class ScribeDragHandleElement : GuiElementStaticText
{
    public System.Action<MouseEvent>? OnDragMouseDown;
    public System.Action<MouseEvent>? OnDragMouseUp;

    public ScribeDragHandleElement(ICoreClientAPI capi, string text, CairoFont font, ElementBounds bounds)
        : base(capi, text, EnumTextOrientation.Center, bounds, font)
    {
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        OnDragMouseDown?.Invoke(args);
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUpOnElement(api, args);
        OnDragMouseUp?.Invoke(args);
    }
}

/// <summary>
/// An icon button that only renders while the mouse is over a given hover region (typically
/// the whole row, not just this icon's own small bounds) -- used for the delete/pin icons so
/// they stay hidden until the player's mouse is somewhere over that row (design.md decision
/// 6). Overrides <see cref="RenderInteractiveElements"/> to skip drawing entirely when the
/// mouse isn't over <see cref="HoverRegion"/>, mirroring the technique
/// <c>GuiElementDialogTitleBar.RenderInteractiveElements</c> already uses for its own
/// close/menu-icon hover-glow (checking live mouse position every frame, confirmed via
/// decompile) -- but hiding the whole icon rather than just adding a glow on top of it, and a
/// caller-supplied region rather than the element's own (much smaller) bounds. This is a
/// render-time check, not a composer <c>AddIf</c>/recompose: the icon element itself still
/// exists and can still be clicked/handle mouse events normally, it just isn't drawn most
/// frames -- no recompose means no focus/caret reset risk (see the same concern already
/// solved for note-height changes via <c>RecomposeEditorViewPreservingFocus</c>).
/// </summary>
public sealed class ScribeHoverIconButton : GuiElementToggleButton
{
    /// <summary>The bounds to test the mouse against -- the whole row, not this icon's own
    /// small click target.</summary>
    public ElementBounds? HoverRegion;

    /// <summary><paramref name="toggleable"/> must be <c>true</c> for any icon whose <c>On</c>
    /// state represents persisted model state (e.g. the pin icon's <c>block.Pinned</c>):
    /// the base class's <c>OnMouseUp</c> unconditionally resets <c>On = false</c> whenever
    /// <c>Toggleable</c> is <c>false</c> (confirmed via decompile), which would silently wipe
    /// a just-seeded pinned-state on the very next mouse-up anywhere in the dialog, not only
    /// clicks on this icon. A momentary fire-once icon with no state to preserve (e.g.
    /// delete) should keep this <c>false</c>.</summary>
    public ScribeHoverIconButton(ICoreClientAPI capi, string icon, System.Action<bool> onToggle, ElementBounds bounds, bool toggleable = false)
        : base(capi, icon, "", CairoFont.WhiteDetailText(), onToggle, bounds, toggleable)
    {
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (HoverRegion is not null && !HoverRegion.PointInside(api.Input.MouseX, api.Input.MouseY)) return;
        base.RenderInteractiveElements(deltaTime);
    }
}

/// <summary>
/// Composes one editable row (a task or a text section) directly onto a <see cref="GuiComposer"/>.
///
/// This is NOT an <see cref="IGuiElementCell"/>/<c>AddCellList</c> row: that list widget only
/// forwards mouse events to its cells and never registers them with the composer's keyboard/
/// focus system, so a live, typable text field cannot live inside one (confirmed against the
/// game's own <c>GuiElementCellList</c>/<c>GuiElementTextInput</c> source — every shipped
/// cell-list row is a static, click-only Cairo-rendered texture). Rows are instead composed as
/// ordinary top-level interactive elements with per-row keys, stacked by hand inside a clipped,
/// scrollable region — the same approach <c>GuiDialogTrader</c> uses for its scrollbar, minus
/// the cell list.
/// </summary>
public static class ScribeBlockRowCell
{
    /// <summary>Row height scales with <paramref name="config"/>'s <c>TextSizeScale</c> so a
    /// row can always fit its text at the current font size -- the text input/text area
    /// elements size themselves to their bounds, not to their font, so without this larger
    /// fonts get clipped.</summary>
    public static double RowHeight(ScribeBlock block, ScribeClientConfig config) =>
        // Floor the scaled height at MinRowHeight: below ~15px the engine's icon renderer
        // computes a negative icon size and crashes rasterizing the pin/delete SVGs (confirmed
        // by decompile -- OverflowException in SvgLoader.rasterizeSvg via InnerHeight - scaled(9)
        // going negative). The font still scales below this; only the row box stops shrinking.
        System.Math.Max(config.MinRowHeight,
            (block.IsTask ? config.TaskRowHeight : config.TextSectionRowHeight) * config.TextSizeScale);

    /// <summary>The text element's own width within a row of <paramref name="rowWidth"/> --
    /// the same math <see cref="Compose"/> uses internally, exposed so callers can measure
    /// wrapped text height against the real width before laying out the row. <paramref
    /// name="config"/> must be the same instance <see cref="Compose"/> is called with for the
    /// same row, since the reserved toggle-column width scales with <c>TextSizeScale</c> (see
    /// <see cref="Compose"/>'s checkbox-scaling comment) -- passing a mismatched scale would
    /// reserve too little/much space and either clip the checkbox or overlap the text. Task
    /// rows also reserve a pin-icon column (<c>config.PinWidth</c>) alongside the delete icon
    /// -- text sections get neither the toggle nor the pin column, mirroring pin's task-only
    /// restriction (design.md decision 7).</summary>
    public static double TextWidth(double rowWidth, bool isTask, bool showDragHandle, ScribeClientConfig config)
    {
        double dragHandleWidth = showDragHandle ? config.DragHandleWidth : 0;
        double toggleWidth = isTask ? config.ToggleWidth * config.TextSizeScale : 0;
        double pinWidth = isTask ? config.PinWidth : 0;
        return rowWidth - dragHandleWidth - toggleWidth - pinWidth - config.DeleteWidth;
    }

    public static string ToggleKey(int index) => $"scribeRow{index}Toggle";
    public static string TextKey(int index) => $"scribeRow{index}Text";
    public static string DeleteKey(int index) => $"scribeRow{index}Delete";
    public static string DragHandleKey(int index) => $"scribeRow{index}DragHandle";
    public static string PinKey(int index) => $"scribeRow{index}Pin";

    /// <summary>
    /// Composes the row at <paramref name="index"/> within <paramref name="rowBounds"/>
    /// (already positioned by the caller). <paramref name="showDragHandle"/> reserves space for
    /// and adds the drag handle used by reorder mode.
    ///
    /// Adds elements only -- does NOT seed their values. A text input/area's <c>Bounds</c> isn't
    /// calculated until the whole composer's <c>Compose()</c> runs (that's what turns a fixed
    /// width into a real <c>Bounds.InnerWidth</c>); calling <c>SetValue</c> before then makes the
    /// text-wrapping math run against a bounds tree that still has <c>InnerWidth == 0</c>,
    /// corrupting the auto-height calc and, transitively, the whole dialog's outer size (this was
    /// the root cause of a zero-size-surface crash on recompose). Call <see cref="ApplyValues"/>
    /// after the composer's own <c>.Compose()</c> instead.
    /// </summary>
    public static void Compose(
        GuiComposer composer,
        ScribeBlock block,
        int index,
        ElementBounds rowBounds,
        CairoFont font,
        bool showDragHandle,
        System.Action<int> onToggle,
        System.Action<int, string> onTextChanged,
        System.Action<int> onDelete,
        ScribeClientConfig config,
        System.Action<int, MouseEvent>? onDragMouseDown = null,
        System.Action<int, MouseEvent>? onDragMouseUp = null,
        System.Action<int>? onTogglePin = null)
    {
        // rowBounds is used throughout this method purely as a position/size source (its
        // fixedX/Y/Width/Height feed every sub-element's own bounds) -- it is never itself
        // added to the composer as an element's bounds, so without this it would never be
        // visited by CalcWorldBounds() and its absX/absY (needed below as ScribeHoverIconButton's
        // HoverRegion) would stay uninitialized. Parenting it under the same parent as every
        // other element in this row makes CalcWorldBounds() compute it for free.
        composer.CurParentBounds.WithChild(rowBounds);

        double x = rowBounds.fixedX;
        double dragHandleWidth = showDragHandle ? config.DragHandleWidth : 0;

        if (showDragHandle)
        {
            var dragBounds = ElementBounds.Fixed(x, rowBounds.fixedY, config.DragHandleWidth, rowBounds.fixedHeight);
            var dragHandle = new ScribeDragHandleElement(composer.Api, "::", font, dragBounds)
            {
                OnDragMouseDown = args => onDragMouseDown?.Invoke(index, args),
                OnDragMouseUp = args => onDragMouseUp?.Invoke(index, args),
            };
            composer.AddInteractiveElement(dragHandle, DragHandleKey(index));
            x += config.DragHandleWidth;
        }

        // GuiElementSwitch's constructor unconditionally overwrites bounds.fixedWidth/Height
        // to its own `size` param (confirmed via decompile,
        // /private/tmp/switch_decompile/...GuiElementSwitch.decompiled.cs) -- passing the
        // bounds' own fixed width/height does nothing; `size:` is the only knob that actually
        // controls rendered size. Scaling it by TextSizeScale keeps the checkbox in step with
        // the row's text/height (design.md decision 5) instead of staying a constant pixel
        // size while everything around it grows/shrinks.
        double toggleWidth = block.IsTask ? config.ToggleWidth * config.TextSizeScale : 0;
        if (block.IsTask)
        {
            var toggleBounds = ElementBounds.Fixed(x, rowBounds.fixedY, config.ToggleWidth, rowBounds.fixedHeight);
            composer.AddSwitch(on => onToggle(index), toggleBounds, ToggleKey(index), size: toggleWidth);
            x += toggleWidth;
        }

        double textWidth = TextWidth(rowBounds.fixedWidth, block.IsTask, showDragHandle, config);
        var textBounds = ElementBounds.Fixed(x, rowBounds.fixedY, textWidth, rowBounds.fixedHeight);

        if (block.IsTask)
        {
            composer.AddTextInput(textBounds, text => onTextChanged(index, text), font, TextKey(index));
        }
        else
        {
            composer.AddTextArea(textBounds, text => onTextChanged(index, text), font, TextKey(index));
        }

        x += textWidth;

        // Pin is task-only, same restriction as Done/the checkbox column above -- text
        // sections get no pin affordance at all (design.md decision 7; TextWidth already
        // reserves zero width for this column on a non-task row, so no bounds/space is
        // wasted either).
        if (block.IsTask)
        {
            var pinBounds = ElementBounds.Fixed(x, rowBounds.fixedY, config.PinWidth, rowBounds.fixedHeight);
            var pinButton = new ScribeHoverIconButton(composer.Api, "wpCircle", _ => onTogglePin?.Invoke(index), pinBounds, toggleable: true)
            {
                HoverRegion = rowBounds,
            };
            composer.AddInteractiveElement(pinButton, PinKey(index));
            composer.AddHoverText(Lang.Get("scribe:scribe-gui-pin"), CairoFont.WhiteSmallText(), (int)config.HoverTextWidth, pinBounds.FlatCopy());
            x += config.PinWidth;
        }

        var deleteBounds = ElementBounds.Fixed(x, rowBounds.fixedY, config.DeleteWidth, rowBounds.fixedHeight);
        var deleteButton = new ScribeHoverIconButton(composer.Api, "eraser", _ => onDelete(index), deleteBounds)
        {
            HoverRegion = rowBounds,
        };
        composer.AddInteractiveElement(deleteButton, DeleteKey(index));
        composer.AddHoverText(Lang.Get("scribe:scribe-gui-delete"), CairoFont.WhiteSmallText(), (int)config.HoverTextWidth, deleteBounds.FlatCopy());
    }

    /// <summary>
    /// Seeds the row's live values (toggle state, text content) -- call once per row after the
    /// composer's own <c>.Compose()</c> has run, so the text elements' bounds are real. See the
    /// note on <see cref="Compose"/> for why this must not happen earlier.
    /// </summary>
    public static void ApplyValues(GuiComposer composer, ScribeBlock block, int index)
    {
        if (block.IsTask)
        {
            composer.GetSwitch(ToggleKey(index)).On = block.Done;
            composer.GetTextInput(TextKey(index)).SetValue(block.Text);
            composer.GetToggleButton(PinKey(index)).On = block.Pinned;
        }
        else
        {
            composer.GetTextArea(TextKey(index)).SetValue(block.Text);
        }
    }
}
