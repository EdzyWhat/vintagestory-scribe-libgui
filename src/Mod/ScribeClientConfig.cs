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
/// useful while visually refining the dialog.
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
    /// tracks font size (design.md Decision 3 / spec "ruling padding scales with text size").
    ///
    /// <para>Defaulted to 0 in refine-row-affordance-visuals-2 so the ruling hugs the row content
    /// (the playtester asked for "just the line, no internal padding") -- kept as a tunable knob
    /// because that change flagged the spacing as an area to revisit. The focused input keeps its
    /// symmetric margin regardless of this value: its height is shrunk by
    /// <c>ScribeRowElement.BottomOverheadBandFixed</c>, which still includes the ruling thickness,
    /// so the highlight never butts directly against the line even at padding 0.</para></summary>
    public double RulingPadding = 0;

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

    /// <summary>Width of a row's far-left drag-handle (grip) column. Matches <see cref="ToggleWidth"/>
    /// so the grip and checkbox columns read as equal-width, and so the bare (chrome-less) grip glyph
    /// has room to render at least as tall as the checkbox (refine-row-affordance-visuals-2).</summary>
    public double DragHandleWidth = 28;

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

    /// <summary>Pressed/depressed overlay color as RGBA (0-1), drawn over the WHOLE pin/delete button
    /// while the mouse is held down on it (refine-row-affordance-visuals-2). A DARK tint at moderate
    /// alpha so the press reads clearly on the opaque parchment fill and is distinct from the pin's
    /// filled "on" (pinned) look -- the first pass used a ~10% white wash that was invisible over the
    /// parchment while it visibly lightened the dark ink glyph, so the whole button looked untouched
    /// and only the icon changed (playtest 2026-07-22T15-27-35). Clipped to the button's rounded rect
    /// at render time; cleared on mouse-up/leave.</summary>
    public double AffordancePressedR = 0.0;
    public double AffordancePressedG = 0.0;
    public double AffordancePressedB = 0.0;
    public double AffordancePressedA = 0.18;

    /// <summary>Minimum on-screen size (unscaled px) for the SQUARE pin/delete buttons. The buttons
    /// are sized to <c>max(MinAffordanceButtonSize, singleLineHeight)</c> and stay square (equal
    /// width and height), so at the smallest text-size setting they don't shrink to an illegible
    /// speck (refine-row-affordance-visuals-2). Mirrors the <see cref="MinRowHeight"/> floor idea:
    /// the value is a floor on the drawn button, applied after <c>TextSizeScale</c>.</summary>
    public double MinAffordanceButtonSize = 22;

    /// <summary>Multiplier on the square pin/delete button size, so the grouped button reads as the
    /// same height as a single-row input box rather than the full single-line row height (which made
    /// the group look anchored to the ruling -- playtest 2026-07-22T16-21-57). The user's rough target
    /// was ~85%; the buttons stay square (width tracks height) and are vertically centered on the row's
    /// single text line. Tune to taste. Applied on top of the <see cref="MinAffordanceButtonSize"/>
    /// floor.</summary>
    public double AffordanceButtonSizeFactor = 0.85;

    // ---------------- Pinned-task indicator (refine-row-affordance-visuals-2) ----------------

    /// <summary>How a pinned task is shown at rest (without hovering the row), so a pinned task is
    /// distinguishable from an unpinned one even when its hover-revealed pin button is hidden. The
    /// pin button itself is normally hover-gated like the other affordances; this selects an
    /// always-visible cue on top of that. Chosen in-game after comparing the variants (the first
    /// pass's small top-right dot was unnoticeable -- playtest 2026-07-22T15-27-35 -- so the resting
    /// indicator is now a whole-row background tint):
    /// <list type="bullet">
    /// <item><c>None</c> -- no resting indicator (pin only visible on hover).</item>
    /// <item><c>RowTint</c> -- the whole pinned row gets a subtle background tint (both views).</item>
    /// <item><c>AlwaysShowButton</c> -- a pinned row's pin button stays visible in its filled "on"
    /// look, bypassing the hover gate (editor view).</item>
    /// <item><c>Both</c> -- row tint and always-shown button together.</item>
    /// </list></summary>
    public PinnedIndicatorMode PinnedIndicatorMode = PinnedIndicatorMode.RowTint;

    /// <summary>Color as RGBA (0-1) of the <see cref="PinnedIndicatorMode.RowTint"/> wash -- a tint
    /// filled across the whole row surface for a pinned task, UNDER the text/checkbox/ruling so it
    /// marks the row without fighting them.
    ///
    /// <para>TEMPORARILY LOUD (playtest 2026-07-22T16-21-57: at alpha 0.12 the tint was invisible and
    /// "nothing looked pinned"). Set to an unmistakable amber at 0.35 to PROVE the render path works;
    /// once confirmed in-game, dial the alpha (and/or hue) back to a tasteful ~0.10-0.15 wash.</para></summary>
    public double PinnedRowTintR = 0.95;
    public double PinnedRowTintG = 0.75;
    public double PinnedRowTintB = 0.20;
    public double PinnedRowTintA = 0.35;

    // ---------------- GUI inspect overlay (add-gui-inspect-overlay) ----------------

    /// <summary>Diagnostic "inspect element" overlay for the lectern dialog: outlines and labels
    /// every composed box (rows, columns, checkbox, affordances, controls, viewport, chrome) plus
    /// the inter-element gaps, drawn on top of the real dialog in both views. Mirrors the engine's
    /// own <c>GuiComposer.Outlines</c> convention:
    /// <list type="bullet">
    /// <item><c>0</c> -- off (default). No inspect geometry; the only per-frame cost is this int check.</item>
    /// <item><c>1</c> -- outlines + labels (element key, pixel size, and driving config field/formula
    /// where known; gap bands labeled with their config field).</item>
    /// <item><c>2</c> -- outlines only (no labels) -- the escape hatch for when labels crowd at small
    /// text size.</item>
    /// </list>
    ///
    /// <para>Deliberately NOT gated behind <c>#if DEBUG</c>: the whole point is inspecting the GUI on
    /// platforms where an ImGui-style tuning overlay is dead (Apple Silicon caps at OpenGL 4.1;
    /// VSImGui needs 4.3). It ships in Release and is toggled by editing the on-disk JSON -- the
    /// dialog re-reads config on every open, so changing this value and reopening the lectern
    /// shows/hides the overlay. The overlay uses the macOS-safe <c>IRenderAPI.RenderRectangle</c>
    /// (a plain LineStrip, no 4.3 dependency).</para></summary>
    public int InspectOverlayMode = 0;

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

/// <summary>How a pinned task is indicated at rest (mouse not over the row), so a pinned task is
/// distinguishable even while its hover-revealed pin button is hidden. Selected via
/// <see cref="ScribeClientConfig.PinnedIndicatorMode"/>; the variants are compared in-game before
/// settling on one (refine-row-affordance-visuals-2). Serialized to JSON by name via the config's
/// Newtonsoft round-trip -- an unknown/legacy value falls back to the field default.</summary>
public enum PinnedIndicatorMode
{
    /// <summary>No resting indicator -- the pin is only visible while hovering the row.</summary>
    None,

    /// <summary>The whole pinned row gets a subtle background tint (both read and editor views).</summary>
    RowTint,

    /// <summary>A pinned row's pin button stays visible in its filled "on" look, bypassing the
    /// hover gate (editor view only -- the read view has no pin button).</summary>
    AlwaysShowButton,

    /// <summary>Both the row tint and the always-shown pin button.</summary>
    Both,
}
