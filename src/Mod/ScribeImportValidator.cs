using System.Collections.Generic;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Client-side check of an imported document against the live registries (Core's codecs only carry the parsed
/// reference strings). An item-bound block whose reference is blank, malformed, or unknown degrades to a plain
/// Task carrying the row's text; the import never aborts. The degrade count is returned for reporting.
///
/// <para>Guide-page (<c>"page:"</c>) and quest (<c>"quest:"</c>) Links are left typed without a resolve:
/// proving a Handbook page exists is expensive, a dangling link is a no-op on click
/// (<see cref="ScribeItemRef.OpenHandbookPage"/>), and quest links render from their own captured label.</para>
/// </summary>
internal static class ScribeImportValidator
{
    /// <summary>The (possibly rewritten) document and how many item-bound blocks were degraded.</summary>
    public readonly record struct Result(ScribeDocument Document, int Degraded);

    /// <summary>Degrade unresolved item-bound blocks in <paramref name="doc"/> to plain Tasks in place
    /// (via <see cref="ScribeDocument.ReplaceBlocks"/>). A null document yields an empty one.</summary>
    public static Result Validate(IWorldAccessor world, ScribeDocument? doc)
    {
        if (doc is null) return new Result(new ScribeDocument(), 0);

        int degraded = 0;
        var rebuilt = new List<ScribeBlock>(doc.Blocks.Count);
        foreach (var block in doc.Blocks)
        {
            if (ShouldDegrade(world, block))
            {
                rebuilt.Add(Degrade(block));
                degraded++;
            }
            else
            {
                rebuilt.Add(block); // already correct — item resolved, or a non-item kind
            }
        }

        if (degraded > 0) doc.ReplaceBlocks(rebuilt);
        return new Result(doc, degraded);
    }

    /// <summary>True when a Tracker's <see cref="ScribeBlock.TargetItemCode"/> or an item Link's
    /// <see cref="ScribeBlock.LinkTarget"/> does not resolve in this game. Other kinds never degrade.</summary>
    private static bool ShouldDegrade(IWorldAccessor world, ScribeBlock block)
    {
        if (block.Kind == ScribeBlockKind.Tracker)
            return ScribeItemRef.ResolveStack(world, block.TargetItemCode) is null;

        if (block.Kind == ScribeBlockKind.Link)
        {
            if (ScribeLinkTarget.IsGuidePage(block.LinkTarget)) return false; // guide pages stay typed (see class doc)
            if (ScribeLinkTarget.IsQuest(block.LinkTarget)) return false; // quest links stay typed too (no item to resolve)
            return ScribeItemRef.ResolveStack(world, block.LinkTarget) is null;
        }

        return false; // Task / Text carry no game reference
    }

    /// <summary>Rewrite as a plain Task keeping done state and depth; text falls back to the raw reference
    /// code when the row's text is blank.</summary>
    private static ScribeBlock Degrade(ScribeBlock block)
    {
        string text = !string.IsNullOrEmpty(block.Text)
            ? block.Text
            : (block.Kind == ScribeBlockKind.Tracker ? block.TargetItemCode : block.LinkTarget) ?? "";
        return new ScribeBlock(ScribeBlockKind.Task, text, done: block.Done, depth: block.Depth);
    }
}
