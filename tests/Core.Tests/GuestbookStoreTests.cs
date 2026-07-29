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
    public void TrySetNote_SetsNoteOnMatchingEntry()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        bool changed = store.TrySetNote("Alice", "Found the copper vein!");
        Assert.True(changed);
        Assert.Equal("Found the copper vein!", store.Entries[0].Note);
    }

    [Fact]
    public void TrySetNote_ReturnsFalseForUnknownPlayer()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        bool changed = store.TrySetNote("Bob", "Hello");
        Assert.False(changed);
    }

    [Fact]
    public void TrySetNote_ReturnsFalseIfNoteUnchanged()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        store.TrySetNote("Alice", "First visit");
        bool changed = store.TrySetNote("Alice", "First visit");
        Assert.False(changed);
    }

    [Fact]
    public void TrySetNote_ClampsToMaxNoteLength()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        var longNote = new string('x', GuestbookStore.MaxNoteLength + 10);
        store.TrySetNote("Alice", longNote);
        Assert.Equal(GuestbookStore.MaxNoteLength, store.Entries[0].Note.Length);
    }

    [Fact]
    public void TrySetNote_MatchesFirstEntryForPlayer()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "Day 1");
        store.TryAddEntry("Alice", "Day 2");
        store.TrySetNote("Alice", "Note on first");
        Assert.Equal("Note on first", store.Entries[0].Note);
        Assert.Equal("", store.Entries[1].Note);
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrips()
    {
        var store = new GuestbookStore();
        store.TryAddEntry("Alice", "1st of Harvestmonth");
        store.TrySetNote("Alice", "Found iron!");
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
