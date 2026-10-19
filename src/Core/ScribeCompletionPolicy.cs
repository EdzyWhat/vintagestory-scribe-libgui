namespace Scribe.Core;

/// <summary>
/// What happens to a pinned task, and the player's pin for it, when the player completes it. A per-player,
/// client-local preference (<see cref="ScribePlayerSettings.CompletionPolicy"/>): the client sends its policy
/// with the completion request and the server validates, normalizes and applies it. Pure BCL.
/// </summary>
public enum ScribeCompletionPolicy : byte
{
    /// <summary>The completed task stays pinned; the HUD sinks it to the bottom. The default: nothing is
    /// removed, so completion is reversible.</summary>
    Sink = 0,

    /// <summary>Completing removes the player's pin; the underlying task is untouched.</summary>
    Unpin = 1,

    /// <summary>Completing deletes the underlying task from its document (destructive); the pin goes with it.</summary>
    Delete = 2,

    /// <summary>Completing keeps the task pinned and in place on the HUD (not sunk). Server-side identical
    /// to <see cref="Sink"/>; only the Mod layer's HUD ordering differs.</summary>
    Keep = 3,

    /// <summary>Completing removes the player's pin AND moves the task to the bottom of its source document
    /// (<see cref="Unpin"/> + <see cref="Sink"/>).</summary>
    UnpinSink = 4,
}
