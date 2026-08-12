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
    /// the id was unknown or named a non-task block (the document is then unchanged). <see cref="NowDone"/>
    /// is the done state after the toggle. <see cref="DocChanged"/> is true when the document content
    /// changed in any way (the flip itself, or a policy delete/sink) — a view uses it to decide whether to
    /// refresh. <see cref="ShouldRemovePin"/> is the decided pin action, for the caller to apply against
    /// the pin store.</summary>
    public readonly record struct LocalOutcome(bool Toggled, bool NowDone, bool DocChanged, bool ShouldRemovePin);

    /// <summary>Client convenience: toggle the task with <paramref name="taskId"/>'s done flag on a LOCAL
    /// document copy, then apply <see cref="Decide"/>'s document action directly (there is no persistence
    /// layer to honor client-side — the authoritative resync supersedes this shortly). Produces the same
    /// document the server produces through its write-through, so an optimistic local apply and the later
    /// resync agree. On an unknown or non-task id the document is unchanged and
    /// <see cref="LocalOutcome.Toggled"/> is false.</summary>
    public static LocalOutcome ApplyLocal(ScribeDocument doc, Guid taskId, ScribeCompletionPolicy policy)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        var block = doc.FindByTaskId(taskId);
        if (block is null || !block.IsTask)
            return new LocalOutcome(Toggled: false, NowDone: false, DocChanged: false, ShouldRemovePin: false);

        bool nowDone = !block.Done;
        block.Done = nowDone;
        bool docChanged = true; // the flip itself is a content change

        var decision = Decide(nowDone, policy);
        switch (decision.DocAction)
        {
            case ScribeCompletionDocAction.Delete:
                TryDeleteTask(doc, taskId); // docChanged already true from the flip
                break;
            case ScribeCompletionDocAction.SinkToBottom:
                doc.MoveTaskToBottom(taskId); // no-op if already last; the flip still counts as a change
                break;
            case ScribeCompletionDocAction.None:
            default:
                break;
        }

        return new LocalOutcome(Toggled: true, NowDone: nowDone, DocChanged: docChanged, ShouldRemovePin: decision.ShouldRemovePin);
    }

    /// <summary>Delete the task with <paramref name="taskId"/> by identity, preserving the order of the
    /// rest. Returns whether a task was removed (false on an unknown id).</summary>
    private static bool TryDeleteTask(ScribeDocument doc, Guid taskId)
    {
        for (int i = 0; i < doc.Blocks.Count; i++)
        {
            if (doc.Blocks[i].TaskId == taskId && doc.Blocks[i].IsTask)
                return doc.DeleteBlock(i);
        }
        return false;
    }
}
