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
