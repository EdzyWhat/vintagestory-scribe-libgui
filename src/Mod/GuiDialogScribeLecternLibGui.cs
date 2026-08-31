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

    /// <summary>The Lectern is a shared placed block: editor access requires a server lock round-trip,
    /// so <see cref="ScribeDialogBase.RequestEditorAccess"/> sends a request and the grant lands
    /// asynchronously in <see cref="ScribeDialogBase.EnterEditorMode"/>. A Handbook "Add to Scribe" click
    /// therefore stashes its append and waits for that grant (add-tracker-link-tasks 3.4).</summary>
    protected override bool EditorAccessIsAsync => true;

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
        // Gated on assignment history (refine-assignment-desk-inbox-ux 3.1 / inbox-tab spec): a player
        // who has never received an assignment gets no Inbox nav button here — the Assignment Desk and
        // standalone Inbox block are unaffected, they always show their Inbox surface.
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
