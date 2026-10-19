using System.Text;

namespace Scribe.Core;

public enum TimerMode : byte { InGame = 0, RealTime = 1 }
public enum TimerStatus : byte { Idle = 0, Running = 1, Fired = 2 }

/// <summary>
/// Single-slot countdown timer belonging to one player. Persisted server-side in the savegame (not on an
/// item), so it runs regardless of where the Clockmaker's Notebook is.
///
/// Serialized format (STMR, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "STMR"][1 byte version]
///   [byte status][byte mode][string label][double remainingSeconds]
///   v2+: [double firedElapsedSeconds]
///
/// Accepted versions: v2 (current), v1 (loads with FiredElapsedSeconds = 0); older rejected.
/// See docs/CODEC-MIGRATION.md.
/// </summary>
public sealed class TimerStore
{
    private static readonly byte[] Magic = "STMR"u8.ToArray();
    private const byte Version      = 2;
    private const byte PriorVersion = 1;

    public TimerStatus Status           { get; set; } = TimerStatus.Idle;
    public TimerMode   Mode             { get; set; } = TimerMode.InGame;
    public string      Label            { get; set; } = "";
    public double      RemainingSeconds { get; set; }

    /// <summary>Seconds spent in the <see cref="TimerStatus.Fired"/> state (0 while Idle/Running). The server
    /// accumulates it on its 1 Hz tick; being persisted, the client's auto-disappear resumes across a relog.</summary>
    public double FiredElapsedSeconds { get; set; }

    /// <summary>Seconds a fired timer flashes before auto-disappearing from the HUD, when
    /// <see cref="ScribePlayerSettings.TimerAutoDisappear"/> is on.</summary>
    public const double FiredAutoClearSeconds = 30.0;


    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write((byte)Status);
            w.Write((byte)Mode);
            w.Write(Label);
            w.Write(RemainingSeconds);
            w.Write(FiredElapsedSeconds);
        }
        return ms.ToArray();
    }

    public static TimerStore Deserialize(byte[]? bytes)
    {
        var store = new TimerStore();
        if (bytes is null || bytes.Length < Magic.Length + 1) return store;

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r  = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var magic = r.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic)) return store;

            byte version = r.ReadByte();
            if (version != Version && version != PriorVersion) return store;

            store.Status           = (TimerStatus)r.ReadByte();
            store.Mode             = (TimerMode)r.ReadByte();
            store.Label            = r.ReadString();
            store.RemainingSeconds = r.ReadDouble();
            // v1 blobs have no fired-elapsed field.
            store.FiredElapsedSeconds = version == Version ? r.ReadDouble() : 0.0;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — return default.
        }

        return store;
    }
}
