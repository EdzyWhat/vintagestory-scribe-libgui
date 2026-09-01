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
/// The Assignment Desk block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>. All
/// view state, build methods, lock orchestration, autosave, title editing, and scroll management live
/// in the base class; this dialog's own real work is the Create Assignments tab's staging-and-select
/// flow (assignment-multi-item-creation, design.md D8-D13) plus its right-column nav, which replaces the
/// base's default Read/Editor/Pinned trio with its own six-tab layout: Create Assignments, Sent
/// Assignment History, Inbox, Read, Editor, Settings (add-assignment-desk-own-tasks design.md D1/D2) —
/// still no Pinned tab (the Desk's own document isn't a personal pin target). Defaults to the Assignment
/// tab on open.
/// </summary>
public sealed class GuiDialogScribeAssignmentDesk : ScribeDialogBase
{
    /// <summary>The owning block-entity, kept typed so the staging slot can reach its
    /// <see cref="BlockEntityAssignmentDesk.Inventory"/> (the base only stores the untyped
    /// <see cref="IScribeDocumentHost"/>).</summary>
    private readonly BlockEntityAssignmentDesk assignmentDesk;

    /// <summary>Bridges the staging slot widget to the block-entity inventory — same lifecycle contract as
    /// <see cref="GuiDialogScribeScriptorium.slotController"/>: created lazily on first build, disposed in
    /// <see cref="OnGuiClosed"/>, and subscribed to <see cref="ScribeDialogBase.RebuildBody"/> so a staged
    /// item change (placing/removing the source document) actually re-renders the picker.</summary>
    private SlotController? slotController;

    /// <summary>Selected staged-row identities (design.md D10/D11) — UI-only, cleared on every successful
    /// send and pruned to whatever the currently-staged document actually contains on each build (so
    /// swapping the staged item drops stale selections instead of silently carrying them over).</summary>
    private readonly HashSet<Guid> selectedTaskIds = new();

    /// <summary>"Delete from source on send" (design.md D13) — UI-only session state, never persisted;
    /// reset to false on every successful send.</summary>
    private bool deleteFromSource;

    /// <summary>"Pull from Desk" active-source opt-in (add-assignment-desk-own-tasks design.md D3) — a
    /// sticky, UI-only session bool in the same family as <see cref="deleteFromSource"/>: set true by
    /// <see cref="OnPullFromDesk"/>, reset to false in <see cref="OnGuiClosed"/>, never persisted. A staged
    /// item in the slot always overrides it (see <see cref="BuildAssignmentContent"/>'s source-priority
    /// resolution) without needing to clear this flag — it naturally resumes once the item is removed.</summary>
    private bool deskSourceActive;

    /// <summary>Whether the empty-state "pull from Desk" button should render this build — computed fresh
    /// in <see cref="BuildAssignmentContent"/>, read by <see cref="ScribeAssignmentFormContent"/>.</summary>
    private bool canPullFromDesk;

    /// <summary>Whether <see cref="stagedBlocksCache"/> was resolved from the Desk's own document rather
    /// than a staged item, as of the last build (add-assignment-desk-own-tasks design.md D6) — read by
    /// <see cref="OnSendAssignmentBatch"/> to tell the server which removal path to use on send, without
    /// re-deriving the source-priority decision <see cref="BuildAssignmentContent"/> already made.</summary>
    private bool sourceIsDeskDocument;

    /// <summary>The staged document's raw blocks from the last build, keyed by TaskId — the source of
    /// truth <see cref="OnSendAssignmentBatch"/> reads to build the wire message (a
    /// <see cref="ScribeReadRowData"/> is a display-only projection that drops the raw
    /// TargetItemCode/LinkTarget/RecipeSignature fields a Tracker/Link/Craft row needs to reconstruct).</summary>
    private List<ScribeBlock> stagedBlocksCache = new();

    /// <summary>The staged document's rows from the last build, in display order — read by
    /// <see cref="OnToggleStagedRowSelected"/> to resolve a toggled row's Depth for the D11 cascade.</summary>
    private List<ScribeReadRowData> stagedRowsCache = new();

    /// <summary>Slot edge length in pixels — matches the Scriptorium's inventory slots
    /// (<see cref="GuiDialogScribeScriptorium.SlotSize"/>) so the staging slot reads identically.</summary>
    private const float SlotSize = 48f;

    /// <summary>The staging slot's watermark glyph size as a fraction of <see cref="SlotSize"/> — matches
    /// <see cref="GuiDialogScribeScriptorium.WatermarkScale"/> (triage 2026-08-31: "style it with the same
    /// adornments as the Scriptorium's inventory slots"). Only the SIZE constant was ever actually shared
    /// (per that class's own comment); the veil background + book watermark below were never applied to
    /// this dialog's slot until now.</summary>
    private const float WatermarkScale = 0.66f;

    /// <summary>The pixel-art wooden stamp PNG, matching <see cref="GuiDialogScribeScriptorium.StampAsset"/>
    /// (same asset, same art) — duplicated rather than shared since it's a one-line constant, not tuned
    /// state that could drift (refine-assignment-desk-inbox-ux 10.3).</summary>
    private static readonly AssetLocation StampAsset = new("scribe", "textures/gui/scribe-copy-stamp.png");

    /// <summary>Owns the "Submitted to Player" stamp flourish's animation controller — mirrors
    /// <see cref="GuiDialogScribeScriptorium.stampRegistry"/>, simplified to one slot/one label since this
    /// dialog has neither a second stampable slot nor a second imprint word to choose between.</summary>
    private readonly ScribeAnimationRegistry stampRegistry = new();
    private int stampGeneration;
    /// <summary>Whether the stamp is currently playing over the staging slot — gates both the overlay
    /// (<see cref="BuildStampOverlay"/>) and the Send button (10.4: unclickable for the animation's
    /// duration).</summary>
    private bool stampActive;

    public GuiDialogScribeAssignmentDesk(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        // Pass the BE's staging inventory to the inventory-carrying base ctor (mirrors the Scriptorium)
        // so OpenInventory / CloseInventoryAndSync fire automatically on open/close.
        : base(pos, host, capi, ((BlockEntityAssignmentDesk)host).Inventory)
    {
        assignmentDesk = (BlockEntityAssignmentDesk)host;
        // design.md Decision 1: "Only the Assignment Desk dialog ever sets viewMode to Assignment" —
        // and it does so as its DEFAULT view, not merely a reachable one. Safe here: TryOpen's first
        // Build() runs after this ctor returns.
        DefaultToAssignmentView();
    }

    /// <summary>The Assignment Desk is a shared placed block: editor access requires a server lock
    /// round-trip, same as the Lectern/Scriptorium/Chalkboard. Retained even though no nav button here
    /// currently opens the editor view, so any future/incidental editor-access grant (e.g. a Handbook
    /// append targeting this host) behaves consistently with every other writing station.</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>A plain access grant, the ordinary reply every right-click on the block gets, must leave
    /// whichever tab is already selected alone rather than being force-switched to some default view —
    /// matching the base's own doc-comment reasoning for every Read/Editor-capable surface, just applied
    /// to this dialog's six-tab set (Assignment/Sent-History/Inbox/Read/Editor all count as "already
    /// selected and legitimate"; only <see cref="DefaultToAssignmentView"/> in the ctor picks Create
    /// Assignments as the FIRST-open default). Overriding the base's <c>EnterReadMode()</c> default this
    /// way remains necessary even now that a Read tab exists here, because Create Assignments — not
    /// Read — is this dialog's intended landing tab.</summary>
    public override void EnterGrantedView()
    {
        LeaveEditorIfActive();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Replaces the base's default Read/Editor/Pinned/Settings column with this dialog's own
    /// six-tab layout — Create Assignments, Sent Assignment History, Inbox, Read, Editor, Settings, in
    /// that nav order (add-assignment-desk-own-tasks design.md D1/D2) — still no Pinned tab (the Desk's
    /// own document isn't a personal pin target). The Read/Editor buttons wire straight to the base's own
    /// entry points (<see cref="ScribeDialogBase.EnterReadMode"/> / <see cref="ScribeDialogBase.TryEnterEditor"/>)
    /// and reuse its exact icon codes/tooltip keys/active-color/dimming conventions
    /// (<see cref="ScribeDialogBase.IsReadView"/>/<see cref="ScribeDialogBase.IsEditorView"/>/
    /// <see cref="ScribeDialogBase.EditLockedByOther"/>) — no new view code, per D1. Mirrors
    /// <see cref="GuiDialogScribeTablet"/>'s precedent for replacing this seam wholesale rather than
    /// layering onto <see cref="GetExtraNavButtons"/>, which is reserved for surfaces that keep the base
    /// column and only ADD to it.</summary>
    protected override Widget BuildRightColNav()
    {
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float size = NavButtonSize;
        var navColor = NavIconColor(colors);

        // Plus glyph (refine-assignment-desk-inbox-ux 1.6) — the Create Assignments tab is this dialog's
        // sole create-and-send surface, distinct from the rolled-scroll glyph the row-level accepted-
        // assignment marker still uses (ScribeAssignedTaskIcon, unrelated to this nav button).
        Widget assignmentBtn = TitleButton("scribeplus", "scribe-tab-assignment", navColor,
            size: size, onTap: OnClickSwitchToAssignment, boxShadows: NavButtonShadow,
            activeColor: IsAssignmentView ? ScribeRowConstants.NavActiveEdit : null);
        // History glyph (triage 2026-08-31: "I have no idea what the current [scroll] icon is") — the
        // "scribehistory" icon code already exists (aliased to guestbook.svg) but was unused for this
        // purpose; a guestbook/journal glyph reads unambiguously as "history" where the reused scroll icon
        // didn't. NavActiveHistory (warm amber) is the same constant every other History-flavored nav
        // button in this codebase uses.
        Widget sentHistoryBtn = TitleButton("scribehistory", "scribe-tab-senthistory", navColor,
            size: size, onTap: OnClickSwitchToSentHistory, boxShadows: NavButtonShadow,
            activeColor: IsSentHistoryView ? ScribeRowConstants.NavActiveHistory : null);
        Widget inboxBtn = TitleButton("scribeinboxarrow", "scribe-tab-inbox", navColor,
            size: size, onTap: OnClickSwitchToInbox, boxShadows: NavButtonShadow,
            activeColor: IsInboxView ? ScribeRowConstants.NavActiveGuestbook : null,
            shimmer: ShowInboxShimmer);
        // Read/Editor (add-assignment-desk-own-tasks D1) — same icon codes/tooltip keys/active-color as the
        // base's own default BuildRightColNav, just rebuilt here since this dialog replaces that seam
        // wholesale rather than extending it.
        Widget readBtn = TitleButton("scribecheck", "scribe-gui-nav-read", navColor,
            size: size, onTap: EnterReadMode, boxShadows: NavButtonShadow,
            activeColor: IsReadView ? ScribeRowConstants.NavActiveRead : null);
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
            children: new Widget[] { assignmentBtn, sentHistoryBtn, inboxBtn, readBtn, editorBtn, settingsBtn });
    }

    /// <summary>The Create Assignments tab (assignment-multi-item-creation design.md D8-D13, split down to
    /// staging-and-select only by refine-assignment-desk-inbox-ux 12.1): a staging slot for an existing
    /// Scribe item, its rows rendered Read-view-style with independent Selected checkboxes, an optional
    /// "Delete from source on send" toggle, and the target-player picker + batch-Send button — all composed
    /// by <see cref="ScribeAssignmentFormContent"/>. This player's own Sent history moved to its own tab
    /// (<see cref="ScribeDialogBase.BuildSentAssignmentHistoryContent"/>). Resolves the staged item's rows
    /// fresh on every build (mirroring <see cref="BuildReadContent"/>'s own per-build resolution) rather
    /// than caching them across rebuilds, since the staged item can change underneath this tab at any time
    /// (another client swaps it, or this player pulls/places it).</summary>
    protected override Widget BuildAssignmentContent()
    {
        var controller = EnsureSlotController();
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        var slot = assignmentDesk.Inventory[BlockEntityAssignmentDesk.StagingSlotIndex];

        ScribeDocument? stagedDoc = null;
        bool slotHasItem = slot.Itemstack != null
            && ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out stagedDoc) && stagedDoc is not null;

        // Active-source priority (add-assignment-desk-own-tasks design.md D3): a staged item always wins
        // (unchanged); otherwise, if the "pull from Desk" source has been activated, fall back to the
        // Desk's own document; otherwise empty. Re-resolved fresh every build exactly like the staged-item
        // path always has, so this needs no new caching/diffing (D3's own reasoning).
        List<ScribeBlock> deskEligibleBlocks = EligibleDeskBlocks();
        stagedBlocksCache = slotHasItem
            ? stagedDoc!.Blocks.ToList()
            : deskSourceActive
                ? deskEligibleBlocks
                : new List<ScribeBlock>();
        sourceIsDeskDocument = !slotHasItem && deskSourceActive;

        // Whether the empty-state's "pull from Desk" button should show: only when nothing is staged yet,
        // the Desk source isn't already active, and the Desk's own document actually has something to offer.
        canPullFromDesk = !slotHasItem && !deskSourceActive && deskEligibleBlocks.Count > 0;

        // Resolve rows exactly like the read view (ResolveRowItem + the same empty-Task-text filter), so a
        // staged item's (or the Desk's own pulled-in document's) picker looks like its own Read tab minus
        // the checkbox's completion meaning.
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

        // Prune any selection the current staged document no longer contains (a different item got staged,
        // or this row disappeared from it) — never carry a stale selection across a staged-item swap.
        selectedTaskIds.IntersectWith(stagedRowsCache.Select(r => r.TaskId));

        // Veil background + book watermark (triage 2026-08-31), matching
        // GuiDialogScribeScriptorium.BuildWatermarkedSlot: the book glyph paints UNDER the slot at full
        // Primary, then the slot's own semi-opaque parchment fill sits on top of it as a veil, muting it to
        // a faint watermark rather than a bold icon.
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

        return new ScribeAssignmentFormContent(
            targetPlayers: ComputeAssignmentTargetPlayers(),
            stagingSlot: stagingSlot,
            stagedRows: stagedRowsCache,
            selectedTaskIds: selectedTaskIds,
            onToggleSelected: OnToggleStagedRowSelected,
            deleteFromSource: deleteFromSource,
            onToggleDeleteFromSource: OnToggleDeleteFromSource,
            onSendBatch: OnSendAssignmentBatch,
            sending: stampActive,
            canPullFromDesk: canPullFromDesk,
            onPullFromDesk: OnPullFromDesk,
            style: RowStyle,
            scrollController: sharedScrollController);
    }

    /// <summary>The Desk's own persisted document's rows eligible to pull into the Create Assignments tab
    /// (add-assignment-desk-own-tasks design.md D2/D3) — the same empty-Task-text filter every Read-style
    /// row list already applies, so a blank in-progress Task row is never offered. Reads
    /// <see cref="ScribeDialogBase.host"/>'s <c>Document</c> — the persisted document, never the Editor
    /// tab's in-progress scratch buffer (D4) — so this always reflects the last COMMITTED edit.</summary>
    private List<ScribeBlock> EligibleDeskBlocks() =>
        host.Document.Blocks.Where(b => !b.IsTask || !string.IsNullOrWhiteSpace(b.Text)).ToList();

    /// <summary>The empty-state "pull from Desk" button's click handler (design.md D3): activates the
    /// Desk's own document as the Create Assignments tab's task source and rebuilds. Mirrors
    /// <see cref="OnToggleDeleteFromSource"/>'s shape — a plain session-flag flip plus a rebuild.</summary>
    private void OnPullFromDesk()
    {
        deskSourceActive = true;
        RebuildBody();
    }

    /// <summary>The "Submitted to Player" stamp flourish overlaid on the staging slot after a successful
    /// send (refine-assignment-desk-inbox-ux 10.3) — mirrors
    /// <see cref="GuiDialogScribeScriptorium.BuildStampOverlay"/>'s shape (a fresh <see cref="ScribeStamp"/>
    /// per generation so a re-play remounts and replays), simplified since there's only ever one slot and
    /// one label here.</summary>
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

    /// <summary>Fired once when the stamp play completes: release its controller and drop the overlay
    /// (and re-enable the Send button — 10.4) so the staged slot returns to normal. Mirrors
    /// <see cref="GuiDialogScribeScriptorium.OnStampEnded"/>.</summary>
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

    /// <summary>Toggle one staged row's selection (design.md D10/D11). Selecting a Depth-0 (parent) row
    /// additionally selects every immediately-following Depth&gt;0 row up to the next Depth-0 row — ONCE,
    /// at the moment of selection. After that every row (parent and subtasks alike) toggles independently:
    /// deselecting the parent does not deselect its subtasks, and toggling a subtask never touches the
    /// parent. Deselecting never cascades.</summary>
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

    /// <summary>Sends the selected staged rows as one independent assignment each (design.md D8-D13): mints
    /// a fresh <c>AssignmentId</c> per row client-side, carries each row's full raw block shape (read from
    /// <see cref="stagedBlocksCache"/>, NOT the display-only <see cref="stagedRowsCache"/>) so the server can
    /// reconstruct a Task/Tracker/Link/Craft assignment with no per-kind special-casing, and clears the
    /// selection + resets the delete-from-source flag on send (D13 — never a saved preference).</summary>
    private void OnSendAssignmentBatch(string targetUid)
    {
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
        });

        selectedTaskIds.Clear();
        deleteFromSource = false;
        stampGeneration++;
        stampActive = true;
        if (IsOpened()) RebuildBody();
    }

    /// <summary>Lazily create the slot controller and start watching the staging inventory — same
    /// idempotent-create + rebuild-on-change contract as
    /// <see cref="GuiDialogScribeScriptorium.EnsureSlotController"/>, minus that dialog's copy/import
    /// confirm-state resets (this dialog has no analogous armed-confirm state to clear).</summary>
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

    /// <summary>Tear down the slot controller when the dialog closes so its <c>SlotModified</c> subscription
    /// doesn't outlive the dialog. Mirrors <see cref="GuiDialogScribeScriptorium.OnGuiClosed"/>.</summary>
    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        deskSourceActive = false; // UI-only session state (design.md D3) — never persisted across opens
        if (slotController != null)
        {
            slotController.UnwatchInventory(assignmentDesk.Inventory);
            slotController.Dispose();
            slotController = null;
        }
        stampRegistry.Dispose();
        stampActive = false;
    }
}
