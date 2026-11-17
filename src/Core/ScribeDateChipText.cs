namespace Scribe.Core;

/// <summary>
/// The lang keys (without the <c>scribe:</c> domain) a date chip is built from. <see cref="StatusLangKey"/> is
/// the status wording and <see cref="Count"/> its number of days (0 when the wording has none); it is null for a
/// completed task, whose chip shows only the date. <see cref="TemplateLangKey"/> combines them with the
/// formatted date.
/// </summary>
public readonly record struct ScribeDateChipText(string? StatusLangKey, int Count, string TemplateLangKey)
{
    /// <summary>The chip text for a dated task's <paramref name="status"/>. Singular wording is used for exactly
    /// one day; every other count uses the "other" form.</summary>
    public static ScribeDateChipText For(ScribeDateStatus status) => status.Kind switch
    {
        ScribeDateStatusKind.Done => new ScribeDateChipText(null, 0, DoneTemplate),
        ScribeDateStatusKind.DueToday => new ScribeDateChipText("scribe-datestatus-today", 0, Template),
        ScribeDateStatusKind.Upcoming => new ScribeDateChipText(
            status.Days == 1 ? "scribe-datestatus-upcoming-one" : "scribe-datestatus-upcoming-other", status.Days, Template),
        _ => new ScribeDateChipText(
            status.Days == 1 ? "scribe-datestatus-overdue-one" : "scribe-datestatus-overdue-other", status.Days, Template),
    };

    private const string Template = "scribe-datechip";
    private const string DoneTemplate = "scribe-datechip-done";
}
