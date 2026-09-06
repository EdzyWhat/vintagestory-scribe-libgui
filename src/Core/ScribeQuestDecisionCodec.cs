using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Scribe.Core;

/// <summary>
/// Serializes the per-player quest-decision ledger to byte arrays and back, for both network sync and
/// save-game persistence — mirrors <see cref="ScribePinCodec"/>'s shape exactly (same fail-safe,
/// versioned binary form; a malformed/hostile payload returns false rather than throwing or
/// over-allocating).
///
/// Two blob shapes, each with its own 4-byte magic and 1-byte version:
///   SQDL — one player's <see cref="ScribeQuestDecisionEntry"/> list (the server→client per-player push).
///   SQDS — the whole store, <c>Dictionary&lt;playerUid, List&lt;ScribeQuestDecisionEntry&gt;&gt;</c>
///          (savegame blob).
///
/// Version 1 is the only shipped version so far; future fields append the same progressive-read pattern
/// <see cref="ScribePinCodec"/> documents (see docs/CODEC-MIGRATION.md).
/// </summary>
public static class ScribeQuestDecisionCodec
{
    private static readonly byte[] ListMagic = "SQDL"u8.ToArray();
    private static readonly byte[] StoreMagic = "SQDS"u8.ToArray();

    private const byte Version = 1;
    private const byte MinVersion = 1;

    /// <summary>Hard upper bound on the number of players in a persisted store blob — an allocation guard
    /// for the save-game read path.</summary>
    public const int MaxPlayers = 10_000;

    /// <summary>Hard upper bound on a player-uid/source/quest-code string length, in characters
    /// (allocation guard).</summary>
    public const int MaxStringLength = 512;

    // ---- SQDL: one player's decision list (network) ----

    public static byte[] SerializeList(IReadOnlyList<ScribeQuestDecisionEntry> entries)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(ListMagic);
            w.Write(Version);
            WriteEntryList(w, entries);
        }
        return ms.ToArray();
    }

    public static bool TryDeserializeList(byte[]? bytes, out List<ScribeQuestDecisionEntry>? entries)
    {
        entries = null;
        if (bytes is null) return false;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            int version = ReadHeader(r, ListMagic);
            if (version < MinVersion || version > Version) return false;
            if (!TryReadEntryList(r, bytes.Length, out var list)) return false;
            entries = list;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            entries = null;
            return false;
        }
    }

    // ---- SQDS: the whole store (savegame) ----

    public static byte[] SerializeStore(IReadOnlyDictionary<string, List<ScribeQuestDecisionEntry>> store)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(StoreMagic);
            w.Write(Version);
            w.Write(store.Count);
            foreach (var (uid, entries) in store)
            {
                w.Write(uid);
                WriteEntryList(w, entries);
            }
        }
        return ms.ToArray();
    }

    public static bool TryDeserializeStore(byte[]? bytes, out Dictionary<string, List<ScribeQuestDecisionEntry>>? store)
    {
        store = null;
        if (bytes is null) return false;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            int version = ReadHeader(r, StoreMagic);
            if (version < MinVersion || version > Version) return false;

            int playerCount = r.ReadInt32();
            if (playerCount < 0 || playerCount > bytes.Length || playerCount > MaxPlayers) return false;

            var result = new Dictionary<string, List<ScribeQuestDecisionEntry>>(playerCount);
            for (int i = 0; i < playerCount; i++)
            {
                string uid = r.ReadString();
                if (uid.Length > MaxStringLength) return false;
                if (!TryReadEntryList(r, bytes.Length, out var list)) return false;
                result[uid] = list;
            }
            store = result;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            store = null;
            return false;
        }
    }

    // ---- shared helpers ----

    private static int ReadHeader(BinaryReader r, byte[] expectedMagic)
    {
        var magic = r.ReadBytes(expectedMagic.Length);
        if (!magic.AsSpan().SequenceEqual(expectedMagic)) return -1;
        return r.ReadByte();
    }

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

    private static bool TryReadEntryList(BinaryReader r, int totalBytes, out List<ScribeQuestDecisionEntry> entries)
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
