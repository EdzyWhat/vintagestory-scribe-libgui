using Scribe.Core;
using Xunit;

namespace Scribe.Core.Tests;

public class TimerFormValuesTests
{
    [Theory]
    [InlineData(TimerMode.InGame)]
    [InlineData(TimerMode.RealTime)]
    public void Blank_IsEmptyWithPreferredMode(TimerMode preferred)
    {
        var blank = TimerFormValues.Blank(preferred);
        Assert.Equal("", blank.Label);
        Assert.Equal(0, blank.Hours);
        Assert.Equal(0, blank.Minutes);
        Assert.Equal(0, blank.Seconds);
        Assert.Equal(preferred, blank.Mode);
    }

    [Fact]
    public void Equality_ComparesAllFields()
    {
        var a = new TimerFormValues("Bread", 0, 12, 30, TimerMode.RealTime);
        Assert.Equal(a, new TimerFormValues("Bread", 0, 12, 30, TimerMode.RealTime));
        Assert.NotEqual(a, a with { Seconds = 35 });
    }
}
