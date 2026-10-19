namespace Scribe;

/// <summary>
/// The Scriptorium block, a thin subclass of <see cref="BlockScribeWritingStation"/> supplying only its
/// interaction-hint lang keys and cache key.
/// </summary>
public sealed class BlockScriptorium : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeScriptoriumBlockInteractions";

    // Right-click opens Transcribe; the hint reuses that tab's title lang key.
    protected override string OpenHintLangCode => "scribe:scribe-tab-transcribe";

    protected override string EditHintLangCode => "scribe:blockhelp-scriptorium-edit";
}
