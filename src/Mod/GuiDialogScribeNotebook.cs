using System.Collections.Generic;
using System.Linq;
using Gui.Core.Layout;
using Gui.Rendering;
using Gui.Rendering.Text;
using Gui.Widgets.Basic;
using Gui.Widgets.Framework;
using Gui.Widgets.Layout;
using Gui.Widgets.Overlay;
using Gui.Widgets.Scroll;
using OpenTK.Mathematics;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Notebook item's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>.
/// Differences from the Lectern:
/// <list type="bullet">
/// <item>No Guestbook tab (no Visitors nav button).</item>
/// <item>History tab — a chronicle of significant events recorded automatically and manually.</item>
/// <item>Editor access is always granted without a server round-trip (no lock contention on items).</item>
/// <item>Saves use <see cref="ScribeNotebookSaveMessage"/> instead of <see cref="ScribeEditDocumentMessage"/>.</item>
/// <item>Lock-release is a no-op (no editor lock on items).</item>
/// <item>Proximity auto-close disabled via <see cref="InteractionRange"/> override.</item>
/// </list>
/// </summary>
public class GuiDialogScribeNotebook : ScribeDialogBase
{
    private IInventory? _hotbar;

    public GuiDialogScribeNotebook(IScribeDocumentHost host, ICoreClientAPI capi)
        : base(new BlockPos(0), host, capi)
    {
        capi.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
        _hotbar = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (_hotbar != null)
            _hotbar.SlotModified += OnHotbarSlotModified;
    }

    /// <summary>Disable the engine's frame-by-frame range check. Notebooks are not proximity-bound
    /// to a block position, so we never want the dialog to auto-close based on distance.</summary>
    protected override double InteractionRange => double.MaxValue;

    protected override string EmptyHintLangKey => "scribe:scribe-gui-edit-hint-notebook";

    /// <summary>The Notebook grants editor access immediately without a server round-trip — there is
    /// no lock to contend over when only one player can hold an item at a time. Seed the scratch from
    /// the host's current document so existing tasks and title are preserved when entering editor mode.</summary>
    protected override void RequestEditorAccess()
    {
        EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
    }

    /// <summary>No editor lock on a Notebook — nothing to release.</summary>
    protected override void SendReleaseLockPacket() { }

    /// <summary>Adds the History nav button between Pinned and Settings.</summary>
    protected override IEnumerable<Widget> GetExtraNavButtons()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        yield return TitleButton(
            "scribehistory",
            "scribe-gui-nav-history",
            colors.OnSurfaceVariant,
            NavButtonSize,
            OnClickSwitchToHistory,
            boxShadows: NavButtonShadow,
            activeColor: IsHistoryView ? ScribeRowConstants.NavActiveHistory : null);
    }

    /// <summary>Builds the History tab content for the Notebook — a newest-first read-only list of
    /// all automatically recorded history entries.</summary>
    protected override Widget BuildHistoryContent()
    {
        var nbHost  = host as NotebookHost;
        var entries = nbHost?.History.Entries ?? Array.Empty<HistoryEntry>();
        var colors  = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        float kindSize = bodySize * 0.72f;
        float dateSize = bodySize * 0.72f;

        // Family inherited from the tab's DefaultTextStyle ancestor (below): bodyStyle drops its explicit
        // FontFamily; kind/date carried no family and now follow the task font too (approved change).
        var bodyStyle = new TextStyle { FontSize = bodySize, Color = colors.OnSurface };
        var kindStyle = new TextStyle { FontSize = kindSize, Color = colors.OnSurface with { W = colors.OnSurface.W * 0.65f }, Weight = FontWeight.SemiBold };
        var dateStyle = new TextStyle { FontSize = dateSize, Color = colors.OnSurface with { W = colors.OnSurface.W * 0.55f } };

        var rows = new List<Widget>(entries.Count);
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            rows.Add(new Padding(
                EdgeInsets.Only(bottom: 6f),
                new Column(
                    spacing: 2,
                    crossAxisAlignment: CrossAxisAlignment.Stretch,
                    mainAxisSize: MainAxisSize.Min,
                    children: new Widget[]
                    {
                        new Row(children: new Widget[]
                        {
                            new Expanded(
                                new Padding(EdgeInsets.Only(left: 6f), new Text(KindLabel(entry), kindStyle)),
                                flex: 1),
                            new Text(entry.InGameDate, dateStyle),
                        }),
                        new Padding(EdgeInsets.Only(left: 8f),
                            new Text(entry.ActorName.Length > 0
                                ? $"{entry.ActorName}{(entry.Detail.Length > 0 ? " — " + entry.Detail : "")}"
                                : entry.Detail,
                                bodyStyle)),
                    })));
        }

        Widget body = entries.Count == 0
            ? new Center(child: new Text(Lang.Get("scribe:scribe-gui-history-empty"), bodyStyle))
            : new Scrollbar(controller: sharedScrollController,
                child: new SingleChildScrollView(controller: sharedScrollController,
                    child: new Column(
                        children: rows.ToArray(),
                        mainAxisSize: MainAxisSize.Min,
                        crossAxisAlignment: CrossAxisAlignment.Stretch)))
              { AutoHide = false };

        // Root the History tab subtree in the player's Task Text Font + window-scaled base size
        // (adopt-libgui-31-improvements). Body/kind/date Text widgets all inherit the family from here.
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize, new Padding(
            EdgeInsets.All(10),
            new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[] { new Divider(), new Expanded(body) })));
    }

    private static string KindLabel(HistoryEntry entry) => entry.Kind switch
    {
        HistoryEventKind.Crafted       => Lang.Get("scribe:scribe-gui-history-kind-crafted"),
        HistoryEventKind.PickedUp      => Lang.Get("scribe:scribe-gui-history-kind-pickedup"),
        HistoryEventKind.Death         => Lang.Get("scribe:scribe-gui-history-kind-death"),
        HistoryEventKind.PvpKill       => Lang.Get("scribe:scribe-gui-history-kind-pvpkill"),
        HistoryEventKind.BossKill      => Lang.Get("scribe:scribe-gui-history-kind-bosskill"),
        HistoryEventKind.TemporalStorm => Lang.Get("scribe:scribe-gui-history-kind-temporalstorm"),
        _                              => entry.Kind.ToString(),
    };

    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs _)
    {
        // Close when the player switches the active hand AWAY from THIS notebook — keyed on the document's
        // stable DocId, not merely "still some Scribe item." Switching to a DIFFERENT Scribe item (a second
        // notebook, a tablet) must still close this dialog; only a hotbar reorder that keeps the same item
        // active leaves it open (its DocId is unchanged). See ActiveHandItemHostsThisDocument.
        if (!ActiveHandItemHostsThisDocument())
            TryClose();
    }

    private void OnHotbarSlotModified(int slotId)
    {
        if (slotId == capi.World.Player.InventoryManager.ActiveHotbarSlotNumber)
            OnActiveSlotChanged(default!);
    }

    public override void OnGuiClosed()
    {
        capi.Event.AfterActiveSlotChanged -= OnActiveSlotChanged;
        if (_hotbar != null)
            _hotbar.SlotModified -= OnHotbarSlotModified;
        base.OnGuiClosed();
    }

    /// <summary>Notebook saves use <see cref="ScribeNotebookSaveMessage"/> so the server can write
    /// directly into the player's held ItemStack rather than routing through a block entity.</summary>
    protected override void SendFlushPacket(byte[] documentBytes)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = host.Document.DocId.ToByteArray(),
            DocumentBytes = documentBytes,
        });
    }
}
