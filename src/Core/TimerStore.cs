using System.Text;

namespace Scribe.Core;

public enum TimerMode : byte { InGame = 0, RealTime = 1 }
public enum TimerStatus : byte { Idle = 0, Running = 1, Fired = 2 }

/// <summary>
/// Single-slot countdown timer belonging to one player. Persisted server-side in the savegame
/// (not on an item), so the timer runs regardless of where the Clockmaker's Notebook is.
///
/// Serialized format (STMR, little-endian via <see cref="BinaryWriter"/>):
///   [4 bytes magic "STMR"][1 byte version]
///   [byte status][byte mode][string label][double remainingSeconds]
///   v2+: [double firedElapsedSeconds]
///
/// Accepted-version window:
///   Current : v2   — appended <see cref="FiredElapsedSeconds"/> so a fired timer can be persisted
///                    across relog and resume (not restart) its auto-disappear window
///   Prior   : v1   — no fired-elapsed field; loads with FiredElapsedSeconds = 0
///   Older   : rejected
///
/// See docs/CODEC-MIGRATION.md for the version-bump pattern.
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

    /// <summary>Seconds this timer has spent in the <see cref="TimerStatus.Fired"/> state (0 while
    /// Idle/Running). The server accumulates it on its 1 Hz tick after the timer fires; it is the single
    /// persisted source of truth for how long a fired timer has been flashing, so a client-driven
    /// auto-disappear resumes rather than restarts across a relog (timer-auto-disappear-setting). Added
    /// in codec v2; a v1 blob loads it as 0.</summary>
    public double FiredElapsedSeconds { get; set; }

    /// <summary>How long (seconds) a fired timer flashes before it auto-disappears from the HUD, when the
    /// player's "Timer disappears" preference is on (<see cref="ScribePlayerSettings.TimerAutoDisappear"/>).
    /// The canonical home for the value that was previously a bare <c>30.0</c> literal in the server tick;
    /// the client compares <see cref="FiredElapsedSeconds"/> against it to decide when to auto-clear.</summary>
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
            // v2 appended fired-elapsed; a v1 blob has no such field, so it defaults to 0.
            store.FiredElapsedSeconds = version == Version ? r.ReadDouble() : 0.0;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            // Malformed — return default.
        }

        return store;
    }
}
