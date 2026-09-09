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
}
