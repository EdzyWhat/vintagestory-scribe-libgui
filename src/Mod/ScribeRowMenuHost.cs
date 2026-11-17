using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Core.Framework;        // RenderObject
using Gui.Core.Layout;           // MainAxisSize
using Gui.Core.Painting;         // LayerLink
using Gui.Core.Scroll;           // RenderViewport
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Basic;         // Container, Button, Text
using Gui.Widgets.Events;        // PointerButton
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, BuildContext, Theme, ColorScheme
using Gui.Widgets.Input;         // GestureDetector (barrier), MouseRegion
using Gui.Widgets.Layout;        // Column, Positioned, SizedBox, CrossAxisAlignment
using Gui.Widgets.Overlay;       // Overlay, OverlayEntry
using Gui.Widgets.Painting;      // BoxStyle, CompositedTransformFollower, CompositedTransformTarget, HitTestBehavior
using OpenTK.Mathematics;        // Vector2, Vector4
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>One entry of a row's context menu: a localized label and what choosing it does.</summary>
/// <param name="LabelLangKey">Lang key of the entry's label.</param>
/// <param name="OnChoose">Runs when the entry is chosen with the left button.</param>
/// <param name="CloseOnChoose">Whether choosing the entry closes the menu. An action entry closes it; a future
/// toggle entry keeps it open so the player can see its new state.</param>
/// <param name="SeparatorBefore">Whether a thin divider line is drawn above the entry, grouping it apart from the
/// entries before it.</param>
internal sealed record ScribeRowMenuItem(
    string LabelLangKey, Action OnChoose, bool CloseOnChoose = true, bool SeparatorBefore = false);

/// <summary>Where a secondary press landed: the row's build context, its render object and the pointer position
/// in the same window coordinates the render objects report through <c>LocalToGlobal</c>.</summary>
internal readonly record struct ScribeRowPress(BuildContext Context, RenderObject? RowRender, float PointerX, float PointerY);

/// <summary>
/// The pointer button of the press currently being dispatched, recorded by the dialog before LibGUI sees the
/// press. LibGUI's <c>Checkbox</c> reports only the toggle, not the button that caused it, so a checkbox that
/// must ignore a right-click reads the button from here.
/// </summary>
internal static class ScribeRowPointer
{
    /// <summary>The button of the mouse press in progress, or null between presses.</summary>
    internal static PointerButton? DownButton { get; set; }

    /// <summary>True while a right-button press (and its release) is being dispatched.</summary>
    internal static bool RightDown => DownButton == PointerButton.Right;

    /// <summary>True only while the dialog is dispatching a press to LibGUI, so a focus change caused by that press
    /// can tell it apart from one the keyboard caused.</summary>
    internal static bool Dispatching { get; set; }
}

/// <summary>
/// The one row context menu a dialog can have open. Rows report themselves as mounted so the menu can anchor to
/// the row, re-anchor when the row remounts and close when no row for its <c>TaskId</c> is shown any more. The
/// menu is an overlay pair (a full-screen barrier plus a follower linked to the row), like
/// <see cref="ScribeAddKindPicker"/>'s drop-up.
/// </summary>
internal sealed class ScribeRowMenuHost : IDisposable
{
    private const float MenuGap = 4f;
    private const float MenuMinWidth = 116f;
    private const float CuneiformMenuMinWidth = 150f;
    /// <summary>The menu grows with its widest label, up to this multiple of its minimum width.</summary>
    private const float MaxWidthFactor = 2f;
    private const float TileLabelSize = 14f;
    private const float PanelPadding = 8f;
    private const float TilePadX = 10f;
    private const float TilePadY = 5f;
    private const float CuneiformTilePadY = 3f;
    private const float TileCorner = 4f;
    private const float SeparatorMarginY = 3f;

    private readonly Func<Guid, IReadOnlyList<ScribeRowMenuItem>> itemsFor;
    private readonly Func<ScribeRowStyle> style;
    private readonly Func<ScribeAmbientLightSampler.Shade> shade;

    /// <summary>Every mounted row, oldest first; the newest entry for a <c>TaskId</c> is the live one.</summary>
    private readonly List<MountedRow> mounted = new();

    /// <summary>Set when the open menu's row remounted, so the next <see cref="Tick"/> re-points the follower at the
    /// new row. Deferred because rows report from <c>InitState</c>/<c>Dispose</c>, in the middle of a build.</summary>
    private bool refollow;

    /// <summary>A menu to open on the next <see cref="Tick"/>, from a right-click that closed the open menu.</summary>
    private (Guid TaskId, ScribeRowPress Press, OverlayState? Overlay, ColorScheme Colors)? pendingReopen;

    private OverlayEntry? barrierEntry;
    private OverlayEntry? menuEntry;
    private Guid openTaskId;
    private IReadOnlyList<ScribeRowMenuItem> openItems = Array.Empty<ScribeRowMenuItem>();
    private BuildContext openContext;
    private OverlayState? openOverlay;
    private ColorScheme openColors;
    private Placement placement;
    private ScribeAmbientLightSampler.Shade openShade;

    /// <param name="itemsFor">The entries for a row, resolved when the menu opens; empty means no menu.</param>
    /// <param name="style">The dialog's row style (cuneiform labels on the Tablet).</param>
    /// <param name="shade">The dialog's illumination shade; the overlay sits outside the body's tint.</param>
    internal ScribeRowMenuHost(
        Func<Guid, IReadOnlyList<ScribeRowMenuItem>> itemsFor,
        Func<ScribeRowStyle> style,
        Func<ScribeAmbientLightSampler.Shade> shade)
    {
        this.itemsFor = itemsFor;
        this.style = style;
        this.shade = shade;
    }

    /// <summary>True while a row menu is showing.</summary>
    internal bool IsOpen => menuEntry is not null;

    /// <summary>A row mounted. <paramref name="renderOf"/> returns the row's current render object.</summary>
    internal void RowMounted(Guid taskId, LayerLink link, Func<RenderObject?> renderOf)
    {
        mounted.Add(new MountedRow(taskId, link, renderOf));
        if (IsOpen && taskId == openTaskId) refollow = true;
    }

    /// <summary>A row unmounted. Closing for a vanished row waits for <see cref="Tick"/>, since a remounted row
    /// reports itself in the same frame.</summary>
    internal void RowDisposed(Guid taskId, LayerLink link)
    {
        mounted.RemoveAll(m => m.Link == link);
        if (IsOpen && taskId == openTaskId) refollow = true;
    }

    /// <summary>Opens the menu for the row that was pressed, closing any menu already open. Does nothing when the
    /// row has no entries.</summary>
    internal void Open(Guid taskId, ScribeRowPress press, OverlayState? reuseOverlay = null, ColorScheme? reuseColors = null)
    {
        var items = itemsFor(taskId);
        if (items.Count == 0) return;
        Close();

        // A reopen from the open menu reuses its overlay and colors; its original row's context may be gone.
        var overlay = reuseOverlay ?? Overlay.Of(press.Context);
        if (overlay is null) return;
        openOverlay = overlay;
        openColors = reuseColors ?? Theme.Of(press.Context).ColorScheme;

        openTaskId = taskId;
        openItems = items;
        openContext = press.Context;
        openShade = shade();
        placement = Place(press, items.Count);

        barrierEntry = new OverlayEntry(BuildBarrier());
        menuEntry = new OverlayEntry(BuildFollower());
        // Barrier first so the menu paints on top of it.
        overlay.Insert(barrierEntry);
        overlay.Insert(menuEntry);
    }

    /// <summary>Closes the menu if it is open.</summary>
    internal void Close()
    {
        pendingReopen = null;
        if (menuEntry is null && barrierEntry is null) return;
        menuEntry?.Remove();
        menuEntry = null;
        barrierEntry?.Remove();
        barrierEntry = null;
        refollow = false;
    }

    /// <summary>The current render object of the live row for <paramref name="taskId"/>, or null when none is mounted.</summary>
    internal RenderObject? RenderOf(Guid taskId)
    {
        for (int i = mounted.Count - 1; i >= 0; i--)
            if (mounted[i].TaskId == taskId) return mounted[i].RenderOf();
        return null;
    }

    /// <summary>Per-frame upkeep while a menu is open: closes it when no row for its <c>TaskId</c> is mounted
    /// (deleted, filtered out, collapsed away, or the view changed) or the row has scrolled out of view.</summary>
    internal void Tick()
    {
        if (pendingReopen is { } next)
        {
            pendingReopen = null;
            Open(next.TaskId, next.Press, next.Overlay, next.Colors);
        }
        if (!IsOpen) return;
        var live = LiveRow();
        if (live is null) { Close(); return; }
        if (refollow)
        {
            refollow = false;
            RebuildFollower();
        }
        if (live.Value.RenderOf() is { } ro && IsScrolledOutOfView(ro)) Close();
    }

    public void Dispose() => Close();

    // ---- Geometry ----

    private readonly record struct MountedRow(Guid TaskId, LayerLink Link, Func<RenderObject?> RenderOf);

    /// <summary>The menu's offset from the row's top-left corner, and whether it opens above the row.</summary>
    private readonly record struct Placement(Vector2 Offset, bool Above);

    private MountedRow? LiveRow()
    {
        for (int i = mounted.Count - 1; i >= 0; i--)
            if (mounted[i].TaskId == openTaskId) return mounted[i];
        return null;
    }

    /// <summary>Below the row at the pointer's x; above the row when the menu would pass the window's bottom edge,
    /// and shifted left when it would pass the right edge.</summary>
    private Placement Place(ScribeRowPress press, int entryCount)
    {
        var rowStyle = style();
        float width = EstimateWidth(rowStyle);
        float tileHeight = TileLabelSize * 1.4f + 2f * (rowStyle.UseCuneiform ? CuneiformTilePadY : TilePadY);
        int separators = openItems.Count(i => i.SeparatorBefore);
        float height = entryCount * tileHeight + separators * (1f + 2f * SeparatorMarginY) + 2f * PanelPadding;

        if (press.RowRender is not { } row)
            return new Placement(new Vector2(0f, MenuGap), Above: false);

        var origin = row.LocalToGlobal(Vector2.Zero);
        var far = row.LocalToGlobal(row.Size);
        float rowHeight = Math.Abs(far.Y - origin.Y);
        var windowSize = Root(row).Size;

        float dx = press.PointerX - origin.X;
        if (windowSize.X > 0f && origin.X + dx + width > windowSize.X) dx = windowSize.X - width - origin.X;
        dx = Math.Max(dx, -origin.X);

        bool above = windowSize.Y > 0f
            && origin.Y + rowHeight + MenuGap + height > windowSize.Y
            && origin.Y - MenuGap - height >= 0f;
        return new Placement(new Vector2(dx, above ? -MenuGap : rowHeight + MenuGap), above);
    }

    private float MinWidth(ScribeRowStyle rowStyle) => rowStyle.UseCuneiform ? CuneiformMenuMinWidth : MenuMinWidth;

    /// <summary>The menu's width for edge placement: the widest label's approximate width plus padding, between the
    /// minimum and the maximum. The panel itself sizes to its content within the same bounds.</summary>
    private float EstimateWidth(ScribeRowStyle rowStyle)
    {
        float min = MinWidth(rowStyle);
        float glyph = TileLabelSize * (rowStyle.UseCuneiform ? 1.1f : 0.6f);
        float widest = openItems.Max(i => Lang.Get(i.LabelLangKey).Length) * glyph + 2f * (TilePadX + PanelPadding) + 2f;
        return Math.Clamp(widest, min, min * MaxWidthFactor);
    }

    private static RenderObject Root(RenderObject ro)
    {
        while (ro.Parent is { } parent) ro = parent;
        return ro;
    }

    /// <summary>True when the row lies wholly outside its nearest scroll viewport.</summary>
    private static bool IsScrolledOutOfView(RenderObject row)
    {
        RenderViewport? viewport = null;
        for (var p = row.Parent; p is not null; p = p.Parent)
        {
            if (p is RenderViewport v) { viewport = v; break; }
        }
        if (viewport is null) return false;

        var viewportTop = (viewport.Parent?.LocalToGlobal(new Vector2(viewport.X, viewport.Y))
            ?? new Vector2(viewport.X, viewport.Y)).Y;
        float viewportBottom = viewportTop + viewport.Size.Y;
        var top = row.LocalToGlobal(Vector2.Zero).Y;
        float bottom = row.LocalToGlobal(row.Size).Y;
        return bottom <= viewportTop || top >= viewportBottom;
    }

    // ---- Overlay content ----

    private void RebuildFollower()
    {
        if (menuEntry is null) return;
        menuEntry.Widget = BuildFollower();
        menuEntry.MarkNeedsBuild();
    }

    private Widget BuildBarrier() => new Positioned(
        0, 0, 0, 0,
        child: new GestureDetector(
            onPress: OnBarrierPress,
            child: new Container(new BoxStyle { HitTestBehavior = HitTestBehavior.Opaque })));

    /// <summary>Any press outside the menu closes it. A right-press that lands on another row (or the same one)
    /// then opens that row's menu at the pointer.</summary>
    private void OnBarrierPress(PointerEvent e)
    {
        var context = openContext;
        var overlay = openOverlay;
        var colors = openColors;
        Close();
        if (e.Button != PointerButton.Right) return;
        pendingReopen = null;

        for (int i = mounted.Count - 1; i >= 0; i--)
        {
            if (mounted[i].RenderOf() is not { } row || IsScrolledOutOfView(row)) continue;
            var origin = row.LocalToGlobal(Vector2.Zero);
            var far = row.LocalToGlobal(row.Size);
            if (e.X < Math.Min(origin.X, far.X) || e.X > Math.Max(origin.X, far.X)
                || e.Y < Math.Min(origin.Y, far.Y) || e.Y > Math.Max(origin.Y, far.Y)) continue;
            // Reopened on the next frame: inserting the new overlay in the same dispatch that removed the old one
            // does not redraw until something else invalidates the screen.
            pendingReopen = (mounted[i].TaskId, new ScribeRowPress(context, row, e.X, e.Y), overlay, colors);
            return;
        }
    }

    private Widget BuildFollower()
    {
        var link = LiveRow()?.Link ?? new LayerLink();
        return new CompositedTransformFollower(
            link,
            offset: placement.Offset,
            child: BuildPanel(),
            showAbove: placement.Above);
    }

    private Widget BuildPanel()
    {
        var colors = openColors;
        var rowStyle = style();

        var entries = new List<Widget>();
        foreach (var item in openItems)
        {
            if (item.SeparatorBefore && entries.Count > 0)
            {
                // A Container with only a height has no width; stretching a column gives the line the panel's width.
                entries.Add(new Container(
                    style: new BoxStyle { Padding = EdgeInsets.Symmetric(SeparatorMarginY, 0f) },
                    child: new Column(
                        crossAxisAlignment: CrossAxisAlignment.Stretch,
                        mainAxisSize: MainAxisSize.Min,
                        children: new Widget[]
                        {
                            new Container(style: new BoxStyle { Height = 1f, Color = colors.OnSurface with { W = 0.35f } }),
                        })));
            }
            entries.Add(new ScribeRowMenuTile(
                label => BuildLabel(Lang.Get(item.LabelLangKey), label, rowStyle),
                colors,
                rowStyle.UseCuneiform ? CuneiformTilePadY : TilePadY,
                TilePadX,
                TileCorner,
                TileLabelSize,
                button => Choose(item, button)));
        }

        // One flat raised surface; the hovered entry gets its own accent fill inside the panel padding.
        float minWidth = MinWidth(rowStyle);
        return new ConstrainedBox(
            new LayoutConstraints(minWidth: minWidth, maxWidth: minWidth * MaxWidthFactor),
            new IntrinsicWidth(new ScribeGlobalTint(
                // The panel takes every press so one on its margin or separator never reaches the barrier.
                new GestureDetector(
                    onPress: _ => { },
                    child: new Container(
                    style: new BoxStyle
                    {
                        Color = colors.SurfaceHigh with { W = 1f },
                        BorderColor = colors.OnSurface with { W = 0.35f },
                        BorderThickness = 1f,
                        CornerRadius = new Vector4(TileCorner + 2f),
                        Padding = EdgeInsets.All(PanelPadding),
                    },
                    child: new Column(
                        spacing: 0f,
                        crossAxisAlignment: CrossAxisAlignment.Stretch,
                        mainAxisSize: MainAxisSize.Min,
                        children: entries))),
                brightness: openShade.Brightness,
                tintR: openShade.TintR,
                tintG: openShade.TintG,
                tintB: openShade.TintB)));
    }

    /// <summary>Choosing is left-button only: a right-click on a tile does nothing.</summary>
    private void Choose(ScribeRowMenuItem item, PointerButton button)
    {
        if (button != PointerButton.Left) return;
        if (item.CloseOnChoose) Close();
        item.OnChoose();
    }

    private static Widget BuildLabel(string label, TextStyle labelStyle, ScribeRowStyle rowStyle)
    {
        if (rowStyle.UseCuneiform && rowStyle.CuneiformBundle is { } bundle)
        {
            return new CuneiformText(
                text: label,
                fontSizeEm: labelStyle.FontSize,
                inkColor: labelStyle.Color,
                bundle: bundle,
                glow: default,
                glyphDrawScale: CuneiformMetrics.GlyphDrawScale);
        }
        return new Text(label, labelStyle);
    }
}

/// <summary>
/// A row's side of the context menu: the <see cref="LayerLink"/> the menu anchors to, the registration with the
/// dialog's <see cref="ScribeRowMenuHost"/>, and the right-press entry point the row's children forward to. A row
/// without a host (the Task Notice) still routes its children's presses here so a right-click does nothing.
/// </summary>
internal sealed class ScribeRowMenuAnchor
{
    private readonly LayerLink link = new();
    private ScribeRowMenuHost? host;
    private Guid taskId;
    private Func<RenderObject?>? renderOf;
    private float pointerX;
    private float pointerY;

    /// <summary>Registers the row with <paramref name="menuHost"/> (null when the surface has no row menu).</summary>
    internal void Mount(ScribeRowMenuHost? menuHost, Guid rowTaskId, Func<RenderObject?> rowRender)
    {
        host = menuHost;
        taskId = rowTaskId;
        renderOf = rowRender;
        host?.RowMounted(taskId, link, rowRender);
    }

    /// <summary>Re-registers the row when it is reused for a different task or host.</summary>
    internal void Update(ScribeRowMenuHost? menuHost, Guid rowTaskId)
    {
        if (ReferenceEquals(menuHost, host) && rowTaskId == taskId) return;
        var render = renderOf;
        Unmount();
        if (render is not null) Mount(menuHost, rowTaskId, render);
    }

    /// <summary>Unregisters the row.</summary>
    internal void Unmount() => host?.RowDisposed(taskId, link);

    /// <summary>Wraps the row so the menu can anchor to it; the row unchanged when it has no menu.</summary>
    internal Widget Wrap(Widget row) => host is null ? row : new CompositedTransformTarget(link, row);

    /// <summary>Remembers where the pointer is over the row, for a right-click that arrives without a position
    /// (a checkbox reports only its toggle).</summary>
    internal void NotePointer(PointerEvent e)
    {
        pointerX = e.X;
        pointerY = e.Y;
    }

    /// <summary>For a press handler: when <paramref name="e"/> is a right-button press, opens the row's menu at it
    /// and returns true so the caller does nothing else. Returns false for any other button.</summary>
    internal bool Secondary(PointerEvent e, BuildContext context)
    {
        if (e.Button != PointerButton.Right) return false;
        NotePointer(e);
        Open(context);
        return true;
    }

    /// <summary>Opens the row's menu at the last known pointer position. Does nothing for a row without a menu.</summary>
    internal void Open(BuildContext context)
    {
        host?.Open(taskId, new ScribeRowPress(context, renderOf?.Invoke(), pointerX, pointerY));
    }

    /// <summary>A checkbox toggle caused by a right-click: opens the menu instead of toggling. Returns false when
    /// the toggle came from another button, or the row has no menu.</summary>
    internal bool SecondaryToggle(BuildContext context)
    {
        // The recorded button comes from the dialogs that own a row menu; a surface without one never sees it.
        if (host is null || !ScribeRowPointer.RightDown) return false;
        Open(context);
        return true;
    }
}

/// <summary>
/// One entry of the row menu: transparent until hovered, then a rounded accent fill with the label switching to
/// the accent's text color. Choosing reports the pressed button; the menu acts on the left button only.
/// </summary>
internal sealed class ScribeRowMenuTile : StatefulWidget
{
    public ScribeRowMenuTile(
        Func<TextStyle, Widget> buildLabel, ColorScheme colors, float padY, float padX, float corner, float fontSize,
        Action<PointerButton> onChoose)
    {
        BuildLabel = buildLabel;
        Colors = colors;
        PadY = padY;
        PadX = padX;
        Corner = corner;
        FontSize = fontSize;
        OnChoose = onChoose;
    }

    public Func<TextStyle, Widget> BuildLabel { get; }
    public ColorScheme Colors { get; }
    public float PadY { get; }
    public float PadX { get; }
    public float Corner { get; }
    public float FontSize { get; }
    public Action<PointerButton> OnChoose { get; }

    public override State CreateState() => new ScribeRowMenuTileState();
}

internal sealed class ScribeRowMenuTileState : State<ScribeRowMenuTile>
{
    private bool hovered;

    public override Widget Build(BuildContext context)
    {
        var colors = Widget.Colors;
        var labelStyle = new TextStyle
        {
            FontSize = Widget.FontSize,
            Color = hovered ? colors.OnPrimary : colors.OnSurface,
            FontFamily = ScribeTaskFont.ButtonFamily,
        };

        return new MouseRegion(
            onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
            onExit: _ => { if (hovered) SetState(() => hovered = false); },
            child: new GestureDetector(
                onTap: e => Widget.OnChoose(e.Button),
                child: new Container(
                    style: new BoxStyle
                    {
                        Color = hovered ? colors.Primary : default,
                        CornerRadius = new Vector4(Widget.Corner),
                        Padding = EdgeInsets.Symmetric(Widget.PadY, Widget.PadX),
                        HitTestBehavior = HitTestBehavior.Opaque,
                    },
                    child: Widget.BuildLabel(labelStyle))));
    }
}
