using System;

namespace Scribe.Core;

/// <summary>What a completion does to the document itself, once a task has transitioned INTO done under
/// a policy. The <em>decision</em> is shared (reconcile-animating-surfaces D9); how it is applied differs
/// by layer — the server routes it through its persistence-aware write-through (marks the block dirty /
/// writes the ItemStack), a client view applies it optimistically to a local document copy.</summary>
public enum ScribeCompletionDocAction : byte
{
    /// <summary>Leave the document order and membership as-is (only the done flag changed).</summary>
    None = 0,

    /// <summary>Remove the completed task from the document (the <c>Delete</c> policy).</summary>
    Delete = 1,

    /// <summary>Move the completed task to the bottom of the document (the <c>Sink</c> / <c>UnpinSink</c>
    /// policies).</summary>
    SinkToBottom = 2,
}

/// <summary>
/// The single definition of what completing a task means under a <see cref="ScribeCompletionPolicy"/> —
/// shared by the server (applying to the authoritative document through its write-through) and every
/// client view (applying optimistically to a local copy), so no surface derives its own policy behavior
/// (reconcile-animating-surfaces D9). Pure data; no VS API.
///
/// <para>Two layers, deliberately separated so each caller owns <em>application</em> while the
/// <em>decision</em> stays in one place:</para>
/// <list type="bullet">
/// <item><see cref="Decide"/> — the pure policy table: given whether this completion is a transition into
/// done, return the document action + whether the acting player's pin should be removed. Neither side
/// re-derives the switch. The server dispatches the returned <see cref="ScribeCompletionDocAction"/>
/// through its persistence-aware host methods; it never mutates the raw document here.</item>
/// <item><see cref="ApplyLocal"/> — a client convenience that toggles the task's done flag on a local
/// document copy and applies the decided action to it directly (no persistence layer to honor
/// client-side), for the editor/read optimistic-then-confirm path.</item>
/// </list>
/// <para>Pins are never touched here — they live in a per-player store the server owns, so the caller
/// applies <see cref="LocalOutcome.ShouldRemovePin"/> against that store itself. Read-only-source
/// collapsing (a hard/fired tablet forcing every document-mutating policy to Unpin) is also the caller's
/// job: normalize the policy to <see cref="ScribeCompletionPolicy.Unpin"/> before calling, exactly as the
/// server already does.</para>
/// </summary>
public static class ScribeCompletion
{
    /// <summary>The decided consequence of a completion: the document action to apply and whether to
    /// remove the acting player's pin. On a completion that does NOT transition into done (i.e. an
    /// un-check), both are inert (<see cref="ScribeCompletionDocAction.None"/>, no unpin) — unchecking
    /// never sinks, deletes, or unpins.</summary>
    public readonly record struct Decision(ScribeCompletionDocAction DocAction, bool ShouldRemovePin);

    /// <summary>The pure policy table (reconcile-animating-surfaces D9). <paramref name="nowDone"/> is the
    /// task's done state AFTER the toggle; the policy applies only on a transition INTO done, so a false
    /// value yields an inert decision. This is the one place the Delete/Sink/UnpinSink/Unpin/Keep mapping
    /// lives — both the server and client dispatch off it.</summary>
    public static Decision Decide(bool nowDone, ScribeCompletionPolicy policy)
    {
        if (!nowDone) return new Decision(ScribeCompletionDocAction.None, ShouldRemovePin: false);

        return policy switch
        {
            // Delete removes the task; the pin necessarily goes with it.
            ScribeCompletionPolicy.Delete => new Decision(ScribeCompletionDocAction.Delete, ShouldRemovePin: true),
            // Sink moves the (now-done) task to the bottom; the pin stays.
            ScribeCompletionPolicy.Sink => new Decision(ScribeCompletionDocAction.SinkToBottom, ShouldRemovePin: false),
            // UnpinSink sinks AND removes the pin.
            ScribeCompletionPolicy.UnpinSink => new Decision(ScribeCompletionDocAction.SinkToBottom, ShouldRemovePin: true),
            // Unpin removes the pin only; the document (beyond the done flip) is untouched.
            ScribeCompletionPolicy.Unpin => new Decision(ScribeCompletionDocAction.None, ShouldRemovePin: true),
            // Keep leaves the now-done task in place; nothing removed, reordered, or unpinned.
            _ => new Decision(ScribeCompletionDocAction.None, ShouldRemovePin: false),
        };
    }

    /// <summary>The result of a client-side <see cref="ApplyLocal"/>. <see cref="Toggled"/> is false when
    /// the id was unknown or named a non-completable (Text) block (the document is then unchanged). <see cref="NowDone"/>
    /// is the done state after the toggle. <see cref="DocChanged"/> is true when the document content
    /// changed in any way (the flip itself, or a policy delete/sink) — a view uses it to decide whether to
    /// refresh. <see cref="ShouldRemovePin"/> is the decided pin action, for the caller to apply against
    /// the pin store for every id in <see cref="AffectedTaskIds"/> that is currently pinned.
    /// <see cref="AffectedTaskIds"/> is the rows this option mutated (parent + completable children under
    /// Bound; parent only under Independent; parent after discarding children under Discard). Deleted
    /// children under Discard are listed in <see cref="DeletedTaskIds"/> so the caller can drop their pins
    /// even though they were not "completed."</summary>
    public readonly record struct LocalOutcome(
        bool Toggled, bool NowDone, bool DocChanged, bool ShouldRemovePin,
        IReadOnlyList<Guid> AffectedTaskIds, IReadOnlyList<Guid> DeletedTaskIds)
    {
        public LocalOutcome(bool Toggled, bool NowDone, bool DocChanged, bool ShouldRemovePin)
            : this(Toggled, NowDone, DocChanged, ShouldRemovePin,
                AffectedTaskIds: Array.Empty<Guid>(), DeletedTaskIds: Array.Empty<Guid>()) { }
    }

    /// <summary>Client convenience: toggle the task with <paramref name="taskId"/>'s done flag on a LOCAL
    /// document copy, then apply <see cref="Decide"/>'s document action. When the row is a parent (depth 0
    /// with a non-empty owned run), <paramref name="behavior"/> drives a single range mutation (Bound /
    /// Discard) or a parent-only mutation (Independent). A depth-1 row is always a leaf. On an unknown or
    /// non-completable (Text) id the document is unchanged and <see cref="LocalOutcome.Toggled"/> is false.
    /// Unknown <paramref name="behavior"/> values fall back to Bound.</summary>
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

    /// <summary>Set the acting row to <paramref name="nowDone"/> and apply the document action. Unlike
    /// <see cref="ApplyLocal"/> this does not toggle — the server uses it when the pin store is
    /// authoritative for the next done-state. Same parent-range rules as <see cref="ApplyLocal"/>.</summary>
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

    /// <summary>Standalone trash of <paramref name="taskId"/>. A depth-1 row (or a depth-0 with an empty
    /// run, or Independent) deletes only that row. Bound or Discard of a parent deletes the owned run
    /// (parent + contiguous depth-1 children) as one range. Returns the TaskIds that left the document
    /// (empty when the id was unknown).</summary>
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
        // Uncheck: completable rows in the run are already flipped; no unsink, no undelete.

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

        // Parent is still at parentIndex; apply its completion policy alone (leaf).
        var leaf = ApplyLeaf(doc, parentIndex, taskId, nowDone, policy);
        var deleted = new List<Guid>(discarded.Length + leaf.DeletedTaskIds.Count);
        deleted.AddRange(discarded);
        deleted.AddRange(leaf.DeletedTaskIds);
        return new LocalOutcome(Toggled: true, NowDone: nowDone, DocChanged: true,
            ShouldRemovePin: leaf.ShouldRemovePin,
            AffectedTaskIds: leaf.AffectedTaskIds, DeletedTaskIds: deleted);
    }

    /// <summary>Delete the task with <paramref name="taskId"/> by identity, preserving the order of the
    /// rest. Returns whether a task was removed (false on an unknown id).</summary>
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
