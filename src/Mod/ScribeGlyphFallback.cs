// Per-glyph font fallback for the Unicode arrows in the editable field
// (add-arrow-substitution-and-cuneiform-glyphs).
//
// The typed-arrow substitution stores real U+2192 (→) / U+2190 (←) in the buffer. Several of the
// bundled task fonts are SUBSET builds that carry no arrow glyph — verified: Noto Sans, Noto Serif and
// La Belle Aurore lack ←/→ (Scapholene and Caudex have them); the "Default" ("" → system) family depends
// on the OS. In the READ view this is already handled: LibGUI's Text widget draws through
// CanvasDrawExtensions.DrawText, which shapes via TextShaper/FontRunSplitter and substitutes a fallback
// typeface per missing glyph. But the EDITOR field paints through PaintingContext.DrawText, which calls
// the raw SkiaSharp Canvas.DrawText with a single SKFont and does NO fallback — so a missing arrow renders
// as tofu (□). This helper closes that gap for the editor: when the active font can't render an arrow, it
// draws just that run in "Cormorant Unicase" (a font the hard `gui` dependency always ships, and which
// carries both arrows — so the fallback is deterministic, not reliant on whatever the OS happens to have).
//
// Only the two arrow code points are ever redirected, and only when the active font actually lacks them,
// so any font that DOES carry arrows keeps drawing them natively (matching the read view). When a line
// needs no redirect — the overwhelmingly common case — Draw/Measure take a single-run fast path that is
// byte-for-byte identical to calling PaintingContext.DrawText / TextLayoutHelper.MeasureText directly.

using System.Collections.Generic;
using System.Text;
using Gui.Rendering;              // PaintingContext
using Gui.Rendering.Text;         // TextLayoutHelper, FontWeight, FontRegistry
using OpenTK.Mathematics;         // Vector2, Vector4
using SkiaSharp;                  // SKTypeface (glyph-coverage probe)

namespace Scribe;

internal static class ScribeGlyphFallback
{
    /// <summary>Family used to draw an arrow the active font can't render. Provided by the `gui` mod
    /// (LibGUI bundles CormorantUnicase-*.ttf and registers it under this family), so it is always present
    /// and carries U+2190/U+2192.</summary>
    private const string FallbackFamily = "Cormorant Unicase";

    private static bool IsArrow(char c) => c == Scribe.Core.ScribeArrowDigraph.RightArrow || c == Scribe.Core.ScribeArrowDigraph.LeftArrow;

    /// <summary>Draw one already-wrapped line at <paramref name="pos"/> (baseline Y), redirecting any arrow
    /// the active font lacks to the fallback family. Advances by each run's own measured width so a mixed
    /// line stays contiguous.</summary>
    public static void DrawLine(PaintingContext context, string line, Vector2 pos, float fontSize, Vector4 color, string fontFamily, FontWeight weight)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        // Fast path: nothing to redirect — one draw call, identical to context.DrawText(line, ...).
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

    /// <summary>Measure a string the same way <see cref="DrawLine"/> draws it, so caret/selection math
    /// lines up with the redirected arrows. Matches <c>TextLayoutHelper.MeasureText(...).X</c> exactly when
    /// no redirect is needed.</summary>
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

    /// <summary>Split <paramref name="s"/> into (text, family) runs when — and only when — it contains an
    /// arrow the active font can't render and the fallback font can. Returns false (with an empty
    /// <paramref name="runs"/>) for the common no-redirect case so callers can take a single-draw fast
    /// path.</summary>
    private static bool TrySplitRuns(string s, string fontFamily, FontWeight weight, out List<(string Text, string Family)> runs)
    {
        runs = null!;

        SKTypeface? primary = TextLayoutHelper.GetFont(fontFamily, 1f, weight).Typeface;
        SKTypeface? fallback = FontRegistry.GetCustomTypeface(FallbackFamily, FontWeight.Normal);
        // No usable fallback (family not registered), or the active font already covers arrows — nothing to
        // do; let the caller draw/measure the whole string in one call.
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
