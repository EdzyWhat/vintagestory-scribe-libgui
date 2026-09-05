using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Rendering;              // EdgeInsets
using Gui.Rendering.Text;         // TextStyle
using Gui.Widgets.Framework;      // Widget, ButtonVariantStyle
using Gui.Widgets.Input;          // Dropdown, DropdownItem
using Gui.Widgets.Layout;         // Padding
using Gui.Widgets.Overlay;        // Tooltip
using OpenTK.Mathematics;         // Vector4
using Vintagestory.API.Config;    // Lang

namespace Scribe;

/// <summary>
/// Shared "Link / Dismiss / Settings" action-button rendering + the 0/1/2+ Accept-destination-picker
/// rule for a quest accept/completion prompt (rework-quest-accept-notification-styles) — used identically
/// by the HUD banner (<see cref="HudScribePins"/>) and the center-screen modal
/// (<see cref="GuiDialogScribeQuestPrompt"/>) so the two presentation styles' button colors and behavior
/// can never drift apart (design: "the same three colored/bordered buttons... as the polished HUD
/// banner"). Pure widget composition — no game-state reads beyond what's passed in.
/// </summary>
internal static class ScribeQuestPromptActions
{
    /// <summary>A single bordered, tinted-fill action button in one of the three named accent colors
    /// (<see cref="ScribeRowConstants.QuestPromptLinkColor"/>/<see cref="ScribeRowConstants.QuestPromptDismissColor"/>/
    /// <see cref="ScribeRowConstants.QuestPromptSettingsColor"/>). Built on the stock
    /// <see cref="Gui.Widgets.Basic.Button"/> — a purpose-built replacement (<c>ScribeAccentButton</c>) was
    /// tried to strip the stock widget's hard-coded hover box-shadow (see VSAPI-NOTES.md's LibGUI section),
    /// but wasn't worth maintaining a duplicate of library button logic for one button; reverted per direct
    /// user direction. The downward-hover-shadow look is an accepted stock-LibGUI quirk here.</summary>
    public static Widget AccentButton(
        string label, Vector4 accent, float fontSize, Action? onTap, bool enabled = true,
        Vector4? textColor = null, float? glowWidth = null, Vector4? glowColor = null)
    {
        var variantStyle = new ButtonVariantStyle
        {
            // Fill alpha per interaction state (post-ship retune via tools/quest-prompt-colors/index.html —
            // was 0.16f/0.28f/0.38f): the buttons now read as noticeably more solid/filled rather than a
            // faint tint, which is why the label text below needed a decoupled color option.
            BackgroundColor = accent with { W = enabled ? 0.29f : 0.05f },
            HoverBackgroundColor = accent with { W = enabled ? 0.48f : 0.05f },
            PressBackgroundColor = accent with { W = enabled ? 0.68f : 0.05f },
            BorderColor = accent with { W = enabled ? 0.9f : 0.3f },
            BorderThickness = 1.25f,
            CornerRadius = 4f,
        };
        Vector4 resolvedTextColor = textColor ?? accent;
        // NOTE (LibGUI 3.1.0 landmine — VSAPI-NOTES.md "DefaultTextStyle + TextStyle.Merge"): a
        // TextStyle.Color of exactly Vector4.One (pure opaque white) is Merge's "unset" sentinel and
        // silently inherits whatever ancestor DefaultTextStyle is in scope instead of applying — do NOT
        // pass pure white here. Callers that want "the standard HUD text color" should pass the HUD's
        // own near-white row-text constant (never exactly (1,1,1,1)) rather than white.
        return new Gui.Widgets.Basic.Button(
            child: new Gui.Widgets.Basic.Text(label, new TextStyle
            {
                FontSize = fontSize,
                Color = enabled ? resolvedTextColor : resolvedTextColor with { W = 0.5f },
                GlowWidth = glowWidth ?? 0f,
                GlowColor = glowColor ?? default,
            }),
            variant: ButtonVariant.Primary,
            style: new ButtonStyle { Primary = variantStyle, Padding = EdgeInsets.Symmetric(vertical: 4f, horizontal: 10f) },
            onTap: _ => onTap?.Invoke(),
            enabled: enabled);
    }

    /// <summary>The Link action for a prompt: a disabled button + tooltip (0 eligible carried Scribe
    /// documents — nothing to place onto), a direct Link button (a completion-prompt, which has no
    /// destination to choose, or exactly 1 candidate), or a two-step tap revealing a candidate picker
    /// dropdown above the button (2+ candidates) — mirrors the Inbox Accept control /
    /// <c>GuiDialogTaskNotice</c>'s own Accept shape (add-progression-framework-quest-support Decision 3).
    /// Returns the picker widget (or null when not shown) and the button separately so each host lays
    /// them out in its own structure.</summary>
    public static (Widget? Picker, Widget Button) BuildLinkControl(
        ScribeQuestPrompt prompt,
        IReadOnlyList<ScribeAcceptCandidate> candidates,
        bool pickerOpen,
        int selectedIndex,
        float fontSize,
        Vector4 linkAccent,
        Vector4 disabledTextColor,
        Vector4 tooltipTextColor,
        Action<ScribeQuestPrompt, ScribeAcceptCandidate?> onAccept,
        Action onOpenPicker,
        Action<int> onSelectCandidate,
        Vector4? textColor = null, float? glowWidth = null, Vector4? glowColor = null)
    {
        string label = Lang.Get("scribe:scribe-hud-questprompt-accept-button");

        if (prompt.IsCompletion)
            return (null, AccentButton(label, linkAccent, fontSize, () => onAccept(prompt, null),
                textColor: textColor, glowWidth: glowWidth, glowColor: glowColor));

        if (candidates.Count == 0)
        {
            // The disabled/no-candidates treatment uses disabledTextColor as its own accent (a muted,
            // neutral look distinct from the green accept accent) — it deliberately does NOT take the
            // decoupled textColor/glow above, which is tuned for the enabled green button's fill.
            Widget disabled = AccentButton(label, disabledTextColor, fontSize, null, enabled: false);
            Widget tipped = new Tooltip(
                child: disabled,
                content: new Padding(EdgeInsets.All(6), child: new Gui.Widgets.Basic.Text(
                    Lang.Get("scribe:scribe-assignment-no-eligible-target"),
                    new TextStyle { FontSize = 12, Color = tooltipTextColor, SoftWrap = true })),
                useGlobalOverlay: true);
            return (null, tipped);
        }

        if (candidates.Count == 1)
            return (null, AccentButton(label, linkAccent, fontSize, () => onAccept(prompt, candidates[0]),
                textColor: textColor, glowWidth: glowWidth, glowColor: glowColor));

        if (!pickerOpen)
            return (null, AccentButton(label, linkAccent, fontSize, onOpenPicker,
                textColor: textColor, glowWidth: glowWidth, glowColor: glowColor));

        int idx = Math.Clamp(selectedIndex, 0, candidates.Count - 1);
        Widget picker = new Dropdown<int>(
            value: idx,
            items: candidates.Select((c, i) => new DropdownItem<int> { Value = i, Label = c.Label }).ToList(),
            onChanged: v => onSelectCandidate(v));
        return (picker, AccentButton(label, linkAccent, fontSize, () => onAccept(prompt, candidates[idx]),
            textColor: textColor, glowWidth: glowWidth, glowColor: glowColor));
    }
}
