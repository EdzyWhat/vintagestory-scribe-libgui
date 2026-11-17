namespace Scribe.Core;

/// <summary>
/// The order a player wants displayed dates in (<see cref="ScribePlayerSettings.DateOrder"/>). Persisted as
/// a number: values are explicit and must stay stable.
/// </summary>
public enum ScribeDateOrder : byte
{
    /// <summary>The language's own <c>date-format</c> template. The default.</summary>
    LanguageDefault = 0,

    /// <summary>Day, month, year.</summary>
    DayMonthYear = 1,

    /// <summary>Month, day, year.</summary>
    MonthDayYear = 2,

    /// <summary>Year, month, day.</summary>
    YearMonthDay = 3,
}

/// <summary>
/// Picks the lang template for a <see cref="ScribeDateOrder"/>. Every template takes the same arguments
/// (<c>{0}</c> = day, <c>{1}</c> = localized month name, <c>{2}</c> = year); only the template text sets the
/// order, so this only chooses the key.
/// </summary>
public static class ScribeDateText
{
    /// <summary>The template key (without the <c>scribe:</c> domain) for <paramref name="order"/>. Unknown
    /// values use the language default.</summary>
    public static string TemplateKey(ScribeDateOrder order) => order switch
    {
        ScribeDateOrder.DayMonthYear => "date-format-dmy",
        ScribeDateOrder.MonthDayYear => "date-format-mdy",
        ScribeDateOrder.YearMonthDay => "date-format-ymd",
        _ => "date-format",
    };
}
