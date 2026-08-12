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
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

public abstract partial class ScribeDialogBase : GuiDialogBlockEntityBase
{
    protected readonly IScribeDocumentHost host;

    /// <summary>Whether the player's active hand item is still the SAME document this dialog opened, keyed
    /// by the stable <see cref="ScribeDocument.DocId"/>. The item-hosted dialogs (Notebook, Clockmaker's
    /// Notebook, Tablet) close when the player switches hotbar slots away from the item they opened — but
    /// the switch guard must compare document IDENTITY, not merely "is the new hand item some Scribe item."
    /// An earlier guard tested <c>ActiveHandItemSlot is IScribeDocumentItem</c>, which wrongly kept the
    /// dialog OPEN when the player scrolled/keyed the hotbar to a DIFFERENT Scribe item (e.g. a second
    /// notebook, or a tablet): the old dialog stayed up, still bound to the now-unheld document. Comparing
    /// the DocId closes on any real switch-away while still tolerating a hotbar reorder that keeps the same
    /// item active (its DocId is unchanged). A non-Scribe hand item (or an empty hand) has no document, so
    /// this returns false and the dialog closes. Reads the doc straight off the stack attributes — the same
    /// source the host was seeded from — so it needs no live host↔slot back-reference.</summary>
    private protected bool ActiveHandItemHostsThisDocument()
    {
        var stack = capi.World.Player?.Entity?.ActiveHandItemSlot?.Itemstack;
        if (stack?.Collectible is not IScribeDocumentItem) return false;
        return ScribeDocumentAttributes.TryReadFrom(stack, out var doc)
            && doc is not null
            && doc.DocId == host.Document.DocId;
    }

    /// <summary>The presence half of <see cref="ActiveHandItemHostsThisDocument"/>, WITHOUT the DocId
    /// comparison: true when the active hand still holds SOME Scribe document item, regardless of which
    /// document. Used ONLY by the item-hosted dialogs' <c>OnHotbarSlotModified</c> (an in-place same-slot
    /// content re-sync), NOT by the real hand-switch path (<c>OnActiveSlotChanged</c>), which keeps the
    /// strict DocId identity check above.
    ///
    /// <para>Why the two triggers need different rules (fix-item-dialog-first-open-flicker): opening a
    /// not-yet-crafted item makes the client generate a fresh <see cref="ScribeDocument"/>/<c>DocId</c>
    /// locally and notify the server. The server records the one-time "Picked up" history entry,
    /// <c>MarkDirty()</c>s the slot, and re-syncs the stack back — deliberately WITHOUT the client's
    /// document (it can't know the client-generated DocId). That re-sync fires <c>SlotModified</c> on the
    /// STILL-HELD slot; the strict guard read a stack whose DocId no longer matched and closed the dialog
    /// one frame after it opened (the "first open flickers closed, second sticks" bug). A slot-number change
    /// asks "am I still holding the item this dialog is for?" — identity is right. An in-place content
    /// rewrite of the slot I'm still holding asks "did the thing in my hand stop being a Scribe item?" —
    /// presence is right, because the physical item didn't change, only its bytes were re-synced. The tablet's
    /// legitimate wet→hard/fired transition also rides <c>SlotModified</c> but carries the document (same
    /// DocId, same <c>IScribeDocumentItem</c>), so it passes the presence check just as it passed the strict
    /// one — the fix is additive there.</para></summary>
    private protected bool ActiveHandHoldsAnyScribeDocumentItem()
        => capi.World.Player?.Entity?.ActiveHandItemSlot?.Itemstack?.Collectible is IScribeDocumentItem;

    /// <summary>One scroll controller shared by BOTH views' scroll regions, owned by the dialog rather
    /// than each view's <c>State</c>. Because a view switch is a <see cref="GuiBase.ForceRebuild"/> that
    /// tears down the outgoing view's <c>State</c> (which would dispose a State-owned controller and
    /// lose the offset), sharing one dialog-lived controller keeps the scroll position across the
    /// switch — and since row heights are unified across views (<see cref="ScribeRowStyle"/>), the same
    /// offset shows the same rows. Passed into the <c>ListView</c>/<c>SingleChildScrollView</c>, which
    /// then do NOT dispose it (they only dispose their own internal fallback); the dialog disposes it in
    /// <see cref="OnGuiClosed"/>. An offset past a shorter view's max is clamped on layout.</summary>
    private protected readonly ScrollController sharedScrollController = new();

    // ---- View state ----
    /// <summary>The Lectern dialog's central-region view. Read and Editor are the original two views;
    /// Pinned is the Pin Tab (scribe-pin-editor) — a peer view listing the player's pins, selected from the
    /// <c>scribepin</c> nav button. <see cref="BuildCentralRegion"/> chooses the body from this.</summary>
    private enum ScribeLecternView { Read, Editor, Pinned, Visitors, History, Timer }

    private ScribeLecternView viewMode = ScribeLecternView.Read;

    /// <summary>True when the Guestbook (Visitors) tab is the active view. Exposed so subclasses
    /// can apply the active color to their Guestbook nav button in <see cref="GetExtraNavButtons"/>.</summary>
    protected bool IsVisitorsView => viewMode == ScribeLecternView.Visitors;

    /// <summary>True when the History tab is the active view. Exposed so subclasses can apply the
    /// active color to their History nav button in <see cref="GetExtraNavButtons"/>.</summary>
    protected bool IsHistoryView => viewMode == ScribeLecternView.History;

    /// <summary>True when the Timer tab is the active view. Exposed so subclasses can apply the
    /// active color to their Timer nav button in <see cref="GetExtraNavButtons"/>.</summary>
    protected bool IsTimerView => viewMode == ScribeLecternView.Timer;

    /// <summary>The pixel size used for sidebar nav buttons — subclasses call this when building
    /// their own nav buttons via <see cref="GetExtraNavButtons"/> so the size matches.</summary>
    protected float NavButtonSize => ScribeRowConstants.RowCheckboxSize * 1.7f;

    /// <summary>The drop shadow applied to sidebar nav buttons. Subclasses use this when building
    /// extra nav buttons in <see cref="GetExtraNavButtons"/>.</summary>
    protected BoxShadow[] NavButtonShadow => new[]
    {
        new BoxShadow(Color: new Vector4(0f, 0f, 0f, 0.35f), Offset: new Vector2(2f, 2f), BlurRadius: 4f),
    };

    /// <summary>Whether the central region is in the (lock-gated) EDITOR view. Backed by
    /// <see cref="viewMode"/> so all the editor-lifecycle code that toggled a bool keeps working unchanged:
    /// setting it true selects the Editor view; setting it false returns to the Read view (Editor is the
    /// only view that ever sets this, so false → Read matches the original semantics). Switching to the
    /// Pin Tab sets <see cref="viewMode"/> directly (via <see cref="EnterPinnedMode"/>) after tearing the
    /// editor down.</summary>
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
    /// <summary>The block index of the editor row currently focused, tracked from the rows' focus
    /// nodes so the commit-on-close/switch path can normalize the right row.</summary>
    private int? focusedEditIndex;
    /// <summary>One focus node per editor row, owned by the dialog (not the row widgets) so focus can
    /// be moved across rows programmatically (Enter/Shift+Tab) — LibGUI has no focus-traversal API,
    /// so the parent coordinates focus manually. Kept in sync with <c>scratch.Blocks.Count</c>.</summary>
    private readonly List<FocusNode> editorFocusNodes = new();
    /// <summary>Row index to auto-focus on the next editor rebuild (a newly added task), or null. Rides
    /// the field's mount-only <c>autoFocus</c>, so it only re-homes focus onto a row whose field genuinely
    /// MOUNTS this rebuild — i.e. a brand-new appended/inserted row. On the reconcile path a REUSED row's
    /// field does not re-init, so <see cref="pendingFocusRow"/> covers those (see its remarks).</summary>
    private int? autoFocusRowOnRebuild;
    /// <summary>Row index to re-request keyboard focus on AFTER the next reconcile lays out
    /// (reconcile-animating-surfaces §3.1), or null. The reconcile-path counterpart to
    /// <see cref="autoFocusRowOnRebuild"/>: a mutation that REUSES the target row's element (delete/reorder
    /// re-home, pin-tint repaint) can't rely on the field's <c>autoFocus</c>, which fires only in the
    /// field's <c>InitState</c> (on mount) — a reused field never re-inits. Instead we drive focus through
    /// the dialog-owned <see cref="editorFocusNodes"/> (which persist across everything) via a deferred
    /// <c>RequestFocus</c> in <see cref="OnRenderGUI"/> once the node's element is live, exactly as
    /// <see cref="pendingEnsureVisible"/> defers scroll-into-view. Works for reused AND remounted rows, so
    /// it is the general re-home; <c>autoFocus</c> stays only as the mount-time path for genuinely-new rows.</summary>
    private int? pendingFocusRow;
    /// <summary>Set when a focus move or a row growth needs the focused row scrolled into view; acted
    /// on in <see cref="OnRenderGUI"/> AFTER layout has run (EnsureVisible reads live geometry).</summary>
    private bool pendingEnsureVisible;

    // ---- Document title editing ----
    /// <summary>True while the title row is in inline-input mode (pencil was clicked, input is active).</summary>
    private bool _isTitleEditing;
    private TextEditingController? _titleController;
    private FocusNode? _titleFocusNode;
    /// <summary>The shared title editing controller/focus node, exposed to a subclass that supplies its own
    /// title input widget (the tablet's cuneiform title, add-tablet-cuneiform-chrome). A subclass MUST bind
    /// its input to these — the commit machinery (<see cref="CommitTitleIfEditing"/>) and the blur listener
    /// key off them — rather than creating its own. Non-null once <c>OnGuiOpened</c> has run.</summary>
    private protected TextEditingController TitleController => _titleController!;
    private protected FocusNode TitleFocusNode => _titleFocusNode!;
    /// <summary>Set by the pencil tap so the freshly-rebuilt title field is focused from
    /// <see cref="OnRenderGUI"/>, not inside the tap: calling <c>RequestFocus()</c> right after a
    /// <c>ForceRebuild()</c> mid-pointer-dispatch orphans a sibling button (null <c>Element.Owner</c>) and
    /// LibGUI NPEs in <c>ButtonState.PlaySound</c>. Same defer-out-of-input rule as
    /// <see cref="pendingEnsureVisible"/>.</summary>
    private bool _pendingTitleFocus;
    /// <summary>Set by both title edit-mode transitions (pencil tap and blur commit) so the
    /// <see cref="ForceRebuild"/> that swaps the title slot runs from <see cref="OnRenderGUI"/>, not inside
    /// pointer dispatch: the rebuild itself unmounts the tree mid-walk and triggers the same
    /// <c>ButtonState.PlaySound</c> NPE, so deferring only the follow-up focus
    /// (<see cref="_pendingTitleFocus"/>) is not enough — the whole rebuild must defer to the safe
    /// post-dispatch render point.</summary>
    private bool _pendingTitleEditRebuild;

    // ---- Guestbook note focus ----
    /// <summary>One focus node per editable own-entry note field, keyed by the entry's natural key
    /// <c>(PlayerName, InGameDate)</c> (see <see cref="GuestbookNoteKey"/>). A player may have several
    /// entries (one per in-game day visited), each with its own editable note; a single shared node made
    /// every own field paint a caret at once and routed input to just one of them. Keyed by identity (like
    /// <see cref="pinFocusNodes"/>) so each field is caret-isolated. Kept in sync by
    /// <see cref="SyncGuestbookFocusNodes"/> on each Guestbook rebuild; disposed in <see cref="OnGuiClosed"/>.</summary>
    private readonly Dictionary<string, FocusNode> _guestbookNoteFocusNodes = new();

    // ---- Title-bar grip drag (§8.1) ----
    // The title-bar band is the drag zone (WindowConfig.DragHandleHeight), but a press ON the grip glyph
    // was swallowed instead of moving the window: the grip's Tooltip wraps its child in a MouseRegion,
    // which is an ACTIVE hit target (it handles enter/exit for hover), so GuiBase.OnMouseDown's
    // EventDispatcher.DispatchPointerDown captures it and returns BEFORE the IsInDragZone band check ever
    // runs (see GuiBase.cs). Click-through can't coexist with the tooltip — an IgnorePointer wrapper makes
    // the WHOLE subtree invisible to hit-testing, which would also kill the MouseRegion's hover and thus
    // the tooltip. So the grip drives its OWN window drag: a GestureDetector nested INSIDE the tooltip (so
    // hover still fires on the outer MouseRegion) moves the window from press→move→release, mirroring what
    // GuiBase's band drag does. See VSAPI-NOTES.md (§LibGUI). These track the drag in progress.
    private bool gripDragging;
    private int gripDragStartMouseX;
    private int gripDragStartMouseY;
    private Vector2 gripDragStartWindowPos;

    /// <summary>Set by <see cref="OnRowBlurred"/> when a task row lost focus while empty, so the row's
    /// removal + rebuild is deferred to the next <see cref="OnRenderGUI"/> (add-empty-task-lifecycle).
    /// The blur fires from inside the field's focus-notification (and, on a row→row move, mid-way
    /// through <c>FocusManager.RequestFocus</c> — the old node blurs before the new one focuses), so
    /// removing the row synchronously would dispose focus nodes mid-transition and strand the pending
    /// new focus. Deferring is the same re-entrancy guard the container's self-cleanup relies on.
    /// Null when no empty row is awaiting removal.</summary>
    private int? pendingEmptyRowRemoval;

    /// <summary>Host-owned collapse controllers for the editor's row list, keyed by TaskId so a collapse
    /// RESUMES (not restarts) across the dialog's rebuild remounts (scribe-list-collapse). Passed into
    /// <see cref="ScribeAnimatedList"/>, which diffs the editor's TaskId-keyed row set and animates
    /// departures against it (D0 / extract-animated-task-list §6.1 — the editor no longer hand-wires its own
    /// departing-row map or collapse-cleanup flag). Disposed with the dialog.</summary>
    private readonly ScribeAnimationRegistry editorCollapseRegistry = new();

    /// <summary>Keeps the collapse-time hover refresh running a few frames past the last animating frame so a
    /// refresh lands AFTER the completion-triggered <see cref="ForceRebuild"/> re-lays-out the fresh tree
    /// (fix-list-collapse-stale-hover); without it the row under a stationary cursor loses its hover controls
    /// exactly when the collapse ends. See <see cref="ScribeHoverRefreshLatch"/>.</summary>
    private readonly ScribeHoverRefreshLatch hoverRefreshLatch = new();

    /// <summary>Scroll offset captured just before a switch-to-read rebuild, to be re-applied after the
    /// read view lays out. Needed because the read view's virtualized <c>ListView</c> re-derives its
    /// content height (hence <c>MaxScrollExtent</c>) from <c>estimatedItemHeight</c> on the FIRST layout
    /// after the swap, so the shared controller's offset gets clamped toward the top before the real row
    /// heights are known — the edit→read half of the "scroll survives the switch" behavior. (read→edit
    /// doesn't need this: the editor's non-virtualized scroll view measures full content immediately.)
    /// Null when no restore is pending. See <see cref="OnRenderGUI"/> for the re-apply.</summary>
    private float? pendingRestoreScrollOffset;
    /// <summary>Frames spent trying to re-apply <see cref="pendingRestoreScrollOffset"/>; bounds the
    /// retry so a genuinely-shorter list (target past the real max) gives up instead of looping.</summary>
    private int scrollRestoreFrames;

    /// <summary>Set after a structural row removal (a delete) so <see cref="OnRenderGUI"/> re-clamps the
    /// shared controller's offset down to the new, smaller <c>MaxScrollExtent</c> once layout reports the
    /// reduced content height. Needed because LibGUI only auto-corrects the scroll offset via its
    /// wheel-slop clamp (<c>ScrollWheelHandler.ClampOffset</c>), which ignores an overshoot of 50px or
    /// less — so deleting a single ~30px row while scrolled near the bottom strands the viewport past the
    /// new max, leaving dead space below the last row. Honored over a few frames because the editor's
    /// <c>SingleChildScrollView</c> only reports the shrunk <c>ContentSize</c> on the layout pass after
    /// the rebuild (same settling reason as <see cref="pendingRestoreScrollOffset"/>).</summary>
    private bool pendingClampToExtent;
    /// <summary>Frames spent honoring <see cref="pendingClampToExtent"/>; bounds it to the content-size
    /// settling window so it doesn't run every frame forever.</summary>
    private int clampToExtentFrames;

    // ---- Lost-lock autosave recovery (task 8.6) ----
    /// <summary>Max consecutive autosave failures to auto-recover from before giving up, so a lock
    /// permanently held elsewhere can't spin the re-request/resend loop forever.</summary>
    private const int MaxSaveFailureRetries = 3;
    /// <summary>Count of consecutive lost-lock autosave failures; reset to 0 the moment the lock is
    /// re-acquired (a successful recovery re-grant) or a fresh editor session begins.</summary>
    private int saveFailureRetries;
    /// <summary>True between a save-failed ack and its recovery re-grant, so <see cref="EnterEditorMode"/>
    /// keeps the unsaved scratch instead of reseeding it from the authoritative document.</summary>
    private bool recoveringLostLock;

    // ---- Pin Tab state (inert unless in the Pinned view — scribe-pin-editor) ----
    /// <summary>In-progress (uncommitted) text edits for Pin Tab rows, keyed by the pin's stable
    /// <see cref="ScribeBlock.TaskId"/>, written through on every keystroke. This is the Pin Tab's
    /// equivalent of the editor's <see cref="scratch"/> write-through: because a <see cref="ForceRebuild"/>
    /// (fired by <see cref="OnMyPinsChanged"/>) fully unmounts the tree and re-seeds each field, a row
    /// still being typed must re-seed from its live buffer rather than the stale server snapshot. An entry
    /// is dropped when its edit commits (blur/Enter) or the pin leaves the set (<see cref="DisposePinState"/>).</summary>
    private readonly Dictionary<Guid, string> pinEditBuffer = new();

    /// <summary>One focus node per Pin Tab row, keyed by <see cref="ScribeBlock.TaskId"/> (pin order can
    /// change, so keying by identity is stabler than by index — the editor keys its parallel list by index
    /// because block order is its own scratch). Owned by the dialog so a caret survives the
    /// <see cref="OnMyPinsChanged"/> <see cref="ForceRebuild"/> via <see cref="autoFocusPinTaskId"/>. Kept
    /// in sync with the current pin set by <see cref="SyncPinFocusNodes"/>; disposed in <see cref="OnGuiClosed"/>.</summary>
    private readonly Dictionary<Guid, FocusNode> pinFocusNodes = new();

    /// <summary>Host-owned collapse controllers for the Pin Tab's departing rows (extract-animated-task-list),
    /// passed to <see cref="ScribePinnedContent"/>'s <see cref="ScribeAnimatedList"/> so a removed pin's row
    /// collapses out instead of snapping. Owned by the dialog (like <see cref="editorCollapseRegistry"/>) so a
    /// collapse RESUMES across the resync reconcile, and so <see cref="OnRenderGUI"/> can read
    /// <c>AnyAnimating</c> to pin the scroll + refresh hover while a pin row collapses. Disposed with the dialog.</summary>
    private readonly ScribeAnimationRegistry pinCollapseRegistry = new();

    /// <summary>Host-owned collapse controllers for the read view's departing rows (reconcile-animating-surfaces
    /// §5.5), passed to <see cref="ScribeReadContent"/>'s <see cref="ScribeAnimatedList"/> so a Delete-policy
    /// completion collapses the removed row out instead of vanishing it. Owned by the dialog (like
    /// <see cref="editorCollapseRegistry"/> / <see cref="pinCollapseRegistry"/>) so a collapse RESUMES across the
    /// RefreshReadView reconcile, and so <see cref="OnRenderGUI"/> can read <c>AnyAnimating</c> to pin the scroll +
    /// refresh hover while a read row collapses. Disposed with the dialog.</summary>
    private readonly ScribeAnimationRegistry readCollapseRegistry = new();

    /// <summary>True while ANY of the dialog's row-collapse animations (editor, Pin Tab, or read view) is still
    /// running. Only one of these surfaces is mounted at a time, so at most one registry is ever non-idle — but
    /// OR-ing all three is safe and lets the two <see cref="OnRenderGUI"/> collapse loops (the scroll-pin and the
    /// hover-refresh latch) share one gate instead of repeating the three-way check. Folding a future surface's
    /// registry in here updates both loops at once, so neither can be silently left out of step
    /// (reconcile-animating-surfaces §6.3).</summary>
    private bool AnyRowAnimating =>
        editorCollapseRegistry.AnyAnimating || pinCollapseRegistry.AnyAnimating || readCollapseRegistry.AnyAnimating;

    /// <summary>The <see cref="ScribeBlock.TaskId"/> of the Pin Tab row currently focused, tracked from the
    /// rows' focus nodes so a rebuild can restore the caret and a focus move can commit the row being left.
    /// Not cleared on blur (its listener fires only on focus GAINED — the editor's pattern), so it still
    /// names the row to restore across an async pin resync.</summary>
    private Guid? focusedPinTaskId;

    /// <summary>Pin row to auto-focus on the next Pin Tab rebuild (caret restore across a
    /// <see cref="ForceRebuild"/>), or null. One-shot: consumed in <see cref="BuildPinnedContent"/>. Rides
    /// the field's mount-only <c>autoFocus</c>, so it only re-homes a genuinely-NEW (freshly-mounted) row —
    /// reserved for the view-switch/fresh-mount path. An in-place pin resync (reconcile-animating-surfaces
    /// §4.3) REUSES the focused row's field instead, so it re-homes via <see cref="pendingFocusPinTaskId"/>.</summary>
    private Guid? autoFocusPinTaskId;

    /// <summary>Pin row (by TaskId) to re-home keyboard focus onto after an in-place Pin Tab reconcile
    /// (reconcile-animating-surfaces §4.3), or null. The Pin Tab equivalent of the editor's
    /// <see cref="pendingFocusRow"/>: pressing a per-row pin control (checkbox/unpin/delete — none are
    /// <c>IFocusable</c>) blurs the focused field via <c>DispatchPointerDown</c>, and the async
    /// <see cref="OnMyPinsChanged"/> resync lands a frame or more later. Under reconcile the focused row's
    /// field is REUSED (not remounted), so its mount-only <c>autoFocus</c> won't re-fire; instead this drives
    /// a deferred <see cref="FocusNode.RequestFocus"/> on the dialog-owned node from <c>OnRenderGUI</c> once
    /// the reconciled element has a live Owner. One-shot.</summary>
    private Guid? pendingFocusPinTaskId;

    /// <summary>The mod system, cached for per-player pin queries and the pin/complete network sends.</summary>
    protected readonly ScribeModSystem modSystem;

    /// <summary>Samples the light reaching the player each frame and folds it into a quantized (brightness,
    /// tint) shade the whole dialog is rendered at (respect-local-illumination). Constructed once per dialog;
    /// read only on the render thread in <see cref="OnRenderGUI"/>. Bound to the live
    /// <see cref="ScribeModSystem.MySettings"/> so a floor change takes effect on the next sample.</summary>
    private readonly ScribeAmbientLightSampler lightSampler;

    /// <summary>The current quantized illumination shade, refreshed each frame from <see cref="lightSampler"/>
    /// and read by <see cref="BuildBodyTree"/> to configure the <see cref="ScribeGlobalTint"/> wrap. Seeded to
    /// the identity (full brightness, neutral tint) so the very first build — before the first sample — looks
    /// exactly like the pre-illumination dialog. When the quantized value CHANGES, <see cref="OnRenderGUI"/>
    /// calls <see cref="RebuildBody"/> so LibGUI re-records the paint cache with the new shade; on a static
    /// scene it never changes, so the cache stays valid (D3).</summary>
    private ScribeAmbientLightSampler.Shade currentShade = new(1f, 1f, 1f, 1f, changed: false);

    /// <summary>Stable identity of the single persistent-root <see cref="ScribeDialogBody"/> that wraps the
    /// dialog body (reconcile-animating-surfaces §3.1). Allocated ONCE here (never in <see cref="Build"/>,
    /// per the <see cref="GlobalKey"/> contract) so <see cref="RebuildBody"/> can reach the live body State
    /// and reconcile the tree in place instead of tearing it down with <see cref="GuiBase.ForceRebuild"/>.
    /// A <c>GlobalKey</c> re-registers itself on mount, so it stays valid across a <c>ForceRebuild</c> too.</summary>
    private readonly GlobalKey bodyKey = new();

    /// <summary>Reconcile the dialog body in place (reconcile-animating-surfaces §3.1): the modern
    /// replacement for <see cref="GuiBase.ForceRebuild"/> at every in-place update site — a structural row
    /// edit (add/delete/reorder) or a chrome repaint (pin tint, nav active-color, title display⇄input swap).
    /// A no-op <c>SetState</c> on the persistent body State marks it dirty, so the next frame's
    /// <c>BuildOwner.BuildDirtyElements()</c> re-runs <see cref="BuildBodyTree"/> and reconciles the subtree,
    /// REUSING matching elements — the central editor content, its rows, and each row's live
    /// <c>ScribeMultilineField</c> (caret + unsaved buffer intact) — rather than unmounting and rebuilding
    /// them. Reserve <c>ForceRebuild()</c> for the genuinely-new-tree cases (view switches, fresh editor
    /// seed, lost-lock recovery — §3.3). A no-op before the body has mounted (defensive).</summary>
    private protected void RebuildBody() => bodyKey.CurrentState<ScribeDialogBody.BodyState>()?.Rebuild();

    protected ScribeDialogBase(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, capi)
    {
        this.host = host;
        modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();

        // Light sampler for the ambient-illumination shade (respect-local-illumination). Bound to the live
        // MySettings so a floor change is picked up on the next frame's sample; only read on the render thread.
        lightSampler = new ScribeAmbientLightSampler(capi, modSystem.MySettings);

        // Install the real-or-silent UI sound player for this player's current mute preference
        // (scribe-mute-ui-sounds); GuiBase's ctor already set a real SoundPlayer, so this only needs to
        // swap in the silent one when muted. Re-applied in OnMyPinsChanged so a live toggle takes effect.
        ApplyUiSoundPreference();

        // Repaint the per-player pin indicators whenever this player's pushed pin set changes (a pin
        // added/removed/orphaned, or a snapshot refresh). Unsubscribed in OnGuiClosed.
        modSystem.MyPinsChanged += OnMyPinsChanged;

        // Recolor the Settings nav button live when the standalone settings window opens/closes
        // (add-active-tab-nav-colors) — it can be toggled from the HUD gear while the lectern is open,
        // so we can't rely on a lectern interaction to trigger the rebuild. Unsubscribed in OnGuiClosed.
        modSystem.SettingsVisibilityChanged += OnSettingsVisibilityChanged;

        // Restrict Tab / Shift+Tab to this dialog's own editable text fields (exclude-checkboxes-from-tab-focus).
        // gui@3.1.0 made every checkbox focusable AND made GuiBase.OnKeyDown drive Tab through
        // FocusManager.TraversalPolicy, so the stock ReadingOrderTraversalPolicy began stopping Tab on each
        // row's completion checkbox before its text field. There's no public seam to mark the checkbox's
        // node non-traversable, so we install an allow-list policy that returns ONLY our field nodes, in the
        // active view's row order (see EditorFieldTraversalNodes). The Settings window is a separate dialog
        // with its own FocusManager, so it's unaffected.
        FocusManager.TraversalPolicy = new ScribeFieldOnlyTraversalPolicy(EditorFieldTraversalNodes);

        _titleController = new TextEditingController("");
        _titleFocusNode = new FocusNode();
        _titleFocusNode.AddListener(OnTitleFocusChanged);

#if DEBUG
        // Scroll-jump diagnostics: log EVERY offset change on the shared controller — including the ones
        // LibGUI makes internally (ScrollWheelHandler.ClampOffset's ±50px JumpTo on a content-height
        // change), which none of our own OnRenderGUI loops can see. Paired with the [scribe-scroll] intent
        // tags at our mutation sites, this shows "intent → resulting OnChanged" per frame. DEBUG-only:
        // costs nothing in Release, and TraceScroll is [Conditional("DEBUG")] so its call sites vanish too.
        sharedScrollController.OnChanged += OnScrollControllerChanged;
#endif
    }

    /// <summary>
    /// Override to supply additional nav buttons inserted between the Pins tab and the Settings gear.
    /// Settings is always last. The base implementation returns an empty sequence (no extra buttons).
    /// </summary>
    protected virtual IEnumerable<Widget> GetExtraNavButtons() => Array.Empty<Widget>();

#if DEBUG
    /// <summary>DEBUG-only: fires on every <see cref="sharedScrollController"/> offset change (our JumpTo,
    /// LibGUI's internal clamp, wheel, EnsureVisible). Routes to <see cref="TraceScroll"/> tagged "changed".</summary>
    private void OnScrollControllerChanged() => TraceScroll("changed");
#endif

    /// <summary>DEBUG-only scroll trace. Compiled out entirely in Release (<see cref="ConditionalAttribute"/>),
    /// so call sites need no <c>#if</c> guard. Emits the full scroll state — live offset + max extent, the
    /// active view, the focused editor row, and every pending scroll-intent flag with its frame counter — so
    /// a jumping vs. clean action can be diffed line-for-line in <c>client-main.log</c> (watch with
    /// <c>build/scribe-log.sh --client</c>). <paramref name="tag"/> names the call site (e.g. "sink",
    /// "insert-below", "ensure-visible", "restore", "clamp", "changed").</summary>
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

    /// <summary>Swap the LibGUI UI sound player to match this player's <c>MuteUiSounds</c> preference
    /// (scribe-mute-ui-sounds): the shared no-op <see cref="ScribeModSystem.SilentSoundPlayer"/> when
    /// muted, else the stock <c>SoundPlayer</c> LibGUI's <c>GuiBase</c> installs. Called from the ctor
    /// and on every settings change (via <see cref="OnMyPinsChanged"/>), so flipping the toggle while the
    /// dialog is open re-installs the correct player without a reopen.</summary>
    private void ApplyUiSoundPreference()
        => BuildOwner.SetSoundPlayer(modSystem.GetUiSoundPlayer(capi));

    /// <summary>Whether THIS player has pinned the given task in this lectern's document. Drives the
    /// resting pin tint and the pin-glyph accent in both views, sourced from the server-pushed cache
    /// rather than any document field (pinning is per-player, not document state).</summary>
    private bool IsPinnedForMe(Guid taskId) => modSystem.IsPinnedForMe(host.Document.DocId, taskId);

    /// <summary>Whether the tier cap (<see cref="IScribeDocumentHost.Policy"/>) still permits adding one
    /// more TASK block to the document being edited. Counted against the live scratch document while
    /// editing (falls back to the host document if scratch is not yet initialized). Uncapped tiers
    /// (Lectern, Notebook — <see cref="ScribeDocumentPolicy.Unlimited"/>) always return true, so this
    /// gates only the tablet tier (scribe-document-policy).</summary>
    private bool CanAddTaskUnderPolicy()
    {
        var doc = scratch ?? host.Document;
        int taskCount = doc.Blocks.Count(b => b.IsTask);
        return host.Policy.CanAdd(taskCount);
    }

    /// <summary>Surface the "tablet is full" in-game error when an add is refused by a FINITE task cap
    /// (zero-point-three-fixes §7.2). Only fires for a capped tier (the tablet's
    /// <see cref="ScribeDocumentPolicy.MaxBlocks"/> is set) — uncapped tiers (Lectern, Notebook) never
    /// refuse, so this is inert there. Called from the add-task gestures right where they used to return
    /// silently, so the dimmed "Add task" button and the Enter-insert gesture now both explain themselves
    /// via the same transient-error path the lock notice uses. Keeps Core pure: the refusal is decided by
    /// the boolean <see cref="ScribeDocumentPolicy.CanAdd"/>; only the feedback lives here in the Mod.</summary>
    private void NotifyTabletFull()
    {
        if (host.Policy.MaxBlocks is int)
        {
            capi.TriggerIngameError(this, "scribe-tablet-full", Lang.Get("scribe:tablet-full"));
        }
    }

    /// <summary>Toggle THIS player's pin on the given task, honoring the tier's pin cap
    /// (<see cref="IScribeDocumentHost.Policy"/>) with SWAP semantics rather than refusal. Unpinning is
    /// always allowed. When pinning would exceed a finite <see cref="ScribeDocumentPolicy.MaxPins"/>
    /// cap (the tablet tier: 1 pin per document), the player's oldest pins for THIS document are
    /// released first so the new pin fits — a seamless "pin this one instead" swap. Uncapped tiers
    /// (Lectern, Notebook — <see cref="ScribeDocumentPolicy.Unlimited"/>) never release anything and
    /// simply pin. Pin actions are per-player and lock-free (see <see cref="SendSetPin"/>); the server
    /// re-pushes this player's set, landing in <see cref="OnMyPinsChanged"/> to repaint the rows.</summary>
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

    /// <summary>Release the player's oldest pins for the current document until pinning one more task
    /// stays within the tier's <see cref="ScribeDocumentPolicy.MaxPins"/> cap. No-op for uncapped tiers
    /// (no cap → nothing to make room for). <see cref="ScribeModSystem.MyPins"/> is in pin order, so the
    /// earliest entries are released first.</summary>
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
            SendSetPin(existing[i].TaskId, false);
    }

    /// <summary>A fresh pin-set/settings push arrived: rebuild so the pin indicators reflect it. A
    /// no-op while not open. In editor mode the pin state is per-player (not part of the scratch doc),
    /// so repainting it does not disturb the in-progress text edit.
    ///
    /// <para>Focus preservation: the per-row pin control isn't <c>IFocusable</c>, so pressing it made
    /// LibGUI's <c>EventDispatcher.DispatchPointerDown</c> clear focus (it blurs on any press whose hit
    /// path holds no focusable element) — and this rebuild lands asynchronously, after the server
    /// re-pushes the set, so nothing re-homed the caret and it vanished. Re-arm the one-shot
    /// <see cref="autoFocusRowOnRebuild"/> for the still-focused row (blur does not clear
    /// <see cref="focusedEditIndex"/> — its listener only fires on focus GAINED) so the caret returns to
    /// the row being edited. Covers this player's own pin toggle AND any external pin change mid-edit.</para></summary>
    private void OnMyPinsChanged()
    {
        if (!IsOpened()) return;
        TraceScroll("pins-changed");
        // A settings change may have flipped the mute preference — re-install the matching sound player
        // so a live toggle takes effect on this already-open dialog (scribe-mute-ui-sounds).
        ApplyUiSoundPreference();
        // Editor view (reconcile-animating-surfaces §3.1): the pin set is per-player, NOT part of scratch,
        // so a pin change is a pure chrome repaint — the row set is structurally identical, so an in-place
        // RebuildBody reconcile REUSES every row (each ScribeMultilineField keeps its caret + buffer) and
        // just re-tints the pin indicators. The pin control isn't IFocusable, so pressing it blurred the
        // focused field via DispatchPointerDown; re-home focus onto that still-focused row via the deferred
        // RequestFocus (pendingFocusRow) — the reused field skips its mount-only autoFocus, so
        // autoFocusRowOnRebuild wouldn't fire. Reconcile preserves the shared scroll offset in place, so no
        // capture-restore is needed here (that was only to survive the old ForceRebuild's offset reset).
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx && idx < editorFocusNodes.Count) pendingFocusRow = idx;
            RebuildBody();
            return;
        }

        // Pin Tab (reconcile-animating-surfaces §4.3): like the editor, the pin resync is an in-place
        // reconcile — ScribePinnedContent keys every row by ValueKey<Guid>(TaskId), owns its drag state
        // internally, and re-seeds each field from pinEditBuffer, so RebuildBody REUSES every row (each
        // ScribeMultilineField keeps its caret + unsaved buffer) and just re-tints/re-orders. Re-home the
        // caret on the row being edited via the deferred pendingFocusPinTaskId — pressing a per-row pin
        // control (none IFocusable) blurred the focused field via DispatchPointerDown, and a reused field
        // skips its mount-only autoFocus, so autoFocusPinTaskId wouldn't re-fire here. A blur doesn't clear
        // focusedPinTaskId, so it still names the row; re-arm only if that pin still exists in the new set.
        // Reconcile preserves the shared scroll offset in place, so no capture-restore (that was only for
        // the old ForceRebuild's offset reset).
        if (viewMode == ScribeLecternView.Pinned)
        {
            if (focusedPinTaskId is { } pinId && modSystem.MyPins.Any(p => p.TaskId == pinId))
                pendingFocusPinTaskId = pinId;
            // A completion can re-order the list (sink policy), sliding a different row under a stationary
            // cursor. Unlike a ForceRebuild (fresh hovered=false tree caught by ArmIfRebuilt), a reconcile
            // leaves RootElement unchanged and REUSES rows with their stale hovered flag — so arm the hover
            // latch to re-dispatch a synthetic pointer-move and re-home the hover-gated delete/unpin controls
            // without a mouse wiggle (fix-list-collapse-stale-hover). Harmless no-op when no row moved.
            hoverRefreshLatch.Arm();
            RebuildBody();
            return;
        }

        // Read view (reconcile-animating-surfaces §5): the pin set is per-player, NOT part of the document,
        // so a pin change is a pure chrome repaint — the read row set is structurally identical, so an
        // in-place RebuildBody reconcile REUSES every row (preserving the shared scroll offset inherently)
        // and just re-tints the pin indicators. This replaces the old ForceRebuild + capture-restore dance:
        // the read list is now non-virtualized + TaskId-keyed (see ScribeReadContent), so it reconciles like
        // the editor and Pin Tab. A completion elsewhere can slide a row under a stationary cursor; arm the
        // hover latch so the hover-gated pin control re-homes without a mouse wiggle.
        if (viewMode == ScribeLecternView.Read)
        {
            hoverRefreshLatch.Arm();
            RebuildBody();
            return;
        }
        // Any remaining non-editor view (History/Timer/Visitors) still ForceRebuilds; capture the offset so
        // the OnRenderGUI restore loop re-applies it once content height settles.
        CaptureScrollForRestore();
        ForceRebuild();
    }

    /// <summary>Rebuild when the standalone settings window opens/closes so the Settings nav button picks
    /// up or drops its active color (add-active-tab-nav-colors). Scroll position must be captured before
    /// the rebuild — ForceRebuild re-derives content height and clamps the offset toward 0.</summary>
    private void OnSettingsVisibilityChanged()
    {
        if (!IsOpened()) return;
        TraceScroll("settings-vis");
        // Editor view (§3.1): the nav column (with the Settings button) lives inside the persistent body, so
        // an in-place reconcile re-tints the button while REUSING the editor rows — no caret/scroll loss, no
        // capture-restore needed (reconcile keeps the shared offset). Re-home the focused row's caret in case
        // an earlier press cleared focus. Non-editor views keep ForceRebuild until §4/§5.
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx && idx < editorFocusNodes.Count) pendingFocusRow = idx;
            RebuildBody();
            return;
        }
        // Pin Tab (§4.3): the nav column lives inside the persistent body, so an in-place reconcile re-tints
        // the Settings button while REUSING the pin rows — no caret/scroll loss. Re-home the focused row via
        // the deferred pendingFocusPinTaskId in case an earlier press cleared focus. Read stays on ForceRebuild.
        if (viewMode == ScribeLecternView.Pinned)
        {
            if (focusedPinTaskId is { } pinId && modSystem.MyPins.Any(p => p.TaskId == pinId))
                pendingFocusPinTaskId = pinId;
            RebuildBody();
            return;
        }
        // Read view (§5): the nav column (with the Settings button) lives inside the persistent body, so an
        // in-place reconcile re-tints the button while REUSING the read rows — no scroll loss, no
        // capture-restore. Now reachable because the read list is non-virtualized + TaskId-keyed.
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
        // The whole dialog is the notebook art's OuterArtBox: size it to W × H (art aspect) so the
        // stretch-to-fill backdrop renders un-distorted (scribe-notebook-frame). W is the player's Pixel
        // Art Size preference, read fresh at open (TryOpen calls this per open, after the ctor set
        // modSystem). Non-resizable so the aspect (hence the art) can never be stretched off-square; the
        // title-bar band is the drag zone, so DragHandleHeight matches its height instead of the stock 24.
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
    /// The native dialog overrode <c>IsInRangeOfBlock</c> to fix a Creative-mode walk-away bug: the
    /// engine inflates <c>PickingRange</c> to ~100 blocks in Creative, so the base's pick-range
    /// auto-close never fired. LibGUI's <see cref="GuiDialogBlockEntityBase"/> uses a different
    /// override point -- <c>OnFinalizeFrame</c> calls <c>IsOutOfRange(playerPos, pos,
    /// InteractionRange)</c>, and <c>InteractionRange</c> defaults to the same mode-inflated
    /// <c>PickingRange</c>. Pin it to the fixed survival interaction distance so walk-away
    /// auto-close fires in every game mode, including while a row is being edited.
    /// </summary>
    protected override double InteractionRange => GlobalConstants.DefaultPickingRange + 0.5;

    // ---------------- Input capture + macOS caret translation ----------------

    /// <summary>
    /// Capture ALL keyboard/mouse input ONLY while an editor or Pin-Tab field actually holds focus, so
    /// typed keys don't leak to the game (movement, hotbar, keybinds). Gated on a field being focused
    /// rather than on <c>isEditorMode</c> alone (v1-playtest-fixes): when the editor view is open but
    /// no field holds focus (e.g. after "New Task" → click away), global hotkeys must still fire.
    ///
    /// <para>v1-playtest-fixes (second pass): <see cref="OnRowFocusChanged"/> only fires on focus-gained,
    /// so <see cref="focusedEditIndex"/> stays non-null after a click-away, keeping capture live after
    /// unfocus. Guard with a live <see cref="FocusNode.HasFocus"/> check so capture drops the moment no
    /// field holds the active focus token.</para>
    ///
    /// <para><b>Gate on <see cref="Vintagestory.API.Client.GuiDialog.Focused"/>
    /// (fix-settings-numeric-arrow-focus-leak).</b> <c>GuiManager.OnKeyDown</c> runs a FIRST pass over
    /// every open dialog whose <see cref="CaptureAllInputs"/> is true — BEFORE the normal focused-dialog
    /// pass — and stops on the first that marks the key Handled. Each LibGUI <see cref="GuiBase"/> owns its
    /// OWN <c>FocusManager</c>, so when the standalone Scribe Settings window is on top and a document
    /// editor is also open, the editor's row still reports <c>HasFocus == true</c> in the editor's private
    /// manager even though the editor is no longer the focused VS dialog (clicking the settings field ran
    /// <c>capi.Gui.RequestFocus(settingsDialog)</c>, which <c>UnFocus()</c>es the editor but never touches
    /// the editor's LibGUI focus). Ungated, the editor therefore CAPTURED the settings window's Up/Down
    /// arrow presses in that first pass and drove the row's caret instead of stepping the numeric field —
    /// the arrow-key focus-leak. Requiring <c>Focused</c> means an editor that is not the active dialog no
    /// longer pre-empts keyboard input, so the key reaches the focused settings dialog. This cannot regress
    /// the capture's real job (blocking movement/hotbar keys from the game WHILE typing into the editor),
    /// which only applies when the editor IS the focused dialog.</para>
    /// </summary>
    public override bool CaptureAllInputs()
        => Focused
        && ((isEditorMode && focusedEditIndex is { } idx && idx < editorFocusNodes.Count && editorFocusNodes[idx].HasFocus)
        || (viewMode == ScribeLecternView.Pinned && focusedPinTaskId is { } pinId
            && pinFocusNodes.TryGetValue(pinId, out var pn) && pn.HasFocus)
        || _guestbookNoteFocusNodes.Values.Any(n => n.HasFocus));

    /// <summary>
    /// LibGUI's <see cref="Gui.Widgets.Events.KeyboardEvent"/> carries only Shift/Ctrl/Alt — it drops
    /// VS's Command modifier — so the macOS caret idioms can't be handled inside the editor field.
    /// Translate them here, before <c>base.OnKeyDown</c> maps the (mutable) VS <see cref="KeyEvent"/>
    /// into the Cmd-less LibGUI event: Cmd+Left/Right → Home/End (line start/end), Cmd+A/C/X/V →
    /// Ctrl+A/C/X/V (select-all / clipboard). Alt/Option is already delivered as Alt, so Alt+Arrow
    /// word-skip works in the field without translation. Mirrors the native
    /// <c>ScribeRowTextInput.TranslateMacCaretModifiers</c>, one layer up.
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
