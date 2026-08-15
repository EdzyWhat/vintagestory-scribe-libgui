using System.Text;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// A player-carried Notebook item. Right-click opens the full Scribe dialog (Read / Editor /
/// Pinned / Settings) for the document stored in this specific item stack. No Guestbook tab.
/// Owner-only: only the player currently holding the item can edit it.
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
            // The Ctrl+Shift "place on ground" hint comes from the base GroundStorable behavior itself
            // (its JSON has ctrlKey:true), so we don't add a redundant one here.
        });
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        => _interactions.Append(base.GetHeldInteractionHelp(inSlot));

    /// <summary>Append the stored document's title (quoted, or an untitled placeholder) to the
    /// held/inventory tooltip. A never-opened notebook carries no document attribute yet, so
    /// <see cref="ScribeDocumentAttributes.TryReadFrom"/> returns false and the placeholder shows.</summary>
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
        // Ctrl+Shift+right-click: let base run CollectibleBehaviors (including GroundStorable) for ground
        // placement, following the vanilla spear convention (add-unified-quick-add-interaction). We verified
        // the base ground-storable gate keys on ShiftKey ONLY, so requiring BOTH modifiers here means a
        // Shift-only press never reaches it — no double-action with the quick-add branch below.
        if (byEntity.Controls.CtrlKey && byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        if (byEntity.Api.Side != EnumAppSide.Client) return;
        if (byEntity.Api is not ICoreClientAPI capi) return;

        // Shift+right-click (no Ctrl): unified quick-add — open the editor with a fresh empty task at the
        // top and the caret focused. Plain right-click opens Read.
        handling = EnumHandHandling.PreventDefault;
        OpenNotebookDialog(slot, capi, quickAdd: byEntity.Controls.ShiftKey);
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        if (api.Side != EnumAppSide.Server) return;
        if (outputSlot.Itemstack is null) return; // always set for a crafting output; guard is defensive

        // Resolve the crafting player from the inventory's opener set.
        var playerUid = outputSlot.Inventory.openedByPlayerGUIds.FirstOrDefault();
        var playerName = (playerUid is not null
            ? (api.World.PlayerByUid(playerUid) as IServerPlayer)?.PlayerName
            : null) ?? "Unknown";

        var sapi = (ICoreServerAPI)api;
        string date = NotebookHost.FormatDate(sapi);
        var history = HistoryStore.Deserialize(outputSlot.Itemstack.Attributes.GetBytes("scribeHistory"));
        history.TryAddEntry(new HistoryEntry
        {
            Kind       = HistoryEventKind.Crafted,
            ActorName  = playerName,
            InGameDate = date,
        });
        outputSlot.Itemstack.Attributes.SetBytes("scribeHistory", history.Serialize());
    }

    /// <summary>Open this carried Notebook's Scribe dialog for the Handbook "Add to Scribe" fallback
    /// (add-tracker-link-tasks 3.3). Delegates to the same <see cref="OpenNotebookDialog"/> the right-click
    /// path uses, so host registration, last-opened tracking, and the server pickup notice are identical.</summary>
    public ScribeDialogBase? OpenScribeDialog(ItemSlot slot, ICoreClientAPI capi)
        => OpenNotebookDialog(slot, capi);

    private ScribeDialogBase OpenNotebookDialog(ItemSlot slot, ICoreClientAPI capi, bool quickAdd = false)
    {
        var host = new NotebookHost(slot);
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        modSystem.RegisterHost(host);
        // Tell the server we opened this notebook so it can record the one-time PickedUp entry
        // (opening the dialog is client-only; the server never sees it otherwise).
        modSystem.NotifyServerNotebookOpened(host.Document.DocId);
        // Remember this as the last-opened Scribe item so a later Handbook "Add to Scribe" click with no
        // dialog open re-targets it rather than an arbitrary carried item (add-tracker-link-tasks 3.2).
        modSystem.NoteScribeItemDialogOpened(host.Document.DocId);

        var dialog = new GuiDialogScribeNotebook(host, capi);
        dialog.OnClosed += () => modSystem.UnregisterHost(host.Document.DocId);
        dialog.TryOpen();
        // Quick-add (Shift+right-click): the Notebook grants editor access immediately (no server lock),
        // so enter the editor and drop a fresh empty top task with the caret focused — right after TryOpen
        // has built the tree (add-unified-quick-add-interaction).
        if (quickAdd)
        {
            dialog.EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
            dialog.QuickAddTopTask();
        }
        return dialog;
    }
}
