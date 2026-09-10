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
/// The Assignment Desk's Create Assignments tab (assignment-multi-item-creation): a staging slot for an
/// existing Scribe item, that item's rows rendered Read-view-style with independent Selected checkboxes
/// (<see cref="ScribeAssignmentStageContent"/>), a "Delete from source on send" toggle, and the
/// target-player picker + batch-Send button. Staging-and-select ONLY (refine-assignment-desk-inbox-ux
/// 12.1) — the Sent history this class used to render below a divider moved to its own Sent Assignment
/// History tab (<see cref="ScribeDialogBase.BuildSentAssignmentHistoryContent"/>, 12.2/12.3), so this tab
/// reads as one thing (create) instead of create-plus-a-read-only-history-view bolted underneath it.
///
/// <para>Replaces the freeform task-text field + single-item Send button this class held before
/// (refine-assignment-desk-inbox-ux tasks.md group 7/9.5) — creation now happens by delegating existing,
/// already-authored rows rather than typing a bare checkbox task.</para>
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

    /// <summary>Every other online player, as (uid, display name) — the target-player picker's options.
    /// An offline player can't be targeted (client has no UID→name directory for them); documented MVP
    /// scope, not a hidden limitation.</summary>
    public IReadOnlyList<(string Uid, string Name)> TargetPlayers { get; }
    /// <summary>Dialog-owned target selection (add-assignment-physical-delivery-mode) — lifted out of this
    /// widget's own State so the dialog can react to a target CHANGE (firing the Hybrid range check and
    /// resetting any manual delivery-toggle override), mirroring <see cref="ScribeInboxContent.ActiveFilterGroup"/>'s
    /// same lift-for-a-different-reason precedent. Null only when <see cref="TargetPlayers"/> is empty.</summary>
    public string? SelectedTargetUid { get; }
    public Action<string> OnTargetSelected { get; }
    /// <summary>The dialog-built staging slot widget (owns the SlotController; kept opaque here so this
    /// class stays free of LibGUI Inventory-widget/SlotController concerns).</summary>
    public Widget StagingSlot { get; }
    /// <summary>The staged item's rows, empty when nothing is staged or the staged item has no readable
    /// document.</summary>
    public IReadOnlyList<ScribeReadRowData> StagedRows { get; }
    public ISet<Guid> SelectedTaskIds { get; }
    public Action<Guid> OnToggleSelected { get; }
    /// <summary>UI-only session state (design.md D13) — the dialog owns it, resetting to false on every
    /// tab (re)open; never a saved preference.</summary>
    public bool DeleteFromSource { get; }
    public Action<bool> OnToggleDeleteFromSource { get; }
    /// <summary>Send every selected staged row as its own independent assignment to
    /// <see cref="SelectedTargetUid"/>.</summary>
    public Action OnSendBatch { get; }
    /// <summary>True while the "Submitted to Player" stamp is playing over the staging slot — disables the
    /// Send button for the animation's duration (refine-assignment-desk-inbox-ux 10.4) so a second tap
    /// can't queue another send before the first one's flourish has finished.</summary>
    public bool Sending { get; }
    /// <summary>Whether the Desk's own document has an eligible task to pull in (add-assignment-desk-own-
    /// tasks design.md D3) — gates the empty-state's "pull from Desk" button.</summary>
    public bool CanPullFromDesk { get; }
    /// <summary>Enters the Desk's local Editor through the dialog-owned lock-aware path.</summary>
    public Action OnCreateTasks { get; }
    /// <summary>Activates the Desk's own document as this tab's task source (design.md D3).</summary>
    public Action OnPullFromDesk { get; }
    public ScribeRowStyle Style { get; }
    public ScrollController ScrollController { get; }
    /// <summary>The server's current `scribeDeliveryMode` world config (add-assignment-physical-delivery-mode) —
    /// gates whether the Local Inboxes / Send a Notice toggle shows at all (<see cref="ScribeDeliveryPolicy.ShowsToggle"/>
    /// — Hybrid only) and whether the notice slots show (<see cref="ScribeDeliveryPolicy.RequiresNotice"/>).</summary>
    public ScribeDeliveryMode DeliveryMode { get; }
    /// <summary>The EFFECTIVE delivery choice for this send — already resolved by the dialog (manual
    /// override if the player tapped the toggle, otherwise the Hybrid range-check default, or the fixed
    /// value implied by a non-Hybrid mode). This widget never computes a default itself.</summary>
    public ScribeDeliveryChoice DeliveryChoice { get; }
    /// <summary>Tapping either half of the Local Inboxes / Send a Notice toggle (task 4.2: "remaining
    /// freely overridable with no blocked/grayed state").</summary>
    public Action<ScribeDeliveryChoice> OnDeliveryChoiceChanged { get; }
    /// <summary>The blank-notice stacking supply slot widget, built by the dialog exactly like
    /// <see cref="StagingSlot"/> — non-null only when <see cref="ScribeDeliveryPolicy.RequiresNotice"/>
    /// says the current mode/choice needs it (task 4.4/4.6).</summary>
    public Widget? NoticeSupplySlot { get; }
    /// <summary>The sealed-notice non-stacking output slot widget — same visibility rule as
    /// <see cref="NoticeSupplySlot"/>.</summary>
    public Widget? NoticeOutputSlot { get; }
    /// <summary>Opens the delivery-mode explanation Handbook page (task 4.3's "longer-form explanation") —
    /// a dialog-owned callback since <c>ToggleHandbookPage</c> is <c>protected</c> on
    /// <see cref="ScribeDialogBase"/> and unreachable from this plain widget.</summary>
    public Action OnOpenDeliveryInfo { get; }
    /// <summary>Whether <see cref="ScribeTabHeader.Build"/> renders its Row 2 subtitle line, read from
    /// <c>modSystem.VisualTuning.ShowSubtitleRow</c> (add-subtitle-row-configkit-toggle).</summary>
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

        // Row 3 (the send-to controls) renders entirely in Caudex, matching the title/subtitle rows above
        // it — unlike the tab's general content below the divider, which follows the player's Task Text
        // Font. The picker's popup menu renders in a global overlay outside the tab subtree the ambient
        // DefaultTextStyle ancestor covers anyway, so its font must be set explicitly regardless (same
        // reasoning as ScribePinnedContent's policy-picker dropdown).
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

        // "Send to" row as a LibGUI flex Row (refine-assignment-desk-inbox-ux 7.1 / vslibgui wiki's
        // Layout pattern): a fixed-width label, the player picker taking the flexible remaining space,
        // and the fixed-width Send button.
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

        // Inscribed in a rounded box with a slight inner glow emulating a shadow (refine-assignment-desk-
        // inbox-ux 12.4) — reads as a recessed "tray" the staged rows sit inside, distinguishing this list
        // from the plain-background rows/controls around it. Corner radius/border echo the staging slot's
        // own rounding; BoxShadow.Inset paints INSIDE the box (see BoxShadow's remarks), which is what
        // reads as a shadow cast INTO the box rather than one the box casts onto the page behind it.
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

        // Row 3 = ONLY the send-to controls (2026-09-09 playtest feedback: the rest of the drafting form
        // read as too much header chrome above the divider). Everything else — the staging-slot hint,
        // delete-checkbox, delivery toggle, and notice slots — moves back down into the scrollable/general
        // content below the divider, where a form-shaped tab's non-header controls belong.
        Widget header = ScribeTabHeader.Build(colors, style,
            "scribe:scribe-tab-assignment", "scribe:scribe-tab-subtitle-assignment", Widget.ShowSubtitleRow, sendToRow);

        // Content below the divider: the delete-checkbox gets its own line at the very TOP of the content
        // section (2026-09-10 2nd playtest feedback: reverses the earlier merge into the delivery-toggle
        // row — it read as too easy to miss sharing a line with the toggle buttons). Then the staging hint,
        // the scrollable stage tray, and — when shown — the notice slots sitting directly above the Local
        // Inboxes/Send a Notice group (2026-09-10 playtest feedback). The 8-unit top padding
        // (unify-tab-header-layout §7, doubled from 4 per 2026-09-09 playtest feedback) sits on this
        // content block as a whole, so the durable divider above stays flush against it.
        var contentChildren = new List<Widget> { deleteFromSourceRow, stagingArea, new Expanded(child: stageBox) };
        if (noticeSlotsRow is not null) contentChildren.Add(noticeSlotsRow);
        if (deliveryRow is not null) contentChildren.Add(deliveryRow);

        Widget content = new Padding(EdgeInsets.Only(top: 8f), child: new Column(
            spacing: 8f,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: contentChildren));

        // Rooted in the same Task Text Font + inset every other tab uses (ScribeReadContent/
        // ScribePinnedContent/the Guestbook/the Timer tab) — this tab used to return its Column bare, so its
        // Divider spanned edge-to-edge instead of sitting inset like theirs (refine-assignment-desk-inbox-ux
        // 11.1). Top inset reduced from 10 to 4 (2026-09-08 playtest feedback: 6px less gap between the
        // title bar and the Row 2 subtitle); left/right/bottom stay 10.
        return ScribeTextDefaults.Wrap(style.TaskFontFamily, style.FontSize, new Padding(EdgeInsets.Ltrb(10, 4, 10, 10), child: new Column(
            spacing: 0,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { header, new Expanded(child: content) })));
    }

    /// <summary>The "Local Inboxes" / "Send a Notice" toggle + its info button (`assignment-delivery-mode`
    /// capability, tasks.md 4.1-4.3). Hybrid-only (<see cref="ScribeDeliveryPolicy.ShowsToggle"/>) — a
    /// non-Hybrid mode has only one legal delivery path, so there is nothing to choose between and no
    /// toggle renders at all (task 4.6). Two plain segment buttons rather than a custom pill (task 4.2:
    /// "no blocked/grayed state" — a plain enabled Button pair reads unambiguously as always-tappable).</summary>
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

        // A short hover hint plus a click-through to the full Handbook explanation (task 4.3), mirroring
        // GuiDialogScribeScriptorium's info-button precedent (a Tooltip wrapping a Button that opens a
        // "craftinginfo-scribe-X" page) rather than cramming the long-form text into the tooltip itself.
        //
        // Sized to match the two Buttons beside it (2026-09-10 playtest feedback: it read visibly smaller)
        // — computed from the buttons' own font metrics + Gui's stock Button padding/border (6+6 vertical,
        // ~1+1 border; ButtonStyle.Default) rather than hardcoded, so it tracks any future font/theme
        // change. ScribeRowButton's box AND glyph both scale with Size by the same proportion, so growing
        // Size alone would grow the icon just as much as the padding around it — IconScale is pulled down
        // to counter that, landing the icon only slightly bigger than its old style.ControlSize rendering
        // while the surrounding padding (box minus glyph) does most of the growth, per the user's ask.
        const float buttonFontSize = 12.5f;
        var buttonFontMetrics = TextLayoutHelper.GetFont(ScribeTaskFont.ButtonFamily, buttonFontSize, FontWeight.Normal).Metrics;
        float buttonLineH = buttonFontMetrics.Descent - buttonFontMetrics.Ascent + buttonFontMetrics.Leading;
        float buttonTotalH = buttonLineH + 12f /* ButtonStyle.Default vertical padding, 6+6 */ + 2f /* ~1px border each side */;
        // -2f (2026-09-10 2nd playtest feedback: 1px less internal padding on every side, to read a touch
        // smaller than the two Buttons beside it rather than dead-on matching their height). Below, the
        // icon's target size (oldGlyph * 1.15f) stays untouched — infoIconScale is re-solved against this
        // smaller infoSize, so the 2px comes entirely out of ScribeRowButton's own padding, not the glyph.
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

    /// <summary>The blank-notice supply + sealed-notice output slots (task 4.4/4.6) — shown whenever the
    /// current mode/choice actually requires sealing a notice on Send, per the same
    /// <see cref="ScribeDeliveryPolicy.RequiresNotice"/> check the server re-derives independently. A
    /// single source of truth for "do the notice slots show" means the Hybrid toggle and the fixed
    /// AlwaysPhysical/AlwaysInstant modes all fall out of one rule rather than three separate ones.</summary>
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
