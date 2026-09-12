namespace Scribe.Core;

/// <summary>
/// Maps an external mod's <c>(title, bodyText, extraInfo)</c> triple — the public shape
/// <c>ScribeModSystem.TryCreateExternalTask</c> accepts — onto the <see cref="ScribeBlock"/> shape
/// Scribe actually stores (add-external-mod-task-api). Pure data, no VS API: the Mod layer resolves
/// a target document and appends the returned blocks to it.
/// </summary>
public static class ScribeExternalTaskMapper
{
    /// <summary>
    /// Builds the block(s) for one external task, or an empty list when both <paramref name="title"/>
    /// and <paramref name="bodyText"/> are null/empty (nothing meaningful to create).
    ///
    /// <para><paramref name="title"/>, when present, becomes a Depth-0 checkbox Task. If
    /// <paramref name="bodyText"/> is also present, it becomes a Depth-1 freeform Text block nested
    /// beneath it. When <paramref name="title"/> is absent but <paramref name="bodyText"/> is present,
    /// the body is promoted to a standalone Depth-0 Text block instead of an orphaned subtask.</para>
    ///
    /// <para><paramref name="extraInfo"/>, when non-empty, is attached to whichever block ends up at
    /// Depth 0 (the title Task, or the promoted body Text) — never to a Depth-1 nested note.</para>
    ///
    /// <para>All three inputs are clipped (never rejected) to their existing per-kind caps: title and
    /// extraInfo to <see cref="ScribeDocumentCodec.MaxTaskTextLength"/> (a Task's own cap — extraInfo is
    /// "capped like a task's own text"), body text to <see cref="ScribeDocumentCodec.MaxTextLength"/> (a
    /// Text section's cap).</para>
    /// </summary>
    public static IReadOnlyList<ScribeBlock> MapToBlocks(string? title, string? bodyText, string? extraInfo)
    {
        string? clippedTitle = ClipOrNull(title, ScribeDocumentCodec.MaxTaskTextLength);
        string? clippedBody = ClipOrNull(bodyText, ScribeDocumentCodec.MaxTextLength);
        string? clippedExtraInfo = ClipOrNull(extraInfo, ScribeDocumentCodec.MaxTaskTextLength);

        var blocks = new List<ScribeBlock>();
        if (clippedTitle is not null)
        {
            blocks.Add(new ScribeBlock(ScribeBlockKind.Task, clippedTitle, extraInfo: clippedExtraInfo));
            if (clippedBody is not null)
            {
                blocks.Add(new ScribeBlock(ScribeBlockKind.Text, clippedBody, depth: 1));
            }
        }
        else if (clippedBody is not null)
        {
            // No title: the body stands alone at Depth 0 rather than staying an orphaned subtask,
            // so it — not a nonexistent title — carries any extraInfo.
            blocks.Add(new ScribeBlock(ScribeBlockKind.Text, clippedBody, extraInfo: clippedExtraInfo));
        }
        return blocks;
    }

    private static string? ClipOrNull(string? text, int cap)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length > cap ? text[..cap] : text;
    }
}
