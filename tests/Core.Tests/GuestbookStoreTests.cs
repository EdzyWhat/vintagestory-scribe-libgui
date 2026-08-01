using Scribe.Core;

namespace Scribe.Core.Tests;

public class GuestbookStoreTests
{
    [Fact]
    public void TryAddEntry_AddsFirstEntry()
    {
        var store = new GuestbookStore();
        bool added = store.TryAddEntry("Alice", "1st of Harvestmonth");
        Assert.True(added);
        Assert.Single(store.Entries);
        Assert.Equal("Alice", store.Entries[0].PlayerName);
    }

    [Fact]
    public void TryAddEntry_DeduplicatesSamePlayerSameDay()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        bool second = store.TryAddEntry("Alice", "1st of Harvestmonth");
        Assert.False(second);
        Assert.Single(store.Entries);
    }

    [Fact]
    public void TryAddEntry_AllowsSamePlayerOnNewDay()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        bool second = store.TryAddEntry("Alice", "2nd of Harvestmonth");
        Assert.True(second);
        Assert.Equal(2, store.Entries.Count);
    }

    [Fact]
    public void TryAddEntry_AllowsDifferentPlayersOnSameDay()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        bool second = store.TryAddEntry("Bob", "1st of Harvestmonth");
        Assert.True(second);
        Assert.Equal(2, store.Entries.Count);
    }

    [Fact]
    public void TryAddEntry_DropsOldestWhenAtCap()
    {
        var store = new GuestbookStore();
        for (int i = 0; i < GuestbookStore.MaxEntries; i++)
            store.TryAddEntry($"Player{i}", $"Day {i}");

        Assert.Equal(GuestbookStore.MaxEntries, store.Entries.Count);
        Assert.Equal("Player0", store.Entries[0].PlayerName);

        store.TryAddEntry("NewPlayer", "Day overflow");
        Assert.Equal(GuestbookStore.MaxEntries, store.Entries.Count);
        Assert.Equal("Player1", store.Entries[0].PlayerName);
        Assert.Equal("NewPlayer", store.Entries[^1].PlayerName);
    }

    [Fact]
    public void TryAddEntry_PrunesOldestNotelessEntryWhenPlayerExceedsSoftCap()
    {
        var store = new GuestbookStore();
        // Fill the soft cap exactly; none of these carry a note.
        for (int i = 0; i < GuestbookStore.SoftMaxEntriesPerPlayer; i++)
            store.TryAddEntry("Alice", $"Day {i}");
        Assert.Equal(GuestbookStore.SoftMaxEntriesPerPlayer, store.Entries.Count);

        // One more visit tips her over the soft cap: the oldest note-less entry (Day 0) is pruned,
        // so the count holds at the cap and the newest day survives.
        store.TryAddEntry("Alice", "Day overflow");
        Assert.Equal(GuestbookStore.SoftMaxEntriesPerPlayer, store.Entries.Count);
        Assert.DoesNotContain(store.Entries, e => e.InGameDate == "Day 0");
        Assert.Contains(store.Entries, e => e.InGameDate == "Day overflow");
    }

    [Fact]
    public void TryAddEntry_NeverPrunesEntriesWithNotes()
    {
        var store = new GuestbookStore();
        // Every entry within the soft cap carries a note — none are prunable.
        for (int i = 0; i < GuestbookStore.SoftMaxEntriesPerPlayer; i++)
        {
            store.TryAddEntry("Alice", $"Day {i}");
            store.TrySetNote("Alice", $"Day {i}", $"note {i}");
        }

        // Adding another must NOT delete any noted entry, so the player exceeds the soft cap.
        store.TryAddEntry("Alice", "Day overflow");
        Assert.Equal(GuestbookStore.SoftMaxEntriesPerPlayer + 1, store.Entries.Count);
        for (int i = 0; i < GuestbookStore.SoftMaxEntriesPerPlayer; i++)
            Assert.Contains(store.Entries, e => e.InGameDate == $"Day {i}");
    }

    [Fact]
    public void TryAddEntry_PrunesOnlyTheActingPlayersEntries()
    {
        var store = new GuestbookStore();
        // Bob has an old note-less entry that must be left alone when Alice tips over her cap.
        store.TryAddEntry("Bob", "Bob's day");
        for (int i = 0; i < GuestbookStore.SoftMaxEntriesPerPlayer; i++)
            store.TryAddEntry("Alice", $"Day {i}");

        store.TryAddEntry("Alice", "Day overflow");

        Assert.Contains(store.Entries, e => e.PlayerName == "Bob" && e.InGameDate == "Bob's day");
        Assert.Equal(GuestbookStore.SoftMaxEntriesPerPlayer, store.Entries.Count(e => e.PlayerName == "Alice"));
    }

    [Fact]
    public void TryAddEntry_PrunesOldestNotelessNotOldestOverall()
    {
        var store = new GuestbookStore();
        for (int i = 0; i < GuestbookStore.SoftMaxEntriesPerPlayer; i++)
            store.TryAddEntry("Alice", $"Day {i}");
        // Give the oldest entry a note so it's protected; the oldest note-LESS one is now Day 1.
        store.TrySetNote("Alice", "Day 0", "kept forever");

        store.TryAddEntry("Alice", "Day overflow");

        Assert.Contains(store.Entries, e => e.InGameDate == "Day 0");        // noted → protected
        Assert.DoesNotContain(store.Entries, e => e.InGameDate == "Day 1");  // oldest note-less → pruned
    }

    [Fact]
    public void TrySetNote_SetsNoteOnMatchingEntry()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        bool changed = store.TrySetNote("Alice", "1st of Harvestmonth", "Found the copper vein!");
        Assert.True(changed);
        Assert.Equal("Found the copper vein!", store.Entries[0].Note);
    }

    [Fact]
    public void TrySetNote_ReturnsFalseForUnknownPlayer()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        bool changed = store.TrySetNote("Bob", "1st of Harvestmonth", "Hello");
        Assert.False(changed);
    }

    [Fact]
    public void TrySetNote_ReturnsFalseIfNoteUnchanged()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        store.TrySetNote("Alice", "1st of Harvestmonth", "First visit");
        bool changed = store.TrySetNote("Alice", "1st of Harvestmonth", "First visit");
        Assert.False(changed);
    }

    [Fact]
    public void TrySetNote_ClampsToMaxNoteLength()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        var longNote = new string('x', GuestbookStore.MaxNoteLength + 10);
        store.TrySetNote("Alice", "1st of Harvestmonth", longNote);
        Assert.Equal(GuestbookStore.MaxNoteLength, store.Entries[0].Note.Length);
    }

    [Fact]
    public void TrySetNote_UpdatesOnlyTheAddressedDayForSamePlayer()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "Day 1");
        store.TryAddEntry("Alice", "Day 2");

        bool changed = store.TrySetNote("Alice", "Day 2", "Note on the second day");

        Assert.True(changed);
        Assert.Equal("", store.Entries[0].Note);                       // Day 1 untouched
        Assert.Equal("Note on the second day", store.Entries[1].Note); // Day 2 updated
    }

    [Fact]
    public void TrySetNote_ReturnsFalseWhenDateMatchesNoEntry()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "Day 1");
        // Right player, wrong day: no entry matches, so nothing is written.
        bool changed = store.TrySetNote("Alice", "Day 2", "Note for a day she never visited");
        Assert.False(changed);
        Assert.Equal("", store.Entries[0].Note);
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrips()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        store.TrySetNote("Alice", "1st of Harvestmonth", "Found iron!");
        store.TryAddEntry("Bob", "2nd of Harvestmonth");

        var restored = GuestbookStore.Deserialize(store.Serialize());

        Assert.Equal(2, restored.Entries.Count);
        Assert.Equal("Alice", restored.Entries[0].PlayerName);
        Assert.Equal("1st of Harvestmonth", restored.Entries[0].InGameDate);
        Assert.Equal("Found iron!", restored.Entries[0].Note);
        Assert.Equal("Bob", restored.Entries[1].PlayerName);
    }

    [Fact]
    public void Deserialize_NullBytes_ReturnsEmptyStore()
    {
        var store = GuestbookStore.Deserialize(null);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Deserialize_MalformedBytes_ReturnsEmptyStore()
    {
        var store = GuestbookStore.Deserialize(new byte[] { 0x01, 0x02, 0x03 });
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void EmptyStore_SerializeDeserialize_RoundTrips()
    {
        var store = new GuestbookStore();
        var restored = GuestbookStore.Deserialize(store.Serialize());
        Assert.Empty(restored.Entries);
    }
}
