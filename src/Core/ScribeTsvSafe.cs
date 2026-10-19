using System.Text;

namespace Scribe.Core;

/// <summary>
/// Field-level escaping for the TSV export/import lane (<see cref="ScribeDocumentTsvCodec"/>). Both
/// concerns are reversible so a round-trip is exact:
///
/// <list type="number">
/// <item><b>Structural escaping.</b> A field containing a tab, CR, LF, a double-quote, or leading/trailing
/// whitespace is wrapped in double quotes with internal quotes doubled (RFC-4180 style).</item>
/// <item><b>Formula-injection defang.</b> A field beginning with <c>=</c>, <c>+</c>, <c>-</c>, or <c>@</c>
/// gets a leading apostrophe so a spreadsheet treats it as text. A field that ALREADY begins with an
/// apostrophe is also prefixed, so import can strip exactly one apostrophe unambiguously.</item>
/// </list>
/// </summary>
public static class ScribeTsvSafe
{
    // Characters a spreadsheet treats as the start of a formula.
    private const string FormulaLeads = "=+-@";

    /// <summary>Escapes one field for a TSV cell: formula-defang first, then structural quoting. The two are
    /// independent; the defang apostrophe never itself triggers quoting.</summary>
    public static string Escape(string? field)
    {
        string s = field ?? string.Empty;

        // Defang formula leads and existing leading apostrophes.
        if (s.Length > 0 && (FormulaLeads.IndexOf(s[0]) >= 0 || s[0] == '\''))
            s = "'" + s;

        if (NeedsQuoting(s))
            s = "\"" + s.Replace("\"", "\"\"") + "\"";

        return s;
    }

    /// <summary>Exact inverse of <see cref="Escape"/>.</summary>
    public static string Unescape(string? field)
    {
        string s = field ?? string.Empty;

        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1].Replace("\"\"", "\"");

        // Export adds exactly one apostrophe, so strip exactly one.
        if (s.Length > 0 && s[0] == '\'')
            s = s[1..];

        return s;
    }

    private static bool NeedsQuoting(string s)
    {
        if (s.Length == 0) return false;
        if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])) return true;
        foreach (char c in s)
            if (c == '\t' || c == '\n' || c == '\r' || c == '"') return true;
        return false;
    }
}
