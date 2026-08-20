namespace Scribe;

/// <summary>
/// The Chalkboard block. A thin subclass of <see cref="BlockScribeWritingStation"/> — the same shared
/// placed document as the Lectern (lock, autosave, guestbook, all task kinds) — differing only in its
/// interaction-hint lang keys and, unlike the floor-standing Lectern/Scriptorium, in placement: the
/// chalkboard hangs on a wall like a vanilla painting. Wall attachment + the north/east/south/west
/// facing come from the <c>HorizontalAttachable</c> behavior and the <c>side</c> block variant declared
/// in <c>chalkboard.json</c>; here we only opt out of the base's floor requirement and player-facing
/// mesh rotation (add-chalkboard-block D6). Its distinct model/textures/theme/background live in the
/// asset JSON, <see cref="BlockEntityScribeChalkboard"/>, and <see cref="GuiDialogScribeChalkboard"/>.
/// </summary>
public sealed class BlockScribeChalkboard : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeChalkboardBlockInteractions";

    protected override string OpenHintLangCode => "scribe:blockhelp-scribechalkboard-open";

    protected override string EditHintLangCode => "scribe:blockhelp-scribechalkboard-edit";

    /// <summary>Wall-mounted: no floor cell required (HorizontalAttachable checks the wall instead).</summary>
    protected override bool RequiresSolidGround => false;

    /// <summary>Wall-mounted: facing comes from the `side` variant + shape rotateYByType, not a stored
    /// per-instance mesh angle.</summary>
    protected override bool OrientTowardPlayerOnPlace => false;
}
