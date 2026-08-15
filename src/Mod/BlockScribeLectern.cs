namespace Scribe;

/// <summary>
/// The Lectern block. A thin subclass of <see cref="BlockScribeWritingStation"/>: all placement,
/// interaction, tooltip, and document carry-over logic lives in the base (shared with the Scriptorium).
/// The Lectern supplies only its own interaction-hint lang keys and their cache key.
/// </summary>
public sealed class BlockScribeLectern : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeLecternBlockInteractions";

    protected override string OpenHintLangCode => "scribe:blockhelp-scribelectern-open";

    protected override string EditHintLangCode => "scribe:blockhelp-scribelectern-edit";
}
