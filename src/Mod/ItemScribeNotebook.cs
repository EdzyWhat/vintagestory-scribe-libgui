using System.Text;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// A player-carried Notebook item. Right-click opens the Scribe dialog for the document stored in this
/// ItemStack. No Guestbook tab; owner-only.
/// </summary>
public class ItemScribeNotebook : Item, IScribeDocumentItem
{
    private WorldInteraction[] _interactions = System.Array.Empty<WorldInteraction>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        MaxStackSize = 1;

        if (api.Side != EnumAppSide.Client) return;
        _interactions = ObjectCacheUtil.GetOrCreate(api, "scribeNotebookInteractions", () => new WorldInteraction[]
        {
            new WorldInteraction
            {
                ActionLangCode = "scribe:itemhelp-scribenotebook-open",
                MouseButton = EnumMouseButton.Right,
            },
            new WorldInteraction
            {
                ActionLangCode = "scribe:itemhelp-scribenotebook-quickadd",
                HotKeyCode = "shift",
                MouseButton = EnumMouseButton.Right,
            },
            // The Ctrl+Shift "place on ground" hint comes from the base GroundStorable behavior.
        });
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        => _interactions.Append(base.GetHeldInteractionHelp(inSlot));

    /// <summary>Appends the stored document's title (or an untitled placeholder) to the tooltip. A never-opened
    /// notebook has no document attribute, so the placeholder shows.</summary>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        string? title = inSlot.Itemstack is { } stack
            && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null
            ? doc.Title
            : null;
        dsc.AppendLine(ScribeTooltip.FormatTitleLine(title));
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!firstEvent) return;
        // Ctrl+Shift+right-click: ground placement via base. The base ground-storable gate keys on ShiftKey
        // ONLY, so requiring both modifiers keeps a Shift-only press from double-acting with quick-add.
        if (byEntity.Controls.CtrlKey && byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        if (byEntity.Api.Side != EnumAppSide.Client) return;
        if (byEntity.Api is not ICoreClientAPI capi) return;

        // Shift+right-click (no Ctrl): quick-add with a fresh empty top task focused. Plain right-click opens Read.
        handling = EnumHandHandling.PreventDefault;
        OpenNotebookDialog(slot, capi, quickAdd: byEntity.Controls.ShiftKey);
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        if (api.Side != EnumAppSide.Server) return;
        if (outputSlot.Itemstack is null) return; // always set for a crafting output; guard is defensive

        var playerUid = outputSlot.Inventory.openedByPlayerGUIds.FirstOrDefault();
        var playerName = (playerUid is not null
            ? (api.World.PlayerByUid(playerUid) as IServerPlayer)?.PlayerName
            : null) ?? "Unknown";

        var sapi = (ICoreServerAPI)api;
        string date = NotebookHost.FormatDate(sapi);
        var history = HistoryStore.Deserialize(outputSlot.Itemstack.Attributes.GetBytes("scribeHistory"));
        history.TryAddEntry(new HistoryEntry
        {
            Kind            = HistoryEventKind.Crafted,
            ActorName       = playerName,
            InGameDate      = date,
            InGameTimestamp = sapi.World.Calendar.TotalDays,
        });
        outputSlot.Itemstack.Attributes.SetBytes("scribeHistory", history.Serialize());
    }

    /// <summary>Opens this Notebook's dialog for the Handbook "Add to Scribe" fallback, same path as right-click.</summary>
    public ScribeDialogBase? OpenScribeDialog(ItemSlot slot, ICoreClientAPI capi)
        => OpenNotebookDialog(slot, capi);

    private ScribeDialogBase OpenNotebookDialog(ItemSlot slot, ICoreClientAPI capi, bool quickAdd = false)
    {
        var host = new NotebookHost(slot);
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        modSystem.RegisterHost(host);
        // Tell the server we opened this notebook (it never sees client-only dialog opens) for the one-time PickedUp entry.
        modSystem.NotifyServerNotebookOpened(host.Document.DocId, slot);
        // Remember as the last-opened Scribe item so a later Handbook "Add to Scribe" click re-targets it.
        modSystem.NoteScribeItemDialogOpened(host.Document.DocId);

        var dialog = new GuiDialogScribeNotebook(host, capi);
        dialog.OnClosed += () => modSystem.UnregisterHost(host.Document.DocId);
        dialog.TryOpen();
        // Quick-add: the Notebook grants editor access immediately (no server lock); runs right after TryOpen builds the tree.
        if (quickAdd)
        {
            dialog.EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
            dialog.QuickAddTopTask();
        }
        return dialog;
    }
}
