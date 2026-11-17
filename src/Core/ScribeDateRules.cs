namespace Scribe.Core;

/// <summary>Where a dated task stands relative to today.</summary>
public enum ScribeDateStatusKind : byte
{
    /// <summary>The task is checked off.</summary>
    Done = 0,

    /// <summary>Unchecked, and its (clamped) date is today.</summary>
    DueToday = 1,

    /// <summary>Unchecked, and its date is later than today.</summary>
    Upcoming = 2,

    /// <summary>Unchecked, and its date has passed.</summary>
    Overdue = 3,
}

/// <summary>A dated task's status. <see cref="Days"/> is the number of days until the date for
/// <see cref="ScribeDateStatusKind.Upcoming"/>, since it for <see cref="ScribeDateStatusKind.Overdue"/>, and
/// 0 otherwise.</summary>
public readonly record struct ScribeDateStatus(ScribeDateStatusKind Kind, int Days);

/// <summary>
/// The pure rules for dated blocks: status relative to today and the yearly repeat rollover. Only
/// completable blocks (<see cref="ScribeBlock.IsCompletable"/>) have a status or roll over; a dated note or
/// quest objective has neither. Every comparison uses the clamped day
/// (<see cref="IScribeCalendarShape.Clamp"/>); stored days are never rewritten here.
/// </summary>
public static class ScribeDateRules
{
    /// <summary>Whether the UI offers dates on <paramref name="block"/>: a plain task that did not come from an
    /// assignment. Other kinds keep a date they carry but are never dated through the menu.</summary>
    public static bool IsDatable(ScribeBlock block) =>
        block.Kind == ScribeBlockKind.Task && block.Assignment is null;

    /// <summary>Whether <paramref name="block"/> repeats yearly: a plain task with a date and Repeat yearly on.
    /// Completion never deletes such a task.</summary>
    public static bool IsRepeatingTask(ScribeBlock block) =>
        block.Kind == ScribeBlockKind.Task && block.Date is not null && block.RepeatYearly;

    /// <summary>The block's status relative to <paramref name="today"/>, or null for an undated or
    /// non-completable block.</summary>
    public static ScribeDateStatus? Classify(ScribeBlock block, ScribeDate today, IScribeCalendarShape shape)
    {
        if (block.Date is not { } date || !block.IsCompletable) return null;
        if (block.Done) return new ScribeDateStatus(ScribeDateStatusKind.Done, 0);

        int difference = shape.ToOrdinal(date) - shape.ToOrdinal(today);
        if (difference == 0) return new ScribeDateStatus(ScribeDateStatusKind.DueToday, 0);
        return difference > 0
            ? new ScribeDateStatus(ScribeDateStatusKind.Upcoming, difference)
            : new ScribeDateStatus(ScribeDateStatusKind.Overdue, -difference);
    }

    /// <summary>
    /// The date a repeating block rolls over to, or null when it does not roll over now. A block rolls over
    /// when it is completable, checked, repeats yearly, and its clamped date is before today. The new date
    /// keeps the stored month and day, in the earliest year whose clamped date is on or after today, so a
    /// rollover first evaluated on an anniversary lands on today. The caller sets the new date and unchecks
    /// the block. An unchecked past date never rolls over (it stays overdue), and a checked date that is
    /// today or later waits until it has passed.
    /// </summary>
    public static ScribeDate? NextOccurrence(ScribeBlock block, ScribeDate today, IScribeCalendarShape shape)
    {
        if (block.Date is not { } date || !block.RepeatYearly || !block.Done || !block.IsCompletable) return null;

        int todayOrdinal = shape.ToOrdinal(today);
        if (shape.ToOrdinal(date) >= todayOrdinal) return null;

        for (int year = Math.Max(date.Year + 1, today.Year); year <= ScribeDate.MaxYear; year++)
        {
            var candidate = ScribeDate.Create(year, date.Month, date.Day);
            if (shape.ToOrdinal(candidate) >= todayOrdinal) return candidate;
        }
        return null;
    }
}
