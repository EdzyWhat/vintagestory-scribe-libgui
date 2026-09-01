using System.Collections.Generic;
using System.Linq;
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, TextAlignment, FontWeight
using Gui.Widgets.Basic;         // Text, Button, ButtonVariant
using Gui.Widgets.Framework;     // Widget, StatelessWidget, BuildContext, Theme, ColorScheme, ValueKey, Key
using Gui.Widgets.Layout;        // Row, Padding, Expanded, CrossAxisAlignment, MainAxisSize, Column, Center
using Gui.Widgets.Scroll;        // Scrollbar, SingleChildScrollView
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector4
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// One selectable row of a staged document on the Create Assignments tab (assignment-multi-item-creation
/// design.md D10). Reuses <see cref="ScribeReadRowData"/> as its data model and
/// <see cref="ScribeReadRow.BuildItemContent"/>'s per-kind rendering shape (task text, or an item
/// icon+name with a Tracker/Craft have-need counter) — but NOT <see cref="ScribeReadRow"/> itself: this is
/// a picker surface, not an editable/completable one, so it drops the pin affordance, the "switch to
/// editor" footer, cuneiform display, and the Done checkbox entirely, swapping in an independent Selected
/// checkbox instead. Stateless: selection lives on the dialog (<see cref="GuiDialogScribeAssignmentDesk"/>)
/// because the parent-cascades-to-subtasks rule (D11) needs to touch several rows' selection at once,
/// which a self-contained per-row State couldn't do.
/// </summary>
internal sealed class ScribeAssignmentStageRow : StatelessWidget
{
    public ScribeAssignmentStageRow(ScribeReadRowData data, bool selected, System.Action<System.Guid> onToggleSelected,
        ScribeRowStyle style, Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Data = data;
        Selected = selected;
        OnToggleSelected = onToggleSelected;
        Style = style;
    }

    public ScribeReadRowData Data { get; }
    public bool Selected { get; }
    public System.Action<System.Guid> OnToggleSelected { get; }
    public ScribeRowStyle Style { get; }

    /// <summary>Mirrors <see cref="ScribeReadRow.BuildItemContent"/>'s Tracker/Link/Craft rendering
    /// (icon + name, plus a have/need counter for the count-tracked kinds), minus the open-link gesture
    /// and quest-progress line — neither applies to a picker row.</summary>
    private Widget BuildItemContent(ColorScheme colors, ScribeRowStyle style)
    {
        float iconSize = ScribeRowConstants.ItemIconSize * (style.ControlSize / ScribeRowConstants.RowCheckboxSize);
        float lineHeight = ScribeRowControlNudge.TextLineHeight(style.FontSize);
        float bandHeight = ScribeLinkIcon.VisualSize(iconSize, Data.LinkTarget);
        Vector4 linkColor = style.LinkColor ?? colors.Primary;
        Widget icon = ScribeLinkIcon.Build(Data.DisplayStack, Data.LinkTarget, iconSize, linkColor, lineHeight, heightNeutral: false);
        Widget nameLabel = new Expanded(child: ScribeCenterIfShort.Name(
            ScribeItemLabel.Build(Data.Label, linkColor, style), style, bandHeight));

        var rowChildren = new List<Widget>();
        if (Data.IsLink)
        {
            rowChildren.Add(icon);
            rowChildren.Add(nameLabel);
        }
        else // Tracker or Craft parent: have/need counter on the left, then the item icon + name.
        {
            bool satisfied = Data.CurrentQuantity >= Data.TargetQuantity;
            rowChildren.Add(ScribeCenterIfShort.InBand(
                ScribeTrackerCounterText.Build(Data.CurrentQuantity, Data.TargetQuantity, satisfied,
                    strongColor: linkColor, mutedColor: colors.OnSurfaceVariant, lineHeight: lineHeight, cuneiform: style),
                bandHeight));
            rowChildren.Add(icon);
            rowChildren.Add(nameLabel);
        }

        return new Padding(
            EdgeInsets.Only(left: 4f, right: style.FieldPadX),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: rowChildren));
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        var style = Style;

        var children = new List<Widget>
        {
            new Padding(
                EdgeInsets.Only(top: ScribeRowControlNudge.CheckboxAndGripTop(style, Data.IsItemKind)),
                child: ScribeRowControlNudge.BuildTaskCheckbox(
                    context, style, Selected, _ => OnToggleSelected(Data.TaskId))),
        };

        Widget textChild = Data.IsItemKind
            ? BuildItemContent(colors, style)
            : ScribeTaskTextDisplay.Build(Data.Text, style, colors.OnSurface);
        children.Add(new Expanded(child: textChild));

        float subtaskIndent = Data.Depth > 0 ? style.SubtaskIndent : 0f;
        return new Padding(
            EdgeInsets.Only(
                left: style.RowHorizontalPadding + subtaskIndent,
                right: style.RowHorizontalPadding,
                top: style.RowVerticalPadding,
                bottom: style.RowVerticalPadding),
            child: new Row(
                spacing: style.CheckboxTextGap,
                crossAxisAlignment: CrossAxisAlignment.Start,
                mainAxisSize: MainAxisSize.Max,
                children: children));
    }
}

/// <summary>
/// The Create Assignments tab's staged-document content: a plain scrollable list of
/// <see cref="ScribeAssignmentStageRow"/>s, or the empty state when nothing is staged. Unlike
/// <see cref="ScribeReadContent"/> this is a picker surface with no live document to animate/collapse
/// against, so it is a bare <see cref="StatelessWidget"/> — no animation registry, no ghost rows.
/// </summary>
internal sealed class ScribeAssignmentStageContent : StatelessWidget
{
    public ScribeAssignmentStageContent(IReadOnlyList<ScribeReadRowData> rows, ISet<System.Guid> selectedTaskIds,
        System.Action<System.Guid> onToggleSelected, bool canPullFromDesk, System.Action onPullFromDesk,
        ScribeRowStyle style, ScrollController scrollController,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Rows = rows;
        SelectedTaskIds = selectedTaskIds;
        OnToggleSelected = onToggleSelected;
        CanPullFromDesk = canPullFromDesk;
        OnPullFromDesk = onPullFromDesk;
        Style = style;
        ScrollController = scrollController;
    }

    public IReadOnlyList<ScribeReadRowData> Rows { get; }
    public ISet<System.Guid> SelectedTaskIds { get; }
    public System.Action<System.Guid> OnToggleSelected { get; }
    /// <summary>Whether the Desk's own document has an eligible task to pull in (add-assignment-desk-own-
    /// tasks design.md D3) — gates the empty-state's "pull from Desk" button below.</summary>
    public bool CanPullFromDesk { get; }
    public System.Action OnPullFromDesk { get; }
    public ScribeRowStyle Style { get; }
    public ScrollController ScrollController { get; }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        if (Rows.Count == 0)
        {
            // "Pull from Desk" button (add-assignment-desk-own-tasks design.md D3/D7): shown below the
            // existing hint only when the Desk's own document actually has something to offer and the
            // dialog hasn't already activated it as this tab's source.
            var emptyChildren = new List<Widget>
            {
                new Text(
                    Lang.Get("scribe:scribe-assignment-stage-empty"),
                    new TextStyle { Color = colors.OnSurfaceVariant, SoftWrap = true, Align = TextAlignment.Center }),
            };
            if (CanPullFromDesk)
            {
                emptyChildren.Add(new Button(
                    child: new Text(Lang.Get("scribe:scribe-assignment-pull-from-desk"),
                        new TextStyle { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily }),
                    variant: ButtonVariant.Primary,
                    onTap: _ => OnPullFromDesk()));
            }
            return new Center(child: new Column(
                spacing: 10f,
                mainAxisSize: MainAxisSize.Min,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: emptyChildren));
        }

        return new Scrollbar(
            controller: ScrollController,
            child: new SingleChildScrollView(
                controller: ScrollController,
                child: new Column(
                    spacing: 0,
                    crossAxisAlignment: CrossAxisAlignment.Stretch,
                    mainAxisSize: MainAxisSize.Min,
                    children: Rows.Select(r => (Widget)new ScribeAssignmentStageRow(
                        data: r, selected: SelectedTaskIds.Contains(r.TaskId), onToggleSelected: OnToggleSelected,
                        style: Style, key: new Gui.Widgets.Framework.ValueKey<System.Guid>(r.TaskId))).ToList())))
        { AutoHide = false };
    }
}
