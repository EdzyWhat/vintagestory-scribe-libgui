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

    // ---- SPSE / SPSS: settings round-trip + defaults ----

    [Fact]
    public void Settings_RoundTrip_PreservesFields()
    {
        var settings = new ScribePlayerSettings { CompleteUnpins = false, HudCollapsed = true };

        byte[] bytes = ScribePinCodec.SerializeSettings(settings);
        bool ok = ScribePinCodec.TryDeserializeSettings(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.False(restored!.CompleteUnpins);
        Assert.True(restored.HudCollapsed);
    }

    [Fact]
    public void Settings_DefaultInstance_HasCompleteUnpinsEnabled()
    {
        // A player who never changed a setting: complete-to-unpin on, HUD not collapsed.
        var settings = new ScribePlayerSettings();

        Assert.True(settings.CompleteUnpins);
        Assert.False(settings.HudCollapsed);
    }

    [Fact]
    public void SettingsStore_RoundTrip_PreservesPerPlayerSettings()
    {
        var store = new Dictionary<string, ScribePlayerSettings>
        {
            ["player-1"] = new() { CompleteUnpins = false },
            ["player-2"] = new() { HudCollapsed = true },
        };

        byte[] bytes = ScribePinCodec.SerializeSettingsStore(store);
        bool ok = ScribePinCodec.TryDeserializeSettingsStore(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.False(restored!["player-1"].CompleteUnpins);
        Assert.True(restored["player-2"].HudCollapsed);
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
        // A settings blob is not a pin list — the magic must gate it out.
        byte[] settingsBytes = ScribePinCodec.SerializeSettings(new ScribePlayerSettings());

        Assert.False(ScribePinCodec.TryDeserializeList(settingsBytes, out var restored));
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
