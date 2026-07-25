using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the per-player pin + settings codec. The same bytes are used for network sync and
// save-game persistence, so the round-trip must be exact and any malformed/hostile input must
// fail safely (return false) rather than throw or over-allocate.
public class ScribePinCodecTests
{
    private static ScribePinnedRef Pin(string text = "Find copper", bool done = false, bool orphaned = false) => new()
    {
        OwnerDocId = Guid.NewGuid(),
        TaskId = Guid.NewGuid(),
        PinnedAtTotalHours = 1234.5,
        Orphaned = orphaned,
        LastKnownText = text,
        LastKnownDone = done,
    };

    private static void AssertPinEqual(ScribePinnedRef expected, ScribePinnedRef actual)
    {
        Assert.Equal(expected.OwnerDocId, actual.OwnerDocId);
        Assert.Equal(expected.TaskId, actual.TaskId);
        Assert.Equal(expected.PinnedAtTotalHours, actual.PinnedAtTotalHours);
        Assert.Equal(expected.Orphaned, actual.Orphaned);
        Assert.Equal(expected.LastKnownText, actual.LastKnownText);
        Assert.Equal(expected.LastKnownDone, actual.LastKnownDone);
    }

    // ---- SPIN: list round-trip ----

    [Fact]
    public void List_RoundTrip_PreservesAllFields()
    {
        var pins = new List<ScribePinnedRef> { Pin("A", done: true), Pin("B", orphaned: true) };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        AssertPinEqual(pins[0], restored[0]);
        AssertPinEqual(pins[1], restored[1]);
    }

    [Fact]
    public void List_RoundTrip_Empty()
    {
        byte[] bytes = ScribePinCodec.SerializeList(new List<ScribePinnedRef>());
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Empty(restored!);
    }

    // ---- SPST: store round-trip ----

    [Fact]
    public void Store_RoundTrip_PreservesPerPlayerSets()
    {
        var store = new Dictionary<string, List<ScribePinnedRef>>
        {
            ["player-1"] = new() { Pin("A") },
            ["player-2"] = new() { Pin("B"), Pin("C") },
        };

        byte[] bytes = ScribePinCodec.SerializeStore(store);
        bool ok = ScribePinCodec.TryDeserializeStore(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        Assert.Single(restored["player-1"]);
        Assert.Equal(2, restored["player-2"].Count);
        AssertPinEqual(store["player-2"][1], restored["player-2"][1]);
    }

    // ---- Player-preference defaults + normalize guards ----
    //
    // Settings are no longer a codec blob — they persist as client-local JSON (add-pinned-task-hud
    // settings-storage pivot), so there is no serialize/deserialize round-trip to cover here. What
    // survives in Core are the default values and the clamp/normalize guards applied when loading the
    // (hand-editable, untrusted) config; those are exercised directly below.

    [Fact]
    public void Settings_DefaultInstance_SinksAndShowsThreeRows()
    {
        // A player who never changed a setting: Sink policy, HUD not collapsed, 3 rows.
        var settings = new ScribePlayerSettings();

        Assert.Equal(ScribeCompletionPolicy.Sink, settings.CompletionPolicy);
        Assert.False(settings.HudCollapsed);
        Assert.Equal(3, settings.HudMaxRows);
    }

    [Theory]
    [InlineData(0, 1)]                 // below min → clamped up
    [InlineData(-5, 1)]                // negative → clamped up
    [InlineData(1000, 20)]            // above max → clamped down
    [InlineData(5, 5)]                 // in range → unchanged
    public void Settings_ClampHudMaxRows_BoundsTheValue(int stored, int expected)
    {
        Assert.Equal(expected, ScribePlayerSettings.ClampHudMaxRows(stored));
    }

    [Fact]
    public void Settings_NormalizePolicy_FallsUnknownBackToSink()
    {
        // A defined value passes through; an undefined enum value (cast from a hand-broken JSON) falls
        // back to the safe non-destructive default.
        Assert.Equal(ScribeCompletionPolicy.Delete,
            ScribePlayerSettings.NormalizePolicy(ScribeCompletionPolicy.Delete));
        Assert.Equal(ScribeCompletionPolicy.Sink,
            ScribePlayerSettings.NormalizePolicy((ScribeCompletionPolicy)99));
    }

    [Fact]
    public void Settings_Normalized_ClampsRowsAndPolicyInPlace()
    {
        var settings = new ScribePlayerSettings
        {
            HudMaxRows = 1000,
            CompletionPolicy = (ScribeCompletionPolicy)99,
            HudCollapsed = true,
        };

        var result = settings.Normalized();

        Assert.Same(settings, result);                                   // mutates + returns this
        Assert.Equal(ScribePlayerSettings.MaxHudMaxRows, settings.HudMaxRows);
        Assert.Equal(ScribeCompletionPolicy.Sink, settings.CompletionPolicy);
        Assert.True(settings.HudCollapsed);                              // untouched field preserved
    }

    // ---- HUD position preferences (add-pinned-task-hud 4.4) ----

    [Fact]
    public void Settings_DefaultInstance_AnchorsTopRightAt250Wide()
    {
        var settings = new ScribePlayerSettings();

        Assert.Equal(ScribeHudAnchor.TopRight, settings.HudAnchor);
        Assert.Equal(250, settings.HudRowWidth);
        Assert.Equal(0, settings.HudOffsetX);
        Assert.Equal(0, settings.HudOffsetY);
    }

    [Fact]
    public void Settings_NormalizeAnchor_FallsUnknownBackToTopRight()
    {
        Assert.Equal(ScribeHudAnchor.BottomLeft,
            ScribePlayerSettings.NormalizeAnchor(ScribeHudAnchor.BottomLeft));
        Assert.Equal(ScribeHudAnchor.TopRight,
            ScribePlayerSettings.NormalizeAnchor((ScribeHudAnchor)99));
    }

    [Theory]
    [InlineData(0, ScribePlayerSettings.MinHudRowWidth)]        // below min → clamped up
    [InlineData(-40, ScribePlayerSettings.MinHudRowWidth)]      // negative → clamped up
    [InlineData(5000, ScribePlayerSettings.MaxHudRowWidth)]     // above max → clamped down
    [InlineData(300, 300)]                                      // in range → unchanged
    public void Settings_ClampHudRowWidth_BoundsTheValue(int stored, int expected)
    {
        Assert.Equal(expected, ScribePlayerSettings.ClampHudRowWidth(stored));
    }

    [Fact]
    public void Settings_Normalized_ClampsWidthAndAnchorInPlace()
    {
        var settings = new ScribePlayerSettings
        {
            HudAnchor = (ScribeHudAnchor)99,
            HudRowWidth = 5000,
            HudOffsetX = -37,   // offsets are free-form nudges; not clamped
            HudOffsetY = 42,
        };

        settings.Normalized();

        Assert.Equal(ScribeHudAnchor.TopRight, settings.HudAnchor);
        Assert.Equal(ScribePlayerSettings.MaxHudRowWidth, settings.HudRowWidth);
        Assert.Equal(-37, settings.HudOffsetX);   // untouched
        Assert.Equal(42, settings.HudOffsetY);    // untouched
    }

    // ---- fail-safe paths ----

    [Fact]
    public void TryDeserializeList_Null_FailsSafely()
    {
        Assert.False(ScribePinCodec.TryDeserializeList(null, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_Garbage_FailsSafely()
    {
        var garbage = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0x7A, 0x00 };

        Assert.False(ScribePinCodec.TryDeserializeList(garbage, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_WrongMagic_FailsSafely()
    {
        // A blob with a different 4-byte magic is not a pin list — the magic must gate it out.
        byte[] wrongMagic = System.Text.Encoding.UTF8.GetBytes("SPSE");

        Assert.False(ScribePinCodec.TryDeserializeList(wrongMagic, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_Truncated_FailsSafely()
    {
        byte[] bytes = ScribePinCodec.SerializeList(new List<ScribePinnedRef> { Pin() });
        var truncated = bytes[..(bytes.Length - 4)]; // lop off the tail of the text

        Assert.False(ScribePinCodec.TryDeserializeList(truncated, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_OverPinCap_FailsSafely()
    {
        // Hand-build a list blob claiming more than MaxPinsPerPlayer entries.
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)1);
            w.Write(ScribePinCodec.MaxPinsPerPlayer + 1);
        }

        Assert.False(ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_OverLongText_FailsSafely()
    {
        var pin = Pin(text: new string('a', ScribeDocumentCodec.MaxTextLength + 1));
        byte[] bytes = ScribePinCodec.SerializeList(new List<ScribePinnedRef> { pin });

        Assert.False(ScribePinCodec.TryDeserializeList(bytes, out var restored));
        Assert.Null(restored);
    }
}
