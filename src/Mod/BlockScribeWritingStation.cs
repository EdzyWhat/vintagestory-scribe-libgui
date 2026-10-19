using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// Shared base for Scribe's placed writing-station blocks (Lectern, Scriptorium, ...). Stays thin: all
/// interaction/document logic lives on <see cref="BlockEntityScribeWritingStation"/>, mirroring the vanilla
/// Sign block/block-entity split.
/// </summary>
public abstract class BlockScribeWritingStation : Block
{
    private WorldInteraction[] interactions = System.Array.Empty<WorldInteraction>();

    // ── Per-block config supplied by the concrete subclass ────────────────

    /// <summary>Object-cache key for this block's cached world-interaction hints; MUST be distinct per block type.</summary>
    protected abstract string InteractionsCacheKey { get; }

    /// <summary>Lang code for the plain right-click "open/read" hint.</summary>
    protected abstract string OpenHintLangCode { get; }

    /// <summary>Lang code for the shift+right-click "edit/quick-add" hint.</summary>
    protected abstract string EditHintLangCode { get; }

    /// <summary>Whether the placement needs a solid ground cell below it (default true). A wall-mounted
    /// variant overrides this to <c>false</c> so <see cref="CanPlaceBlock"/> skips the floor test and
    /// <c>HorizontalAttachable</c> can place it on a vertical face. Takes the selection because a dual-mode
    /// block (the Inbox) varies per placement attempt — see <see cref="BlockInbox"/>.</summary>
    protected virtual bool RequiresSolidGround(BlockSelection blockSel) => true;

    /// <summary>Whether placing rotates the block to face the player via
    /// <see cref="BlockEntityScribeWritingStation.MeshAngleRad"/> (default true). A wall-mounted variant
    /// overrides this to <c>false</c>: its facing comes from the <c>side</c> variant + the shape's
    /// <c>rotateYByType</c>, so no mesh angle is stored and <c>RotatedBox</c> stays null.</summary>
    protected virtual bool OrientTowardPlayerOnPlace => true;

    /// <summary>Whether the placed/held-item tooltip appends a "Title: ..." line (default true). The Inbox
    /// and Assignment Desk override this to <c>false</c>: they have no settable title, so the line would
    /// only ever read "(untitled)".</summary>
    protected virtual bool ShowsDocumentTitleInTooltip => true;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api.Side != EnumAppSide.Client) return;

        // Two hints, one per view mode, matching vanilla container tooltips (e.g. BlockLabeledChest).
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

    /// <summary>Appends the document's title (or an untitled placeholder) to the look-at tooltip, read live
    /// from the block entity; a missing block entity yields the placeholder.</summary>
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        string baseInfo = base.GetPlacedBlockInfo(world, pos, forPlayer);
        if (!ShowsDocumentTitleInTooltip) return baseInfo;

        string title = world.BlockAccessor.GetBlockEntity(pos) is BlockEntityScribeWritingStation station
            ? station.Document.Title
            : null!;
        return baseInfo + ScribeTooltip.FormatTitleLine(title) + "\n";
    }

    /// <summary>Appends the title to the held/inventory tooltip too. A picked-up station carries its document
    /// in the stack (see <see cref="GetDrops"/>); a freshly-crafted block shows the untitled placeholder.</summary>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (!ShowsDocumentTitleInTooltip) return;

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
            // Shift+right-click is the unified quick-add gesture: take the editor lock AND drop a fresh
            // empty task at the top with the caret focused. Plain right-click opens Read.
            bool quickAdd = byPlayer.Entity?.Controls?.ShiftKey == true;
            station.OnRightClick(byPlayer, wantEditor: quickAdd, quickAdd: quickAdd);
        }
        return true;
    }

    /// <summary>
    /// Floor-only placement: requires a solid up-face in the cell directly BELOW the placement (via
    /// <see cref="Block.CanAttachBlockAt"/>, as vanilla furniture's <c>UnstableFalling</c> does), not a check
    /// of the clicked face — a side-face click can still place onto a valid floor cell. Deliberately does NOT
    /// use <c>UnstableFalling</c>, which would turn the block into a falling entity when its support is mined.
    /// <c>base.TryPlaceBlock</c> invokes this first. The failure toast reuses the vanilla key
    /// <c>placefailure-requiresolidground</c>.
    /// </summary>
    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

        // A wall-mounted variant opts out (HorizontalAttachable handles it); a dual-mode block (the
        // Inbox) opts out only for a horizontal-face placement and keeps this floor check for ground mode.
        if (!RequiresSolidGround(blockSel)) return true;

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
    /// Places the block, then turns it to face the player (vanilla Sign/clutter idiom): the horizontal
    /// angle toward the player, snapped to 22.5° steps (as <c>BlockClutter</c>), stored as
    /// <see cref="BlockEntityScribeWritingStation.MeshAngleRad"/>. The shape's authored front is SOUTH (+Z)
    /// at angle 0, so no extra offset is needed (see VSAPI-NOTES "Block placement orientation").
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode)) return false;

        // A wall-mounted variant takes its facing from the `side` variant and stores no mesh angle.
        // Skipping also avoids reading a block entity at blockSel.Position that HorizontalAttachable
        // may have placed at an offset cell.
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

    /// <summary>Surfaces the block entity's rotated collision box so the hitbox tracks the facing; falls back to the JSON box.</summary>
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
    /// Carries the block's document (with its stable ids) onto the dropped item so break→re-place restores
    /// content and pins. Both drop paths run while the block entity is still alive (before RemoveBlockEntity).
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

    /// <summary>Middle-click pick: the picked stack also carries the current document, like break→re-place.</summary>
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var stack = base.OnPickBlock(world, pos);
        // base can return null (nothing to pick) though the API declares non-nullable; only stamp a real stack.
        if (stack is not null && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityScribeWritingStation station)
        {
            ScribeDocumentAttributes.WriteTo(stack, station.Document);
        }
        return stack!;
    }
}
