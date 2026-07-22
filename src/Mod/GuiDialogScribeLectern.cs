using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// The lectern's GUI. Two independent view modes:
/// - Read view (default, lock-free): renders <see cref="BlockEntityScribeLectern.Document"/>
///   live; never mutates anything, never sends anything.
/// - Editor view (shift+right-click, or the in-GUI toggle; holds the server-tracked
///   single-editor lock): edits a private scratch copy, autosaved to the server on a throttled
///   tick while dirty.
///
/// A <c>GuiDialogBlockEntity</c> rather than a plain <c>GuiDialog</c>, for the engine's own
/// per-block-position dialog dedup and walk-away auto-close. (The auto-close needs a range-check
/// override for Creative mode -- see <see cref="IsInRangeOfBlock"/>.)
/// </summary>
public sealed class GuiDialogScribeLectern : GuiDialogBlockEntity
{
    private readonly BlockEntityScribeLectern lectern;
    private readonly ScribeClientConfig clientConfig;

    public bool IsEditorMode { get; private set; }

    /// <summary>Editor-view-only scratch copy; never aliased to <see cref="BlockEntityScribeLectern.Document"/>.</summary>
    private ScribeDocument? scratchDocument;

    private bool isDirty;
    private long? autosaveTickListenerId;

    private bool isToolPanelExpanded = true;

    /// <summary>The block index of the editor row currently being edited in place (the row the
    /// single floating <see cref="ScribeRowTextInput"/> is positioned over), or null when no row
    /// is being edited. That row suppresses drawing its own text label and hosts the live input;
    /// every other row draws a static text label (design.md Decision 1). Reset in
    /// <see cref="EnterMode"/> so entering the editor starts with no row focused.</summary>
    private int? focusedEditIndex;

    /// <summary>The single live edit input for the current compose, or null when no row is being
    /// edited. Held so the compose tail can seed its value + focus it, and so a recompose can
    /// snapshot its caret before rebuilding. There is only ever ONE (design.md Decision 1's
    /// single-live-input invariant): it is added to exactly one row per compose and never more.</summary>
    private ScribeRowTextInput? editInput;

    /// <summary>The block index <see cref="editInput"/> was composed onto (mirrors
    /// <see cref="focusedEditIndex"/> at compose time). Read by
    /// <see cref="RecomposeEditorViewPreservingFocus"/> so it only restores the old caret position
    /// when the recompose stayed on the SAME row (e.g. a text-size change), not when focus moved to
    /// a different row (Enter/Shift+Tab/click), where the new row should start caret-at-end.</summary>
    private int? editInputIndex;

    /// <summary>Set while a recompose is tearing down the editor's live input, so the input's
    /// <see cref="ScribeRowTextInput.OnFocusLost"/> blur-commit doesn't fire for a
    /// programmatic (recompose-driven) focus loss -- only a genuine click-away should commit on
    /// blur. The real commit for a recompose path is done explicitly by the caller (Enter/
    /// Shift+Tab/row-click all flush before recomposing), so suppressing the blur here just avoids
    /// a redundant second flush, never a dropped edit.</summary>
    private bool suppressBlurCommit;

    /// <summary>
    /// One entry per tool-panel button. <c>Icon</c> is a built-in icon-font code (see
    /// <c>Vintagestory.API.Client.IconUtil</c>) drawn on a small square button, with
    /// <c>LangKey</c>'s text shown as a hover tooltip rather than a label — the built-in icon set
    /// has no dedicated reorder/edit glyph, but a bare icon reads better at this size than a
    /// truncated word. <c>IsVisible</c> is the gating hook for future context-sensitive tools
    /// (e.g. hide "Reorder" below two rows) — v1 wires every option visible unconditionally, per
    /// task 5.3.
    /// </summary>
    private readonly record struct ToolbarOption(string Key, string Icon, string LangKey, System.Func<bool> IsVisible, ActionConsumable OnActivate);

    public GuiDialogScribeLectern(ICoreClientAPI capi, BlockEntityScribeLectern lectern, bool isEditorMode, byte[]? documentBytes)
        : base(Lang.Get("scribe:scribe-gui-title"), lectern.Pos, capi)
    {
        this.lectern = lectern;
        this.clientConfig = capi.LoadModConfig<ScribeClientConfig>(ScribeModSystem.ClientConfigFileName) ?? new ScribeClientConfig();

        // Clamp a pre-existing saved value into the current range -- a config saved before
        // these bounds were introduced (or before they were changed) could fall outside them.
        clientConfig.TextSizeScale = System.Math.Clamp(clientConfig.TextSizeScale, clientConfig.MinTextSizePercent / 100f, clientConfig.MaxTextSizePercent / 100f);

        if (IsDuplicate) return;

        EnterMode(isEditorMode, documentBytes);

#if DEBUG
        RegisterDebugSliders();
#endif
    }

#if DEBUG
    /// <summary>Debug-only (see add-imgui-configlib-tuning design.md decisions 1-2 --
    /// excluded from Release builds by <c>Mod.csproj</c>'s Condition on the VSImGui
    /// <c>ItemGroup</c>, not just this preprocessor guard) live-tuning sliders for the
    /// row-list layout fields most relevant to diagnosing rendering issues. Each
    /// <c>FloatSlider</c> call registers a persistent entry drawn automatically by
    /// VSImGui's own always-on debug-window handler (confirmed via decompile -- no manual
    /// per-frame draw call or event subscription needed here); the returned ids are
    /// tracked so <see cref="OnGuiClosed"/> can remove them via
    /// <see cref="VSImGui.Debug.DebugWidgets.Remove"/>, since a closed dialog's
    /// <c>clientConfig</c> instance would otherwise leave stale getter/setter closures
    /// registered against a dialog that no longer exists.</summary>
    private readonly System.Collections.Generic.List<int> debugSliderIds = new();

    private const string DebugDomain = "scribe";
    private const string DebugCategory = "Lectern Layout";

    private void RegisterDebugSliders()
    {
        debugSliderIds.Add(VSImGui.Debug.DebugWidgets.FloatSlider(
            DebugDomain, DebugCategory, "Visible List Height", 100f, 1000f,
            () => (float)clientConfig.VisibleListHeight,
            value => { clientConfig.VisibleListHeight = value; RequestRecompose(); }));

        debugSliderIds.Add(VSImGui.Debug.DebugWidgets.FloatSlider(
            DebugDomain, DebugCategory, "Row Spacing", 0f, 60f,
            () => (float)clientConfig.RowSpacing,
            value => { clientConfig.RowSpacing = value; RequestRecompose(); }));

        debugSliderIds.Add(VSImGui.Debug.DebugWidgets.FloatSlider(
            DebugDomain, DebugCategory, "Top Content Gap", 0f, 100f,
            () => (float)clientConfig.TopContentGap,
            value => { clientConfig.TopContentGap = value; RequestRecompose(); }));

        debugSliderIds.Add(VSImGui.Debug.DebugWidgets.FloatSlider(
            DebugDomain, DebugCategory, "Row List Width", 100f, 800f,
            () => (float)clientConfig.RowListWidth,
            value => { clientConfig.RowListWidth = value; RequestRecompose(); }));

        debugSliderIds.Add(VSImGui.Debug.DebugWidgets.Button(DebugDomain, DebugCategory,
            "Save to scribe-client-config.json",
            () => capi.StoreModConfig(clientConfig, ScribeModSystem.ClientConfigFileName)));
    }

    private void UnregisterDebugSliders()
    {
        foreach (int id in debugSliderIds)
        {
            VSImGui.Debug.DebugWidgets.Remove(DebugDomain, id);
        }
        debugSliderIds.Clear();
    }
#endif

    private CairoFont RowFont() =>
        CairoFont.TextInput().WithFontSize((float)(GuiStyle.NormalFontSize * clientConfig.TextSizeScale));

    private int TextSizePercent => (int)System.Math.Round(clientConfig.TextSizeScale * 100);

    /// <summary>Row gap scaled by the current text size, so spacing grows/shrinks with the rows
    /// (mirrors how <c>ScribeBlockRowCell.RowHeight</c> scales row height). The config value
    /// itself stays the unscaled base -- scaling here at the point of use, not by mutating the
    /// stored value, avoids compounding the scale on every recompose/reopen.</summary>
    private double ScaledRowSpacing => clientConfig.RowSpacing * clientConfig.TextSizeScale;

    /// <summary>The row list's content bounds (the single element every row is parented under)
    /// for whichever view is currently composed. Re-set at the top of each ComposeXxxView call;
    /// only one view is ever live at a time so one field suffices. No longer scroll-shifted --
    /// rows are composed at a viewport-relative Y (design.md Decision 4, third correction), so
    /// this is now only a non-null "a row list has been composed" guard for
    /// <see cref="OnRowListScroll"/>.</summary>
    private ElementBounds? rowListContentBounds;

    /// <summary>The row list's current scroll offset, in the same units <c>AddVerticalScrollbar</c>
    /// reports. Read by ComposeReadView/ComposeEditorView at the start of every compose (not just
    /// the first) so a culling-triggered recompose (see <see cref="OnRowListScroll"/>) preserves
    /// scroll position instead of snapping back to the top. Reset to 0 in <see cref="EnterMode"/>
    /// since opening a dialog or switching view mode should start scrolled to the top.</summary>
    private double rowListScrollValue;

    /// <summary>One-shot request to scroll the currently focused editor row fully into view on the
    /// next editor compose. Set by the focus-moving actions (Add Task, Enter/Shift+Tab navigation)
    /// so a row that lands outside the visible window -- e.g. a task appended to the bottom of an
    /// overflowing list -- is scrolled to rather than left off-screen (task 6.11). Consumed (and
    /// cleared) in <see cref="ComposeEditorView"/>. NOT set on an ordinary click-to-focus (the
    /// clicked row is already visible) or a live resync, so it never fights the user's own scroll.</summary>
    private bool scrollFocusedRowIntoView;

    /// <summary>Suppresses <see cref="OnRowListScroll"/>'s recompose-triggering logic while a
    /// ComposeXxxView call is already in progress. <c>AddVerticalScrollbar(...).SetHeights(...)</c>
    /// always fires this callback synchronously with a freshly-constructed scrollbar's value (0),
    /// regardless of the real scroll position being restored right after -- without this guard,
    /// that transient 0 would immediately trigger a second, unnecessary (and endlessly
    /// re-entrant, since the recursive compose call would trigger it again) recompose.</summary>
    private bool isComposingRowList;

    /// <summary>Set by any callback that determines a recompose is needed, but must not act on
    /// it synchronously -- drained on the next <see cref="OnRenderGUI"/> tick instead. Every
    /// button/toggle/switch click handler in this dialog (<c>GuiElementToggleButton</c>,
    /// <c>GuiElementTextButton</c>, <c>GuiElementSwitch</c>) fires its callback from inside
    /// <c>OnMouseDownOnElement</c>/<c>OnMouseUpOnElement</c>, which <c>GuiComposer</c> calls from
    /// its own <c>OnMouseDown</c>/<c>OnMouseUp</c>/<c>OnKeyDown</c> while those methods are still
    /// iterating their own <c>interactiveElements</c> collection -- tearing down and replacing
    /// <c>SingleComposer</c> mid-iteration corrupts that in-progress dispatch. Confirmed two ways:
    /// live (a row rendered detached above the title bar, from a build that recomposed
    /// synchronously from the scrollbar's scroll callback) and via the client crash log itself
    /// (a <c>GuiElementDialogBackground</c> blur exception with stack
    /// <c>OnClickAddTask</c> &lt;- <c>GuiElementToggleButton.OnMouseDownOnElement</c> &lt;-
    /// <c>GuiComposer.OnMouseDown</c> -- the exact same reentrancy class, via the Add Task button
    /// rather than the scrollbar). Originally scoped to just the scrollbar's callback
    /// (<c>rowListRecomposePending</c>); generalized to every mid-dispatch recompose site
    /// (<see cref="OnClickAddTask"/>, <see cref="OnClickToggleToolPanel"/>,
    /// <see cref="OnRequestEditRow"/>, <see cref="OnEditViewToggleTask"/>,
    /// <see cref="OnClickSwitchToRead"/>) once each was found to share the identical hazard. Mirrors
    /// <see cref="textSizePendingRecompose"/>'s existing, already-proven defer-to-next-safe-point
    /// pattern for the same class of problem, generalized to <c>OnRenderGUI</c> rather than
    /// <c>OnMouseUp</c> alone since mouse-wheel scrolling (and most of these button clicks) have
    /// no "mouse up" to hook that fires after the dispatch loop completes. NOT used by the
    /// text-size-slider recompose inside this dialog's own <see cref="OnMouseUp"/> override --
    /// that runs AFTER <c>base.OnMouseUp(args)</c> has already returned, so the composer-level
    /// dispatch loop has already finished and a direct, synchronous recompose there is provably
    /// safe.</summary>
    private System.Action? pendingRecomposeAction;

    private void RequestRecompose()
    {
        pendingRecomposeAction = RecomposeCurrentView;
    }

    /// <summary>Recomposes whichever view is currently live, preserving typing focus in the
    /// editor view. The single place that knows how to rebuild "the current view" -- shared by
    /// <see cref="RequestRecompose"/>'s deferred path and <see cref="OnMouseUp"/>'s
    /// scroll-drag-release path.</summary>
    private void RecomposeCurrentView()
    {
        if (IsEditorMode) RecomposeEditorViewPreservingFocus();
        else ComposeReadView();
    }

    private void OnRowListScroll(float value)
    {
        if (rowListContentBounds is null || isComposingRowList) return;

        rowListScrollValue = value;

        // Both views (row-list-rework S1 read view, S2 editor view): rows are custom
        // ScribeRowElements drawn in the interactive pass, which the BeginClip scissor clips
        // natively. Scrolling is therefore the plain vanilla idiom -- shift the content parent's
        // fixedY and recalc; every row's renderY (and its hit-test absY) follows in one call, with
        // no cull, no recompose, and no drag-handoff needed. This is the whole payoff of the
        // rework: smooth sub-pixel scrolling for free, identical in both views (task 3.4). The
        // one floating edit input is a child of the same content parent, so it scrolls in lockstep
        // with the row it sits on.
        rowListContentBounds.fixedY = 0 - value;
        rowListContentBounds.CalcWorldBounds();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (pendingRecomposeAction is { } action)
        {
            pendingRecomposeAction = null;
            action();
        }

        base.OnRenderGUI(deltaTime);
    }

    /// <summary>
    /// The base <c>GuiDialogBlockEntity.OnFinalizeFrame</c> auto-closes the dialog (and, via our
    /// <see cref="OnGuiClosed"/>, flushes the pending edit and releases the lock -- task 7.8) once
    /// the player leaves <c>IsInRangeOfBlock</c>. But the base measures against the player's
    /// <c>WorldData.PickingRange</c>, which the engine inflates to ~100 blocks in Creative mode
    /// (the <c>EnumGameMode</c> switch sets <c>PickingRange = PreviousPickingRange</c>, default
    /// 100). A creative player -- e.g. anyone who just placed the lectern from the creative
    /// inventory -- could therefore walk hundreds of blocks and the dialog would never close.
    /// Gate on the fixed survival interaction distance instead so walk-away auto-close fires in
    /// every game mode. This mirrors the base's exact selection-box distance math (confirmed via
    /// decompile), swapping only the mode-dependent range for <c>DefaultPickingRange</c>.
    /// </summary>
    public override bool IsInRangeOfBlock(BlockPos blockEntityPos)
    {
        Cuboidf[]? selectionBoxes = capi.World.BlockAccessor.GetBlock(blockEntityPos)
            .GetSelectionBoxes(capi.World.BlockAccessor, blockEntityPos);
        Vec3d eyePos = capi.World.Player.Entity.Pos.XYZ.Add(capi.World.Player.Entity.LocalEyePos);

        double nearest = 99.0;
        if (selectionBoxes != null)
        {
            foreach (Cuboidf box in selectionBoxes)
            {
                nearest = System.Math.Min(nearest, box.ToDouble()
                    .Translate(blockEntityPos.X, blockEntityPos.InternalY, blockEntityPos.Z)
                    .ShortestDistanceFrom(eyePos));
            }
        }

        return nearest <= GlobalConstants.DefaultPickingRange + 0.5;
    }

    /// <summary>Set while a text-size drag is pending a recompose (see <see cref="OnMouseUp"/>).
    /// Recomposing rebuilds the slider element from scratch, which would discard its own
    /// in-progress-drag state (<c>GuiElementSlider</c> has no public way to defer its own change
    /// callback to mouse-up, so this dialog does it instead) -- without deferring, every
    /// intermediate value during a single drag tears down and rebuilds a fresh slider that never
    /// saw the mouse-down, ending the drag after one step.</summary>
    private bool textSizePendingRecompose;

    private bool OnTextSizeSliderChanged(int percent)
    {
        clientConfig.TextSizeScale = percent / 100f;
        textSizePendingRecompose = true;
        return true;
    }

    /// <summary>
    /// Called by <see cref="BlockEntityScribeLectern.HandleServerReply"/> when a mode-switch
    /// request is granted for an already-open dialog. Also used internally by the constructor
    /// for the initial mode.
    /// </summary>
    public void SwitchMode(bool editorMode, byte[]? documentBytes)
    {
        EnterMode(editorMode, documentBytes);
    }

    private void EnterMode(bool editorMode, byte[]? documentBytes)
    {
        StopAutosaveTick();
        IsEditorMode = editorMode;
        focusedEditIndex = null;
        rowListScrollValue = 0;
        pendingRecomposeAction = null;

        if (editorMode)
        {
            scratchDocument = ScribeDocumentCodec.TryDeserialize(documentBytes, out var doc) && doc is not null
                ? doc
                : new ScribeDocument();
            isDirty = false;
            StartAutosaveTick();
            ComposeEditorView();
        }
        else
        {
            scratchDocument = null;
            ComposeReadView();
        }
    }

    /// <summary>Called by <see cref="BlockEntityScribeLectern.FromTreeAttributes"/> whenever the authoritative document changes; a no-op while in editor mode.</summary>
    public void RefreshReadView()
    {
        if (!IsEditorMode && IsOpened())
        {
            ComposeReadView();
        }
    }

    private ElementBounds DialogBounds() =>
        ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

    /// <summary>Placeholder skeuomorphic backdrop, replacing the generic
    /// <c>AddShadedDialogBG</c> panel (design.md decision 2/3). A single texture path is the
    /// entire swap point -- replacing this asset with real per-tier art (or repointing it to
    /// a different <see cref="AssetLocation"/> per tier later) needs no change to this file's
    /// layout/composition logic, satisfying `specs/lectern-gui-shell/spec.md`'s "Backdrop is
    /// swappable" scenario. Uses the engine's own <c>AddImageBG</c> (a tiled/scaled Cairo
    /// `SurfacePattern` fill with rounded corners, confirmed via
    /// `GuiElementImageBackground.ComposeElements`) rather than a hand-rolled Cairo draw --
    /// same swappability, less code.</summary>
    private static readonly AssetLocation BackdropTexture = new("scribe:textures/gui/lecternbackdrop.png");


    // ---------------- Read view ----------------

    private void ComposeReadView()
    {
        var blocks = lectern.Document.Blocks;
        double rowSpacing = ScaledRowSpacing;
        double listWidth = clientConfig.RowListWidth;
        double y = clientConfig.TopContentGap;

        var dialogBounds = DialogBounds();
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = Vintagestory.API.Client.ElementSizing.FitToChildren;

        // Pass 1: measure every row's position/height. Each row draws as a ScribeRowElement in the
        // interactive pass, which the dialog's BeginClip scissor clips NATIVELY (row-list-rework)
        // -- so there is no viewport culling and no viewport-relative Y here: every row is composed
        // once at its absolute content Y, and scrolling is a plain parent-fixedY shift (see
        // OnRowListScroll and SetupRowListScrollbar). Row height = the wrapped text height plus
        // the ruling overhead the row draws around it. Both views now share this idiom (task 3.4).
        var rowYs = new double[blocks.Count];
        var rowHeights = new double[blocks.Count];
        double contentY = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            // ScribeRowElement.RowHeightFixed is the single source of row height, shared with the
            // element's own drawing so the baked surface is always tall enough for the text (it
            // handles the scaled-vs-fixed unit conversion that a naive measure got wrong -- see
            // its doc comment). Read view reserves no affordance gutters, so text fills the full width.
            rowYs[i] = contentY;
            rowHeights[i] = ScribeRowElement.RowHeightFixed(capi, blocks[i], listWidth, RowFont(), clientConfig, reserveAffordances: false);
            contentY += rowHeights[i] + rowSpacing;
        }

        double hintHeight = 0;
        if (blocks.Count == 0)
        {
            // Full-width hint (no reserved columns), floored at ControlRowHeight. Uses the single
            // shared wrapped-text-height primitive so there is one correct measure in the codebase.
            hintHeight = System.Math.Max(
                clientConfig.ControlRowHeight,
                ScribeRowElement.MeasureWrappedTextHeightFixed(capi, Lang.Get("scribe:scribe-gui-edit-hint"), RowFont(), listWidth));
            contentY = hintHeight + rowSpacing;
        }

        // Clamp the restored scroll position against the real content height, so a document that
        // shrank while scrolled down (e.g. a task synced away) doesn't stay scrolled past the end.
        rowListScrollValue = System.Math.Clamp(rowListScrollValue, 0, System.Math.Max(0, contentY - clientConfig.VisibleListHeight));

        // The row list lives inside a fixed-height clipped region so a long document scrolls
        // instead of growing the dialog -- see VSAPI-NOTES.md for the BeginClip/AddVerticalScrollbar
        // idiom. contentBounds is the single parent every row is added under; the read-view branch
        // of OnRowListScroll shifts its fixedY on scroll and the BeginClip scissor clips the
        // interactive-pass row textures natively (no cull, no recompose-on-scroll).
        var clipBounds = ElementBounds.Fixed(0, y, listWidth, clientConfig.VisibleListHeight);
        var scrollbarBounds = ElementStdBounds.VerticalScrollbar(clipBounds);
        var contentBounds = ElementBounds.Fixed(0, 0, listWidth, contentY);

        SingleComposer = capi.Gui.CreateCompo("scribeLectern", dialogBounds)
            .AddImageBG(bgBounds, BackdropTexture)
            .AddDialogTitleBar(Lang.Get("scribe:scribe-gui-title"), OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .BeginClip(clipBounds)
                .BeginChildElements(contentBounds);

        // Compose every row at its absolute content Y as a custom ScribeRowElement. The element
        // bakes its own texture (checkbox glyph + text + lined-paper ruling) and blits it in the
        // interactive pass, where the clip scissor applies -- so a row at the scroll boundary is
        // drawn partially clipped rather than popping in/out, and rows past the edge are hidden by
        // the scissor, not by never composing them.
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var rowBounds = ElementBounds.Fixed(0, rowYs[i], listWidth, rowHeights[i]);

            SingleComposer.AddInteractiveElement(
                new ScribeRowElement(
                    capi,
                    rowBounds,
                    ScribeRowMode.Read,
                    blockIndex: i,
                    isTask: block.IsTask,
                    done: block.Done,
                    text: block.Text,
                    font: RowFont(),
                    config: clientConfig,
                    onToggleClicked: block.IsTask ? OnReadViewToggleTask : null),
                ScribeBlockRowCell.TextKey(i));
        }

        if (blocks.Count == 0)
        {
            SingleComposer.AddStaticText(Lang.Get("scribe:scribe-gui-edit-hint"), RowFont(), ElementBounds.Fixed(0, 0, listWidth, hintHeight));
        }

        rowListContentBounds = contentBounds;

        SingleComposer
                .EndChildElements()
            .EndClip()
            .AddInteractiveElement(new ScribeRowListScrollbar(capi, OnRowListScroll, scrollbarBounds), "rowListScrollbar");

        y += clientConfig.VisibleListHeight + rowSpacing;

        var switchBounds = ElementBounds.Fixed(0, y, listWidth, clientConfig.ControlRowHeight);
        SingleComposer.AddSmallButton(Lang.Get("scribe:scribe-gui-switch-to-editor"), OnClickSwitchToEditor, switchBounds, key: "switchModeButton");

        SingleComposer.EndChildElements().Compose();

        SetupRowListScrollbar(contentY);

        // Native clipping means scrolling is a parent-fixedY shift; apply the restored scroll
        // position now (SetupRowListScrollbar set the thumb but fires no scroll callback), so a
        // recompose triggered by a live document resync keeps its scroll offset.
        rowListContentBounds.fixedY = 0 - rowListScrollValue;
        rowListContentBounds.CalcWorldBounds();
    }

    /// <summary>Read-view task checkbox click: fire-and-forget a lock-free toggle to the server.
    /// The read view holds no editor lock (see BlockEntityScribeLectern), so this uses the
    /// dedicated <see cref="ScribeToggleTaskMessage"/> rather than the lock-gated edit path. No
    /// optimistic local mutation -- the server applies it and re-syncs, and FromTreeAttributes ->
    /// RefreshReadView recomposes with the new state.</summary>
    private void OnReadViewToggleTask(int index)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeToggleTaskMessage
        {
            PosX = lectern.Pos.X,
            PosY = lectern.Pos.Y,
            PosZ = lectern.Pos.Z,
            BlockIndex = index,
        });
    }

    /// <summary>Post-Compose scrollbar wiring shared by both views. Sets the mouse-wheel step to
    /// one task-row height (so a notch scrolls one row, not the engine default ~2 -- playtest
    /// feedback 2026-07-20), tells the scrollbar its visible/total heights, and restores the real
    /// scroll position.</summary>
    private void SetupRowListScrollbar(double contentY)
    {
        // SetHeights synchronously fires OnRowListScroll(0) via the scrollbar's own
        // change-notify plumbing (GuiElementScrollbar.SetNewTotalHeight -> TriggerChanged),
        // regardless of the real scroll position -- isComposingRowList suppresses that spurious
        // call so it can't snap the list back to the top. The real scroll position is reapplied
        // right after via CurrentYPosition's public setter (no callback re-entrancy) so the thumb
        // sits at the right spot.
        //
        // This method only positions the THUMB; the content parent's fixedY shift (which actually
        // slides the rows to the scrolled position, native-clip path) is applied by the caller
        // right after -- both views compose their ScribeRowElements at absolute Y and scroll by
        // that parent shift (row-list-rework, task 3.4), so this shared helper stays view-agnostic.
        isComposingRowList = true;
        var scrollbar = (ScribeRowListScrollbar)SingleComposer.GetScrollbar("rowListScrollbar");
        scrollbar.RowStep = clientConfig.TaskRowHeight * clientConfig.TextSizeScale;
        scrollbar.SetHeights((float)clientConfig.VisibleListHeight, (float)System.Math.Max(clientConfig.VisibleListHeight, contentY));
        scrollbar.CurrentYPosition = (float)rowListScrollValue;
        isComposingRowList = false;
    }

    private bool OnClickSwitchToEditor()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeRequestAccessMessage
        {
            PosX = lectern.Pos.X,
            PosY = lectern.Pos.Y,
            PosZ = lectern.Pos.Z,
            WantEditor = true,
        });
        return true;
    }

    // ---------------- Editor view ----------------

    private void ComposeEditorView()
    {
        if (scratchDocument is null) return;

        double rowSpacing = ScaledRowSpacing;
        double listWidth = clientConfig.RowListWidth;
        double y = clientConfig.TopContentGap;

        var dialogBounds = DialogBounds();
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = Vintagestory.API.Client.ElementSizing.FitToChildren;

        var blocks = scratchDocument.Blocks;

        // Keep the focused-row index in range if the document shrank (e.g. an Add-then-something
        // that removed a row) -- a stale index would suppress the wrong row's label or place the
        // input off the list. Null it out entirely if there are no rows.
        if (focusedEditIndex is { } fi && (fi < 0 || fi >= blocks.Count))
        {
            focusedEditIndex = blocks.Count == 0 ? null : System.Math.Clamp(fi, 0, blocks.Count - 1);
        }

        // Pass 1: measure every row's position/height at its ABSOLUTE content Y. Editor rows are
        // now the same custom-drawn ScribeRowElement as the read view (row-list-rework S2), drawn
        // in the interactive pass and clipped natively by BeginClip -- so, exactly like
        // ComposeReadView, there is no viewport culling and no viewport-relative Y: every row is
        // composed once at its absolute Y and scrolling is a plain parent-fixedY shift (task 3.4).
        // Height comes from the shared ScribeRowElement.RowHeightFixed so a row measures identically
        // in both views (task 2.3).
        var rowYs = new double[blocks.Count];
        var rowHeights = new double[blocks.Count];
        double contentY = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            rowYs[i] = contentY;
            // Editor view reserves the pin/delete/grip gutters, so its text column is narrower and a
            // row measures taller than the same block does in the read view.
            rowHeights[i] = ScribeRowElement.RowHeightFixed(capi, blocks[i], listWidth, RowFont(), clientConfig, reserveAffordances: true);
            contentY += rowHeights[i] + rowSpacing;
        }

        // A focus-moving action (Add Task, Enter/Shift+Tab) may have put the focused row outside the
        // visible window -- scroll so it's fully in view before clamping (task 6.11). One-shot: an
        // ordinary click-to-focus or a live resync leaves the flag unset, so the user's own scroll
        // is never overridden.
        if (scrollFocusedRowIntoView && focusedEditIndex is { } focusIdx && focusIdx < blocks.Count)
        {
            double rowTop = rowYs[focusIdx];
            double rowBottom = rowTop + rowHeights[focusIdx];
            if (rowTop < rowListScrollValue)
            {
                rowListScrollValue = rowTop; // row above the window: scroll up to its top
            }
            else if (rowBottom > rowListScrollValue + clientConfig.VisibleListHeight)
            {
                // row below the window: scroll down so its bottom sits at the window bottom
                rowListScrollValue = rowBottom - clientConfig.VisibleListHeight;
            }
        }
        scrollFocusedRowIntoView = false;

        // Clamp the (possibly adjusted) scroll position against the real content height (same as
        // ComposeReadView).
        rowListScrollValue = System.Math.Clamp(rowListScrollValue, 0, System.Math.Max(0, contentY - clientConfig.VisibleListHeight));

        var clipBounds = ElementBounds.Fixed(0, y, listWidth, clientConfig.VisibleListHeight);
        var scrollbarBounds = ElementStdBounds.VerticalScrollbar(clipBounds);
        var contentBounds = ElementBounds.Fixed(0, 0, listWidth, contentY);

        SingleComposer = capi.Gui.CreateCompo("scribeLectern", dialogBounds)
            .AddImageBG(bgBounds, BackdropTexture)
            .AddDialogTitleBar(Lang.Get("scribe:scribe-gui-title"), OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .BeginClip(clipBounds)
                .BeginChildElements(contentBounds);

        // Compose every row at its absolute content Y as a ScribeRowElement in Edit mode. Each row
        // draws its checkbox (task) + text label + ruling. The focused row suppresses its own text
        // draw (the single floating input paints the text for it instead -- design.md Decision 1).
        editInput = null;
        editInputIndex = null;
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            bool isFocusedRow = focusedEditIndex == i;
            var rowBounds = ElementBounds.Fixed(0, rowYs[i], listWidth, rowHeights[i]);
            // Shared horizontal layout for this row -- the row element, the floating input, and the
            // pin/delete/grip affordance columns below all read from this one metric so they stay
            // aligned. reserveAffordances: true (editor view reserves the right-side gutters).
            var layout = RowTextLayout.For(listWidth, block.IsTask, RowFont(), clientConfig, reserveAffordances: true);

            SingleComposer.AddInteractiveElement(
                new ScribeRowElement(
                    capi,
                    rowBounds,
                    ScribeRowMode.Edit,
                    blockIndex: i,
                    isTask: block.IsTask,
                    done: block.Done,
                    text: block.Text,
                    font: RowFont(),
                    config: clientConfig,
                    onToggleClicked: block.IsTask ? OnEditViewToggleTask : null,
                    onRequestEdit: OnRequestEditRow,
                    suppressText: isFocusedRow),
                ScribeBlockRowCell.TextKey(i));

            // No separate divider element: each ScribeRowElement bakes its own lined-paper ruling
            // (ScribeRowElement.DrawRuling) into its clipped interactive-pass texture. The old
            // AddInset dividers were both redundant with that ruling AND unclippable (AddInset is a
            // static-pass element -> it drew unclipped and bled into the controls below the list;
            // playtest 2026-07-21T20-58-36 / VSAPI-NOTES "BeginClip doesn't clip static elements").

            // The single floating input goes onto the focused row, aligned to where that row's
            // static label would draw via the SAME RowTextLayout metric the element uses -- so the
            // handoff between label and input has no baseline/x/font jump (design.md Decision 1/5,
            // task 3.2). It is a child of contentBounds like the rows, so it scrolls with them.
            if (isFocusedRow)
            {
                // Input occupies the row's text column (x/width from the shared RowTextLayout, so
                // it aligns horizontally with where the static label draws -- design.md Decision 5)
                // at the row's full height. The base single-line input vertically centers its text
                // within these bounds, which closely tracks the label's top-padded single line for
                // a task-height row; exact baseline is a flagged playtest item (design.md risk).
                // Uses the shared `layout` above, so the input's TextX/TextWidth match the narrowed
                // static label (the row reserves the pin/delete/grip gutters) -- keeps the
                // label<->input handoff jump-free (design.md Decision 5).
                var inputBounds = ElementBounds.Fixed(layout.TextX, rowYs[i], layout.TextWidth, rowHeights[i]);
                editInput = new ScribeRowTextInput(
                    capi, inputBounds, OnEditInputTextChanged, RowFont(),
                    onCommitAndAdvance: OnEditCommitAndAdvance,
                    onCommitAndRetreat: OnEditCommitAndRetreat,
                    onBlur: OnEditBlur);
                SingleComposer.AddInteractiveElement(editInput, "rowEditInput");
                editInputIndex = i;
            }

            // Per-row affordance icons (restore-row-affordance-columns): hover-conditional pin
            // (task rows only) + delete, plus a drag-handle grip. Added AFTER the row element (and
            // the floating input) so they render on top and win the mouse-down over the full-width
            // row, which yields gutter clicks to them (ScribeRowElement.IsInIconGutter). Each is a
            // child of contentBounds like the rows, so it scrolls + clips natively. HoverRegion is
            // the SAME rowBounds instance the row uses, so it appears whenever the mouse is anywhere
            // over the row and tracks the scroll for free. Uses the registered custom SVG codes.
            if (block.IsTask)
            {
                var pinBounds = ElementBounds.Fixed(layout.PinX, rowYs[i], layout.PinWidth, rowHeights[i]);
                // toggleable: true is mandatory for a stateful icon -- the base GuiElementToggleButton
                // resets On=false on ANY dialog mouse-up when not toggleable, wiping the seeded
                // pinned state (see ScribeHoverIconButton's ctor doc). On is seeded post-Compose below.
                var pinButton = new ScribeHoverIconButton(capi, "scribepin", _ => OnEditViewTogglePin(i), pinBounds, toggleable: true)
                {
                    HoverRegion = rowBounds,
                };
                SingleComposer.AddInteractiveElement(pinButton, ScribeBlockRowCell.PinKey(i));
                SingleComposer.AddHoverText(Lang.Get("scribe:scribe-gui-pin"), CairoFont.WhiteSmallText(), (int)clientConfig.HoverTextWidth, pinBounds.FlatCopy());
            }

            var deleteBounds = ElementBounds.Fixed(layout.DeleteX, rowYs[i], layout.DeleteWidth, rowHeights[i]);
            var deleteButton = new ScribeHoverIconButton(capi, "scribeclose", _ => OnEditViewDeleteRow(i), deleteBounds)
            {
                HoverRegion = rowBounds,
            };
            SingleComposer.AddInteractiveElement(deleteButton, ScribeBlockRowCell.DeleteKey(i));
            SingleComposer.AddHoverText(Lang.Get("scribe:scribe-gui-delete"), CairoFont.WhiteSmallText(), (int)clientConfig.HoverTextWidth, deleteBounds.FlatCopy());

            // Grip: renders the scribegrip SVG and hover-hides for free via ScribeHoverIconButton.
            // No-op callback -- the actual drag-to-reorder interaction (and its lift-ghost/insertion
            // feedback) is owned by the parked lectern-drag-reorder-feedback change, which will adopt
            // ScribeDragHandleElement's drag plumbing on top of this restored column.
            var gripBounds = ElementBounds.Fixed(layout.DragHandleX, rowYs[i], layout.DragHandleWidth, rowHeights[i]);
            var gripButton = new ScribeHoverIconButton(capi, "scribegrip", _ => { }, gripBounds)
            {
                HoverRegion = rowBounds,
            };
            SingleComposer.AddInteractiveElement(gripButton, ScribeBlockRowCell.DragHandleKey(i));
        }

        rowListContentBounds = contentBounds;

        SingleComposer
                .EndChildElements()
            .EndClip()
            .AddInteractiveElement(new ScribeRowListScrollbar(capi, OnRowListScroll, scrollbarBounds), "rowListScrollbar");

        y += clientConfig.VisibleListHeight + clientConfig.ListToControlsGap;
        SingleComposer.AddStaticText(Lang.Get("scribe:scribe-gui-textsize"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, y, clientConfig.TextSizeLabelWidth, clientConfig.ControlRowHeight));
        double sliderX = clientConfig.TextSizeLabelWidth + clientConfig.TextSizeLabelToSliderGap;
        SingleComposer.AddSlider(OnTextSizeSliderChanged, ElementBounds.Fixed(sliderX, y, listWidth - sliderX, clientConfig.ControlRowHeight), key: "textSizeSlider");
        SingleComposer.GetSlider("textSizeSlider").SetValues(TextSizePercent, clientConfig.MinTextSizePercent, clientConfig.MaxTextSizePercent, 10, "%");
        y += clientConfig.ControlRowGap;

        var toolPanelToggleBounds = ElementBounds.Fixed(0, y, clientConfig.ToolPanelToggleWidth, clientConfig.ControlRowHeight);
        string collapseLangKey = isToolPanelExpanded ? "scribe:scribe-gui-collapse" : "scribe:scribe-gui-expand";
        SingleComposer.AddSmallButton(Lang.Get(collapseLangKey), OnClickToggleToolPanel, toolPanelToggleBounds, key: "toolPanelToggleButton");
        y += clientConfig.ControlRowGap;

        if (isToolPanelExpanded)
        {
            double optionX = 0;
            foreach (var option in ToolbarOptions())
            {
                var optionBounds = ElementBounds.Fixed(optionX, y, clientConfig.ToolbarIconWidth, clientConfig.ToolbarIconHeight);
                SingleComposer.AddIf(option.IsVisible());
                SingleComposer.AddIconButton(option.Icon, _ => option.OnActivate(), optionBounds, key: option.Key);
                SingleComposer.AddHoverText(Lang.Get(option.LangKey), CairoFont.WhiteSmallText(), (int)clientConfig.HoverTextWidth, optionBounds.FlatCopy());
                SingleComposer.EndIf();
                optionX += clientConfig.ToolbarIconSpacing;
            }

            y += clientConfig.ControlRowGap;
        }

        var switchBounds = ElementBounds.Fixed(0, y, clientConfig.SwitchButtonWidth, clientConfig.ControlRowHeight);
        SingleComposer.AddSmallButton(Lang.Get("scribe:scribe-gui-switch-to-read"), OnClickSwitchToRead, switchBounds, key: "switchModeButton");

        SingleComposer.EndChildElements().Compose();

        // Seed each task row's pin toggle to its persisted state -- only AFTER Compose() (the toggle
        // button is toggleable, so its On isn't reset on mouse-up, but it still starts at the ctor
        // default until seeded). Delete/grip are momentary, nothing to seed.
        for (int i = 0; i < blocks.Count; i++)
        {
            if (!blocks[i].IsTask) continue;
            SingleComposer.GetToggleButton(ScribeBlockRowCell.PinKey(i)).On = blocks[i].Pinned;
        }

        SetupRowListScrollbar(contentY);

        // Native clipping means scrolling is a parent-fixedY shift; apply the restored scroll
        // position now (SetupRowListScrollbar set the thumb but fires no scroll callback), so a
        // recompose keeps its scroll offset. Same as ComposeReadView's tail.
        rowListContentBounds.fixedY = 0 - rowListScrollValue;
        rowListContentBounds.CalcWorldBounds();

        // Seed the single floating input's text and focus it -- only AFTER Compose() has real
        // bounds (VSAPI-NOTES: SetValue before Compose corrupts the text-wrap/auto-height math).
        // suppressBlurCommit guards the FocusElement below: focusing the input can steal focus
        // from a prior element and, on the next recompose, the teardown blur must not double-commit.
        if (focusedEditIndex is { } idx && editInput is not null && idx >= 0 && idx < blocks.Count)
        {
            suppressBlurCommit = true;
            // Re-baseline the multi-line height-change gate to the row we just seeded BEFORE
            // SetValue: SetValue -> OnTextChanged -> OnEditInputTextChanged compares against this
            // baseline, so setting it first means seeding the input at its own current height fires
            // no spurious relist (and a focus move to a shorter/taller row doesn't fire on the stale
            // previous row's height). The first real wrap/newline that changes THIS row's height
            // then triggers the relist.
            editRowMeasuredHeight = rowHeights[idx];
            editInput.SetValue(blocks[idx].Text);
            // FocusElement (not OnFocusGained directly) so Compose()'s default
            // focusFirstElement:true focus is properly transferred rather than leaving two
            // elements marked HasFocus (VSAPI-NOTES recompose-focus pattern).
            SingleComposer.FocusElement(editInput.TabIndex);
            suppressBlurCommit = false;
        }
    }

    private System.Collections.Generic.IEnumerable<ToolbarOption> ToolbarOptions()
    {
        yield return new ToolbarOption("addTaskButton", "plus", "scribe:scribe-gui-addtask", () => true, OnClickAddTask);
    }

    private bool OnClickToggleToolPanel()
    {
        isToolPanelExpanded = !isToolPanelExpanded;
        RequestRecompose();
        return true;
    }

    /// <summary>Editor-view task checkbox click: toggle done on the scratch document (lock-gated
    /// edit path -- the editor holds the lock) and mark dirty so the autosave/commit picks it up.
    /// A recompose re-bakes the row with the new check state.</summary>
    private void OnEditViewToggleTask(int index)
    {
        scratchDocument?.ToggleTask(index);
        isDirty = true;
        RequestRecompose();
    }

    /// <summary>STUB (restore-row-affordance-columns): a row's delete affordance was clicked. This
    /// change restores the icon + hit-testing only; the actual server-authoritative delete is a
    /// later change. For now just log so the affordance is verifiably wired.</summary>
    private void OnEditViewDeleteRow(int index)
    {
        // TODO(follow-up): scratchDocument?.DeleteBlock(index); isDirty = true; RequestRecompose();
        // ScribeDocument.DeleteBlock already exists; the deferred-recompose machinery
        // (pendingRecomposeAction) handles the mid-dispatch reentrancy the real handler needs.
        capi.Logger.VerboseDebug("[scribe] delete affordance clicked (stub): row {0}", index);
    }

    /// <summary>STUB (restore-row-affordance-columns): a task row's pin affordance was clicked. The
    /// button is toggleable so it flips visually, but block.Pinned is untouched -- the next recompose
    /// re-seeds On from block.Pinned. Real pin persistence is a later change.</summary>
    private void OnEditViewTogglePin(int index)
    {
        // TODO(follow-up): scratchDocument?.TogglePinned(index); isDirty = true; RequestRecompose();
        // ScribeDocument.TogglePinned already exists.
        capi.Logger.VerboseDebug("[scribe] pin affordance clicked (stub): row {0}", index);
    }

    /// <summary>A row's text column was clicked: float the single live input onto it, then recompose
    /// so the input moves and the newly focused row suppresses its label (task 3.2/3.3). A click on
    /// the ALREADY-focused row early-returns here -- that row's mouse-down already fell through to
    /// the overlapping input (see <see cref="ScribeRowElement.OnMouseDownOnElement"/>), which kept
    /// focus and placed the caret, so there is nothing to do.</summary>
    private void OnRequestEditRow(int index)
    {
        if (focusedEditIndex == index) return;

        // Push any pending edit from the row we're leaving before moving focus (task 4.3-style
        // commit-on-leave). Normalize (trailing-trim) that row first, then flush. FlushIfDirty is a
        // no-op if nothing changed.
        if (focusedEditIndex is { } leavingIdx) NormalizeRowOnCommit(leavingIdx);
        FlushIfDirty();

        focusedEditIndex = index;

        // Deferred recompose: this fires from inside the row element's OnMouseUpOnElement, which
        // runs during GuiComposer's mouse dispatch loop -- same mid-dispatch hazard as every other
        // recompose site here (see pendingRecomposeAction's doc comment).
        RequestRecompose();
    }

    /// <summary>Live text-change callback for the single floating input: write straight through to
    /// the focused row's block and mark dirty (the autosave tick / commit path serializes it).</summary>
    private void OnEditInputTextChanged(string text)
    {
        if (focusedEditIndex is not { } index || scratchDocument is null) return;
        // Live edit: store the text exactly as typed so a trailing newline from a just-pressed
        // Shift+Enter survives long enough for the row to grow (task 4.6). SetBlockText stores task
        // text verbatim; trailing whitespace/newlines are trimmed on COMMIT instead
        // (NormalizeRowOnCommit), which is the correct place per the 4.7 trailing-trim design.
        scratchDocument.SetBlockText(index, text);
        isDirty = true;

        // Multi-line grow/shrink (lectern-multiline-edit-input): as typing wraps onto a new line or
        // a Shift+Enter adds a hard break (or deleting removes one), the focused row's MEASURED
        // height changes. The input element auto-heights itself (GuiElementTextArea.TextChanged),
        // but the row list's rowHeights/rowYs/content-height were computed once at compose time --
        // so recompose the list to re-measure this row, shift the rows below, and update the
        // scrollbar. Gate on an ACTUAL height change so typing within a line doesn't thrash the
        // list every keystroke. Measure via the SAME ScribeRowElement.RowHeightFixed the compose
        // path uses (single source of truth, so label + input agree). Scroll the growing row into
        // view (reusing the one-shot) and preserve the caret across the recompose.
        if (index >= 0 && index < scratchDocument.Blocks.Count)
        {
            double newHeight = ScribeRowElement.RowHeightFixed(
                capi, scratchDocument.Blocks[index], clientConfig.RowListWidth, RowFont(), clientConfig, reserveAffordances: true);
            if (editRowMeasuredHeight is not { } prev || System.Math.Abs(prev - newHeight) > 0.5)
            {
                editRowMeasuredHeight = newHeight;
                scrollFocusedRowIntoView = true;
                RequestRecompose();
            }
        }
    }

    /// <summary>The last measured height of the focused edit row, tracked so
    /// <see cref="OnEditInputTextChanged"/> only recomposes the row list when a wrap/newline
    /// actually changes the row's height (not on every keystroke). Reset whenever focus moves to a
    /// different row (in <see cref="ComposeEditorView"/>).</summary>
    private double? editRowMeasuredHeight;

    /// <summary>Enter: commit the focused row and advance to the next (task 4.1).</summary>
    private bool OnEditCommitAndAdvance()
    {
        if (focusedEditIndex is not { } index || scratchDocument is null) return false;

        NormalizeRowOnCommit(index);
        FlushIfDirty();
        int count = scratchDocument.Blocks.Count;
        int next = System.Math.Min(index + 1, count - 1);
        MoveEditFocusTo(next);
        return true;
    }

    /// <summary>Shift+Tab: commit the focused row and retreat to the previous (task 4.2).</summary>
    private bool OnEditCommitAndRetreat()
    {
        if (focusedEditIndex is not { } index || scratchDocument is null) return false;

        NormalizeRowOnCommit(index);
        FlushIfDirty();
        int prev = System.Math.Max(index - 1, 0);
        MoveEditFocusTo(prev);
        return true;
    }

    /// <summary>Blur (genuine click-away, not a recompose-driven focus loss): commit the row's edit
    /// (task 4.3). Skipped during a programmatic recompose focus transfer, where the caller already
    /// flushed.</summary>
    private void OnEditBlur()
    {
        if (suppressBlurCommit) return;
        if (focusedEditIndex is { } index) NormalizeRowOnCommit(index);
        FlushIfDirty();
    }

    /// <summary>
    /// Commit-time text normalization for a row (lectern-multiline-edit-input): strip trailing blank
    /// lines and trailing whitespace while PRESERVING interior newlines, e.g. "a\n\nb\n" -> "a\n\nb".
    /// Prevents a stray trailing Shift+Enter from committing a row that looks empty but stays tall,
    /// while keeping intentional interior spacing. Applied ONLY at genuine row-commit sites (Enter,
    /// Shift+Tab, blur, switch-view, close) -- NOT in FlushIfDirty (the 1s autosave tick calls that,
    /// and trimming mid-typing would fight a player who just pressed Shift+Enter to start a new
    /// line). No leading trim (a player may indent intentionally). Marks dirty only if it changed
    /// something.
    /// </summary>
    private void NormalizeRowOnCommit(int index)
    {
        if (scratchDocument is null || index < 0 || index >= scratchDocument.Blocks.Count) return;
        string current = scratchDocument.Blocks[index].Text;
        string trimmed = current.TrimEnd();
        if (trimmed != current)
        {
            // This is the sole normalization site: SetBlockText stores task text verbatim (the model
            // no longer trims), so the TrimEnd() above -- interior and leading whitespace kept
            // intentionally -- is exactly what lands.
            scratchDocument.SetBlockText(index, trimmed);
            isDirty = true;
        }
    }

    /// <summary>Moves the in-place edit focus to <paramref name="index"/> and recomposes so the
    /// single input repositions onto that row. Requests a scroll-into-view since keyboard navigation
    /// (Enter/Shift+Tab) can move focus to a row outside the visible window (task 6.11).</summary>
    private void MoveEditFocusTo(int index)
    {
        focusedEditIndex = index;
        scrollFocusedRowIntoView = true;
        RequestRecompose();
    }

    /// <summary>
    /// Recomposes the editor view without disturbing the in-place edit -- the focused row and its
    /// caret position are captured before the rebuild and restored after (a recompose creates a
    /// brand-new element tree, so the old input reference is gone -- VSAPI-NOTES recompose-focus
    /// pattern). Without this, a plain recompose's <c>focusFirstElement: true</c> would yank focus
    /// off the edited row.
    /// </summary>
    private void RecomposeEditorViewPreservingFocus()
    {
        int caretPos = 0;
        int? caretRow = null;
        if (editInput is { HasFocus: true } && editInputIndex is { } prevIndex)
        {
            // CaretPosWithoutLineBreaks is the caret's absolute offset in the logical text,
            // independent of how it wraps into display lines -- the right measure now the input is
            // multi-line (CaretPosInLine alone would drop the caret onto wrapped line 0).
            caretPos = editInput.CaretPosWithoutLineBreaks;
            caretRow = prevIndex;
        }

        // The recompose itself tears down the old input; its blur must not commit (the value is
        // already live on the scratch doc via OnEditInputTextChanged).
        suppressBlurCommit = true;
        ComposeEditorView();
        suppressBlurCommit = false;

        // ComposeEditorView already re-seeded + focused the input for focusedEditIndex (caret at
        // end). Only restore the old caret when the recompose stayed on the SAME row -- a
        // recompose that moved focus to a different row (Enter/Shift+Tab/click) should keep the
        // new row's caret-at-end, not stamp the old position onto it.
        if (caretRow is { } row && row == focusedEditIndex && editInput is not null)
        {
            // Restore by absolute offset; the setter re-derives the (line, col) from the new wrap.
            editInput.CaretPosWithoutLineBreaks = System.Math.Min(caretPos, editInput.GetText().Length);
        }
    }

    private bool OnClickAddTask()
    {
        if (scratchDocument is null) return true;
        scratchDocument.AddTask(Lang.Get("scribe:scribe-gui-newtask-placeholder"));
        isDirty = true;
        // Focus the newly added row so the player can immediately type over the placeholder, and
        // scroll it into view -- appending to an overflowing list puts the new row below the visible
        // window otherwise (task 6.11).
        focusedEditIndex = scratchDocument.Blocks.Count - 1;
        scrollFocusedRowIntoView = true;
        RequestRecompose();
        return true;
    }

    private bool OnClickSwitchToRead()
    {
        // Flush any pending edit BEFORE releasing the lock: the server processes messages in
        // send order, so releasing first would let the flushed edit arrive lock-less and be
        // silently rejected by ApplyEdit's lock check.
        if (focusedEditIndex is { } switchIdx) NormalizeRowOnCommit(switchIdx);
        FlushIfDirty();
        SendReleaseLockPacket();

        // EnterMode ends in a Compose call, so it shares the same mid-dispatch hazard as every
        // other button handler here (see pendingRecomposeAction's doc comment) -- this button's
        // own OnMouseUpOnElement fires it from inside GuiComposer.OnMouseUp's dispatch loop.
        // Deferred as a raw action (not via RequestRecompose, which only knows how to recompose
        // the CURRENT view) since this call also flips IsEditorMode itself.
        pendingRecomposeAction = () => EnterMode(false, null);
        return true;
    }

    public override void OnMouseUp(MouseEvent args)
    {
        base.OnMouseUp(args);

        // The text-size slider defers its recompose to here (see textSizePendingRecompose): a
        // slider drag rebuilds the slider element every intermediate value, so recomposing inside
        // the change callback would orphan the drag after one step. base.OnMouseUp has already
        // returned, so the composer's dispatch loop is finished and a direct recompose is safe.
        if (textSizePendingRecompose)
        {
            textSizePendingRecompose = false;
            capi.StoreModConfig(clientConfig, ScribeModSystem.ClientConfigFileName);
            RecomposeEditorViewPreservingFocus();
        }
    }

    // ---------------- Autosave (throttled) ----------------

    private void StartAutosaveTick()
    {
        autosaveTickListenerId = capi.Event.RegisterGameTickListener(OnAutosaveTick, 1000);
    }

    private void StopAutosaveTick()
    {
        if (autosaveTickListenerId is { } id)
        {
            capi.Event.UnregisterGameTickListener(id);
            autosaveTickListenerId = null;
        }
    }

    private void OnAutosaveTick(float deltaTime)
    {
        FlushIfDirty();
    }

    private void FlushIfDirty()
    {
        if (!isDirty || scratchDocument is null) return;

        var bytes = ScribeDocumentCodec.Serialize(scratchDocument);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeEditDocumentMessage
        {
            PosX = lectern.Pos.X,
            PosY = lectern.Pos.Y,
            PosZ = lectern.Pos.Z,
            DocumentBytes = bytes,
        });
        isDirty = false;

        // The authoritative resync for this edit is still in flight — update the local cache
        // now so an immediate switch-to-read doesn't flash the pre-edit content in between. A
        // fresh deserialize (not the live scratchDocument reference) keeps it un-aliased, since
        // scratchDocument keeps mutating while editor mode continues.
        if (ScribeDocumentCodec.TryDeserialize(bytes, out var copy) && copy is not null)
        {
            lectern.ApplyLocalOptimisticEdit(copy);
        }
    }

    // ---------------- Lifecycle ----------------

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();

        if (IsEditorMode)
        {
            if (focusedEditIndex is { } closeIdx) NormalizeRowOnCommit(closeIdx);
            FlushIfDirty();
            SendReleaseLockPacket();
        }

        StopAutosaveTick();

#if DEBUG
        UnregisterDebugSliders();
#endif
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private void SendReleaseLockPacket()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeReleaseLockMessage
        {
            PosX = lectern.Pos.X,
            PosY = lectern.Pos.Y,
            PosZ = lectern.Pos.Z,
        });
    }
}
