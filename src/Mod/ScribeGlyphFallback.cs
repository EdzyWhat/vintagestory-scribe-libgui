// Per-glyph font fallback for the Unicode arrows in the editable field.
//
// The typed-arrow substitution stores real U+2192 (→) / U+2190 (←) in the buffer, but several bundled task
// fonts are SUBSET builds without arrow glyphs (Noto Sans, Noto Serif, La Belle Aurore; the "Default" system
// family depends on the OS). The read view substitutes a fallback typeface per missing glyph, but the editor
// field paints through PaintingContext.DrawText (raw Canvas.DrawText, one SKFont, NO fallback), so a missing
// arrow renders as tofu. This helper draws just that run in "Cormorant Unicase", which the hard `gui`
// dependency always ships and which carries both arrows.
//
// Only the two arrow code points are redirected, and only when the active font lacks them. A line needing no
// redirect takes a single-run fast path equivalent to calling PaintingContext.DrawText /
// TextLayoutHelper.MeasureText directly.

using System.Collections.Generic;
using System.Text;
using Gui.Rendering;              // PaintingContext
using Gui.Rendering.Text;         // TextLayoutHelper, FontWeight, FontRegistry
using OpenTK.Mathematics;         // Vector2, Vector4
using SkiaSharp;                  // SKTypeface (glyph-coverage probe)

namespace Scribe;

internal static class ScribeGlyphFallback
{
    /// <summary>Family used to draw an arrow the active font can't render. Bundled and registered by the `gui`
    /// mod, so always present and carries U+2190/U+2192.</summary>
    private const string FallbackFamily = "Cormorant Unicase";

    private static bool IsArrow(char c) => c == Scribe.Core.ScribeArrowDigraph.RightArrow || c == Scribe.Core.ScribeArrowDigraph.LeftArrow;

    /// <summary>Draw one already-wrapped line at <paramref name="pos"/> (baseline Y), redirecting arrows the
    /// active font lacks to the fallback family. Each run advances by its own measured width.</summary>
    public static void DrawLine(PaintingContext context, string line, Vector2 pos, float fontSize, Vector4 color, string fontFamily, FontWeight weight)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        // Fast path: nothing to redirect.
        if (!TrySplitRuns(line, fontFamily, weight, out List<(string Text, string Family)> runs))
        {
            context.DrawText(line, pos, fontSize, color, fontFamily, weight);
            return;
        }

        float x = pos.X;
        foreach (var (text, family) in runs)
        {
            context.DrawText(text, new Vector2(x, pos.Y), fontSize, color, family, weight);
            x += TextLayoutHelper.MeasureText(text, family, fontSize, weight).X;
        }
    }

    /// <summary>Measure a string the way <see cref="DrawLine"/> draws it, so caret/selection math lines up with
    /// redirected arrows.</summary>
    public static float MeasureWidth(string s, float fontSize, string fontFamily, FontWeight weight)
    {
        if (string.IsNullOrEmpty(s))
        {
            return 0f;
        }

        if (!TrySplitRuns(s, fontFamily, weight, out List<(string Text, string Family)> runs))
        {
            return TextLayoutHelper.MeasureText(s, fontFamily, fontSize, weight).X;
        }

        float width = 0f;
        foreach (var (text, family) in runs)
        {
            width += TextLayoutHelper.MeasureText(text, family, fontSize, weight).X;
        }
        return width;
    }

    /// <summary>Split <paramref name="s"/> into (text, family) runs only when it has an arrow the active font
    /// can't render and the fallback can. Returns false (empty <paramref name="runs"/>) otherwise, so callers
    /// take the single-draw fast path.</summary>
    private static bool TrySplitRuns(string s, string fontFamily, FontWeight weight, out List<(string Text, string Family)> runs)
    {
        runs = null!;

        SKTypeface? primary = TextLayoutHelper.GetFont(fontFamily, 1f, weight).Typeface;
        SKTypeface? fallback = FontRegistry.GetCustomTypeface(FallbackFamily, FontWeight.Normal);
        // No usable typeface or fallback (family not registered): the caller draws the whole string.
        if (primary == null || fallback == null)
        {
            return false;
        }

        bool anyRedirect = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (IsArrow(s[i]) && primary.GetGlyph(s[i]) == 0 && fallback.GetGlyph(s[i]) != 0)
            {
                anyRedirect = true;
                break;
            }
        }
        if (!anyRedirect)
        {
            return false;
        }

        runs = new List<(string, string)>();
        var current = new StringBuilder();
        string currentFamily = fontFamily;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            string family = (IsArrow(c) && primary.GetGlyph(c) == 0 && fallback.GetGlyph(c) != 0)
                ? FallbackFamily
                : fontFamily;
            if (current.Length > 0 && family != currentFamily)
            {
                runs.Add((current.ToString(), currentFamily));
                current.Clear();
            }
            currentFamily = family;
            current.Append(c);
        }
        if (current.Length > 0)
        {
            runs.Add((current.ToString(), currentFamily));
        }
        return true;
    }
}
