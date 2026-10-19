using System;
using System.Collections.Generic;
using Gui.Rendering;           // SkiaExtensions.ToSkColor
using Gui.Rendering.Text;      // TextLayoutHelper, FontWeight
using Gui.Widgets.Framework;   // ColorScheme, Widget
using Gui.Widgets.Layout;      // Alignment
using Gui.Widgets.Painting;    // Transform
using OpenTK.Mathematics;      // Vector2, Vector4
using Scribe.Core;             // ScribePlayerSettings.KnownTaskFonts
using SkiaSharp;               // SKColor.ToHsv/FromHsv
using Vintagestory.API.Common; // ILogger

namespace Scribe;

/// <summary>
/// Code-owned layout constants for task rows and text sizing. The window font size is a base size here
/// multiplied by the player's <c>ScribePlayerSettings.WindowFontScale</c>; a scale of <c>1.0</c> yields
/// the baseline pixel layout. The pinned-row tint is derived from the active theme, not a constant.
/// </summary>
internal static class ScribeRowConstants
{
    /// <summary>Base (scale-1.0) window text size in points, for both read-view text and editor input.
    /// Multiplied by <c>ScribePlayerSettings.WindowFontScale</c> at the <see cref="ScribeRowStyle"/>
    /// chokepoint.</summary>
    public const float BaseWindowFontSize = 15f;

    /// <summary>Base (scale-1.0) pinned-task HUD row text size (points); multiplied by
    /// <c>ScribePlayerSettings.HudFontScale</c>.</summary>
    public const float BaseHudFontSize = 16f;

    /// <summary>Base (scale-1.0) HUD row checkbox size (pixels); scaled by <c>HudFontScale</c>.</summary>
    public const float BaseHudCheckboxSize = 20f;

    /// <summary>Settings form label/section text size (points). Not scaled; Window Text Size previews
    /// Read/Edit only.</summary>
    public const float BaseSettingsFontSize = 14f;

    /// <summary>Settings form checkbox size (pixels). Not scaled.</summary>
    public const float BaseSettingsCheckboxSize = 22f;

    /// <summary>Each row's top/bottom padding (pixels); scaled by the window font scale.</summary>
    public const float RowVerticalPadding = 4f;

    /// <summary>Each row's left/right padding (pixels), shared by both views. NOT scaled.</summary>
    public const float RowHorizontalPadding = 2f;

    /// <summary>Gap between a row's checkbox and its text/input (pixels); scaled. Kept tight so the
    /// Tracker/Craft stepper sits close to the item icon.</summary>
    public const float RowCheckboxTextGap = 4f;

    /// <summary>Tracker/Link/Craft item-icon size (pixels at scale 1.0); scaled with the window font.</summary>
    public const float ItemIconSize = 28f;

    /// <summary>Checkbox widget size (pixels), shared by both views; scaled.</summary>
    // TEMPORARY EXPERIMENT (Option 2, tools/row-leading-slot-alignment): was 22f.
    public const float RowCheckboxSize = 18f;

    /// <summary>The editor field's internal horizontal padding (pixels); scaled. The read row insets its
    /// text by the same amount so the text's left edge lines up across a view switch.</summary>
    public const float FieldInnerPaddingX = 8f;

    /// <summary>The editor field's internal vertical padding (pixels); scaled. The read row insets its
    /// text vertically by the same amount so single-line row heights match across a view switch.</summary>
    public const float FieldInnerPaddingY = 6f;

    /// <summary>Alpha applied to the theme's <c>Secondary</c> color for the resting pinned-row tint.</summary>
    public const float PinnedTintAlpha = 0.55f;

    /// <summary>Saturation multiplier applied to the theme's <c>Secondary</c> before it becomes the pinned
    /// wash. Clamped inside <see cref="ShiftBrightness"/>, so an over-boost saturates fully rather than
    /// wrapping.</summary>
    public const float PinnedTintSaturationScale = 1.35f;

    /// <summary>The resting pinned-row background wash, derived from the active theme's
    /// <see cref="ColorScheme.Secondary"/> at <see cref="PinnedTintAlpha"/>. Uses <c>Secondary</c> rather
    /// than <c>Primary</c> because the focused-input border also reads <c>Primary</c>; sharing the hue
    /// would weaken the focus cue inside a pinned row. Shared by tablet, Lectern/Notebook and HUD rows.</summary>
    public static Vector4 PinnedTint(ColorScheme colors) =>
        ShiftBrightness(colors.Secondary, 0f, PinnedTintSaturationScale) with { W = PinnedTintAlpha };

    /// <summary>Returns <paramref name="color"/> with HSV Value shifted by <paramref name="deltaValue"/>
    /// and Saturation scaled by <paramref name="saturationScale"/> (Skia 0-100 scale; both clamped), hue
    /// and float alpha preserved. Keeps the theme's chroma, unlike an RGB lerp toward white/black.</summary>
    public static Vector4 ShiftBrightness(Vector4 color, float deltaValue, float saturationScale = 1f)
    {
        color.ToSkColor().ToHsv(out float h, out float s, out float v);
        var shifted = SKColor.FromHsv(h, Math.Clamp(s * saturationScale, 0f, 100f), Math.Clamp(v + deltaValue, 0f, 100f));
        return new Vector4(shifted.Red / 255f, shifted.Green / 255f, shifted.Blue / 255f, color.W);
    }

    /// <summary>"Active tab" fill for the Read nav button, slate blue <c>#465481</c>. The glyph switches
    /// to <see cref="NavActiveGlyph"/> for contrast; hover brightens the fill +10 V.</summary>
    public static readonly Vector4 NavActiveRead = new(0.2745f, 0.3294f, 0.5059f, 1f);

    /// <summary>"Active tab" fill for the Edit nav button, brick red <c>#9d4b44</c>.</summary>
    public static readonly Vector4 NavActiveEdit = new(0.6157f, 0.2941f, 0.2667f, 1f);

    /// <summary>"Active tab" fill for the Pinned nav button, sage green <c>#6b8257</c>.</summary>
    public static readonly Vector4 NavActivePinned = new(0.4196f, 0.5098f, 0.3412f, 1f);

    /// <summary>"Active tab" fill for the Settings nav button, warm gray <c>#746f66</c>.</summary>
    public static readonly Vector4 NavActiveSettings = new(0.4549f, 0.4353f, 0.4000f, 1f);

    /// <summary>"Active tab" fill for the History nav button, warm amber <c>#b28651</c>.</summary>
    public static readonly Vector4 NavActiveHistory = new(0.6980f, 0.5255f, 0.3176f, 1f);

    /// <summary>"Active tab" fill for the Guestbook nav button, dusty plum <c>#7a597e</c>.</summary>
    public static readonly Vector4 NavActiveGuestbook = new(0.4784f, 0.3490f, 0.4941f, 1f);

    /// <summary>"Active tab" fill for the Timer nav button, muted teal <c>#63929c</c>.</summary>
    public static readonly Vector4 NavActiveTimer = new(0.3882f, 0.5725f, 0.6118f, 1f);

    /// <summary>"Active tab" fill for the Transcribe nav button, bright warm gold <c>#cf9d2e</c>; kept
    /// distinct from the theme Primary brown and <see cref="NavActiveHistory"/>'s amber.</summary>
    public static readonly Vector4 NavActiveTranscribe = new(0.8118f, 0.6157f, 0.1804f, 1f);

    /// <summary>Cream glyph color <c>#eae6dd</c> for a nav button's icon while it is the active tab.</summary>
    public static readonly Vector4 NavActiveGlyph = new(0.9176f, 0.9020f, 0.8667f, 1f);

    // ── Assignment state chip colors ──────────────────────────────────────────────────────────────
    // Aliases of nav colors, so a nav retune flows through to the matching chip.

    /// <summary>New/Unaccepted state chip.</summary>
    public static readonly Vector4 AssignmentChipNew = NavActiveGuestbook;

    /// <summary>Accepted state chip.</summary>
    public static readonly Vector4 AssignmentChipAccepted = NavActiveRead;

    /// <summary>Declined state chip.</summary>
    public static readonly Vector4 AssignmentChipRejected = NavActiveEdit;

    /// <summary>Cancelled state chip.</summary>
    public static readonly Vector4 AssignmentChipCancelled = NavActiveTranscribe;

    /// <summary>Discarded state chip: midway between Declined and Cancelled. Grouped with Declined under
    /// <see cref="ScribeAssignmentFilterGroup.RejectedGroup"/> but still reads as a distinct row state.</summary>
    public static readonly Vector4 AssignmentChipDiscarded = new(
        (AssignmentChipRejected.X + AssignmentChipCancelled.X) / 2f,
        (AssignmentChipRejected.Y + AssignmentChipCancelled.Y) / 2f,
        (AssignmentChipRejected.Z + AssignmentChipCancelled.Z) / 2f,
        1f);

    /// <summary>Completed state chip.</summary>
    public static readonly Vector4 AssignmentChipCompleted = NavActivePinned;

    /// <summary>The "All" filter pill's neutral swatch; non-committal since it represents no one state.</summary>
    public static readonly Vector4 AssignmentChipAll = NavActiveSettings;

    /// <summary>Sent state chip: a dusty amber reading as "in transit" between New's purple and
    /// Accepted's blue. Tuned value, since no nav button represents this state.</summary>
    public static readonly Vector4 AssignmentChipSent = new(0.62f, 0.50f, 0.24f, 1f);

    /// <summary>Dark-red ink of a <see cref="ScribeStamp"/>'s imprint text and border, shared so the
    /// Assignment Desk stamp cannot drift from the Scriptorium's.</summary>
    public static readonly Vector4 StampImprintInk = new(0.66f, 0.18f, 0.16f, 1f);

    // ── Quest-prompt action button colors ──────────────────────────────────────────────────────────
    // ColorScheme has no Success/Warning role, so these are fixed colors, held in one place so the HUD
    // banner and the center modal (ScribeQuestPromptActions) cannot drift apart. Colors are tuned in tools/quest-prompt-colors/index.html.

    /// <summary>"Track Quest" action color, a deep saturated green that stays green under the higher
    /// per-state fill alpha (<see cref="ScribeQuestPromptActions.AccentButton"/>).</summary>
    public static readonly Vector4 QuestPromptLinkColor = new(0.0471f, 0.4f, 0.0392f, 1f);

    /// <summary>"Not Now" (dismiss) action color, red.</summary>
    public static readonly Vector4 QuestPromptDismissColor = new(0.6863f, 0.098f, 0.0314f, 1f);

    /// <summary>"Settings" action color, near-white neutral, matching the HUD header's ink.</summary>
    public static readonly Vector4 QuestPromptSettingsColor = new(0.902f, 0.902f, 0.902f, 1f);

    /// <summary>Title LABEL line color of the quest-prompt HUD banner (<see cref="HudScribePins.BuildQuestPromptBanner"/>)
    /// only; the center modal titles with the theme's <c>ColorScheme.Primary</c> instead.</summary>
    public static readonly Vector4 QuestPromptTitleColor = new(1f, 0.9529f, 0.6392f, 1f);

    // ── Read View filter-pill colors ──────────────────────────────────────────────────────────────
    // Aliases with a different mapping from the Assignment chips (blue is Accepted there, Completed
    // here); the two pill rows never share a view.

    /// <summary>Read View "All" pill swatch.</summary>
    public static readonly Vector4 ReadFilterAll = NavActiveSettings;

    /// <summary>Read View "Completed" pill.</summary>
    public static readonly Vector4 ReadFilterCompleted = NavActiveRead;

    /// <summary>Read View "Active" pill.</summary>
    public static readonly Vector4 ReadFilterActive = NavActivePinned;

    /// <summary>Read View "Pinned" pill.</summary>
    public static readonly Vector4 ReadFilterPinned = NavActiveTranscribe;

    /// <summary>Read View "Other" pill (freeform Text + Quest objectives).</summary>
    public static readonly Vector4 ReadFilterOther = NavActiveGuestbook;

    /// <summary>The HUD's near-white row-text color. Deliberately not <c>Vector4.One</c>: that is the
    /// LibGUI 3.1.0 <c>TextStyle.Merge</c> "unset" sentinel (see the note on
    /// <see cref="ScribeQuestPromptActions.AccentButton"/>), so pure white would silently fail to
    /// override an ancestor <c>DefaultTextStyle</c>.</summary>
    public static readonly Vector4 HudStandardTextColor = new(0.93f, 0.93f, 0.93f, 1f);
}

/// <summary>Maps the player's task-font preference (<c>ScribePlayerSettings.TaskFontFamily</c>) to the
/// family name LibGUI renders with, and pegs every selectable TTF's Skia line-box to Caudex. The empty
/// default resolves to <see cref="DefaultFamily"/>; any other value is assumed already normalized to a
/// registered family and passed through. Cuneiform and Caudex chrome (<see cref="ButtonFamily"/>) do not
/// use the scale table.</summary>
internal static class ScribeTaskFont
{
    /// <summary>Scribe's resting body face: the "Default" task-font selection, the fallback reference
    /// family if Caudex fails to register, and the face for every non-task-font-following surface (HUD,
    /// Settings, dev-tuning, popups) via <see cref="ScribeTextDefaults.WrapChrome"/>/<see cref="ScribeTextDefaults.WrapSettingsChrome"/>.
    /// Set once by <see cref="BuildMetrics"/>; falls back to <c>"sans-serif"</c> only if no bundled face
    /// loaded, so chrome does not float to a platform-dependent system-font lookup.</summary>
    public static string DefaultFamily { get; private set; } = "sans-serif";

    /// <summary>Font family for the in-Lectern TEXT buttons, Caudex like the dialog title; deliberately
    /// independent of the player's task-font choice. Registered under every weight in
    /// <c>ScribeModSystem.RegisterCustomFonts</c>.</summary>
    public const string ButtonFamily = "Caudex";

    /// <summary>Probe string whose measured Y is Skia's line-box (<c>Descent − Ascent + Leading</c>),
    /// matching <c>TextLayoutHelper.MeasureText</c> / LibGUI <c>Text</c> layout.</summary>
    private const string LineBoxProbe = "Ag";

    /// <summary>Nominal size at which <see cref="BuildMetrics"/> samples line-boxes; the resulting
    /// <see cref="FamilyMetrics.SizeScale"/> is dimensionless.</summary>
    private const float MetricsReferenceSize = ScribeRowConstants.BaseWindowFontSize;

    /// <summary>Family whose line-box is the layout lock. Starts as Caudex; <see cref="BuildMetrics"/>
    /// falls back to <see cref="DefaultFamily"/> if Caudex failed to register.</summary>
    private static string referenceFamily = ButtonFamily;

    /// <summary>Per-family knobs. <c>SizeScale</c> is the auto line-box match filled by
    /// <see cref="BuildMetrics"/>; <c>OpticalScale</c> is a hand-tuned multiplier so letters read
    /// similarly sized after that match; <c>OffsetEm</c> is a vertical sit nudge; <c>SizeScaleOverride</c>
    /// replaces the auto scale entirely.</summary>
    private readonly record struct FamilyMetrics(float SizeScale, float OffsetEm, float OpticalScale, float? SizeScaleOverride);

    private static readonly Dictionary<string, FamilyMetrics> Metrics = new(StringComparer.Ordinal);

    /// <summary>Family name to render with: the built-in body face for the empty default, else the chosen
    /// registered family.</summary>
    public static string Resolve(string? taskFontFamily) =>
        string.IsNullOrEmpty(taskFontFamily) ? DefaultFamily : taskFontFamily;

    /// <summary>The single branch point deciding whether the tablet tier renders cuneiform
    /// (<c>CuneiformText</c>) or normal text via <see cref="Resolve"/>. Keeps the accessibility fallback
    /// a one-line change and every tablet surface in agreement. Callers pass the player's client-local
    /// <see cref="ScribePlayerSettings.CuneiformTablets"/>.</summary>
    public static bool UseCuneiform(bool cuneiformTablets) => cuneiformTablets;

    /// <summary>Point size to LAYOUT this family at so its Skia line-box matches Caudex: auto line-box
    /// scale (or override) times <paramref name="nominalSize"/>. Excludes
    /// <see cref="FamilyMetrics.OpticalScale"/>, which is paint-only (see <see cref="OffsetWrap"/>);
    /// putting it in <c>TextStyle.FontSize</c> would make stock <c>Text</c> report a taller layout box.</summary>
    public static float LayoutSize(string? taskFontFamily, float nominalSize)
    {
        FamilyMetrics m = Lookup(Resolve(taskFontFamily));
        float scale = m.SizeScaleOverride ?? m.SizeScale;
        return nominalSize * scale;
    }

    /// <summary>Point size to DRAW the family at: <see cref="LayoutSize"/> times
    /// <see cref="FamilyMetrics.OpticalScale"/>. For custom painters (<see cref="ScribeMultilineField"/>);
    /// stock <c>Text</c> uses <see cref="LayoutSize"/> plus <see cref="OffsetWrap"/>.</summary>
    public static float EffectiveSize(string? taskFontFamily, float nominalSize)
    {
        FamilyMetrics m = Lookup(Resolve(taskFontFamily));
        return LayoutSize(taskFontFamily, nominalSize) * m.OpticalScale;
    }

    /// <summary>Vertical glyph nudge in pixels (positive is down); does not change the reserved line-box.</summary>
    public static float OffsetY(string? taskFontFamily, float nominalSize) =>
        Lookup(Resolve(taskFontFamily)).OffsetEm * nominalSize;

    /// <summary>Layout line-box height at <paramref name="nominalSize"/>: always the reference family's
    /// <c>"Ag"</c> Y, never the selected family's native Y.</summary>
    public static float LineHeight(float nominalSize)
    {
        float h = TextLayoutHelper.MeasureText(LineBoxProbe, referenceFamily, nominalSize, FontWeight.Normal).Y;
        return h > 0 ? h : nominalSize * 1.2f;
    }

    /// <summary>Paint-only sit + optical size. <see cref="Transform"/> does not change layout
    /// constraints, so stock <c>Text</c> still reports Caudex's line-box. Wrap only the text child, not
    /// the row's checkbox/grip. Scale origin is center-left so a 2x script face grows in place.</summary>
    public static Widget OffsetWrap(string? taskFontFamily, float nominalSize, Widget child)
    {
        FamilyMetrics m = Lookup(Resolve(taskFontFamily));
        if (MathF.Abs(m.OpticalScale - 1f) > 0.001f)
            child = Transform.Scale(child, m.OpticalScale, Alignment.CenterLeft);
        float dy = m.OffsetEm * nominalSize;
        return dy == 0f ? child : Transform.Translate(child, new Vector2(0f, dy));
    }

    /// <summary>Measures each selectable task font (every <see cref="ScribePlayerSettings.KnownTaskFonts"/>
    /// entry plus the empty default) against Caudex and stores <c>SizeScale = caudexY / familyY</c>.
    /// Call once after typefaces are registered. Caudex is forced to identity.</summary>
    /// <param name="resolvedDefaultFamily">The bundled family resolved as Scribe's default (or
    /// <c>"sans-serif"</c> if none loaded); becomes <see cref="DefaultFamily"/>.</param>
    public static void BuildMetrics(ILogger logger, bool caudexRegistered, string resolvedDefaultFamily)
    {
        DefaultFamily = resolvedDefaultFamily;
        referenceFamily = caudexRegistered ? ButtonFamily : DefaultFamily;
        if (!caudexRegistered)
        {
            logger.Warning("[scribe] Caudex failed to register; task-font line-box pegs to '{0}' instead",
                DefaultFamily);
        }

        // Diagnostic bracket: the first real text-shaping call of the session (MeasureText -> HarfBuzz).
        // A crash log showing this line but not the "measured reference line-box" line below means the
        // abort is in shaping Caudex itself, pointing to a HarfBuzzSharp/system ABI mismatch.
        logger.Notification("[scribe] measuring '{0}' as the task-font line-box reference", referenceFamily);
        float referenceY = ProbeY(referenceFamily);
        logger.Notification("[scribe] measured reference line-box ({0}px); measuring remaining task fonts", referenceY);
        if (referenceY <= 0f)
        {
            logger.Warning("[scribe] reference font '{0}' measured no line-box; task-font size scales stay 1",
                referenceFamily);
            referenceY = MetricsReferenceSize * 1.2f;
        }

        Metrics.Clear();
        // DefaultFamily is a bundled registered family, so this probe avoids a live OS/fontconfig lookup,
        // unless it is the literal "sans-serif" fallback, in which case it IS that lookup.
        logger.Notification("[scribe] measuring default family '{0}'", DefaultFamily);
        SeedFamily(DefaultFamily, referenceY);
        logger.Notification("[scribe] measured default family '{0}'; measuring selectable task fonts", DefaultFamily);
        foreach (string family in ScribePlayerSettings.KnownTaskFonts)
        {
            SeedFamily(family, referenceY);
        }
        // Caudex as a task-font choice is a no-op even if auto-measure rounded off 1.0.
        Upsert(ButtonFamily, MetricsOf(ButtonFamily, sizeScale: 1f));
    }

    private static void SeedFamily(string family, float referenceY)
    {
        float familyY = ProbeY(family);
        float scale = family == ButtonFamily || familyY <= 0f ? 1f : referenceY / familyY;
        Upsert(family, MetricsOf(family, scale));
    }

    private static FamilyMetrics MetricsOf(string family, float sizeScale) =>
        new(sizeScale, OffsetEmOf(family), OpticalScaleOf(family), OverrideOf(family));

    private static void Upsert(string family, FamilyMetrics metrics) => Metrics[family] = metrics;

    private static FamilyMetrics Lookup(string resolved) =>
        Metrics.TryGetValue(resolved, out FamilyMetrics m) ? m : MetricsOf(resolved, sizeScale: 1f);

    private static float ProbeY(string family) =>
        TextLayoutHelper.MeasureText(LineBoxProbe, family, MetricsReferenceSize, FontWeight.Normal).Y;

    /// <summary>Hand-tuned vertical sit vs Caudex, in ems (positive is down). Tune in
    /// <c>tools/task-font-optical-scale/index.html</c> and paste the generated switch here.</summary>
    private static float OffsetEmOf(string family) => family switch
    {
        "sans-serif" => 0.00f,
        "Scapholene" => 0.05f,
        "La Belle Aurore" => 0.18f,
        "Noto Sans" => 0.00f,
        "Noto Serif" => 0.00f,
        "Playfair Display" => -0.03f,
        "Cormorant Unicase" => -0.02f,
        _ => 0f,
    };

    /// <summary>Hand-tuned optical size vs Caudex, multiplied on the auto line-box scale (1 = no change).
    /// Tune in <c>tools/task-font-optical-scale/index.html</c> and paste the generated switch here.</summary>
    private static float OpticalScaleOf(string family) => family switch
    {
        "sans-serif" => 0.92f,
        "Scapholene" => 0.96f,
        "La Belle Aurore" => 2.00f,
        "Noto Sans" => 1.02f,
        "Noto Serif" => 1.01f,
        "Playfair Display" => 1.10f,
        "Cormorant Unicase" => 1.08f,
        _ => 1f,
    };

    /// <summary>Optional replacement for the auto-measured size scale, for a face unreadable after
    /// line-box matching.</summary>
    private static float? OverrideOf(string family) => family switch
    {
        _ => null,
    };
}
