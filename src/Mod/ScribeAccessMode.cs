namespace Scribe;

/// <summary>
/// A placed writing station's persisted per-block access mode, distinct from the transient single-editor
/// lock. Shared by every <see cref="BlockEntityScribeWritingStation"/>; synced server-authoritatively.
/// </summary>
public enum ScribeAccessMode : byte
{
    /// <summary>Anyone may edit (subject to the editor lock). The default.</summary>
    Public = 0,

    /// <summary>Reserved for a future private / read-only permission. Not wired: it round-trips and syncs, but
    /// nothing sets or reads it, so every block behaves as <see cref="Public"/>.</summary>
    Private = 1,
}
