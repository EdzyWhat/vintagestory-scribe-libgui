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
using Scribe.Core;                // ScribeLinkTarget
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// One selectable row of a staged document on the Create Assignments tab. Reuses <see cref="ScribeReadRowData"/>
/// and <see cref="ScribeReadRow.BuildItemContent"/>'s per-kind rendering shape, but not <see cref="ScribeReadRow"/>
/// itself: this is a picker, so it drops the pin affordance, "switch to editor" footer, cuneiform display, and
/// Done checkbox in favor of an independent Selected checkbox. Stateless: selection lives on the dialog
/// (<see cref="GuiDialogScribeAssignmentDesk"/>) because the parent-cascades-to-subtasks rule touches several rows at once.
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

    /// <summary>Mirrors <see cref="ScribeReadRow.BuildItemContent"/>'s Tracker/Link/Craft rendering, minus the
    /// open-link gesture and quest-progress line.</summary>
    private Widget BuildItemContent(ColorScheme colors, ScribeRowStyle style)
    {
        float iconSize = ScribeRowConstants.ItemIconSize * (style.ControlSize / ScribeRowConstants.RowCheckboxSize);
        float lineHeight = ScribeRowControlNudge.TextLineHeight(style.FontSize);
        // The leading checkbox is a selection control, so the quest-marker icon can't use that slot; a Quest
        // Link shows it inline instead of the generic book glyph.
        bool isQuestLink = ScribeLinkTarget.IsQuest(Data.LinkTarget);
        float bandHeight = ScribeLinkIcon.VisualSize(iconSize, Data.LinkTarget);
        Vector4 linkColor = isQuestLink
            ? style.QuestLinkColor ?? ScribeTheme.QuestLinkAccent
            : style.LinkColor ?? colors.Primary;
        Widget icon = isQuestLink
            ? new ScribeVsIconGlyph("scribequest", ScribeLinkIcon.VisualSize(iconSize, Data.LinkTarget), linkColor)
            : ScribeLinkIcon.Build(Data.DisplayStack, Data.LinkTarget, iconSize, linkColor, lineHeight, heightNeutral: false);
        Widget nameLabel = new Expanded(child: ScribeCenterIfShort.Name(
            ScribeItemLabel.Build(Data.Label, linkColor, style), style, bandHeight));

        var rowChildren = new List<Widget>();
        if (Data.IsLink)
        {
            rowChildren.Add(icon);
            rowChildren.Add(nameLabel);
        }
        else // Tracker or Craft parent: have/need counter, then item icon + name.
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
/// <see cref="ScribeAssignmentStageRow"/>s, or the empty state when nothing is staged. A picker surface with
/// no animation registry or ghost rows, unlike <see cref="ScribeReadContent"/>.
/// </summary>
internal sealed class ScribeAssignmentStageContent : StatelessWidget
{
    public ScribeAssignmentStageContent(IReadOnlyList<ScribeReadRowData> rows, ISet<System.Guid> selectedTaskIds,
        System.Action<System.Guid> onToggleSelected, bool canPullFromDesk, System.Action onCreateTasks,
        System.Action onPullFromDesk,
        ScribeRowStyle style, ScrollController scrollController,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Rows = rows;
        SelectedTaskIds = selectedTaskIds;
        OnToggleSelected = onToggleSelected;
        CanPullFromDesk = canPullFromDesk;
        OnCreateTasks = onCreateTasks;
        OnPullFromDesk = onPullFromDesk;
        Style = style;
        ScrollController = scrollController;
    }

    public IReadOnlyList<ScribeReadRowData> Rows { get; }
    public ISet<System.Guid> SelectedTaskIds { get; }
    public System.Action<System.Guid> OnToggleSelected { get; }
    /// <summary>Whether the Desk's own document has an eligible task to pull in (gates the empty-state button).</summary>
    public bool CanPullFromDesk { get; }
    /// <summary>Enters the Desk's local Editor through the dialog-owned lock-aware path.</summary>
    public System.Action OnCreateTasks { get; }
    public System.Action OnPullFromDesk { get; }
    public ScribeRowStyle Style { get; }
    public ScrollController ScrollController { get; }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        if (Rows.Count == 0)
        {
            // The always-present Editor route precedes the conditional "Pull from Desk" action; the dialog owns both callbacks.
            var emptyChildren = new List<Widget>
            {
                new Text(
                    Lang.Get("scribe:scribe-assignment-stage-empty"),
                    new TextStyle { Color = colors.OnSurfaceVariant, SoftWrap = true, Align = TextAlignment.Center }),
                new Button(
                    child: new Text(Lang.Get("scribe:scribe-assignment-create-tasks"),
                        new TextStyle { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily }),
                    variant: ButtonVariant.Primary,
                    onTap: _ => OnCreateTasks()),
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
