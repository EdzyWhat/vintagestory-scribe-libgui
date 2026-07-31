using ProtoBuf;
using Scribe.Core;

namespace Scribe;

/// <summary>Client → server: start or restart the player's timer.</summary>
[ProtoContract]
public sealed class ScribeSetTimerMessage
{
    [ProtoMember(1)] public double    DurationSeconds { get; set; }
    [ProtoMember(2)] public string?   Label           { get; set; }
    [ProtoMember(3)] public TimerMode Mode            { get; set; }
}

/// <summary>Client → server: stop and clear the player's timer (any state).</summary>
[ProtoContract]
public sealed class ScribeClearTimerMessage { }

/// <summary>Server → client: authoritative timer snapshot pushed on every state change.</summary>
[ProtoContract]
public sealed class ScribeTimerStateMessage
{
    [ProtoMember(1)] public TimerStatus Status           { get; set; }
    [ProtoMember(2)] public TimerMode   Mode             { get; set; }
    [ProtoMember(3)] public string?     Label            { get; set; }
    [ProtoMember(4)] public double      RemainingSeconds { get; set; }
}
