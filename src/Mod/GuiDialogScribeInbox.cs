using System.Linq;
using Gui.Widgets.Framework;     // Widget
using Gui.Widgets.Inventory;     // SlotController
using Gui.Widgets.Layout;        // Column, Row, CrossAxisAlignment, MainAxisAlignment, Center
using Gui.Core.Layout;           // MainAxisSize
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The standalone Inbox block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>. All
/// view state, build methods, lock orchestration, autosave, title editing, and scroll management live
/// in the base class; this dialog opens directly into the shared Inbox tab and, since
/// add-inbox-inventory-tab, also exposes its own Inbox Inventory tab (the block's mixed
/// restricted/open storage). Its right-column nav drops the base's Read/Editor/Pinned buttons entirely
/// — per <c>inbox-block</c>'s spec this block's capabilities are the shared Inbox tab plus its own
/// Inbox Inventory tab — keeping an Assignment Inbox button, an Inbox Inventory button, and Settings.
///
/// <para>PARTIAL (add-assignment-and-quest-support §6.3): defaulting into the Inbox tab is real; the
/// Inbox tab's own row/filter content (§7) does not exist yet, so it still renders through
/// <see cref="ScribeDialogBase.BuildInboxContent"/>'s empty-state placeholder.</para>
/// </summary>
public sealed class GuiDialogScribeInbox : ScribeDialogBase
{
    /// <summary>The owning block-entity, kept typed so the Inbox Inventory tab can reach its
    /// <see cref="BlockEntityInbox.Inventory"/> (the base only stores the untyped
    /// <see cref="IScribeDocumentHost"/>).</summary>
    private readonly BlockEntityInbox inbox;

    /// <summary>Bridges the LibGUI slot widgets to the block-entity inventory — same lifecycle contract
    /// as <see cref="GuiDialogScribeScriptorium.slotController"/>: created lazily on the first Inbox
    /// Inventory build, disposed in <see cref="OnGuiClosed"/>, and subscribed to
    /// <see cref="ScribeDialogBase.RebuildBody"/> so a slot change actually re-renders.</summary>
    private SlotController? slotController;

    public GuiDialogScribeInbox(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        // Pass the BE's inventory to the inventory-carrying base ctor (mirrors the Scriptorium/
        // Assignment Desk) so OpenInventory / CloseInventoryAndSync fire automatically on open/close.
        : base(pos, host, capi, ((BlockEntityInbox)host).Inventory)
    {
        inbox = (BlockEntityInbox)host;
        DefaultToInboxView();
        // Unlike every other Inbox-reaching surface, this dialog never routes through
        // OnClickSwitchToInbox() (it has no nav button that does) — mark-seen must fire here directly,
        // or the ambient particle/nav shimmer can persist indefinitely regardless of how long the
        // player looks at their assignments (refine-assignment-desk-inbox-ux D2/4.1-4.2).
        MarkInboxSeenIfNeeded();
    }

    /// <summary>The Inbox block is a shared placed block: editor access requires a server lock
    /// round-trip, same as the Lectern/Scriptorium/Chalkboard/Assignment Desk. Retained even though no
    /// nav button here ever opens the editor view — see the Assignment Desk's identical note.</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>This dialog has no Read view to land on (see class remarks) — a plain access grant, the
    /// ordinary reply every right-click on the block gets, must leave the dialog on Inbox instead of
    /// being force-switched to a nonexistent Read view. Overriding the base's <c>EnterReadMode()</c>
    /// default this way was the fix for the Inbox always opening on Read despite having no Read nav
    /// button.</summary>
    public override void EnterGrantedView()
    {
        LeaveEditorIfActive();
        // Every right-click re-open grants access here and lands back on the (only) Inbox view without
        // going through OnClickSwitchToInbox() — mark-seen must fire on every grant, not just the first
        // (refine-assignment-desk-inbox-ux D2/4.2).
        MarkInboxSeenIfNeeded();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Inbox / Inbox Inventory / Settings (add-inbox-inventory-tab). The Inbox button switches
    /// to the shared Inbox tab, the Inbox Inventory button to the block's own 8-slot storage tab.</summary>
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
            activeColor: IsInboxInventoryView ? ScribeRowConstants.NavActiveTranscribe : null);
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

    /// <summary>Builds the Inbox Inventory tab: 8 slots — the first row (indices 0-3) Scribe-items-only
    /// (any <c>IScribeDocumentItem</c>, including blank and sealed Task Notices, matching the
    /// Scriptorium's own restriction), the second row (4-7) open — laid out 2 rows of 4 and centered
    /// both horizontally and vertically in the tab's content region (add-inbox-inventory-tab). Each
    /// slot uses the shared <see cref="ScribeInventorySlotStyle"/> helper so it matches the Assignment
    /// Desk's own slots exactly; only the restricted row passes a watermark icon, using the Scriptorium's
    /// generic "scribebook" glyph (not "scribeassignment") since the restriction is no longer Task-Notice-
    /// specific.</summary>
    protected override Widget BuildInboxInventoryContent()
    {
        var controller = EnsureSlotController();
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        var inv = inbox.Inventory;

        Widget[] restrictedSlots = Enumerable.Range(0, BlockEntityInbox.RestrictedSlotCount)
            .Select(i => ScribeInventorySlotStyle.Build(inv[i], controller, colors, CurrentShade, "scribebook"))
            .ToArray();
        Widget[] openSlots = Enumerable.Range(BlockEntityInbox.RestrictedSlotCount,
                BlockEntityInbox.SlotCount - BlockEntityInbox.RestrictedSlotCount)
            .Select(i => ScribeInventorySlotStyle.Build(inv[i], controller, colors, CurrentShade, null))
            .ToArray();

        return new Center(child: new Column(
            spacing: SlotRowSpacing,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                new Row(spacing: SlotSpacing, mainAxisSize: MainAxisSize.Min, children: restrictedSlots),
                new Row(spacing: SlotSpacing, mainAxisSize: MainAxisSize.Min, children: openSlots),
            }));
    }

    /// <summary>Gap between the two slot rows, matching <see cref="SlotSpacing"/> (the gap between slots
    /// within a row) so the 2×4 grid reads evenly spaced in both directions.</summary>
    private const float SlotRowSpacing = 4f;

    /// <summary>Gap between adjacent slots in a row, matching <c>SlotGrid</c>'s default spacing (the
    /// same value the Scriptorium's own hand-built slot row uses).</summary>
    private const float SlotSpacing = 4f;

    /// <summary>Lazily create the slot controller and start watching the Inbox's inventory — same
    /// idempotent-create + rebuild-on-change contract as
    /// <see cref="GuiDialogScribeScriptorium.EnsureSlotController"/>.</summary>
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

    /// <summary>Tear down the slot controller when the dialog closes so its <c>SlotModified</c>
    /// subscription doesn't outlive the dialog. Mirrors <see cref="GuiDialogScribeScriptorium.OnGuiClosed"/>.</summary>
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
