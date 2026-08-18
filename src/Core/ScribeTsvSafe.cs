using System.Text;

namespace Scribe.Core;

/// <summary>
/// Field-level escaping for the TSV export/import lane (<see cref="ScribeDocumentTsvCodec"/>). Two concerns,
/// both reversible so a round-trip is exact:
///
/// <list type="number">
/// <item><b>Structural escaping.</b> A field containing a tab, CR, LF, a double-quote, or leading/trailing
/// whitespace is wrapped in double quotes with any internal quote doubled (RFC-4180 style, which Excel and
/// Google Sheets honor for tab-separated clipboard data too). This lets a task's text hold interior line
/// breaks and tabs without breaking the table grid.</item>
/// <item><b>Formula-injection defang.</b> Spreadsheets execute a cell whose text begins with <c>=</c>,
/// <c>+</c>, <c>-</c>, or <c>@</c> as a formula. On export such a field is prefixed with a single apostrophe
/// (<c>'</c>) — the standard defang — so a pasted cell is inert text, never a live formula. To keep the
/// round-trip exact a field that ALREADY begins with an apostrophe is also prefixed, so the import-side
/// single-apostrophe strip restores the original unambiguously.</item>
/// </list>
///
/// <para>Pure string logic, no VS API — the codec that uses it stays unit-testable in Core.</para>
/// </summary>
public static class ScribeTsvSafe
{
    // The characters a spreadsheet treats as the start of a formula. A field leading with one of these is
    // defanged on export. Apostrophe is added to the trigger set purely for reversibility (see class doc).
    private const string FormulaLeads = "=+-@";

    /// <summary>Escapes one field for writing into a TSV cell: formula-defang first (content level), then
    /// structural quoting (grid level). The two are independent — defang only prepends an apostrophe, which
    /// never itself triggers structural quoting.</summary>
    public static string Escape(string? field)
    {
        string s = field ?? string.Empty;

        // 1) Formula-injection defang: prefix an apostrophe when the field would start a formula, or when it
        //    already starts with an apostrophe (so the import-side single strip is unambiguous).
        if (s.Length > 0 && (FormulaLeads.IndexOf(s[0]) >= 0 || s[0] == '\''))
            s = "'" + s;

        // 2) Structural quoting: wrap when the field carries a delimiter/newline/quote or edge whitespace.
        if (NeedsQuoting(s))
            s = "\"" + s.Replace("\"", "\"\"") + "\"";

        return s;
    }

    /// <summary>Reverses <see cref="Escape"/>: unwrap structural quoting first, then strip a single leading
    /// defang apostrophe if present. Exact inverse of export.</summary>
    public static string Unescape(string? field)
    {
        string s = field ?? string.Empty;

        // 1) Structural unquote.
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1].Replace("\"\"", "\"");

        // 2) Un-defang: strip exactly one leading apostrophe (export adds exactly one, always, for any field
        //    that led with a formula char or an apostrophe).
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
