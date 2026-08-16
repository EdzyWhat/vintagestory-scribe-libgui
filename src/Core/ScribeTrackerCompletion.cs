namespace Scribe.Core;

/// <summary>
/// What happens to a Tracker task when its carried-inventory count reaches its target
/// (add-tracker-link-tasks D6). A per-player, client-local preference
/// (<see cref="ScribePlayerSettings.TrackerCompletion"/>): the client's count engine detects
/// target-met and issues the matching edit through the normal server-authoritative path.
///
/// Distinct from <see cref="ScribeCompletionPolicy"/>, which governs what completing (any) PINNED
/// task does to the pin — this governs only the auto-action a Tracker takes when it fills up.
/// Game-agnostic (pure BCL).
/// </summary>
public enum ScribeTrackerCompletion : byte
{
    /// <summary>The default. When the Tracker reaches its target it is marked done — exactly the same
    /// edit as the player ticking its checkbox (so it inherits the player's own
    /// <see cref="ScribeCompletionPolicy"/>). Non-destructive: the task stays in the document.</summary>
    Complete = 0,

    /// <summary>When the Tracker reaches its target it is deleted from its document (the "gather N, then
    /// it's off my list" gesture). Destructive.</summary>
    Delete = 1,

    /// <summary>When the Tracker reaches its target nothing happens automatically — the row simply reads
    /// as satisfied and the player decides what to do with it. For players who want the counter as a
    /// passive readout, not an auto-completing task.</summary>
    Nothing = 2,
}
