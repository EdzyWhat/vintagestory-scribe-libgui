using System.Text;

namespace Scribe.Core;

/// <summary>
/// A fixed-column <c>ScribeDocument ⇄ TSV</c> codec — the spreadsheet-native clipboard lane for the
/// Scriptorium. Tab-separated because Excel and Google Sheets parse it straight into columns on paste.
///
/// <para><b>Fixed columns, forever:</b> <c>Type · Done · Text · Special · Count · Depth</c>. Per-kind richness
/// lives INSIDE the <c>Special</c> cell as a comma-separated payload, never in new columns, so old and new
/// exports stay mutually loadable.</para>
/// <list type="bullet">
/// <item><b>Type</b> — the kind token (see <see cref="ScribeBlockKindToken"/>). <c>title</c> is a reserved ROW
/// type, not a block: it carries the document title (in Text) and produces no block.</item>
/// <item><b>Done</b> — <c>x</c> / blank. Ignored for note/title.</item>
/// <item><b>Text</b> — the human-readable label (task text, a guide-link's title, or the title row's title).</item>
/// <item><b>Special</b> — the machine reference: a tracker's item code, a link's target, or a craft's
/// <c>outputCode[,recipeSignature]</c>.</item>
/// <item><b>Count</b> — a tracker's or craft's target quantity.</item>
/// <item><b>Depth</b> — integer nesting (no parent links).</item>
/// </list>
///
/// <para>Row position is the sequence. Import is loose (degrade, never reject): an unknown Type becomes a
/// Task, a malformed row is skipped, extra columns are ignored, missing columns default. The header is
/// matched by NAME (case-insensitive), so column order is tolerated.</para>
///
/// <para>Pure Core string logic; the Mod layer resolves item/link references. Every block gets a fresh
/// <see cref="ScribeBlock.TaskId"/>, so an import never carries a pin. See docs/CODEC-MIGRATION.md.</para>
/// </summary>
public static class ScribeDocumentTsvCodec
{
    private const char Delimiter = '\t';
    private const string LineEnding = "\n";

    // Import matches by name, so this order is for humans.
    private const string ColType = "Type";
    private const string ColDone = "Done";
    private const string ColText = "Text";
    private const string ColSpecial = "Special";
    private const string ColCount = "Count";
    private const string ColDepth = "Depth";

    public static string Serialize(ScribeDocument doc)
    {
        var sb = new StringBuilder();
        AppendRow(sb, ColType, ColDone, ColText, ColSpecial, ColCount, ColDepth);

        AppendRow(sb, ScribeBlockKindToken.TitleRow, "", doc.Title, "", "", "");

        foreach (var block in doc.Blocks)
        {
            string type = ScribeBlockKindToken.ToToken(block.Kind);
            string done = block.IsCompletable && block.Done ? "x" : "";
            string text = TextFor(block);
            string special = block.Kind switch
            {
                ScribeBlockKind.Tracker => block.TargetItemCode ?? "",
                ScribeBlockKind.Link => block.LinkTarget ?? "",
                ScribeBlockKind.Craft => CraftSpecial(block),
                // The objective's own match code (LinkTarget) is JSON-only and does not round-trip here.
                ScribeBlockKind.QuestObjective => block.TargetItemCode ?? "",
                _ => "",
            };
            string count = block.Kind is ScribeBlockKind.Tracker or ScribeBlockKind.Craft or ScribeBlockKind.QuestObjective
                ? block.TargetQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "";
            string depth = block.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture);

            AppendRow(sb, type, done, text, special, count, depth);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Deserializes a TSV table. Returns false (document null) only when there is no <c>Type</c> header;
    /// otherwise it always produces a document, degrading bad rows. A <c>title</c> row sets the title (absent,
    /// it defaults and the Mod apply layer leaves the target's title unchanged). Never throws.
    /// </summary>
    public static bool TryDeserialize(string? tsv, out ScribeDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(tsv)) return false;

        var records = ParseRecords(tsv);
        if (records.Count == 0) return false;

        // Header → column index map, by name. A Type column is required.
        var header = records[0];
        int idxType = HeaderIndex(header, ColType);
        if (idxType < 0) return false;
        int idxDone = HeaderIndex(header, ColDone);
        int idxText = HeaderIndex(header, ColText);
        int idxSpecial = HeaderIndex(header, ColSpecial);
        int idxCount = HeaderIndex(header, ColCount);
        int idxDepth = HeaderIndex(header, ColDepth);

        var blocks = new List<ScribeBlock>();
        string? title = null;

        for (int r = 1; r < records.Count; r++)
        {
            var row = records[r];
            string typeToken = Cell(row, idxType);
            if (string.IsNullOrWhiteSpace(typeToken) && AllBlank(row)) continue; // skip a wholly blank row

            if (ScribeBlockKindToken.IsTitleRow(typeToken))
            {
                title = Cell(row, idxText); // first title row wins; a later one is ignored (loose)
                title ??= "";
                continue;
            }

            if (blocks.Count >= ScribeDocumentCodec.MaxBlocks) break; // hard cap, same as the binary codec

            ScribeBlockKindToken.TryParse(typeToken, out var kind); // unknown/blank → Task (loose import)
            bool done = ParseDone(Cell(row, idxDone));
            string text = ClipText(Cell(row, idxText), kind);
            string special = Cell(row, idxSpecial);
            int count = ParseCount(Cell(row, idxCount));
            int depth = ParseDepth(Cell(row, idxDepth));

            blocks.Add(BuildBlock(kind, text, done, depth, special, count));
        }

        var doc = new ScribeDocument();
        if (!string.IsNullOrWhiteSpace(title))
        {
            string t = title!;
            if (t.Length > ScribeDocument.MaxTitleLength) t = t[..ScribeDocument.MaxTitleLength];
            doc.Title = t;
        }
        doc.SetBlocks(blocks);
        document = doc;
        return true;
    }

    /// <summary>The Text-column value for a block. A guide-page or quest Link keeps its title in
    /// <see cref="ScribeBlock.LinkLabel"/>, so surface that. A quest Link's <see cref="ScribeBlock.LinkDescription"/>
    /// has no column and does not round-trip through TSV (the JSON codec carries it).</summary>
    private static string TextFor(ScribeBlock block)
    {
        if (block.Kind == ScribeBlockKind.Link
            && (ScribeLinkTarget.IsGuidePage(block.LinkTarget) || ScribeLinkTarget.IsQuest(block.LinkTarget))
            && !string.IsNullOrEmpty(block.LinkLabel))
            return block.LinkLabel!;
        // A label-only QuestObjective (no resolvable item) keeps its display text in LinkLabel.
        if (block.Kind == ScribeBlockKind.QuestObjective
            && string.IsNullOrEmpty(block.TargetItemCode) && !string.IsNullOrEmpty(block.LinkLabel))
            return block.LinkLabel!;
        return block.Text;
    }

    private static ScribeBlock BuildBlock(ScribeBlockKind kind, string text, bool done, int depth, string special, int count)
    {
        switch (kind)
        {
            case ScribeBlockKind.Tracker:
                return new ScribeBlock(kind, text, done: done, depth: depth,
                    targetItemCode: NullIfBlank(special), targetQuantity: count);

            case ScribeBlockKind.Link:
                string? target = NullIfBlank(special);
                // A guide-page or quest target has no item to name it, so Text IS its display label.
                string? label = (ScribeLinkTarget.IsGuidePage(target) || ScribeLinkTarget.IsQuest(target))
                    && !string.IsNullOrEmpty(text) ? text : null;
                return new ScribeBlock(kind, text, done: done, depth: depth,
                    linkTarget: target, linkLabel: label);

            case ScribeBlockKind.Craft:
                // Special = "outputCode[,recipeSignature]"; split on the FIRST comma.
                SplitCraftSpecial(special, out string? craftCode, out string craftSignature);
                return new ScribeBlock(kind, text, done: done, depth: depth,
                    targetItemCode: craftCode, targetQuantity: count, recipeSignature: craftSignature);

            case ScribeBlockKind.QuestObjective:
                // Tracker-shaped: an item code in Special resolves an icon; otherwise Text is the fallback label.
                string? questItemCode = NullIfBlank(special);
                string? questLabel = questItemCode is null ? NullIfBlank(text) : null;
                return new ScribeBlock(kind, "", done: done, depth: depth,
                    targetItemCode: questItemCode, targetQuantity: count, linkLabel: questLabel);

            default: // Task, Text, and any future/unknown kind degraded to Task
                return new ScribeBlock(kind, text, done: done, depth: depth);
        }
    }

    /// <summary>The Special-cell payload for a Craft: the output item code, optionally followed by <c>","</c>
    /// and the recipe signature. Both are comma-free by construction, so the first comma separates them.</summary>
    private static string CraftSpecial(ScribeBlock block)
    {
        string code = block.TargetItemCode ?? "";
        return string.IsNullOrEmpty(block.RecipeSignature) ? code : code + "," + block.RecipeSignature;
    }

    /// <summary>Inverse of <see cref="CraftSpecial"/>: splits on the FIRST comma into the code (null when blank) and signature (empty when absent).</summary>
    private static void SplitCraftSpecial(string special, out string? code, out string signature)
    {
        int comma = special.IndexOf(',');
        if (comma < 0)
        {
            code = NullIfBlank(special);
            signature = "";
            return;
        }
        code = NullIfBlank(special.Substring(0, comma));
        signature = special.Substring(comma + 1);
    }


    private static int HeaderIndex(List<string> header, string name)
    {
        for (int i = 0; i < header.Count; i++)
            if (ScribeTsvSafe.Unescape(header[i]).Trim().Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static string Cell(List<string> row, int index)
        => index >= 0 && index < row.Count ? ScribeTsvSafe.Unescape(row[index]) : "";

    private static bool AllBlank(List<string> row)
    {
        foreach (var c in row)
            if (!string.IsNullOrWhiteSpace(ScribeTsvSafe.Unescape(c))) return false;
        return true;
    }

    private static bool ParseDone(string cell)
    {
        string s = cell.Trim().ToLowerInvariant();
        return s == "x" || s == "1" || s == "true" || s == "yes" || s == "done";
    }

    private static int ParseCount(string cell)
    {
        return int.TryParse(cell.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 1
            ? n : 1; // block setter also clamps ≥ 1
    }

    private static int ParseDepth(string cell)
    {
        return int.TryParse(cell.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 0
            ? n : 0;
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string ClipText(string text, ScribeBlockKind kind)
    {
        int cap = kind == ScribeBlockKind.Task
            ? ScribeDocumentCodec.MaxTaskTextLength
            : ScribeDocumentCodec.MaxTextLength;
        return text.Length > cap ? text[..cap] : text;
    }

    private static void AppendRow(StringBuilder sb, params string[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0) sb.Append(Delimiter);
            sb.Append(ScribeTsvSafe.Escape(cells[i]));
        }
        sb.Append(LineEnding);
    }

    /// <summary>
    /// Splits TSV text into records of RAW (still-escaped) fields, honoring RFC-4180-style quoting so a quoted
    /// field may hold tabs and newlines. Every character (including quotes) is preserved verbatim; the
    /// <see cref="ScribeTsvSafe.Unescape"/> call is the single place that unquotes. CRLF is normalized to LF
    /// and a trailing newline does not produce an empty record.
    /// </summary>
    private static List<List<string>> ParseRecords(string text)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        bool fieldStarted = false;
        bool rowHasContent = false;

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append("\"\""); i += 2; continue; } // literal doubled quote, kept verbatim
                    sb.Append('"'); inQuotes = false; i++; continue; // closing quote, kept verbatim
                }
                sb.Append(c); i++; continue; // literal tab/newline/char inside the quoted field
            }

            switch (c)
            {
                case '"' when !fieldStarted:
                    sb.Append('"'); inQuotes = true; fieldStarted = true; rowHasContent = true; i++;
                    break;
                case Delimiter:
                    fields.Add(sb.ToString()); sb.Clear();
                    fieldStarted = false; rowHasContent = true; i++;
                    break;
                case '\r':
                    i++; // normalize away
                    break;
                case '\n':
                    fields.Add(sb.ToString()); sb.Clear();
                    records.Add(fields); fields = new List<string>();
                    fieldStarted = false; rowHasContent = false; i++;
                    break;
                default:
                    sb.Append(c); fieldStarted = true; rowHasContent = true; i++;
                    break;
            }
        }

        // Finalize a trailing record only if the last row had content.
        fields.Add(sb.ToString());
        if (rowHasContent || fields.Count > 1 || (fields.Count == 1 && fields[0].Length > 0))
            records.Add(fields);

        return records;
    }
}
