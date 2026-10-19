namespace Scribe.Core;

/// <summary>
/// Maps an external mod's <c>(title, bodyText, extraInfo)</c> triple (the shape
/// <c>ScribeModSystem.TryCreateExternalTask</c> accepts) onto <see cref="ScribeBlock"/>s. Pure data; the
/// Mod layer appends the result to a target document.
/// </summary>
public static class ScribeExternalTaskMapper
{
    /// <summary>
    /// Builds the block(s) for one external task, or an empty list when both title and body are null/empty.
    /// A title becomes a Depth-0 Task; a body becomes a Depth-1 Text block beneath it, or a standalone
    /// Depth-0 Text block when there is no title. <paramref name="extraInfo"/> attaches to whichever block
    /// ends up at Depth 0. All inputs are clipped (never rejected): title and extraInfo to
    /// <see cref="ScribeDocumentCodec.MaxTaskTextLength"/>, body to <see cref="ScribeDocumentCodec.MaxTextLength"/>.
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
            // No title: the body stands alone at Depth 0 and so carries any extraInfo.
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
