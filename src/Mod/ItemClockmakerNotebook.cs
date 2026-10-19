using System.Text;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// A player-carried Clockmaker's Notebook item. Identical to <see cref="ItemScribeNotebook"/>
/// but opens <see cref="GuiDialogClockmakerNotebook"/>, which adds a Timer tab.
/// </summary>
public class ItemClockmakerNotebook : Item, IScribeDocumentItem
{
    private WorldInteraction[] _interactions = System.Array.Empty<WorldInteraction>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        MaxStackSize = 1;

        if (api.Side != EnumAppSide.Client) return;
        _interactions = ObjectCacheUtil.GetOrCreate(api, "scribeClockmakerNotebookInteractions", () => new WorldInteraction[]
        {
            new WorldInteraction
            {
                ActionLangCode = "scribe:itemhelp-scribeclockmakernotebook-open",
                MouseButton = EnumMouseButton.Right,
            },
            new WorldInteraction
            {
                ActionLangCode = "scribe:itemhelp-scribeclockmakernotebook-quickadd",
                HotKeyCode = "shift",
                MouseButton = EnumMouseButton.Right,
            },
            // The Ctrl+Shift "place on ground" hint comes from the base GroundStorable behavior.
        });
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        => _interactions.Append(base.GetHeldInteractionHelp(inSlot));

    /// <summary>Appends the stored document's title (or an untitled placeholder) to the tooltip.</summary>
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
        // Ctrl+Shift+right-click: ground placement via base. Requiring both modifiers keeps a Shift-only press off the base ground-storable gate (keys on ShiftKey only).
        if (byEntity.Controls.CtrlKey && byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        if (byEntity.Api.Side != EnumAppSide.Client) return;
        if (byEntity.Api is not ICoreClientAPI capi) return;

        // Shift+right-click (no Ctrl): quick-add. Plain right-click opens Read.
        handling = EnumHandHandling.PreventDefault;
        OpenNotebookDialog(slot, capi, quickAdd: byEntity.Controls.ShiftKey);
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        // Derive the side from the crafting grid's inventory, not this.api: under ImprovedHandbookRecipesFork's
        // static-caching bug this.api can hold the client API even when the server invokes this override.
        var craftApi = (outputSlot.Inventory as InventoryBase)?.Api;
        if (craftApi is not ICoreServerAPI sapi) return;
        if (outputSlot.Itemstack is null) return;

        // Carry the source Notebook's document + history onto the output so the upgrade keeps the same DocId,
        // tasks and chronicle. The source is found by its document attribute (robust to grid position); raw byte
        // copies preserve the DocId exactly. With no source document, NotebookHost initializes an empty one.
        var sourceStack = allInputSlots
            .Select(s => s.Itemstack)
            .FirstOrDefault(st => st is not null
                && st.Attributes.HasAttribute(ScribeDocumentAttributes.DocumentAttributeKey));
        if (sourceStack is not null)
        {
            var docBytes = sourceStack.Attributes.GetBytes(ScribeDocumentAttributes.DocumentAttributeKey);
            if (docBytes is not null)
                outputSlot.Itemstack.Attributes.SetBytes(ScribeDocumentAttributes.DocumentAttributeKey, docBytes);

            // Copy history BEFORE the "Crafted" stamp below so the entry appends onto the carried chronicle.
            var histBytes = sourceStack.Attributes.GetBytes("scribeHistory");
            if (histBytes is not null)
                outputSlot.Itemstack.Attributes.SetBytes("scribeHistory", histBytes);
        }

        var playerUid = outputSlot.Inventory.openedByPlayerGUIds.FirstOrDefault();
        var playerName = (playerUid is not null
            ? (sapi.World.PlayerByUid(playerUid) as IServerPlayer)?.PlayerName
            : null) ?? "Unknown";

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

    /// <summary>Opens this Clockmaker's Notebook's dialog for the Handbook "Add to Scribe" fallback.</summary>
    public ScribeDialogBase? OpenScribeDialog(ItemSlot slot, ICoreClientAPI capi)
        => OpenNotebookDialog(slot, capi);

    private ScribeDialogBase OpenNotebookDialog(ItemSlot slot, ICoreClientAPI capi, bool quickAdd = false)
    {
        var host = new NotebookHost(slot, ScribeBackdrops.ClockmakerPage);
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        modSystem.RegisterHost(host);
        // Tell the server we opened this notebook (it never sees client-only dialog opens) for the one-time PickedUp entry.
        modSystem.NotifyServerNotebookOpened(host.Document.DocId, slot);
        // Remember this as the last-opened Scribe item.
        modSystem.NoteScribeItemDialogOpened(host.Document.DocId);

        var dialog = new GuiDialogClockmakerNotebook(host, capi);
        dialog.OnClosed += () => modSystem.UnregisterHost(host.Document.DocId);
        dialog.TryOpen();
        // Quick-add: enter the editor immediately (no server lock) with a fresh empty top task focused.
        if (quickAdd)
        {
            dialog.EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
            dialog.QuickAddTopTask();
        }
        return dialog;
    }
}
