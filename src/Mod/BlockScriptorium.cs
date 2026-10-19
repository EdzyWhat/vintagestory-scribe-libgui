namespace Scribe;

/// <summary>
/// The Scriptorium block — a placed writing station. A thin subclass of
/// <see cref="BlockScribeWritingStation"/>: all placement, interaction, tooltip, and document
/// carry-over logic is shared with the Lectern. The Scriptorium supplies only its own
/// interaction-hint lang keys and their cache key.
/// </summary>
public sealed class BlockScriptorium : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeScriptoriumBlockInteractions";

    // Right-click opens Transcribe — the help text reuses Transcribe's own tab-title lang key
    // rather than a bespoke "open" string.
    protected override string OpenHintLangCode => "scribe:scribe-tab-transcribe";

    protected override string EditHintLangCode => "scribe:blockhelp-scriptorium-edit";
}
