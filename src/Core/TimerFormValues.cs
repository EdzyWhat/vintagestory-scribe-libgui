namespace Scribe.Core;

/// <summary>The values of the Clockmaker's Notebook new-timer form: name, duration fields and mode.</summary>
public readonly record struct TimerFormValues(string Label, int Hours, int Minutes, int Seconds, TimerMode Mode)
{
    /// <summary>An empty form: no name, zero duration, and the player's preferred mode.</summary>
    public static TimerFormValues Blank(TimerMode preferredMode) => new("", 0, 0, 0, preferredMode);
}
