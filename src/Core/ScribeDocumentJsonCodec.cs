using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scribe.Core;

/// <summary>
/// A human-readable, versioned <c>ScribeDocument ⇄ JSON</c> codec — the LOSSLESS clipboard export/import
/// lane for the Scriptorium's Import/Export section (add-scriptorium-import-export). Parallel to the binary
/// <see cref="ScribeDocumentCodec"/> (world persistence / network sync); this one exists purely so a player
/// can copy a document out to text, edit it anywhere, and paste it back.
///
/// <para>Uses <c>System.Text.Json</c>, which is part of the .NET base class library on net10.0 — NOT a
/// NuGet/mod package, so it satisfies the "no new dependencies" guardrail and keeps Core VS-API-free.</para>
///
/// Shape (indented, camelCase keys):
/// <code>
/// { "v": 1, "title": "My List",
///   "blocks": [
///     { "kind": "task", "text": "Chop wood", "done": false, "depth": 0 },
///     { "kind": "tracker", "done": false, "depth": 0, "targetItemCode": "game:ingot-copper", "targetQuantity": 8 },
///     { "kind": "link", "linkTarget": "page:craftinginfo-knapping", "linkLabel": "Knapping" }
///   ] }
/// </code>
///
/// <para>What is written: <c>kind</c> (a stable string token, see <see cref="ScribeBlockKindToken"/>),
/// <c>text</c>, <c>done</c>, <c>depth</c>, and the Tracker/Link references. What is OMITTED and why:
/// <c>TaskId</c>/<c>DocId</c> (import mints fresh identity — see the Mod import handler), <c>assignedToUid</c>
/// (assignment is place-bound, not shareable), and <c>currentQuantity</c> (live/derived from carried
/// inventory, recomputed after import). Null references are omitted for legibility.</para>
///
/// <para>Versioning mirrors the binary codec's forward/backward window: <c>"v"</c> is a single counter and a
/// new field is a version bump, never a breaking reshuffle. <see cref="Version"/>=1 is the initial format;
/// any document with <c>v &gt;= <see cref="MinVersion"/></c> is accepted (a newer producer's extra fields are
/// ignored by the DTO). A payload lacking a <c>v</c> is treated as "not a Scribe export" and rejected, so a
/// foreign JSON object can't import as an empty document. See docs/CODEC-MIGRATION.md.</para>
/// </summary>
public static class ScribeDocumentJsonCodec
{
    /// <summary>Current JSON format version. Bump (never reshuffle) when adding a field. See the class doc.</summary>
    public const int Version = 1;

    /// <summary>Oldest JSON version still accepted. A payload with <c>v</c> below this — or with no <c>v</c>
    /// at all (parsed as 0) — is rejected as not a Scribe export.</summary>
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
                // Only a Tracker carries a meaningful quantity; omit it elsewhere to keep the JSON legible.
                TargetQuantity = block.Kind == ScribeBlockKind.Tracker ? block.TargetQuantity : null,
                LinkTarget = block.LinkTarget,
                LinkLabel = block.LinkLabel,
            });
        }
        return JsonSerializer.Serialize(dto, WriteOptions);
    }

    /// <summary>
    /// Deserializes a Scribe JSON export. Fails safely (returns false, <paramref name="document"/> null) on
    /// null/blank input, malformed JSON, or a payload that is not a Scribe export (no/too-old <c>v</c>).
    /// Defensive by construction: unknown kind tokens degrade to <see cref="ScribeBlockKind.Task"/>, missing
    /// fields default, over-long text is CLIPPED to the shared caps (not rejected), and the block cap is
    /// enforced. Every block gets a FRESH <see cref="ScribeBlock.TaskId"/> (the ctor default), so an import
    /// can never carry a pin. Never throws to the caller.
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
        // A real Scribe export always stamps "v". A foreign JSON object lacks it (parses to 0) — reject so it
        // can't slip through as an empty document (which an Overwrite import would use to wipe the target).
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
                    assignedToUid: null,          // never imported (place-bound)
                    taskId: null,                 // fresh id → never pinned
                    targetItemCode: b.TargetItemCode,
                    targetQuantity: targetQuantity,
                    currentQuantity: 0,           // live/derived — recomputed from carried inventory
                    linkTarget: b.LinkTarget,
                    linkLabel: b.LinkLabel));
            }
        }

        string title = string.IsNullOrWhiteSpace(dto.Title) ? ScribeDocument.DefaultTitle : dto.Title!;
        if (title.Length > ScribeDocument.MaxTitleLength) title = title[..ScribeDocument.MaxTitleLength];

        var doc = new ScribeDocument { Title = title };
        doc.SetBlocks(blocks);
        document = doc;
        return true;
    }

    /// <summary>Clips over-long text to the same per-kind caps the binary codec enforces (a Task to the soft
    /// task cap, everything else to the note cap), so one runaway field can never blow the document bound.</summary>
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
    }
}
