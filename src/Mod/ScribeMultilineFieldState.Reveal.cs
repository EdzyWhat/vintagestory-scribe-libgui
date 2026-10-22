using Gui.Widgets.Animations;

namespace Scribe;

internal sealed partial class ScribeMultilineFieldState
{
    /// <summary>
    /// Decides how the committed <see cref="text"/> reveals when stroke-progression is on. A pure append animates
    /// only the new suffix (prior length becomes the revealed baseline); any other change snaps to fully revealed.
    /// </summary>
    private void UpdateReveal()
    {
        if (revealController is null)
        {
            return;
        }

        // Append iff the new text starts with the old text and is longer.
        bool isAppend = text.Length > revealTrackedText.Length
            && text.StartsWith(revealTrackedText, StringComparison.Ordinal);

        if (isAppend)
        {
            // Preserve accumulated elapsed time so mid-flight letters are not re-hidden (elapsed = Value × Duration).
            double elapsedMs = revealActive
                ? revealController.Value * revealController.Duration.TotalMilliseconds
                : 0.0;

            if (!revealActive)
            {
                revealBaselineChars = revealTrackedText.Length;
            }

            double durationMs = Scribe.Core.Cuneiform.CuneiformReveal.TotalDurationMs(
                revealBaselineChars, CumulativeRevealUnits(text), RevealScheduleShared);
            if (durationMs > 0)
            {
                revealActive = true;
                revealController.Duration = TimeSpan.FromMilliseconds(durationMs);
                // Resume from the same absolute elapsed against the NEW duration so prior strokes stay pressed.
                revealController.Forward(from: Math.Clamp(elapsedMs / durationMs, 0.0, 1.0));
            }
        }
        else
        {
            revealActive = false;
            revealController.Stop();
        }

        revealTrackedText = text;
    }

    /// <summary>Prefix-sum of per-character stroke-units, sizing the reveal duration to the renderer's per-stroke
    /// timing. Falls back to one unit per character when the bundle isn't loaded.</summary>
    private int[] CumulativeRevealUnits(string forText)
    {
        if (Widget.CuneiformBundle is not { } bundle)
        {
            var flat = new int[forText.Length];
            Array.Fill(flat, 1);
            return Scribe.Core.Cuneiform.CuneiformReveal.CumulativeStrokeUnits(flat);
        }

        var layout = new Scribe.Core.Cuneiform.CuneiformLineLayout(bundle);
        return Scribe.Core.Cuneiform.CuneiformReveal.CumulativeStrokeUnits(layout.StrokeUnitsFor(forText));
    }

    private void OnRevealTick(double _) => MarkNeedsBuild();

    private void OnRevealStatus(AnimationStatus status)
    {
        if (status == AnimationStatus.Completed)
        {
            revealActive = false;
            MarkNeedsBuild();
        }
    }
}
