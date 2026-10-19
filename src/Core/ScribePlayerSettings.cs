namespace Scribe.Core;

/// <summary>
/// A player's Scribe display/behavior preferences: CLIENT-LOCAL, persisted as JSON via the mod's client
/// config (<c>ScribeModSystem.HudConfigFileName</c>), identical across worlds, never server-synced. New
/// preferences append as properties (absent JSON keys take the initializer default). Game-agnostic; the
/// Mod layer owns persistence and UI. Bool preferences carry no data beyond on/off, so
/// <see cref="Normalized"/> leaves them untouched; numeric/enum preferences are clamped on load.
/// </summary>
public sealed class ScribePlayerSettings
{
    /// <summary>What completing a pinned task does: <see cref="ScribeCompletionPolicy.Sink"/> (default — keep
    /// it pinned, de-prioritized), <see cref="ScribeCompletionPolicy.Unpin"/>, or
    /// <see cref="ScribeCompletionPolicy.Delete"/> (delete the task). Sent with each completion request;
    /// the server applies it.</summary>
    public ScribeCompletionPolicy CompletionPolicy { get; set; } = ScribeCompletionPolicy.Sink;

    /// <summary>Policy for automatically linking quests detected by the optional VS Quest integration.</summary>
    public ScribeQuestAcceptPolicy QuestAcceptPolicy { get; set; } = ScribeQuestAcceptPolicy.PromptHud;

    /// <summary>Policy for automatically completing tasks linked to quests detected by the optional VS Quest integration.</summary>
    public ScribeQuestCompletionPolicy QuestCompletionPolicy { get; set; } = ScribeQuestCompletionPolicy.Prompt;

    /// <summary>Whether accepting a quest prompt also pins the resulting linked task (same operation as a
    /// manual pin). Sent with the accept request, like <see cref="CompletionPolicy"/>.</summary>
    public bool AutoPinOnQuestAccept { get; set; } = true;

    /// <summary>What a Tracker task does when its count reaches its target: <see cref="ScribeTrackerCompletion.Complete"/>
    /// (default — same edit as ticking it), <see cref="ScribeTrackerCompletion.Delete"/>, or
    /// <see cref="ScribeTrackerCompletion.Nothing"/>. Read by the client count engine; distinct from
    /// <see cref="CompletionPolicy"/>, which is about pins.</summary>
    public ScribeTrackerCompletion TrackerCompletion { get; set; } = ScribeTrackerCompletion.Complete;

    /// <summary>What completing or trashing a parent (depth-0 plus its contiguous depth-1 run) does to its
    /// children: <see cref="ScribeSubtaskBehavior.Bound"/> (default — the run moves with the parent),
    /// <see cref="ScribeSubtaskBehavior.Independent"/> (parent only), or
    /// <see cref="ScribeSubtaskBehavior.DiscardChildren"/> (drop children, then apply the completion policy
    /// to the parent alone). The server normalizes unknown values to Bound.</summary>
    public ScribeSubtaskBehavior SubtaskBehavior { get; set; } = ScribeSubtaskBehavior.Bound;

    /// <summary>Where footer Add, Shift+right-click quick-add, and Handbook Add to Scribe insert a new
    /// block: <see cref="ScribeNewTaskInsert.Top"/> (default, index 0) or
    /// <see cref="ScribeNewTaskInsert.Bottom"/>. Enter insert-below does not read this.</summary>
    public ScribeNewTaskInsert NewTaskInsert { get; set; } = ScribeNewTaskInsert.Top;

    /// <summary>Where a newly pinned task with no pinned parent lands: <see cref="ScribePinInsert.Top"/> or
    /// <see cref="ScribePinInsert.Bottom"/> (default, append). A subtask attaching under its pinned
    /// parent ignores this setting.</summary>
    public ScribePinInsert PinInsert { get; set; } = ScribePinInsert.Bottom;

    /// <summary>Whether the pinned-task HUD header shows its settings gear (the Settings tab stays available
    /// either way).</summary>
    public bool HudShowSettingsGear { get; set; } = true;

    /// <summary>Whether the player has collapsed the pinned-task HUD; persisted so it is restored across
    /// sessions.</summary>
    public bool HudCollapsed { get; set; }

    /// <summary>Whether HUD Tracker/Link rows render their icon; when <c>false</c> they show text/counter only.</summary>
    public bool HudShowIcons { get; set; } = true;

    /// <summary>Whether to silence Scribe's OWN LibGUI button click sounds. The Mod layer swaps a no-op
    /// sound player onto each Scribe dialog's <c>BuildOwner</c>; other audio is untouched.</summary>
    public bool MuteUiSounds { get; set; }

    /// <summary>Whether a fired Clockmaker's Notebook timer disappears from the HUD after
    /// <see cref="TimerStore.FiredAutoClearSeconds"/>. When <c>false</c> it stays until dismissed. Driven
    /// by the player's own client, which alone knows the preference.</summary>
    public bool TimerAutoDisappear { get; set; } = true;

    /// <summary>Alarm volume as an integer 0–100 (passed to the sound engine as <c>TimerAlarmVolume / 100f</c>).
    /// Clamped to <see cref="MinTimerAlarmVolume"/>..<see cref="MaxTimerAlarmVolume"/> on load.</summary>
    public int TimerAlarmVolume { get; set; } = DefaultTimerAlarmVolume;

    /// <summary>Default alarm volume.</summary>
    public const int DefaultTimerAlarmVolume = 65;

    /// <summary>Inclusive lower bound: 0 = silent.</summary>
    public const int MinTimerAlarmVolume = 0;

    /// <summary>Inclusive upper bound. The breathing envelope peaks at volume × 1.1, so values above ~91
    /// can clip to the engine's 1.0 ceiling.</summary>
    public const int MaxTimerAlarmVolume = 100;

    /// <summary>Clamps a loaded alarm volume to 0..100.</summary>
    public static int ClampTimerAlarmVolume(int v) => Math.Clamp(v, MinTimerAlarmVolume, MaxTimerAlarmVolume);

    /// <summary>Whether the Lectern dialog renders in the "pixel-art" parchment look. When off it falls back
    /// to the player's global LibGUI theme. Governs ONLY the Lectern: the HUD and settings window always
    /// follow the global theme.</summary>
    public bool PixelArtDisplay { get; set; } = true;

    /// <summary>Whether the pinned-task HUD corrupts its text (and swaps its title to "Survive the Storm")
    /// while a temporal storm is active or personal stability is below 50%.</summary>
    public bool StormCorruption { get; set; } = true;

    /// <summary>Whether the tablet tier renders text in the cuneiform pseudo-font. Positive polarity so it
    /// reads the same direction as its UI label; the legacy negative key is folded in by
    /// <see cref="MigrateLegacyKeys"/>.</summary>
    public bool CuneiformTablets { get; set; } = true;

    /// <summary>Whether newly-typed cuneiform text presses in stroke-by-stroke. Only has an effect while
    /// <see cref="CuneiformTablets"/> is also on.</summary>
    public bool CuneiformProgression { get; set; }

    /// <summary>Legacy on-disk key (<c>"DisableCuneiformFont"</c>) from before the polarity flip to
    /// <see cref="CuneiformTablets"/>. Null unless reading an old config; <see cref="MigrateLegacyKeys"/>
    /// maps and clears it, and <see cref="ShouldSerializeDisableCuneiformFont"/> keeps it from being
    /// written back. Nullable to distinguish an absent key from an explicit <c>false</c>.</summary>
    public bool? DisableCuneiformFont { get; set; }

    /// <summary>Newtonsoft.Json convention: <c>false</c> omits the legacy <see cref="DisableCuneiformFont"/> key on write.</summary>
    public bool ShouldSerializeDisableCuneiformFont() => false;

    /// <summary>The timer type the Notebook's "set timer" form pre-selects (the last one chosen). Only seeds
    /// the Idle form; an unknown value falls back to the default on load (<see cref="NormalizeTimerMode"/>).</summary>
    public TimerMode PreferredTimerMode { get; set; } = TimerMode.RealTime;

    /// <summary>UID of the player this Assigner last sent an assignment to; the target picker's default.
    /// Empty means no prior send or a target no longer listed, and the picker falls back to its first entry.
    /// Set optimistically on the client when a send succeeds.</summary>
    public string LastAssignmentTargetUid { get; set; } = "";

    /// <summary>Maximum pinned tasks the HUD shows at once; pins beyond this are summarized ("+N more").</summary>
    public int HudMaxRows { get; set; } = DefaultHudMaxRows;

    /// <summary>Default <see cref="HudMaxRows"/>.</summary>
    public const int DefaultHudMaxRows = 3;

    /// <summary>Inclusive lower bound clamped on load.</summary>
    public const int MinHudMaxRows = 1;

    /// <summary>Inclusive upper bound clamped on load, so a hand-edited file can't request unbounded rows.</summary>
    public const int MaxHudMaxRows = 30;

    /// <summary>Which screen corner/edge the HUD is pinned to. An unknown value falls back to the default on load.</summary>
    public ScribeHudAnchor HudAnchor { get; set; } = ScribeHudAnchor.TopLeft;

    /// <summary>Horizontal pixel nudge from <see cref="HudAnchor"/>, to clear other overlays (minimap etc.).
    /// Positive moves toward screen-center from a right anchor and rightward from left/middle anchors; the
    /// Mod layer owns the exact sign per anchor. Clamped to <see cref="MinHudOffset"/>..<see cref="MaxHudOffset"/>.</summary>
    public int HudOffsetX { get; set; }

    /// <summary>Vertical pixel nudge from <see cref="HudAnchor"/>; see <see cref="HudOffsetX"/>.</summary>
    public int HudOffsetY { get; set; }

    /// <summary>Inclusive lower bound for the HUD offsets. The offset is RELATIVE to the anchor's built-in
    /// offset (the Mod layer's <c>ApplyAnchor</c>).</summary>
    public const int MinHudOffset = -300;

    /// <summary>Inclusive upper bound; see <see cref="MinHudOffset"/>.</summary>
    public const int MaxHudOffset = 300;

    /// <summary>Fixed pixel width of the HUD's task-row area; long tasks wrap within it.</summary>
    public int HudRowWidth { get; set; } = DefaultHudRowWidth;

    /// <summary>Default <see cref="HudRowWidth"/>.</summary>
    public const int DefaultHudRowWidth = 250;

    /// <summary>Inclusive lower bound clamped on load.</summary>
    public const int MinHudRowWidth = 80;

    /// <summary>Inclusive upper bound clamped on load.</summary>
    public const int MaxHudRowWidth = 1000;

    /// <summary>The Lectern layout's single driving width `W` in pixels (the "Pixel Art Size"). The dialog is
    /// an art-sized box of <c>W × (W·1160/1024)</c> and every inner size derives from <c>W</c>. Read fresh
    /// each build so a change re-lays-out live. Snapped to a 10px grid and clamped on load
    /// (<see cref="ClampPixelArtSize"/>).</summary>
    public int PixelArtSize { get; set; } = DefaultPixelArtSize;

    /// <summary>Default <see cref="PixelArtSize"/>.</summary>
    public const int DefaultPixelArtSize = 600;

    /// <summary>Inclusive lower bound clamped on load.</summary>
    public const int MinPixelArtSize = 400;

    /// <summary>Inclusive upper bound clamped on load.</summary>
    public const int MaxPixelArtSize = 1000;

    /// <summary>Text-size multiplier for HUD row text (1.0 = no change), in 5% notches (0.80..1.20). A
    /// MULTIPLIER so it stacks on the game's global GUI Scale. Clamped and snapped on load.</summary>
    public float HudFontScale { get; set; } = DefaultFontScale;

    /// <summary>Text-size multiplier for block/item window text; same notches and semantics as
    /// <see cref="HudFontScale"/>. Applied at the single <c>ScribeRowStyle</c> chokepoint.</summary>
    public float WindowFontScale { get; set; } = DefaultFontScale;

    /// <summary>Font family for Lectern task/note ROW text. Empty means the built-in body font; otherwise it
    /// must be one of <see cref="KnownTaskFonts"/>, else it falls back on load
    /// (<see cref="NormalizeTaskFontFamily"/>). Does not govern the in-Lectern buttons.</summary>
    public string TaskFontFamily { get; set; } = DefaultTaskFontFamily;

    /// <summary>Empty string = the built-in body font (no override).</summary>
    public const string DefaultTaskFontFamily = "";

    /// <summary>Minimum brightness the GUI is shaded down to in total darkness: the illumination shade
    /// multiplies brightness by the light reaching the player but never below this floor. Range
    /// <see cref="MinIlluminationFloor"/>..<see cref="MaxIlluminationFloor"/> (1.0 opts out of shading).
    /// Clamped on load (<see cref="ClampIlluminationFloor"/>). The sampled light itself is transient.</summary>
    public float IlluminationFloor { get; set; } = DefaultIlluminationFloor;

    /// <summary>Default <see cref="IlluminationFloor"/>: the y-value of the leftmost control point of
    /// <see cref="ScribeBrightnessCurve"/> (GUI brightness at zero local light).</summary>
    public const float DefaultIlluminationFloor = 0.05f;

    /// <summary>Inclusive lower bound clamped on load. Not 0, so a hand-edited config can't render a
    /// perfectly black, apparently broken dialog.</summary>
    public const float MinIlluminationFloor = 0.02f;

    /// <summary>Inclusive upper bound clamped on load: 1.0 = always full brightness.</summary>
    public const float MaxIlluminationFloor = 1.0f;

    /// <summary>The non-default task-font families the selector offers, by exact registered family name
    /// (the empty default is always valid). "Playfair Display" and "Cormorant Unicase" come from the
    /// <c>gui</c> dependency; the rest are bundled. Plain strings so Core stays free of font APIs.</summary>
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

    /// <summary>Maps a loaded task-font value to the empty default or an exact match in
    /// <see cref="KnownTaskFonts"/>; anything else (including null) becomes the default.</summary>
    public static string NormalizeTaskFontFamily(string? value)
    {
        if (string.IsNullOrEmpty(value)) return DefaultTaskFontFamily;
        return Array.IndexOf(KnownTaskFonts, value) >= 0 ? value : DefaultTaskFontFamily;
    }

    /// <summary>Default font-scale multiplier (no scaling).</summary>
    public const float DefaultFontScale = 1.0f;

    /// <summary>Inclusive lower bound (-20%) for both font scales.</summary>
    public const float MinFontScale = 0.8f;

    /// <summary>Inclusive upper bound (+20%).</summary>
    public const float MaxFontScale = 1.2f;

    /// <summary>Clamps a loaded HUD row count to the safe range.</summary>
    public static int ClampHudMaxRows(int value) => Math.Clamp(value, MinHudMaxRows, MaxHudMaxRows);

    /// <summary>Clamps a loaded HUD offset (X or Y) to the safe range.</summary>
    public static int ClampHudOffset(int value) => Math.Clamp(value, MinHudOffset, MaxHudOffset);

    /// <summary>Clamps a font scale to <see cref="MinFontScale"/>..<see cref="MaxFontScale"/> and snaps it to
    /// the nearest 0.05 notch.</summary>
    public static float ClampFontScale(float value)
    {
        float clamped = Math.Clamp(value, MinFontScale, MaxFontScale);
        return MathF.Round(clamped * 20f) / 20f;
    }

    /// <summary>Clamps a loaded HUD row width to the safe range.</summary>
    public static int ClampHudRowWidth(int value) => Math.Clamp(value, MinHudRowWidth, MaxHudRowWidth);

    /// <summary>Clamps a loaded illumination floor to <see cref="MinIlluminationFloor"/>..<see
    /// cref="MaxIlluminationFloor"/>.</summary>
    public static float ClampIlluminationFloor(float value) =>
        Math.Clamp(value, MinIlluminationFloor, MaxIlluminationFloor);

    /// <summary>Clamps a loaded Pixel Art Size to its range and snaps it to the nearest 10px.</summary>
    public static int ClampPixelArtSize(int value)
    {
        int clamped = Math.Clamp(value, MinPixelArtSize, MaxPixelArtSize);
        return (int)Math.Round(clamped / 10.0) * 10;
    }

    /// <summary>Maps an unrecognized HUD anchor to <see cref="ScribeHudAnchor.TopLeft"/>.</summary>
    public static ScribeHudAnchor NormalizeAnchor(ScribeHudAnchor value) =>
        Enum.IsDefined(typeof(ScribeHudAnchor), value) ? value : ScribeHudAnchor.TopLeft;

    /// <summary>Maps an unrecognized completion policy to <see cref="ScribeCompletionPolicy.Sink"/>. The
    /// server normalizes the policy in completion requests the same way.</summary>
    public static ScribeCompletionPolicy NormalizePolicy(ScribeCompletionPolicy value) =>
        Enum.IsDefined(typeof(ScribeCompletionPolicy), value) ? value : ScribeCompletionPolicy.Sink;

    public static ScribeQuestAcceptPolicy NormalizeQuestAcceptPolicy(ScribeQuestAcceptPolicy value) =>
        Enum.IsDefined(typeof(ScribeQuestAcceptPolicy), value) ? value : ScribeQuestAcceptPolicy.PromptHud;

    public static ScribeQuestCompletionPolicy NormalizeQuestCompletionPolicy(ScribeQuestCompletionPolicy value) =>
        Enum.IsDefined(typeof(ScribeQuestCompletionPolicy), value) ? value : ScribeQuestCompletionPolicy.Prompt;

    /// <summary>Maps an unrecognized tracker-completion value to <see cref="ScribeTrackerCompletion.Complete"/>.</summary>
    public static ScribeTrackerCompletion NormalizeTrackerCompletion(ScribeTrackerCompletion value) =>
        Enum.IsDefined(typeof(ScribeTrackerCompletion), value) ? value : ScribeTrackerCompletion.Complete;

    /// <summary>Maps an unrecognized (loaded or on-the-wire) Subtask Behavior to
    /// <see cref="ScribeSubtaskBehavior.Bound"/>, covering old clients that omit the packet field.</summary>
    public static ScribeSubtaskBehavior NormalizeSubtaskBehavior(ScribeSubtaskBehavior value) =>
        Enum.IsDefined(typeof(ScribeSubtaskBehavior), value) ? value : ScribeSubtaskBehavior.Bound;

    /// <summary>Maps an unrecognized New Task Insert value to <see cref="ScribeNewTaskInsert.Top"/>.</summary>
    public static ScribeNewTaskInsert NormalizeNewTaskInsert(ScribeNewTaskInsert value) =>
        Enum.IsDefined(typeof(ScribeNewTaskInsert), value) ? value : ScribeNewTaskInsert.Top;

    /// <summary>Maps an unrecognized Pin Insert value to <see cref="ScribePinInsert.Bottom"/>.</summary>
    public static ScribePinInsert NormalizePinInsert(ScribePinInsert value) =>
        Enum.IsDefined(typeof(ScribePinInsert), value) ? value : ScribePinInsert.Bottom;

    /// <summary>Maps an unrecognized timer-mode value to <see cref="TimerMode.RealTime"/>.</summary>
    public static TimerMode NormalizeTimerMode(TimerMode value) =>
        Enum.IsDefined(typeof(TimerMode), value) ? value : TimerMode.RealTime;

    /// <summary>Folds legacy on-disk keys into their successors, in place. When the legacy
    /// <c>DisableCuneiformFont</c> is present it wins (<c>CuneiformTablets = !DisableCuneiformFont</c>) and is
    /// then cleared; an absent key leaves the new value alone. Idempotent. Called by <see cref="Normalized"/>.</summary>
    public ScribePlayerSettings MigrateLegacyKeys()
    {
        if (DisableCuneiformFont is bool legacyDisabled)
        {
            CuneiformTablets = !legacyDisabled;
            DisableCuneiformFont = null;
        }
        return this;
    }

    /// <summary>Normalizes fields in place after loading untrusted (hand-edited) JSON: migrates legacy keys,
    /// clamps/snaps numeric preferences, and resets unknown enum values to their defaults. Returns this.</summary>
    public ScribePlayerSettings Normalized()
    {
        MigrateLegacyKeys();
        HudMaxRows = ClampHudMaxRows(HudMaxRows);
        CompletionPolicy = NormalizePolicy(CompletionPolicy);
        QuestAcceptPolicy = NormalizeQuestAcceptPolicy(QuestAcceptPolicy);
        QuestCompletionPolicy = NormalizeQuestCompletionPolicy(QuestCompletionPolicy);
        TrackerCompletion = NormalizeTrackerCompletion(TrackerCompletion);
        SubtaskBehavior = NormalizeSubtaskBehavior(SubtaskBehavior);
        NewTaskInsert = NormalizeNewTaskInsert(NewTaskInsert);
        PinInsert = NormalizePinInsert(PinInsert);
        PreferredTimerMode = NormalizeTimerMode(PreferredTimerMode);
        HudAnchor = NormalizeAnchor(HudAnchor);
        HudRowWidth = ClampHudRowWidth(HudRowWidth);
        PixelArtSize = ClampPixelArtSize(PixelArtSize);
        HudOffsetX = ClampHudOffset(HudOffsetX);
        HudOffsetY = ClampHudOffset(HudOffsetY);
        HudFontScale = ClampFontScale(HudFontScale);
        WindowFontScale = ClampFontScale(WindowFontScale);
        TaskFontFamily = NormalizeTaskFontFamily(TaskFontFamily);
        IlluminationFloor = ClampIlluminationFloor(IlluminationFloor);
        TimerAlarmVolume  = ClampTimerAlarmVolume(TimerAlarmVolume);
        return this;
    }
}
