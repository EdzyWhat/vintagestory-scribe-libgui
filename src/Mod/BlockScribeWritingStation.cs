using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// Shared base for Scribe's placed writing-station blocks (the Lectern and the Scriptorium). Stays thin —
/// all interaction/document logic lives on <see cref="BlockEntityScribeWritingStation"/>, mirroring the
/// vanilla Sign block/block-entity split. Subclasses supply only their own interaction-hint lang keys and
/// the object-cache key for those hints.
/// </summary>
public abstract class BlockScribeWritingStation : Block
{
    private WorldInteraction[] interactions = System.Array.Empty<WorldInteraction>();

    // ── Per-block config supplied by the concrete subclass ────────────────

    /// <summary>Object-cache key for this block's cached world-interaction hints. MUST be distinct per
    /// block type so the Lectern and Scriptorium do not share a cache entry.</summary>
    protected abstract string InteractionsCacheKey { get; }

    /// <summary>Lang code for the plain right-click "open/read" hint.</summary>
    protected abstract string OpenHintLangCode { get; }

    /// <summary>Lang code for the shift+right-click "edit/quick-add" hint.</summary>
    protected abstract string EditHintLangCode { get; }

    /// <summary>Whether this block is standing furniture that requires a solid ground cell below it
    /// (the Lectern/Scriptorium default). A wall-mounted variant (the chalkboard) overrides this to
    /// <c>false</c> so <see cref="CanPlaceBlock"/> skips the below-floor test and lets a wall-attach
    /// behavior (vanilla <c>HorizontalAttachable</c>) place it against a vertical face instead.</summary>
    protected virtual bool RequiresSolidGround => true;

    /// <summary>Whether placing this block rotates it to face the player via
    /// <see cref="BlockEntityScribeWritingStation.MeshAngleRad"/> (the Lectern/Scriptorium default). A
    /// wall-mounted variant overrides this to <c>false</c>: its facing comes from the <c>side</c> block
    /// variant (+ <c>rotateYByType</c> on the shape), so no per-instance mesh angle is stored and the
    /// base's <c>RotatedBox</c> stays null (collision/selection fall back to the JSON boxes, which carry
    /// their own <c>rotateYByType</c>).</summary>
    protected virtual bool OrientTowardPlayerOnPlace => true;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api.Side != EnumAppSide.Client) return;

        // Two hints, one per view mode -- matches the tooltip pattern vanilla containers use
        // (e.g. BlockLabeledChest's "shift+right-click: write" hint) so looking at the block
        // explains both interactions, not just its name.
        interactions = ObjectCacheUtil.GetOrCreate(api, InteractionsCacheKey, () => new WorldInteraction[]
        {
            new WorldInteraction
            {
                ActionLangCode = OpenHintLangCode,
                MouseButton = EnumMouseButton.Right,
            },
            new WorldInteraction
            {
                ActionLangCode = EditHintLangCode,
                HotKeyCode = "shift",
                MouseButton = EnumMouseButton.Right,
            },
        });
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    /// <summary>Append the document's title (quoted, or an untitled placeholder) to the placed-block
    /// look-at tooltip, read live from the block entity's document. A missing block entity falls back
    /// to the placeholder via <see cref="ScribeTooltip.FormatTitleLine"/> (null → untitled).</summary>
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        string title = world.BlockAccessor.GetBlockEntity(pos) is BlockEntityScribeWritingStation station
            ? station.Document.Title
            : null!;
        return base.GetPlacedBlockInfo(world, pos, forPlayer) + ScribeTooltip.FormatTitleLine(title) + "\n";
    }

    /// <summary>Append the title to the block's held/inventory tooltip too, matching the Notebook items.
    /// A broken-and-picked-up writing station carries its document in the stack (see <see cref="GetDrops"/>
    /// / <see cref="OnPickBlock"/>), so the title reads via <see cref="ScribeDocumentAttributes.TryReadFrom"/>;
    /// a freshly-crafted block with no stored document shows the untitled placeholder.</summary>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        string? title = inSlot.Itemstack is { } stack
            && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null
            ? doc.Title
            : null;
        dsc.AppendLine(ScribeTooltip.FormatTitleLine(title));
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityScribeWritingStation station)
        {
            // Shift+right-click is the unified quick-add gesture (add-unified-quick-add-interaction):
            // take the editor lock AND drop a fresh empty task at the top with the caret focused. Plain
            // right-click opens Read; the plain-editor view is still reachable via the Editor nav tab.
            // (A placed writing station is furniture, so there is no Ctrl+Shift ground-place branch here —
            // that only applies to the carried Notebook/Tablet items.)
            bool quickAdd = byPlayer.Entity?.Controls?.ShiftKey == true;
            station.OnRightClick(byPlayer, wantEditor: quickAdd, quickAdd: quickAdd);
        }
        return true;
    }

    /// <summary>
    /// Floor-only placement: the block is a standing piece of furniture, so it may only be placed on a
    /// solid ground surface — never a wall or ceiling. We check the cell directly BELOW the placement for
    /// a solid up-face (via <see cref="Block.CanAttachBlockAt"/>, the same test vanilla furniture's
    /// <c>UnstableFalling</c> behavior uses), rather than testing which face was clicked — a player can
    /// click a side face yet still be placing onto a valid floor cell. We deliberately do NOT use the
    /// <c>UnstableFalling</c> behavior, which would also turn the block into a falling physics entity
    /// when its support is mined — undesirable for a block that stores a document and resolves pins by
    /// position. <c>base.TryPlaceBlock</c> invokes this before placing, so a rejection here also
    /// short-circuits the orientation logic below. The failure toast reuses the vanilla lang key
    /// <c>placefailure-requiresolidground</c> ("Cannot place this block here. Requires a solid ground").
    /// </summary>
    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

        // A wall-mounted variant (chalkboard) opts out of the floor requirement: its attach-to-wall
        // check is handled by the HorizontalAttachable behavior instead (add-chalkboard-block D6).
        if (!RequiresSolidGround) return true;

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
    /// Place the block, then turn it to face the player — the vanilla Sign/clutter idiom. We compute
    /// the horizontal angle from the player toward the block and snap it to 22.5° steps (matching the
    /// vanilla <c>BlockClutter</c> that authored these shapes), then store it as the block entity's
    /// <see cref="BlockEntityScribeWritingStation.MeshAngleRad"/>. The reused shape's authored front is
    /// SOUTH (+Z) at angle 0, so this raw angle points the reading face at the player with no extra
    /// offset (see VSAPI-NOTES "Block placement orientation").
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode)) return false;

        // A wall-mounted variant takes its facing from the `side` block variant + shape rotateYByType,
        // so it stores no per-instance mesh angle (add-chalkboard-block D6). Skipping this also avoids
        // reading a block entity at blockSel.Position that the HorizontalAttachable behavior may have
        // placed at an offset cell.
        if (!OrientTowardPlayerOnPlace) return true;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityScribeWritingStation station
            && byPlayer?.Entity is { } entity)
        {
            var placedPos = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position;
            double dz = entity.Pos.X - (placedPos.X + blockSel.HitPosition.X);
            double dx = entity.Pos.Z - (placedPos.Z + blockSel.HitPosition.Z);
            float angle = (float)System.Math.Atan2(dz, dx);
            const float snap = (float)System.Math.PI / 8f; // 22.5°, matching vanilla clutter
            station.MeshAngleRad = (float)System.Math.Round(angle / snap) * snap;
        }

        return true;
    }

    /// <summary>Surface the block entity's rotated collision/selection box so the hitbox tracks the
    /// placed facing (vanilla Sign pattern); falls back to the JSON box before an angle is set.</summary>
    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is BlockEntityScribeWritingStation { RotatedBox: { } box }) return box;
        return base.GetCollisionBoxes(blockAccessor, pos);
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is BlockEntityScribeWritingStation { RotatedSelectionBox: { } box }) return box;
        return base.GetSelectionBoxes(blockAccessor, pos);
    }

    /// <summary>
    /// Carry the block's document (with its stable ids) onto the item produced when the block is
    /// broken, so a break→re-place round-trip restores the same content and pins keep resolving. Both
    /// drop paths run while the block entity is still alive (before RemoveBlockEntity), so the document
    /// is readable here. Precedent: vanilla clutter-book / falling-block "carry BE data onto the item".
    /// Content is only truly lost if the dropped item despawns.
    /// </summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityScribeWritingStation station)
        {
            foreach (var drop in drops) ScribeDocumentAttributes.WriteTo(drop, station.Document);
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
        if (stack is not null && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityScribeWritingStation station)
        {
            ScribeDocumentAttributes.WriteTo(stack, station.Document);
        }
        return stack!;
    }
}
