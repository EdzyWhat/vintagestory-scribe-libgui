using System.Collections.Generic;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Mod-layer best-effort reconstruction for an imported document (add-scriptorium-import-export D5). Core's
/// codecs are VS-API-free, so they produce blocks carrying only the parsed reference STRINGS — they can't tell
/// whether a <c>tracker</c>/<c>link</c> code names a real item in THIS game. This validator runs on the client
/// (which has the live registries) before the import is sent: an item-bound block whose reference resolves to a
/// real collectible stays typed; one whose reference is blank, malformed, or unknown DEGRADES to a plain Task
/// carrying the row's text (never abort — the loose-scratchpad tenet). The number of degradations is returned so
/// the caller can report it ("Imported 12 tasks; 2 unknown items imported as plain tasks").
///
/// <para>Guide-page Links (<c>"page:"</c>-prefixed) are left typed without a resolve: enumerating every Handbook
/// page to prove existence is expensive, and a dangling guide link degrades gracefully anyway — clicking it is a
/// no-op (<see cref="ScribeItemRef.OpenHandbookPage"/>) rather than an error. Only ITEM references are resolved.</para>
/// </summary>
internal static class ScribeImportValidator
{
    /// <summary>The outcome of validating a parsed import: the (possibly rewritten) document and how many
    /// item-bound blocks were degraded to plain tasks.</summary>
    public readonly record struct Result(ScribeDocument Document, int Degraded);

    /// <summary>Validate every item-bound block in <paramref name="doc"/> against the running game, degrading
    /// any unresolved reference to a plain Task in place. Returns the same document (mutated via
    /// <see cref="ScribeDocument.ReplaceBlocks"/>) plus the degrade count. A null document validates to an empty
    /// one with zero degradations.</summary>
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

    /// <summary>True when an item-bound block's reference does not resolve to a real collectible in this game.
    /// A Tracker's <see cref="ScribeBlock.TargetItemCode"/> and an ITEM Link's <see cref="ScribeBlock.LinkTarget"/>
    /// are checked; a guide-page Link and every non-item kind (Task/Text) never degrade.</summary>
    private static bool ShouldDegrade(IWorldAccessor world, ScribeBlock block)
    {
        if (block.Kind == ScribeBlockKind.Tracker)
            return ScribeItemRef.ResolveStack(world, block.TargetItemCode) is null;

        if (block.Kind == ScribeBlockKind.Link)
        {
            if (ScribeLinkTarget.IsGuidePage(block.LinkTarget)) return false; // guide pages stay typed (see class doc)
            return ScribeItemRef.ResolveStack(world, block.LinkTarget) is null;
        }

        return false; // Task / Text carry no game reference
    }

    /// <summary>Rewrite an unresolved item-bound block as a plain Task, preserving its done state and depth and
    /// carrying its best available label as the task text: the row's own text if any, otherwise the raw
    /// reference code (so a hand-authored tracker row with a blank Text still reads as something).</summary>
    private static ScribeBlock Degrade(ScribeBlock block)
    {
        string text = !string.IsNullOrEmpty(block.Text)
            ? block.Text
            : (block.Kind == ScribeBlockKind.Tracker ? block.TargetItemCode : block.LinkTarget) ?? "";
        return new ScribeBlock(ScribeBlockKind.Task, text, done: block.Done, depth: block.Depth);
    }
}
