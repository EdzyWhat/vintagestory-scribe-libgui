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
using Vintagestory.API.Common;   // ItemStack (Tracker/Link display item)
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

public abstract partial class ScribeDialogBase
{
    // ---------------- Build ----------------

    protected override Widget Build() =>
        // Wrap the whole dialog body in the single persistent-root StatefulWidget
        // (reconcile-animating-surfaces §3.1). GuiBase runs this Build() only once per open (and again on
        // each ForceRebuild); the body it returns then persists, so an in-place update reconciles the body
        // via RebuildBody() (reusing the live editor rows + fields) rather than tearing the tree down. The
        // body tree itself is BuildBodyTree(), re-invoked on every reconcile so it re-reads live state.
        new ScribeDialogBody(bodyKey, BuildBodyTree);

    /// <summary>The dialog body subtree, re-invoked on every reconcile by <see cref="ScribeDialogBody"/>
    /// (reconcile-animating-surfaces §3.1) so it reflects the dialog's current live state. Reads the
    /// Pixel-Art Display preference AND the Pixel Art Size (W) fresh each build (mirrors how RowStyle reads
    /// WindowFontScale fresh) so toggling either relays out this dialog on the MyPinsChanged/UpdateMySettings
    /// rebuild with no reopen. On = Scribe's light theme + notebook art; off = the player's global LibGUI
    /// theme with no art. W drives the whole proportional layout via ScribeLayout; the window Size is derived
    /// from the same W in CreateWindowConfig (applied at open).
    ///
    /// <para>The OuterArtBox is the notebook art itself (or a bare box when Pixel-Art Display is OFF, or the
    /// flat placeholder color when the PNG is missing — the existing gate + fallback, now at the root).
    /// Sized to W × H so the stretch-to-fill backdrop is a uniform, distortion-free scale. There is no
    /// WindowFrame: the tree below IS the header + content, so the art frames everything rather than
    /// sitting as a strip beneath a stock bar.</para></summary>
    private Widget BuildBodyTree()
    {
        bool pixelArt = modSystem.MySettings.PixelArtDisplay;
        var layout = host.GetLayout(modSystem.MySettings.PixelArtSize);

        // Shade the ENTIRE composed dialog (backdrop + chrome + text) by the light reaching the player
        // (respect-local-illumination D2/D4). Wrapping OUTSIDE the Theme means the SaveLayer flattens the
        // whole tree and the one brightness/tint matrix applies uniformly to every surface, with no per-dialog
        // wiring. currentShade is refreshed each frame in OnRenderGUI; when it stays in the same quantized
        // bucket the widget is configured identically, so LibGUI's paint cache is undisturbed (D3). A
        // full-bright neutral shade (the seed, and full daylight) is the identity — ScribeGlobalTint skips the
        // layer entirely then, so the fully-lit dialog is pixel-for-pixel the pre-illumination look.
        return new ScribeGlobalTint(
            new Theme(
                ResolveTheme(pixelArt),
                child: WrapBackdrop(pixelArt, layout, BuildOuterArtBox(layout))),
            brightness: currentShade.Brightness,
            tintR: currentShade.TintR,
            tintG: currentShade.TintG,
            tintB: currentShade.TintB);
    }

    /// <summary>The <see cref="ThemeData"/> this dialog wraps its tree in — <c>protected virtual</c> so a
    /// subclass can pick its own palette without forking <see cref="Build"/> (the tablet returns the
    /// earthen <see cref="ScribeTheme.Tablet"/> instead of the parchment <see cref="ScribeTheme.Light"/>).
    /// The default selects the shared parchment/global theme, so the three incumbents are unchanged
    /// (add-tablet-dialog D6).</summary>
    protected virtual ThemeData ResolveTheme(bool pixelArt) => ScribeTheme.For(pixelArt);

    /// <summary>Tint color for the title-bar chrome glyphs (the editor pencil and the drag grip) —
    /// <c>private protected virtual</c> so a subclass can restyle them without forking
    /// <see cref="BuildTitleBar"/>. The default is the global theme's mid-gray <c>OnSurfaceVariant</c>;
    /// the three incumbent dialogs are unchanged (add-tablet-clay-type-themes 8.5). The tablet overrides
    /// this to a semi-transparent dark material ink so the clay texture bleeds faintly through the strokes
    /// and the glyphs read as darkened/engraved rather than a washed-out gray. The tint is applied the same
    /// way every <see cref="VsIcon"/> applies it — <see cref="SKBlendMode.SrcIn"/>, glyph-only — so a
    /// partial-alpha color fades the STROKES, never fills the transparent icon tile (a Multiply color-filter
    /// WOULD paint the whole quad; that was the 2026-08-03 "pale tile" regression). Does NOT apply to the
    /// close button, which keeps its <c>Error</c> color.</summary>
    private protected virtual Vector4 TitleChromeGlyphColor(ColorScheme colors) => colors.OnSurfaceVariant;

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
        var bmp = modSystem.GetBackdropBitmap(host.BackdropSpec);
        if (bmp is not null)
        {
            // Draw the backdrop ourselves with NEAREST sampling so the small native-resolution pixel-art
            // source scales up crisp (see ScribePixelArtBackdrop). SizedBox pins the box to the dialog size
            // so the proxy — and thus the backdrop rect — is exactly layout.W×H behind the content tree.
            // The custom widget sets SharedPaint.Color opaque itself, so it needs no ScribeResetPaintColor.
            return new ScribePixelArtBackdrop(bmp,
                new SizedBox(width: layout.W, height: layout.H, child: tree));
        }
        // No bitmap (asset missing): flat parchment-colour fallback. A plain BoxStyle Color sets its own
        // SharedPaint.Color, but wrap in ScribeResetPaintColor to keep the paint-hygiene guarantee uniform.
        var style = new BoxStyle { Color = new Vector4(0.85f, 0.78f, 0.62f, 1.0f), Width = layout.W, Height = layout.H };
        return new ScribeResetPaintColor(new Container(style: style, child: tree));
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

        // Pencil + grip chrome tint — the default is the global gray; the tablet overrides it to a
        // semi-transparent dark material ink so the strokes read as engraved (add-tablet-clay-type-themes 8.5).
        Vector4 chromeColor = TitleChromeGlyphColor(colors);

        const float titleBtnSpacing = 6f;
        Widget titleSlot = new Expanded(_isTitleEditing
            ? BuildTitleField(titleStyle)
            : BuildTitleDisplay(displayTitle, titleStyle));

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
                        child: new ScribeVsIconGlyph("scribeedit", pencilSize, chromeColor))))
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
                                        chromeColor))),
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
                                        chromeColor))),
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

    /// <summary>The resting (non-editing) title widget, sized to fill the title slot's Expanded. Default is
    /// today's single-line <see cref="RichText"/> with ellipsis overflow (Lectern/Notebook, unchanged). The
    /// tablet overrides this to render the title as display-only cuneiform truncated to the band width
    /// (add-tablet-cuneiform-chrome D2). <paramref name="displayTitle"/> is the already-resolved title text
    /// (host default substituted for the codec's "Untitled"); <paramref name="titleStyle"/> carries the
    /// resolved size/family/weight/color so an override can match the band metrics.</summary>
    private protected virtual Widget BuildTitleDisplay(string displayTitle, TextStyle titleStyle) =>
        new RichText(new TextSpan(displayTitle), titleStyle, maxLines: 1, overflow: TextOverflow.Ellipsis);

    /// <summary>The active (editing) title widget — a live text input bound to <see cref="_titleController"/>
    /// and <see cref="_titleFocusNode"/>. Default is the stock LibGUI single-line <see cref="TextField"/>
    /// (Lectern/Notebook, unchanged), with the maxlength gate and Enter/Escape commit wired here so the
    /// shared commit machinery (<see cref="CommitTitleIfEditing"/>) is untouched by an override. The tablet
    /// overrides this to a single-line cuneiform input driven by the SAME controller/focus node, so its
    /// <see cref="_isTitleEditing"/> / <see cref="_pendingTitleEditRebuild"/> / <see cref="_pendingTitleFocus"/>
    /// deferral all still apply (add-tablet-cuneiform-chrome D2).</summary>
    private protected virtual Widget BuildTitleField(TextStyle titleStyle) =>
        new TextField(
            _titleController!,
            _titleFocusNode!,
            new TextFieldStyle { FillColor = new Vector4(0, 0, 0, 0), BorderThickness = 0, TextStyle = titleStyle },
            onKeyDown: OnTitleFieldKeyDown);

    /// <summary>Shared title-input key handling for both the default <see cref="TextField"/> and a subclass's
    /// cuneiform title input: block typing past <see cref="ScribeDocument.MaxTitleLength"/> (letting the
    /// caret/delete keys through), and commit + rebuild on Enter/Escape. Extracted so the tablet's cuneiform
    /// title field reuses the identical maxlength + commit behavior.</summary>
    private protected void OnTitleFieldKeyDown(KeyboardEvent e)
    {
        if (_titleController!.Text.Length >= ScribeDocument.MaxTitleLength
            && !e.Ctrl && e.KeyCode is not ((int)GlKeys.BackSpace or (int)GlKeys.Delete
                or (int)GlKeys.Left or (int)GlKeys.Right or (int)GlKeys.Home or (int)GlKeys.End))
            e.Handled = true;
        if (e.KeyCode is (int)GlKeys.Enter or (int)GlKeys.KeypadEnter or (int)GlKeys.Escape)
        {
            CommitTitleIfEditing();
            // Swap the title slot back from inline-input to display via an in-place reconcile
            // (reconcile-animating-surfaces §3.1). RebuildBody marks the body dirty for the NEXT frame's
            // build pass rather than unmounting the tree synchronously, so it also sidesteps the
            // mid-dispatch orphan-button NPE the old ForceRebuild had to defer around (see
            // _pendingTitleEditRebuild) — and the editor rows are reused, so any focused row keeps its caret.
            RebuildBody();
            e.Handled = true;
        }
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

    /// <summary>Re-dispatch a synthetic pointer-move at the current cursor position so LibGUI re-runs its
    /// hit-test and updates hover (fix-list-collapse-stale-hover). Called each frame while a list collapse
    /// is animating, because LibGUI otherwise only recomputes hover on real mouse motion — so a row that
    /// slides under a stationary cursor keeps stale hover and its delete/pin controls stay hidden until the
    /// mouse moves. This reuses LibGUI's own idiom (GuiBase.OnMouseMove itself synthesizes a PointerEvent to
    /// correct hover) and the exact raw→window-local conversion the grip drag uses; all members are reachable
    /// on the GuiBase subclass without a gui-dep change.</summary>
    private void RefreshHoverAtCursor()
    {
        if (RootElement?.RenderObject == null) return;
        var local = ScribeHoverRefresh.ToWindowLocal(
            capi.Input.MouseX, capi.Input.MouseY, GetUiScale(), WindowPos);
        EventDispatcher.DispatchPointerMove(RootElement, new PointerEvent(local.X, local.Y));
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
    /// <summary>SectionRightCol builder — <c>protected virtual</c> so a subclass may replace the entire
    /// right column (the tablet returns an empty, nav-less column whose <c>SideColW</c> width still
    /// preserves the symmetric side margin). The default body builds the baseline nav stack; the three
    /// incumbent dialogs (Lectern + both Notebooks) override nothing and take this path unchanged
    /// (add-tablet-dialog D2).</summary>
    protected virtual Widget BuildRightColNav()
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
    private Widget WithTooltip(string key, Widget child)
    {
        var theme = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay);
        // Shade the whole tooltip — bubble AND content — by the live illumination shade at reduced hover
        // strength (refine-scribe-hover-tooltips D2 + bug-1). Tooltips render in the global Overlay layer —
        // OUTSIDE the body's own ScribeGlobalTint wrap (BuildBodyTree) — so without ShadedTooltip the bubble
        // (and, before bug-1, its text) stays full-brightness while the body is dimmed by low light and
        // visibly "sticks out." ShadedTooltip shades the bubble Background/Border via a Theme sandwich and the
        // content via ForHover; see its remarks. NOTE: this is the shared entry point for every
        // nav-button/title-bar tooltip, so Scribe Settings is excluded BY CONSTRUCTION — its dialog
        // (ScribeSettingsDialog) builds a bare WindowFrame with no ScribeGlobalTint and its help tooltips don't
        // route through here; don't "unify" the two without preserving that.
        return ScribeGlobalTint.ShadedTooltip(
            child: child,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(Lang.Get("scribe:" + key), new TextStyle
                {
                    FontSize = 13,
                    SoftWrap = true,
                    Color = theme.ColorScheme.OnBackground,
                })),
            baseTheme: theme,
            shade: currentShade);
    }

    /// <summary>The tasks column's content builder — <c>protected virtual</c> so a subclass may supply
    /// its own single-view center instead of the <see cref="viewMode"/>-switched view (the tablet returns
    /// a cuneiform title banner over the inherited editable task list). The default body routes on
    /// <see cref="viewMode"/>; the three incumbent dialogs override nothing and take this path unchanged
    /// (add-tablet-dialog D2). Its former gear-header chrome row moved to the SectionRightCol nav stack
    /// (scribe-notebook-frame), so this is now just the active view filling the column.</summary>
    protected virtual Widget BuildCentralRegion() => viewMode switch
    {
        ScribeLecternView.Editor   => BuildEditorContent(),
        ScribeLecternView.Pinned   => BuildPinnedContent(),
        ScribeLecternView.Visitors => BuildVisitorsContent(),
        ScribeLecternView.History  => BuildHistoryContent(),
        ScribeLecternView.Timer    => BuildTimerContent(),
        ScribeLecternView.Inventory => BuildInventoryContent(),
        _                          => BuildReadContent(),
    };

    /// <summary>The live row style for this build, derived from the player's current settings (NOT cached
    /// at open — add-settings-tab D4), so a window-font-scale change from the settings view repaints the
    /// open dialog on the next rebuild. Passes through <see cref="DecorateRowStyle"/> so a subclass may
    /// layer tier-specific row behavior (the tablet flips on the cuneiform row path) without duplicating
    /// the settings-derivation.</summary>
    private ScribeRowStyle RowStyle => DecorateRowStyle(ScribeRowStyle.FromSettings(modSystem.MySettings));

    /// <summary>Hook to adjust the settings-derived <see cref="ScribeRowStyle"/> for this dialog tier. The
    /// default returns it unchanged (Lectern/Notebook). The tablet overrides it to set
    /// <see cref="ScribeRowStyle.UseCuneiform"/> + the glyph bundle under the single cuneiform branch, so
    /// its editable rows type in cuneiform (add-tablet-cuneiform-chrome).</summary>
    private protected virtual ScribeRowStyle DecorateRowStyle(ScribeRowStyle style) => style;

    /// <summary>Action for the editor footer's Settings gear, or null to omit it. The default is null: the
    /// Lectern/Notebook reach Scribe Settings through their nav column, so their footer has no gear. The
    /// tablet — which drops the nav column (D3) — overrides this to <c>modSystem.OpenSettings</c> so a gear
    /// appears right of the ⓘ info button, styled identically (add-tablet-cuneiform-chrome).</summary>
    private protected virtual Action? EditorSettingsGearAction => null;

    /// <summary>Lang key for the empty-document hint shown in the Read and Edit views. Notebook
    /// subclasses override this to show "This notebook is empty…" instead of the Lectern phrasing.</summary>
    protected virtual string EmptyHintLangKey => "scribe:scribe-gui-edit-hint";

    /// <summary>The document's display title as the title bar renders it: the live scratch title (or the
    /// persisted document title when not editing), with the codec default ("Untitled") mapped to the host's
    /// meaningful default (e.g. "Tablet"). Exposed so a subclass banner (the tablet's cuneiform title) shows
    /// exactly the title the bar shows. Never blank (falls back to <see cref="IScribeDocumentHost.DefaultDocumentTitle"/>).</summary>
    protected string DisplayDocumentTitle
    {
        get
        {
            var raw = scratch?.Title ?? host.Document.Title;
            return (raw == ScribeDocument.DefaultTitle ? null : raw) ?? host.DefaultDocumentTitle;
        }
    }

    /// <summary>Whether the read view is a permanently-read-only surface: the "switch to editor" footer
    /// button is dropped and TEXT editing is blocked (a hard or fired tablet — tablet-firing).
    /// The default is false, so the Lectern/Notebook read view keeps its Edit button. The tablet overrides it
    /// to true when the stack is not editable.
    ///
    /// <para>NOTE (zero-point-three-fixes §7.3): read-only no longer forces the checkbox/pin inert — that is
    /// governed by <see cref="ReadViewCompletionAndPinLive"/>, so a hard/fired tablet keeps completion + pin
    /// live while its text stays locked.</para></summary>
    private protected virtual bool ReadViewIsReadOnly => false;

    /// <summary>Whether a read-only read view keeps its checkbox and pin INTERACTIVE (zero-point-three-fixes
    /// §7.3). Default false: irrelevant on the Lectern/Notebook (they are not read-only, so their toggles are
    /// live off <see cref="ReadViewIsReadOnly"/> = false anyway). The tablet overrides it true so a hard/fired
    /// tablet can still complete and unpin — preventing a fired tablet's pin from being stranded on the HUD.</summary>
    private protected virtual bool ReadViewCompletionAndPinLive => false;

    /// <summary>Invoked when the player taps a locked row's text on a read-only tablet, so the surface can
    /// raise its material-specific "soften it / cannot be changed" in-game message (zero-point-three-fixes
    /// §7.4). Null on the Lectern/Notebook and on a wet tablet, where a text tap is not a blocked edit.</summary>
    private protected virtual Action<Guid>? ReadViewTextEditRefused => null;

    /// <summary>Resolve a block's Tracker/Link item icon + display name for a row snapshot, or
    /// <c>(null, null)</c> for a Task/Text block (which render their authored text instead). A Tracker uses
    /// its <see cref="ScribeBlock.TargetItemCode"/>, a Link its <see cref="ScribeBlock.LinkTarget"/>; both are
    /// plain code strings that Core stores API-free, so the parse against the live registries happens here in
    /// the Mod layer (add-tracker-link-tasks Group 5). Kept off the row widgets so they stay <c>capi</c>-free.</summary>
    private (ItemStack? Stack, string? Name) ResolveRowItem(ScribeBlock b)
    {
        if (!b.IsTracker && !b.IsLink) return (null, null);
        string? code = b.IsTracker ? b.TargetItemCode : b.LinkTarget;
        return ScribeItemRef.ResolveDisplay(capi.World, code, b.LinkLabel);
    }

    /// <summary>Build the read view. Promoted from <c>private</c> so the always-edit tablet can render it
    /// directly for a non-editable (hard/fired) stack — it has no <see cref="viewMode"/> switching, so it
    /// can't reach the read view through the default <see cref="BuildCentralRegion"/> routing.</summary>
    protected Widget BuildReadContent() =>
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
                .Select((b, i) =>
                {
                    var (stack, name) = ResolveRowItem(b);
                    return new ScribeReadRowData(
                        Index: i, Kind: b.Kind, Done: b.Done, Pinned: IsPinnedForMe(b.TaskId), TaskId: b.TaskId,
                        Text: b.Text, DisplayStack: stack, DisplayName: name,
                        TargetQuantity: b.TargetQuantity, CurrentQuantity: b.CurrentQuantity, LinkTarget: b.LinkTarget);
                })
                // Drop only an empty-text Task (a stray blank checkbox — belt-and-suspenders, see below).
                // A Text note may be legitimately empty, and a Tracker/Link has no text of its own (it renders
                // an item icon + name), so both pass through — all are non-IsTask, so `!r.IsTask` keeps them.
                .Where(r => !r.IsTask || !string.IsNullOrWhiteSpace(r.Text))
                .ToList(),
            onToggleTask: OnReadViewCompleteTask,
            onTogglePinned: OnReadViewTogglePinned,
            // A Link row's item name is a hyperlink: tapping it opens the referenced Handbook page and never
            // touches completion (add-tracker-link-tasks 5.3). A Tracker's name opens its TARGET item's page
            // the same way (feedback 6.5 — the count target IS a real item with a Handbook entry). Resolve the
            // tapped block by TaskId, then open the page keyed off whichever code the kind carries.
            onOpenLink: taskId =>
            {
                var block = host.Document.FindByTaskId(taskId);
                if (block?.IsLink == true) ScribeItemRef.OpenHandbookPage(capi, block.LinkTarget);
                else if (block?.IsTracker == true) ScribeItemRef.OpenHandbookPage(capi, block.TargetItemCode);
            },
            onSwitchToEditor: TryEnterEditor,
            // Symmetric 0.04·W horizontal inset on the footer button, from the same ScribeLayout width.
            footerButtonPadding: EdgeInsets.Symmetric(
                horizontal: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W),
            style: RowStyle,
            scrollController: sharedScrollController,
            // Host-owned collapse registry (reconcile-animating-surfaces §5.5): a task removed by a Delete-policy
            // completion collapses out via ScribeAnimatedList instead of vanishing. Lives on the dialog so the
            // motion survives the RefreshReadView reconcile, and so OnRenderGUI reads AnyAnimating to pin the
            // scroll + refresh hover — mirroring the editor/Pin Tab wiring.
            collapseRegistry: readCollapseRegistry,
            // A departing read row finished collapsing → re-clamp the (now shorter) scroll extent, mirroring the
            // Pin Tab's onDepartureSettled → RequestClampToExtent. The container retires the ghost itself.
            onDepartureSettled: RequestClampToExtent,
            hintLangKey: EmptyHintLangKey,
            readOnly: ReadViewIsReadOnly,
            completionAndPinLive: ReadViewCompletionAndPinLive,
            onTextEditRefused: ReadViewTextEditRefused);

    /// <summary>The editable task list for the current scratch document. Promoted from <c>private</c> to
    /// <c>protected</c> so a subclass may reuse the inherited editor rather than fork it — the tablet
    /// stacks it under a display-only cuneiform title banner (add-tablet-dialog D4). Bound to the same
    /// <see cref="scratch"/>, focus nodes, and mutation handlers, so task add/edit/check/pin behave
    /// identically wherever it is rebuilt.</summary>
    protected Widget BuildEditorContent()
    {
        var blocks = scratch!.Blocks
            .Select((b, i) =>
            {
                var (stack, name) = ResolveRowItem(b);
                return new ScribeEditRowData(
                    Index: i, Kind: b.Kind, Done: b.Done, Pinned: IsPinnedForMe(b.TaskId), TaskId: b.TaskId,
                    Text: b.Text, DisplayStack: stack, DisplayName: name,
                    TargetQuantity: b.TargetQuantity, CurrentQuantity: b.CurrentQuantity, LinkTarget: b.LinkTarget);
            })
            .ToList();

        int? autoFocus = autoFocusRowOnRebuild;
        autoFocusRowOnRebuild = null; // one-shot

        return new ScribeEditorContent(
            blocks: blocks,
            focusNodes: editorFocusNodes,
            autoFocusIndex: autoFocus,
            onTextChanged: NotifyTextChanged,
            onCommitAndAdvance: EditorAdvanceFrom,
            onCommitAndRetreat: EditorRetreatFrom,
            onInsertTaskBelow: EditorInsertTaskBelow,
            onRowBlurred: OnRowBlurred,
            onMaxLengthReached: OnRowMaxLengthReached,
            onCaretMoved: NotifyCaretMoved,
            onPointerFocus: NotifyPointerFocus,
            onJumpToFirstRow: EditorJumpToFirstRow,
            onJumpToLastRow: EditorJumpToLastRow,
            onToggleTask: ToggleEditorTask,
            onDeleteBlock: DeleteEditorBlock,
            onTogglePinned: TogglePinnedEditorTask,
            // A Tracker row's inline stepper edits its target quantity in scratch; the normal editor flush
            // persists it (the codec serializes TargetQuantity, so no dedicated packet — add-tracker-link-tasks 5.2).
            onTrackerQuantityChanged: SetEditorTrackerTargetQuantity,
            // Drag-reorder follows the moved row into view (anchorViewport defaults false); only a Sink
            // completion passes anchorViewport: true to hold the viewport still.
            onReorderBlock: (from, to) => ReorderEditorBlock(from, to),
            onAdd: OnClickAdd,
            onSwitchToRead: OnClickSwitchToRead,
            onOpenEditorReference: ToggleEditorReferenceHandbook,
            // Footer gear (tablet only). The base returns null so the Lectern/Notebook footer omits it —
            // those dialogs reach Settings through their nav column, which the tablet drops (D3). The tablet
            // overrides EditorSettingsGearAction to return modSystem.OpenSettings (add-tablet-cuneiform-chrome).
            onOpenSettings: EditorSettingsGearAction,
            // Symmetric 0.04·W horizontal inset on the footer button row, from the same ScribeLayout width.
            footerButtonPadding: EdgeInsets.Symmetric(
                horizontal: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W),
            style: RowStyle,
            scrollController: sharedScrollController,
            collapseRegistry: editorCollapseRegistry,
            // A departing row finished collapsing → re-clamp the (now shorter) scroll extent, mirroring the
            // Pin Tab / Read view (RequestClampToExtent). The container retires the ghost itself; the dialog
            // no longer owns departing-row bookkeeping (D0 — replaces OnEditorRowCollapsed).
            onDepartureSettled: RequestClampToExtent,
            // Current illumination shade: threaded so the footer add-kind picker can tint its floating drop-up
            // menu to match the window (the menu paints in the Overlay, outside this body's ScribeGlobalTint).
            currentShade: currentShade,
            hintLangKey: EmptyHintLangKey,
            // Tier cap (scribe-document-policy): dim + disable "Add task" at the tablet's 10-task cap.
            // Uncapped tiers (Lectern, Notebook) always pass true, so their footer is unchanged.
            addTaskEnabled: CanAddTaskUnderPolicy(),
            // Whether the "Done editing" (switch-to-read) footer button renders. True for the tabbed
            // dialogs; the always-edit tablet overrides ShowEditorSwitchToRead to false since it has no
            // Read view (add-tablet-dialog D4).
            showSwitchToRead: ShowEditorSwitchToRead);
    }

    /// <summary>Whether the editor footer shows the "Done editing" (switch-to-read) button. True for the
    /// tabbed dialogs (Lectern/Notebook), which have a Read view to return to. The always-edit tablet
    /// overrides this to false: it has no Read view, and leaving editor mode would null the scratch the
    /// central region reads (add-tablet-dialog D4).</summary>
    protected virtual bool ShowEditorSwitchToRead => true;

    /// <summary>Footer "Editor Features" (ⓘ) button: TOGGLE the "Scribe Editor Features" handbook page
    /// (v1-release-checklist 9.5 — surfaces the keyboard-navigation reference at the point of use;
    /// add-info-button-handbook-toggle — a 2026-08-02 playtester wanted ⓘ to also dismiss the panel).
    ///
    /// <para>Behavior (design D3, "focus, don't hide"): if no handbook dialog is open, fire the game's
    /// registered <c>"handbook"</c> link protocol to OPEN it on our reference page; if a handbook IS
    /// already open, CLOSE it. Re-firing the link protocol while it is open on a different entry would
    /// merely navigate it to our page, so the observable flow is "open ⇒ toggles closed; closed ⇒ opens
    /// to our page" — a page-aware single-click close is not possible without reading the dialog's private
    /// page state (see VSAPI-NOTES.md, survival-mod systems).</para>
    ///
    /// <para>Kept deliberately DECOUPLED from the survival mod: we discover the open handbook by scanning
    /// the public <c>capi.Gui.OpenedGuis</c> for the <see cref="GuiDialog"/> whose public
    /// <see cref="GuiDialog.ToggleKeyCombinationCode"/> is <c>"handbook"</c> and close it via base
    /// <see cref="GuiDialog.TryClose"/> — no reference to <c>GuiDialogHandbook</c> /
    /// <c>ModSystemSurvivalHandbook</c> and no reflection. It also degrades gracefully: if the survival mod
    /// (and thus its handbook dialog + link protocol) isn't loaded, the <c>OpenedGuis</c> scan finds
    /// nothing and <c>LinkProtocols</c> has no <c>"handbook"</c> entry, so both paths are safe no-ops
    /// instead of a crash.</para></summary>
    private void ToggleEditorReferenceHandbook()
    {
        // Discover any open handbook by its stable PUBLIC identity (its toggle-hotkey code), not by its
        // concrete type — this is the reflection-free, decoupled equivalent of OfType<GuiDialogHandbook>().
        GuiDialog? openHandbook = capi.Gui.OpenedGuis
            .FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook");

        if (openHandbook != null)
        {
            // Open ⇒ close it (D3: any open handbook toggles closed). TryClose runs the dialog's normal
            // OnGuiClosed path (D-Q3: its own close sound/animation, no extra work needed here).
            openHandbook.TryClose();
            return;
        }

        // Closed ⇒ open to our reference page via the registered link protocol (unchanged from the
        // open-only original). Absent survival mod ⇒ no "handbook" protocol ⇒ graceful no-op.
        if (capi.LinkProtocols.TryGetValue("handbook", out var open))
            open(new LinkTextComponent("handbook://craftinginfo-scribe-editor-reference"));
    }

    /// <summary>Read-view task checkbox click: complete the task by its stable identity via the
    /// lock-free <see cref="ScribeCompleteTaskMessage"/> (the read view holds no editor lock). If the
    /// player has pinned this task, the server completes it store-first under their completion policy;
    /// otherwise it just toggles the shared document's done flag — the same gesture the HUD reuses.</summary>
    private void OnReadViewCompleteTask(Guid taskId)
    {
        var policy = modSystem.MySettings.CompletionPolicy;

        // Optimistic-then-confirm (reconcile-animating-surfaces D9): apply the completion to a LOCAL copy of
        // the document and refresh the read view immediately, then send to the server. This is why the
        // editor feels instant — it never waits for the round-trip — and it fixes the read view's real gap:
        // an UNPINNED completion had no pin push to ride, so its Delete/Sink result stayed invisible until
        // some unrelated rebuild. The authoritative resync (BlockEntityScribeLectern.FromTreeAttributes →
        // RefreshReadView) supersedes this shortly, exactly as it supersedes the editor's optimistic edit.
        //
        // EXCEPTION — a permanently read-only source (hard/fired tablet): the SERVER collapses every
        // document-mutating policy to a plain Unpin there (CollapsePolicyForReadOnlySource, §7.5), so
        // predicting a delete/sink locally would flash a removal the server refuses. Skip the optimistic
        // document apply on that surface and let the (pin-push-driven) resync drive the visible change.
        if (!ReadViewIsReadOnly)
        {
            // Un-aliased copy via a codec round-trip (matching FlushIfDirty's optimistic-edit copy), so the
            // authoritative document is never mutated in place by a client prediction. ApplyLocal toggles the
            // done flag and applies the shared policy decision; a genuine content change refreshes the read view.
            var bytes = ScribeDocumentCodec.Serialize(host.Document);
            if (ScribeDocumentCodec.TryDeserialize(bytes, out var copy) && copy is not null)
            {
                var outcome = ScribeCompletion.ApplyLocal(copy, taskId, policy);
                if (outcome.DocChanged)
                {
                    host.ApplyLocalOptimisticEdit(copy);
                    RefreshReadView();
                }
            }
        }

        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeCompleteTaskMessage
        {
            DocId = host.Document.DocId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Policy = (byte)policy,
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
        // Tier cap (scribe-document-policy): honor the tablet's 1-pin cap on the read view too, with the
        // same seamless swap as the editor — pinning a new task at the cap releases the older pin.
        => TogglePinWithPolicy(taskId);
}
