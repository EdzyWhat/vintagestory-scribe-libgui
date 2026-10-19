using System;
using System.Collections.Generic;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// Per-item proportions that parameterise <see cref="ScribeLayout"/>, as fractions of the outer box
/// dimensions. <see cref="Default"/> is the Lectern layout; override fields with <c>Default with { ... }</c>.
/// </summary>
public readonly record struct ScribeLayoutProportions
{
    public float TitleBarFrac { get; init; }
    public float InnerHFrac   { get; init; }
    public float SideColFrac  { get; init; }
    public float TitleBtnsWFrac { get; init; }
    public float TitleBtnsHFrac { get; init; }

    public ScribeLayoutProportions()
    {
        // Small title bar leaves tab content height for the Row 2 subtitle; the two fracs sum to 0.93, leaving a ~7% bottom margin.
        TitleBarFrac   = 0.115f;
        InnerHFrac     = 0.815f;
        SideColFrac    = 0.10f;
        TitleBtnsWFrac = 0.80f;
        TitleBtnsHFrac = 0.065f;
    }

    public static readonly ScribeLayoutProportions Default = new();
}

/// <summary>
/// The dialog layout driven by one outer width <paramref name="W"/>, an art aspect ratio
/// <paramref name="AspectH"/>, and an optional proportions override.
/// </summary>
public readonly record struct ScribeLayout(float W, float AspectH, ScribeLayoutProportions? Props = null)
{
    private ScribeLayoutProportions P => Props ?? ScribeLayoutProportions.Default;

    public float H => W * AspectH;

    public float TitleBarH => P.TitleBarFrac * H;

    public float InnerW => W;

    public float InnerH => P.InnerHFrac * H;

    public float SideColW => P.SideColFrac * W;

    /// <summary>The centre tasks column: InnerW minus both side columns.</summary>
    public float TasksColW => (1f - 2f * P.SideColFrac) * W;

    public float TitleBtnsW => P.TitleBtnsWFrac * W;

    public float TitleBtnsH => P.TitleBtnsHFrac * H;
}

/// <summary>
/// The minimal surface the dialog layer needs from any Scribe block entity or item host; the dialog
/// never holds a concrete block-entity reference.
/// </summary>
public interface IScribeDocumentHost
{
    /// <summary>The server-authoritative document shown in the read and pin views.</summary>
    ScribeDocument Document { get; }

    /// <summary>True when another player holds the editor lock on this block.</summary>
    bool IsLockedByOther(string viewerUid);

    /// <summary>Updates the client-side cache with a fresh optimistic copy after a flush.</summary>
    void ApplyLocalOptimisticEdit(ScribeDocument doc);

    /// <summary>Backdrop spec (art texture + fallback colour) for this item's page.</summary>
    ScribeBackdropSpec BackdropSpec { get; }

    /// <summary>Computes the dialog layout for a pixel-art size setting.</summary>
    ScribeLayout GetLayout(float pixelArtSize);

    /// <summary>Item-specific fallback title when the player clears the title and saves
    /// (e.g. <c>"Lectern"</c>). Distinct from the Core codec's <c>"Untitled"</c> constant.</summary>
    string DefaultDocumentTitle { get; }

    /// <summary>The guestbook for this block — visitor entries recorded server-side on GUI open.</summary>
    GuestbookStore Guestbook { get; }

    /// <summary>The per-tier cap (max blocks, max pins, read-only). Defaults to
    /// <see cref="ScribeDocumentPolicy.Unlimited"/>; only the tablet tier overrides it. The dialog applies it
    /// at the editor mutation boundary, not inside <see cref="ScribeDocument"/>.</summary>
    ScribeDocumentPolicy Policy => ScribeDocumentPolicy.Unlimited;

    // Server-side document write-through (lock-free, called from completion/pin handlers)

    /// <summary>Server-side: set a task's done flag by TaskId. No-op if not found. Every write-through
    /// below persists the host (block: mark dirty and resync; item: write the ItemStack).</summary>
    void SetTaskDoneFromReader(Guid taskId, bool done);

    /// <summary>Server-side: delete a task by TaskId. Returns true if removed.</summary>
    bool DeleteTaskFromReader(Guid taskId);

    /// <summary>Server-side: persist the in-memory document after a Core mutation already applied to <see cref="Document"/>.</summary>
    void PersistFromReader();

    /// <summary>Server-side: move a task to the bottom. Returns true if moved.</summary>
    bool MoveTaskToBottomFromReader(Guid taskId);

    /// <summary>Server-side: set a task's text by TaskId. Rejects blank/whitespace. Returns true if changed.</summary>
    bool SetTaskTextFromReader(Guid taskId, string text);

    /// <summary>Server-side: set a Tracker's live <c>CurrentQuantity</c> (clamped only to ≥ 0; overflow above
    /// the target is preserved). Driven by the client count engine from the viewer's carried inventory, so
    /// multiplayer viewers converge. No-op for a non-Tracker or unknown TaskId. Returns true if changed.</summary>
    bool SetTrackerCurrentQuantityFromReader(Guid taskId, int currentQuantity);

    /// <summary>Server-side: set a QuestObjective's backend-reported progress, clamped into
    /// <c>[0, TargetQuantity]</c> by the Core model. Driven by the client quest watcher, never carried
    /// inventory. No-op for a non-QuestObjective or unknown TaskId. Returns true if changed.</summary>
    bool SetQuestObjectiveProgressFromReader(Guid taskId, int currentQuantity);

    // Read View filter-pill + subtask-collapse persisted state

    /// <summary>The last-selected Read View filter pill (a <see cref="ReadViewFilterCategory"/> cast to byte)
    /// for THIS instance, not per-player. <c>0</c> (All) when never set.</summary>
    byte ReadViewFilterCategory { get; }

    /// <summary>TaskIds of subtask-group parents collapsed in this instance's Read View; empty when never set.</summary>
    IReadOnlyCollection<Guid> CollapsedGroupIds { get; }

    /// <summary>Server-side: overwrite both Read View state pieces at once (they ride the same client packet,
    /// <see cref="ScribeSetReadViewStateMessage"/>). A lock-free, always-allowed viewer preference, not a document edit.</summary>
    void SetReadViewStateFromReader(byte filterCategory, IReadOnlyCollection<Guid> collapsedGroupIds);
}
