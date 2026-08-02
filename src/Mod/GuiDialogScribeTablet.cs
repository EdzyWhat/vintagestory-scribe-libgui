using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Basic;         // Text, Container
using Gui.Widgets.Framework;     // Widget, ThemeData
using Gui.Widgets.Layout;        // Column, Expanded, Padding, SizedBox, Align, Alignment, CrossAxisAlignment
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector4
using Scribe.Core;
using Scribe.Core.Cuneiform;     // GlyphBundle
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;   // GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

/// <summary>
/// The bespoke dialog for the clay/wax tablet item — the scratch tier's always-edit writing surface
/// (add-tablet-dialog, Proposal C). A thin subclass of <see cref="ScribeDialogBase"/> that reuses the
/// inherited title editing, drag-grip, close, autosave, and network-send chrome; it differs from the
/// Notebook in only a few places:
/// <list type="bullet">
/// <item><b>No tabs.</b> <see cref="BuildRightColNav"/> returns an EMPTY right column — none of the
/// Read/Edit/Pinned/Settings nav buttons render. The column still occupies <c>SideColW</c>, so the center
/// content keeps its symmetric side margins (the shared three-column skeleton is untouched).</item>
/// <item><b>Always edit.</b> The dialog enters editor mode in its constructor (before the first
/// <c>Build()</c>, which <c>TryOpen</c> runs), so <see cref="ScribeDialogBase.BuildCentralRegion"/> can
/// render the inherited editable task list with no view switching.</item>
/// <item><b>Cuneiform title banner.</b> The overridden central region stacks a display-only
/// <see cref="CuneiformText"/> banner (the document title, in the cuneiform pseudo-font — Proposal A) over
/// the inherited editable task list. This is the first real cuneiform text inside production dialog chrome.
/// The single <see cref="ScribeTaskFont.UseCuneiform"/> branch falls the banner back to normal text when
/// the player disables the font.</item>
/// <item><b>Earthen theme + material backdrop.</b> <see cref="ResolveTheme"/> selects
/// <see cref="ScribeTheme.Tablet"/>; the host reports the clay/wax backdrop matching the item's material.</item>
/// </list>
///
/// <para>Like the Notebook it is item-hosted, not block-hosted: editor access is granted immediately with
/// no server round-trip (no lock to contend over — only one player holds an item), lock-release is a
/// no-op, saves route through <see cref="ScribeNotebookSaveMessage"/>, and the proximity auto-close is
/// disabled (an item has no block position). It closes when the held item stops being a Scribe document.</para>
/// </summary>
public class GuiDialogScribeTablet : ScribeDialogBase
{
    private IInventory? _hotbar;

    public GuiDialogScribeTablet(IScribeDocumentHost host, ICoreClientAPI capi)
        : base(new BlockPos(0), host, capi)
    {
        // Always-edit: seed the editor scratch from the current document NOW, before TryOpen calls Build()
        // (GuiBase.TryOpen inflates the tree before OnGuiOpened runs). Mirrors the Notebook's immediate
        // grant — there is no lock to wait on for an item-hosted document.
        EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));

        capi.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
        _hotbar = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (_hotbar != null)
            _hotbar.SlotModified += OnHotbarSlotModified;
    }

    /// <summary>Disable the engine's frame-by-frame range check. The tablet is a carried item, not bound to
    /// a block position, so distance must never auto-close it (matches the Notebook).</summary>
    protected override double InteractionRange => double.MaxValue;

    protected override string EmptyHintLangKey => "scribe:scribe-gui-edit-hint-tablet";

    /// <summary>The tablet grants editor access immediately without a server round-trip (item-hosted, no
    /// lock contention). Seeds the scratch from the host's current document.</summary>
    protected override void RequestEditorAccess()
    {
        EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
    }

    /// <summary>No editor lock on an item — nothing to release.</summary>
    protected override void SendReleaseLockPacket() { }

    /// <summary>The tablet is always-edit with no Read view, so the editor footer omits the "Done editing"
    /// button — tapping it would leave editor mode and null the scratch the central region reads
    /// (add-tablet-dialog D4).</summary>
    protected override bool ShowEditorSwitchToRead => false;

    /// <summary>The tablet uses its own earthen palette rather than the parchment one (add-tablet-dialog D6).</summary>
    protected override ThemeData ResolveTheme(bool pixelArt) => ScribeTheme.ForTablet(pixelArt);

    /// <summary>No nav column on the tablet: an empty right column whose <c>SideColW</c> width (set by the
    /// enclosing <c>SizedBox</c> in <c>BuildSectionInnerBox</c>) still preserves the symmetric side margin.
    /// None of the Read/Edit/Pinned/Settings buttons render (add-tablet-dialog D3).</summary>
    protected override Widget BuildRightColNav() => new SizedBox();

    /// <summary>The tablet's always-edit central region: a display-only cuneiform title banner on top, then
    /// the inherited editable task list filling the remainder. The editor is REUSED, not forked
    /// (add-tablet-dialog D2/D4) — task add/edit/check/pin keep working under the tablet's 10-task / 1-pin
    /// policy. The banner takes a fixed height derived from its em size (an <c>Expanded</c> would be inert
    /// inside the editor's scroll view, so the banner is measured first and the list gets the rest).</summary>
    protected override Widget BuildCentralRegion() =>
        new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                BuildTitleBanner(),
                new Expanded(child: BuildEditorContent()),
            });

    /// <summary>The display-only cuneiform title banner: the document's title (always present — falls back
    /// to the host's "Tablet" default) rendered in the cuneiform pseudo-font at a fixed pixel height. The
    /// single <see cref="ScribeTaskFont.UseCuneiform"/> branch point routes to the normal task font via
    /// <see cref="ScribeTaskFont.Resolve"/> when the player has disabled cuneiform, so the accessibility
    /// fallback is one decision in one place (add-tablet-dialog D5).</summary>
    private Widget BuildTitleBanner()
    {
        var colors = ScribeTheme.ForTablet(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        Vector4 ink = colors.OnSurface;

        // Prominent but bounded: the window body size, scaled by the player's font-scale, then enlarged so
        // the title reads as a headline over the task rows. The banner's box is this em height plus padding.
        float fontSizeEm = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale) * 2.4f;

        string title = DisplayDocumentTitle;
        bool useCuneiform = ScribeTaskFont.UseCuneiform(modSystem.MySettings.DisableCuneiformFont);

        Widget line;
        if (useCuneiform)
        {
            GlyphBundle? bundle = modSystem.GetCuneiformBundle();
            line = new CuneiformText(text: title, fontSizeEm: fontSizeEm, inkColor: ink, bundle: bundle);
        }
        else
        {
            // Fallback: the same title through the normal text path in the player's resolved task font.
            line = new Text(title, new TextStyle
            {
                FontSize = fontSizeEm,
                Color = ink,
                FontFamily = ScribeTaskFont.Resolve(modSystem.MySettings.TaskFontFamily),
            });
        }

        // Fixed height (em + vertical breathing room), left-aligned to sit above the task rows. NOT an
        // Expanded — the banner must not consume the list's space, and Expanded is inert in a scroll view.
        const float bannerVerticalPadding = 12f;
        return new SizedBox(
            height: fontSizeEm + bannerVerticalPadding * 2f,
            child: new Padding(
                EdgeInsets.Symmetric(horizontal: 10f, vertical: bannerVerticalPadding),
                child: new Align(Alignment.CenterLeft, child: line)));
    }

    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs _)
    {
        // Any Scribe document item may host this dialog; keep it open only while a document item remains
        // the active hand item (same guard as the Notebook).
        if (capi.World.Player.Entity.ActiveHandItemSlot?.Itemstack?.Collectible
            is not IScribeDocumentItem)
            TryClose();
    }

    private void OnHotbarSlotModified(int slotId)
    {
        if (slotId == capi.World.Player.InventoryManager.ActiveHotbarSlotNumber)
            OnActiveSlotChanged(default!);
    }

    public override void OnGuiClosed()
    {
        capi.Event.AfterActiveSlotChanged -= OnActiveSlotChanged;
        if (_hotbar != null)
            _hotbar.SlotModified -= OnHotbarSlotModified;
        base.OnGuiClosed();
    }

    /// <summary>Tablet saves reuse <see cref="ScribeNotebookSaveMessage"/> (no new packet) so the server
    /// writes directly into the held ItemStack, exactly like the Notebook.</summary>
    protected override void SendFlushPacket(byte[] documentBytes)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = host.Document.DocId.ToByteArray(),
            DocumentBytes = documentBytes,
        });
    }
}
