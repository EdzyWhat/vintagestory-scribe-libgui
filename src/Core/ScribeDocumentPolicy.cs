namespace Scribe.Core;

/// <summary>
/// A per-tier cap on how much a <see cref="ScribeDocument"/> may hold, applied at the host/editor mutation
/// boundary so the document model stays tier-agnostic and uncapped. Hosts consult <see cref="CanAdd"/> /
/// <see cref="CanPin"/> before growing the document or pinning. A <c>null</c> limit means uncapped
/// (<see cref="Unlimited"/>, the default); the tablet tier reports <see cref="Tablet"/>.
/// </summary>
public readonly record struct ScribeDocumentPolicy
{
    /// <summary>The shared block cap for the capped surfaces (tablet, chalkboard).</summary>
    public const int StandardMaxBlocks = 15;

    /// <summary>Maximum number of blocks the document may hold, or <c>null</c> for uncapped.</summary>
    public int? MaxBlocks { get; init; }

    /// <summary>Maximum number of tasks the player may pin from this document, or <c>null</c> for uncapped.</summary>
    public int? MaxPins { get; init; }

    /// <summary>When <c>true</c>, both <see cref="CanAdd"/> and <see cref="CanPin"/> deny regardless of count
    /// (a hard or fired tablet).</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The uncapped, editable policy reported by Lectern, Notebook, and Clockmaker's.</summary>
    public static readonly ScribeDocumentPolicy Unlimited = new();

    /// <summary>A WET tablet: at most <see cref="StandardMaxBlocks"/> blocks and 1 pin, editable.</summary>
    public static readonly ScribeDocumentPolicy Tablet = new() { MaxBlocks = StandardMaxBlocks, MaxPins = 1 };

    /// <summary>A hardened or fired tablet: add and pin always deny. Caps are also 0 so it denies even if
    /// <see cref="ReadOnly"/> were cleared.</summary>
    public static readonly ScribeDocumentPolicy UneditableTablet =
        new() { MaxBlocks = 0, MaxPins = 0, ReadOnly = true };

    /// <summary>Whether one more block (of ANY kind) may be added given <paramref name="currentBlockCount"/>.
    /// False when <see cref="ReadOnly"/>; true when uncapped; otherwise true while below the cap. A negative
    /// count is treated as 0.</summary>
    public bool CanAdd(int currentBlockCount)
    {
        if (ReadOnly) return false;
        if (MaxBlocks is not int max) return true;
        return Math.Max(0, currentBlockCount) < max;
    }

    /// <summary>Whether a whole document of <paramref name="blockCount"/> blocks fits this tier — the
    /// Transcribe copy-paste target check (a copy REPLACES the target). False when <see cref="ReadOnly"/>,
    /// true when uncapped, otherwise true at-or-under the cap (<c>&lt;=</c>, unlike <see cref="CanAdd"/>'s
    /// strict <c>&lt;</c>). A negative count is treated as 0.</summary>
    public bool CanHold(int blockCount)
    {
        if (ReadOnly) return false;
        if (MaxBlocks is not int max) return true;
        return Math.Max(0, blockCount) <= max;
    }

    /// <summary>Whether one more pin may be added given <paramref name="currentPinCount"/>. False when
    /// <see cref="ReadOnly"/>, true when uncapped, otherwise true while below the cap. A negative count is treated as 0.</summary>
    public bool CanPin(int currentPinCount)
    {
        if (ReadOnly) return false;
        if (MaxPins is not int max) return true;
        return Math.Max(0, currentPinCount) < max;
    }
}
