using System.Text.Json;

namespace Scribe.Core.Cuneiform;

/// <summary>
/// A parsed cuneiform glyph set: a character → <see cref="Glyph"/> map built from the combined JSON
/// produced by <c>glyph-forge/tools/build_glyphs_bundle.py</c> (a <c>{ generatedFrom, characterCount,
/// characters: { "A": {…}, … } }</c> object keyed by character). Parsing lives in <c>Scribe.Core</c>
/// (pure BCL, no VS API) so it stays unit-testable; the Mod layer only supplies the raw JSON string it
/// loaded from the mod's asset tree.
///
/// Uses <see cref="System.Text.Json"/> (in-box on .NET, no external package) with hand-written POCO
/// reads rather than source-generated (de)serialization, so there is no trimming/AOT source-gen
/// dependency under the game runtime. Each glyph is normalized through <see cref="Glyph.FromRaw"/>,
/// applying the export-format migration ladder.
/// </summary>
public sealed class GlyphBundle
{
    private readonly IReadOnlyDictionary<char, Glyph> _glyphs;

    /// <summary>The number of distinct characters authored in this bundle.</summary>
    public int CharacterCount => _glyphs.Count;

    /// <summary>The characters present in this bundle (authored set).</summary>
    public IEnumerable<char> Characters => _glyphs.Keys;

    private GlyphBundle(IReadOnlyDictionary<char, Glyph> glyphs) => _glyphs = glyphs;

    /// <summary>Returns the glyph for <paramref name="character"/>, or null if the bundle has no glyph
    /// authored for it (the caller decides how to handle a missing glyph — the layout engine advances a
    /// small gap rather than throwing).</summary>
    public Glyph? Get(char character) => _glyphs.TryGetValue(character, out var g) ? g : null;

    /// <summary>Whether this bundle has a glyph authored for <paramref name="character"/>.</summary>
    public bool Contains(char character) => _glyphs.ContainsKey(character);

    /// <summary>
    /// Parses the combined bundle JSON into a <see cref="GlyphBundle"/>. Reads the top-level
    /// <c>characters</c> object; each entry's key is the character and its value is a glyph object
    /// (<c>gridSize</c>, <c>leftWidth</c>/<c>rightWidth</c> or a legacy width field, paddings, optional
    /// <c>kerning</c>, and an ordered <c>strokes</c> array). Throws <see cref="JsonException"/> on
    /// malformed JSON or a missing <c>characters</c> object.
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
        // A glyph's own "character" field may override the map key (e.g. a punctuation slug key); prefer it.
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
            // A stroke exported before the weight field existed defaults to a small hairline rather
            // than an error; the authored set always carries an explicit weight.
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

    /// <summary>Default stroke weight (grid units) for a stroke loaded without a <c>weight</c> field.</summary>
    private const double DefaultStrokeWeight = 6.0;
}
