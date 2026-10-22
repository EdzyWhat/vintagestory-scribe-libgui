using OpenTK.Mathematics;   // Vector4

namespace Scribe;

/// <summary>
/// Everything that varies with a tablet's <c>(material, state)</c>, resolved once: readability (ink, glow,
/// stroke weight), the clay palette key, the backdrop, the Quest Link accent and the dialog layout. Callers
/// hold one <see cref="TabletStyle"/> instead of switching on the raw material string.
///
/// <para>An unrecognized or absent material resolves to <c>clay-red</c>, the same default the item uses
/// for a missing variant. <c>wax</c> has only a wet state, so any wax state resolves to wet.</para>
/// </summary>
internal sealed record TabletStyle(
    string Material,
    TabletState State,
    TabletReadability Readability,
    ScribeBackdropSpec Backdrop,
    Vector4 QuestLinkAccent,
    ScribeLayoutProportions Layout)
{
    /// <summary>The base material resolved for unrecognized or absent input.</summary>
    public const string DefaultMaterial = "clay-red";

    /// <summary>Whether this is the wax tablet (wet only, with its own panel proportions).</summary>
    public bool IsWax => Material == "wax";

    // Clay fills more of the wide backdrop: shorter title band, taller inner section, narrow side margins.
    private static readonly ScribeLayoutProportions ClayLayout = ScribeLayoutProportions.Default with
    {
        TitleBarFrac   = 0.11f,
        InnerHFrac     = 0.83f,
        SideColFrac    = 0.06f,
        TitleBtnsWFrac = 0.86f,
    };

    // Wax insets further because its art frames a smaller beige panel; its gutters are deliberately unequal
    // per axis (the art is taller than wide). It wraps the title sooner because its panel is narrower.
    private static readonly ScribeLayoutProportions WaxLayout = ScribeLayoutProportions.Default with
    {
        TitleBarFrac   = 0.15f,
        InnerHFrac     = 0.775f,
        SideColFrac    = 0.08f,
        TitleBtnsWFrac = 0.82f,
    };

    /// <summary>Resolves the style for a tablet's base <paramref name="material"/> (no <c>-hard</c>/<c>-fired</c>
    /// suffix) and drying <paramref name="state"/>.</summary>
    public static TabletStyle For(string? material, TabletState state)
    {
        string resolved = material is "clay-red" or "clay-blue" or "clay-fire" or "wax" ? material : DefaultMaterial;
        if (resolved == "wax") state = TabletState.Wet;
        return new TabletStyle(
            resolved,
            state,
            TabletReadability.For(resolved, state),
            ScribeBackdrops.ForTablet(resolved, state),
            ScribeTheme.ForTabletQuestLink(resolved),
            resolved == "wax" ? WaxLayout : ClayLayout);
    }
}
