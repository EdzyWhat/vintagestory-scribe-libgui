using System;

namespace Scribe.Core;

/// <summary>What a completion does to the document once a task transitions INTO done under a policy. The
/// decision is shared; application differs by layer (the server uses its persistence-aware write-through, a
/// client view applies it optimistically to a local copy).</summary>
public enum ScribeCompletionDocAction : byte
{
    /// <summary>Leave order and membership as-is (only the done flag changed).</summary>
    None = 0,

    /// <summary>Remove the completed task (the <c>Delete</c> policy).</summary>
    Delete = 1,

    /// <summary>Move the completed task to the bottom (the <c>Sink</c> / <c>UnpinSink</c> policies).</summary>
    SinkToBottom = 2,
}

/// <summary>
/// The single definition of what completing a task means under a <see cref="ScribeCompletionPolicy"/>,
/// shared by the server and every client view so no surface derives its own policy behavior. Pure data; no
/// VS API.
///
/// <para><see cref="Decide"/> is the pure policy table; <see cref="ApplyLocal"/> is a client convenience
/// that toggles the done flag on a local document copy and applies the decided action. The server
/// dispatches the decided <see cref="ScribeCompletionDocAction"/> through its own persistence-aware host
/// methods and never mutates the raw document here.</para>
///
/// <para>Pins are never touched here (the server owns the per-player store); callers apply
/// <see cref="LocalOutcome.ShouldRemovePin"/> themselves. Callers also collapse read-only sources (a
/// hard/fired tablet) to <see cref="ScribeCompletionPolicy.Unpin"/> before calling.</para>
/// </summary>
public static class ScribeCompletion
{
    /// <summary>The decided consequence of a completion. An un-check (not a transition into done) is inert:
    /// <see cref="ScribeCompletionDocAction.None"/> and no unpin.</summary>
    public readonly record struct Decision(ScribeCompletionDocAction DocAction, bool ShouldRemovePin);

    /// <summary>The pure policy table, the one place the Delete/Sink/UnpinSink/Unpin/Keep mapping lives.
    /// <paramref name="nowDone"/> is the done state AFTER the toggle; the policy applies only on a transition
    /// INTO done.</summary>
    public static Decision Decide(bool nowDone, ScribeCompletionPolicy policy)
    {
        if (!nowDone) return new Decision(ScribeCompletionDocAction.None, ShouldRemovePin: false);

        return policy switch
        {
            // Delete removes the task; the pin goes with it.
            ScribeCompletionPolicy.Delete => new Decision(ScribeCompletionDocAction.Delete, ShouldRemovePin: true),
            // Sink moves the done task to the bottom; the pin stays.
            ScribeCompletionPolicy.Sink => new Decision(ScribeCompletionDocAction.SinkToBottom, ShouldRemovePin: false),
            // UnpinSink sinks AND removes the pin.
            ScribeCompletionPolicy.UnpinSink => new Decision(ScribeCompletionDocAction.SinkToBottom, ShouldRemovePin: true),
            // Unpin removes the pin only.
            ScribeCompletionPolicy.Unpin => new Decision(ScribeCompletionDocAction.None, ShouldRemovePin: true),
            // Keep leaves the done task in place.
            _ => new Decision(ScribeCompletionDocAction.None, ShouldRemovePin: false),
        };
    }

    /// <summary>The result of <see cref="ApplyLocal"/>. <see cref="Toggled"/> is false when the id was
    /// unknown or non-completable (Text), leaving the document unchanged. <see cref="DocChanged"/> is true
    /// for any content change (the flip, or a policy delete/sink). <see cref="ShouldRemovePin"/> is the pin
    /// action for the caller to apply to every id in <see cref="AffectedTaskIds"/> (the rows mutated: parent
    /// plus completable children under Bound, parent only under Independent or Discard).
    /// <see cref="DeletedTaskIds"/> lists rows removed, including children discarded under Discard, so the
    /// caller can drop their pins.</summary>
    public readonly record struct LocalOutcome(
        bool Toggled, bool NowDone, bool DocChanged, bool ShouldRemovePin,
        IReadOnlyList<Guid> AffectedTaskIds, IReadOnlyList<Guid> DeletedTaskIds)
    {
        public LocalOutcome(bool Toggled, bool NowDone, bool DocChanged, bool ShouldRemovePin)
            : this(Toggled, NowDone, DocChanged, ShouldRemovePin,
                AffectedTaskIds: Array.Empty<Guid>(), DeletedTaskIds: Array.Empty<Guid>()) { }
    }

    /// <summary>Client convenience: toggles the done flag of <paramref name="taskId"/> on a LOCAL document
    /// copy, then applies <see cref="Decide"/>'s action. For a parent (depth 0 with a non-empty owned run),
    /// <paramref name="behavior"/> picks a range mutation (Bound / Discard) or parent-only (Independent). A
    /// depth-1 row is always a leaf. Unknown or Text ids leave the document unchanged
    /// (<see cref="LocalOutcome.Toggled"/> false). Unknown behavior values fall back to Bound.</summary>
    public static LocalOutcome ApplyLocal(ScribeDocument doc, Guid taskId, ScribeCompletionPolicy policy,
        ScribeSubtaskBehavior behavior = ScribeSubtaskBehavior.Bound)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        int idx = doc.IndexOf(taskId);
        if (idx < 0) return new LocalOutcome(Toggled: false, NowDone: false, DocChanged: false, ShouldRemovePin: false);
        var block = doc.Blocks[idx];
        if (!block.IsCompletable)
            return new LocalOutcome(Toggled: false, NowDone: false, DocChanged: false, ShouldRemovePin: false);

        bool nowDone = !block.Done;
        return ApplyGivenDone(doc, taskId, nowDone, policy, behavior);
    }

    /// <summary>Sets the acting row to <paramref name="nowDone"/> and applies the document action. Unlike
    /// <see cref="ApplyLocal"/> it does not toggle; the server uses it when the pin store decides the next
    /// done-state. Same parent-range rules.</summary>
    public static LocalOutcome ApplyGivenDone(ScribeDocument doc, Guid taskId, bool nowDone,
        ScribeCompletionPolicy policy, ScribeSubtaskBehavior behavior = ScribeSubtaskBehavior.Bound)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        int idx = doc.IndexOf(taskId);
        if (idx < 0) return new LocalOutcome(Toggled: false, NowDone: false, DocChanged: false, ShouldRemovePin: false);
        var block = doc.Blocks[idx];
        if (!block.IsCompletable)
            return new LocalOutcome(Toggled: false, NowDone: false, DocChanged: false, ShouldRemovePin: false);

        behavior = ScribePlayerSettings.NormalizeSubtaskBehavior(behavior);
        var (runStart, runEnd) = doc.OwnedRun(idx);
        bool isParent = block.Depth == 0 && runStart < runEnd;

        if (!isParent || behavior == ScribeSubtaskBehavior.Independent)
            return ApplyLeaf(doc, idx, taskId, nowDone, policy);

        if (behavior == ScribeSubtaskBehavior.DiscardChildren)
            return ApplyDiscardParent(doc, idx, taskId, nowDone, policy, runStart, runEnd);

        return ApplyBoundParent(doc, idx, taskId, nowDone, policy, runEnd);
    }

    /// <summary>Standalone trash of <paramref name="taskId"/>. A depth-1 row, a depth-0 row with an empty
    /// run, or any row under Independent deletes only itself; a parent under Bound or Discard deletes its
    /// owned run (parent plus contiguous depth-1 children). Returns the TaskIds that left the document.</summary>
    public static IReadOnlyList<Guid> ApplyDelete(ScribeDocument doc, Guid taskId,
        ScribeSubtaskBehavior behavior = ScribeSubtaskBehavior.Bound)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        int idx = doc.IndexOf(taskId);
        if (idx < 0) return Array.Empty<Guid>();

        behavior = ScribePlayerSettings.NormalizeSubtaskBehavior(behavior);
        var (runStart, runEnd) = doc.OwnedRun(idx);
        bool isParent = doc.Blocks[idx].Depth == 0 && runStart < runEnd;

        if (!isParent || behavior == ScribeSubtaskBehavior.Independent)
        {
            var id = doc.Blocks[idx].TaskId;
            doc.DeleteBlock(idx);
            return new[] { id };
        }

        var deleted = new Guid[runEnd - idx];
        for (int i = idx; i < runEnd; i++) deleted[i - idx] = doc.Blocks[i].TaskId;
        doc.DeleteRange(idx, runEnd);
        return deleted;
    }

    private static LocalOutcome ApplyLeaf(ScribeDocument doc, int idx, Guid taskId, bool nowDone,
        ScribeCompletionPolicy policy)
    {
        doc.Blocks[idx].Done = nowDone;
        var decision = Decide(nowDone, policy);
        var deleted = Array.Empty<Guid>();
        switch (decision.DocAction)
        {
            case ScribeCompletionDocAction.Delete:
                TryDeleteTask(doc, taskId);
                deleted = new[] { taskId };
                break;
            case ScribeCompletionDocAction.SinkToBottom:
                doc.MoveTaskToBottom(taskId);
                break;
        }
        return new LocalOutcome(Toggled: true, NowDone: nowDone, DocChanged: true,
            ShouldRemovePin: decision.ShouldRemovePin,
            AffectedTaskIds: new[] { taskId }, DeletedTaskIds: deleted);
    }

    private static LocalOutcome ApplyBoundParent(ScribeDocument doc, int parentIndex, Guid taskId,
        bool nowDone, ScribeCompletionPolicy policy, int runEnd)
    {
        var completable = new List<Guid>();
        for (int i = parentIndex; i < runEnd; i++)
        {
            if (doc.Blocks[i].IsCompletable)
            {
                doc.Blocks[i].Done = nowDone;
                completable.Add(doc.Blocks[i].TaskId);
            }
        }

        var decision = Decide(nowDone, policy);
        var deleted = Array.Empty<Guid>();
        if (nowDone)
        {
            switch (decision.DocAction)
            {
                case ScribeCompletionDocAction.Delete:
                    deleted = new Guid[runEnd - parentIndex];
                    for (int i = parentIndex; i < runEnd; i++)
                        deleted[i - parentIndex] = doc.Blocks[i].TaskId;
                    doc.DeleteRange(parentIndex, runEnd);
                    break;
                case ScribeCompletionDocAction.SinkToBottom:
                    doc.MoveRangeToBottom(parentIndex, runEnd);
                    break;
            }
        }
        // Uncheck: rows are already flipped; no unsink, no undelete.

        return new LocalOutcome(Toggled: true, NowDone: nowDone, DocChanged: true,
            ShouldRemovePin: decision.ShouldRemovePin,
            AffectedTaskIds: completable, DeletedTaskIds: deleted);
    }

    private static LocalOutcome ApplyDiscardParent(ScribeDocument doc, int parentIndex, Guid taskId,
        bool nowDone, ScribeCompletionPolicy policy, int runStart, int runEnd)
    {
        var discarded = new Guid[runEnd - runStart];
        for (int i = runStart; i < runEnd; i++) discarded[i - runStart] = doc.Blocks[i].TaskId;
        doc.DeleteRange(runStart, runEnd);

        // The parent is still at parentIndex; apply its policy alone.
        var leaf = ApplyLeaf(doc, parentIndex, taskId, nowDone, policy);
        var deleted = new List<Guid>(discarded.Length + leaf.DeletedTaskIds.Count);
        deleted.AddRange(discarded);
        deleted.AddRange(leaf.DeletedTaskIds);
        return new LocalOutcome(Toggled: true, NowDone: nowDone, DocChanged: true,
            ShouldRemovePin: leaf.ShouldRemovePin,
            AffectedTaskIds: leaf.AffectedTaskIds, DeletedTaskIds: deleted);
    }

    /// <summary>Deletes the completable task with <paramref name="taskId"/>, preserving the order of the
    /// rest. Returns false on an unknown id.</summary>
    private static bool TryDeleteTask(ScribeDocument doc, Guid taskId)
    {
        for (int i = 0; i < doc.Blocks.Count; i++)
        {
            if (doc.Blocks[i].TaskId == taskId && doc.Blocks[i].IsCompletable)
                return doc.DeleteBlock(i);
        }
        return false;
    }
}
