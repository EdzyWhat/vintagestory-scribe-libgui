using Gui.Widgets.Framework;     // ThemeData, ColorScheme
using OpenTK.Mathematics;        // Vector4

namespace Scribe;

/// <summary>
/// Scribe's GUI theme selector. LibGUI ships only a dark preset (<see cref="ColorScheme.Default"/>), so the
/// light parchment theme is authored here. <see cref="For"/> is the single place the pixel-art-vs-global
/// choice is made for the core views (Lectern read + editor, pinned-task HUD), which wrap their output in
/// <c>new Theme(ScribeTheme.For(pixelArt), child: …)</c>.
///
/// <para>When Pixel-Art Display is OFF, <see cref="For"/> returns <see cref="ThemeData.Default"/>, which
/// LibGUI sets from the player's <c>libgui.json</c>. "Off" means "follow my global game theme," not "force
/// stock dark." The standalone settings window is never wrapped.</para>
///
/// <para>Only the 17 <see cref="ColorScheme"/> roles are authored; per-widget style structs cascade from the
/// scheme inside the <see cref="ThemeData"/> constructor.</para>
///
/// <para>Light/clay palettes invert the dark default semantically, not mechanically:
/// <see cref="ColorScheme.StateHover"/> and <see cref="ColorScheme.StateSelected"/> are dark-ink tints at low
/// alpha (a light surface must DARKEN on hover/select), and <see cref="ColorScheme.SurfaceHigh"/> stays
/// lighter than <see cref="ColorScheme.Surface"/> while <see cref="ColorScheme.SurfaceLow"/> stays darker.</para>
/// </summary>
internal static class ScribeTheme
{
    /// <summary>Dark ink used for all body text on the light surfaces (<c>OnSurface</c>/<c>OnBackground</c>).</summary>
    private static readonly Vector4 Ink = new(0.16f, 0.11f, 0.05f, 1.0f);

    /// <summary>The warm accent (button fills, carets, selection highlights); a deep ochre that stays
    /// legible on light parchment.</summary>
    private static readonly Vector4 Accent = new(0.58f, 0.40f, 0.13f, 1.0f);

    /// <summary>Default Quest Link accent, steel-blue <c>rgb(66,107,183)</c>. The parchment palette has no
    /// other blue, so it stands apart. Used by the Light theme (via <c>ScribeRowStyle.QuestLinkColor ??
    /// QuestLinkAccent</c>) and as the base for the chalkboard/tablet variants.</summary>
    internal static readonly Vector4 QuestLinkAccent = new(0.259f, 0.420f, 0.718f, 1.0f);

    /// <summary>HUD-only Quest Link accent (<c>rgb(172,207,255)</c>). The HUD renders over the live world,
    /// where <see cref="QuestLinkAccent"/> reads under-saturated.</summary>
    internal static readonly Vector4 HudQuestLinkAccent = new(0.675f, 0.812f, 1.0f, 1.0f);

    /// <summary>The light parchment theme: dark ink on warm light paper.</summary>
    internal static readonly ThemeData Light = new(new ColorScheme
    {
        // OnPrimary/OnSecondary are LIGHT because Primary/Secondary are saturated dark-ish fills.
        Primary = Accent,
        OnPrimary = new Vector4(0.97f, 0.93f, 0.82f, 1.0f),
        Secondary = new Vector4(0.64f, 0.50f, 0.30f, 1.0f),
        OnSecondary = new Vector4(0.97f, 0.93f, 0.82f, 1.0f),

        // Background is the deepest tone, Surface the page panels, SurfaceHigh raised (lightest),
        // SurfaceLow recessed (darker).
        Surface = new Vector4(0.92f, 0.87f, 0.75f, 1.0f),
        OnSurface = Ink,
        OnSurfaceVariant = new Vector4(0.42f, 0.35f, 0.24f, 1.0f),
        Background = new Vector4(0.86f, 0.80f, 0.67f, 1.0f),
        OnBackground = Ink,
        SurfaceLow = new Vector4(0.82f, 0.75f, 0.61f, 1.0f),
        SurfaceHigh = new Vector4(0.96f, 0.92f, 0.83f, 1.0f),

        Border = new Vector4(0.45f, 0.33f, 0.15f, 0.55f),
        OutlineVariant = new Vector4(0.45f, 0.33f, 0.15f, 0.28f),

        Error = new Vector4(0.72f, 0.18f, 0.12f, 1.0f),
        OnError = new Vector4(0.97f, 0.93f, 0.82f, 1.0f),

        // Hover/select DARKEN a light surface, so the overlays are dark ink / accent tints.
        StateHover = new Vector4(Ink.X, Ink.Y, Ink.Z, 0.08f),
        StateSelected = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.20f),
    })
    {
        // Points stock LibGUI controls' focus outline at the accent to match ScribeMultilineField's
        // Primary focus border; unset it falls back to LibGUI's gold.
        FocusOutlineColor = Accent,
    };

    /// <summary>Chalk-light text on the brown-slate surfaces; sampled from <c>chalk.png</c> (≈<c>#e7e6e6</c>)
    /// and nudged slightly warm.</summary>
    private static readonly Vector4 Chalk = new(0.91f, 0.90f, 0.87f, 1.0f);

    /// <summary>The chalkboard accent (button fills, carets, selection, task-row checkbox tick): forest green,
    /// lifted to V≈0.52 so the tick reads on dark slate.</summary>
    private static readonly Vector4 ChalkAccent = new(0.210f, 0.520f, 0.297f, 1.0f);

    /// <summary>Chalkboard link/tracked-item TEXT accent, a light chalk-green. The dark
    /// <see cref="ChalkAccent"/> works as a fill but is illegible as small text on slate, so links are set
    /// to this via the chalkboard's <c>DecorateRowStyle</c> → <c>ScribeRowStyle.LinkColor</c>. Other surfaces
    /// keep <c>Primary</c> for links.</summary>
    internal static readonly Vector4 ChalkboardLinkText = new(0.52f, 0.80f, 0.58f, 1.0f);

    /// <summary>Chalkboard Quest Link accent: a pale sky-blue lightened from <see cref="QuestLinkAccent"/>
    /// for the same small-text-on-slate reason as <see cref="ChalkboardLinkText"/>.</summary>
    internal static readonly Vector4 ChalkboardQuestLinkText = new(0.55f, 0.68f, 0.85f, 1.0f);

    /// <summary>Resting tint (<c>#645c52</c>) for INACTIVE right-column nav glyphs on the chalkboard, applied
    /// via the <c>NavIconColor</c> seam because <c>OnSurfaceVariant</c> reads as almost-active on the dark
    /// board.</summary>
    internal static readonly Vector4 ChalkboardNavIcon = new(0.392f, 0.361f, 0.322f, 1.0f);

    /// <summary>Focused-input BORDER color on the chalkboard: the <c>Border</c> hue at full alpha, applied
    /// via the <c>InputFocusBorderColor</c> seam so focus does not use the green <c>Primary</c>. Resting vs
    /// focus differs in alpha only (0.42 → 1.0), so nothing reflows.</summary>
    internal static readonly Vector4 ChalkboardInputFocusBorder = new(0.82f, 0.84f, 0.80f, 1.0f);

    /// <summary>Chalkboard <c>Secondary</c> (pinned-row wash / secondary buttons): a desaturated sage-green
    /// sibling of <see cref="ChalkAccent"/>. <see cref="ScribeRowConstants.PinnedTint"/> derives the pinned
    /// wash from <c>Secondary</c>, so an unrelated hue would read as muddy amber over the slate.</summary>
    private static readonly Vector4 ChalkSecondary = new(0.36f, 0.52f, 0.40f, 1.0f);

    /// <summary>The chalkboard theme: chalk-light text on dark slate. A DARK scheme, so its
    /// <c>StateHover</c>/<c>StateSelected</c> overlays LIGHTEN. Resolved for the chalkboard dialog ONLY (via
    /// its <c>ResolveTheme</c> override).</summary>
    internal static readonly ThemeData Chalkboard = new(new ColorScheme
    {
        // The forest-green accent is a dark fill, so its content is chalk-white.
        Primary = ChalkAccent,
        OnPrimary = new Vector4(0.94f, 0.95f, 0.91f, 1.0f),
        Secondary = ChalkSecondary,
        OnSecondary = Chalk,

        // Sampled from the chalkboard GUI art (frame ≈#5c4434, board ≈#665f5c). Background is the dark
        // wood frame, Surface the board panel, SurfaceHigh raised (lighter, input "writing" tone),
        // SurfaceLow recessed (darker). All warm (R ≥ G ≥ B) so the palette reads brown-slate, not green-grey.
        Surface = new Vector4(0.30f, 0.27f, 0.25f, 1.0f),
        OnSurface = Chalk,
        OnSurfaceVariant = new Vector4(0.68f, 0.66f, 0.62f, 1.0f),
        Background = new Vector4(0.20f, 0.15f, 0.11f, 1.0f),
        OnBackground = Chalk,
        SurfaceLow = new Vector4(0.24f, 0.21f, 0.19f, 1.0f),
        SurfaceHigh = new Vector4(0.39f, 0.36f, 0.34f, 1.0f),

        Border = new Vector4(0.82f, 0.84f, 0.80f, 0.42f),
        OutlineVariant = new Vector4(0.82f, 0.84f, 0.80f, 0.20f),

        Error = new Vector4(0.86f, 0.44f, 0.40f, 1.0f),
        OnError = new Vector4(0.16f, 0.11f, 0.08f, 1.0f),

        // DARK scheme: hover/select LIGHTEN the surface.
        StateHover = new Vector4(Chalk.X, Chalk.Y, Chalk.Z, 0.08f),
        StateSelected = new Vector4(ChalkAccent.X, ChalkAccent.Y, ChalkAccent.Z, 0.24f),
    })
    {
        // Same chalk tone as ScribeMultilineField's focus border, so stock focus rings drop LibGUI's gold.
        FocusOutlineColor = ChalkboardInputFocusBorder,
    };

    /// <summary>HSV Value points (0–100 scale) by which the muted-text role (<c>OnSurfaceVariant</c>) is
    /// lifted above each clay palette's <c>ink</c>; one knob for all clay palettes. Stay below +20 so the
    /// empty-list hint reads on mid-tone backdrops, and keep it clearly above ink so muted stays secondary.</summary>
    private const float MutedTextValueLift = 14f;

    /// <summary>
    /// Builds one clay-tablet <see cref="ThemeData"/> from a material's identity colors, filling the
    /// material-neutral roles (error and the state overlays) from shared clay rules. Like <see cref="Light"/>,
    /// hover/select DARKEN and <c>SurfaceHigh</c> is lighter than <c>SurfaceLow</c>.
    /// </summary>
    /// <param name="ink">Body/title text (<c>OnSurface</c>/<c>OnBackground</c>), passed separately because
    /// it varies per <c>(material, state)</c> (from <see cref="TabletReadability"/> in <see cref="ForTablet"/>).
    /// The theme is rebuilt per view because <see cref="ThemeData"/> derives widget styles at construction.</param>
    /// <param name="c">The state-independent color set for the material.</param>
    private static ThemeData ClayPalette(Vector4 ink, in ClayColors c)
    {
        Vector4 accent = c.Accent, onAccent = c.OnAccent, secondary = c.Secondary,
            surface = c.Surface, surfaceLow = c.SurfaceLow, surfaceHigh = c.SurfaceHigh,
            background = c.Background, border = c.Border;
        return new ThemeData(new ColorScheme
        {
            Primary = accent,
            OnPrimary = onAccent,
            Secondary = secondary,
            OnSecondary = onAccent,

            Surface = surface,
            OnSurface = ink,
            // Muted text is derived from this palette's own ink by a shared Value lift, so the
            // muted-vs-ink step is consistent across clays.
            OnSurfaceVariant = ScribeRowConstants.ShiftBrightness(ink, MutedTextValueLift),
            Background = background,
            OnBackground = ink,
            SurfaceLow = surfaceLow,
            SurfaceHigh = surfaceHigh,

            Border = border,
            OutlineVariant = border with { W = 0.28f },

            Error = new Vector4(0.70f, 0.17f, 0.11f, 1.0f),
            OnError = new Vector4(0.96f, 0.90f, 0.78f, 1.0f),

            StateHover = new Vector4(ink.X, ink.Y, ink.Z, 0.08f),
            StateSelected = new Vector4(accent.X, accent.Y, accent.Z, 0.20f),
        })
        {
            // Matches ScribeMultilineField's Primary focus border; unset it falls back to LibGUI's gold.
            FocusOutlineColor = accent,
        };
    }

    /// <summary>The state-INDEPENDENT color roles for one clay material: everything
    /// <see cref="ClayPalette"/> needs except the body ink, which is per-<c>(material, state)</c>.</summary>
    private readonly record struct ClayColors(
        Vector4 Accent, Vector4 OnAccent, Vector4 Secondary,
        Vector4 Surface, Vector4 SurfaceLow, Vector4 SurfaceHigh, Vector4 Background, Vector4 Border);

    /// <summary>Fire-clay colors, warm tan earthenware (backdrop center ≈ <c>#ccaf89</c>); also the fallback
    /// for unknown materials. <c>Secondary</c> is a deep umber so the pinned wash and focus border read apart.</summary>
    private static readonly ClayColors FireColors = new(
        Accent:           new Vector4(0.55f, 0.30f, 0.15f, 1.0f),
        OnAccent:         new Vector4(0.96f, 0.90f, 0.78f, 1.0f),
        Secondary:        new Vector4(0.42f, 0.32f, 0.18f, 1.0f),
        Surface:          new Vector4(0.80f, 0.66f, 0.50f, 1.0f),
        SurfaceLow:       new Vector4(0.70f, 0.55f, 0.39f, 1.0f),
        SurfaceHigh:      new Vector4(0.87f, 0.74f, 0.58f, 1.0f),
        Background:       new Vector4(0.72f, 0.57f, 0.41f, 1.0f),
        Border:           new Vector4(0.36f, 0.24f, 0.12f, 0.55f));

    /// <summary>Red-clay colors, dusty rose rather than brick terracotta. The accent (≈<c>#8e4c47</c>) stays
    /// in the backdrop panel's rosy family (≈<c>#926f6d</c>) but deeper so button fills stand out;
    /// <c>Secondary</c> is a desaturated rosy-taupe distinct from the accent.</summary>
    private static readonly ClayColors RedColors = new(
        Accent:           new Vector4(0.56f, 0.30f, 0.28f, 1.0f),
        OnAccent:         new Vector4(0.97f, 0.90f, 0.84f, 1.0f),
        Secondary:        new Vector4(0.46f, 0.34f, 0.33f, 1.0f),
        Surface:          new Vector4(0.82f, 0.62f, 0.58f, 1.0f),
        SurfaceLow:       new Vector4(0.72f, 0.52f, 0.49f, 1.0f),
        SurfaceHigh:      new Vector4(0.88f, 0.72f, 0.68f, 1.0f),
        Background:       new Vector4(0.74f, 0.54f, 0.50f, 1.0f),
        Border:           new Vector4(0.42f, 0.20f, 0.16f, 0.55f));

    /// <summary>Blue-clay colors, cool slate blue-grey (backdrop center ≈ <c>#98a6af</c>). The one cool
    /// palette, so <c>onAccent</c> is a cool near-white rather than warm cream.</summary>
    private static readonly ClayColors BlueColors = new(
        Accent:           new Vector4(0.26f, 0.42f, 0.52f, 1.0f),
        OnAccent:         new Vector4(0.93f, 0.96f, 0.98f, 1.0f),
        Secondary:        new Vector4(0.42f, 0.46f, 0.48f, 1.0f),
        Surface:          new Vector4(0.76f, 0.82f, 0.86f, 1.0f),
        SurfaceLow:       new Vector4(0.64f, 0.71f, 0.76f, 1.0f),
        SurfaceHigh:      new Vector4(0.84f, 0.89f, 0.92f, 1.0f),
        Background:       new Vector4(0.66f, 0.73f, 0.78f, 1.0f),
        Border:           new Vector4(0.20f, 0.30f, 0.36f, 0.55f));

    /// <summary>Wax colors, pale honey/beeswax. A terracotta accent would read too orange against the wax
    /// art's pale panel (≈<c>#dfc8a9</c>), so the accent is a low-saturation honey and surfaces sit just off
    /// that panel tone.</summary>
    private static readonly ClayColors WaxColors = new(
        Accent:           new Vector4(0.62f, 0.49f, 0.26f, 1.0f),
        OnAccent:         new Vector4(0.98f, 0.95f, 0.86f, 1.0f),
        Secondary:        new Vector4(0.56f, 0.47f, 0.32f, 1.0f),
        Surface:          new Vector4(0.87f, 0.78f, 0.62f, 1.0f),
        SurfaceLow:       new Vector4(0.80f, 0.70f, 0.53f, 1.0f),
        SurfaceHigh:      new Vector4(0.93f, 0.86f, 0.72f, 1.0f),
        Background:       new Vector4(0.83f, 0.73f, 0.57f, 1.0f),
        Border:           new Vector4(0.44f, 0.34f, 0.18f, 0.55f));

    /// <summary>The state-independent clay color set for a <paramref name="material"/> variant; unknown
    /// materials fall back to fire, mirroring <see cref="ForTablet"/>.</summary>
    private static ClayColors ClayColorsFor(string? material) => material switch
    {
        "clay-blue" => BlueColors,
        "clay-red" => RedColors,
        "wax" => WaxColors,
        _ => FireColors, // clay-fire + any unrecognized material (the fire backdrop twin)
    };

    /// <summary>The theme selector Scribe's core views call: <see cref="Light"/> when Pixel-Art Display is
    /// on, else the player's global theme (<see cref="ThemeData.Default"/>). The off path needs no art.</summary>
    public static ThemeData For(bool pixelArt) => pixelArt ? Light : ThemeData.Default;

    /// <summary>The tablet-tier selector. With Pixel-Art on, returns a palette keyed to the item's
    /// <c>material</c> (default → fire, so theme and <c>ScribeBackdrops.ForTablet</c> agree) with the body
    /// ink from <see cref="TabletReadability.BodyInk"/> for <c>(material, state)</c>, the one state-varying
    /// role. With it off, returns the player's global theme like <see cref="For"/>.</summary>
    public static ThemeData ForTablet(string? material, TabletState state, bool pixelArt)
    {
        if (!pixelArt) return ThemeData.Default;
        Vector4 ink = TabletReadability.For(material, state).BodyInk;
        return ClayPalette(ink, ClayColorsFor(material));
    }

    /// <summary>The per-<c>(material, state)</c> tablet LINK ink for Link/Tracker/Craft row names. The
    /// clay <c>Primary</c> is a mid-value fill that fails AA as small text on clay, so links use the deeper
    /// <see cref="TabletReadability.LinkInk"/> (the clay counterpart of <c>ChalkboardLinkText</c>).
    ///
    /// <para>A pure map with no Pixel-Art gate: the tablet's <c>DecorateRowStyle</c> gates on Pixel-Art
    /// Display, since with it off <c>Primary</c> is the correct link color. It stays a
    /// <c>ScribeRowStyle.LinkColor</c> value, not a <c>ColorScheme</c> role.</para></summary>
    public static Vector4 ForTabletLink(string? material, TabletState state) =>
        TabletReadability.For(material, state).LinkInk;

    /// <summary>Darkened variant of <see cref="QuestLinkAccent"/> for the fire/red/wax clay grounds, where a
    /// mid-value color fails AA as small text. Shared because all three grounds are similarly light-mid.</summary>
    private static readonly Vector4 TabletQuestLinkDark = new(0.16f, 0.28f, 0.50f, 1.0f);

    /// <summary>Blue-clay tablet Quest Link accent, a warm amber/gold. That tablet's own <c>Primary</c> is
    /// already nearly the same hue as <see cref="QuestLinkAccent"/>, so a blue would blend into normal links.</summary>
    private static readonly Vector4 BlueClayQuestLinkGold = new(0.62f, 0.46f, 0.14f, 1.0f);

    /// <summary>The tablet Quest Link accent per clay <paramref name="material"/>; <c>clay-blue</c> gets
    /// <see cref="BlueClayQuestLinkGold"/>, everything else <see cref="TabletQuestLinkDark"/>. Not
    /// <paramref name="state"/>-dependent.</summary>
    public static Vector4 ForTabletQuestLink(string? material, TabletState state) => material switch
    {
        "clay-blue" => BlueClayQuestLinkGold,
        _ => TabletQuestLinkDark,
    };
}
