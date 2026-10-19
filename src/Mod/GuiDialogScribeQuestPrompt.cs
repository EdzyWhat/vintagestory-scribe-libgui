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
/// alternative to the HUD banner. Reuses <see cref="ScribeQuestPromptActions"/> for its buttons so it
/// matches the banner's colors/behavior.
///
/// <para>Self-managing: constructed once in <see cref="ScribeModSystem.StartClientSide"/>, owns its own
/// subscription + tick, and decides in <see cref="Refresh"/> whether it should be open. There is no
/// "already shown" suppression: a modal closed without resolving (e.g. via Settings) reopens on the
/// next tick while its prompt is still eligible.</para>
/// </summary>
public sealed class GuiDialogScribeQuestPrompt : GuiBase
{
    /// <summary>Guard/eligibility re-check cadence, same as <c>ScribeQuestWatcher</c>'s detection tick.</summary>
    private const int GuardCheckIntervalMs = 1000;

    private readonly ScribeModSystem modSystem;
    private long tickListenerId;

    /// <summary>The prompt being shown (or last shown); set in <see cref="Refresh"/>, read by <see cref="Build"/>.</summary>
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

    /// <summary>Same band as <see cref="ScribeSettingsDialog"/>; this can open over a Lectern/Notebook/Tablet.</summary>
    public override double DrawOrder => 0.2;

    protected override WindowConfig CreateWindowConfig() => new()
    {
        // No explicit Position: the base auto-centers a dialog with no persisted position.
        Size = new Vector2(380, 210),
        Draggable = true,
        Resizable = false,
    };

    private void OnQuestPromptsChanged() => Refresh();
    private void OnTick(float dt) => Refresh();

    /// <summary>Recomputes whether to be open, and for which prompt, from the pending set, the Quest Accept
    /// Policy, and the vanilla-dialog guard. Runs on every prompt-list change and on the
    /// <see cref="GuardCheckIntervalMs"/> tick, so a prompt deferred by a vanilla dialog opens once it closes.</summary>
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
            // Only the open transition needs the guard (it prevents an open race, not ongoing z-order).
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

    /// <summary>The oldest pending non-completion prompt while the policy is
    /// <see cref="ScribeQuestAcceptPolicy.PromptPopup"/>; completions and PromptHud prompts always use
    /// the HUD banner. Routing is read from the live policy, not tagged on the queued prompt.</summary>
    private ScribeQuestPrompt? NextEligiblePrompt()
    {
        if (modSystem.MySettings.QuestAcceptPolicy != ScribeQuestAcceptPolicy.PromptPopup) return null;
        foreach (var p in modSystem.PendingQuestPrompts)
            if (!p.IsCompletion) return p;
        return null;
    }

    private static bool PromptEquals(ScribeQuestPrompt a, ScribeQuestPrompt b)
        => a.Source == b.Source && a.QuestCode == b.QuestCode && a.IsCompletion == b.IsCompletion;

    /// <summary>Adopts a prompt and resets the picker so a resolved prompt's selection doesn't leak to the next.</summary>
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

        // Labels use the HUD's near-white row-text color, as the banner does. No glow (unlike the banner):
        // the opaque backdrop doesn't need legibility-over-the-world help.
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

        // Settings closes only this window, never the prompt (as with the banner); Refresh() may reopen it.
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

        return ScribeTextDefaults.WrapChrome(new WindowFrame(
            title: Lang.Get("scribe:scribe-questprompt-modal-title"),
            onClose: () => TryClose(), // closes the window only, not the prompt
            child: new Container(
                style: new BoxStyle { Color = colors.Surface },
                child: new Padding(
                    EdgeInsets.All(14),
                    child: new Column(
                        crossAxisAlignment: CrossAxisAlignment.Center,
                        mainAxisSize: MainAxisSize.Min,
                        spacing: 10,
                        children: body)))));
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
