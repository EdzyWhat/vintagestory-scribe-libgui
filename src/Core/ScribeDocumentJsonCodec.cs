using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scribe.Core;

/// <summary>
/// A human-readable, versioned <c>ScribeDocument ⇄ JSON</c> codec — the LOSSLESS clipboard export/import
/// lane for the Scriptorium. Parallel to the binary <see cref="ScribeDocumentCodec"/> (persistence/sync).
/// Uses <c>System.Text.Json</c> (BCL, no new dependency).
///
/// Shape (indented, camelCase keys):
/// <code>
/// { "v": 1, "title": "My List",
///   "blocks": [
///     { "kind": "task", "text": "Chop wood", "done": false, "depth": 0 },
///     { "kind": "tracker", "done": false, "depth": 0, "targetItemCode": "game:ingot-copper", "targetQuantity": 8 },
///     { "kind": "link", "linkTarget": "page:craftinginfo-knapping", "linkLabel": "Knapping" },
///     { "kind": "link", "linkTarget": "quest:vsquest:quest-freeghost", "linkLabel": "Free the Ghost",
///       "linkDescription": "..." }
///   ] }
/// </code>
///
/// <para>OMITTED: <c>TaskId</c>/<c>DocId</c> (import mints fresh identity), <c>assignment</c> (place-bound,
/// not shareable), and <c>currentQuantity</c> (derived from carried inventory). Null references are omitted.</para>
///
/// <para>Versioning: <c>"v"</c> is a single counter and a new field is a version bump. Any document with
/// <c>v &gt;= <see cref="MinVersion"/></c> is accepted (a newer producer's extra fields are ignored). A
/// payload lacking <c>v</c> is rejected, so a foreign JSON object can't import as an empty document.
/// v2 added the optional <c>extraInfo</c> key. See docs/CODEC-MIGRATION.md.</para>
/// </summary>
public static class ScribeDocumentJsonCodec
{
    /// <summary>Current JSON format version; bump when adding a field.</summary>
    public const int Version = 2;

    /// <summary>Oldest JSON version still accepted; a lower or missing <c>v</c> is rejected.</summary>
    public const int MinVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(ScribeDocument doc)
    {
        var dto = new DocDto
        {
            V = Version,
            Title = doc.Title,
            Blocks = new List<BlockDto>(doc.Blocks.Count),
        };
        foreach (var block in doc.Blocks)
        {
            dto.Blocks.Add(new BlockDto
            {
                Kind = ScribeBlockKindToken.ToToken(block.Kind),
                Text = string.IsNullOrEmpty(block.Text) ? null : block.Text,
                Done = block.Done,
                Depth = block.Depth,
                TargetItemCode = block.TargetItemCode,
                TargetQuantity = block.Kind is ScribeBlockKind.Tracker or ScribeBlockKind.Craft or ScribeBlockKind.QuestObjective
                    ? block.TargetQuantity : null,
                LinkTarget = block.LinkTarget,
                LinkLabel = block.LinkLabel,
                LinkDescription = block.LinkDescription,
                RecipeSignature = string.IsNullOrEmpty(block.RecipeSignature) ? null : block.RecipeSignature,
                ExtraInfo = block.ExtraInfo,
            });
        }
        return JsonSerializer.Serialize(dto, WriteOptions);
    }

    /// <summary>
    /// Deserializes a Scribe JSON export. Returns false (document null) on blank or malformed input or a
    /// payload that is not a Scribe export. Unknown kind tokens degrade to <see cref="ScribeBlockKind.Task"/>,
    /// over-long text is CLIPPED to the shared caps, the block cap is enforced, and every block gets a FRESH
    /// <see cref="ScribeBlock.TaskId"/> so an import never carries a pin. Never throws.
    /// </summary>
    public static bool TryDeserialize(string? json, out ScribeDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json)) return false;

        DocDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DocDto>(json, ReadOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (dto is null) return false;
        // A foreign JSON object lacks "v" (parses to 0); reject it so it can't import as an empty document an Overwrite would use to wipe the target.
        if (dto.V < MinVersion) return false;

        var blocks = new List<ScribeBlock>(dto.Blocks?.Count ?? 0);
        if (dto.Blocks is not null)
        {
            foreach (var b in dto.Blocks)
            {
                if (blocks.Count >= ScribeDocumentCodec.MaxBlocks) break; // hard cap, same as the binary codec
                if (b is null) continue;

                ScribeBlockKindToken.TryParse(b.Kind, out var kind); // unknown/blank → Task (loose import)
                string text = ClipText(b.Text ?? string.Empty, kind);
                int targetQuantity = b.TargetQuantity is int q && q >= 1 ? q : 1; // block setter also clamps ≥ 1

                blocks.Add(new ScribeBlock(
                    kind,
                    text,
                    done: b.Done,
                    depth: b.Depth < 0 ? 0 : b.Depth,
                    assignedToUid: null,          // assignment is never imported (place-bound)
                    taskId: null,                 // fresh id → never pinned
                    targetItemCode: b.TargetItemCode,
                    targetQuantity: targetQuantity,
                    currentQuantity: 0,           // live/derived — recomputed from carried inventory
                    linkTarget: b.LinkTarget,
                    linkLabel: b.LinkLabel,
                    recipeSignature: b.RecipeSignature, // empty for non-Craft; Craft re-generates ingredients from it
                    linkDescription: b.LinkDescription,
                    extraInfo: b.ExtraInfo));
            }
        }

        string title = string.IsNullOrWhiteSpace(dto.Title) ? ScribeDocument.DefaultTitle : dto.Title!;
        if (title.Length > ScribeDocument.MaxTitleLength) title = title[..ScribeDocument.MaxTitleLength];

        var doc = new ScribeDocument { Title = title };
        doc.SetBlocks(blocks);
        document = doc;
        return true;
    }

    /// <summary>Clips over-long text to the same per-kind caps the binary codec enforces.</summary>
    private static string ClipText(string text, ScribeBlockKind kind)
    {
        int cap = kind == ScribeBlockKind.Task
            ? ScribeDocumentCodec.MaxTaskTextLength
            : ScribeDocumentCodec.MaxTextLength;
        return text.Length > cap ? text[..cap] : text;
    }

    private sealed class DocDto
    {
        public int V { get; set; }
        public string? Title { get; set; }
        public List<BlockDto>? Blocks { get; set; }
    }

    private sealed class BlockDto
    {
        public string? Kind { get; set; }
        public string? Text { get; set; }
        public bool Done { get; set; }
        public int Depth { get; set; }
        public string? TargetItemCode { get; set; }
        public int? TargetQuantity { get; set; }
        public string? LinkTarget { get; set; }
        public string? LinkLabel { get; set; }
        public string? LinkDescription { get; set; }
        public string? RecipeSignature { get; set; }
        public string? ExtraInfo { get; set; }
    }
}
