namespace Scribe.Core;

/// <summary>
/// The stable string tokens the human-readable codecs (<see cref="ScribeDocumentJsonCodec"/> and
/// <see cref="ScribeDocumentTsvCodec"/>) use to name a <see cref="ScribeBlockKind"/>, kept in one place so
/// the two export lanes can never drift. The tokens are part of the shared/exported format contract:
/// they are ADDITIVE (a new kind adds a new token) and MUST stay stable, exactly like the byte-enum values.
///
/// <para>The token for <see cref="ScribeBlockKind.Text"/> is <c>"note"</c> — the player-facing name for a
/// free-text section — not "text", so an exported table reads the way the UI labels it.</para>
///
/// <para>The TSV lane also uses the reserved row type <see cref="TitleRow"/> ("title"), which is NOT a block
/// kind — it carries the document title as a leading row (see <see cref="ScribeDocumentTsvCodec"/>). It is
/// intentionally absent from <see cref="TryParse"/> so a body row typed "title" never becomes a block.</para>
/// </summary>
public static class ScribeBlockKindToken
{
    /// <summary>Reserved TSV row type carrying the document title (not a <see cref="ScribeBlockKind"/>).</summary>
    public const string TitleRow = "title";

    /// <summary>The stable export token for a block kind. Additive and stable — never renumber/rename.</summary>
    public static string ToToken(ScribeBlockKind kind) => kind switch
    {
        ScribeBlockKind.Task => "task",
        ScribeBlockKind.Text => "note",
        ScribeBlockKind.Tracker => "tracker",
        ScribeBlockKind.Link => "link",
        ScribeBlockKind.Craft => "craft",
        _ => "task", // future kinds without a token here export as the safe baseline
    };

    /// <summary>
    /// Parses an export token back to a <see cref="ScribeBlockKind"/>. Case-insensitive and
    /// whitespace-trimmed. Returns false for an unknown/blank token OR the reserved <see cref="TitleRow"/>
    /// type, so callers can degrade an unrecognized row to a plain Task (the loose-import tenet) while
    /// still treating a title row specially.
    /// </summary>
    public static bool TryParse(string? token, out ScribeBlockKind kind)
    {
        kind = ScribeBlockKind.Task;
        if (string.IsNullOrWhiteSpace(token)) return false;
        switch (token.Trim().ToLowerInvariant())
        {
            case "task": kind = ScribeBlockKind.Task; return true;
            case "note": kind = ScribeBlockKind.Text; return true;
            case "tracker": kind = ScribeBlockKind.Tracker; return true;
            case "link": kind = ScribeBlockKind.Link; return true;
            case "craft": kind = ScribeBlockKind.Craft; return true;
            default: return false;
        }
    }

    /// <summary>True when <paramref name="token"/> is the reserved title-row type (case-insensitive).</summary>
    public static bool IsTitleRow(string? token)
        => token is not null && token.Trim().Equals(TitleRow, System.StringComparison.OrdinalIgnoreCase);
}
