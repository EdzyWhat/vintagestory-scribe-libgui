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

    [Fact]
    public void Default_MuteUiSounds_IsOff()
    {
        // A fresh profile (no saved preference) has Scribe's own UI click sounds ON (mute off);
        // Normalized() leaves a bool untouched (nothing to clamp), so a stored value round-trips unchanged.
        Assert.False(new ScribePlayerSettings().MuteUiSounds);
        Assert.False(new ScribePlayerSettings().Normalized().MuteUiSounds);
        Assert.True(new ScribePlayerSettings { MuteUiSounds = true }.Normalized().MuteUiSounds);
    }

    [Fact]
    public void Default_TimerAutoDisappear_IsOn()
    {
        // A fresh profile (no saved preference) keeps the original behavior: a fired timer disappears
        // from the HUD after ~30 s (timer-auto-disappear-setting). Normalized() leaves a bool untouched
        // (nothing to clamp), so a stored value round-trips unchanged — including an explicit false.
        Assert.True(new ScribePlayerSettings().TimerAutoDisappear);
        Assert.True(new ScribePlayerSettings().Normalized().TimerAutoDisappear);
        Assert.False(new ScribePlayerSettings { TimerAutoDisappear = false }.Normalized().TimerAutoDisappear);
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
    [InlineData(100, 400)]    // below min -> clamp to min
    [InlineData(0, 400)]
    [InlineData(-50, 400)]
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
    public void NormalizePolicy_UnpinSink_IsPreserved()
    {
        // UnpinSink (=4) is a defined policy and must survive normalization.
        Assert.Equal(ScribeCompletionPolicy.UnpinSink,
            ScribePlayerSettings.NormalizePolicy(ScribeCompletionPolicy.UnpinSink));

        var s = new ScribePlayerSettings { CompletionPolicy = ScribeCompletionPolicy.UnpinSink }.Normalized();
        Assert.Equal(ScribeCompletionPolicy.UnpinSink, s.CompletionPolicy);
    }

    [Fact]
    public void MaxHudMaxRows_IsTen()
    {
        // The HUD row cap was lowered 20 -> 10 (§10.3); a saved 11-20 re-clamps to 10 on next load. The
        // clamp behavior across the bound is covered by ScribePinCodecTests.Settings_ClampHudMaxRows_*.
        Assert.Equal(10, ScribePlayerSettings.MaxHudMaxRows);
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

    [Fact]
    public void Default_CuneiformTablets_IsOn()
    {
        // Positive polarity per D8: a fresh profile writes in cuneiform by default. Normalized() leaves a
        // plain bool untouched, so an explicit false round-trips.
        Assert.True(new ScribePlayerSettings().CuneiformTablets);
        Assert.True(new ScribePlayerSettings().Normalized().CuneiformTablets);
        Assert.False(new ScribePlayerSettings { CuneiformTablets = false }.Normalized().CuneiformTablets);
    }

    [Fact]
    public void Migrate_LegacyDisableCuneiform_InvertsToCuneiformTablets()
    {
        // A pre-flip config carried the negative DisableCuneiformFont key. The migration inverts it once
        // (D8): a player who had cuneiform OFF (DisableCuneiformFont = true) must land at
        // CuneiformTablets = false, NOT the new true default — so the flip doesn't silently re-enable
        // cuneiform for someone who deliberately turned it off.
        var hadItOff = new ScribePlayerSettings { DisableCuneiformFont = true }.Normalized();
        Assert.False(hadItOff.CuneiformTablets);

        var hadItOn = new ScribePlayerSettings { DisableCuneiformFont = false }.Normalized();
        Assert.True(hadItOn.CuneiformTablets);
    }

    [Fact]
    public void Migrate_AbsentLegacyKey_KeepsNewDefault()
    {
        // A config written by the current code has no legacy key (DisableCuneiformFont is null), so the
        // migration is a no-op and the CuneiformTablets value (its true default here) is left alone.
        var s = new ScribePlayerSettings().Normalized();
        Assert.Null(s.DisableCuneiformFont);
        Assert.True(s.CuneiformTablets);
    }

    [Fact]
    public void Migrate_ClearsLegacyKey_AndIsIdempotent()
    {
        // After migrating, the legacy field is cleared (so it is never re-serialized) and a second call is
        // a no-op — it must not re-invert an already-migrated value back to the default.
        var s = new ScribePlayerSettings { DisableCuneiformFont = true };
        s.MigrateLegacyKeys();
        Assert.Null(s.DisableCuneiformFont);
        Assert.False(s.CuneiformTablets);

        s.MigrateLegacyKeys();
        Assert.False(s.CuneiformTablets);
    }

    [Fact]
    public void ShouldSerializeDisableCuneiformFont_IsFalse()
    {
        // The Newtonsoft ShouldSerialize convention keeps the migrated legacy key out of the written file,
        // so a saved config carries only the new positive key.
        Assert.False(new ScribePlayerSettings().ShouldSerializeDisableCuneiformFont());
    }

    [Theory]
    [InlineData(ScribeHudAnchor.TopLeft, true)]
    [InlineData(ScribeHudAnchor.MiddleLeft, true)]
    [InlineData(ScribeHudAnchor.BottomLeft, true)]
    [InlineData(ScribeHudAnchor.TopRight, false)]
    [InlineData(ScribeHudAnchor.MiddleRight, false)]
    [InlineData(ScribeHudAnchor.BottomRight, false)]
    [InlineData(ScribeHudAnchor.TopMiddle, false)]
    public void IsLeftAnchored_ClassifiesHorizontalSide(ScribeHudAnchor anchor, bool expected)
    {
        // Drives the HUD header/footer text alignment (v1-playtest-fixes 5.3): only the three Left anchors
        // are left-aligned; center and both Right anchors hug the right edge, matching the ApplyAnchor
        // X-position switch (only Left anchors add the offset from the left margin).
        Assert.Equal(expected, anchor.IsLeftAnchored());
    }

    // ---- Illumination floor (respect-local-illumination) ----

    [Fact]
    public void Default_IlluminationFloor_MatchesDrawnCurveFloor()
    {
        // The shipped default equals the author-drawn curve's x=0 anchor, so a fresh profile reproduces the
        // graph exactly. Normalized() leaves an in-range value untouched.
        Assert.Equal(0.05f, new ScribePlayerSettings().IlluminationFloor, 4);
        Assert.Equal(0.05f, new ScribePlayerSettings().Normalized().IlluminationFloor, 4);
    }

    [Theory]
    [InlineData(-1f, ScribePlayerSettings.MinIlluminationFloor)]   // below range → min (a hair above black)
    [InlineData(0f, ScribePlayerSettings.MinIlluminationFloor)]    // exactly 0 → clamped up off pure black
    [InlineData(0.5f, 0.5f)]                                        // in range → unchanged
    [InlineData(2f, ScribePlayerSettings.MaxIlluminationFloor)]    // above range → 1.0 (always full bright)
    public void Normalized_IlluminationFloor_ClampsToRange(float stored, float expected)
    {
        var s = new ScribePlayerSettings { IlluminationFloor = stored }.Normalized();
        Assert.Equal(expected, s.IlluminationFloor, 4);
    }
}
