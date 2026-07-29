using System;
using System.Collections.Generic;
using System.Diagnostics;        // Conditional (DEBUG-only scroll trace)
using System.Linq;
using Gui;                       // GuiDialogBlockEntityBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame, VsIcon, Container, Button
using Gui.Widgets.Events;        // PointerEvent
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, FocusNode, GestureDetector, MouseRegion, Dropdown, DropdownItem
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, Align, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable, Scrollbar
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

internal static class ScribeRowControlNudge
{
    /// <summary>The family Scribe's dialog TITLE text is drawn in: "Caudex", the mod's bundled humanist
    /// serif, registered with LibGUI's Skia font registry in
    /// <see cref="ScribeModSystem.RegisterCustomFonts"/> (prove-bundled-font-seam). Only the title uses it;
    /// task-row text stays on the default family (see <see cref="FontFamily"/>). If registration fails the
    /// family falls back to a system face via <c>TextLayoutHelper</c>, so the title still renders.</summary>
    internal const string TitleFontFamily = "Caudex";

    /// <summary>Font family used to measure the single-line input height. MUST match
    /// <c>ScribeMultilineField.FontFamily</c> (and the read <c>Text</c> default) so the measured height
    /// equals the field's actual single-line height. Row text is NOT in the title's Caudex face — the two
    /// are deliberately separate.</summary>
    private const string FontFamily = "sans-serif";

    /// <summary>Measured single-line input height at the style's current font size: the "Ag" line height
    /// (same family the field/read text use) plus the field's top+bottom internal padding — mirroring
    /// <c>ScribeMultilineFieldRender.PerformLayout</c>'s <c>lineCount * lineHeight + PadY*2</c> for one
    /// line.</summary>
    private static float SingleLineInputHeight(ScribeRowStyle style)
    {
        float lineHeight = TextLayoutHelper.MeasureText("Ag", FontFamily, style.FontSize, FontWeight.Normal).Y;
        if (lineHeight <= 0) lineHeight = style.FontSize * 1.2f; // same fallback as the field
        return lineHeight + style.FieldPadY * 2f;
    }

    /// <summary>Down-nudge for the drag grip and the task checkbox (both <see cref="ScribeRowStyle.CheckboxSize"/>
    /// tall) so they center on a one-line input. Computed, not constant, so it stays centered at any font
    /// scale.</summary>
    public static float CheckboxAndGripTop(ScribeRowStyle style)
        => MathF.Max(0f, (SingleLineInputHeight(style) - style.CheckboxSize) / 2f);

    /// <summary>The grip glyph's insets in a row: the vertical centering top-nudge (kept, same as the
    /// checkbox), plus a NEGATIVE right inset that cancels the Row's <see cref="ScribeRowStyle.CheckboxTextGap"/>
    /// which would otherwise sit as a trailing margin between the grip and the next control (§10.4). With
    /// the trailing gap zeroed the grip sits flush against the checkbox and the text column reclaims that
    /// width. Used identically for the editor/pin grips AND the read/frozen grip-column spacers so read and
    /// editor rows stay column-aligned across a view switch.</summary>
    public static EdgeInsets GripInsets(ScribeRowStyle style)
        => EdgeInsets.Only(top: CheckboxAndGripTop(style), right: -style.CheckboxTextGap);

    /// <summary>Absolute top offset (from the row's top edge) that centers a floating pin/delete button's
    /// DRAWN box on the one-line input. The button box is <see cref="ScribeRowButton.BoxShrink"/> px
    /// shorter than <see cref="ScribeRowStyle.ControlSize"/>; the input sits <c>RowVerticalPadding</c>
    /// below the row top (the row's own vertical padding), so the button's box centers on the input's
    /// vertical midpoint. Computed so it tracks the font scale.</summary>
    public static float FloatingButtonTop(ScribeRowStyle style)
    {
        float boxHeight = style.ControlSize - ScribeRowButton.BoxShrink;
        float inputCenter = style.RowVerticalPadding + SingleLineInputHeight(style) / 2f;
        return MathF.Max(0f, inputCenter - boxHeight / 2f);
    }
}

// ============================================================================
// Shared per-row control primitives (add-lectern-row-affordances-libgui)
// ============================================================================

/// <summary>A bare (non-interactive) VS icon glyph rendered by registered <c>CustomIcons</c> code via
/// <see cref="VsIcon"/>. Used for the grip, whose pointer handling lives in a wrapping
/// <see cref="GestureDetector"/> rather than the glyph itself.
///
/// <para>NOTE: this deliberately uses <see cref="VsIcon"/> (icon-by-code) rather than LibGUI's
/// <see cref="Icon"/>/<see cref="IconButton"/> (SVG-by-path). <see cref="Icon"/> loads its SVG through
/// <c>SkiaAssetLoader.LoadSvg</c>, which calls <c>Assets.TryGet</c> WITHOUT <c>loadAsset: true</c> — and
/// VS nulls out every non-patched asset's <c>Data</c> after startup, so that path would fail to draw
/// our icons. <see cref="VsIcon"/> routes through <c>IconUtil.DrawIconInt</c> → the mod's self-healing
/// <c>CustomIcons</c> delegate (which re-resolves the asset on demand), which is why the icons were
/// registered that way (see <c>ScribeModSystem.RegisterSvgIcon</c> / VSAPI-NOTES.md).</para></summary>
internal sealed class ScribeVsIconGlyph : StatelessWidget
{
    private readonly string iconName;
    private readonly float size;
    private readonly Vector4 color;

    public ScribeVsIconGlyph(string iconName, float size, Vector4 color)
    {
        this.iconName = iconName;
        this.size = size;
        this.color = color;
    }

    public override Widget Build(BuildContext context) => new VsIcon(iconName, size, color);
}

/// <summary>A floating per-row action button (delete / pin) with real button chrome: a bordered,
/// solid-background square with theme-derived resting/hover/press fills, wrapping a <see cref="VsIcon"/>
/// glyph. Unlike the earlier bare hover-glyph, this reads as a proper button (2026-07-24 feedback) so it
/// floats legibly ON TOP of the row's text via the row's Stack. Uses <see cref="VsIcon"/> (icon-by-code)
/// for the glyph — NOT LibGUI's <see cref="IconButton"/>/<see cref="Icon"/>, whose SVG-by-path
/// <c>LoadSvg</c> fails on our post-startup-unloaded assets (see <see cref="ScribeVsIconGlyph"/>).</summary>
internal sealed class ScribeRowButton : StatefulWidget
{
    public ScribeRowButton(string iconName, Vector4 iconColor, float size, Action onTap, float iconScale = 1f, BoxShadow[]? boxShadows = null, Vector4? activeColor = null, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        IconName = iconName;
        IconColor = iconColor;
        Size = size;
        OnTap = onTap;
        IconScale = iconScale;
        BoxShadows = boxShadows;
        ActiveColor = activeColor;
    }

    public string IconName { get; }
    public Vector4 IconColor { get; }
    /// <summary>Nominal control-column side length (matches the grip/checkbox column). The DRAWN box is
    /// <see cref="BoxShrink"/> px smaller in each dimension (see <see cref="BoxShrink"/>); the glyph is
    /// still sized from this nominal value so shrinking the box doesn't shrink the icon.</summary>
    public float Size { get; }
    public Action OnTap { get; }

    /// <summary>Multiplier applied to the GLYPH only (not the box), so an icon can read a touch larger while
    /// its button box stays the same size as its neighbors (§10.2, the pin +10%). The padding split absorbs
    /// the difference, keeping the glyph centered — mirroring how <see cref="Size"/> vs the drawn box keeps
    /// the icon fixed when the box shrinks. Default 1 = unchanged.</summary>
    public float IconScale { get; }

    /// <summary>Optional drop/inner shadow(s) painted with the button's box (forwarded to
    /// <c>BoxStyle.BoxShadows</c>). Null = no shadow (the per-row delete/pin buttons); the enlarged sidebar
    /// nav buttons pass one so they read as raised chrome over the notebook art (v1-playtest-fixes 5.6).</summary>
    public BoxShadow[]? BoxShadows { get; }

    /// <summary>Optional "active tab" fill color (add-active-tab-nav-colors). Null = the normal neutral
    /// <c>SurfaceHigh</c> resting/hover/press behavior with the passed <see cref="IconColor"/> glyph. When
    /// set, the button reads as the currently-selected tab: its box fills with this color, its glyph is
    /// forced to <see cref="ScribeRowConstants.NavActiveGlyph"/> (cream) for contrast, and hover brightens
    /// the fill by +10 HSV Brightness (via <see cref="ScribeRowConstants.ShiftBrightness"/>). Only the
    /// sidebar nav buttons pass this; every other caller leaves it null and is unchanged.</summary>
    public Vector4? ActiveColor { get; }

    /// <summary>How much smaller (px, each dimension) the button's drawn chrome is than its nominal
    /// <see cref="Size"/> (2026-07-24 feedback: "2px smaller in height, 2px smaller in width"). The icon
    /// glyph is computed from the full <see cref="Size"/> and kept fixed, so this only tightens the
    /// padding/box around it — it shrinks the SKIN, not the SVG. Exposed so the row that lays the buttons
    /// out can position the pin against the actual (shrunk) box width.</summary>
    public const float BoxShrink = 2f;

    public override State CreateState() => new ScribeRowButtonState();
}

internal sealed class ScribeRowButtonState : State<ScribeRowButton>
{
    private bool hovered;
    private bool pressed;

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        // Solid (opaque) background from the theme's raised-surface tone, brightening resting -> hover ->
        // press so the button reads as interactive. SurfaceHigh is the raised-element tone; nudge it up
        // for hover/press. Kept opaque (W=1) so it fully covers the row text it floats over.
        //
        // ACTIVE TAB (add-active-tab-nav-colors): when ActiveColor is set this button is the current tab,
        // so it fills with the thematic color instead of SurfaceHigh, and its glyph is forced to cream for
        // contrast. Hover brightens the fill by +10 HSV Brightness (reusing ShiftBrightness); press darkens
        // it slightly (-6 V) for the same tactile feedback the neutral path gives via the RGB lift.
        Vector4 bg;
        Vector4 glyphColor = Widget.IconColor;
        if (Widget.ActiveColor is Vector4 active)
        {
            float vShift = pressed ? -6f : hovered ? 10f : 0f;
            bg = (vShift == 0f ? active : ScribeRowConstants.ShiftBrightness(active, vShift)) with { W = 1f };
            glyphColor = ScribeRowConstants.NavActiveGlyph;
        }
        else
        {
            Vector4 baseBg = colors.SurfaceHigh with { W = 1f };
            float lift = pressed ? -0.06f : hovered ? 0.10f : 0f;
            bg = new(
                Math.Clamp(baseBg.X + lift, 0f, 1f),
                Math.Clamp(baseBg.Y + lift, 0f, 1f),
                Math.Clamp(baseBg.Z + lift, 0f, 1f),
                1f);
        }

        // The glyph is sized from the FULL nominal Size so shrinking the box below leaves the icon
        // untouched (2026-07-24 feedback). Shrinking the drawn box by BoxShrink then tightens the padding
        // that surrounds the glyph — the "skin", not the SVG. IconScale (§10.2) then grows just the glyph
        // (the box is unchanged), with the padding split below re-centering it.
        float pad = MathF.Max(3f, Widget.Size * 0.18f); // small padding; glyph fills the rest
        float glyph = (Widget.Size - pad * 2f) * Widget.IconScale;

        // Drawn box is BoxShrink px smaller in each dimension; the padding absorbs the difference so the
        // glyph stays centered at its nominal size. Half the shrink comes off each side of the padding.
        float box = Widget.Size - ScribeRowButton.BoxShrink;
        float drawnPad = MathF.Max(0f, (box - glyph) / 2f);

        return new GestureDetector(
            onTap: _ => Widget.OnTap(),
            onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
            onExit: _ => { if (hovered || pressed) SetState(() => { hovered = false; pressed = false; }); },
            onPress: _ => { if (!pressed) SetState(() => pressed = true); },
            onRelease: _ => { if (pressed) SetState(() => pressed = false); },
            child: new Container(
                style: new BoxStyle
                {
                    Color = bg,
                    Width = box,
                    Height = box,
                    CornerRadius = new Vector4(3f),
                    BorderThickness = 1f,
                    BorderColor = colors.Border,
                    Padding = EdgeInsets.All(drawnPad),
                    BoxShadows = Widget.BoxShadows,
                },
                child: new VsIcon(Widget.IconName, glyph, glyphColor)));
    }
}

/// <summary>A text-glyph twin of <see cref="ScribeRowButton"/>: the same bordered, theme-filled square with
/// hover/press states, but drawing a short text label (e.g. "R") instead of a VS icon. Used for the Read-view
/// nav button as a placeholder until the checkbox check SVG replaces it (scribe-notebook-frame D3).</summary>
internal sealed class ScribeRowButtonText : StatefulWidget
{
    public ScribeRowButtonText(string label, Vector4 color, float size, Action onTap, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Label = label;
        Color = color;
        Size = size;
        OnTap = onTap;
    }

    public string Label { get; }
    public Vector4 Color { get; }
    public float Size { get; }
    public Action OnTap { get; }

    public override State CreateState() => new ScribeRowButtonTextState();
}

internal sealed class ScribeRowButtonTextState : State<ScribeRowButtonText>
{
    private bool hovered;
    private bool pressed;

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;

        // Same solid raised-surface fill + hover/press lift as ScribeRowButton (kept opaque so it reads as a
        // real button over the art).
        Vector4 baseBg = colors.SurfaceHigh with { W = 1f };
        float lift = pressed ? -0.06f : hovered ? 0.10f : 0f;
        Vector4 bg = new(
            Math.Clamp(baseBg.X + lift, 0f, 1f),
            Math.Clamp(baseBg.Y + lift, 0f, 1f),
            Math.Clamp(baseBg.Z + lift, 0f, 1f),
            1f);

        float box = Widget.Size - ScribeRowButton.BoxShrink;
        float fontSize = Widget.Size * 0.62f;

        return new GestureDetector(
            onTap: _ => Widget.OnTap(),
            onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
            onExit: _ => { if (hovered || pressed) SetState(() => { hovered = false; pressed = false; }); },
            onPress: _ => { if (!pressed) SetState(() => pressed = true); },
            onRelease: _ => { if (pressed) SetState(() => pressed = false); },
            child: new Container(
                style: new BoxStyle
                {
                    Color = bg,
                    Width = box,
                    Height = box,
                    CornerRadius = new Vector4(3f),
                    BorderThickness = 1f,
                    BorderColor = colors.Border,
                },
                child: new Center(
                    child: new Text(Widget.Label,
                        new TextStyle { FontSize = fontSize, Weight = FontWeight.Bold, Color = Widget.Color }))));
    }
}

