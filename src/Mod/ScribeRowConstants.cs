using Gui.Rendering;           // SkiaExtensions.ToSkColor
using Gui.Widgets.Framework;   // ColorScheme
using OpenTK.Mathematics;      // Vector4
using SkiaSharp;               // SKColor.ToHsv/FromHsv

namespace Scribe;

/// <summary>
/// Built-in, code-owned layout constants for Scribe's task rows and text sizing. These used to live in
/// <c>ScribeClientConfig</c> (the retired <c>scribe-client-config.json</c> tuning file); of that file's
/// ~35 fields only the handful still consumed by <see cref="ScribeRowStyle.FromSettings"/> survived, and
/// they are now inlined here as constants rather than user configuration (add-settings-tab D1). The one
/// user-facing knob that remains — the window font size — is expressed as a base size here multiplied by
/// the player's <c>ScribePlayerSettings.WindowFontScale</c>; the resting pinned-row tint is no longer a
/// constant at all (it is derived from the active LibGUI theme at build time, D1b).
///
/// <para>Values are the exact former <c>ScribeClientConfig</c> defaults, so a font scale of <c>1.0</c>
/// reproduces today's pixel layout.</para>
/// </summary>
internal static class ScribeRowConstants
{
    /// <summary>Base (scale-1.0) task-row font size for the block/item WINDOW text (points), used by
    /// both the read-view text and the editor-view input. The old <c>ScribeClientConfig.RowFontSize</c>;
    /// multiplied by <c>ScribePlayerSettings.WindowFontScale</c> at the <see cref="ScribeRowStyle"/>
    /// chokepoint.</summary>
    public const float BaseWindowFontSize = 15f;

    /// <summary>Base (scale-1.0) font size for the pinned-task HUD's row text (points). The HUD's former
    /// hardcoded <c>HudPinsContent.RowFontSize</c>; multiplied by
    /// <c>ScribePlayerSettings.HudFontScale</c> where the HUD row is built.</summary>
    public const float BaseHudFontSize = 16f;

    /// <summary>Base (scale-1.0) size for the pinned-task HUD's row checkbox (pixels); multiplied by
    /// <c>ScribePlayerSettings.HudFontScale</c> so the checkbox scales with the HUD text (add-settings-tab
    /// round 1). The HUD's former hardcoded checkbox size.</summary>
    public const float BaseHudCheckboxSize = 20f;

    /// <summary>Base (scale-1.0) font size for the Scribe settings form's own labels/section text
    /// (points); multiplied by <c>ScribePlayerSettings.WindowFontScale</c> so the form re-scales live with
    /// the window text size (add-settings-tab round 1).</summary>
    public const float BaseSettingsFontSize = 14f;

    /// <summary>Base (scale-1.0) size for the settings form's own checkboxes (pixels); multiplied by
    /// <c>ScribePlayerSettings.WindowFontScale</c>.</summary>
    public const float BaseSettingsCheckboxSize = 22f;

    /// <summary>Each row's own top/bottom padding (pixels); scaled by the window font scale so inter-row
    /// separation tracks text size. Former <c>ScribeClientConfig.RowVerticalPadding</c>.</summary>
    public const float RowVerticalPadding = 4f;

    /// <summary>Each row's left/right padding (pixels), shared by both views. NOT scaled (a fixed
    /// left/right inset reads consistently regardless of text size). Former
    /// <c>ScribeClientConfig.RowHorizontalPadding</c>.</summary>
    public const float RowHorizontalPadding = 2f;

    /// <summary>Horizontal gap between a row's checkbox and its text/input (pixels); scaled. Former
    /// <c>ScribeClientConfig.RowCheckboxTextGap</c>.</summary>
    public const float RowCheckboxTextGap = 6f;

    /// <summary>Checkbox widget size (pixels), shared by both views; scaled. Former
    /// <c>ScribeClientConfig.RowCheckboxSize</c>.</summary>
    public const float RowCheckboxSize = 22f;

    /// <summary>The editor field's internal horizontal padding (pixels); scaled. The read row insets its
    /// text by the same amount so the text's left edge lines up across a view switch. Former
    /// <c>ScribeClientConfig.FieldInnerPaddingX</c>.</summary>
    public const float FieldInnerPaddingX = 8f;

    /// <summary>The editor field's internal vertical padding (pixels); scaled. The read row insets its
    /// text vertically by the same amount so single-line row heights match across a view switch. Former
    /// <c>ScribeClientConfig.FieldInnerPaddingY</c>.</summary>
    public const float FieldInnerPaddingY = 6f;

    /// <summary>Alpha applied to the theme's <c>Primary</c> color to make the resting pinned-row tint
    /// (add-settings-tab D1b). Low enough to read as a subtle wash under the row content rather than a
    /// solid fill; the former fixed amber constant is dropped in favor of this theme-derived tint so
    /// switching the LibGUI theme re-tints pinned rows automatically.</summary>
    public const float PinnedTintAlpha = 0.33f;

    /// <summary>The resting pinned-row tint, derived at build time from the active LibGUI theme's
    /// <see cref="ColorScheme.Primary"/> at <see cref="PinnedTintAlpha"/> (add-settings-tab D1b). Used by
    /// both the read and editor rows for a pinned task's whole-row background wash.</summary>
    public static Vector4 PinnedTint(ColorScheme colors) =>
        colors.Primary with { W = PinnedTintAlpha };

    /// <summary>Returns <paramref name="color"/> with its HSV Brightness (Value) shifted by
    /// <paramref name="deltaValue"/> points (Skia's 0–100 scale; clamped), hue/saturation and the
    /// original float alpha preserved. Perceptually nicer than an RGB lerp toward white/black —
    /// keeps the theme's chroma so a shifted theme color still reads as "the theme, brighter/darker".
    /// Used by the Edit-view drag highlights to derive theme-aware source (brighter) and drop-target
    /// (darker) washes from <see cref="ColorScheme.Primary"/>.</summary>
    public static Vector4 ShiftBrightness(Vector4 color, float deltaValue)
    {
        color.ToSkColor().ToHsv(out float h, out float s, out float v);
        var shifted = SKColor.FromHsv(h, s, Math.Clamp(v + deltaValue, 0f, 100f));
        return new Vector4(shifted.Red / 255f, shifted.Green / 255f, shifted.Blue / 255f, color.W);
    }
}
