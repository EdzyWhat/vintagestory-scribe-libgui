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

    /// <summary>Completing the task keeps it pinned AND leaves it in place on the HUD — unlike
    /// <see cref="Sink"/>, a completed <c>Keep</c> task is NOT de-prioritized to the bottom, so the
    /// player keeps a persistent checked record in its original slot. Server-side this behaves exactly
    /// like <see cref="Sink"/> (nothing is removed); the only difference is HUD display ordering, which
    /// the Mod layer applies. Appended as value 3 to preserve the wire/serialized values of the
    /// original three.</summary>
    Keep = 3,

    /// <summary>Completing the task both REMOVES the player's pin for it AND moves the underlying task to
    /// the bottom of its source document (combining <see cref="Unpin"/> + <see cref="Sink"/>). The pin
    /// departs the HUD after the undo window (like Unpin) while the document reorder is applied server-side
    /// (like Sink). Appended as value 4 to preserve the wire/serialized values of the earlier four.</summary>
    UnpinSink = 4,
}
