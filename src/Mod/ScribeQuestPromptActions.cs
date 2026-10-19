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
/// Shared "Link / Dismiss / Settings" action buttons + the 0/1/2+ Accept-destination-picker rule for a quest
/// prompt, used by both the HUD banner (<see cref="HudScribePins"/>) and the modal
/// (<see cref="GuiDialogScribeQuestPrompt"/>) so their behavior can't drift apart. Pure widget composition.
/// </summary>
internal static class ScribeQuestPromptActions
{
    /// <summary>A bordered, tinted-fill action button in one of the prompt accent colors
    /// (<see cref="ScribeRowConstants.QuestPromptLinkColor"/>/<see cref="ScribeRowConstants.QuestPromptDismissColor"/>/
    /// <see cref="ScribeRowConstants.QuestPromptSettingsColor"/>). Built on the stock
    /// <see cref="Gui.Widgets.Basic.Button"/>, so its hard-coded downward hover box-shadow is accepted.</summary>
    public static Widget AccentButton(
        string label, Vector4 accent, float fontSize, Action? onTap, bool enabled = true,
        Vector4? textColor = null, float? glowWidth = null, Vector4? glowColor = null)
    {
        var variantStyle = new ButtonVariantStyle
        {
            BackgroundColor = accent with { W = enabled ? 0.29f : 0.05f },
            HoverBackgroundColor = accent with { W = enabled ? 0.48f : 0.05f },
            PressBackgroundColor = accent with { W = enabled ? 0.68f : 0.05f },
            BorderColor = accent with { W = enabled ? 0.9f : 0.3f },
            BorderThickness = 1.25f,
            CornerRadius = 4f,
        };
        Vector4 resolvedTextColor = textColor ?? accent;
        // LibGUI 3.1.0: a TextStyle.Color of exactly Vector4.One is Merge's "unset" sentinel and silently
        // inherits the ancestor DefaultTextStyle, so never pass pure white here (use a near-white constant).
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

    /// <summary>The Link action: disabled button + tooltip (0 candidates), a direct button (completion prompt
    /// or exactly 1 candidate), or a two-step tap revealing a picker dropdown (2+ candidates). Returns the
    /// picker (null when not shown) and button separately so each host lays them out itself.</summary>
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
            // Deliberately ignores textColor/glow, which are tuned for the enabled button's fill.
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
