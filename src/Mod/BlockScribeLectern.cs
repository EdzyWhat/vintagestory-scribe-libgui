namespace Scribe;

/// <summary>
/// The Lectern block. A thin subclass of <see cref="BlockScribeWritingStation"/>: all placement,
/// interaction, tooltip, and document carry-over logic lives in the base (shared with the Scriptorium).
/// The Lectern supplies only its own interaction-hint lang keys and their cache key.
/// </summary>
public sealed class BlockScribeLectern : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeLecternBlockInteractions";

    // Right-click now opens Guest Book (assignment-icon-and-tab-defaults D6/D7) — the help text reuses
    // Guest Book's own tab-title lang key (self-match) rather than a bespoke "open" string, so it
    // inherits multi-language support automatically.
    protected override string OpenHintLangCode => "scribe:scribe-tab-guestbook";

    protected override string EditHintLangCode => "scribe:blockhelp-scribelectern-edit";
}
