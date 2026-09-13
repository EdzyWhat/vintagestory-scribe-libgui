using Scribe.Core;

namespace Scribe.Core.Tests;

public class PendingHistoryStoreTests
{
    private static HistoryEntry Entry(string detail)
        => new() { Kind = HistoryEventKind.Death, Detail = detail, InGameDate = "Year 1, Day 1", InGameTimestamp = 5 };

    [Fact]
    public void TakeAll_OnUnknownDocId_ReturnsEmpty()
    {
        var store = new PendingHistoryStore();
        Assert.Empty(store.TakeAll(Guid.NewGuid()));
    }

    [Fact]
    public void Enqueue_ThenTakeAll_ReturnsAndClears()
    {
        var store = new PendingHistoryStore();
        var docId = Guid.NewGuid();
        store.Enqueue(docId, Entry("first"));
        store.Enqueue(docId, Entry("second"));

        var taken = store.TakeAll(docId);
        Assert.Equal(new[] { "first", "second" }, taken.Select(e => e.Detail));

        // Taking again returns nothing — TakeAll removes what it returns.
        Assert.Empty(store.TakeAll(docId));
    }

    [Fact]
    public void Enqueue_KeepsDocumentsIndependent()
    {
        var store = new PendingHistoryStore();
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        store.Enqueue(docA, Entry("for A"));
        store.Enqueue(docB, Entry("for B"));

        Assert.Equal("for A", Assert.Single(store.TakeAll(docA)).Detail);
        Assert.Equal("for B", Assert.Single(store.TakeAll(docB)).Detail);
    }

    // ---- Round-trip ----

    [Fact]
    public void RoundTrip_EmptyStore()
    {
        var store = new PendingHistoryStore();
        var restored = new PendingHistoryStore();
        restored.LoadFrom(store.SerializeStore());
        Assert.Equal(0, restored.PendingDocumentCount);
    }

    [Fact]
    public void RoundTrip_OneDocument_OneEntry()
    {
        var store = new PendingHistoryStore();
        var docId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        store.Enqueue(docId, new HistoryEntry
        {
            Kind = HistoryEventKind.Death, ActorName = "", Detail = "Alice was slain by Bob.",
            InGameDate = "Year 1, Day 3", InGameTimestamp = 3.5, EntryId = entryId,
        });

        var restored = new PendingHistoryStore();
        restored.LoadFrom(store.SerializeStore());

        var taken = restored.TakeAll(docId);
        var entry = Assert.Single(taken);
        Assert.Equal(HistoryEventKind.Death, entry.Kind);
        Assert.Equal("Alice was slain by Bob.", entry.Detail);
        Assert.Equal("Year 1, Day 3", entry.InGameDate);
        Assert.Equal(3.5, entry.InGameTimestamp);
        Assert.Equal(entryId, entry.EntryId);
    }

    [Fact]
    public void RoundTrip_MultipleDocuments_MultipleEntriesEach()
    {
        var store = new PendingHistoryStore();
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        store.Enqueue(docA, Entry("A1"));
        store.Enqueue(docA, Entry("A2"));
        store.Enqueue(docB, Entry("B1"));

        var restored = new PendingHistoryStore();
        restored.LoadFrom(store.SerializeStore());

        Assert.Equal(2, restored.PendingDocumentCount);
        Assert.Equal(new[] { "A1", "A2" }, restored.TakeAll(docA).Select(e => e.Detail));
        Assert.Equal(new[] { "B1" }, restored.TakeAll(docB).Select(e => e.Detail));
    }

    [Fact]
    public void LoadFrom_NullBytes_LeavesStoreEmpty()
    {
        var store = new PendingHistoryStore();
        store.Enqueue(Guid.NewGuid(), Entry("stale"));
        store.LoadFrom(null);
        Assert.Equal(0, store.PendingDocumentCount);
    }

    [Fact]
    public void LoadFrom_MalformedBytes_LeavesStoreEmpty()
    {
        var store = new PendingHistoryStore();
        store.LoadFrom(new byte[] { 0x01, 0x02, 0x03 });
        Assert.Equal(0, store.PendingDocumentCount);
    }

    [Fact]
    public void RoundTrip_LiveFacts_Preserved()
    {
        var store = new PendingHistoryStore();
        var docId = Guid.NewGuid();
        store.Enqueue(docId, new HistoryEntry
        {
            Kind = HistoryEventKind.Death,
            Schema = HistorySchema.Live,
            SubjectName = "Alice",
            RefCode = "game:wolf-eurasian-adult-male",
            FlavorSeed = 9,
            InGameTimestamp = 3.5,
        });

        var restored = new PendingHistoryStore();
        restored.LoadFrom(store.SerializeStore());

        var entry = Assert.Single(restored.TakeAll(docId));
        Assert.Equal(HistorySchema.Live, entry.Schema);
        Assert.Equal("Alice", entry.SubjectName);
        Assert.Equal("game:wolf-eurasian-adult-male", entry.RefCode);
        Assert.Equal(9, entry.FlavorSeed);
        Assert.Equal("", entry.Detail);
    }

    [Fact]
    public void LoadFrom_V1Blob_LoadsAsBaked()
    {
        var docId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPHS"u8.ToArray());
            w.Write((byte)1);
            w.Write(1); // docCount
            w.Write(docId.ToByteArray());
            w.Write(1); // entryCount
            w.Write((byte)HistoryEventKind.Death);
            w.Write("");
            w.Write("Alice was slain by Bob.");
            w.Write("Year 1, Day 3");
            w.Write(entryId.ToByteArray());
            w.Write(3.5);
        }

        var store = new PendingHistoryStore();
        store.LoadFrom(ms.ToArray());

        var entry = Assert.Single(store.TakeAll(docId));
        Assert.Equal(HistoryEventKind.Death, entry.Kind);
        Assert.Equal("Alice was slain by Bob.", entry.Detail);
        Assert.Equal(HistorySchema.Baked, entry.Schema);
        Assert.Equal("", entry.SubjectName);
        Assert.Equal(3.5, entry.InGameTimestamp);
        Assert.Equal(entryId, entry.EntryId);
    }

    [Fact]
    public void LoadFrom_V3Blob_LeavesStoreEmpty()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPHS"u8.ToArray());
            w.Write((byte)3);
            w.Write(0);
        }

        var store = new PendingHistoryStore();
        store.Enqueue(Guid.NewGuid(), Entry("stale"));
        store.LoadFrom(ms.ToArray());
        Assert.Equal(0, store.PendingDocumentCount);
    }
}
