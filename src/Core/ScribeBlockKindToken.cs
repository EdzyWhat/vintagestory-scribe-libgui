namespace Scribe.Core;

/// <summary>
/// The stable string tokens the JSON and TSV codecs use to name a <see cref="ScribeBlockKind"/>, kept in
/// one place so the two export lanes cannot drift. Part of the exported format contract: additive and
/// MUST stay stable.
///
/// <para><see cref="ScribeBlockKind.Text"/> is <c>"note"</c>, the player-facing name.</para>
///
/// <para>The TSV lane also uses the reserved row type <see cref="TitleRow"/>, which is not a block kind and
/// is deliberately absent from <see cref="TryParse"/> so a body row typed "title" never becomes a block.</para>
/// </summary>
public static class ScribeBlockKindToken
{
    /// <summary>Reserved TSV row type carrying the document title (not a <see cref="ScribeBlockKind"/>).</summary>
    public const string TitleRow = "title";

    /// <summary>The stable export token for a block kind.</summary>
    public static string ToToken(ScribeBlockKind kind) => kind switch
    {
        ScribeBlockKind.Task => "task",
        ScribeBlockKind.Text => "note",
        ScribeBlockKind.Tracker => "tracker",
        ScribeBlockKind.Link => "link",
        ScribeBlockKind.Craft => "craft",
        ScribeBlockKind.QuestObjective => "questobjective",
        _ => "task", // future kinds without a token here export as the safe baseline
    };

    /// <summary>
    /// Parses an export token (case-insensitive, trimmed). Returns false for an unknown/blank token or the
    /// reserved <see cref="TitleRow"/>, so callers can degrade an unrecognized row to a plain Task while
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
            case "questobjective": kind = ScribeBlockKind.QuestObjective; return true;
            default: return false;
        }
    }

    /// <summary>True when <paramref name="token"/> is the reserved title-row type (case-insensitive).</summary>
    public static bool IsTitleRow(string? token)
        => token is not null && token.Trim().Equals(TitleRow, System.StringComparison.OrdinalIgnoreCase);
}
