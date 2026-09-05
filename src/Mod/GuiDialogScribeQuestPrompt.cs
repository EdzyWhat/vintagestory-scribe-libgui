using System.Linq;
using Gui;                       // GuiBase, WindowConfig
using Gui.Rendering;              // EdgeInsets
using Gui.Rendering.Text;         // TextStyle
using Gui.Widgets.Basic;          // WindowFrame, Container, Text
using Gui.Widgets.Framework;      // Widget, ThemeData, ColorScheme
using Gui.Widgets.Layout;         // Column, Row, Padding, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Painting;       // BoxStyle
using Gui.Core.Layout;            // MainAxisSize
using OpenTK.Mathematics;         // Vector2
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;    // Lang

namespace Scribe;

/// <summary>
/// The center-screen "Add this Quest to Scribe?" modal — the <see cref="ScribeQuestAcceptPolicy.PromptPopup"/>
/// accept-prompt presentation style, an alternative to the HUD banner
/// (rework-quest-accept-notification-styles). Mirrors <see cref="ScribeSettingsDialog"/>'s WindowFrame-hosted
/// construction and <see cref="DrawOrder"/> band. Reuses <see cref="ScribeQuestPromptActions"/> for its
/// Link/Dismiss/Settings buttons so this presentation style can never drift from the polished HUD banner's
/// colors/behavior.
///
/// <para><b>Self-managing visibility</b> (mirrors <see cref="HudScribePins"/>'s own pattern): constructed
/// once in <see cref="ScribeModSystem.StartClientSide"/> and owns its own subscription + tick for its
/// whole lifetime, deciding on every trigger whether it should be open right now and for which prompt —
/// see <see cref="Refresh"/>. There is deliberately no "already shown, don't reopen" suppression: a
/// player-closed-without-resolving modal (e.g. via its own Settings button) simply reopens on the next
/// guard-check tick if it's still eligible, which the design's own Risks section discloses as the accepted
/// trade-off of choosing the more intrusive Popup style over the HUD banner.</para>
/// </summary>
public sealed class GuiDialogScribeQuestPrompt : GuiBase
{
    /// <summary>Guard/eligibility re-check cadence, matching <c>ScribeQuestWatcher</c>'s own detection
    /// tick — "check on the same tick cadence as detection" (tasks.md 3.4).</summary>
    private const int GuardCheckIntervalMs = 1000;

    private readonly ScribeModSystem modSystem;
    private long tickListenerId;

    /// <summary>The prompt this dialog is currently showing (or last showed) — set immediately before
    /// opening/rebuilding in <see cref="Refresh"/>, read by <see cref="Build"/>. Meaningless while closed.</summary>
    private ScribeQuestPrompt shownPrompt;

    private bool showAcceptPicker;
    private int selectedCandidateIndex;

    public GuiDialogScribeQuestPrompt(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;
        modSystem.QuestPromptsChanged += OnQuestPromptsChanged;
        tickListenerId = capi.Event.RegisterGameTickListener(OnTick, GuardCheckIntervalMs);
    }

    public override string DialogCode => "scribequestpromptmodal";

    /// <summary>Matches <see cref="ScribeSettingsDialog"/>'s band — this can open over a Lectern/Notebook/
    /// Tablet (whose own Settings gear sits there too).</summary>
    public override double DrawOrder => 0.2;

    protected override WindowConfig CreateWindowConfig() => new()
    {
        // Fixed size (no explicit Position — the base auto-centers a freshly-opened dialog with no
        // persisted position, which is exactly the "center-screen" placement this style needs).
        Size = new Vector2(380, 210),
        Draggable = true,
        Resizable = false,
    };

    private void OnQuestPromptsChanged() => Refresh();
    private void OnTick(float dt) => Refresh();

    /// <summary>Recompute whether this dialog should be open right now, and for which prompt, from the
    /// live pending set + the player's current Quest Accept Policy + the vanilla-dialog guard. Called on
    /// every prompt-list change AND on <see cref="GuardCheckIntervalMs"/> cadence, so a prompt deferred by
    /// an open vanilla dialog opens automatically once it closes — with no player action required
    /// (spec: "SHALL NEVER be silently dropped").</summary>
    private void Refresh()
    {
        var next = NextEligiblePrompt();
        if (next is null)
        {
            if (IsOpened()) TryClose();
            return;
        }

        if (!IsOpened())
        {
            // Only the OPEN transition needs the guard — an already-open modal isn't newly contesting a
            // click against a vanilla dialog that opens later (fix-libgui-click-draw-order-mismatch's
            // guard exists for the open race, not as an ongoing z-order arbiter).
            if (ScribeVanillaDialogGuard.IsAnyVanillaDialogOpen(capi)) return; // deferred; re-checked next trigger
            ShowPrompt(next.Value);
            TryOpen();
            return;
        }

        if (!PromptEquals(shownPrompt, next.Value))
        {
            ShowPrompt(next.Value);
            ForceRebuild();
        }
    }

    /// <summary>The oldest pending, non-completion prompt while the player's Quest Accept Policy is
    /// <see cref="ScribeQuestAcceptPolicy.PromptPopup"/> — this modal's ONLY concern; a completion-prompt
    /// or a PromptHud-policy accept-prompt always renders via the HUD banner instead (Non-Goal: no modal
    /// style for completions). Routing happens here at read-time off the live policy, matching
    /// <c>HudScribePins</c>' own routing (no style tag on the queued prompt).</summary>
    private ScribeQuestPrompt? NextEligiblePrompt()
    {
        if (modSystem.MySettings.QuestAcceptPolicy != ScribeQuestAcceptPolicy.PromptPopup) return null;
        foreach (var p in modSystem.PendingQuestPrompts)
            if (!p.IsCompletion) return p;
        return null;
    }

    private static bool PromptEquals(ScribeQuestPrompt a, ScribeQuestPrompt b)
        => a.Source == b.Source && a.QuestCode == b.QuestCode && a.IsCompletion == b.IsCompletion;

    /// <summary>Adopt a (possibly new) prompt to show, resetting the picker so a resolved prompt's
    /// candidate selection never leaks onto the next one queued behind it (mirrors
    /// <c>HudScribePins.OnQuestPromptsChanged</c>'s own picker reset).</summary>
    private void ShowPrompt(ScribeQuestPrompt prompt)
    {
        shownPrompt = prompt;
        showAcceptPicker = false;
        selectedCandidateIndex = 0;
    }

    protected override Widget Build()
    {
        var colors = ThemeData.Default.ColorScheme;
        var candidates = modSystem.ComputeQuestAcceptCandidates();

        // The accept/dismiss labels render in the HUD's own standard near-white row-text color, decoupled
        // from their own accent color — matches the HUD banner's treatment (rework-quest-accept-
        // notification-styles) so the two presentation styles never drift apart. No glow here (unlike the
        // banner): this modal has an opaque dialog backdrop, not an in-world overlay, so the glow's
        // legibility-over-the-world purpose doesn't apply.
        Vector4 promptActionTextColor = ScribeRowConstants.HudStandardTextColor;
        var (picker, linkButton) = ScribeQuestPromptActions.BuildLinkControl(
            shownPrompt, candidates, showAcceptPicker, selectedCandidateIndex,
            fontSize: 14f,
            linkAccent: ScribeRowConstants.QuestPromptLinkColor,
            disabledTextColor: colors.OnSurfaceVariant,
            tooltipTextColor: colors.OnSurface,
            onAccept: (p, c) =>
            {
                modSystem.AcceptQuestPrompt(p, c);
                TryClose();
            },
            onOpenPicker: () =>
            {
                showAcceptPicker = true;
                ForceRebuild();
            },
            onSelectCandidate: v =>
            {
                selectedCandidateIndex = v;
                ForceRebuild();
            },
            textColor: promptActionTextColor);

        Widget dismissButton = ScribeQuestPromptActions.AccentButton(
            Lang.Get("scribe:scribe-hud-questprompt-dismiss-button"), ScribeRowConstants.QuestPromptDismissColor,
            14f, () =>
            {
                modSystem.DismissQuestPrompt(shownPrompt);
                TryClose();
            }, textColor: promptActionTextColor);

        // Settings closes only THIS WINDOW, never the underlying prompt (tasks.md 3.2: "matching the
        // banner's existing action semantics exactly" — the banner's own Settings shortcut never dismisses
        // its prompt either). If the prompt is still pending and eligible, Refresh() reopens this on its
        // next trigger — see the class doc comment's disclosed trade-off.
        Widget settingsButton = ScribeQuestPromptActions.AccentButton(
            Lang.Get("scribe:scribe-hud-questprompt-settings-button"), ScribeRowConstants.QuestPromptSettingsColor,
            14f, () =>
            {
                modSystem.OpenSettings();
                TryClose();
            });

        var body = new System.Collections.Generic.List<Widget>
        {
            new Text(shownPrompt.Title, new TextStyle { FontSize = 15, Color = colors.Primary, SoftWrap = true }),
        };
        if (picker is not null) body.Add(picker);
        body.Add(new Row(
            spacing: 8f,
            mainAxisAlignment: MainAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { linkButton, dismissButton, settingsButton }));

        return new WindowFrame(
            title: Lang.Get("scribe:scribe-questprompt-modal-title"),
            onClose: () => TryClose(), // closes the window only — see settingsButton's remarks above
            child: new Container(
                style: new BoxStyle { Color = colors.Surface },
                child: new Padding(
                    EdgeInsets.All(14),
                    child: new Column(
                        crossAxisAlignment: CrossAxisAlignment.Center,
                        mainAxisSize: MainAxisSize.Min,
                        spacing: 10,
                        children: body))));
    }

    public override void Dispose()
    {
        modSystem.QuestPromptsChanged -= OnQuestPromptsChanged;
        if (tickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }
        base.Dispose();
    }
}
