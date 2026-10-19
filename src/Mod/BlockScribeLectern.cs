namespace Scribe;

/// <summary>
/// The Lectern block, a thin subclass of <see cref="BlockScribeWritingStation"/> supplying only its
/// interaction-hint lang keys and cache key.
/// </summary>
public sealed class BlockScribeLectern : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeLecternBlockInteractions";

    // Right-click opens Guest Book; the hint reuses that tab's title lang key.
    protected override string OpenHintLangCode => "scribe:scribe-tab-guestbook";

    protected override string EditHintLangCode => "scribe:blockhelp-scribelectern-edit";
}
