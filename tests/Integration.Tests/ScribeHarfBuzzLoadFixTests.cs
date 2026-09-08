using System.Runtime.InteropServices;
using Scribe;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// broaden-linux-harfbuzz-fix task 3.1: platform gating and RID outcome classification for
/// <see cref="ScribeHarfBuzzLoadFix"/>, run as plain xUnit facts (no Atlas world boot) the same
/// way <see cref="ScribeReadViewFilterTests"/> exercises other pure Mod-layer logic — neither
/// <see cref="ScribeHarfBuzzLoadFix.ClassifyPlatform"/> nor <see cref="ScribeHarfBuzzLoadFix.GetLinuxRid"/>
/// touch the Vintage Story API, Harmony, or an actual native library, so both are testable on any
/// host OS without a real glibc/non-glibc Linux machine. The remaining native dlopen/Harmony-patch
/// behavior is inherently host- and platform-dependent and stays covered by the manual Linux
/// verification in tasks 3.3-3.5, not here.
/// </summary>
public class ScribeHarfBuzzLoadFixTests
{
    [Fact]
    public void NonLinux_is_always_classified_NotLinux_regardless_of_libc()
    {
        Assert.Equal(HarfBuzzPlatformSupport.NotLinux, ScribeHarfBuzzLoadFix.ClassifyPlatform(isLinux: false, isGlibc: true));
        Assert.Equal(HarfBuzzPlatformSupport.NotLinux, ScribeHarfBuzzLoadFix.ClassifyPlatform(isLinux: false, isGlibc: false));
    }

    [Fact]
    public void NonGlibc_Linux_is_classified_UnsupportedLibc()
    {
        Assert.Equal(HarfBuzzPlatformSupport.UnsupportedLibc, ScribeHarfBuzzLoadFix.ClassifyPlatform(isLinux: true, isGlibc: false));
    }

    [Fact]
    public void Glibc_Linux_is_classified_GlibcLinux()
    {
        Assert.Equal(HarfBuzzPlatformSupport.GlibcLinux, ScribeHarfBuzzLoadFix.ClassifyPlatform(isLinux: true, isGlibc: true));
    }

    [Theory]
    [InlineData(Architecture.Arm, "linux-arm")]
    [InlineData(Architecture.Arm64, "linux-arm64")]
    [InlineData(Architecture.X86, "linux-x86")]
    [InlineData(Architecture.X64, "linux-x64")]
    public void GetLinuxRid_maps_known_architectures(Architecture architecture, string expectedRid)
    {
        Assert.Equal(expectedRid, ScribeHarfBuzzLoadFix.GetLinuxRid(architecture));
    }

    [Fact]
    public void GetLinuxRid_falls_back_to_linux_x64_for_an_unrecognized_architecture()
    {
        // Wasm (and any future value the switch doesn't name) must fall back to the x64 folder name
        // rather than throw -- the bundled native asset only ships arm/arm64/x86/x64 RID folders, and
        // a missing-case exception here would crash startup instead of just skipping isolation.
        Assert.Equal("linux-x64", ScribeHarfBuzzLoadFix.GetLinuxRid(Architecture.Wasm));
    }
}
