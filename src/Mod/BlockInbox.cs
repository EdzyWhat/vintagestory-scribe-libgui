using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The standalone Inbox block (add-assignment-and-quest-support §6) — a thin subclass of
/// <see cref="BlockScribeWritingStation"/>, mirroring <see cref="BlockScriptorium"/>/
/// <see cref="BlockAssignmentDesk"/>: interaction, tooltip, and document carry-over logic is shared with
/// the Lectern/Scriptorium. Placement is its own concern (see <see cref="TryPlaceBlock"/>): unlike every
/// other writing station (ground-only) or the Chalkboard (wall-only), the Inbox supports BOTH modes from
/// one item (redesign-inbox-block-placement-and-capacity).
/// </summary>
public sealed class BlockInbox : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeInboxBlockInteractions";

    protected override string OpenHintLangCode => "scribe:blockhelp-scribeinbox-open";

    protected override string EditHintLangCode => "scribe:blockhelp-scribeinbox-edit";

    /// <summary>The Inbox has no renamable document — see the base's doc comment (triage 2026-08-31).</summary>
    protected override bool ShowsDocumentTitleInTooltip => false;

    /// <summary>Only the ground placement mode (a click on a top face) needs a solid floor below; a
    /// wall-mounted placement (a horizontal face) is validated by its own wall-attach check in
    /// <see cref="TryPlaceBlock"/> instead.</summary>
    protected override bool RequiresSolidGround(BlockSelection blockSel) => blockSel.Face == BlockFacing.UP;

    /// <summary>
    /// Dual-mode placement. Ground placement (a click on a top face) is unchanged: delegates to the
    /// shared base's existing floor-check + player-facing rotation, same as the Lectern/Scriptorium/
    /// Assignment Desk. Wall-mounted placement (a click on a horizontal face) is new: mirrors the vanilla
    /// wood torch's <c>BlockGroundAndSideAttachable.TryAttachTo</c> — confirmed by reading both that class
    /// and the base <c>Block.TryPlaceBlock</c>/<c>DoPlaceBlock</c> that this class hierarchy does NOT
    /// inherit that automatic face→variant resolution (it always places `this` block's own id), so it
    /// needs its own override here (design.md Decision 1 / tasks.md 3.3) — checks for a solid block
    /// behind the clicked face, then places the matching "-&lt;direction&gt;" wall variant instead of this
    /// item's own ("-up") block id.
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (blockSel.Face == BlockFacing.UP)
        {
            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        if (!blockSel.Face.IsHorizontal)
        {
            failureCode = "requiresolidground";
            return false;
        }

        if (!CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

        var attachingPos = blockSel.Position.AddCopy(blockSel.Face.Opposite);
        if (!world.BlockAccessor.GetBlock(attachingPos).CanAttachBlockAt(world.BlockAccessor, this, attachingPos, blockSel.Face))
        {
            failureCode = "requireattachable";
            return false;
        }

        var wallBlock = world.BlockAccessor.GetBlock(CodeWithVariant("orientation", blockSel.Face.Code));
        world.BlockAccessor.SetBlock(wallBlock.BlockId, blockSel.Position, itemstack);
        return true;
    }

    /// <summary>Picking the block from either placement mode yields the same ("-up") Inbox item — mirrors
    /// <c>BlockGroundAndSideAttachable.GetDrops</c>'s identical fix-up for the vanilla torch, and the
    /// base's own document-carry-over (<see cref="BlockScribeWritingStation.GetDrops"/>) reimplemented
    /// here against the resolved "-up" block rather than <c>this</c>, since <c>this</c> may be a
    /// wall-mounted variant.</summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        var drop = new ItemStack(world.BlockAccessor.GetBlock(CodeWithVariant("orientation", "up")));
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityScribeWritingStation station)
        {
            ScribeDocumentAttributes.WriteTo(drop, station.Document);
        }
        return new[] { drop };
    }

    /// <summary>Middle-click pick: same "-up"-item normalization as <see cref="GetDrops"/>.</summary>
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var stack = new ItemStack(world.BlockAccessor.GetBlock(CodeWithVariant("orientation", "up")));
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityScribeWritingStation station)
        {
            ScribeDocumentAttributes.WriteTo(stack, station.Document);
        }
        return stack;
    }
}
