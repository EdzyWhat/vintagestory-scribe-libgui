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

public abstract partial class ScribeDialogBase
{
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
        // Title is 1.5× the window body text size — "50% larger" (v1-playtest-fixes 5.1). The body size is
        // BaseWindowFontSize × the player's WindowFontScale, so the title tracks a live font-scale change too.
        float titleFont = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale) * 1.5f;

        var titleStyle = new TextStyle { FontSize = titleFont, FontFamily = ScribeRowControlNudge.TitleFontFamily, Weight = FontWeight.Bold, Color = colors.OnSurface };
        var rawTitle = _isTitleEditing ? null : (scratch?.Title ?? host.Document.Title);
        // Treat the codec default title ("Untitled") as absent so each host type can supply its own
        // meaningful default (e.g. "Notebook" vs "Lectern") rather than always showing "Untitled".
        var displayTitle = (rawTitle == ScribeDocument.DefaultTitle ? null : rawTitle) ?? host.DefaultDocumentTitle;

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
                            // Defer BOTH the rebuild and the focus out of this pointer handler to OnRenderGUI.
                            // Calling ForceRebuild() (or RequestFocus()) here — inside the pointer dispatch —
                            // unmounts the tree mid-walk and orphans a sibling button, crashing LibGUI's
                            // PlaySound on the same click. See _pendingTitleEditRebuild / _pendingTitleFocus.
                            _pendingTitleEditRebuild = true;
                            _pendingTitleFocus = true;
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
        ScribeLecternView.History  => BuildHistoryContent(),
        ScribeLecternView.Timer    => BuildTimerContent(),
        _                          => BuildReadContent(),
    };

    /// <summary>The live row style for this build, derived from the player's current settings (NOT cached
    /// at open — add-settings-tab D4), so a window-font-scale change from the settings view repaints the
    /// open dialog on the next rebuild.</summary>
    private ScribeRowStyle RowStyle => ScribeRowStyle.FromSettings(modSystem.MySettings);

    /// <summary>Lang key for the empty-document hint shown in the Read and Edit views. Notebook
    /// subclasses override this to show "This notebook is empty…" instead of the Lectern phrasing.</summary>
    protected virtual string EmptyHintLangKey => "scribe:scribe-gui-edit-hint";

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
            scrollController: sharedScrollController,
            hintLangKey: EmptyHintLangKey);

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
            onOpenEditorReference: OpenEditorReferenceHandbook,
            // Symmetric 0.04·W horizontal inset on the footer button row, from the same ScribeLayout width.
            footerButtonPadding: EdgeInsets.Symmetric(
                horizontal: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W),
            style: RowStyle,
            scrollController: sharedScrollController,
            departingRows: departing,
            collapseRegistry: editorCollapseRegistry,
            onDepartingCollapsed: OnEditorRowCollapsed,
            hintLangKey: EmptyHintLangKey);
    }

    /// <summary>Footer "Editor Features" (ⓘ) button: open the "Scribe Editor Features" handbook page
    /// (v1-release-checklist 9.5 — surfaces the keyboard-navigation reference at the point of use). We fire
    /// the game's registered <c>"handbook"</c> link protocol with the same <c>handbook://</c> href the lang
    /// pages already link to, rather than reaching into <c>ModSystemSurvivalHandbook</c>'s private dialog —
    /// this keeps us decoupled and degrades gracefully: if the survival mod (and thus its handbook protocol)
    /// isn't loaded, <c>LinkProtocols</c> has no <c>"handbook"</c> entry and this is a no-op instead of a
    /// crash. See VSAPI-NOTES.md (survival-mod systems) for the page-code / link-protocol mechanics.</summary>
    private void OpenEditorReferenceHandbook()
    {
        if (capi.LinkProtocols.TryGetValue("handbook", out var open))
            open(new LinkTextComponent("handbook://craftinginfo-scribe-editor-reference"));
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
}
