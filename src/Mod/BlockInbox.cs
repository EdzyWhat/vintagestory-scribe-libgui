using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The standalone Inbox block, a thin subclass of <see cref="BlockScribeWritingStation"/> (like
/// <see cref="BlockScriptorium"/>/<see cref="BlockAssignmentDesk"/>). Unlike the ground-only stations and
/// the wall-only Chalkboard, it supports BOTH placement modes from one item (see <see cref="TryPlaceBlock"/>).
/// </summary>
public sealed class BlockInbox : BlockScribeWritingStation
{
    protected override string InteractionsCacheKey => "scribeInboxBlockInteractions";

    protected override string OpenHintLangCode => "scribe:blockhelp-scribeinbox-open";

    protected override string EditHintLangCode => "scribe:blockhelp-scribeinbox-edit";

    /// <summary>The Inbox has no renamable document — see the base's doc comment.</summary>
    protected override bool ShowsDocumentTitleInTooltip => false;

    /// <summary>Only ground placement (a top-face click) needs a solid floor; wall placement is validated
    /// by the wall-attach check in <see cref="TryPlaceBlock"/>.</summary>
    protected override bool RequiresSolidGround(BlockSelection blockSel) => blockSel.Face == BlockFacing.UP;

    /// <summary>
    /// Dual-mode placement. A top-face click delegates to the base's floor check + player-facing rotation.
    /// A horizontal-face click mirrors the vanilla torch's <c>BlockGroundAndSideAttachable.TryAttachTo</c>:
    /// this hierarchy does NOT inherit automatic face→variant resolution (<c>Block.TryPlaceBlock</c> always
    /// places `this` block's own id), so it checks for a solid block behind the face and places the matching
    /// "-&lt;direction&gt;" wall variant instead of this item's own ("-up") block id.
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

    /// <summary>Picking from either placement mode yields the same ("-up") Inbox item, as vanilla's torch
    /// does. Reimplements the base's document carry-over (<see cref="BlockScribeWritingStation.GetDrops"/>)
    /// against the resolved "-up" block, since <c>this</c> may be a wall variant.</summary>
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
