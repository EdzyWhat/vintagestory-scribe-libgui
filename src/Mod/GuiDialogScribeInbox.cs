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
/// tab" — keeping an Assignment Inbox button (always active; nothing else to switch to) plus Settings.
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
        // Unlike every other Inbox-reaching surface, this dialog never routes through
        // OnClickSwitchToInbox() (it has no nav button that does) — mark-seen must fire here directly,
        // or the ambient particle/nav shimmer can persist indefinitely regardless of how long the
        // player looks at their assignments (refine-assignment-desk-inbox-ux D2/4.1-4.2).
        MarkInboxSeenIfNeeded();
    }

    /// <summary>The Inbox block is a shared placed block: editor access requires a server lock
    /// round-trip, same as the Lectern/Scriptorium/Chalkboard/Assignment Desk. Retained even though no
    /// nav button here ever opens the editor view — see the Assignment Desk's identical note.</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>This dialog has no Read view to land on (see class remarks) — a plain access grant, the
    /// ordinary reply every right-click on the block gets, must leave the dialog on Inbox instead of
    /// being force-switched to a nonexistent Read view. Overriding the base's <c>EnterReadMode()</c>
    /// default this way was the fix for the Inbox always opening on Read despite having no Read nav
    /// button.</summary>
    public override void EnterGrantedView()
    {
        LeaveEditorIfActive();
        // Every right-click re-open grants access here and lands back on the (only) Inbox view without
        // going through OnClickSwitchToInbox() — mark-seen must fire on every grant, not just the first
        // (refine-assignment-desk-inbox-ux D2/4.2).
        MarkInboxSeenIfNeeded();
        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Inbox + Settings. The Inbox tab is this block's only view, so the Inbox button is
    /// always shown active and never actually switches views — it exists so the block's sole
    /// capability has a visible, labeled nav entry rather than an implicit/unreachable one.</summary>
    protected override Widget BuildRightColNav()
    {
        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float size = NavButtonSize;
        var navColor = NavIconColor(colors);

        Widget inboxBtn = TitleButton("scribeinboxarrow", "scribe-tab-inbox", navColor,
            size: size, onTap: OnClickSwitchToInbox, boxShadows: NavButtonShadow,
            activeColor: IsInboxView ? ScribeRowConstants.NavActiveGuestbook : null);
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
            children: new Widget[] { inboxBtn, settingsBtn });
    }
}
