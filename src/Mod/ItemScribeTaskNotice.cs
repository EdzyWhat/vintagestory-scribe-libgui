using System.Text;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// The Task Notice item, a physical delivery path for an out-of-range or offline assignment target. A BLANK
/// notice carries no <see cref="ScribeDocument"/> (a plain stackable supply item); a SEALED notice holds one
/// <see cref="ScribeBlock"/> per assigned row, each in the Unaccepted state, and never stacks (its unique
/// document bytes already defeat the engine's attribute-equality merge). Never writeable: it is the SOURCE of
/// a pending assignment, never an Accept or Handbook-append destination.
/// </summary>
public sealed class ItemScribeTaskNotice : Item, IScribeDocumentItem
{
    private const string BlankMeshCacheKey = "scribeTaskNoticeBlankMeshRef";
    private const string FilledMeshCacheKey = "scribeTaskNoticeFilledMeshRef";
    private static readonly AssetLocation BlankShapeLoc = new("scribe", "item/tasknotice/blank");
    private static readonly AssetLocation FilledShapeLoc = new("scribe", "item/tasknotice/filled");

    private WorldInteraction[] _interactions = System.Array.Empty<WorldInteraction>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (api.Side != EnumAppSide.Client) return;
        _interactions = ObjectCacheUtil.GetOrCreate(api, "scribeTaskNoticeInteractions", () => new WorldInteraction[]
        {
            new WorldInteraction
            {
                ActionLangCode = "scribe:itemhelp-tasknotice-open",
                MouseButton = EnumMouseButton.Right,
            },
        });
    }

    /// <summary>Swaps the mesh between blank and sealed shapes based on <see cref="IsSealed"/>; each variant is
    /// tesselated once and cached.</summary>
    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target,
        ref ItemRenderInfo renderinfo)
    {
        bool sealedNotice = IsSealed(itemstack);
        string cacheKey = sealedNotice ? FilledMeshCacheKey : BlankMeshCacheKey;
        renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate(capi, cacheKey,
            () => TesselateVariant(capi, sealedNotice ? FilledShapeLoc : BlankShapeLoc));
        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
    }

    private MultiTextureMeshRef TesselateVariant(ICoreClientAPI capi, AssetLocation shapeLoc)
    {
        var shapeAssetLoc = shapeLoc.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
        var shape = capi.Assets.TryGet(shapeAssetLoc).ToObject<Shape>();
        capi.Tesselator.TesselateShape(this, shape, out var meshdata);
        return capi.Render.UploadMultiTextureMesh(meshdata);
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        DisposeCachedMeshRef(api, BlankMeshCacheKey);
        DisposeCachedMeshRef(api, FilledMeshCacheKey);
        base.OnUnloaded(api);
    }

    private static void DisposeCachedMeshRef(ICoreAPI api, string cacheKey) =>
        ObjectCacheUtil.TryGet<MultiTextureMeshRef>(api, cacheKey)?.Dispose();

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        => IsSealed(inSlot.Itemstack) ? _interactions.Append(base.GetHeldInteractionHelp(inSlot)) : base.GetHeldInteractionHelp(inSlot);

    /// <summary>Internal so <see cref="ScribeDocumentSlot.BuildSummaryCard"/> reuses it for its Task-Notice exception.</summary>
    internal static bool IsSealed(ItemStack? stack) =>
        stack is not null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null && doc.Blocks.Count > 0;

    /// <summary>Appends an "unassigned" line for a blank notice, or the recipient for a sealed one.</summary>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack is not { } stack) return;
        if (!IsSealed(stack))
        {
            dsc.AppendLine(Lang.Get("scribe:scribe-tasknotice-blank"));
            return;
        }
        if (!ScribeDocumentAttributes.TryReadFrom(stack, out var doc) || doc?.Blocks.Count is not > 0) return;

        var assignment = doc!.Blocks[0].Assignment;
        if (assignment is null) return;
        string recipientName = world.Api.ModLoader.GetModSystem<ScribeModSystem>().ResolvePlayerName(assignment.TargetPlayerUid);
        dsc.AppendLine(Lang.Get("scribe:scribe-tasknotice-addressed-to", recipientName));
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!firstEvent) return;
        // A blank notice has nothing to review: fall through to base (ground placement etc.).
        if (!IsSealed(slot.Itemstack))
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        if (byEntity.Api.Side != EnumAppSide.Client) return;
        if (byEntity.Api is not ICoreClientAPI capi) return;

        handling = EnumHandHandling.PreventDefault;
        OpenTaskNoticeDialog(slot, capi);
    }

    /// <summary>Never reached in practice (<see cref="IsSlotWriteable"/> is false, so the Handbook resolver skips this item).</summary>
    public ScribeDialogBase? OpenScribeDialog(ItemSlot slot, ICoreClientAPI capi) => null;

    /// <summary>Never writeable: a Task Notice is the SOURCE of a pending assignment, not a destination.</summary>
    public bool IsSlotWriteable(ItemSlot slot) => false;

    private void OpenTaskNoticeDialog(ItemSlot slot, ICoreClientAPI capi)
    {
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        var dialog = new GuiDialogTaskNotice(slot, capi, modSystem);
        dialog.TryOpen();
    }
}
