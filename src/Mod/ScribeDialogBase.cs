using System;
using System.Collections.Generic;
using System.Diagnostics;        // Conditional (DEBUG-only scroll trace)
using System.Linq;
using Gui;                       // GuiDialogBlockEntityBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame, VsIcon, Container, Button
using Gui.Widgets.Events;        // PointerEvent, KeyboardEvent
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, FocusNode, GestureDetector, MouseRegion, Dropdown, DropdownItem, TextField, TextFieldStyle, TextEditingController, TextSelection, TextEditingValue
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, Align, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable, Scrollbar
using Gui.Widgets.Spans;         // TextSpan
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // InventoryBase (inventory-carrying base ctor)
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

public abstract partial class ScribeDialogBase : GuiDialogBlockEntityBase
{
    protected readonly IScribeDocumentHost host;

    /// <summary>Whether the active hand item is the SAME document this dialog opened, compared by
    /// <see cref="ScribeDocument.DocId"/> (not merely "some Scribe item"). Closes on a real hotbar switch-away
    /// while tolerating a reorder that keeps the same item active. False for a non-Scribe or empty hand.</summary>
    private protected bool ActiveHandItemHostsThisDocument()
    {
        var stack = capi.World.Player?.Entity?.ActiveHandItemSlot?.Itemstack;
        if (stack?.Collectible is not IScribeDocumentItem) return false;
        return ScribeDocumentAttributes.TryReadFrom(stack, out var doc)
            && doc is not null
            && doc.DocId == host.Document.DocId;
    }

    /// <summary>True when the active hand holds SOME Scribe document item, without the DocId comparison. Used only by
    /// <c>OnHotbarSlotModified</c> (in-place re-sync), not the hand-switch path (<c>OnActiveSlotChanged</c>).
    /// <para>Why: opening a not-yet-crafted item generates a client-side DocId, and the server's re-sync of the
    /// still-held slot omits that document, so the strict DocId guard would close the dialog one frame after it opened.
    /// A slot change asks "same item?" (identity); an in-place rewrite asks "still a Scribe item?" (presence).</para></summary>
    private protected bool ActiveHandHoldsAnyScribeDocumentItem()
        => capi.World.Player?.Entity?.ActiveHandItemSlot?.Itemstack?.Collectible is IScribeDocumentItem;

    /// <summary>One scroll controller shared by all views' scroll regions, owned by the dialog so a view-switch
    /// <see cref="GuiBase.ForceRebuild"/> doesn't dispose it and the offset survives (row heights are unified across views).
    /// Widgets do not dispose it; <see cref="OnGuiClosed"/> does. An offset past a shorter view's max is clamped on layout.</summary>
    private protected readonly ScrollController sharedScrollController = new();

    // ---- View state ----
    /// <summary>The central-region view; <see cref="BuildCentralRegion"/> chooses the body from this.</summary>
    private enum ScribeLecternView { Read, Editor, Pinned, Visitors, History, Timer, Inventory, Assignment, Inbox, SentHistory, InboxInventory }

    private ScribeLecternView viewMode = ScribeLecternView.Read;

    /// <summary>TaskIds of expanded assignment rows in the Inbox/Sent History views. Dialog-owned so the title-bar
    /// expand/collapse-all toggle can read and mutate it; shared by both tabs (TaskIds are globally unique).</summary>
    private readonly HashSet<Guid> expandedAssignmentIds = new();

    /// <summary>Active Inbox/Sent-History filter-chip group; dialog-owned (like <see cref="expandedAssignmentIds"/>) so the
    /// title-bar toggle knows which rows pass the filter. Shared by both tabs.</summary>
    private ScribeAssignmentFilterGroup assignmentFilterGroup = ScribeAssignmentFilterGroup.All;

    /// <summary>Read View's active filter pill; seeded from and written back through
    /// <see cref="IScribeDocumentHost.ReadViewFilterCategory"/>. Irrelevant when <see cref="SupportsFilterPills"/> is false.</summary>
    private ReadViewFilterCategory readViewFilterCategory;

    /// <summary>The just-locally-selected pill, cleared once <see cref="RefreshReadView"/> sees the host mirror catch up.
    /// While set, <see cref="RefreshReadView"/> must not overwrite <see cref="readViewFilterCategory"/> from the stale
    /// mirror (a row mutation's refresh would otherwise revert the pick).</summary>
    private ReadViewFilterCategory? pendingReadViewFilterCategory;

    /// <summary>TaskIds of collapsed Read View subtask-group parents; same persistence and seeding as <see cref="readViewFilterCategory"/>.</summary>
    private readonly HashSet<Guid> collapsedReadViewGroupIds = new();

    /// <summary>Snapshot of <see cref="collapsedReadViewGroupIds"/> taken after a local toggle, cleared once the host mirror
    /// catches up; same stale-mirror guard as <see cref="pendingReadViewFilterCategory"/>.</summary>
    private HashSet<Guid>? pendingCollapsedReadViewGroupIds;

    /// <summary>True when the Guestbook (Visitors) tab is the active view.</summary>
    protected bool IsVisitorsView => viewMode == ScribeLecternView.Visitors;

    /// <summary>True when the History tab is the active view.</summary>
    protected bool IsHistoryView => viewMode == ScribeLecternView.History;

    /// <summary>True when the Timer tab is the active view.</summary>
    protected bool IsTimerView => viewMode == ScribeLecternView.Timer;

    /// <summary>True when the Inventory tab (Scriptorium storage) is the active view.</summary>
    protected bool IsInventoryView => viewMode == ScribeLecternView.Inventory;

    protected bool IsAssignmentView => viewMode == ScribeLecternView.Assignment;

    protected bool IsInboxView => viewMode == ScribeLecternView.Inbox;

    /// <summary>True when the Inbox Inventory tab (the Inbox block's own storage) is the active view.</summary>
    protected bool IsInboxInventoryView => viewMode == ScribeLecternView.InboxInventory;

    /// <summary>True when the Editor tab is the active view.</summary>
    protected bool IsEditorView => viewMode == ScribeLecternView.Editor;

    /// <summary>Whether another player holds the editor lock; used to dim the Edit nav button.</summary>
    protected bool EditLockedByOther => host.IsLockedByOther(capi.World.Player.PlayerUID);

    /// <summary>True when the Sent Assignment History tab is the active view. Exposed so the Assignment Desk can apply the active color to its own nav button.</summary>
    protected bool IsSentHistoryView => viewMode == ScribeLecternView.SentHistory;

    /// <summary>Whether the Inbox nav button should shimmer: an unseen received assignment exists and the Inbox tab is not already active.</summary>
    protected bool ShowInboxShimmer => modSystem.HasUnseenAssignment && !IsInboxView;

    /// <summary>Selects the Assignment tab as the initial view. Safe from a subclass ctor: it runs before
    /// <c>TryOpen</c> builds the first tree.</summary>
    protected void DefaultToAssignmentView() => viewMode = ScribeLecternView.Assignment;

    /// <summary>Selects the Inbox tab as the initial (and only) view.</summary>
    protected void DefaultToInboxView() => viewMode = ScribeLecternView.Inbox;

    /// <summary>Selects the Transcribe (Inventory) tab as the initial view.</summary>
    protected void DefaultToInventoryView() => viewMode = ScribeLecternView.Inventory;

    /// <summary>Selects the Guest Book (Visitors) tab as the initial view.</summary>
    protected void DefaultToVisitorsView() => viewMode = ScribeLecternView.Visitors;

    /// <summary>Pixel size of sidebar nav buttons.</summary>
    protected float NavButtonSize => ScribeRowConstants.RowCheckboxSize * 1.7f;

    /// <summary>Drop shadow for sidebar nav buttons.</summary>
    protected BoxShadow[] NavButtonShadow => new[]
    {
        new BoxShadow(Color: new Vector4(0f, 0f, 0f, 0.35f), Offset: new Vector2(2f, 2f), BlurRadius: 4f),
    };

    /// <summary>Whether the central region is in the EDITOR view, backed by <see cref="viewMode"/>: true selects Editor,
    /// false returns to Read. Other views set <see cref="viewMode"/> directly.</summary>
    private bool isEditorMode
    {
        get => viewMode == ScribeLecternView.Editor;
        set => viewMode = value ? ScribeLecternView.Editor : ScribeLecternView.Read;
    }

    // ---- Editor state (null / inert while in read mode) ----
    /// <summary>Editor-view-only scratch copy; never aliased to <see cref="BlockEntityScribeLectern.Document"/>.</summary>
    private ScribeDocument? scratch;
    private bool isDirty;
    private long? autosaveTickListenerId;
    /// <summary>Block index of the focused editor row, so commit-on-close/switch normalizes the right row.</summary>
    private int? focusedEditIndex;
    /// <summary>One focus node per editor row, dialog-owned so focus can move across rows (LibGUI has no focus-traversal API).
    /// Kept in sync with <c>scratch.Blocks.Count</c>.</summary>
    private readonly List<FocusNode> editorFocusNodes = new();
    /// <summary>Row index to auto-focus on the next rebuild. Rides the field's mount-only <c>autoFocus</c>, so it only
    /// applies to a newly mounted row; reused rows use <see cref="pendingFocusRow"/>.</summary>
    private int? autoFocusRowOnRebuild;
    /// <summary>Row index to re-request focus on after the next reconcile lays out, or null. For rows whose field is REUSED
    /// (delete/reorder, pin-tint repaint) and so never re-runs <c>autoFocus</c>. Drives the dialog-owned
    /// <see cref="editorFocusNodes"/> via a deferred <c>RequestFocus</c> in <see cref="OnRenderGUI"/>.</summary>
    private int? pendingFocusRow;
    /// <summary>Set when a focus move or a row growth needs the focused row scrolled into view; acted
    /// on in <see cref="OnRenderGUI"/> AFTER layout has run (EnsureVisible reads live geometry).</summary>
    private bool pendingEnsureVisible;
    /// <summary>Shared scroll offset at the end of the last rendered frame. LibGUI's <c>FocusManager.RequestFocus</c> scrolls a
    /// taller-than-viewport row to its edge on focus; <see cref="NotifyPointerFocus"/> restores this offset on click so that scroll is never seen.</summary>
    private float lastStableScrollOffset;

    // ---- Document title editing ----
    /// <summary>True while the title row is in inline-input mode (pencil was clicked, input is active).</summary>
    private bool _isTitleEditing;
    private TextEditingController? _titleController;
    private FocusNode? _titleFocusNode;
    /// <summary>Shared title editing controller/focus node. A subclass supplying its own title input MUST bind to these:
    /// <see cref="CommitTitleIfEditing"/> and the blur listener key off them. Non-null once <c>OnGuiOpened</c> has run.</summary>
    private protected TextEditingController TitleController => _titleController!;
    private protected FocusNode TitleFocusNode => _titleFocusNode!;
    /// <summary>Set by the pencil tap so the title field is focused from <see cref="OnRenderGUI"/>: calling <c>RequestFocus()</c>
    /// right after <c>ForceRebuild()</c> mid-pointer-dispatch orphans a sibling button and LibGUI NPEs in <c>ButtonState.PlaySound</c>.</summary>
    private bool _pendingTitleFocus;
    /// <summary>Set by both title edit-mode transitions so the slot-swapping <see cref="ForceRebuild"/> runs from
    /// <see cref="OnRenderGUI"/>, not inside pointer dispatch (same <c>ButtonState.PlaySound</c> NPE as <see cref="_pendingTitleFocus"/>).</summary>
    private bool _pendingTitleEditRebuild;

    // ---- Guestbook note focus ----
    /// <summary>One focus node per editable own-entry note field, keyed by <see cref="GuestbookNoteKey"/>
    /// (<c>(PlayerName, InGameDate)</c>); a shared node would paint a caret in every field. Synced by
    /// <see cref="SyncGuestbookFocusNodes"/>; disposed in <see cref="OnGuiClosed"/>.</summary>
    private readonly Dictionary<string, FocusNode> _guestbookNoteFocusNodes = new();

    // ---- Title-bar grip drag ----
    // The grip glyph's Tooltip wraps it in a MouseRegion, an ACTIVE hit target that captures the press in GuiBase.OnMouseDown
    // before the drag-zone check runs; an IgnorePointer wrapper would also kill the tooltip's hover. So the grip drives its OWN
    // window drag via a GestureDetector nested inside the tooltip. See VSAPI-NOTES.md (LibGUI).
    private bool gripDragging;
    private int gripDragStartMouseX;
    private int gripDragStartMouseY;
    private Vector2 gripDragStartWindowPos;

    /// <summary>Set by <see cref="OnRowBlurred"/> when a row lost focus while empty; removal + rebuild is deferred to
    /// <see cref="OnRenderGUI"/> because the blur fires mid-focus-transition and removing the row synchronously would dispose
    /// focus nodes and strand the pending new focus. Null when none is pending.</summary>
    private int? pendingEmptyRowRemoval;

    /// <summary>Host-owned collapse controllers for the editor's row list, keyed by TaskId so a collapse resumes across
    /// rebuild remounts. Passed to <see cref="ScribeAnimatedList"/>; disposed with the dialog.</summary>
    private readonly ScribeAnimationRegistry editorCollapseRegistry = new();

    /// <summary>Keeps the collapse-time hover refresh running a few frames past the last animating frame so it lands after the
    /// completion-triggered <see cref="ForceRebuild"/>. See <see cref="ScribeHoverRefreshLatch"/>.</summary>
    private readonly ScribeHoverRefreshLatch hoverRefreshLatch = new();

    /// <summary>Scroll offset captured before a switch-to-read rebuild and re-applied after the read view lays out: its
    /// virtualized <c>ListView</c> clamps the shared offset toward the top on first layout, before real row heights are known.
    /// Null when none is pending; see <see cref="OnRenderGUI"/>.</summary>
    private float? pendingRestoreScrollOffset;
    /// <summary>Frames spent trying to re-apply <see cref="pendingRestoreScrollOffset"/>; bounds the
    /// retry so a genuinely-shorter list (target past the real max) gives up instead of looping.</summary>
    private int scrollRestoreFrames;

    /// <summary>Set after a row removal so <see cref="OnRenderGUI"/> re-clamps the offset to the smaller <c>MaxScrollExtent</c>.
    /// LibGUI's wheel-slop clamp ignores overshoots of 50px or less, and <c>SingleChildScrollView</c> reports the shrunk
    /// <c>ContentSize</c> only a layout pass later, so this is honored over a few frames.</summary>
    private bool pendingClampToExtent;
    /// <summary>Frames spent honoring <see cref="pendingClampToExtent"/>; bounds it to the content-size
    /// settling window so it doesn't run every frame forever.</summary>
    private int clampToExtentFrames;

    // ---- Lost-lock autosave recovery ----
    /// <summary>Max consecutive autosave failures to auto-recover from before giving up, so a lock
    /// permanently held elsewhere can't spin the re-request/resend loop forever.</summary>
    private const int MaxSaveFailureRetries = 3;
    /// <summary>Count of consecutive lost-lock autosave failures; reset to 0 the moment the lock is
    /// re-acquired (a successful recovery re-grant) or a fresh editor session begins.</summary>
    private int saveFailureRetries;
    /// <summary>True between a save-failed ack and its recovery re-grant, so <see cref="EnterEditorMode"/>
    /// keeps the unsaved scratch instead of reseeding it from the authoritative document.</summary>
    private bool recoveringLostLock;

    // ---- Pin Tab state (inert unless in the Pinned view) ----
    /// <summary>In-progress text edits for Pin Tab rows, keyed by <see cref="ScribeBlock.TaskId"/> and written on every keystroke.
    /// A <see cref="ForceRebuild"/> re-seeds each field, so a row being typed re-seeds from this buffer, not the stale server
    /// snapshot. Dropped on commit or when the pin leaves the set (<see cref="DisposePinState"/>).</summary>
    private readonly Dictionary<Guid, string> pinEditBuffer = new();

    /// <summary>One focus node per Pin Tab row, keyed by <see cref="ScribeBlock.TaskId"/> (pin order can change). Dialog-owned so a
    /// caret survives rebuilds via <see cref="autoFocusPinTaskId"/>. Synced by <see cref="SyncPinFocusNodes"/>; disposed in <see cref="OnGuiClosed"/>.</summary>
    private readonly Dictionary<Guid, FocusNode> pinFocusNodes = new();

    /// <summary>Host-owned collapse controllers for the Pin Tab's departing rows, passed to <see cref="ScribePinnedContent"/>'s
    /// <see cref="ScribeAnimatedList"/>. Dialog-owned so a collapse resumes across the resync and <see cref="OnRenderGUI"/> can
    /// read <c>AnyAnimating</c>. Disposed with the dialog.</summary>
    private readonly ScribeAnimationRegistry pinCollapseRegistry = new();

    /// <summary>Host-owned collapse controllers for the read view's departing rows, passed to <see cref="ScribeReadContent"/>'s
    /// <see cref="ScribeAnimatedList"/>. Dialog-owned so a collapse resumes across the RefreshReadView reconcile and
    /// <see cref="OnRenderGUI"/> can read <c>AnyAnimating</c>. Disposed with the dialog.</summary>
    private readonly ScribeAnimationRegistry readCollapseRegistry = new();

    /// <summary>True while any row-collapse animation (editor, Pin Tab, read view) is running. Shared gate for the two
    /// <see cref="OnRenderGUI"/> collapse loops (scroll-pin and hover-refresh latch); add any new surface's registry here.</summary>
    private bool AnyRowAnimating =>
        editorCollapseRegistry.AnyAnimating || pinCollapseRegistry.AnyAnimating || readCollapseRegistry.AnyAnimating;

    /// <summary><see cref="ScribeBlock.TaskId"/> of the focused Pin Tab row, so a rebuild can restore the caret and a focus move
    /// can commit the row being left. Not cleared on blur (the listener fires only on focus gained).</summary>
    private Guid? focusedPinTaskId;

    /// <summary>Pin row to auto-focus on the next Pin Tab rebuild, or null. One-shot, consumed in <see cref="BuildPinnedContent"/>.
    /// Mount-only <c>autoFocus</c>, so only for freshly mounted rows; in-place resyncs use <see cref="pendingFocusPinTaskId"/>.</summary>
    private Guid? autoFocusPinTaskId;

    /// <summary>Pin row to re-home keyboard focus onto after an in-place Pin Tab reconcile, or null. One-shot. The Pin Tab
    /// counterpart of <see cref="pendingFocusRow"/>: a reused field skips <c>autoFocus</c>, so a deferred
    /// <see cref="FocusNode.RequestFocus"/> runs from <c>OnRenderGUI</c> once the element has a live Owner.</summary>
    private Guid? pendingFocusPinTaskId;

    /// <summary>The mod system, cached for per-player pin queries and the pin/complete network sends.</summary>
    protected readonly ScribeModSystem modSystem;

    /// <summary>Samples the light reaching the player each frame into a quantized (brightness, tint) shade. Render-thread only;
    /// bound to the live <see cref="ScribeModSystem.MySettings"/>.</summary>
    private readonly ScribeAmbientLightSampler lightSampler;

    /// <summary>Current quantized illumination shade, refreshed each frame from <see cref="lightSampler"/> and used by
    /// <see cref="BuildBodyTree"/> for the <see cref="ScribeGlobalTint"/> wrap. The identity default is re-primed in the ctor
    /// before the first build (avoiding an open-in-darkness bright flash). A changed value triggers <see cref="RebuildBody"/>.</summary>
    private ScribeAmbientLightSampler.Shade currentShade = new(1f, 1f, 1f, 1f, changed: false);

    /// <summary>The live shade, for subclasses building Overlay-layer hover surfaces that shade at hover strength.</summary>
    // private protected because the Shade type is internal.
    private protected ScribeAmbientLightSampler.Shade CurrentShade => currentShade;

    /// <summary>Identity of the persistent-root <see cref="ScribeDialogBody"/>. Allocated once (never in <see cref="Build"/>, per the
    /// <see cref="GlobalKey"/> contract) so <see cref="RebuildBody"/> can reconcile in place; it re-registers on mount, so it survives <c>ForceRebuild</c>.</summary>
    private readonly GlobalKey bodyKey = new();

    /// <summary>Reconciles the dialog body in place instead of <see cref="GuiBase.ForceRebuild"/>: a no-op <c>SetState</c> re-runs
    /// <see cref="BuildBodyTree"/> next frame, reusing matching elements (rows keep caret + buffer). Reserve <c>ForceRebuild()</c>
    /// for new-tree cases (view switches, fresh editor seed, lost-lock recovery). No-op before the body mounts.</summary>
    private protected void RebuildBody() => bodyKey.CurrentState<ScribeDialogBody.BodyState>()?.Rebuild();

    /// <param name="inventory">Block-entity inventory to bind (opened/closed with the dialog), or <c>null</c> for document-only surfaces.</param>
    protected ScribeDialogBase(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi, InventoryBase? inventory = null)
        : base(pos, inventory, capi)
    {
        this.host = host;
        modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();

        // Seed the Read View filter pill + collapsed groups from persisted per-instance state.
        readViewFilterCategory = (ReadViewFilterCategory)host.ReadViewFilterCategory;
        collapsedReadViewGroupIds.UnionWith(host.CollapsedGroupIds);

        // Light sampler for the ambient-illumination shade; render thread only.
        lightSampler = new ScribeAmbientLightSampler(capi, modSystem.MySettings, modSystem.VisualTuning);

        // Prime the shade before TryOpen inflates the tree (Build runs before OnGuiOpened), so a dialog opened in darkness
        // doesn't paint one bright frame. The sampler's first Sample snaps to target, so the first OnRenderGUI sample reports
        // Changed=false. dt=0 is ignored on that first call.
        currentShade = lightSampler.Sample(0f);

        // Install the silent UI sound player when muted; re-applied in OnMyPinsChanged.
        ApplyUiSoundPreference();

        // Repaint pin indicators when this player's pin set changes. Unsubscribed in OnGuiClosed.
        modSystem.MyPinsChanged += OnMyPinsChanged;

        // Recolor the Settings nav button when the standalone settings window opens/closes. Unsubscribed in OnGuiClosed.
        modSystem.SettingsVisibilityChanged += OnSettingsVisibilityChanged;

        // Refresh an open Inbox/Assignment tab when assignment views resync. Unsubscribed in OnGuiClosed.
        modSystem.MyAssignmentsChanged += OnMyAssignmentsChanged;

        // Refresh an open Create Assignments tab on a known-players sync. Unsubscribed in OnGuiClosed.
        modSystem.KnownPlayersChanged += OnKnownPlayersChanged;

        // Restrict Tab / Shift+Tab to this dialog's editable text fields: gui@3.1.0 makes checkboxes focusable and the stock
        // ReadingOrderTraversalPolicy would stop on them, with no public seam to exclude them. The allow-list policy returns
        // only our field nodes (see EditorFieldTraversalNodes).
        FocusManager.TraversalPolicy = new ScribeFieldOnlyTraversalPolicy(EditorFieldTraversalNodes);

        _titleController = new TextEditingController("");
        _titleFocusNode = new FocusNode();
        _titleFocusNode.AddListener(OnTitleFocusChanged);

#if DEBUG
        // Logs every offset change on the shared controller, including LibGUI's internal clamp JumpTo that our own loops can't see.
        // DEBUG-only.
        sharedScrollController.OnChanged += OnScrollControllerChanged;
#endif
    }

    /// <summary>Override to supply additional nav buttons between the Pins tab and the Settings gear (always last).</summary>
    protected virtual IEnumerable<Widget> GetExtraNavButtons() => Array.Empty<Widget>();

    /// <summary>Override to supply nav buttons BEFORE the Read tab, without forking <see cref="BuildRightColNav"/>'s layout plumbing.</summary>
    protected virtual IEnumerable<Widget> GetLeadingNavButtons() => Array.Empty<Widget>();

#if DEBUG
    /// <summary>DEBUG-only: traces every <see cref="sharedScrollController"/> offset change.</summary>
    private void OnScrollControllerChanged() => TraceScroll("changed");
#endif

    /// <summary>DEBUG-only scroll trace (compiled out in Release). Emits scroll state, view, focus and pending scroll-intent flags
    /// to <c>client-main.log</c>; <paramref name="tag"/> names the call site.</summary>
    [Conditional("DEBUG")]
    private void TraceScroll(string tag)
    {
        capi.Logger.Notification(
            "[scribe-scroll] {0,-14} off={1,7:0.0} max={2,7:0.0} view={3} focus={4} ensureVis={5} restore={6}(f{7}) clamp={8}(f{9})",
            tag,
            sharedScrollController.Offset,
            sharedScrollController.MaxScrollExtent,
            viewMode,
            focusedEditIndex?.ToString() ?? "-",
            pendingEnsureVisible,
            pendingRestoreScrollOffset?.ToString("0.0") ?? "-",
            scrollRestoreFrames,
            pendingClampToExtent,
            clampToExtentFrames);
    }

    /// <summary>Installs the silent sound player when <c>MuteUiSounds</c> is set, else the stock one. Called from the ctor and on settings change.</summary>
    private void ApplyUiSoundPreference()
        => BuildOwner.SetSoundPlayer(modSystem.GetUiSoundPlayer(capi));

    /// <summary>Whether THIS player has pinned the task in this document, via the optimism-aware
    /// <see cref="ScribeModSystem.IsPinnedForMe"/> so every surface agrees immediately.</summary>
    private bool IsPinnedForMe(Guid taskId) => modSystem.IsPinnedForMe(host.Document.DocId, taskId);

    /// <summary>Whether the tier cap still permits adding one more block of ANY kind (<see cref="ScribeDocument.BlockCount"/>).
    /// Counts the live scratch while editing, else the host document. Uncapped tiers always return true.</summary>
    private bool CanAddTaskUnderPolicy()
    {
        var doc = scratch ?? host.Document;
        return host.Policy.CanAdd(doc.BlockCount);
    }

    /// <summary>Index for a document-level create (footer Add, quick-add, Handbook), from the player's
    /// New Task Insert setting. Top → 0; Bottom → scratch count. Callers must have a live scratch.</summary>
    private int NewTaskInsertIndex()
    {
        var pos = ScribePlayerSettings.NormalizeNewTaskInsert(modSystem.MySettings.NewTaskInsert);
        return scratch!.InsertIndex(pos);
    }

    /// <summary>Lang key for the "task cap reached" notice from <see cref="NotifyTabletFull"/>. Default <c>scribe:tablet-full</c>;
    /// the chalkboard overrides it so a board never says "tablet".</summary>
    protected virtual string TaskCapReachedLangKey => "scribe:tablet-full";

    /// <summary>Raises the "task cap reached" error when an add is refused by a finite cap; inert for uncapped tiers.
    /// Wording comes from <see cref="TaskCapReachedLangKey"/>.</summary>
    private void NotifyTabletFull()
    {
        if (host.Policy.MaxBlocks is int)
        {
            capi.TriggerIngameError(this, "scribe-tablet-full", Lang.Get(TaskCapReachedLangKey));
        }
    }

    /// <summary>Toggles THIS player's pin with SWAP semantics at a finite <see cref="ScribeDocumentPolicy.MaxPins"/> cap: the oldest
    /// pins for this document are released first so the new pin fits. Unpinning is always allowed; uncapped tiers just pin.
    /// <see cref="SendSetPin"/> applies the optimistic overlay, so subscribers repaint immediately.</summary>
    private void TogglePinWithPolicy(Guid taskId)
    {
        bool willPin = !IsPinnedForMe(taskId);
        if (!willPin)
        {
            SendSetPin(taskId, false);
            return;
        }
        ReleasePinsToFitPolicy(taskId);
        SendSetPin(taskId, true);
    }

    /// <summary>Releases the oldest pins for this document (pin order) until one more pin fits the
    /// <see cref="ScribeDocumentPolicy.MaxPins"/> cap. No-op for uncapped tiers.</summary>
    private void ReleasePinsToFitPolicy(Guid newlyPinnedTaskId)
    {
        if (host.Policy.MaxPins is not int max) return; // uncapped tier — never swap
        Guid docId = host.Document.DocId;
        var existing = modSystem.MyPins
            .Where(p => p.OwnerDocId == docId && p.TaskId != newlyPinnedTaskId)
            .ToList();
        // Adding one more must leave the count at <= max, so release (count + 1 - max) oldest pins.
        int toRelease = existing.Count + 1 - max;
        for (int i = 0; i < toRelease && i < existing.Count; i++)
        {
            SendSetPin(existing[i].TaskId, false);
        }
    }

    /// <summary>A pin-set/settings push arrived: repaint the pin indicators. No-op while closed. Pin state is per-player, not part
    /// of scratch, so it doesn't disturb an in-progress edit.
    /// <para>Pressing a per-row pin control (not <c>IFocusable</c>) blurs the focused field via <c>DispatchPointerDown</c>, and this
    /// rebuild lands asynchronously, so the caret is re-homed on the still-focused row (blur doesn't clear the focused index).</para></summary>
    private void OnMyPinsChanged()
    {
        if (!IsOpened()) return;
        TraceScroll("pins-changed");
        // The optimistic pin overlay is reconciled in ScribeModSystem before this event fires.
        // A settings change may have flipped the mute preference.
        ApplyUiSoundPreference();
        // Editor view: a pin change is a pure chrome repaint, so an in-place RebuildBody reuses every row (caret + buffer intact).
        // Re-home focus via the deferred pendingFocusRow, since a reused field skips its mount-only autoFocus.
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx && idx < editorFocusNodes.Count) pendingFocusRow = idx;
            RebuildBody();
            return;
        }

        // Pin Tab: same in-place reconcile (rows are TaskId-keyed and re-seed from pinEditBuffer). Re-home the caret via
        // pendingFocusPinTaskId if that pin still exists; autoFocusPinTaskId wouldn't re-fire on a reused field.
        // Reconcile preserves the shared scroll offset in place, so no capture-restore.
        if (viewMode == ScribeLecternView.Pinned)
        {
            if (focusedPinTaskId is { } pinId && modSystem.MyPins.Any(p => p.TaskId == pinId))
                pendingFocusPinTaskId = pinId;
            // A completion can re-order the list under a stationary cursor; a reconcile reuses rows with stale hover flags, so arm the
            // hover latch to re-dispatch a pointer-move.
            hoverRefreshLatch.Arm();
            RebuildBody();
            return;
        }

        // Read view: a pin change is a pure chrome repaint; the list is non-virtualized + TaskId-keyed, so RebuildBody reuses every
        // row (scroll offset preserved). Arm the hover latch in case a completion slid a row under the cursor.
        if (viewMode == ScribeLecternView.Read)
        {
            hoverRefreshLatch.Arm();
            RebuildBody();
            return;
        }
        // Other views (History/Timer/Visitors) ForceRebuild; the OnRenderGUI restore loop re-applies the captured offset.
        CaptureScrollForRestore();
        ForceRebuild();
    }

    /// <summary>Rebuilds so the Settings nav button picks up or drops its active color when the standalone settings window
    /// opens/closes. ForceRebuild paths capture the scroll offset first because the rebuild clamps it toward 0.</summary>
    private void OnSettingsVisibilityChanged()
    {
        if (!IsOpened()) return;
        TraceScroll("settings-vis");
        // Editor: the nav column is in the persistent body, so RebuildBody re-tints the button while reusing rows. Re-home the
        // caret in case an earlier press cleared focus.
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx && idx < editorFocusNodes.Count) pendingFocusRow = idx;
            RebuildBody();
            return;
        }
        // Pin Tab: same reconcile; re-home the focused row via pendingFocusPinTaskId.
        if (viewMode == ScribeLecternView.Pinned)
        {
            if (focusedPinTaskId is { } pinId && modSystem.MyPins.Any(p => p.TaskId == pinId))
                pendingFocusPinTaskId = pinId;
            RebuildBody();
            return;
        }
        // Read view: same reconcile; the list is non-virtualized + TaskId-keyed.
        if (viewMode == ScribeLecternView.Read)
        {
            RebuildBody();
            return;
        }
        CaptureScrollForRestore();
        ForceRebuild();
    }

    protected override WindowConfig CreateWindowConfig()
    {
        // The whole dialog is the notebook art's OuterArtBox: size it to W x H so the backdrop renders un-distorted. W is the Pixel Art
        // Size preference, read fresh per open. Non-resizable to keep the aspect; DragHandleHeight matches the title-bar band.
        var layout = host.GetLayout(modSystem.MySettings.PixelArtSize);
        return new WindowConfig
        {
            Size = new Vector2(layout.W, layout.H),
            Draggable = true,
            Resizable = false,
            DragHandleHeight = layout.TitleBarH,
        };
    }

    /// <summary>
    /// The engine inflates <c>PickingRange</c> to ~100 blocks in Creative, which would defeat <c>OnFinalizeFrame</c>'s walk-away
    /// auto-close; pin it to the survival interaction distance so it fires in every game mode.
    /// </summary>
    protected override double InteractionRange => GlobalConstants.DefaultPickingRange + 0.5;

    /// <summary>
    /// Matches the vanilla Inventory/Handbook/chest <c>DrawOrder</c> band (0.2) so Scribe stacks correctly against other LibGUI
    /// windows sharing the Skia surface. It does NOT fix overlap with vanilla Cairo/GL dialogs (a pipeline-ordering problem with
    /// no fix here) — see the DrawOrder notes in VSAPI-NOTES.md.
    /// </summary>
    public override double DrawOrder => 0.2;

    /// <summary>
    /// Declines a click inside a vanilla Cairo/GL dialog's bounds (<see cref="ScribeVanillaDialogGuard.IsVanillaDialogAt"/>), leaving
    /// <c>args.Handled</c> false so <c>GuiManager.OnMouseDown</c> keeps walking to the vanilla dialog. Vanilla always paints over LibGUI
    /// regardless of focus, so bounds alone decide "Scribe is underneath here" (see <c>DrawOrder</c>).
    /// <para>Lives in <c>OnMouseDown</c> rather than <see cref="Vintagestory.API.Client.GuiDialog.ShouldReceiveMouseEvents"/> because only it
    /// receives the click point. GuiBase's private <c>IsBlockedByFrontDialog</c> is inaccessible and unreliable across the Cairo/Skia boundary.</para>
    /// </summary>
    public override void OnMouseDown(MouseEvent args)
    {
        if (args.Handled) return;
        if (ScribeVanillaDialogGuard.IsVanillaDialogAt(capi, args.X, args.Y)) return;
        base.OnMouseDown(args);
    }

    // ---------------- Input capture + macOS caret translation ----------------

    /// <summary>
    /// Captures ALL input only while an editor or Pin-Tab field actually holds focus, so typed keys don't reach the game.
    /// <see cref="OnRowFocusChanged"/> fires only on focus gained, so <see cref="focusedEditIndex"/> stays set after a click-away;
    /// the live <see cref="FocusNode.HasFocus"/> check drops capture then.
    /// <para>Gated on <see cref="Vintagestory.API.Client.GuiDialog.Focused"/>: <c>GuiManager.OnKeyDown</c>'s first pass runs over every
    /// dialog whose <see cref="CaptureAllInputs"/> is true, and each <see cref="GuiBase"/> has its OWN <c>FocusManager</c>, so an
    /// unfocused editor would still report <c>HasFocus</c> and steal the settings window's arrow keys.</para>
    /// </summary>
    public override bool CaptureAllInputs()
        => Focused
        && ((isEditorMode && focusedEditIndex is { } idx && idx < editorFocusNodes.Count && editorFocusNodes[idx].HasFocus)
        || (viewMode == ScribeLecternView.Pinned && focusedPinTaskId is { } pinId
            && pinFocusNodes.TryGetValue(pinId, out var pn) && pn.HasFocus)
        || _guestbookNoteFocusNodes.Values.Any(n => n.HasFocus));

    /// <summary>
    /// LibGUI's <see cref="Gui.Widgets.Events.KeyboardEvent"/> drops VS's Command modifier, so the macOS caret idioms are translated
    /// here before <c>base.OnKeyDown</c>: Cmd+Left/Right to Home/End, Cmd+Up/Down to Ctrl+Up/Down (first/last row), Cmd+A/C/X/V to
    /// Ctrl+A/C/X/V. Alt+Arrow word-skip already arrives as Alt. Mirrors the native <c>ScribeRowTextInput.TranslateMacCaretModifiers</c>.
    /// </summary>
    public override void OnKeyDown(KeyEvent args)
    {
        if (isEditorMode && args.CommandPressed)
        {
            switch (args.KeyCode)
            {
                case (int)GlKeys.Left:
                    args.KeyCode = (int)GlKeys.Home;
                    args.CommandPressed = false;
                    break;
                case (int)GlKeys.Right:
                    args.KeyCode = (int)GlKeys.End;
                    args.CommandPressed = false;
                    break;
                case (int)GlKeys.Up:
                case (int)GlKeys.Down:
                    // Cmd+Up/Down = document top/bottom on macOS: re-flag as Ctrl to give the field the first/last-row jump.
                    // Alt/Option+Up/Down stays a one-line caret move.
                    args.CtrlPressed = true;
                    args.CommandPressed = false;
                    break;
                case (int)GlKeys.A:
                case (int)GlKeys.C:
                case (int)GlKeys.X:
                case (int)GlKeys.V:
                    args.CtrlPressed = true;
                    args.CommandPressed = false;
                    break;
            }
        }

        base.OnKeyDown(args);
    }
}
