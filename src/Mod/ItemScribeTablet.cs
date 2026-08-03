using System.Text;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// A player-carried tablet item — the early-game "scratch tier" writing surface. One class backs both
/// the clay and wax variants (the <c>material</c> variant axis in <c>itemtypes/scribetablet.json</c>);
/// they differ only in art and recipe, not behavior. Modeled on <see cref="ItemScribeNotebook"/>:
/// MaxStackSize 1, right-click to open (shift passes through to ground storage), a title tooltip line,
/// and a server-side <c>Crafted</c> history entry. NO stylus-in-offhand edit gate (explicitly deferred).
///
/// <para>The document persists on the ItemStack exactly like the notebook — pure reuse of
/// <see cref="ScribeDocumentAttributes"/> — via a <see cref="TabletHost"/> that caps the tier at 10 task
/// blocks and 1 pin. Right-click opens the bespoke <see cref="GuiDialogScribeTablet"/> (add-tablet-dialog,
/// Proposal C): an always-edit, no-tabs surface with the earthen theme, the clay/wax backdrop matching the
/// item's material, and a cuneiform title banner over the inherited editable task list.</para>
/// </summary>
public class ItemScribeTablet : Item, IScribeDocumentItem
{
    private WorldInteraction[] _interactions = System.Array.Empty<WorldInteraction>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        MaxStackSize = 1;

        if (api.Side != EnumAppSide.Client) return;
        _interactions = ObjectCacheUtil.GetOrCreate(api, "scribeTabletInteractions", () => new WorldInteraction[]
        {
            new WorldInteraction
            {
                ActionLangCode = "scribe:itemhelp-scribetablet-open",
                MouseButton = EnumMouseButton.Right,
            },
        });
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        => _interactions.Append(base.GetHeldInteractionHelp(inSlot));

    /// <summary>Append the stored document's title (quoted, or an untitled placeholder) to the
    /// held/inventory tooltip. A never-opened tablet carries no document attribute yet, so
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
        // Shift+right-click: let base run CollectibleBehaviors (including GroundStorable).
        if (byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        if (byEntity.Api.Side != EnumAppSide.Client) return;
        if (byEntity.Api is not ICoreClientAPI capi) return;

        handling = EnumHandHandling.PreventDefault;
        OpenTabletDialog(slot, capi);
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        if (api.Side != EnumAppSide.Server) return;

        // Resolve the crafting player from the inventory's opener set.
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

    /// <summary>Read the recorded clay type (<c>red</c>/<c>blue</c>/<c>fire</c>) from a tablet stack, or
    /// <c>null</c> when absent — a legacy/creative stack with no recorded type, which callers treat as red
    /// (clay-wax-tablet-item). Set at craft by the per-type recipe's output attributes; no ingredient
    /// sniffing needed. Wax tablets carry no <c>clayType</c>.</summary>
    public static string? ReadClayType(ItemStack? stack) =>
        stack?.Attributes.HasAttribute(ClayTypeAttributeKey) == true
            ? stack.Attributes.GetString(ClayTypeAttributeKey)
            : null;

    /// <summary>Read the recorded fired appearance from a tablet stack; absent defaults to <c>false</c>
    /// (soft). An APPEARANCE record only — nothing here fires a tablet (clay-wax-tablet-item).</summary>
    public static bool ReadFired(ItemStack? stack) =>
        stack?.Attributes.GetBool(FiredAttributeKey, false) ?? false;

    private const string ClayTypeAttributeKey = "clayType";
    private const string FiredAttributeKey = "fired";

    private void OpenTabletDialog(ItemSlot slot, ICoreClientAPI capi)
    {
        // The backdrop matches the tablet's material variant + recorded clay type + fired appearance — the
        // item and its dialog agree on the mapping through ScribeBackdrops.ForTablet (add-tablet-dialog D6,
        // add-tablet-clay-type-backdrops). Absent clayType/fired attributes default to red + soft.
        var stack = slot.Itemstack;
        var host = new TabletHost(slot,
            ScribeBackdrops.ForTablet(Variant["material"], ReadClayType(stack), ReadFired(stack)));
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        modSystem.RegisterHost(host);
        // Tell the server we opened this tablet so it can record the one-time PickedUp entry
        // (opening the dialog is client-only; the server never sees it otherwise).
        modSystem.NotifyServerNotebookOpened(host.Document.DocId);

        // The bespoke always-edit tablet dialog (Proposal C): earthen theme, no tabs, cuneiform title
        // banner over the inherited editable task list.
        var dialog = new GuiDialogScribeTablet(host, capi);
        dialog.OnClosed += () => modSystem.UnregisterHost(host.Document.DocId);
        dialog.TryOpen();
    }
}
