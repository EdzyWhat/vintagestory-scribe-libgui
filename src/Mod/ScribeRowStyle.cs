using OpenTK.Mathematics;      // Vector4 (control-glyph color override)
using Scribe.Core;
using Scribe.Core.Cuneiform;   // GlyphBundle (tablet cuneiform row path)

namespace Scribe;

/// <summary>
/// Immutable, already-scaled sizing values for one lectern task row, shared by the read and
/// editor views so a single-line task occupies pixel-identical space in both (see
/// <see cref="GuiDialogScribeLecternLibGui"/>). Carries only sizes; colors (including the resting
/// pinned-row tint, which is now derived from the LibGUI <c>Theme</c> at build time — add-settings-tab
/// D1b) come from the Theme at the widget. Passed by value down the widget tree (read/editor content ->
/// rows -> field), which keeps each widget testable with a literal style and no config/game dependency.
/// </summary>
internal readonly record struct ScribeRowStyle(
    float FontSize,
    float RowVerticalPadding,
    float RowHorizontalPadding,
    float CheckboxTextGap,
    float CheckboxSize,
    float FieldPadX,
    float FieldPadY,
    float ControlSize,
    string TaskFontFamily)
{
    /// <summary>When true (tablet only), editable rows render their text as live cuneiform strokes with a
    /// synthetic caret instead of the normal task font (add-tablet-cuneiform-chrome). Default false keeps
    /// the Lectern/Notebook rows on the normal renderer, and the disable-cuneiform fallback resolves this
    /// back to false so every tablet surface reverts together. An <c>init</c>-only add-on so
    /// <see cref="FromSettings"/> and existing positional constructions stay valid.</summary>
    public bool UseCuneiform { get; init; }

    /// <summary>Parsed cuneiform glyph geometry for the <see cref="UseCuneiform"/> row path; null renders
    /// no strokes (asset not yet loaded). Ignored when <see cref="UseCuneiform"/> is false.</summary>
    public GlyphBundle? CuneiformBundle { get; init; }

    /// <summary>Hand-written jitter strength (0..1) applied to the cuneiform row strokes at paint time
    /// (add-cuneiform-handwriting-feel); 0 = crisp authored geometry. Set on the tablet path only; ignored
    /// when <see cref="UseCuneiform"/> is false. An <c>init</c>-only add-on so existing constructions stay
    /// valid (defaults to 0).</summary>
    public float CuneiformJitter { get; init; }

    /// <summary>Whole-character rotation in degrees applied to the cuneiform row strokes at paint time
    /// (tune-tablet-jitter-add-rotation); 0 = upright. Set on the tablet path only; ignored when
    /// <see cref="UseCuneiform"/> is false. An <c>init</c>-only add-on so existing constructions stay valid
    /// (defaults to 0). Stacks with (and is applied after) <see cref="CuneiformJitter"/>.</summary>
    public float CuneiformRotation { get; init; }

    /// <summary>Whether newly-typed cuneiform row text presses in stroke-by-stroke (per-letter progression).
    /// Set on the tablet path only; ignored when <see cref="UseCuneiform"/> is false. Defaults to false so
    /// the Lectern/Notebook rows and the disable-cuneiform fallback reveal instantly.</summary>
    public bool CuneiformProgression { get; init; }

    /// <summary>The per-material outer glow painted behind the cuneiform row/label strokes to lift the ink
    /// off the clay backdrop (add-tablet-clay-type-themes). Set on the tablet path only (keyed to the
    /// tablet's material); the default (disabled) leaves every non-tablet cuneiform surface un-glowed.
    /// Ignored when <see cref="UseCuneiform"/> is false.</summary>
    public CuneiformGlow CuneiformGlow { get; init; }

    /// <summary>Per-view cuneiform stroke-weight scale (adopt-glyph-forge-tablet-themes), riding the same
    /// tablet-only seam as <see cref="CuneiformGlow"/> to every cuneiform row/label. Multiplies the painted
    /// stroke weight so the tablet firms the ink up (fired) or thins it (wet) per drying state. The
    /// struct-default 0 means "use the base weight" — the render objects treat any non-positive value as 1 —
    /// so every non-tablet cuneiform surface (which never sets it) is pixel-identical. Ignored when
    /// <see cref="UseCuneiform"/> is false.</summary>
    public float CuneiformStrokeWeightScale { get; init; }

    /// <summary>Left inset (already-scaled px) added to a <see cref="ScribeBlock.Depth"/> 1 subtask row so it
    /// reads as nested under its parent (task-subtasks 5.1). Zero for a depth-0 row. Set by the dialog's
    /// <c>RowStyle</c> from the live layout width (10px + 3%·W), since it depends on the window width rather
    /// than the settings-only sizes <see cref="FromSettings"/> knows; an <c>init</c>-only add-on so
    /// <see cref="FromSettings"/> and existing positional constructions stay valid (defaults to 0).</summary>
    public float SubtaskIndent { get; init; }

    /// <summary>Optional override for the per-row grip glyph's ink color (the drag handle, and its ◀/▶
    /// drag-state arrows). Null → the row uses the theme's <c>OnSurfaceVariant</c> mid-gray as before. The
    /// tablet sets this to the same darker material ink the title-bar grip/pencil use
    /// (<c>GuiDialogScribeTablet.TitleChromeGlyphColor</c>) so the row handle reads as firmly engraved
    /// against the clay rather than washing out (replace-drag-wash-with-grip-arrows follow-up). An
    /// <c>init</c>-only add-on so existing constructions stay valid.</summary>
    public Vector4? GripGlyphColor { get; init; }

    /// <summary>Optional override for the accent color of a Link/Tracker/Craft row's tappable content — the
    /// item-name hyperlink text, the guide-page book glyph, and the Tracker's "have/need" count. Null → the
    /// row uses the theme's <c>Primary</c> as before (light themes: a dark accent that reads as a colored
    /// link on the light surface). A DARK theme (the Chalkboard's slate) can't reuse <c>Primary</c> here: the
    /// same dark accent that reads as a button FILL vanishes into the dark surface as TEXT, so the Chalkboard
    /// sets this to a light chalk-green (<c>ScribeTheme.ChalkboardLinkText</c>) via its <c>DecorateRowStyle</c>
    /// — decoupling the link color from the button/outline the user flagged as overloaded. An <c>init</c>-only
    /// add-on so <see cref="FromSettings"/> and existing constructions stay valid.</summary>
    public Vector4? LinkColor { get; init; }

    /// <summary>Optional override for an editable row field's FOCUSED border color. Null → the field uses the
    /// theme's <c>Primary</c> accent (every light theme: a legible accent outline on focus). The Chalkboard
    /// sets this to a chalk-white (<c>ScribeTheme.ChalkboardInputFocusBorder</c>) because its <c>Primary</c> is
    /// a forest green the author disliked on an input border — same decouple-from-<c>Primary</c> reasoning as
    /// <see cref="LinkColor"/>. The base dialog seeds it from its <c>InputFocusBorderColor</c> seam so every
    /// row carries the resolved value; an <c>init</c>-only add-on so existing constructions stay valid.</summary>
    public Vector4? InputFocusBorderColor { get; init; }

    /// <summary>Optional override for the task-row completion checkbox's TICK color (the checkmark itself).
    /// Null → the checkbox uses the ambient theme's <c>CheckboxStyle</c> unchanged (its <c>CheckColor</c> is
    /// the theme <c>Primary</c> accent — every surface except the chalkboard). The Chalkboard sets this to a
    /// chalk-white (<c>ScribeTheme.ChalkboardInputFocusBorder</c>) so the completed-task tick matches its row
    /// text instead of the forest-green <c>Primary</c> — the playtest verdict that superseded the
    /// brighter-green tick (refine-chalkboard §11). Only the tick color changes; the box background/border
    /// stay themed. The base dialog seeds it from its <c>CheckTickColor</c> seam so every row carries the
    /// resolved value; an <c>init</c>-only add-on so existing constructions stay valid.</summary>
    public Vector4? CheckTickColor { get; init; }

    /// <summary>
    /// Builds a row style from the player's consolidated settings. THIS IS THE SINGLE PLACE where the
    /// window font-size scale is applied: the scalable values are multiplied by
    /// <see cref="ScribePlayerSettings.WindowFontScale"/> (default <c>1f</c>, so a fresh player is a
    /// behavioral no-op), on top of the base sizes in <see cref="ScribeRowConstants"/>. The scale comes
    /// straight from the live settings, so re-deriving the style per dialog build repaints an open
    /// dialog when the player changes the scale in the settings view (add-settings-tab D4).
    /// </summary>
    public static ScribeRowStyle FromSettings(ScribePlayerSettings s)
    {
        float scale = ScribePlayerSettings.ClampFontScale(s.WindowFontScale);
        return new ScribeRowStyle(
            FontSize: ScribeRowConstants.BaseWindowFontSize * scale,
            RowVerticalPadding: ScribeRowConstants.RowVerticalPadding * scale,
            RowHorizontalPadding: ScribeRowConstants.RowHorizontalPadding, // fixed left/right inset; not scaled
            CheckboxTextGap: ScribeRowConstants.RowCheckboxTextGap * scale,
            CheckboxSize: ScribeRowConstants.RowCheckboxSize * scale,
            FieldPadX: ScribeRowConstants.FieldInnerPaddingX * scale,
            FieldPadY: ScribeRowConstants.FieldInnerPaddingY * scale,
            // Per-row control glyphs (grip/pin/delete) scale with the row like the checkbox does, so
            // they grow/shrink with the text-size preference (lectern-gui-shell "scales with text size").
            // Sized to the checkbox so the affordance column reads as a sibling of the checkbox column.
            ControlSize: ScribeRowConstants.RowCheckboxSize * scale,
            // The player's task-text font choice (v1-release-checklist §6), normalized to a known family
            // or "" (built-in body font). Read/editor rows apply it to their task/note text; re-deriving
            // the style per build repaints an open Lectern when the selector changes.
            TaskFontFamily: ScribePlayerSettings.NormalizeTaskFontFamily(s.TaskFontFamily));
    }
}
