namespace Scribe.Core;

/// <summary>
/// The stepping and clamping rules of the date picker, free of any UI. The picker shows a month, a day and a
/// year; each stepper changes one of them and the model keeps the combination a valid date.
///
/// <list type="bullet">
/// <item>The day stepper stops at 1 and at the month's length; it never carries into the month.</item>
/// <item>The month stepper carries into the year (stepping back from month 1 gives month 12 of the previous
/// year) and is blocked at Year 1 month 1 and Year 9999 month 12.</item>
/// <item>The year runs 1 to <see cref="ScribeDate.MaxYear"/> and does not wrap.</item>
/// <item>When the month or year changes, a day above the new month's length is clamped down; it does not
/// return to the larger value when stepping back.</item>
/// </list>
/// </summary>
public sealed class ScribeDatePickerModel
{
    private readonly IScribeCalendarShape shape;

    /// <summary>Starts on <paramref name="start"/>: a year above <see cref="ScribeDate.MaxYear"/> starts at the
    /// maximum year, and a day past the month's end starts on the month's last day.</summary>
    public ScribeDatePickerModel(IScribeCalendarShape shape, ScribeDate start)
    {
        this.shape = shape;
        Year = Math.Clamp(start.Year, 1, ScribeDate.MaxYear);
        Month = start.Month;
        Day = Math.Min(start.Day, DaysInCurrentMonth);
    }

    /// <summary>The year, 1 to <see cref="ScribeDate.MaxYear"/>.</summary>
    public int Year { get; private set; }

    /// <summary>The month, 1 to <see cref="ScribeDate.MonthsPerYear"/>.</summary>
    public int Month { get; private set; }

    /// <summary>The day, 1 to the month's length.</summary>
    public int Day { get; private set; }

    /// <summary>Moves the picker to <paramref name="date"/>, with the same year and day limits as the constructor.</summary>
    public void Reset(ScribeDate date)
    {
        Year = Math.Clamp(date.Year, 1, ScribeDate.MaxYear);
        Month = date.Month;
        Day = Math.Min(date.Day, DaysInCurrentMonth);
    }

    /// <summary>The date the picker currently shows.</summary>
    public ScribeDate ToDate() => ScribeDate.Create(Year, Month, Day);

    /// <summary>Steps the day by <paramref name="delta"/> (±1). Returns false when it is already at an end.</summary>
    public bool StepDay(int delta)
    {
        int next = Day + Math.Sign(delta);
        if (next < 1 || next > DaysInCurrentMonth) return false;
        Day = next;
        return true;
    }

    /// <summary>Steps the month by <paramref name="delta"/> (±1), carrying into the year. Returns false at Year 1
    /// month 1 going back and at Year <see cref="ScribeDate.MaxYear"/> month 12 going forward.</summary>
    public bool StepMonth(int delta)
    {
        int year = Year;
        int month = Month + Math.Sign(delta);
        if (month < 1)
        {
            month = ScribeDate.MonthsPerYear;
            year--;
        }
        else if (month > ScribeDate.MonthsPerYear)
        {
            month = 1;
            year++;
        }
        if (year < 1 || year > ScribeDate.MaxYear) return false;

        Year = year;
        Month = month;
        Day = Math.Min(Day, DaysInCurrentMonth);
        return true;
    }

    /// <summary>Steps the year by <paramref name="delta"/> (±1) without wrapping. Returns false at an end.</summary>
    public bool StepYear(int delta)
    {
        int year = Year + Math.Sign(delta);
        if (year < 1 || year > ScribeDate.MaxYear) return false;
        Year = year;
        Day = Math.Min(Day, DaysInCurrentMonth);
        return true;
    }

    private int DaysInCurrentMonth => Math.Min(shape.DaysInMonth(Year, Month), ScribeDate.MaxDay);
}
