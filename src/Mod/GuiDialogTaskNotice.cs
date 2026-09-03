using System;
using System.Collections.Generic;
using System.Linq;
using Gui;                       // GuiBase, WindowConfig
using Gui.Rendering;              // EdgeInsets
using Gui.Rendering.Text;         // TextStyle, FontWeight
using Gui.Widgets.Basic;          // Text, Container, Button, ButtonVariant
using Gui.Widgets.Framework;      // Widget, ThemeData, ColorScheme, Theme
using Gui.Widgets.Gestures;       // ScrollController
using Gui.Widgets.Input;          // Dropdown, DropdownItem
using Gui.Widgets.Layout;         // Column, Row, Padding, Expanded, SizedBox, Align, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;        // Tooltip
using Gui.Widgets.Painting;       // BoxStyle
using Gui.Core.Layout;            // MainAxisSize
using OpenTK.Mathematics;         // Vector2, Vector4
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;    // Lang

namespace Scribe;

/// <summary>
/// The Task Notice's held-item review dialog (`task-notice-item` capability). A minimal standalone
/// window — NOT a <see cref="ScribeDialogBase"/> subclass, since that base class's
/// <see cref="IScribeDocumentHost"/> contract (editor write-through, lock, guestbook) is built for a real
/// editable document surface and doesn't fit a locked, one-shot Accept/Decline letter (see the
/// add-assignment-physical-delivery-mode design notes). Reuses <see cref="ScribeReadContent"/> for the
/// body (read-only, completion/pin inert) exactly as every other Scribe surface renders its Task/Tracker/
/// Link/Craft rows, and mirrors <see cref="ScribeInboxRow"/>'s Accept/Decline button shapes for the footer.
///
/// <para>The notice's document is a frozen snapshot read once at construction: it never changes while
/// this dialog is open (Accept/Decline both consume the item and close the dialog), so there is no
/// server-resync path to react to, unlike every editable Scribe surface.</para>
///
/// <para><b>Chrome (refine-task-notice-ux Decision 3):</b> a custom title bar + 3-column inset frame
/// visually matching <see cref="ScribeDialogBase.Layout"/>'s pattern (drag band, close button, symmetric
/// side-margin columns), backed by the <see cref="ScribeBackdrops.TaskNoticePage"/> parchment/scroll
/// backdrop instead of stock LibGUI <c>WindowFrame</c> chrome — implemented as NEW code local to this
/// file, deliberately not shared with <see cref="ScribeDialogBase"/> (the two surfaces' underlying
/// contracts, live host vs. frozen snapshot, differ enough that sharing would need its own abstraction
/// layer anyway). Sized from the parchment art's own aspect ratio and a fraction of the player's Pixel
/// Art Size setting, so the dialog reads noticeably smaller than a full Notebook/Lectern page.</para>
/// </summary>
public sealed class GuiDialogTaskNotice : GuiBase
{
    /// <summary>The parchment PNG's own aspect ratio (<c>H / W</c>) — a taller-than-wide scroll, unlike
    /// the notebook/lectern family's near-square art (design.md Decision 3).</summary>
    private const float NoticeAspectH = 130f / 105f;

    /// <summary>Tuned via <c>tools/task-notice-layout/index.html</c> against task 4.3's in-game look
    /// (design.md Decision 3, Open Questions): the notice reads noticeably smaller than a full
    /// Notebook/Lectern page — ~2/3 of the player's Pixel Art Size setting.</summary>
    private const float SizeScale = 0.667f;

    /// <summary>Proportions tuned via <c>tools/task-notice-layout/index.html</c> for this dialog's
    /// taller-than-wide scroll art (default <see cref="ScribeLayoutProportions"/> was authored for the
    /// Lectern/Notebook's near-square page and left the notice under-using its space).</summary>
    private static readonly ScribeLayoutProportions NoticeProportions = ScribeLayoutProportions.Default with
    {
        TitleBarFrac = 0.128f,
        InnerHFrac = 0.828f,
        SideColFrac = 0.062f,
        TitleBtnsWFrac = 0.940f,
        TitleBtnsHFrac = 0.080f,
    };

    private readonly ItemSlot slot;
    private readonly ScribeModSystem modSystem;
    private readonly ScribeDocument document;
    private readonly ScrollController scrollController = new();
    private readonly ScribeAnimationRegistry collapseRegistry = new();

    private List<ScribeAcceptCandidate> acceptCandidates = new();
    private int selectedCandidateIndex;
    private bool showAcceptPicker;

    public GuiDialogTaskNotice(ItemSlot slot, ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.slot = slot;
        this.modSystem = modSystem;
        ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out var doc);
        document = doc ?? new ScribeDocument();
        RefreshAcceptCandidates();
    }

    public override string DialogCode => "scribetasknotice";

    /// <summary>Matches the settings window's band (§ScribeSettingsDialog) — a held-item review dialog
    /// can open over a Lectern/Notebook/Tablet just as the settings gear can.</summary>
    public override double DrawOrder => 0.2;

    /// <summary>Computed inline (no <see cref="IScribeDocumentHost.GetLayout"/> — there is no host):
    /// <see cref="NoticeAspectH"/> fixed at the parchment art's own ratio, <c>W</c> derived from the
    /// player's Pixel Art Size setting scaled by <see cref="SizeScale"/>, proportions from
    /// <see cref="NoticeProportions"/>.</summary>
    private ScribeLayout ComputeLayout()
    {
        float w = modSystem.MySettings.PixelArtSize * SizeScale;
        return new ScribeLayout(w, NoticeAspectH, NoticeProportions);
    }

    /// <summary>Window size applied once at open, derived from the same <c>W</c>/<c>H</c> the body layout
    /// uses. <see cref="WindowConfig.DragHandleHeight"/> covers the whole custom title band so
    /// <see cref="GuiBase"/>'s own drag handling moves the window — no grip-drag reimplementation needed,
    /// since this title bar has no tooltip glyph to swallow the click (design.md Decision 3).</summary>
    protected override WindowConfig CreateWindowConfig()
    {
        var layout = ComputeLayout();
        return new()
        {
            Size = new Vector2(layout.W, layout.H),
            DragHandleHeight = layout.TitleBarH,
            Draggable = true,
            Resizable = false,
        };
    }

    private void RefreshAcceptCandidates()
    {
        acceptCandidates = ScribeAcceptCandidates.Compute(capi, null);
        selectedCandidateIndex = 0;
        showAcceptPicker = false;
    }

    protected override Widget Build()
    {
        var theme = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay);
        var colors = theme.ColorScheme;
        var style = ScribeRowStyle.FromSettings(modSystem.MySettings);
        var layout = ComputeLayout();

        var first = document.Blocks.FirstOrDefault();
        string fromLine = first?.Assignment is { } assignment
            ? Lang.Get("scribe:scribe-assignment-assigned-by", ResolvePlayerName(assignment.AssignerUid), assignment.AssignedDate)
            : "";

        var rows = document.Blocks.Select((b, i) =>
        {
            bool isItemKind = b.Kind is ScribeBlockKind.Tracker or ScribeBlockKind.Link or ScribeBlockKind.Craft;
            var (displayStack, displayName) = isItemKind
                ? ScribeItemRef.ResolveDisplay(capi.World, b.TargetItemCode ?? b.LinkTarget, b.LinkLabel)
                : (null, null);
            return new ScribeReadRowData(
                Index: i, Kind: b.Kind, Done: false, Pinned: false, TaskId: b.TaskId, Text: b.Text,
                DisplayStack: displayStack, DisplayName: displayName,
                TargetQuantity: b.TargetQuantity, CurrentQuantity: 0, LinkTarget: b.LinkTarget, Depth: b.Depth,
                IsAcceptedAssignment: false);
        }).ToList();

        Widget body = new ScribeReadContent(
            blocks: rows,
            onToggleTask: _ => { },
            onTogglePinned: _ => { },
            onOpenLink: _ => { },
            onSwitchToEditor: () => { },
            footerButtonPadding: EdgeInsets.Zero,
            style: style,
            scrollController: scrollController,
            collapseRegistry: collapseRegistry,
            onDepartureSettled: () => { },
            currentShade: default,
            readOnly: true,
            completionAndPinLive: false);

        Widget centerColumn = new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                new Column(
                    crossAxisAlignment: CrossAxisAlignment.Stretch,
                    mainAxisSize: MainAxisSize.Min,
                    spacing: 4f,
                    children: new Widget[]
                    {
                        new Text(fromLine,
                            new TextStyle { FontSize = 13, Color = colors.OnSurfaceVariant, SoftWrap = true }),
                        new Text(Lang.Get("scribe:scribe-tasknotice-accept-prompt"),
                            new TextStyle { FontSize = 13, Color = colors.OnSurface, SoftWrap = true }),
                    }),
                new Expanded(child: body),
                BuildActionSection(colors, theme),
            });

        return new Theme(theme,
            child: WrapBackdrop(layout, BuildOuterArtBox(layout, colors, centerColumn)));
    }

    /// <summary>Mirrors <see cref="ScribeDialogBase.Layout.WrapBackdrop"/>'s pattern (missing-asset
    /// flat-color fallback included) for this standalone dialog's own backdrop spec.</summary>
    private Widget WrapBackdrop(ScribeLayout layout, Widget tree)
    {
        if (!modSystem.MySettings.PixelArtDisplay)
            return new SizedBox(width: layout.W, height: layout.H, child: tree);

        var bmp = modSystem.GetBackdropBitmap(ScribeBackdrops.TaskNoticePage);
        if (bmp is not null)
        {
            return new ScribePixelArtBackdrop(bmp,
                new SizedBox(width: layout.W, height: layout.H, child: tree));
        }
        var style = new BoxStyle { Color = new Vector4(0.85f, 0.78f, 0.62f, 1.0f), Width = layout.W, Height = layout.H };
        return new ScribeResetPaintColor(new Container(style: style, child: tree));
    }

    /// <summary>The parchment art's contents: the draggable title band over the 3-column inset frame,
    /// mirroring <see cref="ScribeDialogBase.Layout.BuildOuterArtBox"/>'s shape.</summary>
    private Widget BuildOuterArtBox(ScribeLayout layout, ColorScheme colors, Widget centerColumn) =>
        new Column(
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                BuildTitleBar(layout, colors),
                BuildSectionInnerBox(layout, centerColumn),
            });

    /// <summary>A bespoke title bar (title text + close button), matching
    /// <see cref="ScribeDialogBase.Layout.BuildTitleBar"/>'s visual style but simplified — no pencil, no
    /// expand/collapse-all, no separate grip glyph (the whole band is already the drag zone via
    /// <see cref="WindowConfig.DragHandleHeight"/>, and there is no tooltip glyph here to swallow a
    /// press the way <see cref="ScribeDialogBase"/>'s grip tooltip does).</summary>
    private Widget BuildTitleBar(ScribeLayout layout, ColorScheme colors)
    {
        float titleFont = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale) * 1.5f;
        var titleStyle = new TextStyle
        {
            FontSize = titleFont,
            FontFamily = ScribeRowControlNudge.TitleFontFamily,
            Weight = FontWeight.Bold,
            Color = colors.OnSurface,
        };

        Widget titleText = new Expanded(child: new Text(Lang.Get("scribe:item-tasknotice"), titleStyle));
        Widget closeButton = new ScribeRowButton(
            iconName: "scribeclose",
            iconColor: colors.Error,
            size: ScribeRowConstants.RowCheckboxSize * 1.4f,
            onTap: () => TryClose());

        Widget titleRow = new Row(
            mainAxisAlignment: MainAxisAlignment.SpaceBetween,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { titleText, closeButton });

        return new SizedBox(
            width: layout.W,
            height: layout.TitleBarH,
            child: new Align(
                Alignment.BottomCenter,
                child: new SizedBox(
                    width: layout.TitleBtnsW,
                    height: layout.TitleBtnsH,
                    child: new Padding(
                        EdgeInsets.Only(left: 0.04f * layout.W, right: 0.04f * layout.W),
                        child: titleRow))));
    }

    /// <summary>The 3-column inset frame (side margins sized proportionally to the art width, matching
    /// <see cref="ScribeDialogBase.Layout.BuildSectionInnerBox"/>'s proportions) so content sits inset
    /// from the parchment art's border instead of touching its edge.</summary>
    private Widget BuildSectionInnerBox(ScribeLayout layout, Widget centerColumn) =>
        new SizedBox(
            width: layout.InnerW,
            height: layout.InnerH,
            child: new Row(
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    new SizedBox(width: layout.SideColW),
                    new SizedBox(width: layout.TasksColW, child: centerColumn),
                    new SizedBox(width: layout.SideColW),
                }));

    private string ResolvePlayerName(string uid) => capi.World.PlayerByUid(uid)?.PlayerName ?? uid;

    /// <summary>Decline + Accept, restructured (refine-task-notice-ux 3.4) so the multi-candidate picker —
    /// when shown — renders as its OWN full-width row ABOVE the Decline/Accept row, instead of stacked
    /// inside one of that row's cells (the original overflow: a <c>Column</c> squeezed into the leftover
    /// width next to Decline could push both buttons off the visible dialog area). Decline/Accept both
    /// stay natural-width (a <c>Row</c> never stretches its children's cross-axis width) regardless of
    /// which Accept shape is showing.</summary>
    private Widget BuildActionSection(ColorScheme colors, ThemeData theme)
    {
        var candidates = acceptCandidates;
        Widget decline = ActionButton("scribe:scribe-assignment-action-decline", ButtonVariant.Danger, () =>
        {
            modSystem.SendTaskNoticeAction(slot, ScribeAssignmentAction.Decline, null);
            TryClose();
        }, colors);

        Widget acceptControl;
        Widget? picker = null;

        if (candidates.Count == 0)
        {
            acceptControl = DisabledAcceptButton(colors);
        }
        else if (candidates.Count == 1)
        {
            acceptControl = ActionButton("scribe:scribe-assignment-action-accept", ButtonVariant.Primary,
                () => AcceptOnto(candidates[0]), colors);
        }
        else if (!showAcceptPicker)
        {
            acceptControl = ActionButton("scribe:scribe-assignment-action-accept", ButtonVariant.Primary,
                () => { showAcceptPicker = true; ForceRebuild(); }, colors);
        }
        else
        {
            int idx = Math.Clamp(selectedCandidateIndex, 0, candidates.Count - 1);
            picker = new Dropdown<int>(
                value: idx,
                items: candidates.Select((c, i) => new DropdownItem<int> { Value = i, Label = c.Label }).ToList(),
                onChanged: v => { selectedCandidateIndex = v; ForceRebuild(); },
                style: theme.DropdownStyle);
            acceptControl = ActionButton("scribe:scribe-assignment-action-accept", ButtonVariant.Primary,
                () => AcceptOnto(candidates[idx]), colors);
        }

        Widget buttonsRow = new Row(
            spacing: 8f,
            mainAxisAlignment: MainAxisAlignment.End,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { decline, acceptControl });

        if (picker is null) return buttonsRow;

        return new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            spacing: 6f,
            children: new Widget[] { picker, buttonsRow });
    }

    private Widget DisabledAcceptButton(ColorScheme colors) =>
        new Tooltip(
            child: new Button(
                child: new Text(Lang.Get("scribe:scribe-assignment-action-accept"),
                    new TextStyle { FontSize = 13, Color = colors.OnSurfaceVariant, FontFamily = ScribeTaskFont.ButtonFamily }),
                variant: ButtonVariant.Secondary,
                enabled: false),
            content: new Padding(EdgeInsets.All(6), child: new Text(
                Lang.Get("scribe:scribe-assignment-no-eligible-target"),
                new TextStyle { FontSize = 12, Color = colors.OnSurface, SoftWrap = true })),
            useGlobalOverlay: true);

    private void AcceptOnto(ScribeAcceptCandidate candidate)
    {
        modSystem.SendTaskNoticeAction(slot, ScribeAssignmentAction.Accept, candidate);
        TryClose();
    }

    private static Widget ActionButton(string labelKey, ButtonVariant variant, Action onTap, ColorScheme colors)
    {
        Vector4 fg = variant == ButtonVariant.Danger ? colors.OnError : colors.OnPrimary;
        return new Button(
            child: new Text(Lang.Get(labelKey), new TextStyle { FontSize = 13, Color = fg, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: variant,
            onTap: _ => onTap());
    }

    public override void Dispose()
    {
        scrollController.Dispose();
        collapseRegistry.Dispose();
        base.Dispose();
    }
}
