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
public class ItemClockmakerNotebook : Item
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
        });
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        => _interactions.Append(base.GetHeldInteractionHelp(inSlot));

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!firstEvent) return;
        if (byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        if (byEntity.Api.Side != EnumAppSide.Client) return;
        if (byEntity.Api is not ICoreClientAPI capi) return;

        handling = EnumHandHandling.PreventDefault;
        OpenNotebookDialog(slot, capi);
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        if (api.Side != EnumAppSide.Server) return;

        var playerUid = outputSlot.Inventory.openedByPlayerGUIds.FirstOrDefault();
        var playerName = (playerUid is not null
            ? (api.World.PlayerByUid(playerUid) as IServerPlayer)?.PlayerName
            : null) ?? "Unknown";

        var sapi = (ICoreServerAPI)api;
        var cal  = sapi.World.Calendar;
        int dayOfMonth = (int)(cal.TotalDays % cal.DaysPerMonth) + 1;
        string date = $"{dayOfMonth} {Vintagestory.API.Config.Lang.Get("month-" + cal.MonthName)}, Year {cal.Year}";
        var history = HistoryStore.Deserialize(outputSlot.Itemstack.Attributes.GetBytes("scribeHistory"));
        history.TryAddEntry(new HistoryEntry
        {
            Kind       = HistoryEventKind.Crafted,
            ActorName  = playerName,
            InGameDate = date,
        });
        outputSlot.Itemstack.Attributes.SetBytes("scribeHistory", history.Serialize());
    }

    private void OpenNotebookDialog(ItemSlot slot, ICoreClientAPI capi)
    {
        var host = new NotebookHost(slot);
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        modSystem.RegisterHost(host);

        var dialog = new GuiDialogClockmakerNotebook(host, capi);
        dialog.OnClosed += () => modSystem.UnregisterHost(host.Document.DocId);
        dialog.TryOpen();
    }
}
