using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// The Chalkboard block, a thin subclass of <see cref="BlockScribeWritingStation"/> that hangs on a wall
/// like a vanilla painting. Wall attachment and facing come from the <c>HorizontalAttachable</c> behavior
/// and the <c>side</c> variant in <c>chalkboard.json</c>; this class only opts out of the base's floor
/// requirement and player-facing mesh rotation.
/// </summary>
public sealed class BlockScribeChalkboard : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeChalkboardBlockInteractions";

    protected override string OpenHintLangCode => "scribe:blockhelp-scribechalkboard-open";

    protected override string EditHintLangCode => "scribe:blockhelp-scribechalkboard-edit";

    /// <summary>Wall-mounted: no floor cell required (HorizontalAttachable checks the wall instead).</summary>
    protected override bool RequiresSolidGround(BlockSelection blockSel) => false;

    /// <summary>Wall-mounted: facing comes from the `side` variant + shape rotateYByType, not a stored
    /// per-instance mesh angle.</summary>
    protected override bool OrientTowardPlayerOnPlace => false;
}
