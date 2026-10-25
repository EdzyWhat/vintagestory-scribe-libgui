namespace Scribe.Core;

/// <summary>Remembers which timer status a view was last built for, so it rebuilds only when the status changes.
/// Seed it with the live status when the view opens; otherwise the first push that matches the default
/// (Idle) looks like "no change" and the view stays stale.</summary>
public sealed class TimerViewTracker
{
    public TimerViewTracker(TimerStatus seed = TimerStatus.Idle) => Status = seed;

    public TimerStatus Status { get; private set; }

    /// <summary>Records <paramref name="next"/>; true when it differs from the last recorded status.</summary>
    public bool TryAdvance(TimerStatus next)
    {
        if (next == Status) return false;
        Status = next;
        return true;
    }
}
