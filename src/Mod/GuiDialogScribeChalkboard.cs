using System.Collections.Generic;
using Gui.Widgets.Framework;     // Widget, ThemeData, ColorScheme
using Gui.Widgets.Layout;        // CrossAxisAlignment (nav-button placement seam)
using OpenTK.Mathematics;        // Vector4 (title-chrome glyph color)
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Chalkboard block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>, structurally
/// identical to the Lectern's. All view state, build methods, lock orchestration, autosave, title editing,
/// scroll management, and nav-button layout live in the base class. The Chalkboard adds only its own
/// cosmetic delta: a dark-slate LibGUI theme (via <see cref="ResolveTheme"/>) and the Guestbook nav button.
/// </summary>
public sealed class GuiDialogScribeChalkboard : ScribeDialogBase
{
    public GuiDialogScribeChalkboard(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, host, capi)
    {
    }

    /// <summary>The Chalkboard is a shared placed block: editor access requires a server lock round-trip,
    /// so <see cref="ScribeDialogBase.RequestEditorAccess"/> sends a request and the grant lands
    /// asynchronously in <see cref="ScribeDialogBase.EnterEditorMode"/> (same as the Lectern).</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>The chalkboard's brown-slate / chalk-light theme (add-chalkboard-block D3). Applied only when
    /// Pixel-Art Display is on — the same rule the base and tablet use — so with it off the dialog follows
    /// the player's global theme.</summary>
    protected override ThemeData ResolveTheme(bool pixelArt) =>
        pixelArt ? ScribeTheme.Chalkboard : base.ResolveTheme(pixelArt);

    /// <summary>Draw the title-bar chrome glyphs (edit pencil + drag grip) in the full chalk-white
    /// <c>OnSurface</c> rather than the muted <c>OnSurfaceVariant</c> the base uses, so they read as chalk on
    /// the dark board (the tablet uses this same seam for its engraved look). Falls back to the base default
    /// when Pixel-Art Display is off (global theme).</summary>
    private protected override Vector4 TitleChromeGlyphColor(ColorScheme colors) =>
        modSystem.MySettings.PixelArtDisplay ? colors.OnSurface : base.TitleChromeGlyphColor(colors);

    /// <summary>Decouple the Link/Tracker/Craft row accent from the theme's <c>Primary</c> on the chalkboard:
    /// <c>Primary</c> is a dark forest green that reads as a button FILL (white text on it) but disappears as
    /// small TEXT against the dark slate, so link names / book glyphs / tracker counts were illegible. Route
    /// them to a light chalk-green (<see cref="ScribeTheme.ChalkboardLinkText"/>) via
    /// <see cref="ScribeRowStyle.LinkColor"/> — same seam the tablet uses for its grip ink. Only when Pixel-Art
    /// Display is on (the chalkboard theme is active); otherwise leave the global-theme link color untouched.</summary>
    private protected override ScribeRowStyle DecorateRowStyle(ScribeRowStyle style) =>
        modSystem.MySettings.PixelArtDisplay
            ? style with { LinkColor = ScribeTheme.ChalkboardLinkText }
            : base.DecorateRowStyle(style);

    /// <summary>Darken the inactive nav glyphs to a slate-brown (<see cref="ScribeTheme.ChalkboardNavIcon"/>):
    /// the theme's pale-chalk <c>OnSurfaceVariant</c> read as almost-active on the dark board. Only when
    /// Pixel-Art Display is on (chalkboard theme active); otherwise the global-theme default.</summary>
    private protected override Vector4 NavIconColor(ColorScheme colors) =>
        modSystem.MySettings.PixelArtDisplay ? ScribeTheme.ChalkboardNavIcon : base.NavIconColor(colors);

    /// <summary>Outline a focused input in chalk-white (<see cref="ScribeTheme.ChalkboardInputFocusBorder"/>)
    /// instead of the theme's forest-green <c>Primary</c>: on the chalkboard <c>Primary</c> is the schoolroom
    /// green that reads well as a button FILL but which the author disliked as an input border. Feeds both the
    /// task rows (via <c>RowStyle</c> → <see cref="ScribeRowStyle.InputFocusBorderColor"/>) and the guestbook
    /// note field through the base seam. Only when Pixel-Art Display is on (chalkboard theme active); otherwise
    /// the global-theme default.</summary>
    private protected override Vector4 InputFocusBorderColor(ColorScheme colors) =>
        modSystem.MySettings.PixelArtDisplay ? ScribeTheme.ChalkboardInputFocusBorder : base.InputFocusBorderColor(colors);

    /// <summary>Render the completed-task checkbox TICK in the same chalk-white as the row text
    /// (<see cref="ScribeTheme.ChalkboardInputFocusBorder"/>) instead of the theme's forest-green
    /// <c>Primary</c>: the playtest verdict on §3/§6.4 was that a brighter-green tick still read as a
    /// mismatched accent on the slate, so the tick should match the chalk text (refine-chalkboard §11). Only
    /// the tick color changes — the box background/border stay the resolved chalkboard theme. Feeds the read,
    /// editor, frozen, and pinned rows via <c>RowStyle</c> → <see cref="ScribeRowStyle.CheckTickColor"/>. Only
    /// when Pixel-Art Display is on (chalkboard theme active); otherwise the global-theme tick is unchanged.</summary>
    private protected override Vector4? CheckTickColor(ColorScheme colors) =>
        modSystem.MySettings.PixelArtDisplay ? ScribeTheme.ChalkboardInputFocusBorder : base.CheckTickColor(colors);

    /// <summary>Place the right-column nav buttons the Hard Border-group way (refine-nav-button-placement):
    /// CENTER the stack within its <c>SideColW</c> column when the column is at least as wide as a button, and
    /// align to the END when the column is narrower than a button. RenderFlex offsets an overflowing cross
    /// child by (columnW − childW): End → negative, so the button's right edge pins to the column's outer edge
    /// and the overflow spills LEFT (inward over the tasks margin) rather than off the window's right edge where
    /// it was being clipped at small <c>PixelArtSize</c> (e.g. SideColW ≈ 0.073·400 ≈ 29px &lt; the ≈35px box).
    /// The Pages surfaces keep the base's <c>Start</c> default; the chalkboard's hard-bordered slate art wants
    /// the buttons centered and never clipped at the frame edge. Unlike the theme/border seams this is NOT gated
    /// on Pixel-Art Display — the placement is a property of the chalkboard's fixed frame art, not its palette.</summary>
    private protected override CrossAxisAlignment NavButtonAlignment(float sideColW, float navBoxW) =>
        navBoxW > sideColW ? CrossAxisAlignment.End : CrossAxisAlignment.Center;

    /// <summary>Word the task-cap-reached notice for a chalkboard rather than a tablet (refine-chalkboard):
    /// the chalkboard caps tasks at 10 (<see cref="BlockEntityScribeChalkboard.HostPolicy"/>), and the base's
    /// default notice says "A tablet holds…", which is wrong on a board. Route it to
    /// <c>scribe:chalkboard-full</c>. Not gated on Pixel-Art Display — the cap and its notice apply regardless
    /// of theme.</summary>
    protected override string TaskCapReachedLangKey => "scribe:chalkboard-full";

    /// <summary>Make the completion-policy picker's OPEN menu legible on the dark board (refine-chalkboard).
    /// The stock <see cref="DropdownStyle"/> paints the selected row's fill from <c>StateSelected</c> (a
    /// translucent forest-green <c>Primary</c> wash) and its selected LABEL from <c>SelectionAccentColor =
    /// Primary</c> — dark green text on a see-through dark-green tint over the dark slate, which is unreadable.
    /// Recolor just those two: a FULLY-OPAQUE <c>Primary</c> fill (the same green the buttons use, at alpha 1
    /// so the row reads as a solid selection) with the label flipped to <c>OnPrimary</c> (the chalk-white that
    /// already reads on the accent fill everywhere else). Only when Pixel-Art Display is on (chalkboard theme
    /// active); otherwise the global-theme default menu.</summary>
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
        // Route the guestbook glyph through the SAME NavIconColor seam the built-in nav buttons use, resolved
        // from THIS dialog's theme (chalkboard when Pixel-Art is on) — so it reads as a sibling of
        // read/edit/pin/settings (same slate-brown inactive tint) rather than standing out. The active state
        // still uses its own NavActiveGuestbook accent.
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        yield return TitleButton(
            "scribeguest",
            "scribe-tab-guestbook",
            NavIconColor(colors),
            NavButtonSize,
            OnClickSwitchToVisitors,
            boxShadows: NavButtonShadow,
            activeColor: IsVisitorsView ? ScribeRowConstants.NavActiveGuestbook : null);
        yield return TitleButton(
            "scribeinventory",
            "scribe-tab-inbox",
            NavIconColor(colors),
            NavButtonSize,
            OnClickSwitchToInbox,
            boxShadows: NavButtonShadow,
            activeColor: IsInboxView ? ScribeRowConstants.NavActiveGuestbook : null);
    }
}
