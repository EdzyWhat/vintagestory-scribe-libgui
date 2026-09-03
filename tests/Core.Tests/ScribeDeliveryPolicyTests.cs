using Scribe.Core;

namespace Scribe.Core.Tests;

public class ScribeDeliveryPolicyTests
{
    private static readonly ScribeWorldPosition Desk = new(0, 0, 0);

    // ---- IsInRange ----

    [Fact]
    public void IsInRange_TrueWhenWellInsideRadius()
    {
        var target = new ScribeWorldPosition(10, 0, 0);
        Assert.True(ScribeDeliveryPolicy.IsInRange(Desk, target, radiusBlocks: 200));
    }

    [Fact]
    public void IsInRange_FalseWhenWellOutsideRadius()
    {
        var target = new ScribeWorldPosition(500, 0, 0);
        Assert.False(ScribeDeliveryPolicy.IsInRange(Desk, target, radiusBlocks: 200));
    }

    [Fact]
    public void IsInRange_ExactlyAtRadius_CountsAsInRange()
    {
        var target = new ScribeWorldPosition(200, 0, 0);
        Assert.True(ScribeDeliveryPolicy.IsInRange(Desk, target, radiusBlocks: 200));
    }

    [Fact]
    public void IsInRange_JustBeyondRadius_CountsAsOutOfRange()
    {
        var target = new ScribeWorldPosition(200.01, 0, 0);
        Assert.False(ScribeDeliveryPolicy.IsInRange(Desk, target, radiusBlocks: 200));
    }

    [Fact]
    public void IsInRange_UsesTrueEuclideanDistance_NotAxisAligned()
    {
        // 150-150-150 is well within a 200 radius on each axis alone but its Euclidean distance
        // (~259.8) exceeds it — proving the check is a true 3D sphere, not a per-axis box check.
        var target = new ScribeWorldPosition(150, 150, 150);
        Assert.False(ScribeDeliveryPolicy.IsInRange(Desk, target, radiusBlocks: 200));
    }

    // ---- ClampRadius ----

    [Theory]
    [InlineData(0, ScribeDeliveryPolicy.MinRadiusBlocks)]
    [InlineData(-50, ScribeDeliveryPolicy.MinRadiusBlocks)]
    [InlineData(200, 200)]
    [InlineData(1_000_000, ScribeDeliveryPolicy.MaxRadiusBlocks)]
    public void ClampRadius_ClampsToRange(int input, int expected)
        => Assert.Equal(expected, ScribeDeliveryPolicy.ClampRadius(input));

    // ---- ResolveDefault (DeliveryMode gating logic) ----

    [Fact]
    public void ResolveDefault_AlwaysInstant_AlwaysLocalInboxes_RegardlessOfRange()
    {
        Assert.Equal(ScribeDeliveryChoice.LocalInboxes,
            ScribeDeliveryPolicy.ResolveDefault(ScribeDeliveryMode.AlwaysInstant, targetInRange: true));
        Assert.Equal(ScribeDeliveryChoice.LocalInboxes,
            ScribeDeliveryPolicy.ResolveDefault(ScribeDeliveryMode.AlwaysInstant, targetInRange: false));
    }

    [Fact]
    public void ResolveDefault_AlwaysPhysical_AlwaysSendNotice_RegardlessOfRange()
    {
        Assert.Equal(ScribeDeliveryChoice.SendNotice,
            ScribeDeliveryPolicy.ResolveDefault(ScribeDeliveryMode.AlwaysPhysical, targetInRange: true));
        Assert.Equal(ScribeDeliveryChoice.SendNotice,
            ScribeDeliveryPolicy.ResolveDefault(ScribeDeliveryMode.AlwaysPhysical, targetInRange: false));
    }

    [Fact]
    public void ResolveDefault_Hybrid_InRange_DefaultsLocalInboxes()
        => Assert.Equal(ScribeDeliveryChoice.LocalInboxes,
            ScribeDeliveryPolicy.ResolveDefault(ScribeDeliveryMode.Hybrid, targetInRange: true));

    [Fact]
    public void ResolveDefault_Hybrid_OutOfRange_DefaultsSendNotice()
        => Assert.Equal(ScribeDeliveryChoice.SendNotice,
            ScribeDeliveryPolicy.ResolveDefault(ScribeDeliveryMode.Hybrid, targetInRange: false));

    // ---- ShowsToggle ----

    [Theory]
    [InlineData(ScribeDeliveryMode.AlwaysInstant, false)]
    [InlineData(ScribeDeliveryMode.AlwaysPhysical, false)]
    [InlineData(ScribeDeliveryMode.Hybrid, true)]
    public void ShowsToggle_OnlyHybrid(ScribeDeliveryMode mode, bool expected)
        => Assert.Equal(expected, ScribeDeliveryPolicy.ShowsToggle(mode));

    // ---- RequiresNotice ----

    [Fact]
    public void RequiresNotice_AlwaysPhysical_TrueRegardlessOfChoice()
    {
        Assert.True(ScribeDeliveryPolicy.RequiresNotice(ScribeDeliveryMode.AlwaysPhysical, ScribeDeliveryChoice.LocalInboxes));
        Assert.True(ScribeDeliveryPolicy.RequiresNotice(ScribeDeliveryMode.AlwaysPhysical, ScribeDeliveryChoice.SendNotice));
    }

    [Fact]
    public void RequiresNotice_AlwaysInstant_FalseRegardlessOfChoice()
    {
        Assert.False(ScribeDeliveryPolicy.RequiresNotice(ScribeDeliveryMode.AlwaysInstant, ScribeDeliveryChoice.LocalInboxes));
        Assert.False(ScribeDeliveryPolicy.RequiresNotice(ScribeDeliveryMode.AlwaysInstant, ScribeDeliveryChoice.SendNotice));
    }

    [Theory]
    [InlineData(ScribeDeliveryChoice.LocalInboxes, false)]
    [InlineData(ScribeDeliveryChoice.SendNotice, true)]
    public void RequiresNotice_Hybrid_FollowsTheChoice(ScribeDeliveryChoice choice, bool expected)
        => Assert.Equal(expected, ScribeDeliveryPolicy.RequiresNotice(ScribeDeliveryMode.Hybrid, choice));
}
