namespace Scribe.Core;

/// <summary>
/// What happens to a pinned task — and the player's pin for it — when the player completes it.
/// A per-player, client-local preference (see <see cref="ScribePlayerSettings.CompletionPolicy"/>):
/// the client carries its current policy in the completion request and the server validates/normalizes
/// it and applies it (the server owns both the completion and any pin/task removal).
///
/// Replaces the earlier boolean <c>CompleteUnpins</c>: <see cref="Unpin"/> is the old
/// <c>true</c> behavior and <see cref="Sink"/> the old <c>false</c> behavior, with
/// <see cref="Delete"/> added. Game-agnostic (pure BCL).
/// </summary>
public enum ScribeCompletionPolicy : byte
{
    /// <summary>The completed task stays pinned; the HUD de-prioritizes it (sinks it to the bottom).
    /// The default — nothing is removed, so completion is non-destructive and reversible.</summary>
    Sink = 0,

    /// <summary>Completing the task removes the player's pin for it (the old "check it off and it
    /// leaves my list" gesture). The underlying task is untouched.</summary>
    Unpin = 1,

    /// <summary>Completing the task deletes the underlying task from its document. Destructive; the
    /// pin necessarily goes with the task.</summary>
    Delete = 2,
}
