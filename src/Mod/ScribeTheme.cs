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
    });

    /// <summary>Deep clay ink — a dark red-brown, one notch warmer and redder than <see cref="Ink"/> — so
    /// tablet body/title text reads as pressed into earthenware rather than penned on paper.</summary>
    private static readonly Vector4 TabletInk = new(0.20f, 0.10f, 0.05f, 1.0f);

    /// <summary>The tablet's warm accent — a terracotta/brick tone deeper than the parchment
    /// <see cref="Accent"/> ochre, to sit against the clay/wax backdrops.</summary>
    private static readonly Vector4 TabletAccent = new(0.55f, 0.30f, 0.15f, 1.0f);

    /// <summary>
    /// The tablet tier's earthen/clay palette (add-tablet-dialog, Proposal C). A warm clay surface with
    /// dark red-brown ink — sibling to <see cref="Light"/> but pushed from parchment toward fired earth.
    ///
    /// <para><b>Placeholder this round.</b> The real text-contrast decision is deliberately DEFERRED until
    /// the clay/wax backdrops render in-game: the VS materials those backdrops target span the color gamut
    /// (pale-blue and pale-pink unfired clays, dark-grey and brown fired clays, gold beeswax), so a single
    /// fixed ink color may not stay legible across all of them. The eventual choice — one ink color / dark
    /// text + a light shadow-glow / per-backdrop ink — is captured in the
    /// <c>tablet-theme-contrast-vs-backdrops</c> note and revisited once the art exists. Authored role-by-
    /// role from the same rules as <see cref="Light"/> (see the class remarks for the two semantic
    /// inversions).</para>
    /// </summary>
    internal static readonly ThemeData Tablet = new(new ColorScheme
    {
        Primary = TabletAccent,
        OnPrimary = new Vector4(0.96f, 0.90f, 0.78f, 1.0f),
        Secondary = new Vector4(0.60f, 0.42f, 0.26f, 1.0f),
        OnSecondary = new Vector4(0.96f, 0.90f, 0.78f, 1.0f),

        // Surfaces: warm fired-clay tones. Background is the deepest (a darker earthen tone), Surface the
        // tablet face on top, SurfaceHigh raised (lightest), SurfaceLow recessed (darker) — the
        // raised/recessed ordering kept semantically correct for a light-ish scheme.
        Surface = new Vector4(0.80f, 0.66f, 0.50f, 1.0f),
        OnSurface = TabletInk,
        OnSurfaceVariant = new Vector4(0.40f, 0.28f, 0.18f, 1.0f),
        Background = new Vector4(0.72f, 0.57f, 0.41f, 1.0f),
        OnBackground = TabletInk,
        SurfaceLow = new Vector4(0.70f, 0.55f, 0.39f, 1.0f),
        SurfaceHigh = new Vector4(0.87f, 0.74f, 0.58f, 1.0f),

        // Borders/dividers: a deep brown with alpha, so they read on the clay surface.
        Border = new Vector4(0.36f, 0.24f, 0.12f, 0.55f),
        OutlineVariant = new Vector4(0.36f, 0.24f, 0.12f, 0.28f),

        Error = new Vector4(0.70f, 0.17f, 0.11f, 1.0f),
        OnError = new Vector4(0.96f, 0.90f, 0.78f, 1.0f),

        // Semantic (not mechanical) inversion: hover/select DARKEN a light-ish surface — dark ink / accent
        // tints at low alpha (see the Light theme's matching note).
        StateHover = new Vector4(TabletInk.X, TabletInk.Y, TabletInk.Z, 0.08f),
        StateSelected = new Vector4(TabletAccent.X, TabletAccent.Y, TabletAccent.Z, 0.20f),
    });

    /// <summary>The single theme selector Scribe's core views call: the net-new <see cref="Light"/>
    /// parchment theme when Pixel-Art Display is on, or the player's global theme
    /// (<see cref="ThemeData.Default"/>, loaded from their <c>libgui.json</c> by LibGUI) when it is off.
    /// The off path depends on no art, keeping the mod fully usable with zero assets.</summary>
    public static ThemeData For(bool pixelArt) => pixelArt ? Light : ThemeData.Default;

    /// <summary>The tablet-tier selector: the earthen <see cref="Tablet"/> palette when Pixel-Art Display
    /// is on, else the player's global theme (same off-path rule as <see cref="For"/>). The tablet dialog
    /// calls this in its <c>Build()</c> theme wrapper instead of <see cref="For"/> so it draws its own
    /// clay palette rather than the parchment one (add-tablet-dialog D6).</summary>
    public static ThemeData ForTablet(bool pixelArt) => pixelArt ? Tablet : ThemeData.Default;
}
