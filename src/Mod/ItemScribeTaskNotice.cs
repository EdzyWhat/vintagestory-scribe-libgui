using System.Text;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// The Task Notice item (`task-notice-item` capability) — a physical, hand-carried delivery path for an
/// out-of-range or offline assignment target. A BLANK notice carries no <see cref="ScribeDocument"/> (a
/// plain, stackable crafting-supply item, per <see cref="ScribeDocumentAttributes"/>'s existing
/// attribute-presence convention); a SEALED notice's document holds one <see cref="ScribeBlock"/> per
/// assigned row from the send that populated it (each carrying its own <see cref="ScribeAssignment"/> in
/// the Unaccepted state — the physical item IS the pending record, per the `assignment-state-machine`
/// capability's notice-originated-lifecycle requirement), and never stacks (its document bytes are
/// unique per stack, so the engine's own attribute-equality check already refuses to merge it with any
/// other stack — no override needed, exactly like every other Scribe document item).
///
/// <para>Deliberately NOT writeable (<see cref="IsSlotWriteable"/> always false): a Task Notice is never
/// a valid Accept-placement target (<c>ScribeAcceptCandidates</c> filters on writeability) nor a Handbook
/// "Add to Scribe" append target — it is the SOURCE of a pending assignment, never a destination.</para>
/// </summary>
public sealed class ItemScribeTaskNotice : Item, IScribeDocumentItem
{
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

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        => IsSealed(inSlot.Itemstack) ? _interactions.Append(base.GetHeldInteractionHelp(inSlot)) : base.GetHeldInteractionHelp(inSlot);

    private static bool IsSealed(ItemStack? stack) =>
        stack is not null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null && doc.Blocks.Count > 0;

    /// <summary>Append the notice's status to the held/inventory tooltip: a blank notice gets an explicit
    /// "unassigned" indicator (it's otherwise indistinguishable from a sealed one at a glance in a
    /// stack/slot), a sealed notice gets its recipient.</summary>
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
        string recipientName = world.PlayerByUid(assignment.TargetPlayerUid)?.PlayerName
            ?? assignment.TargetPlayerUid;
        dsc.AppendLine(Lang.Get("scribe:scribe-tasknotice-addressed-to", recipientName));
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!firstEvent) return;
        // A blank notice has nothing to review — fall through to base (ground placement etc.), matching
        // every other Scribe item's Ctrl+Shift convention. Only a sealed notice opens the review dialog.
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

    /// <summary>Handbook "Add to Scribe" fallback entry point (<see cref="IScribeDocumentItem"/>). In
    /// practice never reached — <see cref="IsSlotWriteable"/> is always false, so the Handbook resolver
    /// skips this item before it would call here — but implemented for interface completeness/consistency
    /// with every other Scribe document item.</summary>
    public ScribeDialogBase? OpenScribeDialog(ItemSlot slot, ICoreClientAPI capi) => null;

    /// <summary>A Task Notice is never an Accept-placement or Handbook-append target — it is the SOURCE
    /// of a pending assignment, never a destination (see class remarks).</summary>
    public bool IsSlotWriteable(ItemSlot slot) => false;

    private void OpenTaskNoticeDialog(ItemSlot slot, ICoreClientAPI capi)
    {
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        var dialog = new GuiDialogTaskNotice(slot, capi, modSystem);
        dialog.TryOpen();
    }
}
