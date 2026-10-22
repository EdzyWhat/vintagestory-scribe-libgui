using System.Security.Cryptography;
using System.Text;
using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Pins every shipped tablet <c>(material, state)</c> style (ink, glow, stroke scale, palette, link inks and
/// backdrop) so routing the styling through one resolver cannot change what any variant looks like. Each
/// variant is described as text and compared by hash; a failure prints the description to diff.
/// </summary>
public class TabletStyleMatrixScenarios : AtlasScenarioBase
{
    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];

    private static string Describe(string material, TabletState state)
    {
        var style = TabletStyle.For(material, state);
        var theme = ScribeTheme.ForTablet(style, pixelArt: true);
        return $"{style.Readability}|{theme}|{style.Readability.LinkInk}|{style.QuestLinkAccent}"
             + $"|{style.Backdrop.Texture}|{style.Backdrop.Tint}";
    }

    private static readonly (string Material, TabletState State, string Hash)[] Variants =
    {
        ("clay-red",  TabletState.Wet,   "F28874D8CB1F61F7"),
        ("clay-red",  TabletState.Hard,  "9848FF58F7DB69BB"),
        ("clay-red",  TabletState.Fired, "A6FF233600EF4093"),
        ("clay-blue", TabletState.Wet,   "3B0E755CDC0C8397"),
        ("clay-blue", TabletState.Hard,  "32F5ECEB2C4AC4CC"),
        ("clay-blue", TabletState.Fired, "E075117ADBB85762"),
        ("clay-fire", TabletState.Wet,   "A985C448C3B01A74"),
        ("clay-fire", TabletState.Hard,  "DC72AC49BF74F56F"),
        ("clay-fire", TabletState.Fired, "068CB6139B955E64"),
        ("wax",       TabletState.Wet,   "6CA25C663146B45C"),
    };

    [AtlasScenario]
    public Task Shipped_variant_styles_are_pinned()
    {
        var mismatches = Variants
            .Where(v => Hash(Describe(v.Material, v.State)) != v.Hash)
            .Select(v => $"{v.Material}/{v.State} {Hash(Describe(v.Material, v.State))}\n{Describe(v.Material, v.State)}");
        Assert.True(!mismatches.Any(), string.Join("\n", mismatches));
        return Task.CompletedTask;
    }

    [AtlasScenario]
    public Task Unknown_or_absent_material_resolves_to_red()
    {
        foreach (var state in new[] { TabletState.Wet, TabletState.Hard, TabletState.Fired })
        {
            string red = Describe("clay-red", state);
            Assert.Equal(red, Describe("granite", state));
            Assert.Equal(red, Describe(null!, state));
        }
        return Task.CompletedTask;
    }

    [AtlasScenario]
    public Task Layout_proportions_are_pinned_per_material()
    {
        foreach (var clay in new[] { "clay-red", "clay-blue", "clay-fire" })
        {
            var l = TabletStyle.For(clay, TabletState.Wet).Layout;
            Assert.Equal((0.11f, 0.83f, 0.06f, 0.86f), (l.TitleBarFrac, l.InnerHFrac, l.SideColFrac, l.TitleBtnsWFrac));
        }
        var wax = TabletStyle.For("wax", TabletState.Wet);
        Assert.True(wax.IsWax);
        Assert.Equal((0.15f, 0.775f, 0.08f, 0.82f),
            (wax.Layout.TitleBarFrac, wax.Layout.InnerHFrac, wax.Layout.SideColFrac, wax.Layout.TitleBtnsWFrac));
        return Task.CompletedTask;
    }
}
