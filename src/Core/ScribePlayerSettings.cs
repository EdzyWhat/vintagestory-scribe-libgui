namespace Scribe.Core;

/// <summary>
/// A player's Scribe display/behavior preferences. These are per-player, CLIENT-LOCAL preferences —
/// persisted as JSON via the mod's client config (<c>ScribeModSystem.HudConfigFileName</c>), identical
/// across all of that player's worlds, and never server-synced. Kept deliberately small; new
/// preferences append as new properties (the JSON serializer defaults absent keys, so adding one never
/// breaks an existing file).
///
/// Defaults are what a player who has never changed a setting gets: see the property initializers.
/// Game-agnostic (pure BCL); the Mod layer owns persistence and any UI. The completion policy also
/// travels to the server in the completion request, where it is normalized the same way.
///
/// The bool preferences below carry no data beyond their own on/off state, so <see cref="Normalized"/>
/// leaves them untouched; only the numeric/enum preferences are clamped or range-checked on load.
/// </summary>
public sealed class ScribePlayerSettings
{
    /// <summary>What completing a pinned task does to the task and the player's pin for it:
    /// <see cref="ScribeCompletionPolicy.Sink"/> (default — keep it pinned, the HUD de-prioritizes
    /// it), <see cref="ScribeCompletionPolicy.Unpin"/> (remove the pin), or
    /// <see cref="ScribeCompletionPolicy.Delete"/> (delete the underlying task). The client sends this
    /// with each completion request and the server applies it (the server owns the completion and any
    /// removal). Replaces the earlier boolean <c>CompleteUnpins</c>.</summary>
    public ScribeCompletionPolicy CompletionPolicy { get; set; } = ScribeCompletionPolicy.Sink;

    /// <summary>The pinned-task HUD: whether the player has collapsed or hidden it. Persisted and
    /// synced so the collapsed state is restored across sessions; toggled by the HUD's rebindable
    /// show/hide hotkey.</summary>
    public bool HudCollapsed { get; set; }

    /// <summary>Whether to silence Scribe's OWN LibGUI UI click sounds (the interaction sounds LibGUI's
    /// <c>Button</c> plays on tap — the Lectern action buttons and numeric +/- steppers). Default
    /// <c>false</c> (sounds on). When on, the Mod layer swaps a no-op sound player onto each Scribe
    /// dialog's <c>BuildOwner</c>, so only Scribe's dialogs go silent — vanilla and other-mod audio are
    /// untouched (scribe-mute-ui-sounds).</summary>
    public bool MuteUiSounds { get; set; }

    /// <summary>Whether a fired Clockmaker's Notebook timer automatically disappears from the pinned-task
    /// HUD after <see cref="TimerStore.FiredAutoClearSeconds"/> (~30 s). Default <c>true</c> (the timer
    /// disappears), preserving the original behavior. When <c>false</c>, a fired timer stays on the HUD
    /// until the player dismisses it — by clicking the fired HUD timer row or pressing Stop Timer in the
    /// Clockmaker's Notebook. Because this is client-local, the auto-disappear is driven by the player's
    /// own client (which alone knows the preference) rather than the server tick
    /// (timer-auto-disappear-setting).</summary>
    public bool TimerAutoDisappear { get; set; } = true;

    /// <summary>Whether the Lectern dialog's views (read, editor, and later the pinned view) render in the
    /// mod's net-new "pixel-art" look — the light parchment theme (dark ink on light paper) plus, in a
    /// later phase, illustrated backgrounds. Default <c>true</c> (on). When off, those views fall back to
    /// the player's global LibGUI theme (the stock dark default unless the player set their own),
    /// depending on no art. This governs ONLY the Lectern dialog: the pinned-task HUD and the standalone
    /// settings window are deliberately NOT toggled — they always follow the player's global theme.</summary>
    public bool PixelArtDisplay { get; set; } = true;

    /// <summary>Whether the pinned-task HUD corrupts its own text (and swaps its title to "Survive the
    /// Storm") while a temporal-instability trigger is active — an active temporal storm or personal
    /// stability below 50% (hud-temporal-storm-corruption). Default <c>true</c> (the effect is on). When
    /// <c>false</c>, the HUD never corrupts its text or swaps its title regardless of storm/stability
    /// state, for players who rely on HUD legibility or are motion-sensitive.</summary>
    public bool StormCorruption { get; set; } = true;

    /// <summary>Whether to DISABLE the cuneiform pseudo-font used by the tablet tier and render that text
    /// in the player's selected task font instead (cuneiform-glyph-font). Default <c>false</c> (cuneiform
    /// on — the distinctive carved-wedge script). A per-player, client-local accessibility/legibility
    /// fallback: never server-synced. When <c>true</c>, the single <c>UseCuneiform</c> branch point resolves
    /// to normal text through the existing <see cref="TaskFontFamily"/> chokepoint. A plain bool, so
    /// <see cref="Normalized"/> leaves it untouched.</summary>
    public bool DisableCuneiformFont { get; set; }

    /// <summary>The timer type the Clockmaker's Notebook's "set timer" form pre-selects: the last type the
    /// player chose, remembered across close/reopen. Default <see cref="TimerMode.RealTime"/> — the first
    /// option in the selector, so a player opening the Timer tab for the very first time starts on Real time
    /// (fix-clockmaker-timer-mode-default). Only seeds the Idle form; a running timer always shows its own
    /// stored mode. A per-player, client-local preference: never server-synced. An unknown value falls back
    /// to the default on load (<see cref="NormalizeTimerMode"/>).</summary>
    public TimerMode PreferredTimerMode { get; set; } = TimerMode.RealTime;

    /// <summary>Maximum number of pinned tasks the HUD shows at once (default 3); pins beyond this
    /// are summarized ("+N more"). A per-player display preference; the Mod layer owns the HUD and
    /// clamps this to a sane range on read (see <see cref="ScribePinCodec"/>).</summary>
    public int HudMaxRows { get; set; } = DefaultHudMaxRows;

    /// <summary>Default <see cref="HudMaxRows"/> for a player who has never changed it.</summary>
    public const int DefaultHudMaxRows = 3;

    /// <summary>Inclusive lower bound the codec clamps <see cref="HudMaxRows"/> to on read.</summary>
    public const int MinHudMaxRows = 1;

    /// <summary>Inclusive upper bound clamped on load, so a hand-edited or garbled preference file
    /// can't request an unbounded number of rows. A saved 11–20 re-clamps to 10 on next load (§10.3).</summary>
    public const int MaxHudMaxRows = 10;

    /// <summary>Which screen corner/edge the HUD is pinned to (default <see cref="ScribeHudAnchor.TopRight"/>,
    /// pre-offset left of the minimap by the Mod layer). A per-player display preference; the Mod layer
    /// maps it to a screen position. An unknown value falls back to the default on load.</summary>
    public ScribeHudAnchor HudAnchor { get; set; } = ScribeHudAnchor.TopRight;

    /// <summary>Horizontal pixel nudge applied to the HUD from its <see cref="HudAnchor"/>, so it can be
    /// moved clear of another on-screen overlay (minimap / coordinate / block-info). Positive moves the
    /// HUD toward screen-center from a right anchor and rightward from a left/middle anchor; the Mod
    /// layer owns the exact sign convention per anchor. Defaults to a Mod-supplied value (0 here; the
    /// Mod layer's default top-right resolver applies the minimap clearance). Clamped to
    /// <see cref="MinHudOffset"/>..<see cref="MaxHudOffset"/> on load.</summary>
    public int HudOffsetX { get; set; }

    /// <summary>Vertical pixel nudge applied to the HUD from its <see cref="HudAnchor"/> (see
    /// <see cref="HudOffsetX"/>). Defaults to 0. Clamped like <see cref="HudOffsetX"/>.</summary>
    public int HudOffsetY { get; set; }

    /// <summary>Inclusive lower bound the codec clamps <see cref="HudOffsetX"/>/<see cref="HudOffsetY"/>
    /// to on read, so a hand-edited nudge can't fling the HUD far off its anchor. The offset is applied
    /// RELATIVE to the anchor's built-in pre-baked offset (the Mod layer's <c>ApplyAnchor</c>), so this
    /// bounds how far the player can nudge from that sensible default in either direction.</summary>
    public const int MinHudOffset = -300;

    /// <summary>Inclusive upper bound clamped on load (see <see cref="MinHudOffset"/>).</summary>
    public const int MaxHudOffset = 300;

    /// <summary>Fixed pixel width of the HUD's task-row area (default 250); a long task wraps within
    /// this width instead of the HUD growing arbitrarily wide. Clamped to a sane range on load.</summary>
    public int HudRowWidth { get; set; } = DefaultHudRowWidth;

    /// <summary>Default <see cref="HudRowWidth"/> for a player who has never changed it.</summary>
    public const int DefaultHudRowWidth = 250;

    /// <summary>Inclusive lower bound clamped on load, so a hand-edited value can't collapse the HUD to
    /// an unusably narrow (or non-positive) width.</summary>
    public const int MinHudRowWidth = 80;

    /// <summary>Inclusive upper bound clamped on load, so a hand-edited value can't stretch the HUD
    /// across the whole screen.</summary>
    public const int MaxHudRowWidth = 1000;

    /// <summary>The Lectern layout's single driving width `W` in pixels (the "Pixel Art Size"), default
    /// <c>600</c>. The whole dialog is an art-sized outer box of <c>W × (W·1160/1024)</c> and every inner
    /// structure's size derives from <c>W</c>, so this one number scales the entire proportional layout
    /// (scribe-notebook-frame). A per-player display preference; the Mod layer reads it fresh each build so a
    /// change re-lays-out the open Lectern live. Snapped to a 10px grid and clamped on load
    /// (<see cref="ClampPixelArtSize"/>), mirroring <see cref="HudRowWidth"/>.</summary>
    public int PixelArtSize { get; set; } = DefaultPixelArtSize;

    /// <summary>Default <see cref="PixelArtSize"/> for a player who has never changed it.</summary>
    public const int DefaultPixelArtSize = 600;

    /// <summary>Inclusive lower bound clamped on load, so a hand-edited value can't shrink the Lectern
    /// (and its center tasks column) below a usable size.</summary>
    public const int MinPixelArtSize = 300;

    /// <summary>Inclusive upper bound clamped on load, so a hand-edited value can't blow the Lectern up
    /// past the screen.</summary>
    public const int MaxPixelArtSize = 1000;

    /// <summary>Text-size multiplier for the pinned-task HUD's row text (default <c>1.0</c> = no
    /// change). Snapped to a discrete 5% notch within its range (<c>0.80, 0.85, … , 1.20</c>), entered
    /// in the UI as a percent. A MULTIPLIER (not an absolute point size) so it stacks on top of the
    /// game's global Interface → GUI Scale rather than fighting it. The Mod layer multiplies its base
    /// HUD font size by this. Clamped and snapped to the nearest notch on load.</summary>
    public float HudFontScale { get; set; } = DefaultFontScale;

    /// <summary>Text-size multiplier for the block/item window text (the Lectern now; Desk/Notebook
    /// later), default <c>1.0</c>. Same 5% notch granularity and multiplier semantics as
    /// <see cref="HudFontScale"/>. Supersedes the retired <c>ScribeClientConfig.TextSizeScale</c>; the
    /// Mod layer multiplies its base window font size by this at the single <c>ScribeRowStyle</c>
    /// chokepoint. Clamped and snapped on load.</summary>
    public float WindowFontScale { get; set; } = DefaultFontScale;

    /// <summary>Font family for the Lectern's task/note ROW text (v1-release-checklist §6). Empty string
    /// (the default) means "use the built-in body font" — the existing sans-serif look, so a player who
    /// never touches the selector is unchanged. A non-empty value must be one of <see cref="KnownTaskFonts"/>
    /// (the bundled/registered families); an unrecognized value falls back to the default on load
    /// (<see cref="NormalizeTaskFontFamily"/>). This governs ONLY task/note text — the in-Lectern buttons
    /// use a fixed face chosen by the Mod layer, not this preference. A plain display string carried by no
    /// document/pin data, so it needs no codec version bump.</summary>
    public string TaskFontFamily { get; set; } = DefaultTaskFontFamily;

    /// <summary>The default task-font value: empty string = the built-in body font (no override).</summary>
    public const string DefaultTaskFontFamily = "";

    /// <summary>The task-font families the selector offers, by exact registered family name. The empty
    /// string (<see cref="DefaultTaskFontFamily"/>) is the implicit first choice (built-in body font) and
    /// is always valid; these are the non-default options. "Playfair Display" and "Cormorant Unicase" are
    /// registered by the LibGUI (<c>gui</c>) dependency; the rest are bundled by Scribe. Kept in Core as
    /// plain strings (no game/font API reference) so <see cref="NormalizeTaskFontFamily"/> stays
    /// unit-testable; the Mod layer owns actually registering and rendering them.</summary>
    public static readonly string[] KnownTaskFonts =
    {
        "Scapholene",
        "Caudex",
        "La Belle Aurore",
        "Noto Sans",
        "Noto Serif",
        "Playfair Display",
        "Cormorant Unicase",
    };

    /// <summary>Maps a loaded task-font value to a valid one: the empty-string default, or an exact match
    /// in <see cref="KnownTaskFonts"/>. Any other value (a hand-edited typo, or a font removed in a later
    /// version) falls back to <see cref="DefaultTaskFontFamily"/> so the row text always resolves to a real
    /// registered family. Null is treated as the default.</summary>
    public static string NormalizeTaskFontFamily(string? value)
    {
        if (string.IsNullOrEmpty(value)) return DefaultTaskFontFamily;
        return Array.IndexOf(KnownTaskFonts, value) >= 0 ? value : DefaultTaskFontFamily;
    }

    /// <summary>Default font-scale multiplier (no scaling) for a player who has never changed it.</summary>
    public const float DefaultFontScale = 1.0f;

    /// <summary>Inclusive lower bound (-20%) the codec clamps both font scales to on read.</summary>
    public const float MinFontScale = 0.8f;

    /// <summary>Inclusive upper bound (+20%) clamped on load.</summary>
    public const float MaxFontScale = 1.2f;

    /// <summary>Clamps a loaded HUD row count to the safe range. Applied when reading the client
    /// preference config so a hand-edited or corrupted value can't produce an out-of-range state.</summary>
    public static int ClampHudMaxRows(int value) => Math.Clamp(value, MinHudMaxRows, MaxHudMaxRows);

    /// <summary>Clamps a loaded HUD offset (X or Y) to the safe range (see <see cref="MinHudOffset"/>).</summary>
    public static int ClampHudOffset(int value) => Math.Clamp(value, MinHudOffset, MaxHudOffset);

    /// <summary>Clamps a loaded font-scale multiplier to <see cref="MinFontScale"/>..<see cref="MaxFontScale"/>
    /// AND snaps it to the nearest 0.05 notch, so a hand-edited value settles onto one of the defined 5%
    /// notches (<c>0.80, 0.85, … , 1.20</c>) rather than an arbitrary in-between scale.</summary>
    public static float ClampFontScale(float value)
    {
        float clamped = Math.Clamp(value, MinFontScale, MaxFontScale);
        return MathF.Round(clamped * 20f) / 20f;
    }

    /// <summary>Clamps a loaded HUD row width to the safe range (see <see cref="HudRowWidth"/>).</summary>
    public static int ClampHudRowWidth(int value) => Math.Clamp(value, MinHudRowWidth, MaxHudRowWidth);

    /// <summary>Clamps a loaded Pixel Art Size to <see cref="MinPixelArtSize"/>..<see cref="MaxPixelArtSize"/>
    /// AND snaps it to the nearest 10px, so a hand-edited value settles onto the 10-step grid the UI uses
    /// rather than an arbitrary width.</summary>
    public static int ClampPixelArtSize(int value)
    {
        int clamped = Math.Clamp(value, MinPixelArtSize, MaxPixelArtSize);
        return (int)Math.Round(clamped / 10.0) * 10;
    }

    /// <summary>Maps a loaded HUD anchor value to a defined <see cref="ScribeHudAnchor"/>, falling back
    /// to the default (<see cref="ScribeHudAnchor.TopRight"/>) for any unrecognized value so a
    /// hand-edited or corrupted config can't select an undefined anchor.</summary>
    public static ScribeHudAnchor NormalizeAnchor(ScribeHudAnchor value) =>
        Enum.IsDefined(typeof(ScribeHudAnchor), value) ? value : ScribeHudAnchor.TopRight;

    /// <summary>Maps a loaded completion-policy value to a defined <see cref="ScribeCompletionPolicy"/>,
    /// falling back to the default (<see cref="ScribeCompletionPolicy.Sink"/>) for any unrecognized
    /// value so a hand-edited or corrupted config can't select an undefined behavior. The client also
    /// carries its policy in the completion request, where the server normalizes it the same way.</summary>
    public static ScribeCompletionPolicy NormalizePolicy(ScribeCompletionPolicy value) =>
        Enum.IsDefined(typeof(ScribeCompletionPolicy), value) ? value : ScribeCompletionPolicy.Sink;

    /// <summary>Maps a loaded timer-mode value to a defined <see cref="TimerMode"/>, falling back to the
    /// default (<see cref="TimerMode.RealTime"/>) for any unrecognized value so a hand-edited or corrupted
    /// config can't select an undefined timer type.</summary>
    public static TimerMode NormalizeTimerMode(TimerMode value) =>
        Enum.IsDefined(typeof(TimerMode), value) ? value : TimerMode.RealTime;

    /// <summary>Normalizes this instance's fields in place after a load from an untrusted source
    /// (hand-edited JSON): clamps each numeric preference to its safe range (row count, row width, the
    /// Pixel Art Size — snapped to the 10px grid, the HUD offsets, and both font scales — snapping each
    /// scale to its nearest notch) and falls an unknown <see cref="CompletionPolicy"/>/<see cref="HudAnchor"/>
    /// back to its default. Returns this for chaining.</summary>
    public ScribePlayerSettings Normalized()
    {
        HudMaxRows = ClampHudMaxRows(HudMaxRows);
        CompletionPolicy = NormalizePolicy(CompletionPolicy);
        PreferredTimerMode = NormalizeTimerMode(PreferredTimerMode);
        HudAnchor = NormalizeAnchor(HudAnchor);
        HudRowWidth = ClampHudRowWidth(HudRowWidth);
        PixelArtSize = ClampPixelArtSize(PixelArtSize);
        HudOffsetX = ClampHudOffset(HudOffsetX);
        HudOffsetY = ClampHudOffset(HudOffsetY);
        HudFontScale = ClampFontScale(HudFontScale);
        WindowFontScale = ClampFontScale(WindowFontScale);
        TaskFontFamily = NormalizeTaskFontFamily(TaskFontFamily);
        return this;
    }
}
