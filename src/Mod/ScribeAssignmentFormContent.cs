using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Basic;         // Text, Button, ButtonVariant, Container
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ColorScheme
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Input;         // Checkbox, Dropdown, DropdownItem
using Gui.Widgets.Layout;        // Column, Row, Padding, Expanded, CrossAxisAlignment, SizedBox
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle, BoxShadow
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2, Vector4
using Scribe.Core;
using Vintagestory.API.Config;   // Lang

namespace Scribe;

/// <summary>
/// The Assignment Desk's Create Assignments tab: a staging slot for an existing Scribe item, that item's rows
/// rendered Read-view-style with independent Selected checkboxes (<see cref="ScribeAssignmentStageContent"/>),
/// a "Delete from source on send" toggle, and the target-player picker + batch-Send button. Staging and
/// selection only; Sent history lives on its own tab (<see cref="ScribeDialogBase.BuildSentAssignmentHistoryContent"/>).
/// </summary>
internal sealed class ScribeAssignmentFormContent : StatefulWidget
{
    public ScribeAssignmentFormContent(
        IReadOnlyList<(string Uid, string Name)> targetPlayers,
        string? selectedTargetUid,
        Action<string> onTargetSelected,
        Widget stagingSlot,
        IReadOnlyList<ScribeReadRowData> stagedRows,
        ISet<Guid> selectedTaskIds,
        Action<Guid> onToggleSelected,
        bool deleteFromSource,
        Action<bool> onToggleDeleteFromSource,
        Action onSendBatch,
        bool sending,
        bool canPullFromDesk,
        Action onCreateTasks,
        Action onPullFromDesk,
        ScribeRowStyle style,
        ScrollController scrollController,
        ScribeDeliveryMode deliveryMode,
        ScribeDeliveryChoice deliveryChoice,
        Action<ScribeDeliveryChoice> onDeliveryChoiceChanged,
        Widget? noticeSupplySlot,
        Widget? noticeOutputSlot,
        Action onOpenDeliveryInfo,
        bool showSubtitleRow = true,
        Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        TargetPlayers = targetPlayers;
        SelectedTargetUid = selectedTargetUid;
        OnTargetSelected = onTargetSelected;
        StagingSlot = stagingSlot;
        StagedRows = stagedRows;
        SelectedTaskIds = selectedTaskIds;
        OnToggleSelected = onToggleSelected;
        DeleteFromSource = deleteFromSource;
        OnToggleDeleteFromSource = onToggleDeleteFromSource;
        OnSendBatch = onSendBatch;
        Sending = sending;
        CanPullFromDesk = canPullFromDesk;
        OnCreateTasks = onCreateTasks;
        OnPullFromDesk = onPullFromDesk;
        Style = style;
        ScrollController = scrollController;
        DeliveryMode = deliveryMode;
        DeliveryChoice = deliveryChoice;
        OnDeliveryChoiceChanged = onDeliveryChoiceChanged;
        NoticeSupplySlot = noticeSupplySlot;
        NoticeOutputSlot = noticeOutputSlot;
        OnOpenDeliveryInfo = onOpenDeliveryInfo;
        ShowSubtitleRow = showSubtitleRow;
    }

    /// <summary>Every other online player, as (uid, display name). Offline players can't be targeted (the
    /// client has no UID-to-name directory for them).</summary>
    public IReadOnlyList<(string Uid, string Name)> TargetPlayers { get; }
    /// <summary>Dialog-owned target selection, lifted out of this State so the dialog can react to a target
    /// change (Hybrid range check, resetting a manual delivery override). Null only when <see cref="TargetPlayers"/> is empty.</summary>
    public string? SelectedTargetUid { get; }
    public Action<string> OnTargetSelected { get; }
    /// <summary>The dialog-built staging slot widget, opaque here (the dialog owns the SlotController).</summary>
    public Widget StagingSlot { get; }
    /// <summary>The staged item's rows; empty when nothing is staged or it has no readable document.</summary>
    public IReadOnlyList<ScribeReadRowData> StagedRows { get; }
    public ISet<Guid> SelectedTaskIds { get; }
    public Action<Guid> OnToggleSelected { get; }
    /// <summary>UI-only session state owned by the dialog; resets to false on every tab (re)open.</summary>
    public bool DeleteFromSource { get; }
    public Action<bool> OnToggleDeleteFromSource { get; }
    /// <summary>Sends every selected staged row as its own assignment to <see cref="SelectedTargetUid"/>.</summary>
    public Action OnSendBatch { get; }
    /// <summary>True while the "Submitted to Player" stamp plays over the staging slot; disables Send so a
    /// second tap can't queue another send mid-flourish.</summary>
    public bool Sending { get; }
    /// <summary>Whether the Desk's own document has an eligible task to pull in (gates the empty-state button).</summary>
    public bool CanPullFromDesk { get; }
    /// <summary>Enters the Desk's local Editor through the dialog-owned lock-aware path.</summary>
    public Action OnCreateTasks { get; }
    /// <summary>Activates the Desk's own document as this tab's task source.</summary>
    public Action OnPullFromDesk { get; }
    public ScribeRowStyle Style { get; }
    public ScrollController ScrollController { get; }
    /// <summary>The server's `scribeDeliveryMode` world config; gates the Local Inboxes / Send a Notice toggle
    /// (<see cref="ScribeDeliveryPolicy.ShowsToggle"/>) and the notice slots (<see cref="ScribeDeliveryPolicy.RequiresNotice"/>).</summary>
    public ScribeDeliveryMode DeliveryMode { get; }
    /// <summary>The effective delivery choice, already resolved by the dialog; this widget never computes a default.</summary>
    public ScribeDeliveryChoice DeliveryChoice { get; }
    /// <summary>Tapping either half of the toggle; always freely overridable.</summary>
    public Action<ScribeDeliveryChoice> OnDeliveryChoiceChanged { get; }
    /// <summary>The blank-notice supply slot, built like <see cref="StagingSlot"/>; non-null only when
    /// <see cref="ScribeDeliveryPolicy.RequiresNotice"/> says it is needed.</summary>
    public Widget? NoticeSupplySlot { get; }
    /// <summary>The sealed-notice output slot; same visibility rule as <see cref="NoticeSupplySlot"/>.</summary>
    public Widget? NoticeOutputSlot { get; }
    /// <summary>Opens the delivery-mode Handbook page; a dialog callback since <c>ToggleHandbookPage</c> is
    /// <c>protected</c> on <see cref="ScribeDialogBase"/>.</summary>
    public Action OnOpenDeliveryInfo { get; }
    /// <summary>Whether <see cref="ScribeTabHeader.Build"/> renders its Row 2 subtitle line.</summary>
    public bool ShowSubtitleRow { get; }

    public override State CreateState() => new ScribeAssignmentFormContentState();
}

internal sealed class ScribeAssignmentFormContentState : State<ScribeAssignmentFormContent>
{
    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        var style = Widget.Style;
        var players = Widget.TargetPlayers;
        string? selectedTargetUid = Widget.SelectedTargetUid;

        // Row 3 (send-to controls) renders in Caudex like the title/subtitle rows above it. The picker's popup
        // renders in a global overlay outside the ambient DefaultTextStyle, so its font must be set explicitly.
        var targetDropdownStyle = Theme.Of(context).DropdownStyle;
        targetDropdownStyle = targetDropdownStyle with { TextStyle = targetDropdownStyle.TextStyle with { FontFamily = ScribeTaskFont.ButtonFamily } };

        Widget playerPicker = players.Count == 0
            ? new Text(Lang.Get("scribe:scribe-assignment-no-players"),
                new TextStyle { FontSize = style.FontSize, Color = colors.OnSurfaceVariant, FontFamily = ScribeTaskFont.ButtonFamily })
            : new Dropdown<string>(
                value: selectedTargetUid ?? players[0].Uid,
                items: players.Select(p => new DropdownItem<string> { Value = p.Uid, Label = p.Name }).ToList(),
                onChanged: v => Widget.OnTargetSelected(v),
                style: targetDropdownStyle);

        bool canSend = !Widget.Sending && selectedTargetUid is not null && Widget.SelectedTaskIds.Count > 0;

        Widget sendButton = new Button(
            child: new Text(Lang.Get("scribe:scribe-assignment-send"),
                new TextStyle { FontSize = 14, Color = colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: ButtonVariant.Primary,
            enabled: canSend,
            onTap: canSend ? _ => Widget.OnSendBatch() : null);

        // "Send to" row: fixed-width label, flexible picker, fixed-width Send button.
        Widget sendToRow = new Row(
            crossAxisAlignment: CrossAxisAlignment.Center,
            spacing: 8f,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-assignment-target-label"),
                    new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant, FontFamily = ScribeTaskFont.ButtonFamily }),
                new Expanded(flex: 1, child: playerPicker),
                sendButton,
            });

        Widget? deliveryRow = BuildDeliveryRow(colors, style);
        Widget? noticeSlotsRow = BuildNoticeSlotsRow(colors, style);

        Widget deleteFromSourceRow = new Row(
            spacing: 8f,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                new Checkbox(
                    value: Widget.DeleteFromSource,
                    onChanged: v => Widget.OnToggleDeleteFromSource(v),
                    size: style.CheckboxSize),
                new Text(Lang.Get("scribe:scribe-assignment-delete-from-source"),
                    new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant }),
            });

        Widget stagingArea = new Row(
            spacing: 12f,
            crossAxisAlignment: CrossAxisAlignment.Start,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                Widget.StagingSlot,
                new Text(Lang.Get("scribe:scribe-assignment-stage-hint"),
                    new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant, SoftWrap = true }),
            });

        // A rounded tray with an inset shadow (BoxShadow.Inset paints inside the box) so the staged rows read as recessed.
        Widget stageBox = new Container(
            style: new BoxStyle
            {
                Color = colors.SurfaceHigh,
                CornerRadius = new Vector4(8f),
                BorderThickness = 1f,
                BorderColor = colors.Border,
                Padding = EdgeInsets.All(6f),
                BoxShadows = new[]
                {
                    new BoxShadow(Color: colors.OnSurface with { W = 0.35f }, Offset: new Vector2(0f, 2f),
                        BlurRadius: 6f, Inset: true),
                },
            },
            child: new ScribeAssignmentStageContent(
                rows: Widget.StagedRows,
                selectedTaskIds: Widget.SelectedTaskIds,
                onToggleSelected: Widget.OnToggleSelected,
                canPullFromDesk: Widget.CanPullFromDesk,
                onCreateTasks: Widget.OnCreateTasks,
                onPullFromDesk: Widget.OnPullFromDesk,
                style: style,
                scrollController: Widget.ScrollController));

        // Row 3 holds only the send-to controls; everything else sits below the divider.
        Widget header = ScribeTabHeader.Build(colors, style,
            "scribe:scribe-tab-assignment", "scribe:scribe-tab-subtitle-assignment", Widget.ShowSubtitleRow, sendToRow);

        // Below the divider: delete-checkbox, staging hint, scrollable stage tray, then the notice slots (when
        // shown) above the delivery group. The 8-unit top padding keeps the divider flush against the content.
        var contentChildren = new List<Widget> { deleteFromSourceRow, stagingArea, new Expanded(child: stageBox) };
        if (noticeSlotsRow is not null) contentChildren.Add(noticeSlotsRow);
        if (deliveryRow is not null) contentChildren.Add(deliveryRow);

        Widget content = new Padding(EdgeInsets.Only(top: 8f), child: new Column(
            spacing: 8f,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: contentChildren));

        // Same Task Text Font + inset as every other tab so the Divider sits inset; top inset 4, others 10.
        return ScribeTextDefaults.Wrap(style.TaskFontFamily, style.FontSize, new Padding(EdgeInsets.Ltrb(10, 4, 10, 10), child: new Column(
            spacing: 0,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { header, new Expanded(child: content) })));
    }

    /// <summary>The "Local Inboxes" / "Send a Notice" toggle plus its info button. Hybrid-only
    /// (<see cref="ScribeDeliveryPolicy.ShowsToggle"/>): other modes have one legal path. Two plain segment
    /// buttons read as always-tappable.</summary>
    private Widget? BuildDeliveryRow(ColorScheme colors, ScribeRowStyle style)
    {
        if (!ScribeDeliveryPolicy.ShowsToggle(Widget.DeliveryMode)) return null;

        bool sendNotice = Widget.DeliveryChoice == ScribeDeliveryChoice.SendNotice;
        Widget localInboxesBtn = new Button(
            child: new Text(Lang.Get("scribe:scribe-delivery-local-inboxes"),
                new TextStyle { FontSize = 12.5f, Color = sendNotice ? colors.OnSurfaceVariant : colors.OnPrimary, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: sendNotice ? ButtonVariant.Secondary : ButtonVariant.Primary,
            onTap: _ => Widget.OnDeliveryChoiceChanged(ScribeDeliveryChoice.LocalInboxes));
        Widget sendNoticeBtn = new Button(
            child: new Text(Lang.Get("scribe:scribe-delivery-send-notice"),
                new TextStyle { FontSize = 12.5f, Color = sendNotice ? colors.OnPrimary : colors.OnSurfaceVariant, FontFamily = ScribeTaskFont.ButtonFamily }),
            variant: sendNotice ? ButtonVariant.Primary : ButtonVariant.Secondary,
            onTap: _ => Widget.OnDeliveryChoiceChanged(ScribeDeliveryChoice.SendNotice));

        // Hover hint plus click-through to the full Handbook page, mirroring GuiDialogScribeScriptorium's info button.
        //
        // Sized from the buttons' own font metrics + stock Button padding/border (6+6 vertical, ~1+1 border)
        // so it tracks font/theme changes. ScribeRowButton scales box and glyph together, so IconScale is
        // pulled down to keep the icon only slightly bigger than a style.ControlSize rendering.
        const float buttonFontSize = 12.5f;
        var buttonFontMetrics = TextLayoutHelper.GetFont(ScribeTaskFont.ButtonFamily, buttonFontSize, FontWeight.Normal).Metrics;
        float buttonLineH = buttonFontMetrics.Descent - buttonFontMetrics.Ascent + buttonFontMetrics.Leading;
        float buttonTotalH = buttonLineH + 12f /* ButtonStyle.Default vertical padding, 6+6 */ + 2f /* ~1px border each side */;
        // -2f: the box reads a touch smaller than the two Buttons; the icon target (oldGlyph * 1.15f) is independent
        // because infoIconScale is solved against infoSize.
        float infoSize = buttonTotalH + ScribeRowButton.BoxShrink - 2f; // so the drawn box == buttonTotalH - 2

        float oldPad = MathF.Max(3f, style.ControlSize * 0.18f);
        float oldGlyph = (style.ControlSize - oldPad * 2f) * 1f;
        float newPad = MathF.Max(3f, infoSize * 0.18f);
        float rawGlyphAtScale1 = infoSize - newPad * 2f;
        float infoIconScale = rawGlyphAtScale1 > 0f ? oldGlyph * 1.15f / rawGlyphAtScale1 : 1f;

        Widget infoButton = new Tooltip(
            child: new ScribeRowButton(iconName: "scribeinfo", iconColor: colors.OnSurfaceVariant, size: infoSize,
                iconScale: infoIconScale, onTap: () => Widget.OnOpenDeliveryInfo()),
            content: new Padding(EdgeInsets.All(6), child: new Text(
                Lang.Get("scribe:scribe-delivery-tooltip"),
                new TextStyle { FontSize = 12, Color = colors.OnSurface, SoftWrap = true })),
            useGlobalOverlay: true);

        return new Row(
            spacing: 8f,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[] { localInboxesBtn, sendNoticeBtn, infoButton });
    }

    /// <summary>The blank-notice supply + sealed-notice output slots, shown whenever
    /// <see cref="ScribeDeliveryPolicy.RequiresNotice"/> (the same check the server re-derives) says Send seals a notice.</summary>
    private Widget? BuildNoticeSlotsRow(ColorScheme colors, ScribeRowStyle style)
    {
        if (!ScribeDeliveryPolicy.RequiresNotice(Widget.DeliveryMode, Widget.DeliveryChoice)) return null;
        if (Widget.NoticeSupplySlot is null || Widget.NoticeOutputSlot is null) return null;

        return new Row(
            spacing: 12f,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                Widget.NoticeSupplySlot,
                new Text(Lang.Get("scribe:scribe-delivery-notice-supply-hint"),
                    new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant, SoftWrap = true }),
                new SizedBox(width: 8f),
                Widget.NoticeOutputSlot,
                new Text(Lang.Get("scribe:scribe-delivery-notice-output-hint"),
                    new TextStyle { FontSize = style.FontSize * 0.85f, Color = colors.OnSurfaceVariant, SoftWrap = true }),
            });
    }
}
