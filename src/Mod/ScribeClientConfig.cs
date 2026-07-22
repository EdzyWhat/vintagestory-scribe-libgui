namespace Scribe;

/// <summary>
/// Client-only display preferences and GUI layout-tuning knobs for the lectern dialog. Stored
/// per-side via <c>ICoreAPICommon.StoreModConfig</c>/<c>LoadModConfig</c>, which are never
/// synced between client and server -- this must never be written into a
/// <c>Scribe.Core.ScribeDocument</c>.
///
/// Every field below used to be a hardcoded constant in <see cref="GuiDialogScribeLectern"/>/
/// <see cref="ScribeBlockRowCell"/>. Moved here so the GUI's spacing/sizing can be re-tuned by
/// editing this file's on-disk JSON (<c>ScribeModSystem.ClientConfigFileName</c>, under the
/// game's mod-config folder) and relaunching, instead of editing source and rebuilding --
/// useful while visually refining the custom-drawn dialog. See ROADMAP.md's parked ConfigLib
/// idea for eventually exposing these as a live in-game settings panel instead of a relaunch.
/// </summary>
public sealed class ScribeClientConfig
{
    // ---------------- Text size ----------------

    /// <summary>Current font-size multiplier, 1.0 = 100%. Player-adjustable in-GUI via the
    /// text-size slider (see <c>GuiDialogScribeLectern.OnTextSizeSliderChanged</c>) -- unlike
    /// every other field below, this one is meant to change routinely during normal play, not
    /// just while tuning layout.</summary>
    public float TextSizeScale = 1f;

    /// <summary>Lower bound for the text-size slider, in percent. Mirrors
    /// <see cref="MaxTextSizePercent"/> so the low end is tunable rather than hardcoded (the
    /// slider's floor and the constructor's clamp both read this).</summary>
    public int MinTextSizePercent = 30;

    /// <summary>Upper bound for the text-size slider, in percent. A loose sanity bound now that
    /// the row list scrolls to handle overflow, not a tight guard against it.</summary>
    public int MaxTextSizePercent = 150;

    // ---------------- Row-list viewport ----------------

    /// <summary>Fixed viewport height (unscaled) for the scrollable row-list region, shared by
    /// both views.</summary>
    public double VisibleListHeight = 400;

    /// <summary>Base (unscaled) vertical gap between rows in the scrollable list, shared by both
    /// views. Scaled by <see cref="TextSizeScale"/> at the point of use (see
    /// <c>GuiDialogScribeLectern.ScaledRowSpacing</c>) so the gap grows/shrinks with row text
    /// rather than staying a fixed pixel size -- like <see cref="TaskRowHeight"/>.</summary>
    public double RowSpacing = 14;

    /// <summary>Vertical gap between the title bar and the first row, shared by both views.</summary>
    public double TopContentGap = 20;

    // ---------------- Lined-paper ruling (ScribeRowElement, both views) ----------------
    //
    // Each row (ScribeRowElement) draws its own "lined paper" hairline as a structural part of the
    // row (it scrolls with the row and is drawn per-row in the interactive pass, so it clips
    // natively). This replaced the old AddInset divider chrome entirely -- both views now use this
    // ruling and nothing else (the redundant, unclippable editor dividers were removed 2026-07-21).
    // These knobs tune the hairline; they are authored so the line's *visual* could later be
    // swapped for an image without changing the row's layout math (row-list-rework S1, design.md
    // Decision 3).

    /// <summary>Ruling color as RGBA components (0-1). A low-alpha near-ink tone reads as a faint
    /// ruled line on the parchment backdrop. Kept as four fields (not a Vec-typed member) so the
    /// on-disk JSON stays flat and hand-editable, matching the rest of this config.</summary>
    public double RulingColorR = 0.15;
    public double RulingColorG = 0.11;
    public double RulingColorB = 0.08;
    public double RulingColorA = 0.35;

    /// <summary>Ruling line thickness in unscaled pixels; scaled by <see cref="TextSizeScale"/> at
    /// the point of use so the hairline thickens/thins with row text rather than staying fixed.</summary>
    public double RulingThickness = 1.5;

    /// <summary>Base (unscaled) vertical padding between a row's text and its ruling line, above
    /// and below the line. Scaled by <see cref="TextSizeScale"/> at the point of use so the gap
    /// tracks font size (design.md Decision 3 / spec "ruling padding scales with text size").</summary>
    public double RulingPadding = 4;

    /// <summary>How much of the read-view checkbox column the drawn glyph fills (0-1). The glyph is
    /// centered in the column (<see cref="ToggleWidth"/> wide); a value below 1 insets it. Tuned up
    /// from the original inline 0.76 so the glyph reads a touch larger (playtest 2026-07-21).</summary>
    public double ReadCheckboxGlyphFill = 0.86;

    /// <summary>Multiplier on the read-view checkbox's CLICKABLE area versus its drawn glyph column,
    /// to make the target easier to hit (ease-of-use goal; a "forgiving target" per Fitts's law).
    /// 1.2 = hitbox ~20% larger than the drawn space, expanded symmetrically around the column but
    /// clamped so it never crosses into the text so a text-aimed click won't toggle. Applied only
    /// to hit-testing, never to drawing.</summary>
    public double ReadCheckboxHitboxScale = 1.2;

    // ---------------- Row-list width ----------------

    /// <summary>Row-list width, shared by BOTH the read view and the editor view so switching
    /// modes never resizes the list (row-list-rework S2, tasks 5.1/5.2). Replaces the former
    /// separate <c>ReadListWidth</c>/<c>EditorListWidth</c> fields -- now that the editor edits
    /// in place on the same custom-drawn rows as the read view (no drag-handle/pin/delete icon
    /// gutters eating width), both views compose at one width. An existing on-disk config that
    /// still carries the two old keys loads fine: <c>LoadModConfig</c>'s Newtonsoft deserialize
    /// silently ignores unknown JSON keys, and an absent new key falls back to this default.</summary>
    public double RowListWidth = 500;

    // ---------------- Row cell dimensions (ScribeBlockRowCell) ----------------

    /// <summary>Base (unscaled) height of a task row, before <see cref="TextSizeScale"/>.</summary>
    public double TaskRowHeight = 30;

    /// <summary>Safety floor for the scaled row height (see <c>ScribeBlockRowCell.RowHeight</c>).
    /// Independent of look-and-feel: below roughly 15px the engine's icon renderer computes a
    /// negative icon size (row height minus a fixed <c>scaled(9)</c> inset) and crashes with an
    /// arithmetic overflow while rasterizing the pin/delete SVGs. The font keeps scaling down
    /// past this point; only the row's own height (and thus its icon chrome) stops shrinking, so
    /// a very small text size gives tiny text in a minimally-sized row rather than a crash. 20
    /// leaves margin above the ~15px threshold the old 50% text-size floor happened to sit at.</summary>
    public double MinRowHeight = 20;

    /// <summary>Base (unscaled) height of a text-section row, before <see cref="TextSizeScale"/>.</summary>
    public double TextSectionRowHeight = 70;

    /// <summary>Base (unscaled) width of a task row's checkbox column -- scales with
    /// <see cref="TextSizeScale"/> at the call site so the checkbox stays in step with row
    /// text/height rather than staying a fixed pixel size.</summary>
    public double ToggleWidth = 28;

    /// <summary>Base (unscaled) horizontal gap between a task's checkbox column and where its text
    /// begins, so the text/edit-input isn't flush against the checkbox (playtest 2026-07-21).
    /// Scaled by <see cref="TextSizeScale"/> at the call site (in <see cref="RowTextLayout"/>) so
    /// the gap tracks row text size. Applied via <c>RowTextLayout.TextX</c>, the single source of
    /// the text-column offset, so the static label and the floating edit input stay in lockstep.</summary>
    public double CheckboxTextGap = 8;

    /// <summary>Width of a row's delete-icon column.</summary>
    public double DeleteWidth = 32;

    /// <summary>Width of a row's drag-handle column (editor view only).</summary>
    public double DragHandleWidth = 24;

    /// <summary>Width of a task row's pin-icon column.</summary>
    public double PinWidth = 32;

    /// <summary>When true (the default), the drag-handle column is reserved in BOTH views -- the
    /// read view draws no grip in it, but reserving the same width keeps the checkbox and text at
    /// the same X position across the Read<->Edit toggle (no horizontal shift). Set false to let the
    /// read view reclaim that width. See <see cref="RowTextLayout"/> and
    /// <c>refine-row-affordance-visuals</c>.</summary>
    public bool DragColumnAlwaysReserved = true;

    // ---------------- Affordance buttons (pin/delete/grip, ScribeHoverIconButton) ----------------
    //
    // The per-row affordance buttons are drawn by the mod as minimal "Notion-style" controls
    // (refine-row-affordance-visuals): a thin outline over an OPAQUE background that occludes the
    // text they overlay on hover, with an icon filling most of the button. These replace Vintage
    // Story's default GuiElementToggleButton brown chrome + small icon. Kept as flat RGBA fields so
    // the on-disk JSON stays hand-editable, matching the ruling-color knobs above.

    /// <summary>Opaque button-background fill (a warm parchment tone). Alpha stays ~1.0 so the
    /// button hides the text directly beneath it on hover (the occlusion behavior the user asked for,
    /// matching Notion). Tune to match the lectern backdrop.</summary>
    public double AffordanceBgR = 0.88;
    public double AffordanceBgG = 0.82;
    public double AffordanceBgB = 0.70;
    public double AffordanceBgA = 1.0;

    /// <summary>Thin outline stroke color (ink-tone, low alpha) -- the minimal chrome that replaces
    /// the default filled/embossed button background.</summary>
    public double AffordanceOutlineR = 0.15;
    public double AffordanceOutlineG = 0.11;
    public double AffordanceOutlineB = 0.08;
    public double AffordanceOutlineA = 0.55;

    /// <summary>Outline thickness in unscaled pixels; scaled by <see cref="TextSizeScale"/> at draw
    /// time (floored at 1px) so it tracks the button size.</summary>
    public double AffordanceOutlineThickness = 1.5;

    /// <summary>Icon (glyph) color, ink-tone to match the parchment aesthetic -- NOT white.</summary>
    public double AffordanceIconColorR = 0.15;
    public double AffordanceIconColorG = 0.11;
    public double AffordanceIconColorB = 0.08;
    public double AffordanceIconColorA = 0.9;

    /// <summary>Fraction (0-1) of the button each dimension the icon spans. Near 1 makes the glyph
    /// fill most of the button -- the item-4 "larger icons" fix versus the base button's fixed
    /// <c>scaled(4)</c> inset that shrank the glyph.</summary>
    public double AffordanceIconFill = 0.78;

    /// <summary>Corner radius (unscaled) of the button's rounded-rect outline/background; scaled at
    /// draw time.</summary>
    public double AffordanceCornerRadius = 3;

    // ---------------- Editor toolbar (controls below the row list) ----------------

    /// <summary>Shared height for the text-size label/slider row, the collapse-toggle button,
    /// and the switch-mode button.</summary>
    public double ControlRowHeight = 30;

    /// <summary>Vertical gap between successive control rows below the row list (text-size row,
    /// collapse-toggle row, icon-toolbar row).</summary>
    public double ControlRowGap = 38;

    /// <summary>Gap between the row list's own bottom edge and the first control row below it
    /// (editor view only -- the read view has no stacked control rows below its list, just its
    /// own single switch-mode button spaced by <see cref="RowSpacing"/>).</summary>
    public double ListToControlsGap = 6;

    /// <summary>Width of the "Text Size" label preceding the text-size slider.</summary>
    public double TextSizeLabelWidth = 110;

    /// <summary>Horizontal gap between the "Text Size" label and the slider that follows it.</summary>
    public double TextSizeLabelToSliderGap = 5;

    /// <summary>Width of the collapse/expand tool-panel toggle button.</summary>
    public double ToolPanelToggleWidth = 140;

    /// <summary>Width of one icon-toolbar button (e.g. Add Task).</summary>
    public double ToolbarIconWidth = 36;

    /// <summary>Height of one icon-toolbar button.</summary>
    public double ToolbarIconHeight = 32;

    /// <summary>Horizontal spacing between successive icon-toolbar buttons.</summary>
    public double ToolbarIconSpacing = 42;

    /// <summary>Width of the editor view's "Switch to Read" mode-switch button (the read view's
    /// own "Switch to Editor" button instead spans the full row-list width, so it has no
    /// separate width knob here).</summary>
    public double SwitchButtonWidth = 180;

    /// <summary>Width of the tooltip box shown on hover over a row's pin/delete icon.</summary>
    public double HoverTextWidth = 150;
}
