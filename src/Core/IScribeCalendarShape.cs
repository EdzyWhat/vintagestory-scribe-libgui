namespace Scribe.Core;

/// <summary>
/// The shape of the world's calendar: how many days each month has. Every date calculation asks this
/// interface, so uneven month lengths can be supported later without touching the date rules. A year always
/// has <see cref="ScribeDate.MonthsPerYear"/> months.
///
/// <para>A <b>day ordinal</b> counts days from the first day of Year 1 (ordinal 0). It is used only for
/// arithmetic (days between, next occurrence), never stored.</para>
/// </summary>
public interface IScribeCalendarShape
{
    /// <summary>Days in <paramref name="month"/> (1..12) of <paramref name="year"/>; at least 1.</summary>
    int DaysInMonth(int year, int month);

    /// <summary>The date with its day clamped to the last day of its month. The stored date is not
    /// changed; callers use the result for display and arithmetic.</summary>
    ScribeDate Clamp(ScribeDate date)
    {
        int last = DaysInMonth(date.Year, date.Month);
        return date.Day <= last ? date : ScribeDate.Create(date.Year, date.Month, last);
    }

    /// <summary>The day ordinal of <paramref name="date"/>, using its clamped day.</summary>
    int ToOrdinal(ScribeDate date)
    {
        var clamped = Clamp(date);
        int ordinal = 0;
        for (int year = 1; year < clamped.Year; year++)
            for (int month = 1; month <= ScribeDate.MonthsPerYear; month++)
                ordinal += DaysInMonth(year, month);
        for (int month = 1; month < clamped.Month; month++)
            ordinal += DaysInMonth(clamped.Year, month);
        return ordinal + clamped.Day - 1;
    }
}

/// <summary>
/// A calendar where every month has the same number of days: Vintage Story's calendar, built from the
/// world's days-per-month setting.
/// </summary>
public sealed class UniformCalendarShape : IScribeCalendarShape
{
    /// <summary>Days in every month; at least 1.</summary>
    public int DaysPerMonth { get; }

    public UniformCalendarShape(int daysPerMonth)
    {
        DaysPerMonth = Math.Max(1, daysPerMonth);
    }

    public int DaysInMonth(int year, int month) => DaysPerMonth;

    /// <summary>Same result as the interface's general loop, computed directly.</summary>
    public int ToOrdinal(ScribeDate date)
    {
        int day = Math.Min(date.Day, DaysPerMonth);
        return ((date.Year - 1) * ScribeDate.MonthsPerYear + (date.Month - 1)) * DaysPerMonth + day - 1;
    }

    /// <summary>The date for a day ordinal (0 = the first day of Year 1). Negative ordinals map to that
    /// first day.</summary>
    public ScribeDate FromOrdinal(long ordinal)
    {
        if (ordinal < 0) ordinal = 0;
        long daysPerYear = (long)DaysPerMonth * ScribeDate.MonthsPerYear;
        long year = ordinal / daysPerYear + 1;
        long rest = ordinal % daysPerYear;
        int month = (int)(rest / DaysPerMonth) + 1;
        int day = (int)(rest % DaysPerMonth) + 1;
        return ScribeDate.Create((int)Math.Min(year, ScribeDate.MaxYear), month, day);
    }
}
