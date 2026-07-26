using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the per-player settings normalization: the font-scale clamps/snaps and the HUD-offset
// clamps added by add-settings-tab, plus a regression guard that the existing enum/numeric
// normalization still holds when consolidated into one config. Pure/game-agnostic.
public class ScribePlayerSettingsTests
{
    [Fact]
    public void Defaults_FontScales_AreOne()
    {
        var s = new ScribePlayerSettings();

        Assert.Equal(1.0f, s.HudFontScale);
        Assert.Equal(1.0f, s.WindowFontScale);
    }

    [Fact]
    public void Normalized_FontScales_AtDefault_StayOne()
    {
        var s = new ScribePlayerSettings().Normalized();

        Assert.Equal(1.0f, s.HudFontScale);
        Assert.Equal(1.0f, s.WindowFontScale);
    }

    [Fact]
    public void Default_PixelArtDisplay_IsOn()
    {
        // A fresh profile (no saved preference) gets the pixel-art (light theme) look for the core views;
        // Normalized() leaves a bool untouched (nothing to clamp), so a stored value round-trips unchanged.
        Assert.True(new ScribePlayerSettings().PixelArtDisplay);
        Assert.True(new ScribePlayerSettings().Normalized().PixelArtDisplay);
        Assert.False(new ScribePlayerSettings { PixelArtDisplay = false }.Normalized().PixelArtDisplay);
    }

    [Theory]
    [InlineData(2.5f, 1.2f)]   // above max -> clamp to max notch
    [InlineData(1.5f, 1.2f)]
    [InlineData(0.1f, 0.8f)]   // below min -> clamp to min notch
    [InlineData(0.0f, 0.8f)]
    [InlineData(-3.0f, 0.8f)]
    public void Normalized_OutOfRangeFontScale_ClampsToBound(float raw, float expected)
    {
        var s = new ScribePlayerSettings { HudFontScale = raw, WindowFontScale = raw }.Normalized();

        Assert.Equal(expected, s.HudFontScale);
        Assert.Equal(expected, s.WindowFontScale);
    }

    [Theory]
    [InlineData(0.86f, 0.85f)]  // snaps to the 5% notch, not 0.1
    [InlineData(0.84f, 0.85f)]
    [InlineData(0.94f, 0.95f)]
    [InlineData(1.04f, 1.05f)]
    [InlineData(1.16f, 1.15f)]
    [InlineData(1.13f, 1.15f)]
    public void Normalized_FontScale_SnapsToNearestNotch(float raw, float expected)
    {
        var s = new ScribePlayerSettings { HudFontScale = raw, WindowFontScale = raw }.Normalized();

        Assert.Equal(expected, s.HudFontScale, 3);
        Assert.Equal(expected, s.WindowFontScale, 3);
    }

    [Theory]
    [InlineData(500, 300)]     // above max -> clamp
    [InlineData(301, 300)]
    [InlineData(-500, -300)]   // below min -> clamp
    [InlineData(-301, -300)]
    [InlineData(200, 200)]     // in range -> unchanged
    [InlineData(-200, -200)]
    [InlineData(0, 0)]
    public void Normalized_HudOffsets_ClampToRange(int raw, int expected)
    {
        var s = new ScribePlayerSettings { HudOffsetX = raw, HudOffsetY = raw }.Normalized();

        Assert.Equal(expected, s.HudOffsetX);
        Assert.Equal(expected, s.HudOffsetY);
    }

    [Fact]
    public void Default_PixelArtSize_Is600()
    {
        Assert.Equal(600, new ScribePlayerSettings().PixelArtSize);
        Assert.Equal(600, new ScribePlayerSettings().Normalized().PixelArtSize);
    }

    [Theory]
    [InlineData(2000, 1000)]  // above max -> clamp to max
    [InlineData(1001, 1000)]
    [InlineData(100, 300)]    // below min -> clamp to min
    [InlineData(0, 300)]
    [InlineData(-50, 300)]
    [InlineData(600, 600)]    // in range, already on grid -> unchanged
    public void Normalized_PixelArtSize_ClampsToRange(int raw, int expected)
    {
        var s = new ScribePlayerSettings { PixelArtSize = raw }.Normalized();

        Assert.Equal(expected, s.PixelArtSize);
    }

    [Theory]
    [InlineData(603, 600)]   // snaps down to the nearest 10
    [InlineData(607, 610)]   // snaps up to the nearest 10
    [InlineData(615, 620)]   // 61.5 -> 62 (banker's rounding ToEven) -> 620
    [InlineData(444, 440)]
    [InlineData(446, 450)]
    public void Normalized_PixelArtSize_SnapsToTenGrid(int raw, int expected)
    {
        var s = new ScribePlayerSettings { PixelArtSize = raw }.Normalized();

        Assert.Equal(expected, s.PixelArtSize);
    }

    [Fact]
    public void NormalizePolicy_Keep_IsPreserved()
    {
        // Keep (=3) is a defined policy and must survive normalization (regression against the new value).
        Assert.Equal(ScribeCompletionPolicy.Keep,
            ScribePlayerSettings.NormalizePolicy(ScribeCompletionPolicy.Keep));

        var s = new ScribePlayerSettings { CompletionPolicy = ScribeCompletionPolicy.Keep }.Normalized();
        Assert.Equal(ScribeCompletionPolicy.Keep, s.CompletionPolicy);
    }

    [Fact]
    public void Normalized_UnknownEnums_FallBackToDefault()
    {
        // Regression: consolidating the config must not change the enum/numeric normalization contract.
        var s = new ScribePlayerSettings
        {
            CompletionPolicy = (ScribeCompletionPolicy)200,
            HudAnchor = (ScribeHudAnchor)200,
            HudMaxRows = 9999,
            HudRowWidth = 9999,
        }.Normalized();

        Assert.Equal(ScribeCompletionPolicy.Sink, s.CompletionPolicy);
        Assert.Equal(ScribeHudAnchor.TopRight, s.HudAnchor);
        Assert.Equal(ScribePlayerSettings.MaxHudMaxRows, s.HudMaxRows);
        Assert.Equal(ScribePlayerSettings.MaxHudRowWidth, s.HudRowWidth);
    }
}
