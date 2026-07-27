using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// The lectern block. Stays thin — all interaction/document logic lives on
/// <see cref="BlockEntityScribeLectern"/>, mirroring the vanilla Sign block/block-entity split.
/// </summary>
public sealed class BlockScribeLectern : Block
{
    private WorldInteraction[] interactions = System.Array.Empty<WorldInteraction>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api.Side != EnumAppSide.Client) return;

        // Two hints, one per view mode -- matches the tooltip pattern vanilla containers use
        // (e.g. BlockLabeledChest's "shift+right-click: write" hint) so looking at a lectern
        // explains both interactions, not just "Lectern".
        interactions = ObjectCacheUtil.GetOrCreate(api, "scribeLecternBlockInteractions", () => new WorldInteraction[]
        {
            new WorldInteraction
            {
                ActionLangCode = "scribe:blockhelp-scribelectern-open",
                MouseButton = EnumMouseButton.Right,
            },
            new WorldInteraction
            {
                ActionLangCode = "scribe:blockhelp-scribelectern-edit",
                HotKeyCode = "shift",
                MouseButton = EnumMouseButton.Right,
            },
        });
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityScribeLectern lectern)
        {
            bool wantEditor = byPlayer.Entity?.Controls?.ShiftKey == true;
            lectern.OnRightClick(byPlayer, wantEditor);
        }
        return true;
    }

    /// <summary>
    /// Floor-only placement: the lectern is a standing piece of furniture, so it may only be placed on a
    /// solid ground surface — never a wall or ceiling. We check the cell directly BELOW the placement for
    /// a solid up-face (via <see cref="Block.CanAttachBlockAt"/>, the same test vanilla furniture's
    /// <c>UnstableFalling</c> behavior uses), rather than testing which face was clicked — a player can
    /// click a side face yet still be placing onto a valid floor cell. We deliberately do NOT use the
    /// <c>UnstableFalling</c> behavior, which would also turn the lectern into a falling physics entity
    /// when its support is mined — undesirable for a block that stores a document and resolves pins by
    /// position. <c>base.TryPlaceBlock</c> invokes this before placing, so a rejection here also
    /// short-circuits the orientation logic below. The failure toast reuses the vanilla lang key
    /// <c>placefailure-requiresolidground</c> ("Cannot place this block here. Requires a solid ground").
    /// </summary>
    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

        var posBelow = blockSel.Position.DownCopy();
        var blockBelow = world.BlockAccessor.GetBlock(posBelow);
        if (!blockBelow.CanAttachBlockAt(world.BlockAccessor, this, posBelow, BlockFacing.UP))
        {
            failureCode = "requiresolidground";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Place the lectern, then turn it to face the player — the vanilla Sign/clutter idiom. We compute
    /// the horizontal angle from the player toward the block and snap it to 22.5° steps (matching the
    /// vanilla <c>BlockClutter</c> that authored this shape), then store it as the block entity's
    /// <see cref="BlockEntityScribeLectern.MeshAngleRad"/>. The reused <c>bookshelves/lecturn-book-open</c>
    /// shape's authored front is SOUTH (+Z) at angle 0, so this raw angle points the open book at the
    /// player with no extra offset (see VSAPI-NOTES "Block placement orientation").
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode)) return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityScribeLectern lectern
            && byPlayer?.Entity is { } entity)
        {
            var placedPos = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position;
            double dz = entity.Pos.X - (placedPos.X + blockSel.HitPosition.X);
            double dx = entity.Pos.Z - (placedPos.Z + blockSel.HitPosition.Z);
            float angle = (float)System.Math.Atan2(dz, dx);
            const float snap = (float)System.Math.PI / 8f; // 22.5°, matching vanilla clutter
            lectern.MeshAngleRad = (float)System.Math.Round(angle / snap) * snap;
        }

        return true;
    }

    /// <summary>Surface the block entity's rotated collision/selection box so the hitbox tracks the
    /// placed facing (vanilla Sign pattern); falls back to the JSON box before an angle is set.</summary>
    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is BlockEntityScribeLectern { RotatedBox: { } box }) return box;
        return base.GetCollisionBoxes(blockAccessor, pos);
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is BlockEntityScribeLectern { RotatedBox: { } box }) return box;
        return base.GetSelectionBoxes(blockAccessor, pos);
    }

    /// <summary>
    /// Carry the lectern's document (with its stable ids) onto the item produced when the block is
    /// broken, so a break→re-place round-trip restores the same content and pins keep resolving. Both
    /// drop paths run while the block entity is still alive (before RemoveBlockEntity), so the document
    /// is readable here. Precedent: vanilla clutter-book / falling-block "carry BE data onto the item".
    /// Content is only truly lost if the dropped item despawns.
    /// </summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityScribeLectern lectern)
        {
            foreach (var drop in drops) ScribeDocumentAttributes.WriteTo(drop, lectern.Document);
        }
        return drops;
    }

    /// <summary>Middle-click pick: the picked stack also carries the current document, so picking and
    /// re-placing (creative) preserves content and ids just like break→re-place.</summary>
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var stack = base.OnPickBlock(world, pos);
        // base can return null (nothing to pick); the API declares the return non-nullable, so match
        // that and only stamp a real stack.
        if (stack is not null && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityScribeLectern lectern)
        {
            ScribeDocumentAttributes.WriteTo(stack, lectern.Document);
        }
        return stack!;
    }
}
