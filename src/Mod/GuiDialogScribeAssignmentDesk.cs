using System.Collections.Generic;
using Gui.Widgets.Framework;     // Widget, ColorScheme
using Gui.Widgets.Layout;        // Column, CrossAxisAlignment, MainAxisAlignment
using Gui.Core.Layout;           // MainAxisSize
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Assignment Desk block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>. All
/// view state, build methods, lock orchestration, autosave, title editing, and scroll management live
/// in the base class; this dialog's only real difference is its right-column nav, which drops the
/// base's Read/Editor/Pinned buttons in favor of an Assignment/Inbox tab-switcher pair (this block's
/// dialog has exactly two tabs per <c>assignment-desk-block</c>'s spec — Read/Editor/Pinned don't apply
/// here) and defaults to the Assignment tab on open.
///
/// <para>PARTIAL (add-assignment-and-quest-support §5.4): the nav pair and Assignment-tab default are
/// real; the Assignment tab's own create-and-send form content (§5.5) is not — it still renders through
/// <see cref="ScribeDialogBase.BuildAssignmentContent"/>'s placeholder alias to the Inbox empty-state
/// text, and the Inbox tab's real row/filter content (§7) doesn't exist yet either. The right-column
/// layout here is also a placeholder simplification of the spec's "title bar + tab-switcher nav row"
/// 2-tab chrome (which implies dropping the vertical SectionRightCol entirely) — reusing the existing
/// vertical nav column with just these two buttons + Settings keeps Settings reachable without a new
/// layout mechanism, pending a real 2-tab-only chrome pass once the tab content itself exists.</para>
/// </summary>
public sealed class GuiDialogScribeAssignmentDesk : ScribeDialogBase
{
    public GuiDialogScribeAssignmentDesk(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, host, capi)
    {
        // design.md Decision 1: "Only the Assignment Desk dialog ever sets viewMode to Assignment" —
        // and it does so as its DEFAULT view, not merely a reachable one. Safe here: TryOpen's first
        // Build() runs after this ctor returns.
        DefaultToAssignmentView();
    }

    /// <summary>The Assignment Desk is a shared placed block: editor access requires a server lock
    /// round-trip, same as the Lectern/Scriptorium/Chalkboard. Retained even though no nav button here
    /// currently opens the editor view, so any future/incidental editor-access grant (e.g. a Handbook
    /// append targeting this host) behaves consistently with every other writing station.</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>Replaces the base's Read/Editor/Pinned/Settings column with just this dialog's own
    /// Assignment/Inbox tab-switcher pair plus Settings — this dialog has no Read/Editor/Pinned view to
    /// switch to (see class remarks). Mirrors <see cref="GuiDialogScribeTablet"/>'s precedent for
    /// replacing this seam wholesale rather than layering onto <see cref="GetExtraNavButtons"/>, which is
    /// reserved for surfaces that keep the base column and only ADD to it.</summary>
    protected override Widget BuildRightColNav()
    {
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float size = NavButtonSize;
        var navColor = NavIconColor(colors);

        // Dedicated rolled-scroll glyph (§13.4), matching the same icon the row-level accepted-
        // assignment marker uses (ScribeAssignedTaskIcon) — replaces the earlier edit-pencil placeholder.
        Widget assignmentBtn = TitleButton("scribeassignment", "scribe-tab-assignment", navColor,
            size: size, onTap: OnClickSwitchToAssignment, boxShadows: NavButtonShadow,
            activeColor: IsAssignmentView ? ScribeRowConstants.NavActiveEdit : null);
        Widget inboxBtn = TitleButton("scribeinventory", "scribe-tab-inbox", navColor,
            size: size, onTap: OnClickSwitchToInbox, boxShadows: NavButtonShadow,
            activeColor: IsInboxView ? ScribeRowConstants.NavActiveGuestbook : null,
            shimmer: ShowInboxShimmer);
        Widget settingsBtn = TitleButton("scribegear", "scribe-gui-nav-settings", navColor,
            size: size, onTap: modSystem.OpenSettings, boxShadows: NavButtonShadow,
            activeColor: modSystem.IsSettingsOpen ? ScribeRowConstants.NavActiveSettings : null);

        float sideColW = host.GetLayout(modSystem.MySettings.PixelArtSize).SideColW;
        float navBoxW = size - ScribeRowButton.BoxShrink;

        return new Column(
            spacing: 16,
            mainAxisAlignment: MainAxisAlignment.Start,
            crossAxisAlignment: NavButtonAlignment(sideColW, navBoxW),
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { assignmentBtn, inboxBtn, settingsBtn });
    }
}
