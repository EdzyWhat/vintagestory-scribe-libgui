namespace Scribe;

/// <summary>
/// The Scriptorium block — the v1.2 placed writing-station tier (see
/// <c>docs/specs/v7-scriptorium-and-task-types.md</c>). A thin subclass of
/// <see cref="BlockScribeWritingStation"/>: all placement, interaction, tooltip, and document
/// carry-over logic is shared with the Lectern. The Scriptorium supplies only its own
/// interaction-hint lang keys and their cache key.
/// </summary>
public sealed class BlockScriptorium : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeScriptoriumBlockInteractions";

    // Right-click now opens Transcribe (assignment-icon-and-tab-defaults D6/D7) — the help text reuses
    // Transcribe's own tab-title lang key (self-match) rather than a bespoke "open" string, so it
    // inherits multi-language support automatically.
    protected override string OpenHintLangCode => "scribe:scribe-tab-transcribe";

    protected override string EditHintLangCode => "scribe:blockhelp-scriptorium-edit";
}
