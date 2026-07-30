using System.Collections.Generic;
using Gui.Widgets.Framework;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Lectern block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>.
/// All view state, build methods, lock orchestration, autosave, title editing, scroll management,
/// and nav-button layout live in the base class. The Lectern adds only the Guestbook nav button.
/// </summary>
public sealed class GuiDialogScribeLecternLibGui : ScribeDialogBase
{
    public GuiDialogScribeLecternLibGui(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, host, capi)
    {
    }

    protected override IEnumerable<Widget> GetExtraNavButtons()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        yield return TitleButton(
            "scribeguest",
            "scribe-tab-guestbook",
            colors.OnSurfaceVariant,
            NavButtonSize,
            OnClickSwitchToVisitors,
            boxShadows: NavButtonShadow,
            activeColor: IsVisitorsView ? ScribeRowConstants.NavActiveGuestbook : null);
    }
}
