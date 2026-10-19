using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Widgets.Framework;     // Widget, ColorScheme, ValueKey
using Gui.Widgets.Inventory;     // SlotController, ItemSlotStyle
using Gui.Widgets.Layout;        // Column, Stack, Positioned, CrossAxisAlignment, MainAxisAlignment, SizedBox
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector4
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // AssetLocation
using Vintagestory.API.Config;   // Lang
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Assignment Desk block's dialog: a thin subclass of <see cref="ScribeDialogBase"/> whose own work is the
/// Create Assignments staging-and-select flow and a five-tab nav (Create Assignments, Sent History, Inbox, Editor,
/// Settings; no Read or Pinned tab). Defaults to the Assignment tab on open.
/// </summary>
public sealed class GuiDialogScribeAssignmentDesk : ScribeDialogBase
{
    /// <summary>The owning block-entity, typed so the staging slot can reach its inventory.</summary>
    private readonly BlockEntityAssignmentDesk assignmentDesk;

    /// <summary>Bridges the staging slot widget to the block-entity inventory. Created lazily on first build,
    /// disposed in <see cref="OnGuiClosed"/>, and subscribed to <see cref="ScribeDialogBase.RebuildBody"/> so a
    /// staged item change re-renders the picker (same contract as the Scriptorium's).</summary>
    private SlotController? slotController;

    /// <summary>Selected staged-row identities (UI-only). Cleared on send and pruned to the staged document's
    /// contents each build, so swapping the staged item drops stale selections.</summary>
    private readonly HashSet<Guid> selectedTaskIds = new();

    /// <summary>"Delete from source on send": UI-only, reset to false on every successful send.</summary>
    private bool deleteFromSource;

    /// <summary>"Pull from Desk" source opt-in: UI-only, set by <see cref="OnPullFromDesk"/>, reset in
    /// <see cref="OnGuiClosed"/>. A staged item overrides it without clearing it, so it resumes once the item is
    /// removed.</summary>
    private bool deskSourceActive;

    /// <summary>Whether the empty-state "pull from Desk" button renders this build.</summary>
    private bool canPullFromDesk;

    /// <summary>Whether <see cref="stagedBlocksCache"/> came from the Desk's own document rather than a staged
    /// item; tells the server which removal path to use on send.</summary>
    private bool sourceIsDeskDocument;

    /// <summary>Dialog-owned target selection (UI-only), lifted out of the form's State so a change can fire the
    /// Hybrid range check and reset the delivery-toggle override.</summary>
    private string? selectedTargetUid;

    /// <summary>Last Hybrid range-check result for <see cref="selectedTargetUid"/>; <c>null</c> while pending,
    /// treated as out-of-range (matches the server's offline-target fallback).</summary>
    private bool? rangeCheckInRange;

    /// <summary>Set when the player taps the Local Inboxes / Send a Notice toggle for the current target; reset
    /// to null when the target changes so the range-check default applies again.</summary>
    private ScribeDeliveryChoice? deliveryChoiceOverride;

    /// <summary>The staged document's raw blocks from the last build, keyed by TaskId. The send path reads these,
    /// since <see cref="ScribeReadRowData"/> is a display-only projection that drops fields Tracker/Link/Craft
    /// rows need.</summary>
    private List<ScribeBlock> stagedBlocksCache = new();

    /// <summary>The staged document's rows from the last build, in display order (used for the selection cascade).</summary>
    private List<ScribeReadRowData> stagedRowsCache = new();

    /// <summary>Slot edge length in pixels; matches the Scriptorium's slots.</summary>
    private const float SlotSize = 48f;

    /// <summary>Watermark glyph size as a fraction of <see cref="SlotSize"/>; matches the Scriptorium.</summary>
    private const float WatermarkScale = 0.66f;

    /// <summary>Wooden stamp PNG; same asset as the Scriptorium's.</summary>
    private static readonly AssetLocation StampAsset = new("scribe", "textures/gui/scribe-copy-stamp.png");

    /// <summary>Owns the "Submitted to Player" stamp animation controller (one slot, one label).</summary>
    private readonly ScribeAnimationRegistry stampRegistry = new();
    private int stampGeneration;
    /// <summary>Whether the stamp is playing over the staging slot; gates the overlay and disables Send.</summary>
    private bool stampActive;

    public GuiDialogScribeAssignmentDesk(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        // Passes the BE's staging inventory to the inventory-carrying base ctor so open/close sync fires.
        : base(pos, host, capi, ((BlockEntityAssignmentDesk)host).Inventory)
    {
        assignmentDesk = (BlockEntityAssignmentDesk)host;
        // Create Assignments is this dialog's default view; safe because TryOpen's first Build() runs after the ctor.
        DefaultToAssignmentView();
        modSystem.DeliveryRangeCheckReplied += OnDeliveryRangeCheckReplied;
    }

    /// <summary>Editor access needs a server lock round-trip, as on the other writing stations. Kept even though
    /// no nav button opens the editor, so incidental grants (e.g. a Handbook append) behave consistently.</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>A plain access grant leaves the selected tab alone rather than forcing a default view. Overrides the
    /// base's <c>EnterReadMode()</c> because this dialog's landing tab is Create Assignments, not Read.</summary>
    public override void EnterGrantedView()
    {
        LeaveEditorIfActive();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>The Desk has no Read tab, so leaving the editor (the Done button and the teardown inside
    /// <see cref="ScribeDialogBase.LeaveEditorIfActive"/> both funnel here) returns to Create Assignments.</summary>
    protected override void OnLeftEditorMode() => DefaultToAssignmentView();

    /// <summary>Replaces the base nav column wholesale with this dialog's five tabs. The Editor button reuses the
    /// base's entry point (<see cref="ScribeDialogBase.TryEnterEditor"/>) and its icon, tooltip and active-color
    /// conventions; <see cref="GetExtraNavButtons"/> is only for surfaces that add to the base column.</summary>
    protected override Widget BuildRightColNav()
    {
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float size = NavButtonSize;
        var navColor = NavIconColor(colors);

        // Plus glyph, distinct from the rolled-scroll glyph used by the row-level accepted-assignment marker.
        Widget assignmentBtn = TitleButton("scribeplus", "scribe-tab-assignment", navColor,
            size: size, onTap: OnClickSwitchToAssignment, boxShadows: NavButtonShadow,
            activeColor: IsAssignmentView ? ScribeRowConstants.NavActiveEdit : null);
        // "scribehistory" is aliased to guestbook.svg; NavActiveHistory is shared with other History nav buttons.
        Widget sentHistoryBtn = TitleButton("scribehistory", "scribe-tab-senthistory", navColor,
            size: size, onTap: OnClickSwitchToSentHistory, boxShadows: NavButtonShadow,
            activeColor: IsSentHistoryView ? ScribeRowConstants.NavActiveHistory : null);
        Widget inboxBtn = TitleButton("scribeinboxarrow", "scribe-tab-inbox", navColor,
            size: size, onTap: OnClickSwitchToInbox, boxShadows: NavButtonShadow,
            activeColor: IsInboxView ? ScribeRowConstants.NavActiveGuestbook : null,
            shimmer: ShowInboxShimmer);
        // Same icon codes, tooltip keys and active color as the base's default editor button.
        Widget editorBtn = TitleButton("scribeedit", "scribe-gui-nav-edit",
            EditLockedByOther ? navColor with { W = 0.4f } : navColor,
            size: size, onTap: TryEnterEditor, boxShadows: NavButtonShadow,
            activeColor: IsEditorView ? ScribeRowConstants.NavActiveEdit : null);
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
            children: new Widget[] { assignmentBtn, sentHistoryBtn, inboxBtn, editorBtn, settingsBtn });
    }

    /// <summary>The Create Assignments tab: a staging slot, its rows with independent Selected checkboxes, a
    /// "Delete from source on send" toggle, and the target picker + batch Send, composed by
    /// <see cref="ScribeAssignmentFormContent"/>. Rows are resolved fresh each build because the staged item can
    /// change at any time (another client, or this player pulling/placing it).</summary>
    protected override Widget BuildAssignmentContent()
    {
        var controller = EnsureSlotController();
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        var slot = assignmentDesk.Inventory[BlockEntityAssignmentDesk.StagingSlotIndex];

        ScribeDocument? stagedDoc = null;
        bool slotHasItem = slot.Itemstack != null
            && ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out stagedDoc) && stagedDoc is not null;

        // Source priority: staged item, else the Desk's own document if "pull from Desk" is active, else empty.
        List<ScribeBlock> deskEligibleBlocks = EligibleDeskBlocks();
        stagedBlocksCache = slotHasItem
            ? stagedDoc!.Blocks.ToList()
            : deskSourceActive
                ? deskEligibleBlocks
                : new List<ScribeBlock>();
        sourceIsDeskDocument = !slotHasItem && deskSourceActive;

        // The pull button shows only when nothing is staged, the Desk source is inactive, and the Desk has eligible blocks.
        canPullFromDesk = !slotHasItem && !deskSourceActive && deskEligibleBlocks.Count > 0;

        // Resolve rows like the read view (ResolveRowItem + the empty-Task-text filter).
        stagedRowsCache = stagedBlocksCache
            .Select((b, i) =>
            {
                var (stack, name) = ResolveRowItem(b);
                return new ScribeReadRowData(
                    Index: i, Kind: b.Kind, Done: b.Done, Pinned: false, TaskId: b.TaskId, Text: b.Text,
                    DisplayStack: stack, DisplayName: name,
                    TargetQuantity: b.TargetQuantity, CurrentQuantity: b.CurrentQuantity, LinkTarget: b.LinkTarget,
                    Depth: b.Depth);
            })
            .Where(r => !r.IsTask || !string.IsNullOrWhiteSpace(r.Text))
            .ToList();

        // Prune selections the current staged document no longer contains.
        selectedTaskIds.IntersectWith(stagedRowsCache.Select(r => r.TaskId));

        // Veil background + book watermark, as in GuiDialogScribeScriptorium.BuildWatermarkedSlot: the glyph paints
        // under the slot, whose semi-opaque fill mutes it to a faint watermark.
        Vector4 bookColor = colors.Primary;
        Vector4 veilColor = colors.Surface with { W = 0.66f };
        float watermarkGlyph = SlotSize * WatermarkScale;
        float watermarkInset = (SlotSize - watermarkGlyph) / 2f;
        var slotStyle = ItemSlotStyle.Default with { Size = SlotSize, BackgroundColor = veilColor };
        Widget stagingSlot = new Stack(children: new Widget[]
        {
            new Positioned(
                left: watermarkInset, top: watermarkInset, width: watermarkGlyph, height: watermarkGlyph,
                child: new ScribeVsIconGlyph("scribebook", watermarkGlyph, bookColor)),
            new ScribeDocumentSlot(slot, controller, slotStyle, colors, CurrentShade),
        });
        var stampOverlay = BuildStampOverlay();
        if (stampOverlay != null)
        {
            stagingSlot = new Stack(children: new Widget[]
            {
                stagingSlot,
                new Positioned(left: 0f, top: 0f, width: SlotSize, height: SlotSize, child: stampOverlay),
            });
        }

        // Seed the default target (last-assigned target if still listed, else the first) and fire the one-time
        // range check immediately, so the seeding needs no rebuild.
        var targetPlayers = ComputeAssignmentTargetPlayers();
        if (selectedTargetUid is null || !targetPlayers.Any(p => p.Uid == selectedTargetUid))
        {
            string lastTarget = modSystem.MySettings.LastAssignmentTargetUid;
            selectedTargetUid = !string.IsNullOrEmpty(lastTarget) && targetPlayers.Any(p => p.Uid == lastTarget)
                ? lastTarget
                : targetPlayers.Count > 0 ? targetPlayers[0].Uid : null;
            deliveryChoiceOverride = null;
            rangeCheckInRange = null;
            if (selectedTargetUid is not null && ScribeDeliveryConfig.ReadMode(capi) == ScribeDeliveryMode.Hybrid)
                modSystem.RequestDeliveryRangeCheck(assignmentDesk.Pos, selectedTargetUid);
        }

        var deliveryMode = ScribeDeliveryConfig.ReadMode(capi);
        var deliveryChoice = EffectiveDeliveryChoice(deliveryMode);

        Widget? noticeSupplySlot = null;
        Widget? noticeOutputSlot = null;
        if (ScribeDeliveryPolicy.RequiresNotice(deliveryMode, deliveryChoice))
        {
            noticeSupplySlot = BuildNoticeSlot(assignmentDesk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex], controller, colors);
            noticeOutputSlot = BuildNoticeSlot(assignmentDesk.Inventory[BlockEntityAssignmentDesk.NoticeOutputSlotIndex], controller, colors);
        }

        return new ScribeAssignmentFormContent(
            targetPlayers: targetPlayers,
            selectedTargetUid: selectedTargetUid,
            onTargetSelected: OnTargetSelected,
            stagingSlot: stagingSlot,
            stagedRows: stagedRowsCache,
            selectedTaskIds: selectedTaskIds,
            onToggleSelected: OnToggleStagedRowSelected,
            deleteFromSource: deleteFromSource,
            onToggleDeleteFromSource: OnToggleDeleteFromSource,
            onSendBatch: OnSendAssignmentBatch,
            sending: stampActive,
            canPullFromDesk: canPullFromDesk,
            onCreateTasks: TryEnterEditor,
            onPullFromDesk: OnPullFromDesk,
            style: RowStyle,
            scrollController: sharedScrollController,
            deliveryMode: deliveryMode,
            deliveryChoice: deliveryChoice,
            onDeliveryChoiceChanged: OnDeliveryChoiceChanged,
            noticeSupplySlot: noticeSupplySlot,
            noticeOutputSlot: noticeOutputSlot,
            onOpenDeliveryInfo: () => ToggleHandbookPage("craftinginfo-scribe-delivery"),
            showSubtitleRow: modSystem.VisualTuning.ShowSubtitleRow);
    }

    /// <summary>The Desk's persisted document rows eligible to pull in (blank Task rows excluded). Reads the
    /// committed <c>Document</c>, never the Editor's scratch buffer.</summary>
    private List<ScribeBlock> EligibleDeskBlocks() =>
        host.Document.Blocks.Where(b => !b.IsTask || !string.IsNullOrWhiteSpace(b.Text)).ToList();

    /// <summary>Activates the Desk's own document as the task source and rebuilds.</summary>
    private void OnPullFromDesk()
    {
        deskSourceActive = true;
        RebuildBody();
    }

    /// <summary>The "Submitted to Player" stamp overlaid on the staging slot after a send. A fresh
    /// <see cref="ScribeStamp"/> per generation remounts and replays.</summary>
    private Widget? BuildStampOverlay()
    {
        if (!stampActive) return null;
        string id = StampId(stampGeneration);
        float artWidth = ScribePlayerSettings.ClampPixelArtSize(modSystem.MySettings.PixelArtSize) * 0.2f;
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        return new ScribeStamp(
            id: id,
            registry: stampRegistry,
            stampBitmap: modSystem.GetGuiTextureBitmap(StampAsset),
            copyLabel: Lang.Get("scribe:scribe-assignment-stamp-imprint"),
            imprintColor: ScribeRowConstants.StampImprintInk,
            glowColor: colors.Surface with { W = 0.6f },
            slotSize: SlotSize,
            artWidth: artWidth,
            onEnd: () => OnStampEnded(id),
            onDescend: () => ScribeStampSound.Play(capi),
            key: new ValueKey<string>(id));
    }

    /// <summary>Releases the stamp controller, drops the overlay and re-enables Send.</summary>
    private void OnStampEnded(string id)
    {
        stampRegistry.Release(id);
        if (id == StampId(stampGeneration))
        {
            stampActive = false;
            if (IsOpened()) RebuildBody();
        }
    }

    private static string StampId(int generation) => $"assignment-submit-stamp:{generation}";

    /// <summary>Toggles one staged row. Selecting a Depth-0 row also selects its following Depth&gt;0 rows once, at
    /// selection time; afterwards every row toggles independently, and deselecting never cascades.</summary>
    private void OnToggleStagedRowSelected(Guid taskId)
    {
        if (selectedTaskIds.Remove(taskId))
        {
            RebuildBody();
            return;
        }

        selectedTaskIds.Add(taskId);
        int idx = stagedRowsCache.FindIndex(r => r.TaskId == taskId);
        if (idx >= 0 && stagedRowsCache[idx].Depth == 0)
        {
            for (int i = idx + 1; i < stagedRowsCache.Count && stagedRowsCache[i].Depth > 0; i++)
                selectedTaskIds.Add(stagedRowsCache[i].TaskId);
        }
        RebuildBody();
    }

    private void OnToggleDeleteFromSource(bool value)
    {
        deleteFromSource = value;
        RebuildBody();
    }

    /// <summary>Sends each selected staged row as an independent assignment with a fresh client-minted
    /// <c>AssignmentId</c>. Reads raw blocks from <see cref="stagedBlocksCache"/> (not the display-only rows) so the
    /// server can rebuild any kind, then clears the selection and the delete-from-source flag.</summary>
    private void OnSendAssignmentBatch()
    {
        string? targetUid = selectedTargetUid;
        if (targetUid is null) return;

        // A send needing a notice with no blank one loaded is refused with a message.
        var deliveryMode = ScribeDeliveryConfig.ReadMode(capi);
        var deliveryChoice = EffectiveDeliveryChoice(deliveryMode);
        bool requiresNotice = ScribeDeliveryPolicy.RequiresNotice(deliveryMode, deliveryChoice);
        if (requiresNotice && assignmentDesk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].Itemstack is null)
        {
            capi.TriggerIngameError(this, "no-blank-notice", Lang.Get("scribe:scribe-delivery-no-blank-notice"));
            return;
        }

        var rows = stagedBlocksCache
            .Where(b => selectedTaskIds.Contains(b.TaskId))
            .Select(b => new ScribeAssignmentBatchRow
            {
                AssignmentId = Guid.NewGuid().ToByteArray(),
                SourceTaskId = b.TaskId.ToByteArray(),
                Kind = (byte)b.Kind,
                Text = b.Text,
                TargetItemCode = b.TargetItemCode,
                TargetQuantity = b.TargetQuantity,
                LinkTarget = b.LinkTarget,
                LinkLabel = b.LinkLabel,
                LinkDescription = b.LinkDescription,
                RecipeSignature = b.RecipeSignature,
                Depth = b.Depth,
            })
            .ToList();
        if (rows.Count == 0) return;

        var pos = assignmentDesk.Pos;
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSendAssignmentBatchMessage
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            StagingSlot = BlockEntityAssignmentDesk.StagingSlotIndex,
            TargetPlayerUid = targetUid,
            DeleteFromSource = deleteFromSource,
            SourceIsDeskDocument = sourceIsDeskDocument,
            Rows = rows,
            DeliveryChoice = (byte)deliveryChoice,
        });

        // Set optimistically so the next visit defaults to this target, like the stamp flourish below.
        modSystem.UpdateMySettings(s => s.LastAssignmentTargetUid = targetUid);

        selectedTaskIds.Clear();
        deleteFromSource = false;
        stampGeneration++;
        stampActive = true;
        if (IsOpened()) RebuildBody();
    }

    /// <summary>Lazily creates the slot controller and watches the staging inventory (idempotent).</summary>
    private SlotController EnsureSlotController()
    {
        if (slotController == null)
        {
            slotController = new SlotController(capi);
            slotController.WatchInventory(assignmentDesk.Inventory);
            slotController.AddListener(RebuildBody);
        }
        return slotController;
    }

    /// <summary>Tears down the slot controller so its <c>SlotModified</c> subscription does not outlive the dialog.</summary>
    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        deskSourceActive = false;
        if (slotController != null)
        {
            slotController.UnwatchInventory(assignmentDesk.Inventory);
            slotController.Dispose();
            slotController = null;
        }
        stampRegistry.Dispose();
        stampActive = false;
        modSystem.DeliveryRangeCheckReplied -= OnDeliveryRangeCheckReplied;
    }

    // Physical delivery

    /// <summary>Resets the manual toggle override and fires a fresh Hybrid range check; other modes never read the
    /// result, so the request is skipped.</summary>
    private void OnTargetSelected(string uid)
    {
        selectedTargetUid = uid;
        deliveryChoiceOverride = null;
        rangeCheckInRange = null;
        if (ScribeDeliveryConfig.ReadMode(capi) == ScribeDeliveryMode.Hybrid)
            modSystem.RequestDeliveryRangeCheck(assignmentDesk.Pos, uid);
        RebuildBody();
    }

    /// <summary>Applies a range-check reply only if it matches the current target; superseded replies are ignored.</summary>
    private void OnDeliveryRangeCheckReplied(ScribeDeliveryRangeCheckReplyMessage message)
    {
        if (message.TargetPlayerUid != selectedTargetUid) return;
        rangeCheckInRange = message.InRange;
        if (IsOpened()) RebuildBody();
    }

    /// <summary>Toggle tap handler; always freely overridable.</summary>
    private void OnDeliveryChoiceChanged(ScribeDeliveryChoice choice)
    {
        deliveryChoiceOverride = choice;
        RebuildBody();
    }

    /// <summary>The manual override if set, else the Core default for the mode and last range-check result.</summary>
    private ScribeDeliveryChoice EffectiveDeliveryChoice(ScribeDeliveryMode mode) =>
        deliveryChoiceOverride ?? ScribeDeliveryPolicy.ResolveDefault(mode, rangeCheckInRange == true);

    /// <summary>Builds a notice slot with the staging slot's veil/watermark treatment but the "tasknotice" glyph,
    /// via the shared <see cref="ScribeInventorySlotStyle"/> helper (also used by the Inbox block).</summary>
    private Widget BuildNoticeSlot(ItemSlot slot, SlotController controller, ColorScheme colors) =>
        ScribeInventorySlotStyle.Build(slot, controller, colors, CurrentShade, "scribeassignment");
}
