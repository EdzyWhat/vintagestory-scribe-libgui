namespace Scribe;

/// <summary>
/// The standalone Inbox block (add-assignment-and-quest-support §6) — a thin subclass of
/// <see cref="BlockScribeWritingStation"/>, mirroring <see cref="BlockScriptorium"/>/
/// <see cref="BlockAssignmentDesk"/>: all placement, interaction, tooltip, and document carry-over logic
/// is shared with the Lectern/Scriptorium. Supplies only its own interaction-hint lang keys and their
/// cache key.
/// </summary>
public sealed class BlockInbox : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeInboxBlockInteractions";

    protected override string OpenHintLangCode => "scribe:blockhelp-scribeinbox-open";

    protected override string EditHintLangCode => "scribe:blockhelp-scribeinbox-edit";
}
