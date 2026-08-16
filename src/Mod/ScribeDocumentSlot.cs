using System;
using System.Collections.Generic;
using Gui.Core.Layout;           // MainAxisSize
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Basic;         // Container, Text
using Gui.Widgets.Events;        // PointerEvent, PointerButton
using Gui.Widgets.Framework;     // Widget, StatelessWidget, BuildContext, Theme, ColorScheme, Key
using Gui.Widgets.Input;         // GestureDetector
using Gui.Widgets.Inventory;     // SlotController, ItemSlotOverlay, ItemSlotStyle
using Gui.Widgets.Layout;        // Column, Padding, CrossAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using OpenTK.Mathematics;        // Vector4
using Scribe.Core;               // ScribeDocument, ScribeBlockKind
using Vintagestory.API.Common;   // ItemSlot, ItemStack, EnumMouseButton
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// A compact replacement for LibGUI's <see cref="FlatItemSlot"/> in the Scriptorium's Scribe-items-only
/// inventory (refine-scribe-hover-tooltips D3). It renders the same slot box + item stack, but swaps the
/// stock item tooltip — a fixed 350px panel of name/description/durability/quantity, baked un-injectably into
/// <c>ItemSlotGestureLayer</c> — for a small Scribe <b>document summary card</b> (name, title, per-kind
/// counts, or a "never opened" state; see <see cref="BuildSummaryCard"/>).
///
/// <para><b>Why a bespoke slot.</b> The stock tooltip is not reachable while using <see cref="FlatItemSlot"/>
/// (its content and 350px width are hard-coded), so the only way to control both the hover content and its
/// size from mod code is to compose the slot ourselves out of LibGUI's public parts: <see cref="ItemSlotOverlay"/>
/// for the item render, a <see cref="GestureDetector"/> forwarding to the existing <see cref="SlotController"/>,
/// and our own <see cref="Tooltip"/>. Exactly one tooltip is produced (ours) — we deliberately do NOT embed a
/// <see cref="FlatItemSlot"/>, which would bring its own.</para>
///
/// <para><b>Interaction model = click-to-grab / click-to-place only.</b> On press we forward
/// <see cref="SlotController.ClickSlot"/> (the same call the stock gesture layer makes) which implements the
/// vanilla model: left-click grabs/places against the mouse-held stack, right-click places-one/splits; the
/// wheel forwards <see cref="SlotController.WheelSlot"/>. We intentionally omit
/// <c>BeginDrag</c>/<c>DragEnterSlot</c>/<c>EndDrag</c> — click-hold-drag distribution is not the inventory
/// mechanic here (user decision): a click grabs, a second click places. The Scribe-only accept filter is
/// unaffected — it lives on the inventory's <c>CanHold</c> (server-authoritative) and on the controller's
/// <see cref="SlotController.CanClickSlot"/> predicate, both of which <see cref="SlotController.ClickSlot"/>
/// already honors.</para>
///
/// <para>The cosmetic hover-bounce/punch animation the stock slot plays is dropped (not load-bearing).</para>
/// </summary>
internal sealed class ScribeDocumentSlot : StatelessWidget
{
    /// <summary>How long the pointer must rest on the slot before the summary card appears — matches the
    /// stock item tooltip's 350ms so the hover feel is unchanged.</summary>
    private static readonly TimeSpan CardDelay = TimeSpan.FromMilliseconds(350);

    private readonly ItemSlot? slot;
    private readonly SlotController controller;
    private readonly ItemSlotStyle style;
    private readonly ColorScheme colors;
    private readonly ScribeAmbientLightSampler.Shade shade;

    public ScribeDocumentSlot(ItemSlot? slot, SlotController controller, ItemSlotStyle style,
        ColorScheme colors, ScribeAmbientLightSampler.Shade shade, Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        this.slot = slot;
        this.controller = controller;
        this.style = style;
        this.colors = colors;
        this.shade = shade;
    }

    public override Widget Build(BuildContext context)
    {
        var themeSlot = Theme.Of(context).ItemSlotStyle;

        // Mirror FlatItemSlot.FlatBackground's box so it still reads as a slot (its veiled parchment fill +
        // faint border), minus the hover-highlight animation. Our slotStyle carries the semi-opaque veil as
        // BackgroundColor (see GuiDialogScribeScriptorium.BuildWatermarkedSlot).
        Vector4 fill = style.BackgroundColor ?? themeSlot.BackgroundColor ?? new Vector4(0f, 0f, 0f, 0.4f);
        Vector4 border = style.BorderColor ?? themeSlot.BorderColor ?? new Vector4(1f, 1f, 1f, 0.2f);

        Widget box = new Container(
            style: new BoxStyle
            {
                Width = style.Size,
                Height = style.Size,
                Color = fill,
                BorderThickness = 1f,
                BorderColor = border,
                CornerRadius = new Vector4(2f),
            },
            child: new ItemSlotOverlay(slot, style.Size, style.HoverColor, style.Padding));

        Widget gesture = new GestureDetector(
            child: box,
            onEnter: OnEnter,
            onExit: OnExit,
            onPress: OnPress,
            onWheel: OnWheel);

        // Only show a card for a filled slot; an empty slot gets an inert placeholder + effectively-infinite
        // wait so no bubble ever appears (the stock slot uses the same trick).
        ItemStack? stack = slot?.Itemstack;
        bool hasItem = stack?.Collectible != null;
        Widget content = hasItem
            ? ScribeGlobalTint.ForHover(BuildSummaryCard(stack!, colors), shade)
            : new SizedBox();

        return new Tooltip(
            child: gesture,
            content: content,
            waitDuration: hasItem ? CardDelay : TimeSpan.FromHours(1),
            useGlobalOverlay: true);
    }

    private void OnEnter(PointerEvent e)
    {
        if (slot != null) controller.EnterSlot(slot);
    }

    private void OnExit(PointerEvent e) => controller.LeaveSlot();

    private void OnPress(PointerEvent e)
    {
        if (slot == null) return;
        EnumMouseButton? button = e.Button switch
        {
            PointerButton.Left => EnumMouseButton.Left,
            PointerButton.Right => EnumMouseButton.Right,
            PointerButton.Middle => EnumMouseButton.Middle,
            _ => null,
        };
        // Click-to-grab / click-to-place: ClickSlot moves the stack between the slot and the mouse-held
        // cursor stack (right-click = place-one/split). No BeginDrag — drag-distribute is out of scope.
        if (button.HasValue) controller.ClickSlot(slot, button.Value);
    }

    private void OnWheel(PointerEvent e)
    {
        if (slot != null && controller.WheelSlot(slot, e.Delta > 0f ? 1 : -1)) e.Handled = true;
    }

    /// <summary>Build the compact summary card for a filled Scribe slot (refine-scribe-hover-tooltips D4).
    /// A pure read of the stack's stored document, so the copy/paste change can reuse it as the
    /// import/export preview rather than re-deriving counts:
    /// <list type="bullet">
    /// <item><b>No document</b> (freshly crafted, never opened): item name + an explicit "never opened" line —
    /// NOT a title placeholder + all-zero counts, which would imply an opened-but-empty document.</item>
    /// <item><b>Has a document</b>: item name, the document title (untitled placeholder when it is the model
    /// default), and one line per present block kind with its count.</item>
    /// </list></summary>
    internal static Widget BuildSummaryCard(ItemStack stack, ColorScheme colors)
    {
        var name = stack.GetName();
        var lines = new List<Widget>
        {
            new Text(name, new TextStyle { FontSize = 14, Color = colors.OnBackground }),
        };

        if (ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
        {
            lines.Add(new Text(ScribeTooltip.FormatTitleLine(doc.Title),
                new TextStyle { FontSize = 13, Color = colors.OnBackground }));

            AppendCountLines(lines, doc, colors);
        }
        else
        {
            lines.Add(new Text(Lang.Get("scribe:hover-card-never-opened"),
                new TextStyle { FontSize = 13, Color = colors.OnSurfaceVariant }));
        }

        return new Padding(
            EdgeInsets.All(6),
            child: new Column(
                spacing: 2f,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Min,
                children: lines));
    }

    /// <summary>Append one "&lt;label&gt;: &lt;count&gt;" line per block kind that is present in the document
    /// (omitting absent kinds), or a single "empty" line when an opened document has no blocks at all — so an
    /// opened-but-empty document reads distinctly from a never-opened one.</summary>
    private static void AppendCountLines(List<Widget> lines, ScribeDocument doc, ColorScheme colors)
    {
        int tasks = 0, notes = 0, trackers = 0, links = 0;
        foreach (var block in doc.Blocks)
        {
            switch (block.Kind)
            {
                case ScribeBlockKind.Task: tasks++; break;
                case ScribeBlockKind.Text: notes++; break;
                case ScribeBlockKind.Tracker: trackers++; break;
                case ScribeBlockKind.Link: links++; break;
            }
        }

        var countStyle = new TextStyle { FontSize = 13, Color = colors.OnSurfaceVariant };
        int before = lines.Count;
        AddCount(lines, "scribe:hover-card-tasks", tasks, countStyle);
        AddCount(lines, "scribe:hover-card-notes", notes, countStyle);
        AddCount(lines, "scribe:hover-card-trackers", trackers, countStyle);
        AddCount(lines, "scribe:hover-card-links", links, countStyle);

        if (lines.Count == before)
        {
            lines.Add(new Text(Lang.Get("scribe:hover-card-empty"), countStyle));
        }
    }

    private static void AddCount(List<Widget> lines, string labelKey, int count, TextStyle style)
    {
        if (count <= 0) return;
        lines.Add(new Text($"{Lang.Get(labelKey)}: {count}", style));
    }
}
