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

    protected override string OpenHintLangCode => "scribe:blockhelp-scriptorium-open";

    protected override string EditHintLangCode => "scribe:blockhelp-scriptorium-edit";
}
