using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server -&gt; client: delivers the recipient player their own Scribe settings (e.g. whether
/// completing a task unpins it). The payload is a <c>ScribePinCodec</c> SPSE settings blob, decoded
/// with <c>ScribePinCodec.TryDeserializeSettings</c>. Sent on join and re-sent when the player's
/// settings change. The settings are held server-side because the server enforces the
/// complete-to-unpin behavior; the client caches them for display and to feed a future settings UI.
/// </summary>
[ProtoContract]
public sealed class ScribePlayerSettingsMessage
{
    /// <summary>A <c>ScribePinCodec.SerializeSettings</c> blob of the player's settings.</summary>
    [ProtoMember(1)]
    public byte[]? SettingsBytes { get; set; }
}
