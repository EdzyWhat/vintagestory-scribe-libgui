using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the author-drawn, non-linear brightness response curve (respect-local-illumination). Pin the
// exact control points the user plotted, the piecewise-linear interpolation between them, the floor
// semantics (x=0 anchor + monotonic lift), and the clamping of out-of-range inputs. Pure/game-agnostic.
public class ScribeBrightnessCurveTests
{
    private const float Floor = ScribePlayerSettings.DefaultIlluminationFloor; // 0.05 (the shipped floor)

    [Fact]
    public void AtZeroLight_ReturnsFloor()
    {
        Assert.Equal(Floor, ScribeBrightnessCurve.Evaluate(0f, Floor), 4);
    }

    [Theory]
    // The plotted control points map exactly (input local brightness → GUI brightness). The high knee is
    // 0.90 → 1.00 (full GUI brightness before noon); 1.0 is a flat tail.
    [InlineData(0.45f, 0.53f)]
    [InlineData(0.90f, 1.00f)]
    [InlineData(1.00f, 1.00f)]
    public void PlottedControlPoints_MapExactly(float input, float expected)
    {
        Assert.Equal(expected, ScribeBrightnessCurve.Evaluate(input, Floor), 4);
    }

    [Fact]
    public void BrightKnee_MapsToFull()
    {
        // Full GUI brightness is reached at input 0.90 (a bright-but-not-noon source), not only at noon.
        Assert.Equal(1.0f, ScribeBrightnessCurve.Evaluate(0.90f, Floor), 4);
    }

    [Fact]
    public void MidRamp_InterpolatesBetweenFirstPointAndFull()
    {
        // Between the 0.53 mid point (x=0.45) and full (x=0.90) the curve ramps linearly — a value strictly
        // between the two, not snapped to either. e.g. x=0.675 (halfway) → ~0.765.
        float y = ScribeBrightnessCurve.Evaluate(0.675f, Floor);
        Assert.True(y > 0.53f && y < 1.0f, $"expected a mid-ramp value in (0.53, 1.0), got {y}");
    }

    [Fact]
    public void BelowFirstPoint_InterpolatesFromFloor()
    {
        // Halfway (x=0.225) between the floor anchor (0, 0.05) and the first point (0.45, 0.53).
        float expected = Floor + (0.53f - Floor) * 0.5f;
        Assert.Equal(expected, ScribeBrightnessCurve.Evaluate(0.225f, Floor), 4);
    }

    [Fact]
    public void MidRange_OutputExceedsInput()
    {
        // The whole point of the custom curve: for a given amount of local light the GUI is a touch brighter
        // than a straight linear mapping — at input 0.45 the output (0.53) exceeds it. (On the author's
        // transposed axes this is the pencil curve below-right of the red identity line, never crossing it.)
        Assert.True(ScribeBrightnessCurve.Evaluate(0.45f, Floor) > 0.45f);
    }

    [Fact]
    public void ReachesFullOnlyAtTop()
    {
        // The curve reaches full brightness at x=0.90 and holds it through noon; just below the knee it is
        // still under full (the mid ramp, not an early plateau).
        Assert.Equal(1.0f, ScribeBrightnessCurve.Evaluate(0.90f, Floor), 4);
        Assert.Equal(1.0f, ScribeBrightnessCurve.Evaluate(1.0f, Floor), 4);
        Assert.True(ScribeBrightnessCurve.Evaluate(0.80f, Floor) < 1.0f);
    }

    [Fact]
    public void IsMonotonicNonDecreasing_AtDefaultFloor()
    {
        float prev = -1f;
        for (int i = 0; i <= 100; i++)
        {
            float y = ScribeBrightnessCurve.Evaluate(i / 100f, Floor);
            Assert.True(y >= prev - 1e-4f, $"curve dipped at x={i / 100f}: {y} < {prev}");
            prev = y;
        }
    }

    [Fact]
    public void RaisedFloor_LiftsDarkEnd_StaysMonotonic()
    {
        // A floor above an interior point lifts that point rather than bending the curve down.
        const float highFloor = 0.6f;
        Assert.Equal(highFloor, ScribeBrightnessCurve.Evaluate(0f, highFloor), 4);
        Assert.True(ScribeBrightnessCurve.Evaluate(0.45f, highFloor) >= highFloor - 1e-4f);

        float prev = -1f;
        for (int i = 0; i <= 100; i++)
        {
            float y = ScribeBrightnessCurve.Evaluate(i / 100f, highFloor);
            Assert.True(y >= prev - 1e-4f, $"curve dipped at x={i / 100f} with high floor");
            prev = y;
        }
    }

    [Fact]
    public void FloorOfOne_IsAlwaysFullBright()
    {
        // Opt-out: floor 1.0 flattens the curve to full brightness at every input.
        Assert.Equal(1.0f, ScribeBrightnessCurve.Evaluate(0f, 1.0f), 4);
        Assert.Equal(1.0f, ScribeBrightnessCurve.Evaluate(0.5f, 1.0f), 4);
        Assert.Equal(1.0f, ScribeBrightnessCurve.Evaluate(1.0f, 1.0f), 4);
    }

    [Theory]
    // Out-of-range inputs clamp to the 0..1 endpoints rather than extrapolating.
    [InlineData(-0.5f)]
    [InlineData(0f)]
    public void NegativeOrZeroInput_ClampsToFloor(float input)
    {
        Assert.Equal(Floor, ScribeBrightnessCurve.Evaluate(input, Floor), 4);
    }

    [Fact]
    public void AboveOneInput_ClampsToFull()
    {
        Assert.Equal(1.0f, ScribeBrightnessCurve.Evaluate(1.5f, Floor), 4);
    }
}
