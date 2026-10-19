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
/// A compact replacement for LibGUI's <see cref="FlatItemSlot"/> in the Scriptorium's Scribe-items-only inventory: same slot box
/// and item stack, but with a small Scribe <b>document summary card</b> (see <see cref="BuildSummaryCard"/>) in place of the stock
/// 350px tooltip baked into <c>ItemSlotGestureLayer</c>.
/// <para>Composed from LibGUI's public parts (<see cref="ItemSlotOverlay"/>, a <see cref="GestureDetector"/> forwarding to the
/// <see cref="SlotController"/>, our own <see cref="Tooltip"/>); it must not embed a <see cref="FlatItemSlot"/>, which would add its own tooltip.</para>
/// <para>Click-to-grab / click-to-place only: press forwards <see cref="SlotController.ClickSlot"/> and the wheel forwards
/// <see cref="SlotController.WheelSlot"/>; drag distribution is intentionally omitted. The Scribe-only accept filter lives on the
/// inventory's <c>CanHold</c> and <see cref="SlotController.CanClickSlot"/>, which <c>ClickSlot</c> already honors.</para>
/// </summary>
internal sealed class ScribeDocumentSlot : StatelessWidget
{
    /// <summary>Hover delay before the summary card appears (half the stock tooltip's 350ms).</summary>
    private static readonly TimeSpan CardDelay = TimeSpan.FromMilliseconds(175);

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

        // Mirror FlatItemSlot.FlatBackground's box (veiled parchment fill + faint border) without the hover-highlight animation;
        // slotStyle's BackgroundColor carries the veil.
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

        // Only a FILLED slot gets a hover card. An empty slot returns the bare gesture with no Tooltip: a mounted-idle Tooltip would
        // leave its shown bubble hanging after the item is grabbed (the widget type change unmounts it).
        ItemStack? stack = slot?.Itemstack;
        bool hasItem = stack?.Collectible != null;
        if (!hasItem) return gesture;

        // ShadedTooltip shades the bubble and content by the live illumination shade; it wraps content in ForHover itself, so pass the raw card.
        return ScribeGlobalTint.ShadedTooltip(
            child: gesture,
            content: BuildSummaryCard(stack!, colors, controller.World),
            baseTheme: Theme.Of(context),
            shade: shade,
            waitDuration: CardDelay);
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
        // ClickSlot moves the stack between slot and mouse-held cursor stack (right-click = place-one/split); no drag-distribute.
        if (button.HasValue) controller.ClickSlot(slot, button.Value);
    }

    private void OnWheel(PointerEvent e)
    {
        if (slot != null && controller.WheelSlot(slot, e.Delta > 0f ? 1 : -1)) e.Handled = true;
    }

    /// <summary>Builds the summary card for a filled Scribe slot, a pure read of the stored document:
    /// <list type="bullet">
    /// <item><b>No document</b>: name + "No contents". A document persists only on first edit, so an opened-but-untouched item is
    /// indistinguishable from a never-opened one.</item>
    /// <item><b>Sealed Task Notice</b>: who assigned it and who it is addressed to (same lang keys as
    /// <see cref="ItemScribeTaskNotice.GetHeldItemInfo"/> and <see cref="GuiDialogTaskNotice"/>); it has no custom title.</item>
    /// <item><b>Other document</b>: name, title (untitled placeholder for the default), and one line per present block kind with its count.</item>
    /// </list></summary>
    internal static Widget BuildSummaryCard(ItemStack stack, ColorScheme colors, IWorldAccessor world)
    {
        var name = stack.GetName();
        var lines = new List<Widget>
        {
            new Text(name, new TextStyle { FontSize = 14, Color = colors.OnBackground }),
        };

        bool hasDoc = ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null;

        if (stack.Collectible is ItemScribeTaskNotice && ItemScribeTaskNotice.IsSealed(stack))
        {
            AppendTaskNoticeLines(lines, doc!, colors, world);
        }
        else if (hasDoc)
        {
            lines.Add(new Text(ScribeTooltip.FormatTitleLine(doc!.Title),
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

    /// <summary>Appends the "Assigned by" and "Addressed to" lines for a sealed Task Notice; adds nothing if the first block carries no assignment (defensive).</summary>
    private static void AppendTaskNoticeLines(List<Widget> lines, ScribeDocument doc, ColorScheme colors, IWorldAccessor world)
    {
        var assignment = doc.Blocks.Count > 0 ? doc.Blocks[0].Assignment : null;
        if (assignment is null) return;

        var lineStyle = new TextStyle { FontSize = 13, Color = colors.OnBackground };
        var modSystem = world.Api.ModLoader.GetModSystem<ScribeModSystem>();
        string assignerName = modSystem.ResolvePlayerName(assignment.AssignerUid);
        string recipientName = modSystem.ResolvePlayerName(assignment.TargetPlayerUid);

        lines.Add(new Text(Lang.Get("scribe:scribe-assignment-assigned-by", assignerName,
            AssignmentDisplay.Date(assignment.AssignedDate, assignment.AssignedTimestamp, world)), lineStyle));
        lines.Add(new Text(Lang.Get("scribe:scribe-tasknotice-addressed-to", recipientName), lineStyle));
    }

    /// <summary>Appends one "label: count" line per present block kind, or a single "empty" line when an opened document has no blocks.</summary>
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
