using System.Globalization;

namespace Scribe.Core;

/// <summary>
/// A calendar date as the player wrote it: a year counted from 1 (Year 1 is the world's first calendar
/// year), a month from 1 to 12, and a day of the month from 1. Stored as written, never as a day count, so
/// the label stays the same if the world's days per month changes. A day past its month's end is valid and
/// is clamped only when shown or used in arithmetic (<see cref="IScribeCalendarShape.Clamp"/>).
///
/// <para>Build one through <see cref="TryCreate"/> (or <see cref="Create"/> when the parts are known to be
/// valid); every codec reads dates through <see cref="TryCreate"/>, so an out-of-range stored value becomes
/// "no date" instead of a broken one.</para>
/// </summary>
public readonly record struct ScribeDate
{
    /// <summary>Highest accepted year. Keeps year arithmetic far from overflow.</summary>
    public const int MaxYear = 9999;

    /// <summary>Months per year. Vintage Story's calendar always has 12.</summary>
    public const int MonthsPerYear = 12;

    /// <summary>Highest accepted stored day. The binary save holds the day in one byte; days past the
    /// month's real end are clamped at use.</summary>
    public const int MaxDay = byte.MaxValue;

    public int Year { get; }
    public int Month { get; }
    public int Day { get; }

    private ScribeDate(int year, int month, int day)
    {
        Year = year;
        Month = month;
        Day = day;
    }

    /// <summary>True when the parts form an accepted date: year 1..<see cref="MaxYear"/>, month
    /// 1..12, day 1..<see cref="MaxDay"/>.</summary>
    public static bool TryCreate(int year, int month, int day, out ScribeDate date)
    {
        if (year < 1 || year > MaxYear || month < 1 || month > MonthsPerYear || day < 1 || day > MaxDay)
        {
            date = default;
            return false;
        }
        date = new ScribeDate(year, month, day);
        return true;
    }

    /// <summary>Builds a date from parts that must be valid; throws <see cref="ArgumentOutOfRangeException"/>
    /// otherwise.</summary>
    public static ScribeDate Create(int year, int month, int day) =>
        TryCreate(year, month, day, out var date)
            ? date
            : throw new ArgumentOutOfRangeException(nameof(year), $"Not a valid Scribe date: {year}-{month}-{day}");

    /// <summary>Parses the export text form <c>Y-M-D</c> (for example <c>3-5-25</c>). Returns false for
    /// anything malformed or out of range.</summary>
    public static bool TryParse(string? text, out ScribeDate date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('-');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int year)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int month)) return false;
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int day)) return false;
        return TryCreate(year, month, day, out date);
    }

    /// <summary>The export text form <c>Y-M-D</c>, read back by <see cref="TryParse"/>.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Year}-{Month}-{Day}");
}
