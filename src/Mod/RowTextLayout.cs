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
/// Layout, left to right: <c>[ grip ][ checkbox (tasks only) ][ gap ][ text .......... ]</c>, with the
/// pin/delete buttons floating as a hover OVERLAY on the right end of the text rather than reserving
/// gutter width (refine-row-affordance-visuals):
///
/// - The <b>drag-handle (grip)</b> column is on the FAR LEFT, left of the checkbox. It is reserved in
///   both views when <see cref="ScribeClientConfig.DragColumnAlwaysReserved"/> is true (the default) --
///   the read view draws no grip in it, but reserving the same width keeps the checkbox and text at the
///   same X in both views (no shift on the Read<->Edit toggle). Its width scales with the text-size
///   preference like the checkbox.
/// - The <b>text</b> runs to the row's right edge (<see cref="TextWidth"/> = full remaining width) --
///   it is NOT narrowed to make room for pin/delete.
/// - The <b>pin/delete</b> buttons are hover-only overlays. <see cref="PinX"/>/<see cref="DeleteX"/> are
///   right-anchored overlay anchors (accumulating leftward: <c>[ ... text ... ][ pin ][ delete ]</c>) so
///   they float over the text's right end; because the text keeps full width, they occlude whatever text
///   is beneath them (each button draws an opaque background). Reserved only when
///   <c>reserveAffordances</c> is true (editor view); the read view exposes no pin/delete.
///
/// Pin is task-only, so on a note <see cref="PinWidth"/> is 0 and delete lands at the same X it would on
/// a task -- the overlay cluster lines up down the list. All widths scale with the text-size preference.
/// </summary>
public readonly struct RowTextLayout
{
    /// <summary>Left edge (unscaled) of the checkbox column. Sits just right of the far-left drag
    /// column (so it is <see cref="DragHandleWidth"/> from the row's left edge, not 0, whenever that
    /// column is reserved). Tasks only; for a note there is no checkbox but the value still marks the
    /// column start.</summary>
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

    /// <summary>Available text width (unscaled) from <see cref="TextX"/> to the row's RIGHT edge.
    /// The text is no longer narrowed for the pin/delete gutters -- those float as a hover overlay
    /// over the text's right end (refine-row-affordance-visuals), so the text always runs full width.</summary>
    public double TextWidth { get; }

    /// <summary>Left edge (unscaled) of the pin overlay button. Tasks only, and only when affordances
    /// are reserved (editor view); 0 otherwise. Right-anchored (an overlay anchor, not a reserved
    /// gutter): the pin floats over the text's right end at this X.</summary>
    public double PinX { get; }

    /// <summary>Width (unscaled) of the pin overlay button. 0 for a note or when affordances are not
    /// reserved. Scales with the text-size preference.</summary>
    public double PinWidth { get; }

    /// <summary>Left edge (unscaled) of the delete overlay button -- right-anchored, just right of
    /// the pin. 0 when affordances are not reserved.</summary>
    public double DeleteX { get; }

    /// <summary>Width (unscaled) of the delete overlay button. 0 when affordances are not reserved.
    /// Scales with the text-size preference.</summary>
    public double DeleteWidth { get; }

    /// <summary>Left edge (unscaled) of the drag-handle (grip) column -- the FAR-LEFT column at x=0.
    /// Reserved (both views) per <see cref="ScribeClientConfig.DragColumnAlwaysReserved"/>.</summary>
    public double DragHandleX { get; }

    /// <summary>Width (unscaled) of the far-left drag-handle column. Scales with the text-size
    /// preference; 0 only when the column is not reserved.</summary>
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
    /// <paramref name="reserveAffordances"/> is true for the editor view (compute the pin/delete
    /// overlay anchors) and false for the read view (no pin/delete). The far-left drag column is
    /// reserved in BOTH views when <see cref="ScribeClientConfig.DragColumnAlwaysReserved"/> is set,
    /// so the checkbox/text X match across the Read<->Edit toggle regardless of this flag.
    /// </summary>
    /// <param name="affordanceSize">Square pin/delete button size in FIXED units, when the caller has
    /// computed it (via <see cref="ScribeRowElement.AffordanceButtonSizeFixed"/>). When &gt; 0 it is used
    /// as BOTH the pin and delete width so the two buttons are square and equal (and abut as one group);
    /// the pin/delete overlay anchors are derived from it. When 0 (read-view / height-only callers that
    /// have no <c>capi</c> to measure a line) the legacy per-config widths are used -- those callers only
    /// need the text/checkbox columns, which don't depend on the affordance width.</param>
    public static RowTextLayout For(double rowWidth, bool isTask, CairoFont font, ScribeClientConfig config, bool reserveAffordances = false, double affordanceSize = 0)
    {
        // Far-left drag column (grip). Reserved in both views per DragColumnAlwaysReserved so the
        // checkbox/text sit at the same X in read and editor view -- the read view just draws no grip
        // in it. Width scales with the text-size preference, like the checkbox.
        double dragHandleWidth = config.DragColumnAlwaysReserved ? config.DragHandleWidth * config.TextSizeScale : 0;
        double dragHandleX = 0;

        double checkboxX = dragHandleWidth;
        double checkboxSize = isTask ? config.ToggleWidth * config.TextSizeScale : 0;
        // Gap between the checkbox and the text so the label/input isn't flush against the box
        // (tasks only -- a note has no checkbox, so its text starts right after the drag column).
        double checkboxTextGap = isTask ? config.CheckboxTextGap * config.TextSizeScale : 0;
        double textX = checkboxX + checkboxSize + checkboxTextGap;

        // Text runs to the row's right edge -- NOT narrowed for pin/delete. Those are hover overlays.
        double textWidth = rowWidth - textX;

        // Pin/delete overlay anchors (editor view only). The two buttons are SQUARE and EQUAL: when the
        // caller passes a measured affordanceSize (the single-line-height/min-size square, see
        // ScribeRowElement.AffordanceButtonSizeFixed) both widths are that size; otherwise fall back to
        // the per-config widths (height-only/read callers that don't render the buttons). Right-anchored
        // and accumulating leftward, ABUTTED so they read as one group: [ ... text ... ][ pin | delete ].
        // Pin is task-only, so on a note pinWidth is 0 and delete lands at the same X as on a task -- the
        // overlay cluster lines up down the list.
        double squareSize = affordanceSize > 0 ? affordanceSize : config.DeleteWidth * config.TextSizeScale;
        double deleteWidth = reserveAffordances ? squareSize : 0;
        double pinSquare = affordanceSize > 0 ? affordanceSize : config.PinWidth * config.TextSizeScale;
        double pinWidth = (reserveAffordances && isTask) ? pinSquare : 0;

        double deleteX = rowWidth - deleteWidth;
        double pinX = deleteX - pinWidth;

        return new RowTextLayout(
            checkboxX, checkboxSize, textX, textWidth,
            pinX, pinWidth, deleteX, deleteWidth,
            dragHandleX, dragHandleWidth, font);
    }
}
