using Xunit;

namespace Scribe.Core.Tests;

public class ScribeMathTests
{
    [Theory]
    [InlineData(-3f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(7f, 1f)]
    public void Clamp01_limits_to_unit_range(float input, float expected)
        => Assert.Equal(expected, ScribeMath.Clamp01(input));
}
