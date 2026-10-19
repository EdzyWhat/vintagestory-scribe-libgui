namespace Scribe.Core;

/// <summary>
/// What happens to a Tracker task when its carried-inventory count reaches its target. A client-local
/// per-player preference (<see cref="ScribePlayerSettings.TrackerCompletion"/>); the client issues the
/// matching edit through the normal server-authoritative path.
///
/// Distinct from <see cref="ScribeCompletionPolicy"/>, which governs what completing a pinned task does
/// to the pin.
/// </summary>
public enum ScribeTrackerCompletion : byte
{
    /// <summary>The default. Marks the task done, same edit as ticking its checkbox (so the player's
    /// <see cref="ScribeCompletionPolicy"/> applies). Non-destructive.</summary>
    Complete = 0,

    /// <summary>Deletes the task from its document. Destructive.</summary>
    Delete = 1,

    /// <summary>No automatic action; the row reads as satisfied and the player decides.</summary>
    Nothing = 2,
}
