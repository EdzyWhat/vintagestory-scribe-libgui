using Scribe.Core;

namespace Scribe.Core.Tests;

public class HistoryStoreTests
{
    // ---- Helpers ----

    private static HistoryEntry Entry(HistoryEventKind kind, string actor = "", string detail = "")
        => new() { Kind = kind, ActorName = actor, Detail = detail, InGameDate = "Year 1, Day 1" };

    // ---- Round-trip ----

    [Fact]
    public void RoundTrip_EmptyStore()
    {
        var store = new HistoryStore();
        var restored = HistoryStore.Deserialize(store.Serialize());
        Assert.Empty(restored.Entries);
    }

    [Fact]
    public void RoundTrip_AllKinds()
    {
        var store = new HistoryStore();
        store.TryAddEntry(Entry(HistoryEventKind.Crafted, "Alice"));
        store.TryAddEntry(Entry(HistoryEventKind.PickedUp, "Bob"));
        store.TryAddEntry(Entry(HistoryEventKind.Death, "Alice", "Alice fell to her death."));
        store.TryAddEntry(Entry(HistoryEventKind.PvpKill, "Alice", "Bob was slain"));
        store.TryAddEntry(Entry(HistoryEventKind.BossKill, "Alice", "Eidolon"));
        store.TryAddEntry(Entry(HistoryEventKind.TemporalStorm, "", "Heavy"));
        store.TryAddEntry(Entry(HistoryEventKind.LoreDiscovery, "Alice", "The Miller Part 1/2"));
        store.TryAddEntry(new HistoryEntry
        {
            Kind = HistoryEventKind.Manual, ActorName = "Alice", Detail = "Found the archives",
            InGameDate = "Year 1, Day 1", EntryId = Guid.NewGuid(),
        });

        var restored = HistoryStore.Deserialize(store.Serialize());

        Assert.Equal(8, restored.Entries.Count);
        Assert.Equal(HistoryEventKind.Crafted, restored.Entries[0].Kind);
        Assert.Equal(HistoryEventKind.Manual,  restored.Entries[7].Kind);
    }

    [Fact]
    public void Deserialize_NullBytes_ReturnsEmpty()
    {
        var store = HistoryStore.Deserialize(null);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Deserialize_EmptyBytes_ReturnsEmpty()
    {
        var store = HistoryStore.Deserialize(Array.Empty<byte>());
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Deserialize_MalformedBytes_ReturnsEmpty()
    {
        var store = HistoryStore.Deserialize(new byte[] { 0x01, 0x02, 0x03 });
        Assert.Empty(store.Entries);
    }

    // ---- Crafted: only one ever ----

    [Fact]
    public void Crafted_OnlyWrittenOnce()
    {
        var store = new HistoryStore();
        Assert.True(store.TryAddEntry(Entry(HistoryEventKind.Crafted, "Alice")));
        Assert.False(store.TryAddEntry(Entry(HistoryEventKind.Crafted, "Bob")));
        Assert.Single(store.Entries);
    }

    // ---- PickedUp: deduped by ActorName ----

    [Fact]
    public void PickedUp_DeduplicatedByActorName()
    {
        var store = new HistoryStore();
        Assert.True(store.TryAddEntry(Entry(HistoryEventKind.PickedUp, "Alice")));
        Assert.False(store.TryAddEntry(Entry(HistoryEventKind.PickedUp, "Alice")));
        Assert.True(store.TryAddEntry(Entry(HistoryEventKind.PickedUp, "Bob")));
        Assert.Equal(2, store.Entries.Count);
    }

    // ---- Death: sliding window at MaxDeaths ----

    [Fact]
    public void Death_SlidingWindowAtCap()
    {
        var store = new HistoryStore();
        for (int i = 0; i < HistoryStore.MaxDeaths; i++)
            store.TryAddEntry(Entry(HistoryEventKind.Death, $"Player{i}", $"death {i}"));

        Assert.Equal(HistoryStore.MaxDeaths, store.Entries.Count);

        // Add one more — should drop death 0 and add the new one
        store.TryAddEntry(Entry(HistoryEventKind.Death, "NewPlayer", "death new"));

        var deaths = store.Entries.Where(e => e.Kind == HistoryEventKind.Death).ToList();
        Assert.Equal(HistoryStore.MaxDeaths, deaths.Count);
        Assert.DoesNotContain(deaths, e => e.Detail == "death 0");
        Assert.Contains(deaths, e => e.Detail == "death new");
    }

    // ---- TemporalStorm: sliding window at MaxStorms ----

    [Fact]
    public void TemporalStorm_SlidingWindowAtCap()
    {
        var store = new HistoryStore();
        for (int i = 0; i < HistoryStore.MaxStorms; i++)
            store.TryAddEntry(Entry(HistoryEventKind.TemporalStorm, "", $"Heavy {i}"));

        store.TryAddEntry(Entry(HistoryEventKind.TemporalStorm, "", "Heavy new"));

        var storms = store.Entries.Where(e => e.Kind == HistoryEventKind.TemporalStorm).ToList();
        Assert.Equal(HistoryStore.MaxStorms, storms.Count);
        Assert.DoesNotContain(storms, e => e.Detail == "Heavy 0");
        Assert.Contains(storms, e => e.Detail == "Heavy new");
    }

    // ---- LoreDiscovery round-trips ----

    [Fact]
    public void LoreDiscovery_RoundTripsThroughCodec()
    {
        var store = new HistoryStore();
        store.TryAddEntry(Entry(HistoryEventKind.LoreDiscovery, "Alice", "The Miller Part 1/2"));

        var restored = HistoryStore.Deserialize(store.Serialize());
        Assert.Single(restored.Entries);
        Assert.Equal(HistoryEventKind.LoreDiscovery, restored.Entries[0].Kind);
        Assert.Equal("The Miller Part 1/2", restored.Entries[0].Detail);
    }

    // ---- PvpKill and BossKill sliding windows ----

    [Fact]
    public void PvpKill_SlidingWindowAtCap()
    {
        var store = new HistoryStore();
        for (int i = 0; i < HistoryStore.MaxPvpKills; i++)
            store.TryAddEntry(Entry(HistoryEventKind.PvpKill, "Alice", $"kill {i}"));

        store.TryAddEntry(Entry(HistoryEventKind.PvpKill, "Alice", "kill new"));

        var kills = store.Entries.Where(e => e.Kind == HistoryEventKind.PvpKill).ToList();
        Assert.Equal(HistoryStore.MaxPvpKills, kills.Count);
        Assert.DoesNotContain(kills, e => e.Detail == "kill 0");
    }

    [Fact]
    public void BossKill_SlidingWindowAtCap()
    {
        var store = new HistoryStore();
        for (int i = 0; i < HistoryStore.MaxBossKills; i++)
            store.TryAddEntry(Entry(HistoryEventKind.BossKill, "Alice", "Eidolon"));

        store.TryAddEntry(Entry(HistoryEventKind.BossKill, "Alice", "Mad Crow"));

        var kills = store.Entries.Where(e => e.Kind == HistoryEventKind.BossKill).ToList();
        Assert.Equal(HistoryStore.MaxBossKills, kills.Count);
    }

    // ---- Manual: sliding window at MaxManual ----

    [Fact]
    public void Manual_SlidingWindowAtCap()
    {
        var store = new HistoryStore();
        for (int i = 0; i < HistoryStore.MaxManual; i++)
            store.TryAddEntry(new HistoryEntry
            {
                Kind = HistoryEventKind.Manual, ActorName = "Alice", Detail = $"entry {i}",
                InGameDate = "Year 1, Day 1", EntryId = Guid.NewGuid(),
            });

        Assert.Equal(HistoryStore.MaxManual, store.Entries.Count);

        store.TryAddEntry(new HistoryEntry
        {
            Kind = HistoryEventKind.Manual, ActorName = "Alice", Detail = "entry new",
            InGameDate = "Year 1, Day 1", EntryId = Guid.NewGuid(),
        });

        var manuals = store.Entries.Where(e => e.Kind == HistoryEventKind.Manual).ToList();
        Assert.Equal(HistoryStore.MaxManual, manuals.Count);
        Assert.DoesNotContain(manuals, e => e.Detail == "entry 0");
        Assert.Contains(manuals, e => e.Detail == "entry new");
    }

    // ---- Manual: edit/delete ownership ----

    [Fact]
    public void TrySetManualEntryText_SucceedsForOwnEntry()
    {
        var store = new HistoryStore();
        var id = Guid.NewGuid();
        store.TryAddEntry(new HistoryEntry
        {
            Kind = HistoryEventKind.Manual, ActorName = "Alice", Detail = "",
            InGameDate = "Year 1, Day 1", EntryId = id,
        });

        Assert.True(store.TrySetManualEntryText(id, "Alice", "Found the archives"));
        Assert.Equal("Found the archives", store.Entries[0].Detail);
    }

    [Fact]
    public void TrySetManualEntryText_NoOpsForWrongAuthor()
    {
        var store = new HistoryStore();
        var id = Guid.NewGuid();
        store.TryAddEntry(new HistoryEntry
        {
            Kind = HistoryEventKind.Manual, ActorName = "Alice", Detail = "original",
            InGameDate = "Year 1, Day 1", EntryId = id,
        });

        Assert.False(store.TrySetManualEntryText(id, "Bob", "tampered"));
        Assert.Equal("original", store.Entries[0].Detail);
    }

    [Fact]
    public void TrySetManualEntryText_NoOpsForUnknownEntryId()
    {
        var store = new HistoryStore();
        Assert.False(store.TrySetManualEntryText(Guid.NewGuid(), "Alice", "text"));
    }

    [Fact]
    public void TrySetManualEntryText_ClampsToMaxTaskTextLength()
    {
        var store = new HistoryStore();
        var id = Guid.NewGuid();
        store.TryAddEntry(new HistoryEntry
        {
            Kind = HistoryEventKind.Manual, ActorName = "Alice", Detail = "",
            InGameDate = "Year 1, Day 1", EntryId = id,
        });

        string tooLong = new string('x', ScribeDocumentCodec.MaxTaskTextLength + 500);
        store.TrySetManualEntryText(id, "Alice", tooLong);
        Assert.Equal(ScribeDocumentCodec.MaxTaskTextLength, store.Entries[0].Detail.Length);
    }

    [Fact]
    public void TryDeleteManualEntry_SucceedsForOwnEntry()
    {
        var store = new HistoryStore();
        var id = Guid.NewGuid();
        store.TryAddEntry(new HistoryEntry
        {
            Kind = HistoryEventKind.Manual, ActorName = "Alice", Detail = "text",
            InGameDate = "Year 1, Day 1", EntryId = id,
        });

        Assert.True(store.TryDeleteManualEntry(id, "Alice"));
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void TryDeleteManualEntry_NoOpsForWrongAuthor()
    {
        var store = new HistoryStore();
        var id = Guid.NewGuid();
        store.TryAddEntry(new HistoryEntry
        {
            Kind = HistoryEventKind.Manual, ActorName = "Alice", Detail = "text",
            InGameDate = "Year 1, Day 1", EntryId = id,
        });

        Assert.False(store.TryDeleteManualEntry(id, "Bob"));
        Assert.Single(store.Entries);
    }

    [Fact]
    public void TryDeleteManualEntry_NoOpsForUnknownEntryId()
    {
        var store = new HistoryStore();
        Assert.False(store.TryDeleteManualEntry(Guid.NewGuid(), "Alice"));
    }

    // ---- Manual: EntryId round-trips through the v2 codec ----

    [Fact]
    public void Manual_EntryIdRoundTripsThroughCodec()
    {
        var store = new HistoryStore();
        var id = Guid.NewGuid();
        store.TryAddEntry(new HistoryEntry
        {
            Kind = HistoryEventKind.Manual, ActorName = "Alice", Detail = "text",
            InGameDate = "Year 1, Day 1", EntryId = id,
        });

        var restored = HistoryStore.Deserialize(store.Serialize());
        Assert.Single(restored.Entries);
        Assert.Equal(id, restored.Entries[0].EntryId);
    }

    [Fact]
    public void NonManualEntry_EntryIdRoundTripsAsEmpty()
    {
        var store = new HistoryStore();
        store.TryAddEntry(Entry(HistoryEventKind.Death, "Alice", "died"));

        var restored = HistoryStore.Deserialize(store.Serialize());
        Assert.Equal(Guid.Empty, restored.Entries[0].EntryId);
    }

    // ---- v1 -> v2 migration ----

    [Fact]
    public void Deserialize_V1Payload_MigratesWithEmptyEntryIds()
    {
        // Hand-build a v1 payload: magic, version=1, count=1, one Death entry with the OLD field
        // layout (no EntryId bytes at all).
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SHST"u8.ToArray());
            w.Write((byte)1); // v1
            w.Write(1);       // entryCount
            w.Write((byte)HistoryEventKind.Death);
            w.Write("Alice");
            w.Write("Alice fell to her death.");
            w.Write("Year 1, Day 1");
            // no EntryId bytes in v1
        }

        var store = HistoryStore.Deserialize(ms.ToArray());
        Assert.Single(store.Entries);
        Assert.Equal(HistoryEventKind.Death, store.Entries[0].Kind);
        Assert.Equal(Guid.Empty, store.Entries[0].EntryId);
    }

    // ---- Mixed kinds don't interfere with each other's caps ----

    [Fact]
    public void SlidingWindowPerKind_DoesNotDropOtherKinds()
    {
        var store = new HistoryStore();
        store.TryAddEntry(Entry(HistoryEventKind.Crafted, "Alice"));
        for (int i = 0; i < HistoryStore.MaxDeaths; i++)
            store.TryAddEntry(Entry(HistoryEventKind.Death, "Alice", $"death {i}"));

        // Overflow Deaths — should not touch Crafted entry
        store.TryAddEntry(Entry(HistoryEventKind.Death, "Alice", "death overflow"));

        Assert.Contains(store.Entries, e => e.Kind == HistoryEventKind.Crafted);
        Assert.Equal(HistoryStore.MaxDeaths, store.Entries.Count(e => e.Kind == HistoryEventKind.Death));
    }
}
