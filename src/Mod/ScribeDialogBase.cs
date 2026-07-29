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

public abstract class ScribeDialogBase : GuiDialogBlockEntityBase
{
    protected readonly IScribeDocumentHost host;

    /// <summary>One scroll controller shared by BOTH views' scroll regions, owned by the dialog rather
    /// than each view's <c>State</c>. Because a view switch is a <see cref="GuiBase.ForceRebuild"/> that
    /// tears down the outgoing view's <c>State</c> (which would dispose a State-owned controller and
    /// lose the offset), sharing one dialog-lived controller keeps the scroll position across the
    /// switch — and since row heights are unified across views (<see cref="ScribeRowStyle"/>), the same
    /// offset shows the same rows. Passed into the <c>ListView</c>/<c>SingleChildScrollView</c>, which
    /// then do NOT dispose it (they only dispose their own internal fallback); the dialog disposes it in
    /// <see cref="OnGuiClosed"/>. An offset past a shorter view's max is clamped on layout.</summary>
    private readonly ScrollController sharedScrollController = new();

    // ---- View state ----
    /// <summary>The Lectern dialog's central-region view. Read and Editor are the original two views;
    /// Pinned is the Pin Tab (scribe-pin-editor) — a peer view listing the player's pins, selected from the
    /// <c>scribepin</c> nav button. <see cref="BuildCentralRegion"/> chooses the body from this.</summary>
    private enum ScribeLecternView { Read, Editor, Pinned, Visitors }

    private ScribeLecternView viewMode = ScribeLecternView.Read;

    /// <summary>True when the Guestbook (Visitors) tab is the active view. Exposed so subclasses
    /// can apply the active color to their Guestbook nav button in <see cref="GetExtraNavButtons"/>.</summary>
    protected bool IsVisitorsView => viewMode == ScribeLecternView.Visitors;

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
    /// <summary>Row index to auto-focus on the next editor rebuild (a newly added task), or null.</summary>
    private int? autoFocusRowOnRebuild;
    /// <summary>Set when a focus move or a row growth needs the focused row scrolled into view; acted
    /// on in <see cref="OnRenderGUI"/> AFTER layout has run (EnsureVisible reads live geometry).</summary>
    private bool pendingEnsureVisible;

    // ---- Document title editing ----
    /// <summary>True while the title row is in inline-input mode (pencil was clicked, input is active).</summary>
    private bool _isTitleEditing;
    private TextEditingController? _titleController;
    private FocusNode? _titleFocusNode;

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
    /// new focus. Deferring is the same re-entrancy guard <see cref="needsEditorCollapseCleanup"/> uses.
    /// Null when no empty row is awaiting removal.</summary>
    private int? pendingEmptyRowRemoval;

    /// <summary>Editor rows that have been deleted from the scratch document but are still collapsing their
    /// height to zero before leaving the list (scribe-list-collapse), keyed by the block's stable
    /// <see cref="ScribeBlock.TaskId"/> (unique per block — tasks AND text sections), valued by the deleted
    /// row's last-known snapshot and the display index it held (so it collapses IN PLACE). The row renders as
    /// a static, non-interactive snapshot (its scratch block and focus node are already gone). The entry is
    /// removed when its collapse completes (<see cref="OnEditorRowCollapsed"/>).</summary>
    private readonly Dictionary<Guid, (ScribeEditRowData Row, int Index)> departingEditorRows = new();

    /// <summary>Host-owned collapse controllers for <see cref="departingEditorRows"/>, keyed by TaskId so a
    /// collapse RESUMES (not restarts) across the dialog's <see cref="ForceRebuild"/> remounts
    /// (scribe-list-collapse). Disposed with the dialog.</summary>
    private readonly ScribeCollapseRegistry editorCollapseRegistry = new();

    /// <summary>Set when an editor row's collapse completes, so its removal + rebuild is deferred to the next
    /// <see cref="OnRenderGUI"/> — the completion callback fires from inside the animation pump, where
    /// unmounting + rebuilding the tree would be re-entrant.</summary>
    private bool needsEditorCollapseCleanup;

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

    /// <summary>The <see cref="ScribeBlock.TaskId"/> of the Pin Tab row currently focused, tracked from the
    /// rows' focus nodes so a rebuild can restore the caret and a focus move can commit the row being left.
    /// Not cleared on blur (its listener fires only on focus GAINED — the editor's pattern), so it still
    /// names the row to restore across an async pin resync.</summary>
    private Guid? focusedPinTaskId;

    /// <summary>Pin row to auto-focus on the next Pin Tab rebuild (caret restore across a
    /// <see cref="ForceRebuild"/>), or null. One-shot: consumed in <see cref="BuildPinnedContent"/>.</summary>
    private Guid? autoFocusPinTaskId;

    /// <summary>The mod system, cached for per-player pin queries and the pin/complete network sends.</summary>
    protected readonly ScribeModSystem modSystem;

    protected ScribeDialogBase(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, capi)
    {
        this.host = host;
        modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();

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
        if (isEditorMode && focusedEditIndex is { } idx && idx < editorFocusNodes.Count)
        {
            autoFocusRowOnRebuild = idx;
        }
        // Pin Tab: keep the caret on the row being edited across this async resync rebuild (same hazard the
        // editor faces — see OnMyPinsChanged remarks). A blur doesn't clear focusedPinTaskId, so it still
        // names the row; re-arm the one-shot only if that pin still exists in the (new) set.
        if (viewMode == ScribeLecternView.Pinned && focusedPinTaskId is { } pinId
            && modSystem.MyPins.Any(p => p.TaskId == pinId))
        {
            autoFocusPinTaskId = pinId;
        }
        // Read and Editor views use the virtualized ListView / SingleChildScrollView; a ForceRebuild
        // re-derives content height and clamps the shared controller's offset toward 0, losing the player's
        // scroll position. Capture the offset HERE — right before the rebuild — so the OnRenderGUI restore
        // loop re-applies it once the content height settles. Capturing in OnReadViewTogglePinned (the
        // pre-network-round-trip site) was too early: by the time this async callback fires the restore loop
        // had already terminated (pendingRestoreScrollOffset was null). Pinned view rebuilds use a
        // non-virtualized Column whose content height is exact from frame-1, so no restore needed there.
        if (viewMode != ScribeLecternView.Pinned) CaptureScrollForRestore();
        ForceRebuild();
    }

    /// <summary>Rebuild when the standalone settings window opens/closes so the Settings nav button picks
    /// up or drops its active color (add-active-tab-nav-colors). No scroll/focus preservation needed — the
    /// lectern's own view state is unchanged; only the gear's fill differs.</summary>
    private void OnSettingsVisibilityChanged()
    {
        if (IsOpened()) ForceRebuild();
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
    /// </summary>
    public override bool CaptureAllInputs()
        => (isEditorMode && focusedEditIndex is { } idx && idx < editorFocusNodes.Count && editorFocusNodes[idx].HasFocus)
        || (viewMode == ScribeLecternView.Pinned && focusedPinTaskId is { } pinId
            && pinFocusNodes.TryGetValue(pinId, out var pn) && pn.HasFocus);

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

    // ---------------- View switching ----------------

    /// <summary>Enter (or refresh) the editor view on the given authoritative document bytes. Called
    /// by <see cref="BlockEntityScribeLectern.HandleServerReply"/> when editor access is granted
    /// (the lock is now held). Seeds a fresh scratch copy, starts the autosave tick, and rebuilds
    /// into the editor tree (or lets <c>TryOpen</c> build it if the dialog isn't open yet).</summary>
    public void EnterEditorMode(byte[]? documentBytes)
    {
        if (recoveringLostLock && scratch is not null)
        {
            // This grant is the recovery re-acquire after a lost-lock save failure (task 8.6): the
            // lock is ours again, so keep the player's unsaved scratch and re-flush it rather than
            // reseeding from the authoritative document (which would silently discard their edits).
            recoveringLostLock = false;
            saveFailureRetries = 0;
            isEditorMode = true;
            StartAutosaveTick();
            isDirty = true;   // force the pending edit to be re-sent on the next flush
            FlushIfDirty();
            if (IsOpened()) ForceRebuild();
            return;
        }

        scratch = ScribeDocumentCodec.TryDeserialize(documentBytes, out var doc) && doc is not null
            ? doc
            : new ScribeDocument();
        isDirty = false;
        isEditorMode = true;
        focusedEditIndex = null;
        autoFocusRowOnRebuild = null;
        saveFailureRetries = 0;
        recoveringLostLock = false;
        SyncFocusNodesToScratch();
        StartAutosaveTick();

        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>Enter (or stay in) the read view. Called on a read-access grant and from the Read nav
    /// button. Tears down the editor if it was active; also lands on Read from the Pin Tab (which holds no
    /// lock/scratch, so nothing to tear down — just select the view).</summary>
    public void EnterReadMode()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.Read; // also covers a Pin Tab → Read switch (no editor teardown)
        if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>Switch to the Pin Tab view (scribe-pin-editor). Wired to the <c>scribepin</c> nav button —
    /// a real entry method, not an inline flag flip (the nav discipline the editor's
    /// <see cref="RequestEditorAccess"/> / <see cref="EnterReadMode"/> follow). If the editor view is
    /// active, tear it down first (flush + release the lock) exactly like <see cref="OnClickSwitchToRead"/>,
    /// then select the Pinned view. The Pin Tab reads the server-synced <c>MyPins</c> — no lock, no scratch
    /// doc — so this needs no server round-trip.</summary>
    private void OnClickSwitchToPinned()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
            PurgeEmptyTasksFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.Pinned;
        focusedPinTaskId = null;
        autoFocusPinTaskId = null;
        SyncPinFocusNodes();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Switches to the Guestbook (Visitors) view, tearing down the editor first if active.
    /// Called from the Guestbook nav button in subclasses that expose the tab.</summary>
    protected void OnClickSwitchToVisitors()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
            PurgeEmptyTasksFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            LeaveEditorMode();
        }
        viewMode = ScribeLecternView.Visitors;
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>If the Guestbook tab is currently active, rebuilds it to reflect a just-received
    /// guestbook sync from the server.</summary>
    protected internal void RefreshGuestbookView()
    {
        if (viewMode == ScribeLecternView.Visitors && IsOpened()) ForceRebuild();
    }

    /// <summary>"Done editing" button: flush the pending edit, release the lock, and swap to the read
    /// view — all locally (read is lock-free and reads the block entity's now-optimistically-updated
    /// document). Flush BEFORE releasing the lock: the server processes packets in send order, so
    /// releasing first would let the flushed edit arrive lock-less and be rejected by
    /// <see cref="BlockEntityScribeLectern.ApplyEdit"/>'s lock check.</summary>
    private void OnClickSwitchToRead()
    {
        if (focusedEditIndex is { } idx) NormalizeRowOnCommit(idx);
        // Drop any abandoned empty task before flushing so the read view / persisted doc never shows one
        // (add-empty-task-lifecycle D5) — the focused row may be an untyped new task the blur hasn't swept.
        PurgeEmptyTasksFromScratch();
        pendingEmptyRowRemoval = null; // superseded by the purge; don't act on it after the editor tears down
        FlushIfDirty();
        SendReleaseLockPacket();
        CaptureScrollForRestore();
        LeaveEditorMode();
        ForceRebuild();
    }

    /// <summary>Snapshot the current scroll offset so it can be re-applied after the next view's first
    /// layout (see <see cref="pendingRestoreScrollOffset"/>). Called before a switch-to-read rebuild so
    /// the read view's ListView content-height re-derivation can't strand the offset at the top.</summary>
    private void CaptureScrollForRestore()
    {
        pendingRestoreScrollOffset = sharedScrollController.Offset;
        scrollRestoreFrames = 0;
        TraceScroll("capture-restore");
    }

    /// <summary>Tear down the editor state (stop autosave, drop scratch + focus nodes) and return to
    /// read mode. Does NOT flush or release the lock — callers do that first when appropriate.</summary>
    private void LeaveEditorMode()
    {
        StopAutosaveTick();
        isEditorMode = false;
        scratch = null;
        isDirty = false;
        focusedEditIndex = null;
        autoFocusRowOnRebuild = null;
        saveFailureRetries = 0;
        recoveringLostLock = false;
        DisposeFocusNodes();
    }

    /// <summary>
    /// Called by <see cref="BlockEntityScribeLectern.HandleServerReply"/> when an autosave was rejected
    /// because this client lost the editor lock (task 8.6). The failing <see cref="FlushIfDirty"/>
    /// already cleared <see cref="isDirty"/> optimistically, so without recovery the unsaved edit
    /// would be silently dropped. Instead we re-request the editor lock (keeping the scratch intact via
    /// <see cref="recoveringLostLock"/>); a re-grant lands back in <see cref="EnterEditorMode"/>, which
    /// re-flushes the pending edit. Bounded by <see cref="MaxSaveFailureRetries"/> so a lock genuinely
    /// held by another player can't spin this forever — after the cap we stop, leaving the edits visible
    /// in the (now read-only-until-relock) scratch and the one-time error toast already shown.
    /// Returns true if a recovery re-request was sent, false if the cap was hit.
    /// </summary>
    public bool HandleSaveFailed()
    {
        if (!isEditorMode) return false;

        if (saveFailureRetries >= MaxSaveFailureRetries)
        {
            recoveringLostLock = false;
            return false;
        }

        saveFailureRetries++;
        recoveringLostLock = true;
        RequestEditorAccess();
        return true;
    }

    /// <summary>The user-facing "switch to editor" entry point for both affordances (read-view footer
    /// button and the right-column Edit nav button) — fix-multiplayer-editor-lock §4. If the synced lock
    /// state shows another player already holds the editor lock, this does NOT round-trip to the server:
    /// it surfaces the native in-game error and stays put (the affordance is also rendered inert in that
    /// state — see BuildRightColNav / the read-view footer). Otherwise it requests access normally; the
    /// server refusal remains the authoritative backstop for the render→click race. Distinct from
    /// <see cref="RequestEditorAccess"/>, which is the raw request also used by the lost-lock recovery
    /// re-acquire (<see cref="HandleSaveFailed"/>) and must NOT be gated.</summary>
    private void TryEnterEditor()
    {
        if (host.IsLockedByOther(capi.World.Player.PlayerUID))
        {
            capi.TriggerIngameError(this, "scribe-lectern-locked", Lang.Get("scribe:scribe-gui-locked"));
            return;
        }

        RequestEditorAccess();
    }

    /// <summary>"Switch to editor" button in the read view: request editor access from the server
    /// (design D2 flow, unchanged). A granted reply round-trips back to
    /// <see cref="BlockEntityScribeLectern.HandleServerReply"/>, which calls
    /// <see cref="EnterEditorMode"/>. Callers that gate on the synced lock go through
    /// <see cref="TryEnterEditor"/>; this raw form is also the lost-lock recovery re-acquire.</summary>
    private void RequestEditorAccess()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeRequestAccessMessage
        {
            PosX = host.Pos.X,
            PosY = host.Pos.Y,
            PosZ = host.Pos.Z,
            WantEditor = true,
        });
    }

    /// <summary>
    /// Called by <see cref="BlockEntityScribeLectern.FromTreeAttributes"/> whenever the authoritative
    /// document changes (e.g. another viewer toggled a task, or a HUD-Delete removed one). In read mode,
    /// rebuilds from the current document. In editor mode, the scratch is the source of truth for content
    /// being edited — a full resync must NOT overwrite in-progress text. However, if the fresh authoritative
    /// document is missing a task that still lives in the scratch (e.g. this player completed it via the
    /// HUD under Delete policy), that task no longer exists server-side and its row should disappear from
    /// the editor without disturbing other rows' edits (add-pinned-task-hud follow-up — <c>80777b7b</c>).
    /// </summary>
    public void RefreshReadView()
    {
        if (!isEditorMode)
        {
            if (IsOpened()) ForceRebuild();
            return;
        }

        // Editor mode: don't resync content, but silently drop any tasks the server no longer knows about.
        if (scratch is null || !IsOpened()) return;
        var serverTaskIds = host.Document.Blocks
            .Where(b => b.IsTask)
            .Select(b => b.TaskId)
            .ToHashSet();
        bool any = false;
        for (int i = scratch.Blocks.Count - 1; i >= 0; i--)
        {
            var b = scratch.Blocks[i];
            if (!b.IsTask || serverTaskIds.Contains(b.TaskId)) continue;
            // A task in scratch but absent from the server is EITHER a real task the server dropped
            // (completed via the HUD / Delete policy elsewhere) — which should disappear here too — OR a
            // task this editor JUST created that hasn't reached the server yet: EditorInsertTaskBelow
            // flushes the pre-insert doc, and autosave is throttled (~1s) and skips an empty focused row,
            // so a brand-new row lives locally-only for a beat. A resync landing in that window must NOT
            // yank the new row out from under the player — that was the "Enter makes a task that self-
            // destructs a few frames later" race (trace signature: insert-below N → delete N+1 with no
            // sweep guard tripping, because the delete comes from HERE, not the empty-row sweep). Tell the
            // two apart: never drop the row currently being edited, and never drop an empty task (empty
            // tasks are never persisted by design — see PurgeEmptyTasksFromScratch — so their absence from
            // the server is always expected, never a server-side deletion).
            if (focusedEditIndex == i || string.IsNullOrWhiteSpace(b.Text)) continue;
            DeleteEditorBlock(i);
            any = true;
        }
        // DeleteEditorBlock schedules its own ForceRebuild; only rebuild if nothing was deleted
        // (otherwise we're already rebuilding via the collapse/cleanup path).
        if (!any) return;
    }

    // ---------------- Editor operations (called from the editor rows) ----------------

    /// <summary>Live text-change from a focused editor field: write straight through to the scratch
    /// document's block and mark dirty (the autosave tick / commit path serializes it). The field
    /// auto-grows itself on wrap/newline, and the SingleChildScrollView+Column re-lays-out, so no
    /// explicit height tracking is needed here — just keep the growing focused row in view.</summary>
    private void NotifyTextChanged(int index, string text)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        scratch.SetBlockText(index, text);
        isDirty = true;
        pendingEnsureVisible = true;
    }

    /// <summary>Enter: commit the focused row and advance focus to the next.</summary>
    private void EditorAdvanceFrom(int index)
    {
        if (scratch is null) return;
        NormalizeRowOnCommit(index);
        FlushIfDirty();
        int next = Math.Min(index + 1, scratch.Blocks.Count - 1);
        FocusEditorRow(next);
    }

    /// <summary>Shift+Tab: commit the focused row and retreat focus to the previous.</summary>
    private void EditorRetreatFrom(int index)
    {
        if (scratch is null) return;
        NormalizeRowOnCommit(index);
        FlushIfDirty();
        int prev = Math.Max(index - 1, 0);
        FocusEditorRow(prev);
    }

    /// <summary>Enter: commit the focused row, then insert a new EMPTY task directly beneath it and focus
    /// it (so the player types straight into the new row). Rebuilds because the row set changed.
    ///
    /// <para>Q5 (add-empty-task-lifecycle): Enter on a row that is itself empty/whitespace-only is a
    /// no-op on the row set — it does NOT stack a second empty task. Without this, empty-init would let
    /// Enter-Enter-Enter spam a column of empty rows; instead the caret just stays where it is.</para></summary>
    private void EditorInsertTaskBelow(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        TraceScroll($"insert-below {index}");

        // Q5: don't stack another empty task beneath an already-empty task row.
        var current = scratch.Blocks[index];
        if (current.IsTask && string.IsNullOrWhiteSpace(current.Text)) return;

        NormalizeRowOnCommit(index);
        FlushIfDirty();

        int insertAt = index + 1;
        if (scratch.InsertTask(insertAt, ""))
        {
            isDirty = true;
            SyncFocusNodesToScratch();
            autoFocusRowOnRebuild = insertAt;
            focusedEditIndex = insertAt;
            pendingEnsureVisible = true;
            ForceRebuild();
        }
        else
        {
            FocusEditorRow(Math.Min(index + 1, scratch.Blocks.Count - 1));
        }
    }

    /// <summary>A row's editable field lost focus (add-empty-task-lifecycle D3). If the block at
    /// <paramref name="index"/> is a TASK whose text is empty/whitespace-only, schedule its removal —
    /// this is the self-destruct that turns "clear the text and click away" (or Tab off an untyped new
    /// row) into a keyboard-only delete. A text section may be empty and is never auto-removed.
    ///
    /// <para>The actual delete is DEFERRED to <see cref="OnRenderGUI"/> (<see cref="pendingEmptyRowRemoval"/>)
    /// rather than run here: blur fires from inside the field's focus-notification, and on a row→row move
    /// the old node blurs mid-way through <c>FocusManager.RequestFocus</c> — deleting now (which disposes +
    /// recreates focus nodes and rebuilds the tree) would strand the in-flight focus of the row being
    /// entered. Reading the block from live scratch by index, and re-checking emptiness at removal time,
    /// keeps this idempotent with the <see cref="OnRowFocusChanged"/> commit that also fires on a
    /// row→row move.</para></summary>
    private void OnRowBlurred(int index)
    {
        if (!isEditorMode || scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        if (!block.IsTask || !string.IsNullOrWhiteSpace(block.Text)) return;
        pendingEmptyRowRemoval = index;
    }

    /// <summary>Editor checkbox toggle — completes a task under the player's completion policy so the
    /// editor behaves IDENTICALLY to the read/pinned views and the HUD (scribe-lectern-view-consistency
    /// §4). The row flips its own checkbox optimistically before this runs.
    ///
    /// <para><b>Why the policy is enacted in <c>scratch</c>, not via <see cref="ScribeCompleteTaskMessage"/>
    /// (design Decision 2 fallback, chosen):</b> the editor holds the edit lock and autosaves the WHOLE
    /// scratch document authoritatively (<see cref="FlushIfDirty"/> → lock-gated <c>ApplyEdit</c>). A
    /// lock-free completion message writes the shared document out-of-band, so the very next whole-doc
    /// flush would clobber its Sink reorder / Delete. Enacting the policy directly in scratch keeps
    /// everything on the one authoritative path the editor already owns, and the flush's
    /// <c>ReconcileActorPins</c> then carries the acting player's pin snapshot (and drops a pin whose task
    /// was deleted) for free — so the end state (done + Sink/Delete + pin effect) matches every other
    /// surface. The one pin effect the flush does NOT cover is <c>Unpin</c> (the task survives, so the
    /// snapshot reconcile keeps the pin); that is sent explicitly via the identity pin path.</para>
    ///
    /// <para>Focus preservation (8.5 "toggling a checkbox should NOT disturb the caret in another focused
    /// row"): the checkbox isn't <c>IFocusable</c>, so pressing it blurs whatever field was focused via
    /// LibGUI's <c>DispatchPointerDown</c> focus-clear (same root cause as the delete/pin/reorder controls
    /// — a05caret1). Keep/Unpin do no rebuild (the checkbox flips optimistically in its own State, leaving
    /// the focused field's State mounted), so re-request focus directly on the row that held it; the
    /// Sink/Delete branches delegate to <see cref="ReorderEditorBlock"/>/<see cref="DeleteEditorBlock"/>,
    /// which own their own rebuild + focus fix-up.</para></summary>
    private void ToggleEditorTask(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        if (!block.IsTask) return;

        bool nowDone = !block.Done;
        Guid taskId = block.TaskId;

        if (!scratch.ToggleTask(index)) return;
        isDirty = true;
        TraceScroll($"complete {index} done={nowDone} policy={modSystem.MySettings.CompletionPolicy}");

        // Apply the policy only on a transition INTO done — unchecking never sinks/deletes/unpins.
        if (nowDone)
        {
            switch (modSystem.MySettings.CompletionPolicy)
            {
                case ScribeCompletionPolicy.Delete:
                    // Drop the row (with the collapse animation + focus fix-up); the flush's
                    // ReconcileActorPins then removes any pin on the now-gone task. DeleteEditorBlock
                    // owns the rebuild, so return without the focus re-home below.
                    DeleteEditorBlock(index);
                    return;
                case ScribeCompletionPolicy.Sink:
                    // Move the (now-done) task to the document bottom — a real reorder of the shared doc
                    // once flushed, matching every other surface. ReorderEditorBlock owns the rebuild and
                    // keeps the moved row focused; a task already last is a safe no-op there. anchorViewport:
                    // true holds the scroll where it was (design: Sink completion shouldn't yank the viewport
                    // to the bottom the row sinks to — see the ReorderEditorBlock remarks + Phase 2 trace).
                    ReorderEditorBlock(index, scratch.Blocks.Count - 1, anchorViewport: true);
                    return;
                case ScribeCompletionPolicy.Unpin:
                    // The task stays, so the flush's snapshot reconcile keeps the pin — unpin explicitly.
                    if (IsPinnedForMe(taskId)) SendSetPin(taskId, false);
                    break;
                case ScribeCompletionPolicy.UnpinSink:
                    // Unpin + sink: move the task to the bottom (like Sink), AND unpin it (like Unpin).
                    ReorderEditorBlock(index, scratch.Blocks.Count - 1, anchorViewport: true);
                    if (IsPinnedForMe(taskId)) SendSetPin(taskId, false);
                    return;
                case ScribeCompletionPolicy.Keep:
                default:
                    break;
            }
        }

        // Keep/Unpin (and any un-check): no rebuild happened, so re-home the caret to the row that held
        // focus (a05caret1). Re-request focus WITHOUT the scroll-into-view that FocusEditorRow schedules:
        // clicking a checkbox on some other (possibly off-screen) row must not yank the viewport to the
        // still-focused edit row. That pendingEnsureVisible was the "unchecking a task under Keep/Sink
        // jumps the view" race (trace: complete N done=False → ensure-visible → changed to the focused
        // row's offset). The caret is already where the player left it; only the focus token needs re-
        // granting after DispatchPointerDown's press-clear, so RequestFocus alone is right.
        if (focusedEditIndex is { } held && held < editorFocusNodes.Count)
        {
            editorFocusNodes[held].RequestFocus();
        }
    }

    /// <summary>Per-row delete: remove the block from the scratch document and rebuild. Mirrors
    /// <see cref="OnClickAddTask"/> (mutate scratch -> mark dirty -> resync focus nodes -> rebuild),
    /// with focus cleanup so no path is left focusing a removed row: if the focused row was deleted
    /// or shifted, clear/adjust <see cref="focusedEditIndex"/> before the rebuild (spec "deleting the
    /// focused row does not break focus"). An empty document falls back to the editor's empty-state
    /// hint, which needs no focus.</summary>
    private void DeleteEditorBlock(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        TraceScroll($"delete {index}");

        // Snapshot the row BEFORE removing it, so it can keep rendering as a static, non-interactive ghost
        // while it collapses its height to zero (scribe-list-collapse). The scratch deletion still happens
        // immediately below, so the data model + autosave stay correct at once; only the visual removal is
        // deferred until the collapse completes. Its DISPLAY index (live scratch index offset by any rows
        // already departing above it) lets the ghost collapse in place rather than jumping.
        var deleted = scratch.Blocks[index];
        var snapshot = new ScribeEditRowData(index, deleted.IsTask, deleted.Done,
            IsPinnedForMe(deleted.TaskId), deleted.TaskId, deleted.Text);
        int displayIndex = index + departingEditorRows.Values.Count(d => d.Index <= index);
        departingEditorRows[deleted.TaskId] = (snapshot, displayIndex);

        if (!scratch.DeleteBlock(index))
        {
            departingEditorRows.Remove(deleted.TaskId); // deletion refused: don't leave a ghost behind
            return;
        }

        isDirty = true;

        // Fix up the focused index across the deletion: the focused row is gone (clear), or sat after
        // the deleted one (shift up by one), or before it (unchanged).
        if (focusedEditIndex is { } f)
        {
            if (f == index) focusedEditIndex = null;
            else if (f > index) focusedEditIndex = f - 1;
        }

        // Re-home the caret after the rebuild. Pressing the (non-IFocusable) delete button already blurred
        // the field via LibGUI's DispatchPointerDown focus-clear, so even a surviving edited row needs its
        // focus re-requested — nothing re-grants it otherwise, which is why the caret vanished (a05caret1).
        // If we deleted the focused row, land on the neighbor per design Q1: the row above (index-1), or the
        // new first row when the top row was deleted; an emptied document gets no focus (empty-state hint).
        if (scratch.Blocks.Count > 0)
        {
            int target = focusedEditIndex ?? Math.Max(0, index - 1);
            target = Math.Min(target, scratch.Blocks.Count - 1);
            focusedEditIndex = target;
            autoFocusRowOnRebuild = target;
        }
        else
        {
            focusedEditIndex = null;
        }

        SyncFocusNodesToScratch();
        pendingEnsureVisible = false;
        // The re-clamp to the shrunk extent is DEFERRED to when the collapse completes (see
        // OnEditorRowCollapsed): during the collapse the ghost still occupies (shrinking) height, so the
        // content extent doesn't actually shrink until the row reaches zero — clamping now would fight the
        // collapsing height (scribe-list-collapse). LibGUI's own clamp ignores a sub-50px overshoot, so the
        // deferred clamp is still needed once the row is gone (see pendingClampToExtent).
        ForceRebuild();
    }

    /// <summary>A deleted editor row finished collapsing to zero height (scribe-list-collapse): retire its
    /// ghost and, now that the content is genuinely shorter, re-clamp the scroll extent. Deferred out of the
    /// animation callback via <see cref="needsEditorCollapseCleanup"/> so we don't unmount + rebuild the tree
    /// re-entrantly from inside the ticker pump.
    ///
    /// <para>Scroll preservation (Phase 2 trace: delete was jumping the viewport to the TOP): the
    /// <see cref="needsEditorCollapseCleanup"/> <see cref="ForceRebuild"/> remounts the editor's
    /// <c>SingleChildScrollView</c>, which re-lays-out and (via LibGUI's <c>ClampOffset</c> against a
    /// transiently-zero content height) resets the offset to 0. The <see cref="RequestClampToExtent"/> loop
    /// can only clamp DOWN, so it can't recover from a reset-to-0. Capture the current offset so the
    /// <see cref="OnRenderGUI"/> restore loop re-applies it across that rebuild — the same "hold the offset"
    /// mechanism Pin uses. Restoring the pre-collapse offset and letting the natural clamp reduce it to the
    /// now-smaller max lands the viewport at the shortened list's bottom (the correct resting spot), not 0.</para></summary>
    private void OnEditorRowCollapsed(Guid taskId)
    {
        if (!departingEditorRows.Remove(taskId)) return;
        editorCollapseRegistry.Release(taskId.ToString("N"));
        CaptureScrollForRestore();
        RequestClampToExtent();
        needsEditorCollapseCleanup = true;
    }

    /// <summary>Per-row pin toggle (task rows only; the pin control is absent on text-section rows).
    /// Pinning is a per-player action now, NOT document state: fire a lock-free
    /// <see cref="ScribeSetPinMessage"/> keyed by the task's stable id, toggled against the client's
    /// own pin cache. It never touches the scratch document, the edit lock, or autosave. The server
    /// re-pushes this player's set, which lands in <see cref="OnMyPinsChanged"/> and repaints the row.</summary>
    private void TogglePinnedEditorTask(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        var block = scratch.Blocks[index];
        if (!block.IsTask) return; // no pin control on a text section
        SendSetPin(block.TaskId, !IsPinnedForMe(block.TaskId));
    }

    /// <summary>Fire-and-forget a pin/unpin for a task by stable identity. The document's DocId plus
    /// the task's TaskId fully address the pin; no block position is sent (so the same shape works
    /// from a future HUD). The server derives the snapshot from its own document.</summary>
    private void SendSetPin(Guid taskId, bool pinned)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetPinMessage
        {
            DocId = host.Document.DocId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Pinned = pinned,
        });
    }

    /// <summary>Drag-reorder drop: move the block from <paramref name="from"/> to <paramref name="to"/>
    /// in the scratch document and rebuild. A move-to-same index is a safe no-op
    /// (<see cref="ScribeDocument.MoveBlock"/> returns true without changing anything, so no edit is
    /// sent). Keeps the moved row focused across the rebuild.
    ///
    /// <para><paramref name="anchorViewport"/> chooses what the scroll offset does across the rebuild.
    /// A drag-reorder (false) chases the moved row with <see cref="pendingEnsureVisible"/> — the player
    /// dragged it, so following it into view is expected. A <b>Sink completion</b> (true) instead HOLDS
    /// the viewport where it was: completing a mid-list task should not yank the viewport to the bottom
    /// where the row sinks to (Phase 2 trace: Sink was scrolling to the end). We capture the offset before
    /// the rebuild and let the <see cref="OnRenderGUI"/> restore loop re-apply it — the same "hold still"
    /// mechanism Pin uses via <see cref="OnMyPinsChanged"/>. Note a <c>ForceRebuild</c> resets the editor's
    /// <c>SingleChildScrollView</c> offset to 0, so the capture+restore is required, not optional.</para></summary>
    private void ReorderEditorBlock(int from, int to, bool anchorViewport = false)
    {
        if (scratch is null) return;
        TraceScroll($"reorder {from}->{to} anchor={anchorViewport}");
        if (from == to)
        {
            // Released in place (or a grip click that never dragged): no edit — but the grip press already
            // blurred the focused field via LibGUI's DispatchPointerDown focus-clear (the grip isn't
            // IFocusable), so re-home the caret to the row that was being edited or nothing else will
            // (a05caret1). No rebuild is needed to move the doc; RequestFocus alone re-grants focus.
            if (focusedEditIndex is { } held && held < editorFocusNodes.Count) FocusEditorRow(held);
            return;
        }
        if (!scratch.MoveBlock(from, to)) return;

        isDirty = true;
        focusedEditIndex = to;
        SyncFocusNodesToScratch();
        autoFocusRowOnRebuild = to;
        if (anchorViewport)
        {
            // Sink: hold the viewport still across the rebuild instead of scrolling to the sunk row.
            CaptureScrollForRestore();
        }
        else
        {
            // Drag: follow the moved row into view.
            pendingEnsureVisible = true;
        }
        ForceRebuild();
    }

    /// <summary>"Add task" button: append an EMPTY task, grow the focus-node list, and rebuild with the
    /// new row auto-focused so the player types straight into it (no boilerplate to clear —
    /// add-empty-task-lifecycle). The field renders a dimmed "New task…" ghost hint while empty; if the
    /// row is abandoned without typing, its blur removes it (see <see cref="OnRowBlurred"/>).</summary>
    private void OnClickAddTask()
    {
        if (scratch is null) return;
        if (focusedEditIndex is { } leaving) NormalizeRowOnCommit(leaving);
        scratch.AddTask("");
        isDirty = true;
        SyncFocusNodesToScratch();
        autoFocusRowOnRebuild = scratch.Blocks.Count - 1;
        pendingEnsureVisible = true;
        ForceRebuild();
    }

    /// <summary>Moves editor focus to <paramref name="index"/> by requesting focus on that row's node
    /// (the row stays mounted — the editor uses a non-virtualized scroll container, design D2 — so its
    /// node is always live) and scheduling a scroll-into-view.</summary>
    private void FocusEditorRow(int index)
    {
        if (index < 0 || index >= editorFocusNodes.Count) return;
        editorFocusNodes[index].RequestFocus();
        focusedEditIndex = index;
        pendingEnsureVisible = true;
    }

    /// <summary>
    /// Commit-time text normalization for a row (ported from the native editor): strip trailing blank
    /// lines and trailing whitespace while PRESERVING interior newlines, e.g. "a\n\nb\n" → "a\n\nb".
    /// Prevents a stray trailing Shift+Enter from committing a row that looks empty but stays tall,
    /// while keeping intentional interior spacing. Applied only at genuine row-commit sites (Enter,
    /// Shift+Tab, add-task, switch-to-read, close) — NOT on every keystroke or autosave tick, which
    /// would fight a player who just pressed Shift+Enter. No leading trim (indenting may be intended).
    /// </summary>
    private void NormalizeRowOnCommit(int index)
    {
        if (scratch is null || index < 0 || index >= scratch.Blocks.Count) return;
        string current = scratch.Blocks[index].Text;
        string trimmed = current.TrimEnd();
        if (trimmed != current)
        {
            scratch.SetBlockText(index, trimmed);
            isDirty = true;
        }
    }

    /// <summary>True when the currently-focused editor row is a task whose text is empty/whitespace-only —
    /// a transiently-empty row the player is (or just was) editing (add-empty-task-lifecycle). The autosave
    /// tick uses this to avoid serializing that transient empty task; leaving the row / closing removes it.</summary>
    private bool FocusedRowIsEmptyTask()
    {
        if (scratch is null || focusedEditIndex is not { } idx || idx < 0 || idx >= scratch.Blocks.Count) return false;
        var block = scratch.Blocks[idx];
        return block.IsTask && string.IsNullOrWhiteSpace(block.Text);
    }

    /// <summary>Removes every empty/whitespace-only TASK block from the scratch document, marking dirty if
    /// any were removed (add-empty-task-lifecycle D5). Text sections are left untouched — an empty note is
    /// valid. Called at the terminal commit paths (switch-to-read, close) so an abandoned empty task the
    /// blur-removal hadn't yet swept is never flushed or shown in the read view. No rebuild/collapse — the
    /// caller tears the editor down or rebuilds into the read view immediately after.</summary>
    private void PurgeEmptyTasksFromScratch()
    {
        if (scratch is null) return;
        for (int i = scratch.Blocks.Count - 1; i >= 0; i--)
        {
            var block = scratch.Blocks[i];
            if (block.IsTask && string.IsNullOrWhiteSpace(block.Text) && scratch.DeleteBlock(i))
            {
                isDirty = true;
            }
        }
    }

    // ---------------- Autosave / flush (throttled, lock-gated) ----------------

    private void StartAutosaveTick()
    {
        autosaveTickListenerId ??= capi.Event.RegisterGameTickListener(OnAutosaveTick, 1000);
    }

    private void StopAutosaveTick()
    {
        if (autosaveTickListenerId is { } id)
        {
            capi.Event.UnregisterGameTickListener(id);
            autosaveTickListenerId = null;
        }
    }

    /// <summary>Autosave tick. Skips a flush while the focused row is a transiently-empty task
    /// (add-empty-task-lifecycle): the player is mid-typing an empty new row, and serializing it would
    /// round-trip an empty task into the shared document a beat before its blur removes it. Any OTHER
    /// dirty edit still flushes on the next tick once the focused row has content or focus has moved.</summary>
    private void OnAutosaveTick(float deltaTime)
    {
        if (FocusedRowIsEmptyTask()) return;
        FlushIfDirty();
    }

    /// <summary>Commits the title input (if active): trims, defaults empty to host's default title,
    /// clamps to <see cref="ScribeDocument.MaxTitleLength"/> chars, writes to <see cref="scratch"/>,
    /// resets the editing flag, and flushes. Safe to call when not editing (no-op) or when
    /// <see cref="scratch"/> is null (no-op).</summary>
    private void CommitTitleIfEditing()
    {
        if (!_isTitleEditing || scratch is null) return;
        var raw = _titleController?.Text ?? "";
        var trimmed = raw.Trim();
        var final = string.IsNullOrEmpty(trimmed)
            ? host.DefaultDocumentTitle
            : trimmed[..Math.Min(trimmed.Length, ScribeDocument.MaxTitleLength)];
        scratch.Title = final;
        _isTitleEditing = false;
        isDirty = true;
        FlushIfDirty();
    }

    /// <summary>FocusNode listener for the title input. Commits on focus loss and rebuilds only when the
    /// editing state actually changed (blur path). Focus-gain rebuilds are handled by the pencil onTap,
    /// so suppressing them here avoids a redundant rebuild that causes the editor footer to flicker.</summary>
    private void OnTitleFocusChanged()
    {
        if (!(_titleFocusNode?.HasFocus ?? true) && _isTitleEditing)
        {
            CommitTitleIfEditing();  // sets _isTitleEditing = false
            if (IsOpened()) ForceRebuild();
        }
    }

    private void FlushIfDirty()
    {
        if (!isDirty || scratch is null) return;

        var bytes = ScribeDocumentCodec.Serialize(scratch);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeEditDocumentMessage
        {
            PosX = host.Pos.X,
            PosY = host.Pos.Y,
            PosZ = host.Pos.Z,
            DocumentBytes = bytes,
        });
        isDirty = false;

        // Un-aliased fresh copy: scratch keeps mutating while editing continues.
        if (ScribeDocumentCodec.TryDeserialize(bytes, out var copy) && copy is not null)
        {
            host.ApplyLocalOptimisticEdit(copy);
        }
    }

    // ---------------- Focus-node lifecycle ----------------

    /// <summary>Resize <see cref="editorFocusNodes"/> to one node per scratch block, keeping existing
    /// nodes (so a rebuild after add-task doesn't disturb other rows' focus). Each node carries a
    /// listener that tracks the focused row index and commits the row being left when focus moves
    /// row→row (e.g. clicking another row). Click-away-to-nothing is covered by the autosave tick and
    /// the close/switch commit.</summary>
    private void SyncFocusNodesToScratch()
    {
        int want = scratch?.Blocks.Count ?? 0;

        while (editorFocusNodes.Count > want)
        {
            int last = editorFocusNodes.Count - 1;
            editorFocusNodes[last].Dispose();
            editorFocusNodes.RemoveAt(last);
        }
        while (editorFocusNodes.Count < want)
        {
            int i = editorFocusNodes.Count;
            var node = new FocusNode();
            node.AddListener(() => OnRowFocusChanged(i));
            editorFocusNodes.Add(node);
        }
    }

    private void OnRowFocusChanged(int index)
    {
        if (index < 0 || index >= editorFocusNodes.Count) return;
        if (!editorFocusNodes[index].HasFocus) return;

        // A different row just gained focus (e.g. click-to-edit another row): commit the row we left.
        if (focusedEditIndex is { } prev && prev != index)
        {
            NormalizeRowOnCommit(prev);
            FlushIfDirty();
        }
        focusedEditIndex = index;
    }

    private void DisposeFocusNodes()
    {
        foreach (var node in editorFocusNodes) node.Dispose();
        editorFocusNodes.Clear();
    }

    // ---------------- Lifecycle ----------------

    /// <summary>After layout, honor a pending scroll-into-view for the focused editor row (a focus
    /// move or a row that grew while typing). Deferred to here because
    /// <see cref="Scrollable.EnsureVisible"/> reads the target's live post-layout geometry.</summary>
    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);

        // Keep the window (hence the OuterArtBox art canvas) sized to the live Pixel Art Size (task 7.1).
        // The base only sets the window Size in CreateWindowConfig, which TryOpen runs ONCE per open — so a
        // live W change re-lays-out the content tree (via ForceRebuild) but leaves the window's _layoutSize
        // at the opened W, clamping the art Container to the stale size while the inner SizedBoxes grow past
        // it. Re-apply WindowSize from the current W and call the base SyncLayoutSize() (documented for a
        // programmatic WindowSize change) so the root re-lays-out at the new tight constraints. Done here (a
        // safe post-layout point) rather than in Build() to avoid mutating size mid-rebuild. No-op when W is
        // unchanged (SyncLayoutSize early-returns if _layoutSize already equals WindowSize).
        var liveLayout = host.GetLayout(modSystem.MySettings.PixelArtSize);
        var wantSize = new Vector2(liveLayout.W, liveLayout.H);
        if (WindowSize != wantSize)
        {
            WindowSize = wantSize;
            SyncLayoutSize();
        }

        // An editor row's collapse completed (its callback fired from inside the animation pump, where
        // unmounting the tree would be re-entrant); retire it now with a rebuild (scribe-list-collapse).
        if (needsEditorCollapseCleanup)
        {
            needsEditorCollapseCleanup = false;
            if (IsOpened()) ForceRebuild();
        }

        // A task row lost focus while empty (add-empty-task-lifecycle): remove it now, deferred out of the
        // blur notification so we don't dispose focus nodes mid focus-transition. Re-read from live scratch
        // and re-check emptiness so a stale index or a row that gained text in the meantime is a safe no-op
        // (idempotent with the OnRowFocusChanged commit that also fires on a row→row move). DeleteEditorBlock
        // handles the focus-to-above fixup (Q1) and the collapse animation.
        if (pendingEmptyRowRemoval is { } emptyIdx)
        {
            pendingEmptyRowRemoval = null;
            if (isEditorMode && scratch is not null && emptyIdx >= 0 && emptyIdx < scratch.Blocks.Count)
            {
                var block = scratch.Blocks[emptyIdx];
                // Never sweep a row that CURRENTLY holds keyboard focus, or is the row we still intend to
                // focus. The empty-task self-destruct is for rows the user genuinely LEFT — but a
                // freshly-inserted empty row (Enter / New Task) catches a TRANSIENT blur when the
                // ensure-visible SetState churns the subtree and its field remounts (the one-shot auto-focus
                // is already consumed), so OnRowBlurred scheduled it here even though the user never left it.
                // The trace signature was: insert-below N -> focus=N+1 -> delete N+1, the new row vanishing
                // "a few frames" after Enter. Guard on live focus so only a real leave removes the row; the
                // terminal PurgeEmptyTasksFromScratch on switch/close still guarantees no empty task persists.
                bool stillFocused =
                    (focusedEditIndex == emptyIdx)
                    || (emptyIdx < editorFocusNodes.Count && editorFocusNodes[emptyIdx].HasFocus);
                if (block.IsTask && string.IsNullOrWhiteSpace(block.Text) && !stillFocused)
                {
                    DeleteEditorBlock(emptyIdx);
                }
            }
        }

        if (pendingEnsureVisible && isEditorMode && focusedEditIndex is { } idx
            && idx < editorFocusNodes.Count && editorFocusNodes[idx].Owner is { } element)
        {
            TraceScroll("ensure-visible");
            Scrollable.EnsureVisible(element);
            pendingEnsureVisible = false;
        }

        // Re-apply a scroll offset captured across a view switch (see pendingRestoreScrollOffset). The
        // read view's ListView settles its content height over the first frame(s) after the swap, so a
        // single JumpTo can still be clamped; re-apply until the offset sticks or we've given a few
        // frames (a genuinely shorter list clamps to its real max and stops differing, so this also
        // self-terminates when the target is simply unreachable).
        if (pendingRestoreScrollOffset is { } want)
        {
            TraceScroll("restore");
            sharedScrollController.JumpTo(want);
            scrollRestoreFrames++;
            if (Math.Abs(sharedScrollController.Offset - want) < 0.5f || scrollRestoreFrames >= 5)
            {
                pendingRestoreScrollOffset = null;
            }
        }

        // Re-clamp after a delete once layout has reported the shrunk content height (see
        // pendingClampToExtent). We run the FULL settling window rather than stopping as soon as the
        // offset is within bounds: ContentSize is only reported on the layout pass(es) after the rebuild
        // and may still be the pre-delete (larger) value on the first frame — so an early "already within
        // max" check would terminate before the shrink is ever reflected. Clamping while already in
        // bounds is a harmless no-op (JumpTo only moves when Offset > max), so running all frames is safe.
        // Skipped while a restore is still pending so the two don't fight.
        if (pendingClampToExtent && pendingRestoreScrollOffset is null)
        {
            TraceScroll("clamp");
            float max = sharedScrollController.MaxScrollExtent;
            if (sharedScrollController.Offset > max) sharedScrollController.JumpTo(max);
            if (++clampToExtentFrames >= 5) pendingClampToExtent = false;
        }
    }

    /// <summary>Ask <see cref="OnRenderGUI"/> to clamp the shared scroll offset down to the new
    /// <c>MaxScrollExtent</c> after the next layout — used when a row removal shrinks the content so the
    /// viewport can't be left stranded past the (now smaller) bottom.</summary>
    private void RequestClampToExtent()
    {
        pendingClampToExtent = true;
        clampToExtentFrames = 0;
        TraceScroll("request-clamp");
    }

    public override void OnGuiClosed()
    {
        CommitTitleIfEditing();
        if (isEditorMode)
        {
            if (focusedEditIndex is { } closeIdx) NormalizeRowOnCommit(closeIdx);
            // Same empty-task cleanup as switch-to-read: closing with an abandoned empty task must not
            // persist it (add-empty-task-lifecycle D5).
            PurgeEmptyTasksFromScratch();
            pendingEmptyRowRemoval = null;
            FlushIfDirty();
            SendReleaseLockPacket();
            StopAutosaveTick();
            DisposeFocusNodes();
        }
        modSystem.MyPinsChanged -= OnMyPinsChanged;
        modSystem.SettingsVisibilityChanged -= OnSettingsVisibilityChanged;
        DisposePinState();
#if DEBUG
        sharedScrollController.OnChanged -= OnScrollControllerChanged;
#endif
        _titleFocusNode?.RemoveListener(OnTitleFocusChanged);
        _titleFocusNode?.Dispose();
        _titleController?.Dispose();
        // The dialog owns the shared scroll controller (see its field); dispose it once here rather
        // than in either view's State, which come and go with each view-switch ForceRebuild.
        sharedScrollController.Dispose();
        // Drop any in-flight collapse ghosts + their controllers so a reopen starts clean (scribe-list-collapse).
        departingEditorRows.Clear();
        editorCollapseRegistry.Dispose();
        base.OnGuiClosed();
    }

    private void SendReleaseLockPacket()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeReleaseLockMessage
        {
            PosX = host.Pos.X,
            PosY = host.Pos.Y,
            PosZ = host.Pos.Z,
        });
    }

    // ---------------- Build ----------------

    protected override Widget Build()
    {
        // Read the Pixel-Art Display preference AND the Pixel Art Size (W) fresh each build (mirrors how
        // RowStyle reads WindowFontScale fresh) so toggling either relays out this dialog on the
        // MyPinsChanged/UpdateMySettings rebuild with no reopen. On = Scribe's light theme + notebook art;
        // off = the player's global LibGUI theme with no art. W drives the whole proportional layout via
        // ScribeLayout; the window Size is derived from the same W in CreateWindowConfig (applied at open).
        bool pixelArt = modSystem.MySettings.PixelArtDisplay;
        var layout = host.GetLayout(modSystem.MySettings.PixelArtSize);

        // The OuterArtBox is the notebook art itself (or a bare box when Pixel-Art Display is OFF, or the
        // flat placeholder color when the PNG is missing — the existing gate + fallback, now at the root).
        // Sized to W × H so the stretch-to-fill backdrop is a uniform, distortion-free scale. There is no
        // WindowFrame: the tree below IS the header + content, so the art frames everything rather than
        // sitting as a strip beneath a stock bar.
        return new Theme(
            ScribeTheme.For(pixelArt),
            child: WrapBackdrop(pixelArt, layout, BuildOuterArtBox(layout)));
    }

    /// <summary>Wrap the layout tree in the OuterArtBox: the notebook backdrop <see cref="Container"/> sized to
    /// <c>W × H</c> when Pixel-Art Display is ON, or the tree in a bare same-sized box when OFF (the existing
    /// gate — scribe-gui-backdrops D5). The single <see cref="host.BackdropSpec"/> spec backs both
    /// views; a missing PNG degrades to the flat tan placeholder (existing fallback).
    /// <see cref="ScribeModSystem.GetBackdropBitmap"/> caches the bitmap, so this re-reads a cached reference
    /// each build (no reload). The size is pinned here (not only via the window Size) so the art fills the
    /// whole dialog exactly and the aspect can't drift.</summary>
    private Widget WrapBackdrop(bool pixelArt, ScribeLayout layout, Widget tree)
    {
        if (!pixelArt)
        {
            return new SizedBox(width: layout.W, height: layout.H, child: tree);
        }
        var bmp = modSystem.GetBackdropBitmap(host.BackdropSpec.Texture);
        var style = bmp is not null
            ? new BoxStyle { Texture = bmp, Width = layout.W, Height = layout.H }
            : new BoxStyle { Color = new Vector4(0.85f, 0.78f, 0.62f, 1.0f), Width = layout.W, Height = layout.H };
        return new Container(style: style, child: tree);
    }

    /// <summary>The OuterArtBox's contents: a vertical stack of the draggable TitleBar band and the
    /// three-column SectionInnerBox, framed by the notebook art (scribe-notebook-frame). The ~7% of H below
    /// the inner box is bottom margin (the Column is top-aligned by default).</summary>
    private Widget BuildOuterArtBox(ScribeLayout layout) =>
        new Column(
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                BuildTitleBar(layout),
                BuildSectionInnerBox(layout),
            });

    /// <summary>When Pixel-Art Display is OFF (no notebook art backdrop), wrap <paramref name="child"/> in a
    /// solid theme-surface panel so the title row and central content read as opaque panels rather than
    /// transparent gaps onto the world; when ON, the notebook art is the background, so return the child
    /// unwrapped. Uses <c>ThemeData.Default.ColorScheme.Surface</c> — the same fill (and reason) as the
    /// standalone Scribe Settings window's body — since the OFF Lectern follows the player's global LibGUI
    /// theme (scribe-themed-toggle). Deliberately panels only these two regions, not the whole window.</summary>
    private Widget FlatPanel(Widget child)
    {
        if (modSystem.MySettings.PixelArtDisplay) return child;
        return new Container(
            style: new BoxStyle { Color = ThemeData.Default.ColorScheme.Surface },
            child: child);
    }

    /// <summary>The TitleBar band (<c>W × 0.13H</c>) — the window's drag zone (see
    /// <see cref="WindowConfig.DragHandleHeight"/>). It holds a bottom-anchored, centered TitleTextButtons row
    /// (<c>0.75W × 0.065H</c>): the dialog title on the left (window text ×1.1) and a right-aligned group of
    /// SVG nav/close buttons. Closing works without the stock frame — the close button calls
    /// <see cref="GuiBase.TryClose"/>.</summary>
    private Widget BuildTitleBar(ScribeLayout layout)
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        // Title is 1.5× the window body text size — "50% larger" (v1-playtest-fixes 5.1). The ask moved
        // 50% → 100% (×2.0) then back down a relative 25% to ×1.5 after ×2.0 read too large in-game
        // (playtest 2026-07-27T18-22-10 then a follow-up). The body size is BaseWindowFontSize × the
        // player's WindowFontScale, so the title tracks a live font-scale change too.
        float titleFont = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale) * 1.5f;

        var titleStyle = new TextStyle { FontSize = titleFont, FontFamily = ScribeRowControlNudge.TitleFontFamily, Weight = FontWeight.Bold, Color = colors.OnSurface };
        var displayTitle = (_isTitleEditing ? null : (scratch?.Title ?? host.Document.Title)) ?? host.DefaultDocumentTitle;

        const float titleBtnSpacing = 6f;
        Widget titleSlot = _isTitleEditing
            ? new Expanded(new TextField(
                _titleController!,
                _titleFocusNode!,
                new TextFieldStyle { FillColor = new Vector4(0, 0, 0, 0), BorderThickness = 0, TextStyle = titleStyle },
                onKeyDown: e =>
                {
                    if (_titleController!.Text.Length >= ScribeDocument.MaxTitleLength
                        && !e.Ctrl && e.KeyCode is not ((int)GlKeys.BackSpace or (int)GlKeys.Delete
                            or (int)GlKeys.Left or (int)GlKeys.Right or (int)GlKeys.Home or (int)GlKeys.End))
                        e.Handled = true;
                    if (e.KeyCode is (int)GlKeys.Enter or (int)GlKeys.KeypadEnter or (int)GlKeys.Escape)
                    {
                        CommitTitleIfEditing();
                        ForceRebuild();
                        e.Handled = true;
                    }
                }))
            : new Expanded(new RichText(new TextSpan(displayTitle), titleStyle, maxLines: 1, overflow: TextOverflow.Ellipsis));

        // Pencil — icon-only (no chrome), same visual weight as the grip glyph.
        // Only shown in editor view (scratch is non-null); hidden in read and pin views.
        // Left margin = 1.5× the inter-button spacing, to separate it visually from the title text.
        float pencilSize = ScribeRowConstants.RowCheckboxSize * 1.1f * 0.75f;
        Widget? pencilSlot = scratch is not null
            ? new Padding(
                EdgeInsets.Only(left: titleBtnSpacing * 1.5f),
                WithTooltip("scribe:scribe-gui-title-edit-tooltip",
                    new GestureDetector(
                        onTap: _ =>
                        {
                            _titleController!.Value = new TextEditingValue(displayTitle, TextSelection.Collapsed(displayTitle.Length));
                            _isTitleEditing = true;
                            ForceRebuild();
                            _titleFocusNode!.RequestFocus();
                        },
                        child: new ScribeVsIconGlyph("scribeedit", pencilSize, colors.OnSurfaceVariant))))
            : null;

        Widget titleRow = new Row(
            mainAxisAlignment: MainAxisAlignment.SpaceBetween,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                titleSlot,
                // Trailing group: pencil (editor only) · drag-grip · close button.
                // (refine-settings-and-window-chrome). The whole TitleBar band is the drag zone via
                // WindowConfig.DragHandleHeight, and it signals that discoverably (players won't intuit an
                // invisible drag band). But a press landing ON the grip used to be swallowed instead of
                // moving the window: the tooltip wraps its child in a MouseRegion (needed for hover), which
                // is an active hit target, so GuiBase captures the pointer-down before its band-drag check
                // runs — and click-through can't coexist with the tooltip (an IgnorePointer would kill the
                // MouseRegion's hover too). So the grip owns its OWN window drag via a GestureDetector nested
                // INSIDE the tooltip: the outer MouseRegion still fires hover, and press→move→release moves
                // the window just like the band (§8.1; see the gripDragging fields + VSAPI-NOTES.md §LibGUI).
                // A "drag to move" tooltip labels it. Close reuses the delete SVG at 1.4× the per-row size.
                new Row(
                    crossAxisAlignment: CrossAxisAlignment.Center,
                    mainAxisSize: MainAxisSize.Min,
                    spacing: titleBtnSpacing,
                    children: pencilSlot is not null
                        ? new Widget[]
                        {
                            pencilSlot,
                            WithTooltip("scribe-gui-drag",
                                new GestureDetector(
                                    onPress: OnGripDragStart,
                                    onMove: OnGripDragMove,
                                    onRelease: OnGripDragEnd,
                                    child: new ScribeVsIconGlyph("scribegrip", ScribeRowConstants.RowCheckboxSize * 1.1f,
                                        colors.OnSurfaceVariant))),
                            TitleButton("scribeclose", "scribe-gui-close", colors.Error,
                                size: ScribeRowConstants.RowCheckboxSize * 1.4f, onTap: () => TryClose()),
                        }
                        : new Widget[]
                        {
                            WithTooltip("scribe-gui-drag",
                                new GestureDetector(
                                    onPress: OnGripDragStart,
                                    onMove: OnGripDragMove,
                                    onRelease: OnGripDragEnd,
                                    child: new ScribeVsIconGlyph("scribegrip", ScribeRowConstants.RowCheckboxSize * 1.1f,
                                        colors.OnSurfaceVariant))),
                            TitleButton("scribeclose", "scribe-gui-close", colors.Error,
                                size: ScribeRowConstants.RowCheckboxSize * 1.4f, onTap: () => TryClose()),
                        }),
            });

        return new SizedBox(
            width: layout.W,
            height: layout.TitleBarH,
            child: new Align(
                Alignment.BottomCenter,
                child: new SizedBox(
                    width: layout.TitleBtnsW,
                    height: layout.TitleBtnsH,
                    // Panel behind the title row when Pixel-Art is OFF (no art backdrop) so it isn't
                    // transparent onto the world; unchanged when ON (the art is the background). The row's
                    // content is inset symmetrically by 0.04·W on each side (plus the original 10px of
                    // left breathing room) so the title + close/grip group sit clear of the panel edges.
                    child: FlatPanel(new Padding(
                        EdgeInsets.Only(left: 10 + 0.04f * layout.W, right: 0.04f * layout.W),
                        child: titleRow)))));
    }

    // ---------------- Title-bar grip drag (§8.1) ----------------
    // The grip glyph moves the window itself, because a press on it is captured by the tooltip's
    // MouseRegion before GuiBase's title-band drag can fire (see the gripDragging fields' comment and
    // the BuildTitleBar grip note). We reproduce GuiBase's band-drag math here: capture the mouse and
    // window position on press, then track the raw-pixel mouse delta (converted to logical pixels via
    // GUIScale, the same conversion GuiBase.ToLogicalScreen uses) into the protected WindowPos each move.
    // GestureDetector holds the pointer capture across the move (EventDispatcher._capturedElement), so
    // OnMouseMove keeps dispatching to the grip even as the cursor leaves the glyph's bounds.

    private void OnGripDragStart(PointerEvent e)
    {
        gripDragging = true;
        gripDragStartMouseX = capi.Input.MouseX;
        gripDragStartMouseY = capi.Input.MouseY;
        gripDragStartWindowPos = WindowPos;
    }

    private void OnGripDragMove(PointerEvent e)
    {
        if (!gripDragging) return;
        // Raw-pixel delta since press → logical (UI-scaled) pixels, matching WindowPos's units.
        float scale = RuntimeEnv.GUIScale;
        float dx = (capi.Input.MouseX - gripDragStartMouseX) / scale;
        float dy = (capi.Input.MouseY - gripDragStartMouseY) / scale;
        WindowPos = new Vector2(gripDragStartWindowPos.X + dx, gripDragStartWindowPos.Y + dy);
        // OnRenderGUI syncs rootRo.ScreenOffset from WindowPos and clamps it on-screen every frame, so no
        // explicit relayout/hit-bounds sync is needed here — the position takes effect on the next frame.
    }

    private void OnGripDragEnd(PointerEvent e)
    {
        if (!gripDragging) return;
        gripDragging = false;
        // Persist the moved position under the same dialog key GuiBase's own band drag saves to, so the
        // window reopens where the player left it.
        capi.Gui.SetDialogPosition(DialogCode, new Vec2i((int)WindowPos.X, (int)WindowPos.Y));
    }

    /// <summary>The SectionInnerBox (<c>0.9W × 0.8H</c>, centered): a row of three full-height columns —
    /// a left spacer, the center tasks column hosting the existing scrolling read/editor content, and a
    /// right column of tooltipped nav icons. The three widths sum to <see cref="ScribeLayout.InnerW"/>
    /// exactly, so nothing overflows.</summary>
    private Widget BuildSectionInnerBox(ScribeLayout layout) =>
        new SizedBox(
            width: layout.InnerW,
            height: layout.InnerH,
            child: new Row(
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    new SizedBox(width: layout.SideColW),                             // SectionLeftCol (spacer)
                    // Panel behind the central content when Pixel-Art is OFF (no art backdrop); unchanged
                    // when ON. Only the tasks column and the title row get a flat panel, not the whole window.
                    new SizedBox(width: layout.TasksColW, child: FlatPanel(BuildCentralRegion())), // LecternTasksBox
                    new SizedBox(width: layout.SideColW, child: BuildRightColNav()),   // SectionRightCol
                }));

    /// <summary>SectionRightCol: a vertical stack of tooltipped nav buttons — Settings (gear → the shared
    /// standalone settings window), Read view (check glyph), Edit view (pencil), Pinned tasks (pin). All
    /// reuse the mod's registered SVGs (scribe-notebook-frame D3). Read/Edit switch the dialog's own view;
    /// Pinned switches to the Pin Tab (scribe-pin-editor).</summary>
    private Widget BuildRightColNav()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        // Sidebar nav buttons enlarged (v1-playtest-fixes 5.6): the base was RowCheckboxSize × 1.2; ×1.7 on
        // top of that grows BOTH the button box and its inscribed SVG, since ScribeRowButton derives its box
        // size AND glyph size from this one `size` value.
        float size = ScribeRowConstants.RowCheckboxSize * 1.7f;

        // Whether the editor lock is held by ANOTHER player (fix-multiplayer-editor-lock §4.1). When true
        // the Edit nav button reads as unavailable (dimmed glyph) and its tap surfaces the native error
        // instead of entering the editor — TryEnterEditor enforces the no-entry; this only styles it.
        bool editLockedByOther = host.IsLockedByOther(capi.World.Player.PlayerUID);

        // A soft drop shadow so the enlarged nav buttons read as raised chrome floating over the notebook
        // art (v1-playtest-fixes 5.6). Semi-transparent black, nudged down-right, gently blurred.
        var navShadow = new[]
        {
            new BoxShadow(
                Color: new Vector4(0f, 0f, 0f, 0.35f),
                Offset: new Vector2(2f, 2f),
                BlurRadius: 4f),
        };

        // Build baseline nav buttons; insert extra (subclass-supplied) between Pins and Settings.
        // Settings is always last per the nav contract.
        Widget readBtn = TitleButton("scribecheck", "scribe-gui-nav-read", colors.OnSurfaceVariant,
            size: size, onTap: EnterReadMode, boxShadows: navShadow,
            activeColor: viewMode == ScribeLecternView.Read ? ScribeRowConstants.NavActiveRead : null);
        Widget editBtn = TitleButton("scribeedit", "scribe-gui-nav-edit",
            editLockedByOther ? colors.OnSurfaceVariant with { W = 0.4f } : colors.OnSurfaceVariant,
            size: size, onTap: TryEnterEditor, boxShadows: navShadow,
            activeColor: viewMode == ScribeLecternView.Editor ? ScribeRowConstants.NavActiveEdit : null);
        // Pinned enlarged +15% (§10.2): the pin glyph reads a touch larger than the others.
        Widget pinBtn = TitleButton("scribepin", "scribe-gui-nav-pinned", colors.OnSurfaceVariant,
            size: size, onTap: OnClickSwitchToPinned, iconScale: 1.15f, boxShadows: navShadow,
            activeColor: viewMode == ScribeLecternView.Pinned ? ScribeRowConstants.NavActivePinned : null);
        // Settings gear LAST in the group (§10.1), always after any extra buttons.
        Widget settingsBtn = TitleButton("scribegear", "scribe-gui-nav-settings", colors.OnSurfaceVariant,
            size: size, onTap: modSystem.OpenSettings, boxShadows: navShadow,
            activeColor: modSystem.IsSettingsOpen ? ScribeRowConstants.NavActiveSettings : null);

        var navChildren = new Widget[] { readBtn, editBtn, pinBtn }
            .Concat(GetExtraNavButtons())
            .Append(settingsBtn)
            .ToArray();

        return new Column(
            spacing: 16,
            mainAxisAlignment: MainAxisAlignment.Start,
            crossAxisAlignment: CrossAxisAlignment.Start,
            mainAxisSize: MainAxisSize.Max,
            children: navChildren);
    }

    /// <summary>A tooltipped icon button reusing the per-row button chrome (<see cref="ScribeRowButton"/>).
    /// <paramref name="iconScale"/> grows just the glyph (not the box) — used to enlarge the pin +15%
    /// (§10.2). <paramref name="boxShadows"/> passes an optional drop shadow through to the button's
    /// <c>BoxStyle</c> (the sidebar nav buttons use one to read as raised chrome — v1-playtest-fixes 5.6).
    /// Protected so subclasses can build matching nav buttons in <see cref="GetExtraNavButtons"/>.</summary>
    protected Widget TitleButton(string iconName, string tooltipKey, Vector4 color, float size, Action onTap, float iconScale = 1f, BoxShadow[]? boxShadows = null, Vector4? activeColor = null) =>
        WithTooltip(tooltipKey, new ScribeRowButton(iconName: iconName, iconColor: color, size: size, onTap: onTap, iconScale: iconScale, boxShadows: boxShadows, activeColor: activeColor));

    /// <summary>Wrap a button in a localized hover tooltip (<c>scribe:&lt;key&gt;</c>), using the global
    /// overlay so it isn't clipped by the surrounding boxes. The bubble fills with the theme's
    /// <c>Background</c> (Tooltip renders it that way), so the content text uses its partner
    /// <c>OnBackground</c> — the same dark ink the Lectern's body text uses when Pixel-Art Display paints
    /// the light parchment theme. Without an explicit color the content defaulted to white, which washed
    /// out against the light bubble; resolving through <see cref="ScribeTheme.For"/> keeps it correct in
    /// both modes (dark ink on light paper when pixel-art is on, light text on the dark global theme when
    /// off).</summary>
    private Widget WithTooltip(string key, Widget child) =>
        new Tooltip(
            child: child,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(Lang.Get("scribe:" + key), new TextStyle
                {
                    FontSize = 13,
                    SoftWrap = true,
                    Color = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme.OnBackground,
                })),
            useGlobalOverlay: true);

    /// <summary>The tasks column's content region: the read or editor view. Its former gear-header chrome row
    /// moved to the SectionRightCol nav stack (scribe-notebook-frame), so this is now just the active view
    /// filling the column.</summary>
    private Widget BuildCentralRegion() => viewMode switch
    {
        ScribeLecternView.Editor   => BuildEditorContent(),
        ScribeLecternView.Pinned   => BuildPinnedContent(),
        ScribeLecternView.Visitors => BuildVisitorsContent(),
        _                          => BuildReadContent(),
    };

    /// <summary>The live row style for this build, derived from the player's current settings (NOT cached
    /// at open — add-settings-tab D4), so a window-font-scale change from the settings view repaints the
    /// open dialog on the next rebuild.</summary>
    private ScribeRowStyle RowStyle => ScribeRowStyle.FromSettings(modSystem.MySettings);

    private Widget BuildReadContent() =>
        new ScribeReadContent(
            // Snapshot the block list for this build into value copies (never a live block
            // reference), so a later mutation of the authoritative document can't alias into a built
            // row — a re-sync rebuilds instead. Pinned is a per-player query (IsPinnedForMe), not a
            // document field, so each row carries its TaskId and is tinted from the client cache.
            // Belt-and-suspenders (add-empty-task-lifecycle D5): the editor's blur-removal + terminal purge
            // keep an empty task out of the persisted document, so this filter should rarely matter — but
            // if an empty task ever reaches the read view (e.g. an older doc, or an autosave that raced a
            // clear), never render it as a blank checkbox row. Task-only: an empty note is valid. The
            // read-view toggle addresses tasks by TaskId, so dropping rows here doesn't misalign anything.
            blocks: host.Document.Blocks
                .Select((b, i) => new ScribeReadRowData(i, b.IsTask, b.Done, IsPinnedForMe(b.TaskId), b.TaskId, b.Text))
                .Where(r => !r.IsTask || !string.IsNullOrWhiteSpace(r.Text))
                .ToList(),
            onToggleTask: OnReadViewCompleteTask,
            onTogglePinned: OnReadViewTogglePinned,
            onSwitchToEditor: TryEnterEditor,
            // Symmetric 0.04·W horizontal inset on the footer button, from the same ScribeLayout width.
            footerButtonPadding: EdgeInsets.Symmetric(
                horizontal: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W),
            style: RowStyle,
            scrollController: sharedScrollController);

    private Widget BuildEditorContent()
    {
        var blocks = scratch!.Blocks
            .Select((b, i) => new ScribeEditRowData(i, b.IsTask, b.Done, IsPinnedForMe(b.TaskId), b.TaskId, b.Text))
            .ToList();

        int? autoFocus = autoFocusRowOnRebuild;
        autoFocusRowOnRebuild = null; // one-shot

        // Rows that were deleted but are still collapsing out (scribe-list-collapse), each with the display
        // index it held so the editor content can splice it back in place as a static, collapsing ghost.
        var departing = departingEditorRows.Values
            .Select(d => new ScribeDepartingEditorRow(d.Row, d.Index))
            .ToList();

        return new ScribeEditorContent(
            blocks: blocks,
            focusNodes: editorFocusNodes,
            autoFocusIndex: autoFocus,
            onTextChanged: NotifyTextChanged,
            onCommitAndAdvance: EditorAdvanceFrom,
            onCommitAndRetreat: EditorRetreatFrom,
            onInsertTaskBelow: EditorInsertTaskBelow,
            onRowBlurred: OnRowBlurred,
            onToggleTask: ToggleEditorTask,
            onDeleteBlock: DeleteEditorBlock,
            onTogglePinned: TogglePinnedEditorTask,
            // Drag-reorder follows the moved row into view (anchorViewport defaults false); only a Sink
            // completion passes anchorViewport: true to hold the viewport still.
            onReorderBlock: (from, to) => ReorderEditorBlock(from, to),
            onAddTask: OnClickAddTask,
            onSwitchToRead: OnClickSwitchToRead,
            // Symmetric 0.04·W horizontal inset on the footer button row, from the same ScribeLayout width.
            footerButtonPadding: EdgeInsets.Symmetric(
                horizontal: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W),
            style: RowStyle,
            scrollController: sharedScrollController,
            departingRows: departing,
            collapseRegistry: editorCollapseRegistry,
            onDepartingCollapsed: OnEditorRowCollapsed);
    }

    /// <summary>Read-view task checkbox click: complete the task by its stable identity via the
    /// lock-free <see cref="ScribeCompleteTaskMessage"/> (the read view holds no editor lock). If the
    /// player has pinned this task, the server completes it store-first under their completion policy;
    /// otherwise it just toggles the shared document's done flag — the same gesture the HUD reuses.</summary>
    private void OnReadViewCompleteTask(Guid taskId)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeCompleteTaskMessage
        {
            DocId = host.Document.DocId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Policy = (byte)modSystem.MySettings.CompletionPolicy,
        });
    }

    /// <summary>Read-view pin toggle (scribe-lectern-view-consistency §2): pin/unpin the task by its
    /// stable identity, reusing the same lock-free <see cref="SendSetPin"/> path the editor row uses.
    /// The read view holds no scratch document, so it addresses the pin purely by TaskId.
    ///
    /// <para>Scroll preservation is handled in <see cref="OnMyPinsChanged"/> immediately before the
    /// rebuild — capturing here (pre-network-round-trip) was too early and the restore loop expired
    /// before the async callback arrived (v1-playtest-fixes second pass).</para></summary>
    private void OnReadViewTogglePinned(Guid taskId)
    {
        SendSetPin(taskId, !IsPinnedForMe(taskId));
    }

    // ---------------- Pin Tab (scribe-pin-editor) ----------------

    /// <summary>The Pin Tab body: the player's pins across every document (in pin-list order, no row cap),
    /// each row editable by default reusing the editor's <see cref="ScribeEditRow"/> rendering but sourced
    /// from <see cref="ScribeModSystem.MyPins"/>, plus the completion-policy picker. Focus is coordinated
    /// through the dialog-owned <see cref="pinFocusNodes"/> (keyed by TaskId) the same way the editor
    /// coordinates its index-keyed nodes.</summary>
    private Widget BuildPinnedContent()
    {
        SyncPinFocusNodes();

        // Same driving width the rest of the dialog uses, so the policy picker's inset tracks the layout.
        float layoutW = host.GetLayout(modSystem.MySettings.PixelArtSize).W;

        Guid? autoFocus = autoFocusPinTaskId;
        autoFocusPinTaskId = null; // one-shot

        // Apply sink ordering only when the policy actually sinks done pins. Under Keep, done pins
        // hold their position (same behaviour as the HUD's SunkForOrder). Unpin/Delete remove the
        // pin entirely, so ordering is moot for them and raw order is fine.
        var policy = modSystem.MySettings.CompletionPolicy;
        bool sinkOrder = policy is ScribeCompletionPolicy.Sink or ScribeCompletionPolicy.UnpinSink;
        var orderedPins = sinkOrder
            ? ScribePinOrdering.ForDisplay(modSystem.MyPins)
            : (IReadOnlyList<ScribePinnedRef>)modSystem.MyPins;

        // Each row's text seeds from its live edit buffer if one is in flight (a keystroke mid-resync),
        // else the authoritative server snapshot — the Pin Tab's equivalent of the editor re-seeding from
        // its scratch doc across a ForceRebuild (which fully unmounts + remounts the field).
        var rows = orderedPins
            .Select(p => new ScribePinRowData(
                p.OwnerDocId, p.TaskId, p.LastKnownDone,
                pinEditBuffer.TryGetValue(p.TaskId, out var buffered) ? buffered : p.LastKnownText))
            .ToList();

        return new ScribePinnedContent(
            rows: rows,
            focusNodes: pinFocusNodes,
            autoFocusTaskId: autoFocus,
            onTextChanged: OnPinTextChanged,
            onCommitText: CommitPinTextEdit,
            onToggleComplete: OnPinCompleteTask,
            onDelete: OnPinDeleteTask,
            onUnpin: OnPinUnpinTask,
            onReorder: OnPinReorder,
            completionPolicy: modSystem.MySettings.CompletionPolicy,
            onCompletionPolicyChanged: p => modSystem.UpdateMySettings(s => s.CompletionPolicy = p),
            // Match the title row's horizontal inset (left: 10 + 0.04·W, right: 0.04·W) so the picker lines
            // up with the title band; W comes from the same ScribeLayout the rest of the dialog uses.
            policyPickerPadding: EdgeInsets.Only(left: 10 + 0.04f * layoutW, right: 0.04f * layoutW),
            style: RowStyle,
            scrollController: sharedScrollController);
    }

    /// <summary>Guestbook tab content: a read-only (except own note) three-column visitor table.
    /// Virtual so the Desk or other future blocks can override if needed; the default reads from
    /// <see cref="IScribeDocumentHost.Guestbook"/> and needs no override for standard blocks.</summary>
    protected virtual Widget BuildVisitorsContent()
    {
        var colors      = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize  = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        float dateSize  = bodySize * 0.7f;
        float noteSize  = bodySize * 0.8f;
        var headerStyle = new TextStyle { FontFamily = ScribeRowControlNudge.TitleFontFamily, Weight = FontWeight.Bold, FontSize = bodySize, Color = colors.OnSurface };
        var bodyStyle   = new TextStyle { FontSize = bodySize, Color = colors.OnSurface };
        var dateStyle   = new TextStyle { FontSize = dateSize, Color = colors.OnSurface with { W = colors.OnSurface.W * 0.7f } };
        var myName = capi.World.Player.PlayerName;

        var entries = host.Guestbook.Entries;

        // Newest-first display (entries are stored oldest-first in the store).
        var rows = new List<Widget>(entries.Count);
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            Widget noteSlot;
            if (entry.PlayerName == myName)
            {
                string current = entry.Note;
                string lastSent = entry.Note;
                noteSlot = new Expanded(
                    new Padding(EdgeInsets.Only(bottom: 2f),
                        new ScribeMultilineField(
                            initialText: entry.Note,
                            fontSize: noteSize,
                            fontFamily: ScribeTaskFont.Resolve(modSystem.MySettings.TaskFontFamily),
                            maxLength: GuestbookStore.MaxNoteLength,
                            onChanged: text => current = text,
                            onBlur: () =>
                            {
                                var trimmed = current.Trim();
                                if (trimmed == lastSent) return;
                                lastSent = trimmed;
                                capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(
                                    new ScribeEditGuestbookNoteMessage
                                    {
                                        PosX = host.Pos.X, PosY = host.Pos.Y, PosZ = host.Pos.Z,
                                        Note = trimmed,
                                    });
                            })),
                    flex: 5);
            }
            else
            {
                var noteStyle = new TextStyle { FontSize = noteSize, Color = colors.OnSurface };
                noteSlot = new Expanded(
                    new Padding(EdgeInsets.Only(bottom: 2f),
                        new Text(entry.Note, noteStyle)),
                    flex: 5);
            }

            rows.Add(new Row(children: new Widget[]
            {
                new Expanded(
                    new Column(
                        spacing: 0,
                        mainAxisSize: MainAxisSize.Min,
                        crossAxisAlignment: CrossAxisAlignment.Stretch,
                        children: new Widget[]
                        {
                            new Text(entry.PlayerName, bodyStyle),
                            new Text(entry.InGameDate, dateStyle),
                        }),
                    flex: 3),
                noteSlot,
            }));
        }

        Widget body = entries.Count == 0
            ? new Center(child: new Text(Lang.Get("scribe:scribe-guestbook-empty"), bodyStyle))
            : new Scrollbar(controller: sharedScrollController,
                child: new SingleChildScrollView(controller: sharedScrollController,
                    child: new Column(children: rows.ToArray(), mainAxisSize: MainAxisSize.Min)))
              { AutoHide = false };

        return new Padding(
            EdgeInsets.All(10),
            new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    new Row(children: new Widget[]
                    {
                        new Expanded(new Text(Lang.Get("scribe:scribe-guestbook-col-visitor"), headerStyle), flex: 3),
                        new Expanded(new Text(Lang.Get("scribe:scribe-guestbook-col-note"),    headerStyle), flex: 5),
                    }),
                    new Divider(),
                    new Expanded(body),
                }));
    }

    /// <summary>Keep <see cref="pinFocusNodes"/> in sync with the current pin set: add a node for each new
    /// pin, dispose+drop nodes for pins that left, and prune stale edit buffers. Each node carries a
    /// listener that tracks the focused row and commits the row being left on a row→row focus move (the
    /// editor's <see cref="OnRowFocusChanged"/> pattern, keyed by TaskId).</summary>
    private void SyncPinFocusNodes()
    {
        var live = modSystem.MyPins.Select(p => p.TaskId).ToHashSet();

        foreach (var taskId in pinFocusNodes.Keys.ToList())
        {
            if (!live.Contains(taskId))
            {
                pinFocusNodes[taskId].Dispose();
                pinFocusNodes.Remove(taskId);
            }
        }
        foreach (var taskId in live)
        {
            if (!pinFocusNodes.ContainsKey(taskId))
            {
                var node = new FocusNode();
                var id = taskId; // capture per-iteration
                node.AddListener(() => OnPinRowFocusChanged(id));
                pinFocusNodes[taskId] = node;
            }
        }

        // Drop edit buffers for pins no longer present so the dictionary can't grow unbounded.
        foreach (var taskId in pinEditBuffer.Keys.ToList())
        {
            if (!live.Contains(taskId)) pinEditBuffer.Remove(taskId);
        }
    }

    private void OnPinRowFocusChanged(Guid taskId)
    {
        if (!pinFocusNodes.TryGetValue(taskId, out var node) || !node.HasFocus) return;
        // A different row just gained focus (click-to-edit another row): commit the row we left.
        if (focusedPinTaskId is { } prev && prev != taskId) CommitPinTextEdit(prev);
        focusedPinTaskId = taskId;
    }

    /// <summary>Live text-change from a focused Pin Tab field: buffer it (write-through, so a resync
    /// rebuild re-seeds from the buffer, not the stale snapshot). No network send yet — the edit commits on
    /// blur/Enter (<see cref="CommitPinTextEdit"/>).</summary>
    private void OnPinTextChanged(Guid taskId, string text)
    {
        pinEditBuffer[taskId] = text;
    }

    /// <summary>Commit a Pin Tab row's buffered text edit: send the identity-addressed edit if the text
    /// changed from the server snapshot and is non-blank, then drop the buffer. A blank/whitespace-only
    /// edit is dropped WITHOUT sending (the server would reject it anyway — spec "blank edit is rejected");
    /// the field re-seeds from the unchanged snapshot on the next rebuild. Called on blur, Enter, and a
    /// row→row focus move.</summary>
    private void CommitPinTextEdit(Guid taskId)
    {
        if (!pinEditBuffer.TryGetValue(taskId, out var text)) return;
        pinEditBuffer.Remove(taskId);

        var pin = modSystem.MyPins.FirstOrDefault(p => p.TaskId == taskId);
        if (pin is null) return; // pin left the set meanwhile

        string trimmed = text.TrimEnd(); // commit-time normalization, matching the editor (no leading trim)
        if (string.IsNullOrWhiteSpace(trimmed)) return; // reject blank — leave the task unchanged
        if (trimmed == pin.LastKnownText) return;        // no change

        SendEditPinnedTask(pin.OwnerDocId, taskId, trimmed);
    }

    /// <summary>Pin Tab checkbox: complete the task by identity with NO undo delay (unlike the HUD) — send
    /// the completion immediately under the player's current policy. Reuses the existing
    /// <see cref="ScribeCompleteTaskMessage"/> (the server toggles store-first, applies the policy, and
    /// re-pushes, which lands in <see cref="OnMyPinsChanged"/>).</summary>
    private void OnPinCompleteTask(Guid docId, Guid taskId)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeCompleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Policy = (byte)modSystem.MySettings.CompletionPolicy,
        });
    }

    /// <summary>Pin Tab delete control: delete the underlying task by identity (a standalone action, not a
    /// completion side effect) via <see cref="ScribeDeleteTaskMessage"/>. Drop any in-flight edit buffer so
    /// a stale commit can't fire after the row is gone.</summary>
    private void OnPinDeleteTask(Guid docId, Guid taskId)
    {
        pinEditBuffer.Remove(taskId);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeDeleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
        });
    }

    /// <summary>Pin Tab unpin control: remove only the pin (the task survives), via the existing
    /// <see cref="ScribeSetPinMessage"/> with <c>Pinned = false</c> — no block resolution needed.</summary>
    private void OnPinUnpinTask(Guid docId, Guid taskId)
    {
        pinEditBuffer.Remove(taskId);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetPinMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Pinned = false,
        });
    }

    /// <summary>Pin Tab drag-reorder drop: send the whole new pin order (permuting the current list so the
    /// pin at <paramref name="from"/> lands at <paramref name="to"/>) via <see cref="ScribeReorderPinsMessage"/>.
    /// The server permutes only this player's list and re-pushes. A move-to-same index is a no-op.</summary>
    private void OnPinReorder(int from, int to)
    {
        var pins = modSystem.MyPins;
        if (from == to || from < 0 || to < 0 || from >= pins.Count || to >= pins.Count) return;

        var order = pins.Select(p => (p.OwnerDocId, p.TaskId)).ToList();
        var moved = order[from];
        order.RemoveAt(from);
        order.Insert(to, moved);

        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeReorderPinsMessage
        {
            DocIds = order.Select(o => o.Item1.ToByteArray()).ToList(),
            TaskIds = order.Select(o => o.Item2.ToByteArray()).ToList(),
        });
    }

    /// <summary>Fire the identity-addressed pin-edit message. The document's DocId + the task's TaskId fully
    /// address the edit; no block position is sent. The server writes through (best-effort) and re-pushes.</summary>
    private void SendEditPinnedTask(Guid docId, Guid taskId, string text)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeEditPinnedTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Text = text,
        });
    }

    /// <summary>Dispose every Pin Tab focus node and clear the buffers (called from
    /// <see cref="OnGuiClosed"/>).</summary>
    private void DisposePinState()
    {
        foreach (var node in pinFocusNodes.Values) node.Dispose();
        pinFocusNodes.Clear();
        pinEditBuffer.Clear();
    }
}
