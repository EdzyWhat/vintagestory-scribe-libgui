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
    /// <c>ScribeClientConfig.RowCheckboxTextGap</c>. Tightened 6 → 4 so the Tracker/Craft stepper sits
    /// closer to the item icon (refine-crafting-tasks-1-3-2 playtest).</summary>
    public const float RowCheckboxTextGap = 4f;

    /// <summary>Tracker/Link/Craft item-icon size (pixels at scale 1.0); scaled with the window font via
    /// <c>ControlSize / RowCheckboxSize</c>. Formerly <c>ControlSize × 1.4</c> (~30.8px); reduced so the
    /// icon sits closer to the checkbox/stepper height.</summary>
    public const float ItemIconSize = 28f;

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

    /// <summary>Alpha applied to the theme's <c>Secondary</c> color to make the resting pinned-row tint
    /// (add-settings-tab D1b). Raised 0.33 → 0.55 (2026-08-03) after the subtler wash read as too
    /// transparent to spot pinned tasks at a glance — this is an assertive wash that stays under the row
    /// content without hiding it. The former fixed amber constant is dropped in favor of this
    /// theme-derived tint so switching the LibGUI theme re-tints pinned rows automatically.</summary>
    public const float PinnedTintAlpha = 0.55f;

    /// <summary>Saturation multiplier applied to the theme's <c>Secondary</c> before it becomes the pinned
    /// wash (2026-08-03). The wash read as clearly-pinned at <see cref="PinnedTintAlpha"/> 0.55 but a touch
    /// desaturated against the surrounding rows; boosting chroma ~1.35× gives pinned tasks an extra splash
    /// of color so they pop out of the list. Clamped inside <see cref="ShiftBrightness"/>, so an over-boost
    /// on an already-saturated theme just saturates fully rather than wrapping.</summary>
    public const float PinnedTintSaturationScale = 1.35f;

    /// <summary>The resting pinned-row tint, derived at build time from the active LibGUI theme's
    /// <see cref="ColorScheme.Secondary"/> at <see cref="PinnedTintAlpha"/>. Used by both the read and editor
    /// rows for a pinned task's whole-row background wash. Sourced from <c>Secondary</c> rather than
    /// <c>Primary</c> (add-tablet-clay-type-themes D2a): the focused-input border also reads <c>Primary</c>,
    /// so a focused input inside a pinned row would otherwise draw its border and the row wash from the same
    /// hue and the focus cue reads weakly — <c>Secondary</c> keeps the two visually distinct. This is a
    /// shared helper (tablet + Lectern/Notebook + pinned HUD), so the remap is global; every theme's
    /// <c>Secondary</c> is authored to read as a low-alpha wash distinct from its <c>Primary</c>.</summary>
    public static Vector4 PinnedTint(ColorScheme colors) =>
        ShiftBrightness(colors.Secondary, 0f, PinnedTintSaturationScale) with { W = PinnedTintAlpha };

    /// <summary>Returns <paramref name="color"/> with its HSV Brightness (Value) shifted by
    /// <paramref name="deltaValue"/> points and its Saturation scaled by <paramref name="saturationScale"/>
    /// (both on Skia's 0–100 scale; Value clamped), hue and the original float alpha preserved.
    /// Perceptually nicer than an RGB lerp toward white/black — keeps (a fraction of) the theme's chroma
    /// so a shifted theme color still reads as "the theme, brighter/darker". Used by the drag highlights
    /// to derive theme-aware source (brighter) and drop-target (darker) washes from
    /// <see cref="ColorScheme.Primary"/>, muted to half saturation so they don't overpower the row text.</summary>
    public static Vector4 ShiftBrightness(Vector4 color, float deltaValue, float saturationScale = 1f)
    {
        color.ToSkColor().ToHsv(out float h, out float s, out float v);
        var shifted = SKColor.FromHsv(h, Math.Clamp(s * saturationScale, 0f, 100f), Math.Clamp(v + deltaValue, 0f, 100f));
        return new Vector4(shifted.Red / 255f, shifted.Green / 255f, shifted.Blue / 255f, color.W);
    }

    /// <summary>Thematic "active tab" fill color for the Read nav button — slate blue <c>#465481</c>
    /// (add-active-tab-nav-colors). Shown as the button's box fill when the Read view is current; the
    /// glyph switches to <see cref="NavActiveGlyph"/> for contrast, and hover brightens the fill +10 V.</summary>
    public static readonly Vector4 NavActiveRead = new(0.2745f, 0.3294f, 0.5059f, 1f);

    /// <summary>Thematic "active tab" fill color for the Edit nav button — brick red <c>#9d4b44</c>
    /// (add-active-tab-nav-colors).</summary>
    public static readonly Vector4 NavActiveEdit = new(0.6157f, 0.2941f, 0.2667f, 1f);

    /// <summary>Thematic "active tab" fill color for the Pinned nav button — sage green <c>#6b8257</c>
    /// (add-active-tab-nav-colors).</summary>
    public static readonly Vector4 NavActivePinned = new(0.4196f, 0.5098f, 0.3412f, 1f);

    /// <summary>Thematic "active tab" fill color for the Settings nav button — warm gray <c>#746f66</c>
    /// (add-active-tab-nav-colors). Shown while the standalone settings window is open.</summary>
    public static readonly Vector4 NavActiveSettings = new(0.4549f, 0.4353f, 0.4000f, 1f);

    /// <summary>Thematic "active tab" fill color for the History nav button — warm amber <c>#b28651</c>
    /// (notebook-history-tab).</summary>
    public static readonly Vector4 NavActiveHistory = new(0.6980f, 0.5255f, 0.3176f, 1f);

    /// <summary>Thematic "active tab" fill color for the Guestbook nav button — dusty plum <c>#7a597e</c>
    /// (notebook-history-tab).</summary>
    public static readonly Vector4 NavActiveGuestbook = new(0.4784f, 0.3490f, 0.4941f, 1f);

    /// <summary>Thematic "active tab" fill color for the Timer nav button — muted teal <c>#63929c</c>.</summary>
    public static readonly Vector4 NavActiveTimer = new(0.3882f, 0.5725f, 0.6118f, 1f);

    /// <summary>Thematic "active tab" fill color for the Transcribe nav button — a bright warm gold <c>#cf9d2e</c>
    /// (add-transcribe-copy-paste). Shifted yellower (hue ~41°) and brighter than the first golden-orange pick
    /// (<c>#bb7c31</c>), which read too close to the theme Primary brown and to <see cref="NavActiveHistory"/>'s
    /// amber (refinement round 3: "closer to gold, keep some orange, needs distinction"). Still warm enough to
    /// keep an orange cast, but clearly a distinct gold.</summary>
    public static readonly Vector4 NavActiveTranscribe = new(0.8118f, 0.6157f, 0.1804f, 1f);

    /// <summary>Cream glyph color <c>#eae6dd</c> used for a nav button's icon while that button is the
    /// active tab, for contrast against the thematic fill (add-active-tab-nav-colors).</summary>
    public static readonly Vector4 NavActiveGlyph = new(0.9176f, 0.9020f, 0.8667f, 1f);
}

/// <summary>Maps the player's task-font preference (<c>ScribePlayerSettings.TaskFontFamily</c>) to the
/// concrete family name a LibGUI <c>TextStyle</c>/<c>ScribeMultilineField</c> should render with
/// (v1-release-checklist §6). The stored value is a registered family name or the empty-string default;
/// this resolves the default to LibGUI's built-in body family (<c>"sans-serif"</c>, the <c>TextStyle</c>
/// default) so the row text is unchanged for a player who never picks a font. Any non-empty value is
/// assumed already-normalized by <c>ScribePlayerSettings.NormalizeTaskFontFamily</c> to a registered
/// family, so it is passed through verbatim.</summary>
internal static class ScribeTaskFont
{
    /// <summary>LibGUI's default <c>TextStyle.FontFamily</c>; the resting body face when no font is chosen.</summary>
    public const string DefaultFamily = "sans-serif";

    /// <summary>Fixed font family for the in-Lectern TEXT buttons (Edit / New Task / Done Editing) —
    /// Caudex, the same bundled face as the dialog title (v1-release-checklist §6.2). Deliberately NOT the
    /// player's task-font choice: the buttons keep one consistent face regardless of the task-text font.
    /// Caudex is registered under every weight in <c>ScribeModSystem.RegisterCustomFonts</c>.</summary>
    public const string ButtonFamily = "Caudex";

    /// <summary>Family name to render with: the built-in body face for the empty default, else the chosen
    /// registered family.</summary>
    public static string Resolve(string? taskFontFamily) =>
        string.IsNullOrEmpty(taskFontFamily) ? DefaultFamily : taskFontFamily;

    /// <summary>The SINGLE branch point that decides whether the tablet tier renders text in the cuneiform
    /// pseudo-font or falls back to normal text (add-cuneiform-glyph-font). Returns <c>true</c> to use
    /// cuneiform (the <c>CuneiformText</c> widget), <c>false</c> to render through the normal
    /// <see cref="Resolve"/> path in the player's task font. Centralizing the decision here (rather than
    /// scattering <c>settings.CuneiformTablets</c> checks across the tablet dialog and its rows) keeps
    /// the accessibility fallback a one-line change and guarantees every tablet surface agrees. Callers pass
    /// the player's own client-local <see cref="ScribePlayerSettings.CuneiformTablets"/> preference; with
    /// its positive polarity (true = cuneiform) this is now a straight pass-through (D8).</summary>
    public static bool UseCuneiform(bool cuneiformTablets) => cuneiformTablets;
}
