using System.Linq;
using Gui.Rendering;             // EdgeInsets
using Gui.Widgets.Framework;     // Widget
using Gui.Widgets.Inventory;     // SlotController
using Gui.Widgets.Layout;        // Column, Row, CrossAxisAlignment, MainAxisAlignment, Center
using Gui.Core.Layout;           // MainAxisSize
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The standalone Inbox block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>.
/// Opens into the shared Inbox tab and adds an Inbox Inventory tab (mixed restricted/open storage).
/// Its nav drops the base's Read/Editor/Pinned buttons, keeping Inbox, Inbox Inventory, and Settings.
///
/// <para>The Inbox tab has no content of its own yet and renders
/// <see cref="ScribeDialogBase.BuildInboxContent"/>'s empty-state placeholder.</para>
/// </summary>
public sealed class GuiDialogScribeInbox : ScribeDialogBase
{
    /// <summary>The owning block-entity, kept typed (the base only stores the untyped
    /// <see cref="IScribeDocumentHost"/>) to reach <see cref="BlockEntityInbox.Inventory"/>.</summary>
    private readonly BlockEntityInbox inbox;

    /// <summary>Bridges LibGUI slot widgets to the inventory. Created lazily on the first Inbox
    /// Inventory build, disposed in <see cref="OnGuiClosed"/>, and subscribed to
    /// <see cref="ScribeDialogBase.RebuildBody"/> so slot changes re-render.</summary>
    private SlotController? slotController;

    public GuiDialogScribeInbox(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        // The inventory-carrying base ctor fires OpenInventory / CloseInventoryAndSync on open/close.
        : base(pos, host, capi, ((BlockEntityInbox)host).Inventory)
    {
        inbox = (BlockEntityInbox)host;
        DefaultToInboxView();
        // This dialog never routes through OnClickSwitchToInbox(), so mark-seen must fire here directly
        // or the ambient shimmer persists.
        MarkInboxSeenIfNeeded();
    }

    /// <summary>Shared placed block: editor access requires a server lock round-trip. Retained even
    /// though no nav button here opens the editor view.</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>There is no Read view, so an access grant stays on Inbox instead of taking the base's
    /// <c>EnterReadMode()</c> default.</summary>
    public override void EnterGrantedView()
    {
        LeaveEditorIfActive();
        // Every grant lands on Inbox without OnClickSwitchToInbox(), so mark-seen fires on each one.
        MarkInboxSeenIfNeeded();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Inbox / Inbox Inventory / Settings.</summary>
    protected override Widget BuildRightColNav()
    {
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float size = NavButtonSize;
        var navColor = NavIconColor(colors);

        Widget inboxBtn = TitleButton("scribeinboxarrow", "scribe-tab-inbox", navColor,
            size: size, onTap: OnClickSwitchToInbox, boxShadows: NavButtonShadow,
            activeColor: IsInboxView ? ScribeRowConstants.NavActiveGuestbook : null);
        Widget inboxInventoryBtn = TitleButton("scribeinventory", "scribe-tab-inbox-inventory", navColor,
            size: size, onTap: OnClickSwitchToInboxInventory, boxShadows: NavButtonShadow,
            activeColor: IsInboxInventoryView ? ScribeRowConstants.NavActiveTranscribe : null,
            shimmer: ShowInboxInventoryShimmer());
        Widget settingsBtn = TitleButton("scribegear", "scribe-gui-nav-settings", navColor,
            size: size, onTap: modSystem.OpenSettings, boxShadows: NavButtonShadow,
            activeColor: modSystem.IsSettingsOpen ? ScribeRowConstants.NavActiveSettings : null);

        float sideColW = host.GetLayout(modSystem.MySettings.PixelArtSize).SideColW;
        float navBoxW = size - ScribeRowButton.BoxShrink;

        return new Column(
            spacing: 16,
            mainAxisAlignment: MainAxisAlignment.Start,
            crossAxisAlignment: NavButtonAlignment(sideColW, navBoxW),
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { inboxBtn, inboxInventoryBtn, settingsBtn });
    }

    /// <summary>Builds the Inbox Inventory tab: 12 slots in 3 rows of 4, centered. Slots 0-7 accept
    /// only <c>IScribeDocumentItem</c>s and carry a generic "scribebook" watermark (the restriction is not
    /// Task-Notice-specific); slots 8-11 are open. A restricted slot holding an undiscovered notice for the
    /// local player wraps in <see cref="ScribeShimmerWrap"/>.</summary>
    protected override Widget BuildInboxInventoryContent()
    {
        var controller = EnsureSlotController();
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        var inv = inbox.Inventory;
        string? localUid = capi.World.Player?.PlayerUID;

        Widget BuildRestrictedSlot(int i)
        {
            Widget slotWidget = ScribeInventorySlotStyle.Build(inv[i], controller, colors, CurrentShade, "scribebook");
            bool shimmer = localUid is not null && BlockEntityInbox.HoldsUndiscoveredNoticeFor(inv[i], localUid);
            return new ScribeShimmerWrap(shimmer, ScribeInventorySlotStyle.SlotSize, slotWidget);
        }

        Widget[] restrictedRow1 = Enumerable.Range(0, 4).Select(BuildRestrictedSlot).ToArray();
        Widget[] restrictedRow2 = Enumerable.Range(4, 4).Select(BuildRestrictedSlot).ToArray();
        Widget[] openSlots = Enumerable.Range(BlockEntityInbox.RestrictedSlotCount,
                BlockEntityInbox.SlotCount - BlockEntityInbox.RestrictedSlotCount)
            .Select(i => ScribeInventorySlotStyle.Build(inv[i], controller, colors, CurrentShade, null))
            .ToArray();

        Widget slotGrid = new Center(child: new Padding(EdgeInsets.Only(top: 8f), child: new Column(
            spacing: SlotRowSpacing,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                new Row(spacing: SlotSpacing, mainAxisSize: MainAxisSize.Min, children: restrictedRow1),
                new Row(spacing: SlotSpacing, mainAxisSize: MainAxisSize.Min, children: restrictedRow2),
                new Row(spacing: SlotSpacing, mainAxisSize: MainAxisSize.Min, children: openSlots),
            })));

        // The slot grid is content, not Row 3 controls, so the divider sits directly under the subtitle.
        Widget header = ScribeTabHeader.Build(colors, RowStyle,
            "scribe:scribe-tab-inbox-inventory", "scribe:scribe-tab-subtitle-inbox-inventory",
            modSystem.VisualTuning.ShowSubtitleRow);

        return new Padding(
            EdgeInsets.Ltrb(10, 4, 10, 10),
            child: new Column(
                spacing: 0,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[] { header, new Expanded(child: slotGrid) }));
    }

    /// <summary>True when the local player has an undiscovered notice in any restricted slot — the
    /// Inbox Inventory nav shimmer trigger, finer than <see cref="ScribeDialogBase.ShowInboxShimmer"/>.
    /// Shares <see cref="BlockEntityInbox.HoldsUndiscoveredNoticeFor"/> with the per-slot shimmer.</summary>
    private bool ShowInboxInventoryShimmer()
    {
        string? uid = capi.World.Player?.PlayerUID;
        if (uid is null) return false;
        var inv = inbox.Inventory;
        return Enumerable.Range(0, BlockEntityInbox.RestrictedSlotCount)
            .Any(i => BlockEntityInbox.HoldsUndiscoveredNoticeFor(inv[i], uid));
    }

    /// <summary>Gap between slot rows; equals <see cref="SlotSpacing"/> so the grid is evenly spaced.</summary>
    private const float SlotRowSpacing = 4f;

    /// <summary>Gap between adjacent slots in a row (<c>SlotGrid</c>'s default).</summary>
    private const float SlotSpacing = 4f;

    /// <summary>Idempotently creates the slot controller and watches the inventory, rebuilding on change.</summary>
    private SlotController EnsureSlotController()
    {
        if (slotController == null)
        {
            slotController = new SlotController(capi);
            slotController.WatchInventory(inbox.Inventory);
            slotController.AddListener(RebuildBody);
        }
        return slotController;
    }

    /// <summary>Disposes the slot controller so its <c>SlotModified</c> subscription doesn't outlive the dialog.</summary>
    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        if (slotController != null)
        {
            slotController.UnwatchInventory(inbox.Inventory);
            slotController.Dispose();
            slotController = null;
        }
    }
}
