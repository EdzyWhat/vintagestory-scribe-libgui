namespace Scribe;

/// <summary>
/// The Assignment Desk block — a thin subclass of
/// <see cref="BlockScribeWritingStation"/>, mirroring <see cref="BlockScriptorium"/>: all placement,
/// interaction, tooltip, and document carry-over logic is shared with the Lectern/Scriptorium. Supplies
/// only its own interaction-hint lang keys and their cache key.
/// </summary>
public sealed class BlockAssignmentDesk : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeAssignmentDeskBlockInteractions";

    protected override string OpenHintLangCode => "scribe:blockhelp-scribeassignmentdesk-open";

    protected override string EditHintLangCode => "scribe:blockhelp-scribeassignmentdesk-edit";

    /// <summary>The Assignment Desk has no renamable document — see the base's doc comment.</summary>
    protected override bool ShowsDocumentTitleInTooltip => false;
}
