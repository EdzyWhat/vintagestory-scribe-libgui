using Gui.Widgets.Framework;     // Widget
using Gui.Widgets.Layout;        // Column, CrossAxisAlignment, MainAxisAlignment
using Gui.Core.Layout;           // MainAxisSize
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The standalone Inbox block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>. All
/// view state, build methods, lock orchestration, autosave, title editing, and scroll management live
/// in the base class; this dialog opens directly into (and, since it exposes no other tab, permanently
/// stays on) the shared Inbox tab. Its right-column nav drops the base's Read/Editor/Pinned buttons
/// entirely — per <c>inbox-block</c>'s spec this block's "sole capability is showing the shared Inbox
/// tab" — keeping only Settings reachable.
///
/// <para>PARTIAL (add-assignment-and-quest-support §6.3): defaulting into (and staying on) the Inbox
/// tab is real; the Inbox tab's own row/filter content (§7) does not exist yet, so it still renders
/// through <see cref="ScribeDialogBase.BuildInboxContent"/>'s empty-state placeholder.</para>
/// </summary>
public sealed class GuiDialogScribeInbox : ScribeDialogBase
{
    public GuiDialogScribeInbox(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, host, capi)
    {
        DefaultToInboxView();
    }

    /// <summary>The Inbox block is a shared placed block: editor access requires a server lock
    /// round-trip, same as the Lectern/Scriptorium/Chalkboard/Assignment Desk. Retained even though no
    /// nav button here ever opens the editor view — see the Assignment Desk's identical note.</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>Just Settings — no Read/Editor/Pinned (this block has none) and no Inbox button either
    /// (the Inbox tab is the only view, so there is nothing to switch away from and back to).</summary>
    protected override Widget BuildRightColNav()
    {
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float size = NavButtonSize;

        Widget settingsBtn = TitleButton("scribegear", "scribe-gui-nav-settings", NavIconColor(colors),
            size: size, onTap: modSystem.OpenSettings, boxShadows: NavButtonShadow,
            activeColor: modSystem.IsSettingsOpen ? ScribeRowConstants.NavActiveSettings : null);

        float sideColW = host.GetLayout(modSystem.MySettings.PixelArtSize).SideColW;
        float navBoxW = size - ScribeRowButton.BoxShrink;

        return new Column(
            spacing: 16,
            mainAxisAlignment: MainAxisAlignment.Start,
            crossAxisAlignment: NavButtonAlignment(sideColW, navBoxW),
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { settingsBtn });
    }
}
