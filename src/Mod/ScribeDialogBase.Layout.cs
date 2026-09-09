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

    /// <summary>Tint color for the INACTIVE (non-current-tab) right-column nav glyphs (read/edit/pin/settings
    /// and any subclass extras like the chalkboard's guestbook). <c>private protected virtual</c> so a subclass
    /// can restyle them without forking <see cref="BuildRightColNav"/>. Default is the theme's mid-gray
    /// <c>OnSurfaceVariant</c> — the same role the muted body text uses — so the incumbents are unchanged. The
    /// chalkboard overrides it to a darker slate-brown so the pale chalk-gray inactive icons don't read as
    /// almost-active on the dark board, WITHOUT dragging muted body text down with them (which is why this is a
    /// dedicated seam and not a change to the shared <c>OnSurfaceVariant</c> role). The ACTIVE tab keeps its own
    /// per-view <c>activeColor</c>; only the resting/inactive tint routes through here.</summary>
    private protected virtual Vector4 NavIconColor(ColorScheme colors) => colors.OnSurfaceVariant;

    /// <summary>Border color for a FOCUSED editable field (task rows and the guestbook note field).
    /// <c>private protected virtual</c> so a subclass can restyle it without forking the field. Default is the
    /// theme's <c>Primary</c> accent — the focus outline every light surface has always used. The chalkboard
    /// overrides it to a chalk-white (<see cref="ScribeTheme.ChalkboardInputFocusBorder"/>) because its
    /// <c>Primary</c> is a forest green the author disliked on an input border; this is a dedicated seam (not a
    /// change to <c>Primary</c>) so the accent still fills buttons in green while inputs outline in chalk. Both
    /// the row path (seeded onto <see cref="ScribeRowStyle.InputFocusBorderColor"/> in <c>RowStyle</c>) and the
    /// guestbook field consume this single seam so they can't drift apart.</summary>
    private protected virtual Vector4 InputFocusBorderColor(ColorScheme colors) => colors.Primary;

    /// <summary>Override for the task-row completion checkbox's TICK color (the checkmark), or <c>null</c> to
    /// keep the ambient theme's checkbox tick (its <c>CheckColor</c> ← <c>Primary</c>). <c>private protected
    /// virtual</c> so a subclass can retint just the tick without forking the row widget. Default is
    /// <c>null</c> = unchanged everywhere. The chalkboard overrides it (gated on its Pixel-Art Display) to a
    /// chalk-white so the completed-task tick matches its row text instead of the forest-green <c>Primary</c>
    /// — the playtest verdict that superseded the brighter-green tick (refine-chalkboard §11). Seeded onto
    /// <see cref="ScribeRowStyle.CheckTickColor"/> in <c>RowStyle</c>, so all four row surfaces (read, editor,
    /// frozen, pinned) consume this single seam and can't drift.</summary>
    private protected virtual Vector4? CheckTickColor(ColorScheme colors) => null;

    /// <summary>Horizontal placement of the right-column nav-button stack within its <c>SideColW</c> column,
    /// resolved from the already-computed column width and single nav-button box width (both passed in by
    /// <see cref="BuildRightColNav"/>, which owns the layout math). <c>private protected virtual</c> so a
    /// subclass can pick its surface family's placement rule without forking the nav build (refine-nav-button-placement).
    /// <para>Default = the <b>Pages group</b> (Lectern, Notebook, Scriptorium, Clockmaker's Notebook):
    /// <see cref="CrossAxisAlignment.Start"/>, buttons hugging the left/inner edge of the column — the layout
    /// their paper-margin art was tuned for. The <b>Hard Border group</b> (the Chalkboard) overrides this with
    /// an adaptive center/end rule. The Tablet is Hard Border by intent but renders no nav column
    /// (its <see cref="BuildRightColNav"/> returns an empty box), so this seam never fires for it.</para></summary>
    private protected virtual CrossAxisAlignment NavButtonAlignment(float sideColW, float navBoxW) =>
        CrossAxisAlignment.Start;

    /// <summary>Restyle the completion-policy Dropdown (Pin Tab picker) without forking the widget. Given the
    /// theme's resolved <see cref="DropdownStyle"/>, return it (default) or a tweaked copy. <c>private
    /// protected virtual</c> so a subclass can fix a per-theme legibility problem in its OPEN menu that the
    /// cascade-from-<c>ColorScheme</c> defaults get wrong. The chalkboard overrides it because the stock menu
    /// paints the selected row's fill from <c>StateSelected</c> (a translucent <c>Primary</c> tint) and its
    /// selected LABEL from <c>SelectionAccentColor = Primary</c> — i.e. dark-green text on a see-through
    /// dark-green wash over the dark slate, which is unreadable. Every light surface reads fine, so only the
    /// chalkboard opts in (refine-chalkboard).</summary>
    private protected virtual DropdownStyle DecoratePolicyDropdownStyle(DropdownStyle style) => style;

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
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
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

        int titleMaxLines = TitleMaxLines;

        const float titleBtnSpacing = 6f;
        // Left-padded so the leading grip (below) has visible breathing room from the title text — mirrors
        // the pencil's own left margin in the trailing group. Halved from titleBtnSpacing*1.5 (2026-09-08
        // playtest feedback: too much space between the grip and the title).
        Widget titleSlot = new Expanded(child: new Padding(
            EdgeInsets.Only(left: titleBtnSpacing * 0.75f),
            child: _isTitleEditing
                ? BuildTitleField(titleStyle)
                : BuildTitleDisplay(displayTitle, titleStyle)));

        // Pencil — icon-only (no chrome), same visual weight as the grip glyph.
        // Shown ONLY on the Edit tab (2026-09-10 3rd playtest pass, explicit ask) — `scratch` is non-null
        // exactly while `viewMode == Editor` on every tabbed dialog (LeaveEditorMode nulls it and lands
        // viewMode back on Read the instant any OTHER tab is selected — see ViewSwitching.cs), so this
        // single check already IS the Edit-tab gate; hidden on every other tab. The always-edit Tablet has
        // no separate tabs at all, so its one view counts as "the Edit tab" and keeps the pencil throughout.
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

        // Expand/collapse-all toggle (manage-terminal-assignment-records): only while the Inbox or Sent
        // History view is active — never simultaneously with the pencil, which only shows in Editor view —
        // so it never contends with the pencil for the same slot. Reuses the per-row chevron's own glyphs
        // and direction convention (right = something to expand, down = fully open) rather than a new SVG.
        bool allAssignmentRowsExpanded = AllVisibleAssignmentRowsExpanded();
        Widget? expandCollapseAllSlot = viewMode is ScribeLecternView.Inbox or ScribeLecternView.SentHistory
            ? TitleButton(
                allAssignmentRowsExpanded ? "scribetriangledown" : "scribetriangleright",
                allAssignmentRowsExpanded ? "scribe-assignment-collapse-all" : "scribe-assignment-expand-all",
                chromeColor,
                size: ScribeRowConstants.RowCheckboxSize * 1.1f,
                onTap: ToggleAllVisibleAssignmentRows)
            : null;

        // Leading drag-grip (unify-tab-header-layout §8) — moved out of the trailing group to sit to the
        // LEFT of the title, matching the original exploration mockup. The whole TitleBar band is already
        // the drag zone via WindowConfig.DragHandleHeight; the grip is a discoverability affordance on top
        // of that, not the only way to drag the window. A press landing ON the grip used to be (and still
        // is) swallowed instead of moving the window if left to band-drag alone: the tooltip wraps its
        // child in a MouseRegion (needed for hover), which is an active hit target, so GuiBase captures the
        // pointer-down before its band-drag check runs — and click-through can't coexist with the tooltip
        // (an IgnorePointer would kill the MouseRegion's hover too). So the grip owns its OWN window drag
        // via a GestureDetector nested INSIDE the tooltip: the outer MouseRegion still fires hover, and
        // press→move→release moves the window just like the band (§8.1; see the gripDragging fields +
        // VSAPI-NOTES.md §LibGUI). This is a REPOSITION of that exact mechanism, not a new one — the
        // GestureDetector/Tooltip/glyph are unchanged. A "drag to move" tooltip labels it.
        // Row.crossAxisAlignment centers by BOUNDING BOX, but the title text's box reserves descent+leading
        // space below its own baseline that the icon glyph's tight box doesn't — so a naive box-center left
        // the grip visibly LOWER than the title's visual (cap-height) center (2026-09-08 playtest feedback).
        // Nudge the grip UP by half that reserved space, computed from the title's own font metrics so it
        // tracks any future title-size change, via the "add bottom padding to a centered child" trick: Center
        // alignment splits an enlarged box's extra height evenly above/below, so bottom-only padding of X
        // shifts the glyph itself up by X/2 relative to an unpadded center.
        // 2026-09-09 playtest feedback: the full descent+leading nudge overshot — the grip read as sitting
        // too HIGH relative to the title's visual center. Halved so the up-shift is milder (a quarter of
        // descent+leading rather than half).
        var titleFontMetrics = TextLayoutHelper.GetFont(titleStyle.FontFamily, titleStyle.FontSize, titleStyle.Weight).Metrics;
        float titleReservedBelowBaseline = (titleFontMetrics.Descent + titleFontMetrics.Leading) / 2f;
        Widget gripSlot = new Padding(
            EdgeInsets.Only(bottom: titleReservedBelowBaseline),
            child: WithTooltip("scribe-gui-drag",
                new GestureDetector(
                    onPress: OnGripDragStart,
                    onMove: OnGripDragMove,
                    onRelease: OnGripDragEnd,
                    child: new ScribeVsIconGlyph("scribegrip", ScribeRowConstants.RowCheckboxSize * 1.1f, chromeColor))));

        // Trailing group: pencil (editor only) · expand/collapse-all (Inbox/Sent History only) · close
        // button (refine-settings-and-window-chrome). Close reuses the delete SVG at 1.4× the per-row size.
        var trailingGroup = new List<Widget>();
        if (pencilSlot is not null) trailingGroup.Add(pencilSlot);
        if (expandCollapseAllSlot is not null) trailingGroup.Add(expandCollapseAllSlot);
        trailingGroup.Add(TitleButton("scribeclose", "scribe-gui-close", colors.Error,
            size: ScribeRowConstants.RowCheckboxSize * 1.4f, onTap: () => TryClose()));

        // Only used below to pick a COSMETIC cross-alignment (End vs Center) for the title row — the
        // actual band/content SIZING no longer depends on this estimate at all (see the return statement's
        // comment), so an occasional off-by-one-character guess here just risks the wrong alignment choice,
        // never a wrong size.
        float gripWidth = ScribeRowConstants.RowCheckboxSize * 1.1f;
        float trailingWidth = titleBtnSpacing * (trailingGroup.Count - 1);
        if (pencilSlot is not null) trailingWidth += titleBtnSpacing * 1.5f + pencilSize;
        if (expandCollapseAllSlot is not null) trailingWidth += ScribeRowConstants.RowCheckboxSize * 1.1f;
        trailingWidth += ScribeRowConstants.RowCheckboxSize * 1.4f; // close button, always present
        float titleAvailableW = layout.TitleBtnsW - 0.02f * layout.W - 0.04f * layout.W
            - gripWidth - titleBtnSpacing * 0.75f - trailingWidth;
        // BuildTitleField's TextField can't wrap (no multiline support on LibGUI's TextFieldStyle), so it's
        // always 1 line while editing regardless of what CountWrappedLines would estimate for the same text.
        string titleForWrap = _isTitleEditing ? "" : displayTitle;
        int actualTitleLines = CountWrappedLines(
            titleForWrap, titleStyle.FontFamily, titleStyle.FontSize, titleStyle.Weight, titleAvailableW, titleMaxLines);
        // Bottom-anchor the title + chrome so the grip/close/pencil track the wrapped title's LAST line;
        // single-line keeps the original centered layout.
        CrossAxisAlignment titleCrossAlign = actualTitleLines <= 1 ? CrossAxisAlignment.Center : CrossAxisAlignment.End;

        Widget titleRow = new Row(
            mainAxisAlignment: MainAxisAlignment.SpaceBetween,
            crossAxisAlignment: titleCrossAlign,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                gripSlot,
                titleSlot,
                new Row(
                    crossAxisAlignment: CrossAxisAlignment.Center,
                    mainAxisSize: MainAxisSize.Min,
                    spacing: titleBtnSpacing,
                    children: trailingGroup.ToArray()),
            });

        // 2026-09-10 (3rd/4th playtest passes — REVERTED): both the self-sizing ConstrainedBox rewrite
        // (Round 6) and its Align-removal correction (Round 6 correction) are reverted here. Round 6's
        // rewrite dropped BOTH the outer fixed-size SizedBox AND the Align(BottomCenter) that horizontally
        // CENTERS the fixed-width (TitleBtnsW) inner box within the full-width (W) outer band — without
        // Align, a plain Padding/ConstrainedBox chain has no horizontal-centering step at all, so the whole
        // grip+title+buttons group collapsed against the LEFT edge instead of sitting centered with a
        // symmetric margin (2026-09-10, 5th playtest pass: "ruined the orientation left and right"). The
        // Round 6 correction's fix for the OTHER regression (Align filling the whole dialog height because
        // the outer box's max-height was unbounded) is no longer needed once the outer box is back to an
        // EXPLICIT, bounded height (bandH below) rather than a minimum-only ConstrainedBox with an
        // unbounded max — Align only fills to whatever ambient max it's handed, and a fixed-height SizedBox
        // hands it exactly bandH, not the dialog's total H.
        //
        // Back to Round 5's structure: two nested, EXACTLY-sized SizedBoxes (not self-sizing) — an outer
        // W × bandH band, and an inner TitleBtnsW × contentBoxH box centered/bottom-anchored within it via
        // Align(BottomCenter). Both grow from their single-line baselines by the SAME delta
        // (extraLines * titleLineH) so the inner box's top edge — and thus Row 2/3 below it — never moves;
        // only the bottom extends. Round 5/6's actual titleLineH bug (using CuneiformMetrics.LineHeightRatio,
        // tuned for the Tablet's cuneiform glyphs, as a stand-in for ordinary Latin/Caudex RichText line
        // height — the real cause of the "discrete downward jump" that motivated Round 6's rewrite) is fixed
        // here directly: titleLineH now uses the title's own real font metrics (titleFontMetrics, already
        // computed above for the grip-baseline nudge) instead of the mismatched cuneiform ratio.
        float titleLineH = titleFontMetrics.Descent - titleFontMetrics.Ascent + titleFontMetrics.Leading;
        int extraLines = actualTitleLines - 1;
        float contentBoxH = layout.TitleBtnsH + extraLines * titleLineH;
        float bandH = layout.TitleBarH + extraLines * titleLineH;

        return new SizedBox(
            width: layout.W,
            height: bandH,
            child: new Align(
                Alignment.BottomCenter,
                child: new SizedBox(
                    width: layout.TitleBtnsW,
                    height: contentBoxH,
                    // Panel behind the title row when Pixel-Art is OFF (no art backdrop) so it isn't
                    // transparent onto the world; unchanged when ON (the art is the background). Right inset
                    // stays 0.04·W (unify-tab-header-layout §8); the left inset is HALVED to 0.02·W
                    // (2026-09-08 playtest feedback: still too much space left of the grip+title after the
                    // earlier flat-10px removal), now that the leading grip (plus its own spacing from the
                    // title) occupies that space instead.
                    child: FlatPanel(new Padding(
                        EdgeInsets.Only(left: 0.02f * layout.W, right: 0.04f * layout.W),
                        child: titleRow)))));
    }

    /// <summary>Greedy word-wrap line count for a single-paragraph title against <paramref name="maxWidth"/>,
    /// mirroring the wrap algorithm <see cref="ScribeMultilineField"/> uses for its own caret math (LibGUI's
    /// own break-into-lines is internal). Used only to decide how tall <see cref="BuildTitleBar"/>'s band needs
    /// to be THIS frame — not to render text — so <paramref name="maxWidth"/> is an estimate of the title
    /// slot's available width, not an exact layout measurement.</summary>
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

    /// <summary>The resting (non-editing) title widget, sized to fill the title slot's Expanded. Default is a
    /// <see cref="RichText"/> that wraps to <see cref="TitleMaxLines"/> lines (two by default; wrap-titles-all-surfaces),
    /// with ellipsis overflow past the last line. The tablet overrides this to render the title as display-only
    /// cuneiform (add-tablet-cuneiform-chrome D2), but its cuneiform-OFF path returns to this base wrapping.
    /// <paramref name="displayTitle"/> is the already-resolved title text (host default substituted for the codec's
    /// "Untitled"); <paramref name="titleStyle"/> carries the resolved size/family/weight/color so an override can
    /// match the band metrics.</summary>
    private protected virtual Widget BuildTitleDisplay(string displayTitle, TextStyle titleStyle) =>
        new RichText(new TextSpan(displayTitle), titleStyle, maxLines: TitleMaxLines, overflow: TextOverflow.Ellipsis);

    /// <summary>Maximum number of lines the title band shows before clipping. Default 2 (wrap-titles-all-surfaces):
    /// a long title on every surface (Lectern/Notebook/Scriptorium/Chalkboard, and the cuneiform-OFF tablet) wraps
    /// into the band's existing vertical slack instead of clipping off the right. <see cref="BuildTitleBar"/> reads
    /// this to grow the title slot to that many line-heights; a title that fits on one line is byte-identical to the
    /// old single-line layout. Previously defaulted to 1 (tablet-only override); generalized to the shared default
    /// once the tablet's two-line wrap was proven in-game.</summary>
    private protected virtual int TitleMaxLines => 2;

    /// <summary>The active (editing) title widget — a live text input bound to <see cref="_titleController"/>
    /// and <see cref="_titleFocusNode"/>. Default is the stock LibGUI single-line <see cref="TextField"/>
    /// (Lectern/Notebook, unchanged), with the maxlength gate and Enter/Escape commit wired here so the
    /// shared commit machinery (<see cref="CommitTitleIfEditing"/>) is untouched by an override. The tablet
    /// overrides this to a single-line cuneiform input driven by the SAME controller/focus node, so its
    /// <see cref="_isTitleEditing"/> / <see cref="_pendingTitleEditRebuild"/> / <see cref="_pendingTitleFocus"/>
    /// deferral all still apply (add-tablet-cuneiform-chrome D2).</summary>
    private protected virtual Widget BuildTitleField(TextStyle titleStyle)
    {
        // Height explicitly matched to the title's own line height (2026-09-09 playtest feedback) instead
        // of TextFieldStyle's default fixed 40px, which visibly grew the title band the instant editing
        // started. Mirrors how the Tablet's cuneiform title field already renders with zero padding/border
        // via its own custom renderer (ScribeCuneiformFieldRenderWidget's padX/padY/borderThickness all 0).
        // BorderThickness is already 0 here. The stock TextField's horizontal text inset (a fixed 10px baked
        // into RenderTextField.PaintInternal, not exposed on TextFieldStyle) can't be zeroed without forking
        // `gui` — left as-is; it's dwarfed by the title row's own leading/trailing insets.
        var metrics = TextLayoutHelper.GetFont(titleStyle.FontFamily, titleStyle.FontSize, titleStyle.Weight).Metrics;
        float lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        return new TextField(
            _titleController!,
            _titleFocusNode!,
            new TextFieldStyle { FillColor = new Vector4(0, 0, 0, 0), BorderThickness = 0, Height = lineHeight, TextStyle = titleStyle },
            onKeyDown: OnTitleFieldKeyDown);
    }

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
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        // Sidebar nav buttons enlarged (v1-playtest-fixes 5.6): the base was RowCheckboxSize × 1.2; ×1.7 on
        // top of that grows BOTH the button box and its inscribed SVG, since ScribeRowButton derives its box
        // size AND glyph size from this one `size` value.
        float size = ScribeRowConstants.RowCheckboxSize * 1.7f;

        // Resting/inactive nav-glyph tint (dedicated seam so a dark theme can darken the icons without
        // dragging muted body text down with them — see NavIconColor). The active tab uses its own activeColor.
        Vector4 navColor = NavIconColor(colors);

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
        Widget readBtn = TitleButton("scribecheck", "scribe-gui-nav-read", navColor,
            size: size, onTap: EnterReadMode, boxShadows: navShadow,
            activeColor: viewMode == ScribeLecternView.Read ? ScribeRowConstants.NavActiveRead : null);
        Widget editBtn = TitleButton("scribeedit", "scribe-gui-nav-edit",
            editLockedByOther ? navColor with { W = 0.4f } : navColor,
            size: size, onTap: TryEnterEditor, boxShadows: navShadow,
            activeColor: viewMode == ScribeLecternView.Editor ? ScribeRowConstants.NavActiveEdit : null);
        // Pinned enlarged +15% (§10.2): the pin glyph reads a touch larger than the others.
        Widget pinBtn = TitleButton("scribepin", "scribe-gui-nav-pinned", navColor,
            size: size, onTap: OnClickSwitchToPinned, iconScale: 1.15f, boxShadows: navShadow,
            activeColor: viewMode == ScribeLecternView.Pinned ? ScribeRowConstants.NavActivePinned : null);
        // Settings gear LAST in the group (§10.1), always after any extra buttons.
        Widget settingsBtn = TitleButton("scribegear", "scribe-gui-nav-settings", navColor,
            size: size, onTap: modSystem.OpenSettings, boxShadows: navShadow,
            activeColor: modSystem.IsSettingsOpen ? ScribeRowConstants.NavActiveSettings : null);

        var navChildren = GetLeadingNavButtons()
            .Concat(new Widget[] { readBtn, editBtn, pinBtn })
            .Concat(GetExtraNavButtons())
            .Append(settingsBtn)
            .ToArray();

        // Horizontal placement of the buttons within their SideColW column, resolved through the
        // NavButtonAlignment seam (refine-nav-button-placement). The base owns the layout math — the column
        // width and the single drawn nav-button box width (NavButtonSize == this size, minus the
        // ScribeRowButton BoxShrink) — and the seam maps them to a CrossAxisAlignment for this surface family.
        // Default (Pages group) is Start/left-hugging, the layout the paper-margin art was tuned for; the
        // chalkboard (Hard Border group) overrides it with the adaptive center/end rule. See NavButtonAlignment.
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

    /// <summary>A tooltipped icon button reusing the per-row button chrome (<see cref="ScribeRowButton"/>).
    /// <paramref name="iconScale"/> grows just the glyph (not the box) — used to enlarge the pin +15%
    /// (§10.2). <paramref name="boxShadows"/> passes an optional drop shadow through to the button's
    /// <c>BoxStyle</c> (the sidebar nav buttons use one to read as raised chrome — v1-playtest-fixes 5.6).
    /// <paramref name="shimmer"/> wraps the button in the Inbox nav-button shimmer sweep
    /// (<see cref="ScribeShimmerWrap"/>, §8.5) when true — every caller except the four Inbox nav
    /// buttons leaves it false, in which case the wrap is a costless pass-through.
    /// Protected so subclasses can build matching nav buttons in <see cref="GetExtraNavButtons"/>.</summary>
    protected Widget TitleButton(string iconName, string tooltipKey, Vector4 color, float size, Action onTap, float iconScale = 1f, BoxShadow[]? boxShadows = null, Vector4? activeColor = null, bool shimmer = false) =>
        WithTooltip(tooltipKey, new ScribeShimmerWrap(shimmer, size,
            new ScribeRowButton(iconName: iconName, iconColor: color, size: size, onTap: onTap, iconScale: iconScale, boxShadows: boxShadows, activeColor: activeColor)));

    /// <summary>Wrap a button in a localized hover tooltip (<c>scribe:&lt;key&gt;</c>), using the global
    /// overlay so it isn't clipped by the surrounding boxes. The bubble fills with the theme's
    /// <c>Background</c> (Tooltip renders it that way), so the content text uses its partner
    /// <c>OnBackground</c> — the same dark ink the Lectern's body text uses when Pixel-Art Display paints
    /// the light parchment theme. Without an explicit color the content defaulted to white, which washed
    /// out against the light bubble; resolving through <see cref="ScribeTheme.For"/> keeps it correct in
    /// both modes (dark ink on light paper when pixel-art is on, light text on the dark global theme when
    /// off).</summary>
    protected Widget WithTooltip(string key, Widget child, params object[] args)
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
                child: new Text(Lang.Get("scribe:" + key, args), new TextStyle
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
        ScribeLecternView.Assignment => BuildAssignmentContent(),
        ScribeLecternView.Inbox     => BuildInboxContent(),
        ScribeLecternView.SentHistory => BuildSentAssignmentHistoryContent(),
        ScribeLecternView.InboxInventory => BuildInboxInventoryContent(),
        _                          => BuildReadContent(),
    };

    /// <summary>The live row style for this build, derived from the player's current settings (NOT cached
    /// at open — add-settings-tab D4), so a window-font-scale change from the settings view repaints the
    /// open dialog on the next rebuild. Passes through <see cref="DecorateRowStyle"/> so a subclass may
    /// layer tier-specific row behavior (the tablet flips on the cuneiform row path) without duplicating
    /// the settings-derivation.</summary>
    private protected ScribeRowStyle RowStyle => DecorateRowStyle(ScribeRowStyle.FromSettings(modSystem.MySettings)
        // Subtask indent depends on the window width (not a settings-only size), so it's layered on here from
        // the live layout rather than in FromSettings: 10px + 3%·W, mirroring the 0.04·W footer inset idiom
        // (task-subtasks 5.1). Applied before DecorateRowStyle so a subclass's `with` tweaks still compose.
        // Focus-border color is seeded from the InputFocusBorderColor seam (default Primary; chalkboard →
        // chalk-white) so every editable row shares the guestbook field's resolved focus color.
        with
        {
            SubtaskIndent = 10f + 0.03f * host.GetLayout(modSystem.MySettings.PixelArtSize).W,
            InputFocusBorderColor = InputFocusBorderColor(ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme),
            CheckTickColor = CheckTickColor(ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme),
        });

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

    /// <summary>Whether this dialog's Read View renders the filter-pill row (<c>read-view-filter-pills</c>)
    /// and any subtask-group collapse toggles (<c>read-view-subtask-collapse</c>) — one flag gates both
    /// features together, since they are excluded from the same surfaces as a unit (scribe-dialog-base).
    /// Defaults to <c>true</c> so every existing subclass (Lectern, Notebook, Clockmaker's Notebook,
    /// Chalkboard, Scriptorium, Assignment Desk, Inbox) gets both features with no per-subclass change.
    /// Only <see cref="GuiDialogScribeTablet"/> overrides this to <c>false</c> (tablet-dialog), keeping its
    /// pared-down read-view intentional rather than an oversight.</summary>
    private protected virtual bool SupportsFilterPills => true;

    /// <summary>Whether this dialog's Read View / Editor renders the shared Row 2 subtitle + Row 3 tab
    /// header at all (unify-tab-header-layout). Defaults to <c>true</c> for every tabbed dialog. Only
    /// <see cref="GuiDialogScribeTablet"/> overrides this to <c>false</c>: an earlier pass let the tablet
    /// inherit the subtitle purely because it reuses these same shared content widgets, which read as the
    /// unification going further than intended — the tablet keeps its pared-down header with neither row
    /// (reversed 2026-09-08 playtest feedback; design.md Non-Goals).</summary>
    private protected virtual bool SupportsTabHeader => true;

    /// <summary>Whether this surface's EDITOR rows opt into the read view's click-to-open-Handbook affordance
    /// on their Link/Tracker/Craft name label (enable-tablet-row-links). Default false: every surface with a
    /// distinct read view (Lectern/Notebook/Scriptorium) activates links there, so its editor names stay plain
    /// editable regions. Only the tablet — which has no read/edit split, so a wet tablet always renders the
    /// editor path — overrides it true, wiring link activation directly onto the always-edit rows.</summary>
    protected virtual bool EditorRowsOpenLinks => false;

    /// <summary>Resolve a block's Tracker/Link item icon + display name for a row snapshot, or
    /// <c>(null, null)</c> for a Task/Text block (which render their authored text instead). A Tracker uses
    /// its <see cref="ScribeBlock.TargetItemCode"/>, a Link its <see cref="ScribeBlock.LinkTarget"/>; both are
    /// plain code strings that Core stores API-free, so the parse against the live registries happens here in
    /// the Mod layer (add-tracker-link-tasks Group 5). Kept off the row widgets so they stay <c>capi</c>-free.
    /// Protected (not private): takes any <see cref="ScribeBlock"/>, not just one from <c>host.Document</c> —
    /// <see cref="GuiDialogScribeAssignmentDesk"/> reuses it to resolve rows from a STAGED item's document
    /// (assignment-multi-item-creation design.md D10).</summary>
    protected (ItemStack? Stack, string? Name) ResolveRowItem(ScribeBlock b)
    {
        if (b.IsQuestObjective)
        {
            // An objective's own match code lives in LinkTarget (a raw key, never a "page:"/"quest:"-scheme
            // Link target — add-progression-framework-quest-objective-subtasks 1.3), so it never routes
            // through ResolveDisplay's scheme dispatch below. Item-backed (TargetItemCode set) resolves
            // exactly like a Tracker; label-only has no item to draw and falls back to the captured
            // LinkLabel (or, failing that, the raw match code) with a generic icon (rendered by the caller
            // from a null Stack, mirroring the guide-page-Link fallback).
            return b.TargetItemCode is { } objItemCode
                ? ScribeItemRef.ResolveDisplay(capi.World, objItemCode, null)
                : (null, b.LinkLabel ?? b.LinkTarget);
        }
        if (!b.IsTracker && !b.IsLink && !b.IsCraft) return (null, null);
        // A Link resolves its LinkTarget; a Tracker AND a Craft parent both resolve the item they count —
        // the Craft parent's TargetItemCode is its recipe OUTPUT (add-crafting-tasks 9.1), so it shows the
        // output item's icon + name exactly like a Tracker, differing only in the "Craft …" label framing.
        string? code = b.IsLink ? b.LinkTarget : b.TargetItemCode;
        return ScribeItemRef.ResolveDisplay(capi.World, code, b.LinkLabel);
    }

    /// <summary>Resolve a tapped item row to its Handbook page and open it — the ONE dispatch shared by the read
    /// view and (where <see cref="EditorRowsOpenLinks"/> is on) the editor view, so Link/Tracker/Craft resolve
    /// identically on every path. A Link opens its <see cref="ScribeBlock.LinkTarget"/>; a Tracker AND a Craft
    /// parent open their <see cref="ScribeBlock.TargetItemCode"/> (the Craft parent's is its recipe OUTPUT — see
    /// <see cref="ResolveRowItem"/>). An item-backed QuestObjective opens its own <see cref="ScribeBlock.TargetItemCode"/>
    /// the same way; a label-only QuestObjective has no page to open and falls through to the no-op below (task
    /// add-progression-framework-quest-objective-subtasks 7.2 — not an exception, the intended fallthrough).
    /// Keyed by TaskId off the live document, so it works regardless of which view
    /// dispatched it and mirrors the Pin-tab dispatch (<see cref="OnPinOpenLink"/>). A plain Task/Note has no
    /// item code, so it no-ops here (and its editor label is never wrapped in the gesture — see BuildItemEditorContent).</summary>
    private void OpenRowLink(Guid taskId)
    {
        var block = host.Document.FindByTaskId(taskId);
        if (block?.IsLink == true) ScribeItemRef.OpenHandbookPage(capi, block.LinkTarget);
        else if (block?.IsTracker == true || block?.IsCraft == true || block?.IsQuestObjective == true)
            ScribeItemRef.OpenHandbookPage(capi, block.TargetItemCode);
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
                    // A quest Link's live kill/block-place/block-break progress (§11.3) — null for every
                    // other row kind, a plain/guide-page Link, or when the watcher has nothing for this
                    // quest yet (see ScribeModSystem.TryGetQuestProgressText's doc-comment).
                    string? questProgress = ScribeLinkTarget.QuestCode(b.LinkTarget) is { } questCode
                        ? modSystem.TryGetQuestProgressText(ScribeLinkTarget.QuestSource(b.LinkTarget)!, questCode)
                        : null;
                    var (assignerName, assignedDate, acceptedDate) = ResolveAssignmentTooltipInfo(b.Assignment);
                    // A label-only QuestObjective (no resolved item — ResolveRowItem's stack came back null)
                    // draws the generic book glyph rather than a blank ItemStackDisplay: the row widget's icon
                    // dispatch (ScribeLinkIcon) keys off a "page:"/"quest:"-scheme LinkTarget, and a
                    // QuestObjective's real LinkTarget is a bare match code (never scheme-prefixed — 1.3), so a
                    // synthetic guide-page-scheme string is substituted here purely to route the SAME icon
                    // fallback a guide-page Link already uses. This is display-only: OpenRowLink re-reads the
                    // LIVE block by TaskId rather than this snapshot, so it's never mistaken for a real page.
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
                        QuestProgressText: questProgress,
                        AssignerName: assignerName, AssignedDate: assignedDate, AcceptedDate: acceptedDate,
                        IsStaticVsQuestObjective: isStaticVsQuestObjective);
                })
                // Drop only an empty-text Task (a stray blank checkbox — belt-and-suspenders, see below).
                // A Text note may be legitimately empty, and a Tracker/Link has no text of its own (it renders
                // an item icon + name), so both pass through — all are non-IsTask, so `!r.IsTask` keeps them.
                .Where(r => !r.IsTask || !string.IsNullOrWhiteSpace(r.Text))
                .ToList(),
            onToggleTask: OnReadViewCompleteTask,
            onTogglePinned: OnReadViewTogglePinned,
            // A Link row's item name is a hyperlink: tapping it opens the referenced Handbook page and never
            // touches completion (add-tracker-link-tasks 5.3). A Tracker's name opens its TARGET item's page and
            // a Craft parent its OUTPUT item's page the same way (feedback 6.5 — the count target IS a real item
            // with a Handbook entry). Shared with the editor path via OpenRowLink so all three resolve identically.
            onOpenLink: OpenRowLink,
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
            currentShade: currentShade,
            hintLangKey: EmptyHintLangKey,
            readOnly: ReadViewIsReadOnly,
            completionAndPinLive: ReadViewCompletionAndPinLive,
            onTextEditRefused: ReadViewTextEditRefused,
            assignedStampBitmap: modSystem.GetGuiTextureBitmap(ScribeAssignedTaskIcon.Asset),
            // Filter-pill row + subtask-collapse toggles (read-view-filter-and-collapse), gated as ONE
            // unit on the capability flag (scribe-dialog-base) — false only on the Tablet.
            supportsFilterPills: SupportsFilterPills,
            supportsTabHeader: SupportsTabHeader,
            showSubtitleRow: modSystem.VisualTuning.ShowSubtitleRow,
            activeFilterCategory: readViewFilterCategory,
            onFilterCategoryChanged: OnReadViewFilterCategoryChanged,
            isGroupCollapsed: collapsedReadViewGroupIds.Contains,
            onToggleGroupCollapsed: OnReadViewToggleGroupCollapsed);

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
                // Leading-icon marker only (add-assignment-and-quest-support 9.3) — NOT yet wiring
                // ReadOnly=true here for an accepted assignment's text (design.md Decision 5's "frozen
                // text" half): ScribeEditRow's ScribeMultilineField wiring (focus-node indexing, tablet
                // cuneiform, jump-navigation) has no read-only branch yet, and swapping it for the static
                // display renderer without checking every one of those interactions risked a subtly broken
                // Tab/Enter row-navigation experience. Deferred as a disclosed follow-up (see tasks.md 9.3).
                bool isAcceptedAssignment = b.Assignment?.State == ScribeAssignmentState.Accepted;
                var (assignerName, assignedDate, acceptedDate) = ResolveAssignmentTooltipInfo(b.Assignment);
                // Same synthetic guide-page-scheme substitution as the read view's row construction (see
                // BuildReadContent) — routes a label-only QuestObjective's icon through the existing
                // book-glyph fallback rather than a blank ItemStackDisplay. Display-only.
                bool isStaticVsQuestObjective = b.IsQuestObjective
                    && ScribeQuestCatalog.IsStaticObjectiveCode(b.LinkTarget);
                string? iconLinkTarget = b.IsQuestObjective && stack is null && !isStaticVsQuestObjective
                    ? ScribeLinkTarget.ForPage(b.LinkTarget ?? "")
                    : b.LinkTarget;
                return new ScribeEditRowData(
                    Index: i, Kind: b.Kind, Done: b.Done, Pinned: IsPinnedForMe(b.TaskId), TaskId: b.TaskId,
                    Text: b.Text, DisplayStack: stack, DisplayName: name,
                    TargetQuantity: b.TargetQuantity, CurrentQuantity: b.CurrentQuantity, LinkTarget: iconLinkTarget,
                    Depth: b.Depth, IsAcceptedAssignment: isAcceptedAssignment,
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
            // A Tracker row's inline stepper edits its target quantity in scratch; the normal editor flush
            // persists it (the codec serializes TargetQuantity, so no dedicated packet — add-tracker-link-tasks 5.2).
            onTrackerQuantityChanged: SetEditorTrackerTargetQuantity,
            // A single grip tap toggles the row's subtask depth 0↔1 (task-subtasks 5.3); press-hold-drag
            // still reorders (the dispatcher fires the tap only on a genuine click).
            onGripTap: OnGripTap,
            // Drag-reorder follows the moved row into view (anchorViewport defaults false); only a Sink
            // completion passes anchorViewport: true to hold the viewport still.
            onReorderBlock: (from, to) => ReorderEditorBlock(from, to),
            onAdd: OnClickAdd,
            // Quest Link picker (add-assignment-and-quest-support 10.1/10.2): an empty catalog hides the
            // option entirely (vsquest not installed, installed with no quests, or — filter-quest-link-picker —
            // the player hasn't started any cataloged quest yet this session) — the footer never shows a
            // Quest Link tile that would do nothing.
            questCatalog: StartedQuestCatalogForPicker,
            onAddQuestLink: OnClickAddQuestLink,
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
            // Tier cap (scribe-document-policy): dim the add-picker at a finite tier's 10-entry cap.
            // Buttons stay clickable so the tap surfaces NotifyTabletFull. Uncapped tiers always pass true.
            addTaskEnabled: CanAddTaskUnderPolicy(),
            // Whether the "Done editing" (switch-to-read) footer button renders. True for the tabbed
            // dialogs; the always-edit tablet overrides ShowEditorSwitchToRead to false since it has no
            // Read view (add-tablet-dialog D4).
            showSwitchToRead: ShowEditorSwitchToRead,
            // Click-to-open-Handbook on an item row's name label — the SAME dispatch the read view uses, but
            // only where EditorRowsOpenLinks is on (the tablet, which has no read view). Every other surface
            // passes null, so its editor names stay plain editable regions and render byte-identical to before.
            onOpenLink: EditorRowsOpenLinks ? OpenRowLink : null,
            supportsTabHeader: SupportsTabHeader,
            showSubtitleRow: modSystem.VisualTuning.ShowSubtitleRow,
            assignedStampBitmap: modSystem.GetGuiTextureBitmap(ScribeAssignedTaskIcon.Asset));
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
    private void ToggleEditorReferenceHandbook() => ToggleHandbookPage("craftinginfo-scribe-editor-reference");

    /// <summary>The general "focus, don't hide" handbook toggle behind <see cref="ToggleEditorReferenceHandbook"/>
    /// and the Scriptorium's Transcribe-features button: open the given guide <paramref name="pageCode"/> if no
    /// handbook is open, or close whatever handbook IS open. Decoupled + graceful exactly as documented on
    /// <see cref="ToggleEditorReferenceHandbook"/> — no survival-mod reference, safe no-op if it isn't loaded.</summary>
    protected void ToggleHandbookPage(string pageCode)
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

        // Closed ⇒ open to the requested page via the registered link protocol (unchanged from the
        // open-only original). Absent survival mod ⇒ no "handbook" protocol ⇒ graceful no-op.
        if (capi.LinkProtocols.TryGetValue("handbook", out var open))
            open(new LinkTextComponent("handbook://" + pageCode));
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

/// <summary>The shared Row 2 (subtitle) + Row 3 (per-tab controls) + durable-divider header anatomy every
/// non-tablet <see cref="ScribeDialogBase"/> tab renders above its scrollable/general content
/// (unify-tab-header-layout). One helper enforces the fixed 8-units-above/4-below divider spacing
/// structurally instead of it being copy-pasted (and drifting) across nine call sites — the root cause of
/// the pre-unification inconsistency. A <c>static</c> class (not a member of <see cref="ScribeDialogBase"/>
/// itself) because several callers (<see cref="ScribeReadContent"/>, <see cref="ScribeEditorContent"/>,
/// <see cref="ScribePinnedContent"/>, <see cref="ScribeInboxContent"/>) are plain widgets, not dialog
/// subclasses.</summary>
internal static class ScribeTabHeader
{
    /// <summary>Builds Row 2 + optional Row 3 + the durable divider. <paramref name="row3Content"/> is the
    /// tab's own existing controls (filter pills, the policy picker, column-header labels, or an entire
    /// drafting form for the two form-shaped tabs) — omitted entirely when a tab has none (the Editor view,
    /// Notebook History). <paramref name="hasLeadingDivider"/> is the Guest Book's one exception: an extra
    /// divider directly above <paramref name="row3Content"/>, immediately below the subtitle.
    ///
    /// <para>The durable divider sits flush against the caller's following content — this helper's own
    /// column ends right after it, with no trailing gap. The caller owns the 4-unit breathing-room padding
    /// instead, and MUST place it INSIDE its own scrollable content (e.g. wrapping the <c>Column</c> living
    /// inside a <c>SingleChildScrollView</c>, not the <c>Scrollbar</c>/scroll-view widget from outside — see
    /// <see cref="ScribeReadContent"/> for the established idiom) so it scrolls away with the content
    /// (unify-tab-header-layout §7) rather than sitting as permanent chrome under the divider.</para>
    ///
    /// <para>Small-caps isn't a supported <see cref="TextStyle"/> feature on this LibGUI version (no
    /// font-feature/letter-casing field exists), so it's approximated with two uppercase runs sharing one
    /// family/weight/color: EACH WORD's first letter at the full cap size, that word's remaining letters at
    /// a reduced size — the classic small-caps look (a bigger capital leading smaller capitals per word),
    /// not a uniform ALL-CAPS block.</para>
    ///
    /// <para><c>RenderRichText</c> does NOT baseline-align mixed-size runs sharing one line — every run
    /// draws at <c>y = line.Y - run's own font.Metrics.Ascent</c> (confirmed against the shipped 3.1.0
    /// <c>Gui.dll</c>, not just the 2.0.0 reference clone), which TOP-aligns each run to the line regardless
    /// of its own size, not baseline-aligns them (2026-09-08 playtest feedback: the small-caps tail floated
    /// above the baseline instead of sitting on it). Every text run here is therefore wrapped in a
    /// <see cref="WidgetSpan"/> whose box is forced to the tallest run's own natural line-height, with just
    /// enough top padding pushing its OWN baseline down to that shared reference baseline — see the local
    /// <c>BaselineRun</c> function. A plain space between words needs no such wrapping (no visible glyph to
    /// misalign).</para>
    ///
    /// <para>The descriptor renders in a genuinely italic face: <see cref="ScribeModSystem"/>'s
    /// <c>RegisterCustomFonts</c> registers a real (lighter-weight) italic cut of Caudex under the
    /// "Caudex" family's <see cref="FontWeight.Italic"/> slot, distinct from the bold cut every other
    /// weight slot resolves to.</para>
    ///
    /// <para>The durable divider (and the leading one, if requested) drop to an invisible <see cref="SizedBox"/>
    /// under <see cref="ScribeRowStyle.UseCuneiform"/>, mirroring the pre-existing per-tab divider-hiding
    /// rule (a hard rule reads wrong against the tablet's clay/wax backdrop) rather than inventing a new
    /// tablet-specific gate.</para>
    ///
    /// <para>The Tablet host renders NEITHER Row 2 nor Row 3 at all (design.md Non-Goals; reversed
    /// 2026-09-08 — an earlier pass let the Tablet inherit the subtitle since it reuses these same shared
    /// content widgets, which read as unification going further than intended). Callers gate the ENTIRE
    /// call to this method on their own <c>SupportsTabHeader</c>-equivalent flag rather than this method
    /// gating itself, since only <see cref="ScribeReadContent"/>/<see cref="ScribeEditorContent"/> are ever
    /// reachable from the Tablet (it has no Pinned/Inbox/Guestbook/History/etc. tabs).</para></summary>
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
        // ~82.5% of the cap size (2026-09-10 playtest feedback: 87.5% read as too close to the cap size —
        // pulled back down, still above the original 75%) — reads as small caps (bigger capital, smaller
        // capitals) rather than a second full-size letter.
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

        // Forces every run's box to refLineHeight (so the outer WidgetSpan positioning trivially places it
        // at the line's top regardless of alignment mode) and top-pads its own text by exactly enough to
        // land its baseline at refAscent from that top — the shared reference baseline every run aligns to.
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
