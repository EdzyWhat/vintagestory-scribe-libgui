using System;                    // Action (footer Settings gear seam)
using Gui.Rendering.Text;        // TextStyle (cuneiform title seam overrides)
using Gui.Widgets.Framework;     // Widget, ThemeData, ColorScheme
using Gui.Widgets.Layout;        // SizedBox
using OpenTK.Mathematics;        // Vector4 (title-chrome engrave color, add-tablet-clay-type-themes 8.5)
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

    /// <summary>The tablet item's <c>material</c> variant (<c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c>/
    /// <c>wax</c>), threaded from the item's open path so <see cref="ResolveTheme"/> can pick this tablet's
    /// per-clay palette — the same seam backdrop selection reads (add-tablet-clay-type-themes). Null when the
    /// dialog is opened without a known material; the theme selector then falls back to the fire palette.</summary>
    private readonly string? _material;

    public GuiDialogScribeTablet(IScribeDocumentHost host, ICoreClientAPI capi, string? material = null)
        : base(new BlockPos(0), host, capi)
    {
        _material = material;

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

    /// <summary>The tablet uses its own per-clay-type palette rather than the parchment one, keyed to the
    /// item's <see cref="_material"/> variant (add-tablet-clay-type-themes) — red/blue/fire tablets each
    /// resolve their own colors, and the resolved theme agrees with the resolved backdrop (both key off the
    /// same material). Pixel-Art off still falls back to the player's global theme.</summary>
    protected override ThemeData ResolveTheme(bool pixelArt) => ScribeTheme.ForTablet(_material, pixelArt);

    /// <summary>Engrave the title-bar pencil + drag-grip into the clay (add-tablet-clay-type-themes 8.5):
    /// tint them with this tablet's dark material ink (<c>OnSurface</c>) at partial alpha instead of the
    /// global gray. The base applies it via <see cref="SKBlendMode.SrcIn"/> (glyph-only), so the partial
    /// alpha fades only the STROKES — the clay texture bleeds faintly through them, reading as a darkened
    /// engraved impression — while the transparent icon tile stays clear. (An earlier attempt used a
    /// Multiply blend, but VsIcon applies its tint as a color FILTER, which fills the whole transparent
    /// quad under Multiply — the 2026-08-03 "pale tile" regression. SrcIn + a dark alpha avoids that.)
    /// Only on the Pixel-Art (backdrop) path — with Pixel-Art OFF the tablet follows the global theme over
    /// a flat panel, so we defer to the base gray chrome there.</summary>
    private protected override Vector4 TitleChromeGlyphColor(ColorScheme colors)
    {
        if (!modSystem.MySettings.PixelArtDisplay)
        {
            return base.TitleChromeGlyphColor(colors);
        }
        // 0.8 alpha keeps the strokes firmly dark while letting a hint of clay show through.
        return colors.OnSurface with { W = 0.8f };
    }

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
            // Hand-written wobble on the tablet's cuneiform rows (add-cuneiform-handwriting-feel). The
            // per-row seed (its stable TaskId) is supplied at the field; strength comes from the default
            // until the client-config knob (task 6) sources it. 0 would reproduce the crisp geometry.
            CuneiformJitter = CuneiformMetrics.DefaultJitterStrength,
            // Newly-typed text presses in stroke-by-stroke, gated by the player's opt-in setting (defaults
            // off). Re-deriving the row style per build means toggling it in Scribe Settings repaints an
            // open tablet.
            CuneiformProgression = modSystem.MySettings.CuneiformProgression,
            // Per-material outer glow to lift the row ink off this tablet's clay backdrop
            // (add-tablet-clay-type-themes). Keyed off the same material the theme/backdrop use.
            CuneiformGlow = CuneiformGlowTable.For(_material),
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
                bundle: bundle,
                // Per-material glow so the resting title lifts off the clay backdrop like the rows do.
                glow: CuneiformGlowTable.For(_material)));
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
            onKeyDown: OnTitleFieldKeyDown,
            jitterStrength: CuneiformMetrics.DefaultJitterStrength,
            // A fixed seed: one title band per tablet, so a constant keeps it stable across rebuilds while
            // typing (a text-derived seed would re-wobble the whole title on every keystroke).
            jitterSeed: TitleJitterSeed,
            // Stroke-by-stroke title reveal, gated by the same player setting as the rows (defaults off).
            progression: modSystem.MySettings.CuneiformProgression,
            // Per-material glow so the editing title lifts off the clay backdrop like the rows/resting title.
            glow: CuneiformGlowTable.For(_material));
    }

    /// <summary>Fixed base seed for the title band's cuneiform jitter — arbitrary constant, distinct from
    /// the rows' TaskId seeds so the title doesn't share a wobble pattern with any row.</summary>
    private const int TitleJitterSeed = 0x5C71B7;

    /// <summary>The parsed glyph bundle when the single cuneiform branch is active for the tablet, else null
    /// (cuneiform disabled OR the asset hasn't loaded). One place that resolves the branch so the title
    /// display, title input, rows, and labels all agree (add-tablet-cuneiform-chrome D4).</summary>
    private GlyphBundle? ActiveCuneiformBundle =>
        ScribeTaskFont.UseCuneiform(modSystem.MySettings.CuneiformTablets)
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
