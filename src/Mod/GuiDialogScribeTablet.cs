using System;                    // Action (footer Settings gear seam)
using Gui.Rendering.Text;        // TextStyle (cuneiform title seam overrides)
using Gui.Widgets.Framework;     // Widget, ThemeData
using Gui.Widgets.Layout;        // SizedBox
using Scribe.Core;               // ScribeDocumentCodec, ScribePlayerSettings
using Scribe.Core.Cuneiform;     // GlyphBundle (cuneiform title path)
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
/// <item><b>Live cuneiform input.</b> Under the single <see cref="ScribeTaskFont.UseCuneiform"/> branch,
/// the tablet's editable task rows type directly in the cuneiform pseudo-font (Proposal A) with a synthetic
/// caret — driven by <see cref="DecorateRowStyle"/> flipping on the cuneiform row path
/// (add-tablet-cuneiform-chrome). Disabling the font reverts every surface to the normal editable field.</item>
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

    /// <summary>Flip the editable rows to live cuneiform under the single <see cref="ScribeTaskFont.UseCuneiform"/>
    /// branch (add-tablet-cuneiform-chrome). When the player disables cuneiform, the flag resolves false and
    /// the rows fall back to the normal editable field — one decision, threaded to every tablet surface. The
    /// glyph bundle is the client-cached parse; null (asset not yet loaded) simply renders no strokes.</summary>
    private protected override ScribeRowStyle DecorateRowStyle(ScribeRowStyle style)
    {
        if (ActiveCuneiformBundle is not { } bundle)
        {
            return style;
        }

        return style with
        {
            UseCuneiform = true,
            CuneiformBundle = bundle,
        };
    }

    /// <summary>Resting title: display-only cuneiform, single-line, hard-clipped to the band width, under
    /// the single cuneiform branch (add-tablet-cuneiform-chrome D2). When cuneiform is disabled the base
    /// default (a RichText with ellipsis) is used, so the title reverts with every other tablet surface.
    /// A null glyph bundle (asset not yet loaded) also falls back to the readable default.</summary>
    private protected override Widget BuildTitleDisplay(string displayTitle, TextStyle titleStyle)
    {
        var bundle = ActiveCuneiformBundle;
        if (bundle is null)
        {
            return base.BuildTitleDisplay(displayTitle, titleStyle);
        }

        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        return new Gui.Widgets.Painting.Clip(
            child: new CuneiformText(
                text: displayTitle,
                fontSizeEm: titleStyle.FontSize,
                inkColor: colors.OnSurface,
                bundle: bundle));
    }

    /// <summary>Editing title: a live single-line cuneiform input bound to the SAME title controller/focus
    /// node the base owns, so <see cref="ScribeDialogBase"/>'s commit/blur/deferred-rebuild machinery is
    /// untouched (add-tablet-cuneiform-chrome D2). Falls back to the base's stock TextField when cuneiform is
    /// disabled or the glyph bundle hasn't loaded. The shared key handler (<see cref="OnTitleFieldKeyDown"/>)
    /// enforces the identical maxlength + Enter/Escape commit as the normal title field.</summary>
    private protected override Widget BuildTitleField(TextStyle titleStyle)
    {
        var bundle = ActiveCuneiformBundle;
        if (bundle is null)
        {
            return base.BuildTitleField(titleStyle);
        }

        return new ScribeCuneiformTitleField(
            TitleController,
            TitleFocusNode,
            fontSizeEm: titleStyle.FontSize,
            bundle: bundle,
            onKeyDown: OnTitleFieldKeyDown);
    }

    /// <summary>The parsed glyph bundle when the single cuneiform branch is active for the tablet, else null
    /// (cuneiform disabled OR the asset hasn't loaded). One place that resolves the branch so the title
    /// display, title input, rows, and labels all agree (add-tablet-cuneiform-chrome D4).</summary>
    private GlyphBundle? ActiveCuneiformBundle =>
        ScribeTaskFont.UseCuneiform(modSystem.MySettings.DisableCuneiformFont)
            ? modSystem.GetCuneiformBundle()
            : null;

    /// <summary>The tablet has no nav column to reach Scribe Settings through (D3), so its editor footer
    /// grows a Settings gear right of the ⓘ info button, wired to <see cref="ScribeModSystem.OpenSettings"/>
    /// (add-tablet-cuneiform-chrome). The base returns null, keeping the Lectern/Notebook footer gear-free.</summary>
    private protected override Action? EditorSettingsGearAction => modSystem.OpenSettings;

    /// <summary>No nav column on the tablet: an empty right column whose <c>SideColW</c> width (set by the
    /// enclosing <c>SizedBox</c> in <c>BuildSectionInnerBox</c>) still preserves the symmetric side margin.
    /// None of the Read/Edit/Pinned/Settings buttons render (add-tablet-dialog D3).</summary>
    protected override Widget BuildRightColNav() => new SizedBox();

    /// <summary>The tablet's always-edit central region: just the inherited editable task list, now that the
    /// display-only cuneiform title banner is retired (add-tablet-cuneiform-chrome — the 2026-08-02 playtest
    /// rejected it as a redundant second copy of the title bar). The editor is REUSED, not forked
    /// (add-tablet-dialog D2/D4); its rows type in live cuneiform via <see cref="DecorateRowStyle"/>, and the
    /// title itself renders in cuneiform through the base title bar. Task add/edit/check/pin keep working
    /// under the tablet's 10-task / 1-pin policy.</summary>
    protected override Widget BuildCentralRegion() => BuildEditorContent();

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
