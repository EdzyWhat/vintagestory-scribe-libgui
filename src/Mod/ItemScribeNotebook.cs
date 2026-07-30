using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Scribe;

/// <summary>
/// A player-carried Notebook item. Right-click opens the full Scribe dialog (Read / Editor /
/// Pinned / Settings) for the document stored in this specific item stack. No Guestbook tab.
/// Owner-only: only the player currently holding the item can edit it.
/// </summary>
public class ItemScribeNotebook : Item
{
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        MaxStackSize = 1;
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!firstEvent) return;
        if (byEntity.Api.Side != EnumAppSide.Client) return;
        if (byEntity.Api is not ICoreClientAPI capi) return;

        handling = EnumHandHandling.PreventDefault;
        OpenNotebookDialog(slot, capi);
    }

    private void OpenNotebookDialog(ItemSlot slot, ICoreClientAPI capi)
    {
        var host = new NotebookHost(slot);
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        modSystem.RegisterHost(host);

        var dialog = new GuiDialogScribeNotebook(host, capi);
        dialog.OnClosed += () => modSystem.UnregisterHost(host.Document.DocId);
        dialog.TryOpen();
    }
}
