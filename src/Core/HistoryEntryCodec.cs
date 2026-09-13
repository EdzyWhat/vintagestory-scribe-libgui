namespace Scribe.Core;

/// <summary>Shared per-entry binary layout for <see cref="HistoryStore"/> (<c>SHST</c>) and
/// <see cref="PendingHistoryStore"/> (<c>SPHS</c>), so the two blobs cannot drift. Progressive
/// reads: v1 = kind/actor/detail/date; v2 adds <c>EntryId</c>; v3 adds <c>InGameTimestamp</c>;
/// v4 adds schema + live fact fields. Writers always emit the current (v4) field set.</summary>
internal static class HistoryEntryCodec
{
    public static void WriteEntry(BinaryWriter w, HistoryEntry e)
    {
        w.Write((byte)e.Kind);
        w.Write(e.ActorName);
        w.Write(e.Detail);
        w.Write(e.InGameDate);
        w.Write(e.EntryId.ToByteArray());
        w.Write(e.InGameTimestamp);
        w.Write((byte)e.Schema);
        w.Write(e.SubjectName);
        w.Write(e.OtherName);
        w.Write(e.RefCode);
        w.Write(e.RefCode2);
        w.Write(e.FlavorSeed);
    }

    /// <summary>Reads one entry using the field set of <paramref name="version"/> (the SHST layout
    /// version, not the SPHS document version). Unread v4 fields keep their C# defaults (baked,
    /// empty facts).</summary>
    public static HistoryEntry ReadEntry(BinaryReader r, byte version)
    {
        var entry = new HistoryEntry
        {
            Kind       = (HistoryEventKind)r.ReadByte(),
            ActorName  = r.ReadString(),
            Detail     = r.ReadString(),
            InGameDate = r.ReadString(),
        };
        if (version >= 2) entry.EntryId = new Guid(r.ReadBytes(16));
        if (version >= 3) entry.InGameTimestamp = r.ReadDouble();
        if (version >= 4)
        {
            entry.Schema      = (HistorySchema)r.ReadByte();
            entry.SubjectName = r.ReadString();
            entry.OtherName   = r.ReadString();
            entry.RefCode     = r.ReadString();
            entry.RefCode2    = r.ReadString();
            entry.FlavorSeed  = r.ReadInt32();
        }
        return entry;
    }
}
