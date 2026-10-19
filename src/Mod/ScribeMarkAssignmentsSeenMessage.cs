using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: mark every one of the sender's currently-unseen received assignments as seen
/// (opening the Inbox flips it server-side). No payload — the server derives the
/// viewing player from the authenticated sender and marks against <see cref="ScribeAssignmentStore"/>
/// directly, like the empty-marker <c>ScribeClearTimerMessage</c>.</summary>
[ProtoContract]
public sealed class ScribeMarkAssignmentsSeenMessage { }
