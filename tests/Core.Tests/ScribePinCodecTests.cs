using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the per-player pin + settings codec. The same bytes are used for network sync and
// save-game persistence, so the round-trip must be exact and any malformed/hostile input must
// fail safely (return false) rather than throw or over-allocate.
public class ScribePinCodecTests
{
    private static ScribePinnedRef Pin(string text = "Find copper", bool done = false, bool orphaned = false,
        ScribeBlockKind kind = ScribeBlockKind.Task, string? linkTarget = null) => new()
    {
        OwnerDocId = Guid.NewGuid(),
        TaskId = Guid.NewGuid(),
        PinnedAtTotalHours = 1234.5,
        Orphaned = orphaned,
        LastKnownText = text,
        LastKnownDone = done,
        Kind = kind,
        LinkTarget = linkTarget,
    };

    private static void AssertPinEqual(ScribePinnedRef expected, ScribePinnedRef actual)
    {
        Assert.Equal(expected.OwnerDocId, actual.OwnerDocId);
        Assert.Equal(expected.TaskId, actual.TaskId);
        Assert.Equal(expected.PinnedAtTotalHours, actual.PinnedAtTotalHours);
        Assert.Equal(expected.Orphaned, actual.Orphaned);
        Assert.Equal(expected.LastKnownText, actual.LastKnownText);
        Assert.Equal(expected.LastKnownDone, actual.LastKnownDone);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.LinkTarget, actual.LinkTarget);
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

    // ---- v2: Kind + LinkTarget (add-tracker-link-tasks 5.5) ----

    [Fact]
    public void List_RoundTrip_PreservesKindAndLinkTarget()
    {
        // A Link pin carries its target; a Tracker pin carries its kind but no link target; a plain Task
        // pin round-trips with the defaults — all three must survive the v2 layout exactly.
        var pins = new List<ScribePinnedRef>
        {
            Pin("See copper", kind: ScribeBlockKind.Link, linkTarget: "game:ingot-copper"),
            Pin("Gather flax", kind: ScribeBlockKind.Tracker),
            Pin("Plain task"),
        };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(3, restored!.Count);
        AssertPinEqual(pins[0], restored[0]);
        AssertPinEqual(pins[1], restored[1]);
        AssertPinEqual(pins[2], restored[2]);
    }

    [Fact]
    public void TryDeserialize_V1Bytes_KindAndLinkTarget_AreUpgraded()
    {
        // Hand-build a v1 SPIN blob (no Kind/LinkTarget fields) exactly as the pre-5.5 codec wrote it, and
        // assert the migration defaults them (Kind→Task, LinkTarget→null) rather than merely deserializing.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)1);          // v1
            w.Write(1);                // one pin
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(999.0);            // PinnedAtTotalHours
            w.Write(false);            // Orphaned
            w.Write(true);             // LastKnownDone
            w.Write("Old pin");        // LastKnownText — v1 ends here, no Kind/LinkTarget
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        var pin = Assert.Single(restored!);
        Assert.Equal(docId, pin.OwnerDocId);
        Assert.Equal(taskId, pin.TaskId);
        Assert.Equal("Old pin", pin.LastKnownText);
        Assert.True(pin.LastKnownDone);
        Assert.Equal(ScribeBlockKind.Task, pin.Kind);   // defaulted by ApplyV1ToV2Migrations
        Assert.Null(pin.LinkTarget);                    // defaulted by ApplyV1ToV2Migrations
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
    [InlineData(1000, 10)]           // above max → clamped down (max lowered 20 → 10, §10.3)
    [InlineData(11, 10)]             // a previously-valid value now re-clamps to the new max
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
