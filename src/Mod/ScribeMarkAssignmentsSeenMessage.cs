using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: mark every one of the sender's currently-unseen received assignments as seen
/// (opening the Inbox). No payload: the server derives the
/// player from the authenticated sender and marks against <see cref="ScribeAssignmentStore"/>.</summary>
[ProtoContract]
public sealed class ScribeMarkAssignmentsSeenMessage { }
