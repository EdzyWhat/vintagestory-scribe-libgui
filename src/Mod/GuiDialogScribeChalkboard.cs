using System.Collections.Generic;
using Gui.Widgets.Framework;     // Widget, ThemeData, ColorScheme
using Gui.Widgets.Layout;        // CrossAxisAlignment (nav-button placement seam)
using OpenTK.Mathematics;        // Vector4 (title-chrome glyph color)
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Chalkboard block's dialog: a thin subclass of <see cref="ScribeDialogBase"/> (same structure as
/// the Lectern's) adding only a dark-slate theme (<see cref="ResolveTheme"/>) and the Guestbook nav button.
/// </summary>
public sealed class GuiDialogScribeChalkboard : ScribeDialogBase
{
    public GuiDialogScribeChalkboard(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, host, capi)
    {
    }

    /// <summary>Editor access needs a server lock round-trip; the grant lands asynchronously (same as the Lectern).</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>Chalkboard theme, applied only when Pixel-Art Display is on; otherwise the global theme.</summary>
    protected override ThemeData ResolveTheme(bool pixelArt) =>
        pixelArt ? ScribeTheme.Chalkboard : base.ResolveTheme(pixelArt);

    /// <summary>Title-bar chrome glyphs (pencil, grip) in full chalk-white <c>OnSurface</c> so they read as chalk
    /// on the dark board. Base default when Pixel-Art Display is off.</summary>
    private protected override Vector4 TitleChromeGlyphColor(ColorScheme colors) =>
        modSystem.MySettings.PixelArtDisplay ? colors.OnSurface : base.TitleChromeGlyphColor(colors);

    /// <summary>Link/Tracker/Craft row accent uses a light chalk-green: the theme's dark green <c>Primary</c> works as a
    /// button fill but is illegible as small text on dark slate. Only when Pixel-Art Display is on.</summary>
    private protected override ScribeRowStyle DecorateRowStyle(ScribeRowStyle style) =>
        modSystem.MySettings.PixelArtDisplay
            ? style with { LinkColor = ScribeTheme.ChalkboardLinkText, QuestLinkColor = ScribeTheme.ChalkboardQuestLinkText }
            : base.DecorateRowStyle(style);

    /// <summary>Inactive nav glyphs in slate-brown; the theme's pale <c>OnSurfaceVariant</c> looks almost active on
    /// the dark board. Only when Pixel-Art Display is on.</summary>
    private protected override Vector4 NavIconColor(ColorScheme colors) =>
        modSystem.MySettings.PixelArtDisplay ? ScribeTheme.ChalkboardNavIcon : base.NavIconColor(colors);

    /// <summary>Focused inputs get a chalk-white outline instead of the dark-green <c>Primary</c>, which does not
    /// read as a border on slate. Covers task rows and the guestbook note field. Only when Pixel-Art Display is on.</summary>
    private protected override Vector4 InputFocusBorderColor(ColorScheme colors) =>
        modSystem.MySettings.PixelArtDisplay ? ScribeTheme.ChalkboardInputFocusBorder : base.InputFocusBorderColor(colors);

    /// <summary>Completed-task checkbox tick in chalk-white to match the row text; box background/border keep the
    /// theme. Only when Pixel-Art Display is on.</summary>
    private protected override Vector4? CheckTickColor(ColorScheme colors) =>
        modSystem.MySettings.PixelArtDisplay ? ScribeTheme.ChalkboardInputFocusBorder : base.CheckTickColor(colors);

    /// <summary>Centers the nav stack in its <c>SideColW</c> column, or aligns End when the column is narrower than a
    /// button so the overflow spills left (inward) instead of being clipped at the window's right edge at small
    /// <c>PixelArtSize</c>. Not gated on Pixel-Art Display: it follows the fixed frame art, not the palette.</summary>
    private protected override CrossAxisAlignment NavButtonAlignment(float sideColW, float navBoxW) =>
        navBoxW > sideColW ? CrossAxisAlignment.End : CrossAxisAlignment.Center;

    /// <summary>Uses a chalkboard-worded task-cap notice; the base default says "A tablet holds...".</summary>
    protected override string TaskCapReachedLangKey => "scribe:chalkboard-full";

    /// <summary>Selected row of the policy dropdown becomes a fully opaque <c>Primary</c> fill with <c>OnPrimary</c>
    /// label; the stock translucent green wash with green text is unreadable on dark slate. Only when Pixel-Art
    /// Display is on.</summary>
    private protected override DropdownStyle DecoratePolicyDropdownStyle(DropdownStyle style)
    {
        if (!modSystem.MySettings.PixelArtDisplay) return base.DecoratePolicyDropdownStyle(style);

        var colors = ResolveTheme(pixelArt: true).ColorScheme;
        return style with
        {
            SelectionColor = new Vector4(colors.Primary.X, colors.Primary.Y, colors.Primary.Z, 1.0f),
            SelectionAccentColor = colors.OnPrimary,
        };
    }

    protected override IEnumerable<Widget> GetExtraNavButtons()
    {
        // Same NavIconColor seam as the built-in nav buttons so the guestbook glyph matches their inactive tint.
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        yield return TitleButton(
            "scribeguest",
            "scribe-tab-guestbook",
            NavIconColor(colors),
            NavButtonSize,
            OnClickSwitchToVisitors,
            boxShadows: NavButtonShadow,
            activeColor: IsVisitorsView ? ScribeRowConstants.NavActiveGuestbook : null);
        // Shown only once the player has received an assignment; the Desk and standalone Inbox always show Inbox.
        if (modSystem.MyReceivedAssignments.Count > 0)
        {
            yield return TitleButton(
                "scribeinboxarrow",
                "scribe-tab-inbox",
                NavIconColor(colors),
                NavButtonSize,
                OnClickSwitchToInbox,
                boxShadows: NavButtonShadow,
                activeColor: IsInboxView ? ScribeRowConstants.NavActiveGuestbook : null,
                shimmer: ShowInboxShimmer);
        }
    }
}
