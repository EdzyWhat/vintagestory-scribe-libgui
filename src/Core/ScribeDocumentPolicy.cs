namespace Scribe.Core;

/// <summary>
/// A per-tier cap on how much a <see cref="ScribeDocument"/> may hold, applied at the host/editor
/// mutation boundary rather than inside the document model. <see cref="ScribeDocument"/> backs every
/// writing surface (Lectern, Notebook, tablet) and must stay tier-agnostic and uncapped — putting a
/// limit inside it would leak the tablet tier into the shared model and risk silently limiting the
/// notebook. Instead each host reports the policy for its tier and consults <see cref="CanAdd"/> /
/// <see cref="CanPin"/> before letting the editor grow the document or the player pin a task.
///
/// <para>A <c>null</c> limit means "uncapped": <see cref="Unlimited"/> is the default every existing
/// host reports, so notebook and lectern behavior is unchanged. The tablet tier reports
/// <see cref="Tablet"/> (at most 10 task blocks, 1 pin). This type has no dependency on the Vintage
/// Story API and is unit-tested in <c>Core.Tests</c>.</para>
/// </summary>
public readonly record struct ScribeDocumentPolicy
{
    /// <summary>Maximum number of task blocks the document may hold, or <c>null</c> for uncapped.
    /// Counts TASK blocks specifically (the user's "10 tasks"), not freeform text sections — a future
    /// text-block cap would be a separate limit so the two never get conflated.</summary>
    public int? MaxBlocks { get; init; }

    /// <summary>Maximum number of tasks the player may pin from this document, or <c>null</c> for
    /// uncapped.</summary>
    public int? MaxPins { get; init; }

    /// <summary>When <c>true</c>, the document may not be edited at all: both <see cref="CanAdd"/> and
    /// <see cref="CanPin"/> deny regardless of count. Reported by <see cref="UneditableTablet"/> for a hard
    /// or fired tablet (tablet-firing); the editable tiers leave it false.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The uncapped policy every existing host (Lectern, Notebook, Clockmaker's) reports, so
    /// those tiers stay behaviorally unchanged: no block cap, no pin cap, editable.</summary>
    public static readonly ScribeDocumentPolicy Unlimited = new();

    /// <summary>The scratch-tier tablet cap: at most 10 task blocks and 1 pin. Editable. Applies to a WET
    /// clay/wax tablet only; a hardened or fired tablet reports <see cref="UneditableTablet"/> instead.</summary>
    public static readonly ScribeDocumentPolicy Tablet = new() { MaxBlocks = 10, MaxPins = 1 };

    /// <summary>The read-only preset a non-editable tablet (hardened or fired — tablet-firing) reports:
    /// <see cref="CanAdd"/> and <see cref="CanPin"/> always deny, so no task can be added and no task can be
    /// pinned. The block/pin caps are also pinned to 0 so the preset denies even if <see cref="ReadOnly"/>
    /// were ever cleared. Distinct from <see cref="Tablet"/> so a wet tablet's editable behavior is
    /// untouched.</summary>
    public static readonly ScribeDocumentPolicy UneditableTablet =
        new() { MaxBlocks = 0, MaxPins = 0, ReadOnly = true };

    /// <summary>Whether one more block (of ANY kind) may be added given the document's
    /// <paramref name="currentBlockCount"/>. The cap is "N of anything" (refine-chalkboard §12) — tasks, notes,
    /// trackers, links, and craft parents all count equally. Always <c>true</c> when <see cref="MaxBlocks"/> is
    /// <c>null</c> (uncapped) and always <c>false</c> when <see cref="ReadOnly"/>; otherwise <c>true</c> only
    /// while the count is still below the cap. A negative count is treated as 0 so a garbled caller can't wrap
    /// past the cap.</summary>
    public bool CanAdd(int currentBlockCount)
    {
        if (ReadOnly) return false;
        if (MaxBlocks is not int max) return true;
        return Math.Max(0, currentBlockCount) < max;
    }

    /// <summary>Whether a whole document of <paramref name="blockCount"/> blocks (of any kind) may be HELD by
    /// this tier at once — the Transcribe copy-paste target check. A copy REPLACES the target document, so the
    /// result's block count equals the source's; the target is a valid destination only if it can hold that
    /// many. Always <c>false</c> when <see cref="ReadOnly"/> (a hard/fired tablet can't be written at all,
    /// so it fails this too), always <c>true</c> when <see cref="MaxBlocks"/> is <c>null</c> (uncapped);
    /// otherwise <c>true</c> only when the count fits at-or-under the cap (<c>&lt;=</c>, not <c>&lt;</c>:
    /// exactly filling the tier is a legal destination). A negative count is treated as 0.
    ///
    /// <para>Distinct from <see cref="CanAdd"/> (which asks "is there room for ONE MORE beyond the current
    /// count?", a strict <c>&lt;</c>): <see cref="CanHold"/> asks "does this whole count fit?".</para></summary>
    public bool CanHold(int blockCount)
    {
        if (ReadOnly) return false;
        if (MaxBlocks is not int max) return true;
        return Math.Max(0, blockCount) <= max;
    }

    /// <summary>Whether one more pin may be added given the player's <paramref name="currentPinCount"/> for
    /// this document. Always <c>false</c> when <see cref="ReadOnly"/> (a hard/fired tablet — tablet-firing),
    /// always <c>true</c> when <see cref="MaxPins"/> is <c>null</c> (uncapped); otherwise <c>true</c> only
    /// while the count is still below the cap. A negative count is treated as 0.</summary>
    public bool CanPin(int currentPinCount)
    {
        if (ReadOnly) return false;
        if (MaxPins is not int max) return true;
        return Math.Max(0, currentPinCount) < max;
    }
}
