using System.Runtime.InteropServices;
using Scribe;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Platform gating and RID classification for <see cref="ScribeHarfBuzzLoadFix"/>, run as plain xUnit
/// facts (no Atlas world boot). Native dlopen/Harmony behavior is host-dependent and needs manual
/// verification on Linux.
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
        // Unnamed values (e.g. Wasm) must fall back to the x64 folder rather than throw: the bundled
        // native asset ships only arm/arm64/x86/x64 RID folders, and a throw would crash startup.
        Assert.Equal("linux-x64", ScribeHarfBuzzLoadFix.GetLinuxRid(Architecture.Wasm));
    }
}
