using System.Collections.Generic;
using Gui.Widgets.Framework;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Lectern block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>.
/// The Lectern adds only the Guestbook nav button.
/// </summary>
public sealed class GuiDialogScribeLecternLibGui : ScribeDialogBase
{
    public GuiDialogScribeLecternLibGui(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, host, capi)
    {
        // Guest Book is what a plain right-click opens (see EnterGrantedView).
        DefaultToVisitorsView();
    }

    /// <summary>Shared placed block: <see cref="ScribeDialogBase.RequestEditorAccess"/> sends a request and
    /// the grant lands asynchronously in <see cref="ScribeDialogBase.EnterEditorMode"/>, so a Handbook
    /// "Add to Scribe" click stashes its append and waits for the grant.</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>Guest Book comes before Read/Edit/Pinned in the nav order.</summary>
    protected override IEnumerable<Widget> GetLeadingNavButtons()
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

    /// <summary>Right-click lands on Guest Book rather than the base's Read view. Crouch+right-click
    /// (quick-add) is handled upstream in <see cref="BlockScribeWritingStation.OnBlockInteractStart"/>.</summary>
    public override void EnterGrantedView() => OnClickSwitchToVisitors();

    protected override IEnumerable<Widget> GetExtraNavButtons()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        // Players who have never received an assignment get no Inbox nav button here.
        if (modSystem.MyReceivedAssignments.Count > 0)
        {
            yield return TitleButton(
                "scribeinboxarrow",
                "scribe-tab-inbox",
                colors.OnSurfaceVariant,
                NavButtonSize,
                OnClickSwitchToInbox,
                boxShadows: NavButtonShadow,
                activeColor: IsInboxView ? ScribeRowConstants.NavActiveGuestbook : null,
                shimmer: ShowInboxShimmer);
        }
    }
}
