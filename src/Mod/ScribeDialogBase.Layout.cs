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
using Gui.Widgets.Painting;      // BoxStyle, Transform
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
        // Single persistent-root widget: GuiBase calls Build() once per open (and per ForceRebuild); in-place updates reconcile via RebuildBody(), reusing live rows/fields.
        new ScribeDialogBody(bodyKey, BuildBodyTree);

    /// <summary>The dialog body subtree, re-invoked on every reconcile so it re-reads live state (Pixel-Art Display, Pixel Art Size).
    /// On = Scribe's light theme + notebook art; off = the player's global LibGUI theme, no art. W drives the proportional layout via
    /// ScribeLayout; the window Size derives from the same W in CreateWindowConfig.</summary>
    private Widget BuildBodyTree()
    {
        bool pixelArt = modSystem.MySettings.PixelArtDisplay;
        var layout = host.GetLayout(modSystem.MySettings.PixelArtSize);

        // Wrapping OUTSIDE the Theme lets the SaveLayer flatten the whole tree so one brightness/tint matrix shades every surface.
        // currentShade is refreshed in OnRenderGUI; staying in the same quantized bucket leaves LibGUI's paint cache undisturbed,
        // and a full-bright neutral shade skips the layer entirely.
        return new ScribeGlobalTint(
            new Theme(
                ResolveTheme(pixelArt),
                child: WrapBackdrop(pixelArt, layout, BuildOuterArtBox(layout))),
            brightness: currentShade.Brightness,
            tintR: currentShade.TintR,
            tintG: currentShade.TintG,
            tintB: currentShade.TintB);
    }

    /// <summary>The <see cref="ThemeData"/> the tree is wrapped in; overridable so a subclass (e.g. the tablet) picks its own palette.</summary>
    protected virtual ThemeData ResolveTheme(bool pixelArt) => ScribeTheme.For(pixelArt);

    /// <summary>Tint for the title-bar chrome glyphs (pencil, drag grip); the close button keeps its Error color.
    /// Applied SrcIn (glyph-only), so a partial-alpha override fades the strokes without filling the transparent icon tile.</summary>
    private protected virtual Vector4 TitleChromeGlyphColor(ColorScheme colors) => colors.OnSurfaceVariant;

    /// <summary>Tint for INACTIVE right-column nav glyphs. A dedicated seam so a subclass can darken them without changing the shared OnSurfaceVariant role used by muted body text. The active tab uses its own per-view color.</summary>
    private protected virtual Vector4 NavIconColor(ColorScheme colors) => colors.OnSurfaceVariant;

    /// <summary>Border color for a FOCUSED editable field; the row path (ScribeRowStyle.InputFocusBorderColor) and the guestbook field both consume it so they can't drift.</summary>
    private protected virtual Vector4 InputFocusBorderColor(ColorScheme colors) => colors.Primary;

    /// <summary>Override for the completion checkbox's tick color, or null to keep the theme's CheckColor. Seeded onto ScribeRowStyle.CheckTickColor so all four row surfaces share it.</summary>
    private protected virtual Vector4? CheckTickColor(ColorScheme colors) => null;

    /// <summary>Horizontal placement of the right-column nav-button stack within its SideColW column.
    /// Default (Pages group) is Start, hugging the inner edge; the Chalkboard overrides it. The Tablet renders no nav column, so it never fires there.</summary>
    private protected virtual CrossAxisAlignment NavButtonAlignment(float sideColW, float navBoxW) =>
        CrossAxisAlignment.Start;

    /// <summary>Restyle the completion-policy Dropdown (Pin Tab picker). Returns the theme's style by default; the chalkboard overrides it because the stock selected-row fill and label are unreadable on dark slate.</summary>
    private protected virtual DropdownStyle DecoratePolicyDropdownStyle(DropdownStyle style) => style;

    /// <summary>Wraps the layout tree in the W × H OuterArtBox: the notebook backdrop when Pixel-Art Display is ON, a bare same-sized box when OFF.
    /// A missing PNG degrades to a flat tan placeholder. The size is pinned here so the art fills the dialog exactly.</summary>
    private Widget WrapBackdrop(bool pixelArt, ScribeLayout layout, Widget tree)
    {
        if (!pixelArt)
        {
            return new SizedBox(width: layout.W, height: layout.H, child: tree);
        }
        var bmp = modSystem.GetBackdropBitmap(host.BackdropSpec);
        if (bmp is not null)
        {
            // NEAREST sampling keeps the small pixel-art source crisp; the SizedBox pins the backdrop rect to layout.W×H.
            return new ScribePixelArtBackdrop(bmp,
                new SizedBox(width: layout.W, height: layout.H, child: tree));
        }
        // No bitmap: flat parchment fallback; ScribeResetPaintColor keeps paint hygiene uniform.
        var style = new BoxStyle { Color = new Vector4(0.85f, 0.78f, 0.62f, 1.0f), Width = layout.W, Height = layout.H };
        return new ScribeResetPaintColor(new Container(style: style, child: tree));
    }

    /// <summary>The OuterArtBox's contents: the draggable TitleBar band over the three-column SectionInnerBox. The ~7% of H below the inner box is bottom margin.</summary>
    private Widget BuildOuterArtBox(ScribeLayout layout)
    {
        // The title's content box (TitleBtnsH) is taller than one text line (TitleLineHeight) and the title sits flush at the top, so all
        // that slack lies between title and subtitle. It is the same for any wrapped line count. Closed by 45% via a paint-only nudge of
        // the SectionInnerBox, since Padding didn't visibly move things in-game.
        const float TitleSubtitleGapReduction = 0.45f;
        // Clamped to >= 0 so a small dialog at max font scale never nudges the wrong way.
        float titleSubtitleGap = MathF.Max(0f, layout.TitleBtnsH - TitleLineHeight);
        float sectionUpNudge = TitleSubtitleGapReduction * titleSubtitleGap;

        return new Column(
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                BuildTitleBar(layout),
                Transform.Translate(BuildSectionInnerBox(layout), new Vector2(0f, -sectionUpNudge)),
            });
    }

    /// <summary>When Pixel-Art Display is OFF, wraps <paramref name="child"/> in an opaque theme-Surface panel (same fill as the Settings window body) so the title row and content don't read as transparent onto the world; ON returns the child unwrapped.</summary>
    private Widget FlatPanel(Widget child)
    {
        if (modSystem.MySettings.PixelArtDisplay) return child;
        return new Container(
            style: new BoxStyle { Color = ThemeData.Default.ColorScheme.Surface },
            child: child);
    }

    /// <summary>The title's live font size: 1.5× the window body text size, WindowFontScale-aware. Shared by <see cref="BuildTitleBar"/> and <see cref="TitleLineHeight"/>.</summary>
    private float TitleFontSize => ScribeRowConstants.BaseWindowFontSize
        * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale) * 1.5f;

    /// <summary>The title font's line height at <see cref="TitleFontSize"/>. <see cref="BuildTitleBar"/> and <see cref="BuildOuterArtBox"/> must use this identical value for the band-growth and gap math.</summary>
    private float TitleLineHeight
    {
        get
        {
            var metrics = TextLayoutHelper.GetFont(ScribeRowControlNudge.TitleFontFamily, TitleFontSize, FontWeight.Bold).Metrics;
            return metrics.Descent - metrics.Ascent + metrics.Leading;
        }
    }

    /// <summary>The TitleBar band (<c>W × 0.13H</c>), the window's drag zone (<see cref="WindowConfig.DragHandleHeight"/>): a bottom-anchored row with the title on the left and nav/close buttons right-aligned. The close button calls <see cref="GuiBase.TryClose"/>.</summary>
    private Widget BuildTitleBar(ScribeLayout layout)
    {
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float titleFont = TitleFontSize;

        var titleStyle = new TextStyle { FontSize = titleFont, FontFamily = ScribeRowControlNudge.TitleFontFamily, Weight = FontWeight.Bold, Color = colors.OnSurface };
        var rawTitle = _isTitleEditing ? null : (scratch?.Title ?? host.Document.Title);
        // The codec default title ("Untitled") counts as absent so each host supplies its own default.
        var displayTitle = (rawTitle == ScribeDocument.DefaultTitle ? null : rawTitle) ?? host.DefaultDocumentTitle;

        Vector4 chromeColor = TitleChromeGlyphColor(colors);

        int titleMaxLines = TitleMaxLines;

        const float titleBtnSpacing = 6f;
        // Left padding gives the leading grip breathing room from the title text.
        Widget titleSlot = new Expanded(child: new Padding(
            EdgeInsets.Only(left: titleBtnSpacing * 0.75f),
            child: _isTitleEditing
                ? BuildTitleField(titleStyle)
                : BuildTitleDisplay(displayTitle, titleStyle)));

        // Pencil: shown only on the Edit tab. `scratch` is non-null exactly while viewMode == Editor (other tabs null it), so the null check is the gate; the always-edit Tablet keeps it throughout.
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
                            // Defer the rebuild and focus to OnRenderGUI: ForceRebuild()/RequestFocus() inside pointer dispatch unmounts the tree mid-walk, orphaning a sibling button and crashing PlaySound. See _pendingTitleEditRebuild / _pendingTitleFocus.
                            _pendingTitleEditRebuild = true;
                            _pendingTitleFocus = true;
                        },
                        child: new ScribeVsIconGlyph("scribeedit", pencilSize, chromeColor))))
            : null;

        // Expand/collapse-all toggle for Inbox/Sent History only (the pencil is Editor-only, so they never share the slot); reuses the per-row chevron glyphs.
        bool allAssignmentRowsExpanded = AllVisibleAssignmentRowsExpanded();
        Widget? expandCollapseAllSlot = viewMode is ScribeLecternView.Inbox or ScribeLecternView.SentHistory
            ? TitleButton(
                allAssignmentRowsExpanded ? "scribetriangledown" : "scribetriangleright",
                allAssignmentRowsExpanded ? "scribe-assignment-collapse-all" : "scribe-assignment-expand-all",
                chromeColor,
                size: ScribeRowConstants.RowCheckboxSize * 1.1f,
                onTap: ToggleAllVisibleAssignmentRows)
            : null;

        // Leading drag-grip. It owns its own window drag via a GestureDetector nested INSIDE the tooltip: the tooltip's MouseRegion is an active
        // hit target, so GuiBase would capture the pointer-down before its band-drag check and swallow the press. See the gripDragging fields + VSAPI-NOTES.md §LibGUI.
        // Chrome (grip + trailing buttons) is anchored to a fixed top offset matching the title, so a wrapped title doesn't drag it down; a paint-only
        // Transform.Translate nudge (Padding doesn't visibly move the glyph) centers it on the first line. Scaled by WindowFontScale like the title font.
        const float ChromeTopNudgeBase = 6f;
        float chromeTopNudge = ChromeTopNudgeBase * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        Widget gripSlot = Transform.Translate(
            WithTooltip("scribe-gui-drag",
                new GestureDetector(
                    onPress: OnGripDragStart,
                    onMove: OnGripDragMove,
                    onRelease: OnGripDragEnd,
                    child: new ScribeVsIconGlyph("scribegrip", ScribeRowConstants.RowCheckboxSize * 1.1f, chromeColor))),
            new Vector2(0f, chromeTopNudge));

        // Trailing group: pencil (Editor only) · expand/collapse-all (Inbox/Sent History only) · close (delete SVG at 1.4×).
        var trailingGroup = new List<Widget>();
        if (pencilSlot is not null) trailingGroup.Add(pencilSlot);
        if (expandCollapseAllSlot is not null) trailingGroup.Add(expandCollapseAllSlot);
        trailingGroup.Add(TitleButton("scribeclose", "scribe-gui-close", colors.Error,
            size: ScribeRowConstants.RowCheckboxSize * 1.4f, onTap: () => TryClose()));

        // Estimates the title's wrapped line count to size the band; an off-by-one guess only grows the band a line early/late.
        float gripWidth = ScribeRowConstants.RowCheckboxSize * 1.1f;
        float trailingWidth = titleBtnSpacing * (trailingGroup.Count - 1);
        if (pencilSlot is not null) trailingWidth += titleBtnSpacing * 1.5f + pencilSize;
        if (expandCollapseAllSlot is not null) trailingWidth += ScribeRowConstants.RowCheckboxSize * 1.1f;
        trailingWidth += ScribeRowConstants.RowCheckboxSize * 1.4f; // close button, always present
        float titleAvailableW = layout.TitleBtnsW - 0.02f * layout.W - 0.04f * layout.W
            - gripWidth - titleBtnSpacing * 0.75f - trailingWidth;
        // The title TextField can't wrap, so it is always 1 line while editing.
        string titleForWrap = _isTitleEditing ? "" : displayTitle;
        int actualTitleLines = CountWrappedLines(
            titleForWrap, titleStyle.FontFamily, titleStyle.FontSize, titleStyle.Weight, titleAvailableW, titleMaxLines);

        // Every child is top-anchored (Start) so the title's top is invariant across 1-line and wrapped titles; chrome has its own chromeTopNudge.
        Widget titleRow = new Row(
            mainAxisAlignment: MainAxisAlignment.SpaceBetween,
            crossAxisAlignment: CrossAxisAlignment.Start,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                gripSlot,
                titleSlot,
                Transform.Translate(
                    new Row(
                        crossAxisAlignment: CrossAxisAlignment.Center,
                        mainAxisSize: MainAxisSize.Min,
                        spacing: titleBtnSpacing,
                        children: trailingGroup.ToArray()),
                    new Vector2(0f, chromeTopNudge)),
            });

        // Two exactly-sized SizedBoxes: an outer W × bandH band and an inner TitleBtnsW × contentBoxH box bottom-centered via Align(BottomCenter)
        // (Padding/ConstrainedBox can't center horizontally and would collapse left). The outer height is bounded so the Align fills only bandH.
        // Both grow by extraLines * titleLineH so the inner box's top edge (and the rows below) never moves; only the bottom extends.
        float titleLineH = TitleLineHeight;
        int extraLines = actualTitleLines - 1;
        float contentBoxH = layout.TitleBtnsH + extraLines * titleLineH;
        float bandH = layout.TitleBarH + extraLines * titleLineH;

        // Paint-only downward nudge (1.2% of H) for the whole title section; Transform.Translate leaves bandH/contentBoxH, and the rows below, untouched.
        const float TitleSectionDownNudgeFrac = 0.012f;
        float titleSectionDownNudge = TitleSectionDownNudgeFrac * layout.H;

        return new SizedBox(
            width: layout.W,
            height: bandH,
            child: new Align(
                Alignment.BottomCenter,
                child: new SizedBox(
                    width: layout.TitleBtnsW,
                    height: contentBoxH,
                    // Opaque panel behind the title row when Pixel-Art is OFF. The left inset is 0.02·W because the leading grip occupies that space.
                    child: Transform.Translate(
                        FlatPanel(new Padding(
                            EdgeInsets.Only(left: 0.02f * layout.W, right: 0.04f * layout.W),
                            child: titleRow)),
                        new Vector2(0f, titleSectionDownNudge)))));
    }

    /// <summary>Greedy word-wrap line count for a single-paragraph title, mirroring ScribeMultilineField's wrap (LibGUI's is internal). Only sizes the band this frame, so <paramref name="maxWidth"/> is an estimate.</summary>
    private static int CountWrappedLines(string text, string fontFamily, float fontSize, FontWeight weight, float maxWidth, int cap)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f || cap <= 1) return 1;
        int lines = 1;
        var current = new System.Text.StringBuilder();
        foreach (var word in text.Split(' '))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            float w = TextLayoutHelper.MeasureText(candidate, fontFamily, fontSize, weight).X;
            if (w <= maxWidth || current.Length == 0)
            {
                current.Clear();
                current.Append(candidate);
            }
            else
            {
                lines++;
                if (lines >= cap) return cap;
                current.Clear();
                current.Append(word);
            }
        }
        return lines;
    }

    /// <summary>The resting (non-editing) title widget: a RichText wrapping to <see cref="TitleMaxLines"/> with ellipsis overflow. The tablet overrides it to render display-only cuneiform (its cuneiform-OFF path returns here).
    /// <paramref name="displayTitle"/> has the host default already substituted for "Untitled"; <paramref name="titleStyle"/> carries the resolved size/family/weight/color so an override can match band metrics.</summary>
    private protected virtual Widget BuildTitleDisplay(string displayTitle, TextStyle titleStyle) =>
        new RichText(new TextSpan(displayTitle), titleStyle, maxLines: TitleMaxLines, overflow: TextOverflow.Ellipsis);

    /// <summary>Maximum title lines before clipping (default 2). <see cref="BuildTitleBar"/> grows the title slot to this many line-heights; a one-line title lays out identically to the single-line layout.</summary>
    private protected virtual int TitleMaxLines => 2;

    /// <summary>The active (editing) title widget: a live input bound to <see cref="_titleController"/> and <see cref="_titleFocusNode"/>. Default is the stock single-line TextField with maxlength and Enter/Escape commit wired here, so <see cref="CommitTitleIfEditing"/> is untouched by an override (the tablet uses a cuneiform input on the same controller/focus node).</summary>
    private protected virtual Widget BuildTitleField(TextStyle titleStyle)
    {
        // Height matches the title's line height instead of TextFieldStyle's fixed 40px, which would grow the band the moment editing starts. The stock TextField's 10px horizontal inset is baked into RenderTextField and can't be zeroed without forking `gui`.
        var metrics = TextLayoutHelper.GetFont(titleStyle.FontFamily, titleStyle.FontSize, titleStyle.Weight).Metrics;
        float lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        return new TextField(
            _titleController!,
            _titleFocusNode!,
            new TextFieldStyle { FillColor = new Vector4(0, 0, 0, 0), BorderThickness = 0, Height = lineHeight, TextStyle = titleStyle },
            onKeyDown: OnTitleFieldKeyDown);
    }

    /// <summary>Shared title-input key handling (default TextField and the tablet's cuneiform input): blocks typing past <see cref="ScribeDocument.MaxTitleLength"/> (caret/delete keys pass) and commits + rebuilds on Enter/Escape.</summary>
    private protected void OnTitleFieldKeyDown(KeyboardEvent e)
    {
        if (_titleController!.Text.Length >= ScribeDocument.MaxTitleLength
            && !e.Ctrl && e.KeyCode is not ((int)GlKeys.BackSpace or (int)GlKeys.Delete
                or (int)GlKeys.Left or (int)GlKeys.Right or (int)GlKeys.Home or (int)GlKeys.End))
            e.Handled = true;
        if (e.KeyCode is (int)GlKeys.Enter or (int)GlKeys.KeypadEnter or (int)GlKeys.Escape)
        {
            CommitTitleIfEditing();
            // Swap back to display via an in-place reconcile: RebuildBody dirties the body for the next frame, avoiding the mid-dispatch orphan-button NPE a synchronous ForceRebuild would hit (see _pendingTitleEditRebuild) and keeping a focused row's caret.
            RebuildBody();
            e.Handled = true;
        }
    }

    // ---------------- Title-bar grip drag ----------------
    // A press on the grip is captured by the tooltip's MouseRegion before GuiBase's band drag fires, so GuiBase's band-drag math is reproduced here:
    // capture mouse + window position on press, then convert the raw-pixel mouse delta to logical pixels via GUIScale into WindowPos.
    // GestureDetector holds pointer capture, so moves keep arriving after the cursor leaves the glyph.

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
        // OnRenderGUI syncs ScreenOffset from WindowPos and clamps it on-screen each frame, so no relayout is needed here.
    }

    private void OnGripDragEnd(PointerEvent e)
    {
        if (!gripDragging) return;
        gripDragging = false;
        // Persist under the same dialog key GuiBase's band drag uses so the window reopens where left.
        capi.Gui.SetDialogPosition(DialogCode, new Vec2i((int)WindowPos.X, (int)WindowPos.Y));
    }

    /// <summary>Re-dispatches a synthetic pointer-move at the cursor so LibGUI re-runs hit-testing. Called each frame while a list collapse animates: LibGUI only recomputes hover on real mouse motion, so a row sliding under a still cursor would keep stale hover (delete/pin hidden). Uses the same raw→window-local conversion as the grip drag.</summary>
    private void RefreshHoverAtCursor()
    {
        if (RootElement?.RenderObject == null) return;
        var local = ScribeHoverRefresh.ToWindowLocal(
            capi.Input.MouseX, capi.Input.MouseY, GetUiScale(), WindowPos);
        EventDispatcher.DispatchPointerMove(RootElement, new PointerEvent(local.X, local.Y));
    }

    /// <summary>The SectionInnerBox (<c>0.9W × 0.8H</c>, centered): left spacer, center tasks column, right nav column. The three widths sum to <see cref="ScribeLayout.InnerW"/> exactly.</summary>
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
                    // Flat panel only behind the tasks column (and title row) when Pixel-Art is OFF.
                    new SizedBox(width: layout.TasksColW, child: FlatPanel(BuildCentralRegion())), // LecternTasksBox
                    new SizedBox(width: layout.SideColW, child: BuildRightColNav()),   // SectionRightCol
                }));

    /// <summary>SectionRightCol: the vertical stack of tooltipped nav buttons (Read, Edit, Pinned, Settings last). <c>protected virtual</c> so a subclass may replace the whole column (the tablet returns an empty one that keeps the <c>SideColW</c> side margin).</summary>
    protected virtual Widget BuildRightColNav()
    {
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        // Nav buttons are RowCheckboxSize × 1.7; ScribeRowButton derives both box and glyph size from this one value.
        float size = ScribeRowConstants.RowCheckboxSize * 1.7f;

        // Resting/inactive tint via the NavIconColor seam; the active tab uses its own activeColor.
        Vector4 navColor = NavIconColor(colors);

        // Another player's editor lock dims the Edit button; TryEnterEditor enforces the no-entry, this only styles it.
        bool editLockedByOther = host.IsLockedByOther(capi.World.Player.PlayerUID);

        // Soft drop shadow so the enlarged buttons read as raised chrome over the art.
        var navShadow = new[]
        {
            new BoxShadow(
                Color: new Vector4(0f, 0f, 0f, 0.35f),
                Offset: new Vector2(2f, 2f),
                BlurRadius: 4f),
        };

        // Extra subclass buttons go between Pins and Settings; Settings is always last.
        Widget readBtn = TitleButton("scribecheck", "scribe-gui-nav-read", navColor,
            size: size, onTap: EnterReadMode, boxShadows: navShadow,
            activeColor: viewMode == ScribeLecternView.Read ? ScribeRowConstants.NavActiveRead : null);
        Widget editBtn = TitleButton("scribeedit", "scribe-gui-nav-edit",
            editLockedByOther ? navColor with { W = 0.4f } : navColor,
            size: size, onTap: TryEnterEditor, boxShadows: navShadow,
            activeColor: viewMode == ScribeLecternView.Editor ? ScribeRowConstants.NavActiveEdit : null);
        Widget pinBtn = TitleButton("scribepin", "scribe-gui-nav-pinned", navColor,
            size: size, onTap: OnClickSwitchToPinned, iconScale: 1.15f, boxShadows: navShadow,
            activeColor: viewMode == ScribeLecternView.Pinned ? ScribeRowConstants.NavActivePinned : null);
        Widget settingsBtn = TitleButton("scribegear", "scribe-gui-nav-settings", navColor,
            size: size, onTap: modSystem.OpenSettings, boxShadows: navShadow,
            activeColor: modSystem.IsSettingsOpen ? ScribeRowConstants.NavActiveSettings : null);

        var navChildren = GetLeadingNavButtons()
            .Concat(new Widget[] { readBtn, editBtn, pinBtn })
            .Concat(GetExtraNavButtons())
            .Append(settingsBtn)
            .ToArray();

        // Placement is resolved through the NavButtonAlignment seam from the column width and the drawn nav-button box width (size minus ScribeRowButton.BoxShrink).
        float sideColW = host.GetLayout(modSystem.MySettings.PixelArtSize).SideColW;
        float navBoxW = size - ScribeRowButton.BoxShrink;
        CrossAxisAlignment navAlign = NavButtonAlignment(sideColW, navBoxW);

        return new Column(
            spacing: 16,
            mainAxisAlignment: MainAxisAlignment.Start,
            crossAxisAlignment: navAlign,
            mainAxisSize: MainAxisSize.Max,
            children: navChildren);
    }

    /// <summary>A tooltipped icon button using the per-row button chrome. <paramref name="iconScale"/> grows only the glyph; <paramref name="boxShadows"/> adds a drop shadow; <paramref name="shimmer"/> wraps it in the Inbox nav shimmer sweep (a costless pass-through when false).
    /// Protected so subclasses can build matching buttons in <see cref="GetExtraNavButtons"/>.</summary>
    protected Widget TitleButton(string iconName, string tooltipKey, Vector4 color, float size, Action onTap, float iconScale = 1f, BoxShadow[]? boxShadows = null, Vector4? activeColor = null, bool shimmer = false) =>
        WithTooltip(tooltipKey, new ScribeShimmerWrap(shimmer, size,
            new ScribeRowButton(iconName: iconName, iconColor: color, size: size, onTap: onTap, iconScale: iconScale, boxShadows: boxShadows, activeColor: activeColor)));

    /// <summary>Wraps a button in a localized hover tooltip (<c>scribe:&lt;key&gt;</c>) in the global overlay so surrounding boxes don't clip it. Content text uses the theme's <c>OnBackground</c>, the partner of the bubble's <c>Background</c>; without an explicit color it would default to white and wash out on the light bubble.</summary>
    protected Widget WithTooltip(string key, Widget child, params object[] args)
    {
        var theme = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay);
        // Tooltips render in the global Overlay, OUTSIDE the body's ScribeGlobalTint, so ShadedTooltip applies the live shade (at reduced hover strength) to bubble and content; otherwise they stick out when the body is dimmed.
        // Scribe Settings is excluded by construction: it builds a bare WindowFrame with no ScribeGlobalTint and its help tooltips don't route through here; don't "unify" the two without preserving that.
        return ScribeGlobalTint.ShadedTooltip(
            child: child,
            content: new Padding(
                EdgeInsets.All(6),
                child: new Text(Lang.Get("scribe:" + key, args), new TextStyle
                {
                    FontSize = 13,
                    SoftWrap = true,
                    Color = theme.ColorScheme.OnBackground,
                })),
            baseTheme: theme,
            shade: currentShade);
    }

    /// <summary>The tasks column's content. <c>protected virtual</c> so a subclass can supply its own single view (the tablet adds a cuneiform title banner over the editable list); the default routes on <see cref="viewMode"/>.</summary>
    protected virtual Widget BuildCentralRegion() => viewMode switch
    {
        ScribeLecternView.Editor   => BuildEditorContent(),
        ScribeLecternView.Pinned   => BuildPinnedContent(),
        ScribeLecternView.Visitors => BuildVisitorsContent(),
        ScribeLecternView.History  => BuildHistoryContent(),
        ScribeLecternView.Timer    => BuildTimerContent(),
        ScribeLecternView.Inventory => BuildInventoryContent(),
        ScribeLecternView.Assignment => BuildAssignmentContent(),
        ScribeLecternView.Inbox     => BuildInboxContent(),
        ScribeLecternView.SentHistory => BuildSentAssignmentHistoryContent(),
        ScribeLecternView.InboxInventory => BuildInboxInventoryContent(),
        _                          => BuildReadContent(),
    };

    /// <summary>The live row style, derived from current settings each build (not cached at open) so a font-scale change repaints the open dialog. Passes through <see cref="DecorateRowStyle"/> for tier-specific behavior.</summary>
    private protected ScribeRowStyle RowStyle => DecorateRowStyle(ScribeRowStyle.FromSettings(modSystem.MySettings)
        // SubtaskIndent depends on window width, so it is layered on here (10px + 3%·W) before DecorateRowStyle so subclass `with` tweaks still compose.
        // Focus-border and tick colors come from their seams so every editable row shares the guestbook field's resolved colors.
        with
        {
            SubtaskIndent = 10f + 0.03f * host.GetLayout(modSystem.MySettings.PixelArtSize).W,
            InputFocusBorderColor = InputFocusBorderColor(ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme),
            CheckTickColor = CheckTickColor(ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme),
        });

    /// <summary>Hook to adjust the settings-derived row style for this dialog tier; the tablet sets UseCuneiform + the glyph bundle here.</summary>
    private protected virtual ScribeRowStyle DecorateRowStyle(ScribeRowStyle style) => style;

    /// <summary>Action for the editor footer's Settings gear, or null to omit it. Null by default (Lectern/Notebook use the nav column); the tablet, with no nav column, overrides it to <c>modSystem.OpenSettings</c>.</summary>
    private protected virtual Action? EditorSettingsGearAction => null;

    /// <summary>Lang key for the empty-document hint in Read and Edit views; Notebook subclasses override it.</summary>
    protected virtual string EmptyHintLangKey => "scribe:scribe-gui-edit-hint";

    /// <summary>The document's display title as the title bar renders it (the codec default "Untitled" mapped to the host default). Never blank; lets a subclass banner show exactly the bar's title.</summary>
    protected string DisplayDocumentTitle
    {
        get
        {
            var raw = scratch?.Title ?? host.Document.Title;
            return (raw == ScribeDocument.DefaultTitle ? null : raw) ?? host.DefaultDocumentTitle;
        }
    }

    /// <summary>Whether the read view is permanently read-only: the "switch to editor" button is dropped and TEXT editing is blocked (hard/fired tablet). Does not make the checkbox/pin inert; that is <see cref="ReadViewCompletionAndPinLive"/>.</summary>
    private protected virtual bool ReadViewIsReadOnly => false;

    /// <summary>Whether a read-only read view keeps its checkbox and pin interactive. The tablet overrides it true so a hard/fired tablet can still complete and unpin (a fired tablet's pin is never stranded on the HUD).</summary>
    private protected virtual bool ReadViewCompletionAndPinLive => false;

    /// <summary>Invoked when the player taps a locked row's text on a read-only tablet, so the surface can raise its "cannot be changed" message. Null elsewhere.</summary>
    private protected virtual Action<Guid>? ReadViewTextEditRefused => null;

    /// <summary>Whether the Read View renders the filter-pill row and subtask-group collapse toggles (gated together). True by default; only <see cref="GuiDialogScribeTablet"/> turns it off.</summary>
    private protected virtual bool SupportsFilterPills => true;

    /// <summary>Whether the Read View / Editor renders the shared Row 2 subtitle + Row 3 tab header. True by default; only <see cref="GuiDialogScribeTablet"/> turns it off.</summary>
    private protected virtual bool SupportsTabHeader => true;

    /// <summary>Whether EDITOR rows make their Link/Tracker/Craft name label click-to-open-Handbook. False by default (surfaces with a read view activate links there); the tablet, with no read/edit split, overrides it true.</summary>
    protected virtual bool EditorRowsOpenLinks => false;

    /// <summary>Resolves a block's Tracker/Link item icon + display name for a row snapshot, or <c>(null, null)</c> for Task/Text blocks. Core stores the codes API-free, so the parse against live registries happens here.
    /// Protected: takes any block, e.g. <see cref="GuiDialogScribeAssignmentDesk"/> resolves rows from a STAGED item's document.</summary>
    protected (ItemStack? Stack, string? Name) ResolveRowItem(ScribeBlock b)
    {
        if (b.IsQuestObjective)
        {
            // An objective's match code is in LinkTarget (a raw key, never a scheme Link target), so it bypasses ResolveDisplay's scheme dispatch. Item-backed resolves like a Tracker; label-only falls back to LinkLabel (else the raw code) with a generic icon from a null Stack.
            return b.TargetItemCode is { } objItemCode
                ? ScribeItemRef.ResolveDisplay(capi.World, objItemCode, null)
                : (null, b.LinkLabel ?? b.LinkTarget);
        }
        if (!b.IsTracker && !b.IsLink && !b.IsCraft) return (null, null);
        // A Link resolves LinkTarget; a Tracker and a Craft parent (whose TargetItemCode is its recipe OUTPUT) resolve the item they count.
        string? code = b.IsLink ? b.LinkTarget : b.TargetItemCode;
        return ScribeItemRef.ResolveDisplay(capi.World, code, b.LinkLabel);
    }

    /// <summary>Resolves a tapped item row to its Handbook page and opens it; shared by the read view and (when <see cref="EditorRowsOpenLinks"/> is on) the editor so all paths resolve identically.
    /// Link opens LinkTarget; Tracker, Craft parent and item-backed QuestObjective open TargetItemCode; anything else (plain Task/Note, label-only objective) no-ops. Keyed by TaskId off the live document.</summary>
    private void OpenRowLink(Guid taskId)
    {
        var block = host.Document.FindByTaskId(taskId);
        if (block?.IsLink == true) ScribeItemRef.OpenHandbookPage(capi, block.LinkTarget);
        else if (block?.IsTracker == true || block?.IsCraft == true || block?.IsQuestObjective == true)
            ScribeItemRef.OpenHandbookPage(capi, block.TargetItemCode);
    }

    /// <summary>Builds the read view. Not private so the always-edit tablet can render it directly for a non-editable stack (it has no <see cref="viewMode"/> switching).</summary>
    protected Widget BuildReadContent() =>
        new ScribeReadContent(
            // Value-copy snapshot of the block list (never live block refs) so later document mutation can't alias into a built row. Pinned is a per-player query, so each row carries its TaskId.
            // An empty Task is filtered below (the editor's blur-removal/purge should already keep them out of the document); the toggle addresses tasks by TaskId, so dropping rows doesn't misalign anything.
            blocks: host.Document.Blocks
                .Select((b, i) =>
                {
                    var (stack, name) = ResolveRowItem(b);
                    // A quest Link's live progress text; null for other row kinds or when the watcher has nothing yet.
                    string? questProgress = ScribeLinkTarget.QuestCode(b.LinkTarget) is { } questCode
                        ? modSystem.TryGetQuestProgressText(ScribeLinkTarget.QuestSource(b.LinkTarget)!, questCode)
                        : null;
                    var (assignerName, assignedDate, acceptedDate) = ResolveAssignmentTooltipInfo(b.Assignment);
                    // A label-only QuestObjective (null stack) has a bare match code as LinkTarget, which ScribeLinkIcon's scheme dispatch can't route; a synthetic page-scheme string reaches the guide-page book-glyph fallback. Display-only: OpenRowLink re-reads the live block by TaskId.
                    bool isStaticVsQuestObjective = b.IsQuestObjective
                        && ScribeQuestCatalog.IsStaticObjectiveCode(b.LinkTarget);
                    string? iconLinkTarget = b.IsQuestObjective && stack is null && !isStaticVsQuestObjective
                        ? ScribeLinkTarget.ForPage(b.LinkTarget ?? "")
                        : b.LinkTarget;
                    return new ScribeReadRowData(
                        Index: i, Kind: b.Kind, Done: b.Done, Pinned: IsPinnedForMe(b.TaskId), TaskId: b.TaskId,
                        Text: b.Text, DisplayStack: stack, DisplayName: name,
                        TargetQuantity: b.TargetQuantity, CurrentQuantity: b.CurrentQuantity, LinkTarget: iconLinkTarget,
                        Depth: b.Depth,
                        IsAcceptedAssignment: b.Assignment?.State == ScribeAssignmentState.Accepted,
                        ExtraInfo: b.ExtraInfo,
                        QuestProgressText: questProgress,
                        AssignerName: assignerName, AssignedDate: assignedDate, AcceptedDate: acceptedDate,
                        IsStaticVsQuestObjective: isStaticVsQuestObjective);
                })
                // Drop only empty-text Tasks; notes may be empty and Tracker/Link rows carry no text of their own.
                .Where(r => !r.IsTask || !string.IsNullOrWhiteSpace(r.Text))
                .ToList(),
            onToggleTask: OnReadViewCompleteTask,
            onTogglePinned: OnReadViewTogglePinned,
            // A Link row's name is a hyperlink to its Handbook page (never touches completion); Tracker and Craft rows open their target/output item's page. Shared with the editor via OpenRowLink.
            onOpenLink: OpenRowLink,
            onSwitchToEditor: TryEnterEditor,
            footerButtonPadding: EdgeInsets.Symmetric(
                horizontal: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W),
            style: RowStyle,
            scrollController: sharedScrollController,
            // Host-owned so the Delete-policy collapse motion survives the RefreshReadView reconcile and OnRenderGUI can read AnyAnimating to pin scroll + refresh hover.
            collapseRegistry: readCollapseRegistry,
            // A departing row finished collapsing: re-clamp the shorter scroll extent.
            onDepartureSettled: RequestClampToExtent,
            currentShade: currentShade,
            hintLangKey: EmptyHintLangKey,
            readOnly: ReadViewIsReadOnly,
            completionAndPinLive: ReadViewCompletionAndPinLive,
            onTextEditRefused: ReadViewTextEditRefused,
            assignedStampBitmap: modSystem.GetGuiTextureBitmap(ScribeAssignedTaskIcon.Asset),
            externalStampBitmap: modSystem.GetGuiTextureBitmap(ScribeExternalInfoIcon.Asset),
            supportsFilterPills: SupportsFilterPills,
            supportsTabHeader: SupportsTabHeader,
            showSubtitleRow: modSystem.VisualTuning.ShowSubtitleRow,
            activeFilterCategory: readViewFilterCategory,
            onFilterCategoryChanged: OnReadViewFilterCategoryChanged,
            isGroupCollapsed: collapsedReadViewGroupIds.Contains,
            onToggleGroupCollapsed: OnReadViewToggleGroupCollapsed);

    /// <summary>The editable task list for the current scratch document. Protected so a subclass can reuse the editor (the tablet stacks it under a cuneiform title banner); bound to the same scratch, focus nodes and handlers.</summary>
    protected Widget BuildEditorContent()
    {
        var blocks = scratch!.Blocks
            .Select((b, i) =>
            {
                var (stack, name) = ResolveRowItem(b);
                // Leading-icon marker only: an accepted assignment's text is not ReadOnly here because ScribeMultilineField wiring (focus-node indexing, tablet cuneiform, jump-navigation) has no read-only branch yet.
                bool isAcceptedAssignment = b.Assignment?.State == ScribeAssignmentState.Accepted;
                var (assignerName, assignedDate, acceptedDate) = ResolveAssignmentTooltipInfo(b.Assignment);
                // Same synthetic page-scheme substitution as BuildReadContent. Display-only.
                bool isStaticVsQuestObjective = b.IsQuestObjective
                    && ScribeQuestCatalog.IsStaticObjectiveCode(b.LinkTarget);
                string? iconLinkTarget = b.IsQuestObjective && stack is null && !isStaticVsQuestObjective
                    ? ScribeLinkTarget.ForPage(b.LinkTarget ?? "")
                    : b.LinkTarget;
                return new ScribeEditRowData(
                    Index: i, Kind: b.Kind, Done: b.Done, Pinned: IsPinnedForMe(b.TaskId), TaskId: b.TaskId,
                    Text: b.Text, DisplayStack: stack, DisplayName: name,
                    TargetQuantity: b.TargetQuantity, CurrentQuantity: b.CurrentQuantity, LinkTarget: iconLinkTarget,
                    Depth: b.Depth, IsAcceptedAssignment: isAcceptedAssignment, ExtraInfo: b.ExtraInfo,
                    AssignerName: assignerName, AssignedDate: assignedDate, AcceptedDate: acceptedDate,
                    IsStaticVsQuestObjective: isStaticVsQuestObjective);
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
            // The inline stepper edits TargetQuantity in scratch; the normal editor flush persists it.
            onTrackerQuantityChanged: SetEditorTrackerTargetQuantity,
            // A grip tap toggles subtask depth 0↔1; press-hold-drag still reorders.
            onGripTap: OnGripTap,
            // Drag-reorder follows the moved row into view; only a Sink completion passes anchorViewport: true.
            onReorderBlock: (from, to) => ReorderEditorBlock(from, to),
            onAdd: OnClickAdd,
            // An empty catalog hides the Quest Link option (no vsquest, no quests, or none started this session).
            questCatalog: StartedQuestCatalogForPicker,
            onAddQuestLink: OnClickAddQuestLink,
            onSwitchToRead: OnClickSwitchToRead,
            onOpenEditorReference: ToggleEditorReferenceHandbook,
            // Footer gear is tablet-only: the base returns null since the Lectern/Notebook reach Settings via the nav column.
            onOpenSettings: EditorSettingsGearAction,
            footerButtonPadding: EdgeInsets.Symmetric(
                horizontal: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W),
            style: RowStyle,
            scrollController: sharedScrollController,
            collapseRegistry: editorCollapseRegistry,
            // A departing row finished collapsing: re-clamp the shorter scroll extent.
            onDepartureSettled: RequestClampToExtent,
            // Lets the footer add-kind picker tint its drop-up menu, which paints in the Overlay outside this body's ScribeGlobalTint.
            currentShade: currentShade,
            hintLangKey: EmptyHintLangKey,
            // Dims the add-picker at a finite tier's 10-entry cap; buttons stay clickable so the tap surfaces NotifyTabletFull.
            addTaskEnabled: CanAddTaskUnderPolicy(),
            // The always-edit tablet has no Read view, so it overrides this to hide the "Done editing" button.
            showSwitchToRead: ShowEditorSwitchToRead,
            // Click-to-open-Handbook on item-row names, same dispatch as the read view; only the tablet (no read view) enables it, elsewhere null keeps names plain editable regions.
            onOpenLink: EditorRowsOpenLinks ? OpenRowLink : null,
            supportsTabHeader: SupportsTabHeader,
            showSubtitleRow: modSystem.VisualTuning.ShowSubtitleRow,
            assignedStampBitmap: modSystem.GetGuiTextureBitmap(ScribeAssignedTaskIcon.Asset),
            externalStampBitmap: modSystem.GetGuiTextureBitmap(ScribeExternalInfoIcon.Asset));
    }

    /// <summary>Whether the editor footer shows the "Done editing" button. False on the always-edit tablet, which has no Read view and where leaving editor mode would null the scratch the central region reads.</summary>
    protected virtual bool ShowEditorSwitchToRead => true;

    /// <summary>Footer "Editor Features" (ⓘ) button: toggles the "Scribe Editor Features" handbook page ("focus, don't hide"): opens it if no handbook dialog is open, otherwise closes the open one (re-firing the link protocol on an open handbook would only navigate it; a page-aware close would need the dialog's private state, see VSAPI-NOTES.md).
    /// The open handbook is found by scanning public <c>capi.Gui.OpenedGuis</c> for ToggleKeyCombinationCode == "handbook", so there is no survival-mod reference or reflection; without that mod both paths are safe no-ops.</summary>
    private void ToggleEditorReferenceHandbook() => ToggleHandbookPage("craftinginfo-scribe-editor-reference");

    /// <summary>The "focus, don't hide" handbook toggle behind <see cref="ToggleEditorReferenceHandbook"/> and the Scriptorium's Transcribe-features button: opens guide <paramref name="pageCode"/> if no handbook is open, else closes the open one. Decoupled from the survival mod as documented there.</summary>
    protected void ToggleHandbookPage(string pageCode)
    {
        // Find an open handbook by its public toggle-hotkey code, not its concrete type (reflection-free).
        GuiDialog? openHandbook = capi.Gui.OpenedGuis
            .FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook");

        if (openHandbook != null)
        {
            // TryClose runs the dialog's normal OnGuiClosed path (close sound/animation).
            openHandbook.TryClose();
            return;
        }

        // Closed ⇒ open to the requested page via the registered link protocol. Absent survival mod ⇒ no "handbook" protocol ⇒ graceful no-op.
        if (capi.LinkProtocols.TryGetValue("handbook", out var open))
            open(new LinkTextComponent("handbook://" + pageCode));
    }

    /// <summary>Read-view checkbox click: completes the task by TaskId via the lock-free <see cref="ScribeCompleteTaskMessage"/> (no editor lock held). A pinned task completes server-side store-first under the player's policy; otherwise it toggles the shared done flag, like the HUD.</summary>
    private void OnReadViewCompleteTask(Guid taskId)
    {
        var policy = modSystem.MySettings.CompletionPolicy;

        // Optimistic-then-confirm: apply the completion to a local copy and refresh immediately, since an unpinned completion has no pin push to ride; the authoritative resync supersedes it.
        // Skipped on a permanently read-only source (hard/fired tablet): the server collapses mutating policies to Unpin there, so predicting a delete/sink would flash a removal the server refuses.
        if (!ReadViewIsReadOnly)
        {
            // Codec round-trip copy so a client prediction never mutates the authoritative document.
            var bytes = ScribeDocumentCodec.Serialize(host.Document);
            if (ScribeDocumentCodec.TryDeserialize(bytes, out var copy) && copy is not null)
            {
                var outcome = ScribeCompletion.ApplyLocal(copy, taskId, policy, modSystem.MySettings.SubtaskBehavior);
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
            SubtaskBehavior = (byte)modSystem.MySettings.SubtaskBehavior,
        });
    }

    /// <summary>Read-view pin toggle by TaskId, via the same lock-free <see cref="SendSetPin"/> path as the editor row. Scroll preservation happens in <see cref="OnMyPinsChanged"/> just before the rebuild; capturing earlier would let the restore loop expire before the async callback.</summary>
    private void OnReadViewTogglePinned(Guid taskId)
        // Honors the tablet's 1-pin cap with the editor's seamless swap.
        => TogglePinWithPolicy(taskId);
}

/// <summary>The shared Row 2 (subtitle) + Row 3 (per-tab controls) + divider header every non-tablet <see cref="ScribeDialogBase"/> tab renders above its content. One helper keeps the divider spacing (8 above/4 below) from drifting across call sites. A static class because several callers are plain widgets, not dialog subclasses.</summary>
internal static class ScribeTabHeader
{
    /// <summary>Builds Row 2 + optional Row 3 + the durable divider. <paramref name="row3Content"/> is the tab's own controls, omitted when a tab has none; <paramref name="hasLeadingDivider"/> adds a divider directly above it (Guest Book only).
    ///
    /// <para>The divider sits flush against the following content with no trailing gap; the caller owns the 4-unit padding and MUST place it INSIDE its own scrollable content (see <see cref="ScribeReadContent"/>) so it scrolls away.</para>
    ///
    /// <para>Small caps is approximated (TextStyle has no font-feature field): each word's first letter at full cap size, the rest at a reduced size. <c>RenderRichText</c> TOP-aligns mixed-size runs rather than baseline-aligning them (confirmed against shipped 3.1.0 Gui.dll), so every run is wrapped in a <see cref="WidgetSpan"/> forced to the tallest line height with top padding shifting its baseline to the shared reference; see <c>BaselineRun</c>.</para>
    ///
    /// <para>The descriptor uses the real italic Caudex cut registered by <c>RegisterCustomFonts</c> under <see cref="FontWeight.Italic"/>.</para>
    ///
    /// <para>Dividers become an invisible SizedBox under <see cref="ScribeRowStyle.UseCuneiform"/> (a hard rule reads wrong on clay/wax). The tablet renders neither row; callers gate the whole call on their own SupportsTabHeader-equivalent flag.</para></summary>
    internal static Widget Build(
        ColorScheme colors,
        ScribeRowStyle style,
        string labelLangKey,
        string descriptorLangKey,
        bool showSubtitleRow,
        Widget? row3Content = null,
        bool hasLeadingDivider = false)
    {
        var capStyle = new TextStyle
        {
            FontFamily = ScribeRowControlNudge.TitleFontFamily,
            FontSize = style.FontSize * 0.82f,
            Weight = FontWeight.Bold,
            Color = colors.OnSurfaceVariant,
        };
        // ~82.5% of the cap size reads as small caps.
        var smallCapStyle = capStyle with { FontSize = capStyle.FontSize * 0.825f };
        var descriptorStyle = new TextStyle
        {
            FontFamily = ScribeRowControlNudge.TitleFontFamily,
            FontSize = style.FontSize * 0.95f,
            Weight = FontWeight.Italic,
            Color = colors.OnSurfaceVariant,
        };

        var capFont = TextLayoutHelper.GetFont(capStyle.FontFamily, capStyle.FontSize, capStyle.Weight);
        var smallCapFont = TextLayoutHelper.GetFont(smallCapStyle.FontFamily, smallCapStyle.FontSize, smallCapStyle.Weight);
        var descriptorFont = TextLayoutHelper.GetFont(descriptorStyle.FontFamily, descriptorStyle.FontSize, descriptorStyle.Weight);

        static float Ascent(SkiaSharp.SKFont font) => -font.Metrics.Ascent;
        static float LineHeight(SkiaSharp.SKFont font) => font.Metrics.Descent - font.Metrics.Ascent + font.Metrics.Leading;

        float refAscent = System.Math.Max(Ascent(capFont), System.Math.Max(Ascent(smallCapFont), Ascent(descriptorFont)));
        float refLineHeight = System.Math.Max(LineHeight(capFont), System.Math.Max(LineHeight(smallCapFont), LineHeight(descriptorFont)));

        // Boxes every run to refLineHeight and top-pads it so its baseline lands at the shared refAscent.
        InlineSpan BaselineRun(string text, TextStyle textStyle, SkiaSharp.SKFont font) =>
            new WidgetSpan(new SizedBox(height: refLineHeight, child: new Padding(
                EdgeInsets.Only(top: refAscent - Ascent(font)),
                child: new Text(text, textStyle))));

        var spaceStyle = new SpanStyle
        {
            FontFamily = capStyle.FontFamily, FontSize = capStyle.FontSize, Weight = capStyle.Weight, Color = capStyle.Color,
        };

        string label = Lang.Get(labelLangKey).ToUpperInvariant();
        var labelSpans = new List<InlineSpan>();
        var words = label.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (word.Length == 0) continue;
            if (labelSpans.Count > 0) labelSpans.Add(new TextSpan(" ", spaceStyle));
            labelSpans.Add(BaselineRun(word[..1], capStyle, capFont));
            if (word.Length > 1) labelSpans.Add(BaselineRun(word[1..], smallCapStyle, smallCapFont));
        }
        labelSpans.Add(BaselineRun(":  ", smallCapStyle, smallCapFont));
        labelSpans.Add(BaselineRun(Lang.Get(descriptorLangKey), descriptorStyle, descriptorFont));

        Widget subtitle = new RichText(new TextSpan(children: labelSpans.ToArray()));

        Widget NewDivider() => style.UseCuneiform ? new SizedBox() : new Divider();

        var children = new List<Widget>();
        if (showSubtitleRow) children.Add(subtitle);
        if (hasLeadingDivider)
            children.Add(new Padding(EdgeInsets.Only(top: 8f), child: NewDivider()));
        if (row3Content is not null)
            children.Add(new Padding(EdgeInsets.Only(top: 8f), child: row3Content));
        children.Add(new Padding(EdgeInsets.Only(top: 8f), child: NewDivider()));

        return new Column(
            spacing: 0,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: children);
    }
}
