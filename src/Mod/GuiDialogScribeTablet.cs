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

    /// <summary>The tablet's drying state (wet/hard/fired), threaded from the item's open path. A WET tablet
    /// is the always-edit surface; a HARD or FIRED tablet opens read-only (tablet-firing). Drives
    /// <see cref="IsEditable"/>, the read-only affordance suppression, and the empty-state message wording.</summary>
    private readonly TabletState _state;

    /// <summary>Whether this tablet is editable: only a WET tablet. A hard tablet is read-only until
    /// rehydrated; a fired tablet is permanently read-only (tablet-firing Decision 5). The single resolve
    /// point every read-only branch below keys off.</summary>
    private bool IsEditable => _state == TabletState.Wet;

    public GuiDialogScribeTablet(IScribeDocumentHost host, ICoreClientAPI capi, string? material = null,
        TabletState state = TabletState.Wet)
        : base(new BlockPos(0), host, capi)
    {
        _material = material;
        _state = state;

        // Wet tablet = always-edit: seed the editor scratch from the current document NOW, before TryOpen
        // calls Build() (GuiBase.TryOpen inflates the tree before OnGuiOpened runs). Mirrors the Notebook's
        // immediate grant — there is no lock to wait on for an item-hosted document. A hard/fired tablet
        // stays in the base's default Read view (scratch null), so it never enters editor mode.
        if (IsEditable)
        {
            EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
        }

        capi.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
        _hotbar = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (_hotbar != null)
            _hotbar.SlotModified += OnHotbarSlotModified;
    }

    /// <summary>Disable the engine's frame-by-frame range check. The tablet is a carried item, not bound to
    /// a block position, so distance must never auto-close it (matches the Notebook).</summary>
    protected override double InteractionRange => double.MaxValue;

    /// <summary>The centered empty-document message. A WET tablet shows the editable "write here" hint; a
    /// read-only tablet's empty document (e.g. pulled blank from Creative, or fired without writing) shows a
    /// state-appropriate line instead of an empty task list, so it reads as intentional rather than broken
    /// (tablet-firing Decision 6): dried → "dunk in water to edit", fired → "fired without writing".</summary>
    protected override string EmptyHintLangKey => _state switch
    {
        TabletState.Fired => "scribe:tablet-fired-empty",
        TabletState.Hard  => "scribe:tablet-hard-empty",
        _                                 => "scribe:scribe-gui-edit-hint-tablet",
    };

    /// <summary>A hard or fired tablet is a permanently-read-only surface: the read view drops its "switch
    /// to editor" footer button and blocks TEXT editing (tablet-firing task 5.2). It does NOT force the
    /// checkbox/pin inert — see <see cref="ReadViewCompletionAndPinLive"/> (zero-point-three-fixes §7.3).</summary>
    private protected override bool ReadViewIsReadOnly => !IsEditable;

    // TitleMaxLines is no longer overridden here: two-line title wrapping is now the shared base default
    // (wrap-titles-all-surfaces), so BOTH the cuneiform title (BuildTitleDisplay/BuildTitleField, unchanged)
    // and the cuneiform-OFF readable RichText fallback wrap to two lines. Previously this override forced the
    // cuneiform-off path back to a single line; that gating is intentionally dropped.

    /// <summary>Keep the checkbox and pin interactive on a read-only (hard/fired) tablet
    /// (zero-point-three-fixes §7.3): completing and unpinning stay reachable, so firing a tablet with a
    /// pinned task never strands that pin on the HUD. Only text editing is blocked. A wet tablet is fully
    /// editable, so this only matters when read-only — return true whenever the tablet is not editable.</summary>
    private protected override bool ReadViewCompletionAndPinLive => !IsEditable;

    /// <summary>On a read-only (hard/fired) tablet, tapping a row's text surfaces the material-specific
    /// locked message via the game's transient-error path (zero-point-three-fixes §7.4) — hardened tablets
    /// can be softened; fired tablets are permanent. Null on a wet tablet (text is edited normally there).</summary>
    private protected override Action<Guid>? ReadViewTextEditRefused => IsEditable
        ? null
        : _ => capi.TriggerIngameError(this, "scribe-tablet-locked", Lang.Get(ReadOnlyLockedLangKey));

    /// <summary>The material-specific "this tablet is locked" lang key for the current read-only state — fired
    /// tablets are permanent, hardened tablets can be softened. Shared by the read-view row-text-edit refusal
    /// and the Handbook read-only append notice so both surfaces the same wording (feedback 7.13).</summary>
    private string ReadOnlyLockedLangKey => _state == TabletState.Fired
        ? "scribe:tablet-fired-locked"
        : "scribe:tablet-hard-locked";

    /// <summary>A fired/hardened tablet is permanently read-only, so a Handbook "Add to Scribe" click on an open
    /// set tablet can never reach the editor — report it rather than dropping silently (feedback 7.13).</summary>
    protected override bool CanEditFromHandbook => IsEditable;

    /// <summary>Material-specific read-only notice for a Handbook append onto a set tablet, reusing the same
    /// fired/hardened wording as the row-text-edit refusal (feedback 7.13).</summary>
    protected override void NotifyHandbookAppendReadOnly()
        => capi.TriggerIngameError(this, "scribe-tablet-locked", Lang.Get(ReadOnlyLockedLangKey));

    /// <summary>A WET tablet grants editor access immediately without a server round-trip (item-hosted, no
    /// lock contention), seeding the scratch from the host's current document. A HARD or FIRED tablet is
    /// read-only: this is a hard no-op, so even if some path tried to enter the editor (there is none — no
    /// nav column, no read-view "switch to editor" button), a set tablet can never become editable
    /// (tablet-firing read-only audit, task 5.2).</summary>
    protected override void RequestEditorAccess()
    {
        if (!IsEditable) return;
        EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
    }

    /// <summary>No editor lock on an item — nothing to release.</summary>
    protected override void SendReleaseLockPacket() { }

    /// <summary>The tablet is always-edit with no Read view, so the editor footer omits the "Done editing"
    /// button — tapping it would leave editor mode and null the scratch the central region reads
    /// (add-tablet-dialog D4).</summary>
    protected override bool ShowEditorSwitchToRead => false;

    /// <summary>The tablet has no read/edit split — a wet tablet always renders the EDITOR rows — so link
    /// activation must live directly on those rows. Opt the editor path into the read view's
    /// click-to-open-Handbook affordance on Link/Tracker/Craft name labels (enable-tablet-row-links). Every other
    /// surface leaves this false and activates links in its distinct read view instead.</summary>
    protected override bool EditorRowsOpenLinks => true;

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
        // Row grip/arrow glyphs borrow the SAME darker ink the title-bar grip + pencil use, so the drag
        // handle reads as firmly engraved on the clay instead of the washed-out global mid-gray
        // (replace-drag-wash-with-grip-arrows follow-up). TitleChromeGlyphColor is OnSurface @ 0.8 on the
        // Pixel-Art backdrop path and the base gray otherwise, so this tracks the backdrop toggle and applies
        // whether or not cuneiform is on (it's about clay contrast, not the font).
        var chromeColor = TitleChromeGlyphColor(ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme);
        style = style with { GripGlyphColor = chromeColor };

        // A row's tappable Link/Tracker/Craft name resolves its color as `style.LinkColor ?? colors.Primary`.
        // On the clay backdrop, `Primary` is a mid-value FILL accent that fails AA as small text on the
        // same-value clay (2.3–3.7 : 1, tablet-text-visibility) — so decouple it onto a dedicated per-material
        // link ink that clears 4.5 : 1. Gated on Pixel-Art Display exactly like the chalkboard's link ink and
        // this dialog's glow/theme: with Pixel-Art OFF the tablet is a flat themed panel where `Primary` is the
        // correct, legible link color, so leave `LinkColor` unset there. Applies whether or not cuneiform is on
        // (the readability problem is the clay ground, not the font).
        if (modSystem.MySettings.PixelArtDisplay)
        {
            style = style with { LinkColor = ScribeTheme.ForTabletLink(_material) };
        }

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
            // Whole-character tilt stacked on top of the per-endpoint wobble (tune-tablet-jitter-add-rotation):
            // each glyph leans a few degrees so the rows read as hand-pressed, not mechanically upright.
            CuneiformRotation = CuneiformMetrics.DefaultRotationDegrees,
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
        // Wrap the resting title to at most two lines (wrap-tablet-title-band): swap the single-line
        // CuneiformText for the display-only WRAPPING cuneiform renderer (mirrors ScribeReadContent's
        // resting cuneiform usage). The enclosing Clip is sized by the title slot, which BuildTitleBar
        // grows to two line-heights when TitleMaxLines == 2, so a title longer than two lines clips at the
        // end of line 2 (cuneiform has no '…' glyph). All look-preserving params (jitter/rotation/glow and
        // the text-derived seed) match the CuneiformText this replaces, so a one-line title is unchanged.
        return new Gui.Widgets.Painting.Clip(
            child: new ScribeCuneiformFieldRenderWidget(
                text: displayTitle,
                caret: 0,
                selectionAnchor: 0,
                hasFocus: false,
                fontSizeEm: titleStyle.FontSize,
                inkColor: colors.OnSurface,
                caretColor: Vector4.Zero,
                selectionColor: Vector4.Zero,
                bundle: bundle,
                padX: 0f,   // the title band supplies its own inset; keep the glyphs flush (as CuneiformText was)
                padY: 0f,
                boxColor: Vector4.Zero,
                borderColor: Vector4.Zero,
                borderThickness: 0f,
                cornerRadii: Vector4.Zero,
                singleLine: false,   // WRAP (was single-line, hard-clipped)
                caretVisible: false,
                jitterStrength: CuneiformMetrics.DefaultJitterStrength,
                jitterSeed: CuneiformMetrics.SeedFromString(displayTitle),
                rotationDegrees: CuneiformMetrics.DefaultRotationDegrees,
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
            // Match the rows' whole-character tilt so the editing title reads hand-pressed like everything else.
            rotationDegrees: CuneiformMetrics.DefaultRotationDegrees,
            // A fixed seed: one title band per tablet, so a constant keeps it stable across rebuilds while
            // typing (a text-derived seed would re-wobble the whole title on every keystroke).
            jitterSeed: TitleJitterSeed,
            // Stroke-by-stroke title reveal, gated by the same player setting as the rows (defaults off).
            progression: modSystem.MySettings.CuneiformProgression,
            // Per-material glow so the editing title lifts off the clay backdrop like the rows/resting title.
            glow: CuneiformGlowTable.For(_material),
            // Wrap the editing title to two lines too (wrap-tablet-title-band), so typing past one line drops to
            // a second line instead of clipping off the right; Enter still commits via OnTitleFieldKeyDown.
            singleLine: false);
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
    protected override Widget BuildCentralRegion() =>
        IsEditable ? BuildEditorContent() : BuildReadContent();

    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs _)
    {
        // Close when the player switches the active hand AWAY from THIS tablet — keyed on the document's
        // stable DocId, not merely "still some Scribe item" (same guard as the Notebook). Switching to a
        // DIFFERENT Scribe item must still close this dialog; only a hotbar reorder that keeps the same
        // item active leaves it open. See ActiveHandItemHostsThisDocument.
        if (!ActiveHandItemHostsThisDocument())
            TryClose();
    }

    private void OnHotbarSlotModified(int slotId)
    {
        // In-place content re-sync of the STILL-HELD active slot uses a presence-only check, NOT the strict
        // DocId identity guard OnActiveSlotChanged runs (fix-item-dialog-first-open-flicker) — same fix as the
        // Notebook. The first open of a not-yet-crafted tablet triggers a server re-sync WITHOUT the
        // client-generated document, whose DocId then mismatched and closed the dialog one frame after it
        // opened. The tablet's legitimate wet→hard/fired transition also rides SlotModified but carries the
        // document forward (same DocId, same IScribeDocumentItem), so it passes the presence check exactly as
        // it passed the old strict one — this change is additive for it. See ActiveHandHoldsAnyScribeDocumentItem.
        if (slotId == capi.World.Player.InventoryManager.ActiveHotbarSlotNumber
            && !ActiveHandHoldsAnyScribeDocumentItem())
            TryClose();
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
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(BuildItemSavePacket(documentBytes));
    }
}
