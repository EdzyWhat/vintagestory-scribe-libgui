using Gui.Widgets.Framework;     // ThemeData, ColorScheme
using OpenTK.Mathematics;        // Vector4

namespace Scribe;

/// <summary>
/// Scribe's GUI theme selector (scribe-themed-toggle). LibGUI ships exactly one built-in preset —
/// <see cref="ColorScheme.Default"/>, a DARK parchment palette — so a LIGHT theme is net-new; this file
/// authors it. <see cref="For"/> is the single place the pixel-art-vs-global choice is made for Scribe's
/// three CORE views (Lectern read + editor, pinned-task HUD): each wraps its <c>Build()</c> output in
/// <c>new Theme(ScribeTheme.For(pixelArt), child: …)</c>, and the descendants that read
/// <c>Theme.Of(context)</c> recolor for free.
///
/// <para>When Pixel-Art Display is OFF, <see cref="For"/> returns <see cref="ThemeData.Default"/> — which
/// LibGUI's own <c>GuiModSystem</c> sets from the player's <c>libgui.json</c> (their custom global theme
/// if they authored one, else the stock dark default). So "off" means "follow my global game theme,"
/// NOT "force stock dark." The standalone settings window is never wrapped at all (it always follows the
/// global theme); only the core views consult this selector. See the 2026-07-25 pivot.</para>
///
/// <para>Only the 17 <see cref="ColorScheme"/> roles are authored. The per-widget style structs
/// (<c>ButtonStyle</c>, <c>CheckboxStyle</c>, <c>DropdownStyle</c>, …) all cascade from the scheme via
/// their <c>Default(colors, …)</c> factories inside the <see cref="ThemeData"/> constructor, so we do
/// not restate any of them.</para>
///
/// <para>The palette is a warm aged-paper parchment: light surfaces, dark-ink text. Most roles are the
/// luminance inverse of the shipped dark default, but two are inverted <em>semantically</em>, not
/// mechanically: <see cref="ColorScheme.StateHover"/> and <see cref="ColorScheme.StateSelected"/> are
/// translucent overlays, and a light surface must DARKEN on hover/select where a dark one lightens — so
/// those overlays use a dark ink tint at low alpha rather than the dark theme's light tint. Likewise the
/// raised/recessed pair is kept semantically correct (<see cref="ColorScheme.SurfaceHigh"/> lighter than
/// <see cref="ColorScheme.Surface"/>, <see cref="ColorScheme.SurfaceLow"/> darker) rather than blindly
/// inverting the dark values, which would swap raised and recessed.</para>
/// </summary>
internal static class ScribeTheme
{
    /// <summary>Dark ink used for all body text on the light surfaces (<c>OnSurface</c>/<c>OnBackground</c>).</summary>
    private static readonly Vector4 Ink = new(0.16f, 0.11f, 0.05f, 1.0f);

    /// <summary>The warm accent (button fills, carets, selection highlights). A deep ochre that stays
    /// legible against light parchment, where the dark theme's bright gold would wash out.</summary>
    private static readonly Vector4 Accent = new(0.58f, 0.40f, 0.13f, 1.0f);

    /// <summary>The net-new light parchment theme: dark ink on warm light paper. Authored role-by-role
    /// (see the class remarks for the two semantic — not mechanical — inversions).</summary>
    internal static readonly ThemeData Light = new(new ColorScheme
    {
        // Accent + its content. OnPrimary/OnSecondary are LIGHT because Primary/Secondary are now
        // saturated dark-ish fills (inverse of the dark theme, where the gold fill carried dark text).
        Primary = Accent,
        OnPrimary = new Vector4(0.97f, 0.93f, 0.82f, 1.0f),
        Secondary = new Vector4(0.64f, 0.50f, 0.30f, 1.0f),
        OnSecondary = new Vector4(0.97f, 0.93f, 0.82f, 1.0f),

        // Surfaces: warm light paper. Background is the deepest (a touch darker tan desk tone), Surface
        // the page panels on top, SurfaceHigh raised (lightest), SurfaceLow recessed (darker) — the
        // raised/recessed ordering kept semantically correct for a light scheme.
        Surface = new Vector4(0.92f, 0.87f, 0.75f, 1.0f),
        OnSurface = Ink,
        OnSurfaceVariant = new Vector4(0.42f, 0.35f, 0.24f, 1.0f),
        Background = new Vector4(0.86f, 0.80f, 0.67f, 1.0f),
        OnBackground = Ink,
        SurfaceLow = new Vector4(0.82f, 0.75f, 0.61f, 1.0f),
        SurfaceHigh = new Vector4(0.96f, 0.92f, 0.83f, 1.0f),

        // Borders/dividers: a medium brown with alpha, so they read on light paper.
        Border = new Vector4(0.45f, 0.33f, 0.15f, 0.55f),
        OutlineVariant = new Vector4(0.45f, 0.33f, 0.15f, 0.28f),

        // Error stays red but a shade deeper for contrast on light; its content is light.
        Error = new Vector4(0.72f, 0.18f, 0.12f, 1.0f),
        OnError = new Vector4(0.97f, 0.93f, 0.82f, 1.0f),

        // Semantic (not mechanical) inversion: hover/select DARKEN a light surface, so these translucent
        // overlays are dark ink / accent tints at low alpha — the opposite of the dark theme, which
        // lightens with a light tint at the same alphas.
        StateHover = new Vector4(Ink.X, Ink.Y, Ink.Z, 0.08f),
        StateSelected = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.20f),
    })
    {
        // Focused-input outline for STOCK LibGUI controls (numeric field's TextField, checkboxes, dropdowns).
        // Point it at the accent (Primary) so a focused stock input matches the hand-drawn
        // ScribeMultilineField, which lights its focus border with Primary; unset, it would fall back to
        // LibGUI's stock bright gold and diverge from the parchment accent (refine-chalkboard field-consistency).
        FocusOutlineColor = Accent,
    };

    /// <summary>Chalk-light text drawn on the brown-slate surfaces (<c>OnSurface</c>/<c>OnBackground</c>).
    /// Sampled from the chalkboard's own <c>chalk.png</c> texture (≈<c>#e7e6e6</c>) and nudged a hair warm so
    /// it reads like VS Chalk Stone / real chalk dust rather than a clinical pure white.</summary>
    private static readonly Vector4 Chalk = new(0.91f, 0.90f, 0.87f, 1.0f);

    /// <summary>The chalkboard accent (button fills, carets, selection, and the task-row CHECKBOX tick). A
    /// strong FOREST GREEN — the classic schoolroom "greenboard" colour, complementary to the warm brown-slate
    /// palette so buttons pop without clashing (it replaced an earlier chalk-yellow that read as UI gold).
    /// Brightened by +10 HSV Value points (refine-chalkboard): the original <c>(0.17, 0.42, 0.24)</c> sat at
    /// V≈0.42, which was too dark to read as a checkbox tick on the dark slate; lifting V→0.52 (a uniform ×
    /// 0.52/0.42 scale on all three channels, which holds hue + saturation constant) keeps the exact same
    /// green while making the accent legible. To try the NAVY alternative the author floated, swap this for
    /// <c>new Vector4(0.16f, 0.24f, 0.46f, 1.0f)</c> — its content (<c>OnPrimary</c>) stays the same
    /// chalk-white.</summary>
    private static readonly Vector4 ChalkAccent = new(0.210f, 0.520f, 0.297f, 1.0f);

    /// <summary>The link/tracked-item TEXT accent for the chalkboard (item-name hyperlink, guide-page book
    /// glyph, Tracker have/need count) — a LIGHT chalk-green. The dark forest <see cref="ChalkAccent"/> works
    /// as a button FILL (white text reads on it) but is illegible as small TEXT on the dark slate surface, so
    /// links are decoupled onto this lighter tone (set via the chalkboard's <c>DecorateRowStyle</c> →
    /// <c>ScribeRowStyle.LinkColor</c>). Same green hue family as the accent, lifted in Value so it reads as a
    /// distinct, tappable colored link that is clearly not the chalk-white body text. Only the chalkboard uses
    /// it; every other surface keeps <c>Primary</c> for links (its accent is dark-on-light, legible for free).</summary>
    internal static readonly Vector4 ChalkboardLinkText = new(0.52f, 0.80f, 0.58f, 1.0f);

    /// <summary>Resting tint for the INACTIVE right-column nav glyphs on the chalkboard (<c>#645c52</c>, a
    /// dark slate-brown). The theme's <c>OnSurfaceVariant</c> (a pale chalk-gray) reads as almost-active on
    /// the dark board, so inactive nav icons are darkened via the dedicated <c>NavIconColor</c> seam — muted
    /// body text keeps the lighter <c>OnSurfaceVariant</c>. The ACTIVE tab still uses its own per-view accent.</summary>
    internal static readonly Vector4 ChalkboardNavIcon = new(0.392f, 0.361f, 0.322f, 1.0f);

    /// <summary>The focused-input BORDER color on the chalkboard (a solid chalk line — the <c>Border</c> hue
    /// at full alpha). Every other theme lights a focused input's border with <c>Primary</c> (its accent), but
    /// the chalkboard's <c>Primary</c> is the forest-green <see cref="ChalkAccent"/>, and the author disliked
    /// green on an input border. So the chalkboard decouples the focus border onto this bright chalk tone via
    /// the <c>InputFocusBorderColor</c> seam: at rest the border stays the faint chalk <c>Border</c>, on focus
    /// it brightens to a crisp chalk outline — a "the input lights up in chalk" affordance with no green. The
    /// resting-vs-focus contrast is alpha only (0.42 → 1.0), same RGB, so nothing reflows on focus.</summary>
    internal static readonly Vector4 ChalkboardInputFocusBorder = new(0.82f, 0.84f, 0.80f, 1.0f);

    /// <summary>The chalkboard <c>Secondary</c> role (pinned-row wash / secondary buttons): a muted, lighter
    /// SAGE GREEN — a desaturated sibling of the forest-green <see cref="ChalkAccent"/> <c>Primary</c>
    /// (<c>≈#5C8566</c>). This follows the pattern the Notebook/Lectern (Light theme) uses, where
    /// <c>Secondary</c> (<c>#A07F4D</c>) is a lighter, desaturated sibling of that theme's gold <c>Primary</c>
    /// (<c>#955F21</c>) — same hue family, not an unrelated color. The chalkboard originally used an unrelated
    /// stained-wood brown (<c>0.42,0.31,0.22</c>) here, which — because <see cref="ScribeRowConstants.PinnedTint"/>
    /// derives the pinned-row wash from <c>Secondary</c> (boosted ×1.35 saturation at 0.55 alpha) — read as a
    /// discordant muddy amber over the dark slate (refine-chalkboard). A green-family sage keeps the pinned wash
    /// harmonious with the accent, legible over slate, and still distinct enough from the focus cue.</summary>
    private static readonly Vector4 ChalkSecondary = new(0.36f, 0.52f, 0.40f, 1.0f);

    /// <summary>The chalkboard theme (add-chalkboard-block D3): chalk-light text on a dark slate surface.
    /// Unlike <see cref="Light"/> and the clay palettes — which are light surfaces and therefore DARKEN on
    /// hover/select — this is a DARK scheme, so its <c>StateHover</c>/<c>StateSelected</c> overlays LIGHTEN
    /// (light tints at low alpha), the same mechanical direction as LibGUI's stock dark default. Only the 17
    /// <see cref="ColorScheme"/> roles are authored; the per-widget style structs cascade from them. Resolved
    /// for the chalkboard dialog ONLY (via its <c>ResolveTheme</c> override), so no other surface and no
    /// global preference is affected.</summary>
    internal static readonly ThemeData Chalkboard = new(new ColorScheme
    {
        // Accent + its content. The forest-green accent is a dark fill, so its content (OnPrimary) is a
        // chalk-white that reads against it — matching the body text. Secondary is a muted sage green
        // (pinned-row wash / secondary buttons), a desaturated sibling of the accent, also carrying chalk-white
        // content.
        Primary = ChalkAccent,
        OnPrimary = new Vector4(0.94f, 0.95f, 0.91f, 1.0f),
        Secondary = ChalkSecondary,
        OnSecondary = Chalk,

        // Surfaces: brown slate over dark stained wood, sampled from the chalkboard GUI art (frame ≈#5c4434,
        // board ≈#665f5c). Background is the deepest — the dark stained-wood frame behind the panels; Surface
        // the brown-slate board panel; SurfaceHigh raised (lighter, the input-field "writing" tone, seeded to
        // the sampled board colour); SurfaceLow recessed (darker). Raised/recessed ordering kept correct for a
        // dark scheme (raised = lighter). All warm (R ≥ G ≥ B) so the palette reads brown-slate, not the
        // earlier green-grey.
        Surface = new Vector4(0.30f, 0.27f, 0.25f, 1.0f),
        OnSurface = Chalk,
        OnSurfaceVariant = new Vector4(0.68f, 0.66f, 0.62f, 1.0f),
        Background = new Vector4(0.20f, 0.15f, 0.11f, 1.0f),
        OnBackground = Chalk,
        SurfaceLow = new Vector4(0.24f, 0.21f, 0.19f, 1.0f),
        SurfaceHigh = new Vector4(0.39f, 0.36f, 0.34f, 1.0f),

        // Borders/dividers: a faint chalk line, so they read on the brown slate.
        Border = new Vector4(0.82f, 0.84f, 0.80f, 0.42f),
        OutlineVariant = new Vector4(0.82f, 0.84f, 0.80f, 0.20f),

        // Error stays red but softened toward chalk so it reads on the dark surface; its content is dark.
        Error = new Vector4(0.86f, 0.44f, 0.40f, 1.0f),
        OnError = new Vector4(0.16f, 0.11f, 0.08f, 1.0f),

        // DARK scheme: hover/select LIGHTEN the surface, so these translucent overlays are light chalk /
        // accent tints at low alpha (the opposite direction from the Light/clay palettes).
        StateHover = new Vector4(Chalk.X, Chalk.Y, Chalk.Z, 0.08f),
        StateSelected = new Vector4(ChalkAccent.X, ChalkAccent.Y, ChalkAccent.Z, 0.24f),
    })
    {
        // Focused-input outline for STOCK LibGUI controls (the numeric field's TextField, checkboxes,
        // dropdowns, radios). Left unset, this defaults to LibGUI's stock gold (0.95,0.78,0.38), which read as
        // a jarring gold focus ring on the dark slate and — for the numeric field — diverged from the
        // hand-drawn ScribeMultilineField's chalk-white focus border. Point it at the same chalk tone the
        // multiline field uses (ChalkboardInputFocusBorder) so both input types light their focus border the
        // same, and every stock focus ring drops the gold (refine-chalkboard field-consistency).
        FocusOutlineColor = ChalkboardInputFocusBorder,
    };

    /// <summary>HSV <b>Value</b> points (Skia's 0–100 scale) the muted-text role (<c>OnSurfaceVariant</c>)
    /// is lifted ABOVE each clay palette's own <c>ink</c> — the single shared knob governing muted/hint/
    /// placeholder contrast across fire, red, and blue (tune-tablet-clay-text-contrast D1). Because every
    /// clay <c>ink</c> is near-black, lifting Value by a fixed amount produces a consistent perceptual
    /// muted-vs-ink step per hue. The three previously hand-authored <c>onSurfaceVariant</c> colors all sat
    /// at exactly +20 above their ink; this seeds LOWER (darker) than that so the empty-list hint reads on
    /// the mid-tone clay backdrops (the screenshot symptom). Lower this to darken the muted text further,
    /// raise it toward 20 to lighten — one edit moves all three palettes in lockstep. Finalized in-game
    /// (D3). Muted must stay clearly above ink so it still reads as SECONDARY text, not body ink.</summary>
    private const float MutedTextValueLift = 14f;

    /// <summary>
    /// Author one clay-tablet <see cref="ThemeData"/> from the roles that carry a material's identity, filling
    /// the material-NEUTRAL roles (error, and the two translucent state overlays) from shared clay rules so
    /// the three clay palettes can't drift apart on those (add-tablet-clay-type-themes D1/D2). Every clay
    /// palette is a sibling of <see cref="Light"/> — a light-ish surface with dark ink — so it inherits the
    /// same two SEMANTIC (not mechanical) inversions the class remarks describe: <c>StateHover</c>/
    /// <c>StateSelected</c> DARKEN the surface (ink/accent tints at low alpha), and <c>SurfaceHigh</c> is
    /// raised (lighter) while <c>SurfaceLow</c> is recessed (darker).
    /// </summary>
    /// <param name="ink">Body/title text (<c>OnSurface</c>/<c>OnBackground</c>) — a dark, saturated tone of
    /// the material hue so it reads (with the cuneiform glow's help) against the mid-tone backdrop.</param>
    /// <param name="accent">The <c>Primary</c> accent. Programmatically drives button fill + its hover
    /// (<c>+0.1</c>) / press (<c>−0.08</c>) states, the caret, the focused-input border, and text selection —
    /// so authoring this per material recolors all of them at once.</param>
    /// <param name="onAccent">Light content drawn on the accent fill (<c>OnPrimary</c>/<c>OnSecondary</c>).</param>
    /// <param name="secondary">The <c>Secondary</c> tone that now drives the pinned-row wash
    /// (<c>ScribeRowConstants.PinnedTint</c>, remapped off <c>Primary</c> in this change). MUST read clearly
    /// distinct from <paramref name="accent"/> so a focused input's accent border stays legible against the
    /// pinned wash on the same row.</param>
    /// <param name="surface">The tablet face panel tone.</param>
    /// <param name="surfaceLow">Recessed surface (darker than <paramref name="surface"/>).</param>
    /// <param name="surfaceHigh">Raised surface (lighter) — the input-field background.</param>
    /// <param name="background">The deepest panel/desk tone behind <paramref name="surface"/>.</param>
    /// <param name="border">Input/divider border, RGBA with its own alpha; <c>OutlineVariant</c> reuses the
    /// same RGB at a fainter alpha.</param>
    private static ThemeData ClayPalette(
        Vector4 ink, Vector4 accent, Vector4 onAccent, Vector4 secondary,
        Vector4 surface, Vector4 surfaceLow, Vector4 surfaceHigh, Vector4 background, Vector4 border) =>
        new(new ColorScheme
        {
            Primary = accent,
            OnPrimary = onAccent,
            Secondary = secondary,
            OnSecondary = onAccent,

            Surface = surface,
            OnSurface = ink,
            // Muted/secondary text (hints, placeholders) is DERIVED from this palette's own ink by one
            // shared HSV Value lift (tune-tablet-clay-text-contrast D1), not authored per clay. Lifting the
            // near-black ink UP by MutedTextValueLift points yields a muted tone that tracks each clay's own
            // ink/surface relationship, so the muted-vs-ink contrast step is a perceptually equal amount
            // across fire/red/blue. To darken the muted text everywhere, lower the one constant below.
            OnSurfaceVariant = ScribeRowConstants.ShiftBrightness(ink, MutedTextValueLift),
            Background = background,
            OnBackground = ink,
            SurfaceLow = surfaceLow,
            SurfaceHigh = surfaceHigh,

            Border = border,
            OutlineVariant = border with { W = 0.28f },

            // Material-neutral: a deep clay-red error and its light content, shared across all clay types.
            Error = new Vector4(0.70f, 0.17f, 0.11f, 1.0f),
            OnError = new Vector4(0.96f, 0.90f, 0.78f, 1.0f),

            // Semantic (not mechanical) inversion: hover/select DARKEN a light-ish surface — dark ink / accent
            // tints at low alpha (see the Light theme's matching note). Shared alphas across clay types.
            StateHover = new Vector4(ink.X, ink.Y, ink.Z, 0.08f),
            StateSelected = new Vector4(accent.X, accent.Y, accent.Z, 0.20f),
        })
        {
            // Focused-input outline for STOCK LibGUI controls: the clay accent, matching the hand-drawn
            // ScribeMultilineField's Primary focus border (unset it would fall back to LibGUI's stock gold,
            // which diverges from each clay's own accent) — refine-chalkboard field-consistency.
            FocusOutlineColor = accent,
        };

    /// <summary>Fire-clay tablet palette — warm tan earthenware. The original single-tablet palette
    /// (add-tablet-dialog, Proposal C) rebased through <see cref="ClayPalette"/>; also the fallback for
    /// <c>wax</c> and unknown materials, matching its interim backdrop twin. Seeded to sit against the fire
    /// <c>-soft.png</c> backdrop (sampled center ≈ <c>#ccaf89</c>). <c>Secondary</c> is a deep umber, pulled
    /// off the terracotta <c>Primary</c> so the pinned wash and the focus border read apart.</summary>
    internal static readonly ThemeData TabletFire = ClayPalette(
        ink:              new Vector4(0.20f, 0.10f, 0.05f, 1.0f),
        accent:           new Vector4(0.55f, 0.30f, 0.15f, 1.0f),
        onAccent:         new Vector4(0.96f, 0.90f, 0.78f, 1.0f),
        secondary:        new Vector4(0.42f, 0.32f, 0.18f, 1.0f),
        surface:          new Vector4(0.80f, 0.66f, 0.50f, 1.0f),
        surfaceLow:       new Vector4(0.70f, 0.55f, 0.39f, 1.0f),
        surfaceHigh:      new Vector4(0.87f, 0.74f, 0.58f, 1.0f),
        background:       new Vector4(0.72f, 0.57f, 0.41f, 1.0f),
        border:           new Vector4(0.36f, 0.24f, 0.12f, 0.55f));

    /// <summary>Red-clay tablet palette — dusty rose, NOT brick terracotta. The accent was retuned to match
    /// the red <c>-soft.png</c> backdrop after playtest: the art's writing panel samples ≈<c>#926f6d</c>
    /// (HSV ≈3° / 25 % sat — a muted mauve-rose), but the old <c>Primary</c> was a brick-orange
    /// (HSV ≈9° / 67 % sat) that read too orange and too saturated against it. The accent now sits at
    /// ≈4° / 50 % sat (<c>#8e4c47</c>) — same rosy family as the backdrop, a shade deeper/more saturated than
    /// the panel so a button fill still reads as a distinct element. Ink/secondary follow the same hue pull.
    /// Surfaces already tracked the backdrop and are unchanged. <c>Secondary</c> (the pinned-row wash) stays a
    /// desaturated rosy-taupe, distinct from the accent so a focused input's accent border reads apart on a
    /// pinned row.</summary>
    internal static readonly ThemeData TabletRed = ClayPalette(
        ink:              new Vector4(0.24f, 0.10f, 0.09f, 1.0f),
        accent:           new Vector4(0.56f, 0.30f, 0.28f, 1.0f),
        onAccent:         new Vector4(0.97f, 0.90f, 0.84f, 1.0f),
        secondary:        new Vector4(0.46f, 0.34f, 0.33f, 1.0f),
        surface:          new Vector4(0.82f, 0.62f, 0.58f, 1.0f),
        surfaceLow:       new Vector4(0.72f, 0.52f, 0.49f, 1.0f),
        surfaceHigh:      new Vector4(0.88f, 0.72f, 0.68f, 1.0f),
        background:       new Vector4(0.74f, 0.54f, 0.50f, 1.0f),
        border:           new Vector4(0.42f, 0.20f, 0.16f, 0.55f));

    /// <summary>Blue-clay tablet palette — cool slate blue-grey. Seeded to sit against the blue
    /// <c>-soft.png</c> backdrop (sampled center ≈ <c>#98a6af</c>): steel-blue <c>Primary</c>, deep-slate
    /// ink, and a neutral warm-grey <c>Secondary</c> whose pinned wash reads apart from the blue accent
    /// border. The one cool clay palette — its <c>onAccent</c> is a cool near-white rather than the warm
    /// cream the earthen palettes use.</summary>
    internal static readonly ThemeData TabletBlue = ClayPalette(
        ink:              new Vector4(0.12f, 0.16f, 0.20f, 1.0f),
        accent:           new Vector4(0.26f, 0.42f, 0.52f, 1.0f),
        onAccent:         new Vector4(0.93f, 0.96f, 0.98f, 1.0f),
        secondary:        new Vector4(0.42f, 0.46f, 0.48f, 1.0f),
        surface:          new Vector4(0.76f, 0.82f, 0.86f, 1.0f),
        surfaceLow:       new Vector4(0.64f, 0.71f, 0.76f, 1.0f),
        surfaceHigh:      new Vector4(0.84f, 0.89f, 0.92f, 1.0f),
        background:       new Vector4(0.66f, 0.73f, 0.78f, 1.0f),
        border:           new Vector4(0.20f, 0.30f, 0.36f, 0.55f));

    /// <summary>Wax tablet palette — pale honey/beeswax, NOT the terracotta the clay palettes use. Wax rode
    /// <see cref="TabletFire"/> as a stopgap while it shared the fire backdrop, but that terracotta accent
    /// (HSV ≈22° / 73 % sat) read too orange against the bespoke wax GUI art, whose beige writing panel
    /// samples ≈<c>#dfc8a9</c> (HSV ≈34° / 24 % sat — a much paler, less saturated warm tan). This palette
    /// pulls the whole scheme toward that hue: a soft honey <c>Primary</c> at far lower saturation, a warm
    /// mid-brown ink that still reads on the light panel, and surfaces seeded just off the sampled panel tone
    /// so the flat-panel fallback and the art agree. Authored through the shared <see cref="ClayPalette"/>
    /// factory like the three clays, so it inherits the same semantic hover/select inversions and the derived
    /// muted-text lift.</summary>
    internal static readonly ThemeData TabletWax = ClayPalette(
        ink:              new Vector4(0.28f, 0.22f, 0.12f, 1.0f),
        accent:           new Vector4(0.62f, 0.49f, 0.26f, 1.0f),
        onAccent:         new Vector4(0.98f, 0.95f, 0.86f, 1.0f),
        secondary:        new Vector4(0.56f, 0.47f, 0.32f, 1.0f),
        surface:          new Vector4(0.87f, 0.78f, 0.62f, 1.0f),
        surfaceLow:       new Vector4(0.80f, 0.70f, 0.53f, 1.0f),
        surfaceHigh:      new Vector4(0.93f, 0.86f, 0.72f, 1.0f),
        background:       new Vector4(0.83f, 0.73f, 0.57f, 1.0f),
        border:           new Vector4(0.44f, 0.34f, 0.18f, 0.55f));

    /// <summary>The single theme selector Scribe's core views call: the net-new <see cref="Light"/>
    /// parchment theme when Pixel-Art Display is on, or the player's global theme
    /// (<see cref="ThemeData.Default"/>, loaded from their <c>libgui.json</c> by LibGUI) when it is off.
    /// The off path depends on no art, keeping the mod fully usable with zero assets.</summary>
    public static ThemeData For(bool pixelArt) => pixelArt ? Light : ThemeData.Default;

    /// <summary>The tablet-tier selector: a per-clay-type palette when Pixel-Art Display is on, keyed to the
    /// item's <c>material</c> variant (add-tablet-clay-type-themes D1) — <c>clay-red</c>→<see cref="TabletRed"/>,
    /// <c>clay-blue</c>→<see cref="TabletBlue"/>, <c>clay-fire</c>→<see cref="TabletFire"/>,
    /// <c>wax</c>→<see cref="TabletWax"/> (its own pale-honey palette matched to the bespoke wax GUI art), and
    /// any unrecognized material→<see cref="TabletFire"/> (the fire backdrop twin, so the resolved theme
    /// and backdrop always agree — mirrors <c>ScribeBackdrops.ForTablet</c>'s default arm). When Pixel-Art
    /// Display is off it returns the player's global theme (<see cref="ThemeData.Default"/>), same off-path
    /// rule as <see cref="For"/> — per-clay coloring applies ONLY with Pixel-Art on. The tablet dialog calls
    /// this in its <c>Build()</c> theme wrapper instead of <see cref="For"/>.</summary>
    public static ThemeData ForTablet(string? material, bool pixelArt)
    {
        if (!pixelArt) return ThemeData.Default;
        return material switch
        {
            "clay-blue" => TabletBlue,
            "clay-fire" => TabletFire,
            "clay-red" => TabletRed,
            "wax" => TabletWax, // bespoke pale-honey palette matched to the wax GUI art (not the fire twin)
            _ => TabletFire, // any unrecognized material rides the fire palette (its backdrop twin)
        };
    }
}
