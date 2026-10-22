namespace Scribe.Core;

/// <summary>
/// Serializes the per-player quest-decision ledger for network sync and save-game persistence, in the
/// same fail-safe versioned binary form as <see cref="ScribePinCodec"/> (malformed input returns false,
/// never throws or over-allocates). See docs/CODEC-MIGRATION.md.
///
/// Two blobs, each with a 4-byte magic and 1-byte version:
///   SQDL — one player's <see cref="ScribeQuestDecisionEntry"/> list (server→client push).
///   SQDS — the whole store, <c>Dictionary&lt;playerUid, List&lt;ScribeQuestDecisionEntry&gt;&gt;</c> (savegame).
/// </summary>
public static class ScribeQuestDecisionCodec
{
    private static readonly byte[] ListMagic = "SQDL"u8.ToArray();
    private static readonly byte[] StoreMagic = "SQDS"u8.ToArray();

    private const byte Version = 1;
    private const byte MinVersion = 1;

    /// <summary>Upper bound on a uid/source/quest-code string length, in characters (allocation guard).</summary>
    public const int MaxStringLength = 512;

    // ---- SQDL: one player's decision list (network) ----

    public static byte[] SerializeList(IReadOnlyList<ScribeQuestDecisionEntry> entries)
        => ScribePlayerListBlob.SerializeList(ListMagic, Version, entries, WriteEntryList);

    public static bool TryDeserializeList(byte[]? bytes, out List<ScribeQuestDecisionEntry>? entries)
        => ScribePlayerListBlob.TryDeserializeList<ScribeQuestDecisionEntry>(bytes, ListMagic, MinVersion, Version,
            ReadEntryList, out entries);

    // ---- SQDS: the whole store (savegame) ----

    public static byte[] SerializeStore(IReadOnlyDictionary<string, List<ScribeQuestDecisionEntry>> store)
        => ScribePlayerListBlob.SerializeStore(StoreMagic, Version, store, WriteEntryList);

    public static bool TryDeserializeStore(byte[]? bytes, out Dictionary<string, List<ScribeQuestDecisionEntry>>? store)
        => ScribePlayerListBlob.TryDeserializeStore<ScribeQuestDecisionEntry>(bytes, StoreMagic, MinVersion, Version,
            MaxStringLength, ReadEntryList, out store);

    // ---- shared helpers ----

    private static void WriteEntryList(BinaryWriter w, IReadOnlyList<ScribeQuestDecisionEntry> entries)
    {
        w.Write(entries.Count);
        foreach (var e in entries)
        {
            w.Write(e.Source);
            w.Write(e.QuestCode);
            w.Write(e.IsCompletion);
            w.Write((byte)e.Decision);
        }
    }

    private static bool ReadEntryList(BinaryReader r, int totalBytes, byte version, out List<ScribeQuestDecisionEntry> entries)
    {
        entries = new List<ScribeQuestDecisionEntry>();
        int count = r.ReadInt32();
        if (count < 0 || count > totalBytes || count > ScribeQuestDecisionSet.MaxEntriesPerPlayer) return false;

        var list = new List<ScribeQuestDecisionEntry>(count);
        for (int i = 0; i < count; i++)
        {
            string source = r.ReadString();
            if (source.Length > MaxStringLength) return false;
            string questCode = r.ReadString();
            if (questCode.Length > MaxStringLength) return false;
            bool isCompletion = r.ReadBoolean();
            var decision = (ScribeQuestDecision)r.ReadByte();

            list.Add(new ScribeQuestDecisionEntry
            {
                Source = source,
                QuestCode = questCode,
                IsCompletion = isCompletion,
                Decision = decision,
            });
        }
        entries = list;
        return true;
    }
}
