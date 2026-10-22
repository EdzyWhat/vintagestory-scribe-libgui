using Gui.Rendering;             // WorldAnchor (Anchor opt-out)
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
/// The dialog for the clay/wax tablet item — the scratch tier's always-edit writing surface.
/// A thin subclass of <see cref="ScribeDialogBase"/>; differs from the Notebook in that it has:
/// <list type="bullet">
/// <item><b>No tabs.</b> <see cref="BuildRightColNav"/> returns an empty column that still occupies
/// <c>SideColW</c>, keeping the center content's symmetric margins.</item>
/// <item><b>Always edit</b> (wet tablets). Editor mode is entered in the constructor, before the first
/// <c>Build()</c>.</item>
/// <item><b>Live cuneiform input.</b> Under <see cref="ScribeTaskFont.UseCuneiform"/>, task rows type in
/// the cuneiform pseudo-font via <see cref="DecorateRowStyle"/>; disabling it reverts to the normal field.</item>
/// <item><b>Earthen theme + material backdrop</b> (<see cref="ResolveTheme"/>).</item>
/// </list>
///
/// <para>Like the Notebook it is item-hosted: editor access is immediate, lock-release is a no-op, saves
/// use <see cref="ScribeNotebookSaveMessage"/>, and there is no proximity auto-close. It closes when the
/// held item stops being a Scribe document.</para>
/// </summary>
public class GuiDialogScribeTablet : ScribeDialogBase
{
    private IInventory? _hotbar;

    /// <summary>The style resolved from the item's base material (<c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c>/<c>wax</c>)
    /// and <see cref="_state"/>: palette, ink, glow and Quest Link accent.</summary>
    private readonly TabletStyle _style;

    /// <summary>The drying state (wet/hard/fired). Only a wet tablet is editable; hard and fired open read-only.</summary>
    private readonly TabletState _state;

    /// <summary>Only a wet tablet is editable (hard until rehydrated, fired permanently read-only);
    /// every read-only branch keys off this.</summary>
    private bool IsEditable => _state == TabletState.Wet;

    public GuiDialogScribeTablet(IScribeDocumentHost host, ICoreClientAPI capi, string? material = null,
        TabletState state = TabletState.Wet)
        : base(new BlockPos(0), host, capi)
    {
        _state = state;
        _style = TabletStyle.For(material, state);

        // Seed the editor scratch before TryOpen calls Build() (it inflates the tree before OnGuiOpened).
        // A hard/fired tablet stays in the base's Read view (scratch null).
        if (IsEditable)
        {
            EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
        }

        capi.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
        _hotbar = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (_hotbar != null)
            _hotbar.SlotModified += OnHotbarSlotModified;
    }

    /// <summary>Disables the engine's range check; a carried item has no block position.</summary>
    protected override double InteractionRange => double.MaxValue;

    /// <summary>Opts out of LibGUI's "floaty" world-anchored positioning: the dummy <c>BlockPos(0)</c>
    /// would pin the window to the world origin and drift with the camera. Null = screen-space placement.</summary>
    protected override WorldAnchor? Anchor => null;

    /// <summary>The centered empty-document message: a "write here" hint when wet, otherwise a
    /// state-specific line (dried: "dunk in water to edit"; fired: "fired without writing").</summary>
    protected override string EmptyHintLangKey => _state switch
    {
        TabletState.Fired => "scribe:tablet-fired-empty",
        TabletState.Hard  => "scribe:tablet-hard-empty",
        _                                 => "scribe:scribe-gui-edit-hint-tablet",
    };

    /// <summary>A hard or fired tablet's read view drops the "switch to editor" button and blocks text
    /// editing, but not checkbox/pin (see <see cref="ReadViewCompletionAndPinLive"/>).</summary>
    private protected override bool ReadViewIsReadOnly => !IsEditable;

    /// <summary>The Tablet's read view omits the filter-pill row and subtask-collapse toggles.</summary>
    private protected override bool SupportsFilterPills => false;

    /// <summary>The Tablet has no Row 2 subtitle or Row 3 header; its title bar is the whole header.</summary>
    private protected override bool SupportsTabHeader => false;

    /// <summary>Keeps checkbox and pin live on a read-only tablet so firing one with a pinned task never
    /// strands the pin on the HUD; only text editing is blocked.</summary>
    private protected override bool ReadViewCompletionAndPinLive => !IsEditable;

    /// <summary>On a read-only tablet, tapping row text shows the locked message. Null when wet.</summary>
    private protected override Action<Guid>? ReadViewTextEditRefused => IsEditable
        ? null
        : _ => capi.TriggerIngameError(this, "scribe-tablet-locked", Lang.Get(ReadOnlyLockedLangKey));

    /// <summary>The "tablet is locked" lang key for the current read-only state, shared by the row-text
    /// refusal and the Handbook append notice.</summary>
    private string ReadOnlyLockedLangKey => _state == TabletState.Fired
        ? "scribe:tablet-fired-locked"
        : "scribe:tablet-hard-locked";

    /// <summary>A set tablet can never reach the editor, so a Handbook "Add to Scribe" click is reported
    /// rather than dropped.</summary>
    protected override bool CanEditFromHandbook => IsEditable;

    /// <summary>Read-only notice for a Handbook append onto a set tablet.</summary>
    protected override void NotifyHandbookAppendReadOnly()
        => capi.TriggerIngameError(this, "scribe-tablet-locked", Lang.Get(ReadOnlyLockedLangKey));

    /// <summary>A wet tablet grants editor access immediately, seeding the scratch from the host's
    /// document. A hard or fired tablet is a no-op so it can never become editable.</summary>
    protected override void RequestEditorAccess()
    {
        if (!IsEditable) return;
        EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
    }

    /// <summary>No editor lock on an item — nothing to release.</summary>
    protected override void SendReleaseLockPacket() { }

    /// <summary>The tablet is always-edit with no Read view, so the editor footer omits the "Done editing"
    /// button — tapping it would leave editor mode and null the scratch the central region reads.</summary>
    protected override bool ShowEditorSwitchToRead => false;

    /// <summary>A wet tablet always renders editor rows, so link activation (click-to-open-Handbook on
    /// Link/Tracker/Craft labels) must live on them. Other surfaces activate links in their read view.</summary>
    protected override bool EditorRowsOpenLinks => true;

    /// <summary>The <see cref="TabletReadability"/> bundle for this material + state: the single source for
    /// theme ink, link ink, cuneiform glow, and stroke-weight scale.</summary>
    private TabletReadability Readability => _style.Readability;

    /// <summary>Per-clay palette keyed to the style's material, agreeing with the backdrop. Pixel-Art
    /// off falls back to the player's global theme.</summary>
    protected override ThemeData ResolveTheme(bool pixelArt) => ScribeTheme.ForTablet(_style, pixelArt);

    /// <summary>Tints the title-bar pencil + drag-grip with the tablet's dark ink at partial alpha so they
    /// read as engraved. The base applies it via <see cref="SKBlendMode.SrcIn"/>, which fades only the
    /// strokes; a Multiply blend would fill the whole transparent icon quad (VsIcon tints as a color
    /// filter), producing a pale tile. Pixel-Art off defers to the base gray.</summary>
    private protected override Vector4 TitleChromeGlyphColor(ColorScheme colors)
    {
        if (!modSystem.MySettings.PixelArtDisplay)
        {
            return base.TitleChromeGlyphColor(colors);
        }
        return colors.OnSurface with { W = 0.8f };
    }

    /// <summary>Flips editable rows to live cuneiform under <see cref="ScribeTaskFont.UseCuneiform"/>;
    /// otherwise they use the normal field. A null glyph bundle (asset not loaded) leaves the style unchanged.</summary>
    private protected override ScribeRowStyle DecorateRowStyle(ScribeRowStyle style)
    {
        // Row grip/arrow glyphs use the title chrome's darker ink for contrast on clay, cuneiform or not.
        var chromeColor = TitleChromeGlyphColor(ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme);
        style = style with { GripGlyphColor = chromeColor };

        // Link names resolve `style.LinkColor ?? colors.Primary`. On clay, `Primary` fails AA as small
        // text, so use a per-material link ink (4.5:1). With Pixel-Art OFF the panel is flat and `Primary`
        // is legible, so LinkColor stays unset.
        if (modSystem.MySettings.PixelArtDisplay)
        {
            style = style with
            {
                LinkColor = Readability.LinkInk,
                QuestLinkColor = _style.QuestLinkAccent,
            };
        }

        if (ActiveCuneiformBundle is not { } bundle)
        {
            return style;
        }

        return style with
        {
            UseCuneiform = true,
            CuneiformBundle = bundle,
            // Hand-written wobble; the per-row seed (TaskId) is supplied at the field. 0 = crisp geometry.
            CuneiformJitter = CuneiformMetrics.DefaultJitterStrength,
            // Whole-glyph tilt on top of the per-endpoint wobble.
            CuneiformRotation = CuneiformMetrics.DefaultRotationDegrees,
            // Stroke-by-stroke reveal of new text, behind a player opt-in (re-derived per build).
            CuneiformProgression = modSystem.MySettings.CuneiformProgression,
            // Outer glow lifting the ink off the backdrop: dark halo when wet, light when hard/fired.
            CuneiformGlow = Readability.Glow,
            // Firms the ink up as the tablet dries (wet 0.9 → fired 1.1); non-tablet surfaces keep base weight.
            CuneiformStrokeWeightScale = Readability.StrokeWeightScale,
            // Cuneiform's line box (FontSize×LineHeightRatio, ~27.7px) is taller than the 22px Latin
            // controls, so size the checkbox and ControlSize (stepper, grip, pin, delete) to one line.
            CheckboxSize = style.FontSize * CuneiformMetrics.LineHeightRatio,
            ControlSize = style.FontSize * CuneiformMetrics.LineHeightRatio,
        };
    }

    /// <summary>Resting title: display-only wrapping cuneiform, clipped to two lines. Falls back to the
    /// base RichText when cuneiform is disabled or the glyph bundle isn't loaded.</summary>
    private protected override Widget BuildTitleDisplay(string displayTitle, TextStyle titleStyle)
    {
        var bundle = ActiveCuneiformBundle;
        if (bundle is null)
        {
            return base.BuildTitleDisplay(displayTitle, titleStyle);
        }

        var colors = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        // The Clip is sized by the title slot (two line-heights when TitleMaxLines == 2); overflow clips
        // at the end of line 2 since cuneiform has no '…' glyph.
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
                padX: 0f,   // the title band supplies its own inset
                padY: 0f,
                boxColor: Vector4.Zero,
                borderColor: Vector4.Zero,
                borderThickness: 0f,
                cornerRadii: Vector4.Zero,
                singleLine: false,
                caretVisible: false,
                jitterStrength: CuneiformMetrics.DefaultJitterStrength,
                jitterSeed: CuneiformMetrics.SeedFromString(displayTitle),
                rotationDegrees: CuneiformMetrics.DefaultRotationDegrees,
                glow: Readability.Glow,
                strokeWeightScale: Readability.StrokeWeightScale,
                glyphDrawScale: CuneiformMetrics.GlyphDrawScale,
                caretHeightScale: CuneiformMetrics.CaretHeightScale));
    }

    /// <summary>Editing title: a live cuneiform input bound to the base's title controller/focus node, so
    /// its commit/blur machinery is unchanged. Falls back to the stock TextField when cuneiform is disabled
    /// or the bundle isn't loaded. <see cref="OnTitleFieldKeyDown"/> enforces maxlength and Enter/Escape commit.</summary>
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
            rotationDegrees: CuneiformMetrics.DefaultRotationDegrees,
            // Fixed seed: a text-derived one would re-wobble the whole title on every keystroke.
            jitterSeed: TitleJitterSeed,
            progression: modSystem.MySettings.CuneiformProgression,
            glow: Readability.Glow,
            strokeWeightScale: Readability.StrokeWeightScale,
            // Wraps to a second line instead of clipping; Enter still commits.
            singleLine: false);
    }

    /// <summary>Fixed jitter seed for the title band, distinct from the rows' TaskId seeds.</summary>
    private const int TitleJitterSeed = 0x5C71B7;

    /// <summary>The glyph bundle when cuneiform is active, else null (disabled or not loaded); the single
    /// resolution point for the title, rows, and labels.</summary>
    private GlyphBundle? ActiveCuneiformBundle =>
        ScribeTaskFont.UseCuneiform(modSystem.MySettings.CuneiformTablets)
            ? modSystem.GetCuneiformBundle()
            : null;

    /// <summary>With no nav column, the editor footer gets a Settings gear right of the ⓘ button
    /// (the base returns null).</summary>
    private protected override Action? EditorSettingsGearAction => modSystem.OpenSettings;

    /// <summary>Empty column; the enclosing <c>SizedBox</c> in <c>BuildSectionInnerBox</c> still gives it
    /// <c>SideColW</c> width.</summary>
    protected override Widget BuildRightColNav() => new SizedBox();

    /// <summary>The inherited editor (wet) or read view; there is no separate title banner.</summary>
    protected override Widget BuildCentralRegion() =>
        IsEditable ? BuildEditorContent() : BuildReadContent();

    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs _)
    {
        // Keyed on the document's DocId: switching to a different Scribe item must close this dialog.
        if (!ActiveHandItemHostsThisDocument())
            TryClose();
    }

    private void OnHotbarSlotModified(int slotId)
    {
        // A re-sync of the still-held slot needs a presence-only check, not the DocId guard: a first open
        // of a not-yet-crafted tablet re-syncs without the client-generated document (DocId mismatch). The
        // wet→hard/fired transition keeps the same DocId and passes either way.
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

    /// <summary>Saves reuse <see cref="ScribeNotebookSaveMessage"/>, writing into the held ItemStack.</summary>
    protected override void SendFlushPacket(byte[] documentBytes)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(BuildItemSavePacket(documentBytes));
    }
}
