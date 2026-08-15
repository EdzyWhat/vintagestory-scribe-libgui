using System.Collections.Generic;
using Gui.Widgets.Framework;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Scriptorium block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>,
/// mirroring <see cref="GuiDialogScribeLecternLibGui"/>. All view state, build methods, lock
/// orchestration, autosave, title editing, scroll management, and nav-button layout live in the base
/// class. For v1.2 the Scriptorium adds only the Guestbook nav button, exactly like the Lectern (both
/// are shared, placed blocks). This subclass exists as the v1.3 attachment point for the
/// Scriptorium-only Assign &amp; History and Inbox nav buttons.
/// </summary>
public sealed class GuiDialogScribeScriptorium : ScribeDialogBase
{
    public GuiDialogScribeScriptorium(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, host, capi)
    {
    }

    /// <summary>The Scriptorium is a shared placed block (like the Lectern): editor access requires a
    /// server lock round-trip, so the grant lands asynchronously in
    /// <see cref="ScribeDialogBase.EnterEditorMode"/>. A Handbook "Add to Scribe" click stashes its append
    /// and waits for that grant (add-tracker-link-tasks 3.4).</summary>
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
    }
}
