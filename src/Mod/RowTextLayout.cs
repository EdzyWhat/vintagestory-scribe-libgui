using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// The single source of truth for where a row's pieces sit horizontally and which font its text
/// uses -- computed once from the row's width + the current text-size scale, and read by BOTH the
/// row's own drawing (<see cref="ScribeRowElement"/>) and the placement of the one live edit field
/// that floats onto the focused row. Deriving the label draw and the edit input from the same metric
/// (rather than measuring one and matching the other) is what keeps a row's text from visibly jumping
/// the moment it gains an edit field (row-list-rework design.md Decision 5).
///
/// All offsets are in UNSCALED fixed units -- the same coordinate space as <c>ElementBounds.Fixed</c>
/// and every <see cref="ScribeClientConfig"/> layout knob. Callers drawing onto a scaled Cairo
/// surface (or hit-testing against a scaled <c>Bounds.absX/absY</c>) apply <c>GuiElement.scaled(...)</c>
/// to these values at the point of use, exactly as the rest of the dialog does.
///
/// Layout, left to right: <c>[ checkbox (tasks only) ][ gap ][ text ][ pin (tasks only) ][ delete ][ grip ]</c>.
/// The pin/delete/grip gutters are reserved only when <c>reserveAffordances</c> is true (the editor
/// view) -- the read view exposes no per-row controls beyond the checkbox, so it passes false and its
/// text fills to the right edge. The gutters are anchored to the RIGHT edge and accumulate leftward, so
/// a note (which has no pin column) puts its delete/grip at the same X as a task's -- the columns line
/// up down the list. All column widths (checkbox and gutters) scale with the text-size preference so
/// the affordances stay in step with the row's text and height (restore-row-affordance-columns).
/// </summary>
public readonly struct RowTextLayout
{
    /// <summary>Left edge (unscaled) of the checkbox column. Tasks only; 0 for a note.</summary>
    public double CheckboxX { get; }

    /// <summary>Width/height (unscaled, square) of the checkbox glyph column. 0 for a note. Scales
    /// with the text-size preference so the checkbox stays in step with row text -- mirrors
    /// <see cref="ScribeBlockRowCell"/>'s <c>ToggleWidth * TextSizeScale</c> so the read-view and
    /// editor-view checkbox columns line up.</summary>
    public double CheckboxSize { get; }

    /// <summary>Left edge (unscaled) where the row's text begins -- after the checkbox column plus
    /// a small <see cref="ScribeClientConfig.CheckboxTextGap"/> for a task, or the row's left edge
    /// for a note.</summary>
    public double TextX { get; }

    /// <summary>Available text width (unscaled) from <see cref="TextX"/> to the start of the pin
    /// gutter (or the row's right edge when affordances are not reserved).</summary>
    public double TextWidth { get; }

    /// <summary>Left edge (unscaled) of the pin-icon gutter. Tasks only, and only when affordances
    /// are reserved (editor view); 0 otherwise. When present it sits just right of the text column.</summary>
    public double PinX { get; }

    /// <summary>Width (unscaled) of the pin-icon gutter. 0 for a note or when affordances are not
    /// reserved. Scales with the text-size preference.</summary>
    public double PinWidth { get; }

    /// <summary>Left edge (unscaled) of the delete-icon gutter. 0 when affordances are not reserved.</summary>
    public double DeleteX { get; }

    /// <summary>Width (unscaled) of the delete-icon gutter. 0 when affordances are not reserved.
    /// Scales with the text-size preference.</summary>
    public double DeleteWidth { get; }

    /// <summary>Left edge (unscaled) of the drag-handle (grip) gutter -- the rightmost column.
    /// 0 when affordances are not reserved.</summary>
    public double DragHandleX { get; }

    /// <summary>Width (unscaled) of the drag-handle gutter. 0 when affordances are not reserved.
    /// Scales with the text-size preference.</summary>
    public double DragHandleWidth { get; }

    /// <summary>The font the row's text is drawn in (already sized for the current text-size
    /// preference by the caller). The same instance the floating edit field is handed.</summary>
    public CairoFont Font { get; }

    private RowTextLayout(
        double checkboxX, double checkboxSize, double textX, double textWidth,
        double pinX, double pinWidth, double deleteX, double deleteWidth,
        double dragHandleX, double dragHandleWidth, CairoFont font)
    {
        CheckboxX = checkboxX;
        CheckboxSize = checkboxSize;
        TextX = textX;
        TextWidth = textWidth;
        PinX = pinX;
        PinWidth = pinWidth;
        DeleteX = deleteX;
        DeleteWidth = deleteWidth;
        DragHandleX = dragHandleX;
        DragHandleWidth = dragHandleWidth;
        Font = font;
    }

    /// <summary>
    /// Computes the layout for one row of unscaled width <paramref name="rowWidth"/>. <paramref
    /// name="font"/> must be the same text-size-scaled font the row is measured and drawn with, and
    /// <paramref name="config"/> the same instance used elsewhere for the row, so the reserved
    /// columns match the measured text width (a mismatch would clip a glyph or overlap the text).
    ///
    /// <paramref name="reserveAffordances"/> is true for the editor view (reserve the pin/delete/grip
    /// gutters, narrowing the text column) and false for the read view (text fills to the right edge).
    /// </summary>
    public static RowTextLayout For(double rowWidth, bool isTask, CairoFont font, ScribeClientConfig config, bool reserveAffordances = false)
    {
        double checkboxSize = isTask ? config.ToggleWidth * config.TextSizeScale : 0;
        // Gap between the checkbox and the text so the label/input isn't flush against the box
        // (tasks only -- a note has no checkbox, so its text starts at the row's left edge).
        double checkboxTextGap = isTask ? config.CheckboxTextGap * config.TextSizeScale : 0;
        double textX = checkboxSize + checkboxTextGap;

        // Right-side affordance gutters (editor view only). Widths scale with the text-size
        // preference, mirroring the checkbox. Anchored to the right edge and accumulating leftward:
        // [ ... text ... ][ pin ][ delete ][ grip ]. Pin is task-only, so on a note pinWidth is 0 and
        // delete/grip land at the same X as they would on a task -- the columns line up down the list.
        double dragHandleWidth = reserveAffordances ? config.DragHandleWidth * config.TextSizeScale : 0;
        double deleteWidth = reserveAffordances ? config.DeleteWidth * config.TextSizeScale : 0;
        double pinWidth = (reserveAffordances && isTask) ? config.PinWidth * config.TextSizeScale : 0;

        double dragHandleX = rowWidth - dragHandleWidth;
        double deleteX = dragHandleX - deleteWidth;
        double pinX = deleteX - pinWidth;

        double textWidth = pinX - textX;

        return new RowTextLayout(
            0, checkboxSize, textX, textWidth,
            pinX, pinWidth, deleteX, deleteWidth,
            dragHandleX, dragHandleWidth, font);
    }
}
