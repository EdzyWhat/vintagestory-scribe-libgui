using System.Text.Json;

namespace Scribe.Core.Cuneiform;

/// <summary>
/// A parsed cuneiform glyph set (character → <see cref="Glyph"/>) built from the combined JSON produced
/// by <c>glyph-forge/tools/build_glyphs_bundle.py</c>. Uses hand-written <see cref="System.Text.Json"/>
/// reads (no source-gen dependency under the game runtime); each glyph is normalized through
/// <see cref="Glyph.FromRaw"/>.
/// </summary>
public sealed class GlyphBundle
{
    private readonly IReadOnlyDictionary<char, Glyph> _glyphs;

    public int CharacterCount => _glyphs.Count;

    public IEnumerable<char> Characters => _glyphs.Keys;

    private GlyphBundle(IReadOnlyDictionary<char, Glyph> glyphs) => _glyphs = glyphs;

    /// <summary>The glyph for <paramref name="character"/>, or null if none is authored.</summary>
    public Glyph? Get(char character) => _glyphs.TryGetValue(character, out var g) ? g : null;

    public bool Contains(char character) => _glyphs.ContainsKey(character);

    /// <summary>
    /// Parses the bundle JSON (a top-level <c>characters</c> object keyed by character). Throws
    /// <see cref="JsonException"/> on malformed JSON or a missing <c>characters</c> object.
    /// </summary>
    public static GlyphBundle Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("characters", out JsonElement characters)
            || characters.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Cuneiform glyph bundle is missing a 'characters' object.");
        }

        var glyphs = new Dictionary<char, Glyph>();
        foreach (JsonProperty entry in characters.EnumerateObject())
        {
            if (entry.Name.Length == 0) continue; // defensive: skip an empty key
            char character = entry.Name[0];
            glyphs[character] = ParseGlyph(character, entry.Value);
        }

        return new GlyphBundle(glyphs);
    }

    private static Glyph ParseGlyph(char character, JsonElement el)
    {
        // A glyph's own "character" field overrides the map key (e.g. a punctuation slug key).
        if (el.TryGetProperty("character", out JsonElement chEl)
            && chEl.ValueKind == JsonValueKind.String)
        {
            string s = chEl.GetString() ?? string.Empty;
            if (s.Length > 0) character = s[0];
        }

        double gridSize = ReadDouble(el, "gridSize") ?? 100.0;
        double? leftWidth = ReadDouble(el, "leftWidth");
        double? rightWidth = ReadDouble(el, "rightWidth");
        double? width = ReadDouble(el, "width");
        double? advanceWidth = ReadDouble(el, "advanceWidth");
        double? leftPadding = ReadDouble(el, "leftPadding");
        double? rightPadding = ReadDouble(el, "rightPadding");

        var strokes = ParseStrokes(el);
        var kerning = ParseKerning(el);

        return Glyph.FromRaw(
            character, gridSize, leftWidth, rightWidth, width, advanceWidth,
            leftPadding, rightPadding, strokes, kerning);
    }

    private static IReadOnlyList<GlyphStroke> ParseStrokes(JsonElement el)
    {
        if (!el.TryGetProperty("strokes", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GlyphStroke>();
        }

        var strokes = new List<GlyphStroke>(arr.GetArrayLength());
        foreach (JsonElement s in arr.EnumerateArray())
        {
            Vec2 start = ReadPoint(s, "start");
            Vec2 end = ReadPoint(s, "end");
            double weight = ReadDouble(s, "weight") ?? DefaultStrokeWeight;
            strokes.Add(new GlyphStroke(start, end, weight));
        }

        return strokes;
    }

    private static IReadOnlyDictionary<char, double>? ParseKerning(JsonElement el)
    {
        if (!el.TryGetProperty("kerning", out JsonElement k) || k.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var map = new Dictionary<char, double>();
        foreach (JsonProperty pair in k.EnumerateObject())
        {
            if (pair.Name.Length == 0) continue;
            if (pair.Value.ValueKind == JsonValueKind.Number)
            {
                map[pair.Name[0]] = pair.Value.GetDouble();
            }
        }

        return map.Count > 0 ? map : null;
    }

    private static Vec2 ReadPoint(JsonElement parent, string name)
    {
        if (parent.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.Object)
        {
            double x = ReadDouble(p, "x") ?? 0.0;
            double y = ReadDouble(p, "y") ?? 0.0;
            return new Vec2(x, y);
        }

        return new Vec2(0, 0);
    }

    private static double? ReadDouble(JsonElement parent, string name)
    {
        if (parent.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number)
        {
            return v.GetDouble();
        }

        return null;
    }

    /// <summary>Stroke weight (grid units) for a stroke without a <c>weight</c> field.</summary>
    private const double DefaultStrokeWeight = 6.0;
}
