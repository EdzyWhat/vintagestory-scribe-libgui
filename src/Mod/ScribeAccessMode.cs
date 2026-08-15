namespace Scribe;

/// <summary>
/// A placed writing-station's durable access mode — a per-block permission governing who may edit
/// the document, distinct from the transient single-editor lock (the lock guards concurrent editing
/// within a session; the access mode is a persisted permission). Shared by every
/// <see cref="BlockEntityScribeWritingStation"/> (the Lectern and the Scriptorium). Persisted and
/// synced server-authoritatively via <c>ToTreeAttributes</c>/<c>FromTreeAttributes</c>.
/// </summary>
public enum ScribeAccessMode : byte
{
    /// <summary>Anyone may edit (subject only to the transient editor lock). The default for every
    /// placed or loaded writing station.</summary>
    Public = 0,

    /// <summary>Reserved for a future private / read-only permission (e.g. owner-only editing). Not
    /// wired in this version: the field round-trips and syncs, but no player-facing control sets it
    /// and the editor-entry gate does not read it, so every block behaves as <see cref="Public"/>.
    /// Defining it now lets a future change begin writing it without a breaking save-format change
    /// (mirrors the reserved <c>HistoryEventKind.LoreDiscovery</c> precedent).</summary>
    Private = 1,
}
