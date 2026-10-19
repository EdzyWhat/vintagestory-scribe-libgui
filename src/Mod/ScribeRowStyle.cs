using OpenTK.Mathematics;      // Vector4 (control-glyph color override)
using Scribe.Core;
using Scribe.Core.Cuneiform;   // GlyphBundle (tablet cuneiform row path)

namespace Scribe;

/// <summary>
/// Immutable, already-scaled sizing values for one lectern task row, shared by the read and editor views
/// so a single-line task occupies pixel-identical space in both. Carries sizes plus optional per-surface
/// overrides; other colors come from the Theme at the widget. Passed by value down the widget tree, so
/// each widget is testable with a literal style.
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
    // The init-only members below are add-ons so FromSettings and positional constructions stay valid.

    /// <summary>When true (tablet only), editable rows render text as live cuneiform strokes with a
    /// synthetic caret. The disable-cuneiform fallback resolves it back to false so every tablet surface
    /// reverts together.</summary>
    public bool UseCuneiform { get; init; }

    /// <summary>Parsed glyph geometry for the <see cref="UseCuneiform"/> path; null renders no strokes
    /// (asset not yet loaded). Ignored when <see cref="UseCuneiform"/> is false.</summary>
    public GlyphBundle? CuneiformBundle { get; init; }

    /// <summary>Hand-written jitter strength (0..1) for cuneiform strokes at paint time; 0 = crisp.
    /// Tablet path only.</summary>
    public float CuneiformJitter { get; init; }

    /// <summary>Whole-character rotation in degrees for cuneiform strokes; 0 = upright. Applied after
    /// <see cref="CuneiformJitter"/>. Tablet path only.</summary>
    public float CuneiformRotation { get; init; }

    /// <summary>Whether newly-typed cuneiform text presses in stroke-by-stroke. Tablet path only; false
    /// reveals instantly.</summary>
    public bool CuneiformProgression { get; init; }

    /// <summary>Per-material outer glow behind cuneiform strokes to lift the ink off the clay backdrop.
    /// Tablet path only; the default (disabled) leaves other surfaces un-glowed.</summary>
    public CuneiformGlow CuneiformGlow { get; init; }

    /// <summary>Per-view cuneiform stroke-weight multiplier (firmer when fired, thinner when wet).
    /// Default 0 means "base weight": render objects treat any non-positive value as 1, so non-tablet
    /// surfaces are unchanged.</summary>
    public float CuneiformStrokeWeightScale { get; init; }

    /// <summary>Already-scaled left inset for a <see cref="ScribeBlock.Depth"/> 1 subtask row; zero at
    /// depth 0. Set by the dialog's <c>RowStyle</c> from the live layout width (10px + 3%·W), which
    /// <see cref="FromSettings"/> cannot know.</summary>
    public float SubtaskIndent { get; init; }

    /// <summary>Optional ink color for the per-row grip glyph (drag handle and its drag-state arrows).
    /// Null uses the theme's <c>OnSurfaceVariant</c>. The tablet sets the same darker material ink as
    /// its title-bar grip (<c>GuiDialogScribeTablet.TitleChromeGlyphColor</c>).</summary>
    public Vector4? GripGlyphColor { get; init; }

    /// <summary>Optional accent for a Link/Tracker/Craft row's tappable content (item-name text, guide
    /// book glyph, "have/need" count). Null uses the theme's <c>Primary</c>. A dark theme (the
    /// Chalkboard) cannot reuse <c>Primary</c>, which vanishes into the dark surface as text, so it sets a
    /// light chalk-green (<c>ScribeTheme.ChalkboardLinkText</c>) via <c>DecorateRowStyle</c>.</summary>
    public Vector4? LinkColor { get; init; }

    /// <summary>Optional accent for a QUEST Link row's marker icon and name, distinct from
    /// <see cref="LinkColor"/>. Null uses <c>ScribeTheme.QuestLinkAccent</c>. The tablet and chalkboard
    /// bake their own values via <c>DecorateRowStyle</c> wherever they bake <see cref="LinkColor"/>.</summary>
    public Vector4? QuestLinkColor { get; init; }

    /// <summary>Optional FOCUSED border color for an editable row field. Null uses the theme's
    /// <c>Primary</c>. The Chalkboard sets a chalk-white (<c>ScribeTheme.ChalkboardInputFocusBorder</c>)
    /// because its forest-green <c>Primary</c> looked wrong on an input border. The base dialog seeds it
    /// from its <c>InputFocusBorderColor</c> seam.</summary>
    public Vector4? InputFocusBorderColor { get; init; }

    /// <summary>Optional TICK color for the completion checkbox. Null keeps the ambient
    /// <c>CheckboxStyle</c> (theme <c>Primary</c>). The Chalkboard sets chalk-white
    /// (<c>ScribeTheme.ChalkboardInputFocusBorder</c>) to match its row text; box background and border
    /// stay themed. The base dialog seeds it from its <c>CheckTickColor</c> seam.</summary>
    public Vector4? CheckTickColor { get; init; }

    /// <summary>
    /// Builds a row style from the player's settings. THIS IS THE SINGLE PLACE the window font-size scale
    /// (<see cref="ScribePlayerSettings.WindowFontScale"/>, default <c>1f</c>) is applied to the base
    /// sizes in <see cref="ScribeRowConstants"/>. Re-deriving per dialog build repaints an open dialog
    /// when the scale changes.
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
            // Per-row control glyphs (grip/pin/delete) are sized to the checkbox so the affordance
            // column reads as its sibling.
            ControlSize: ScribeRowConstants.RowCheckboxSize * scale,
            TaskFontFamily: ScribePlayerSettings.NormalizeTaskFontFamily(s.TaskFontFamily));
    }
}
