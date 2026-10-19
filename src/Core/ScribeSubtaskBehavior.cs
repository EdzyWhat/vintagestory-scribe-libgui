namespace Scribe.Core;

/// <summary>
/// What completing, sinking, or deleting a <b>parent</b> (a depth-0 row plus its contiguous depth-1
/// owned run) does to those children. A client-local per-player preference
/// (<see cref="ScribePlayerSettings.SubtaskBehavior"/>) sent with complete and standalone-delete
/// requests; the server normalizes unknown values to <see cref="Bound"/>. A depth-1 row is always a leaf.
/// </summary>
public enum ScribeSubtaskBehavior : byte
{
    /// <summary>Complete, sink, or trash the parent together with its owned run as one range
    /// (parent first, children in prior order). The default.</summary>
    Bound = 0,

    /// <summary>Mutate only the parent. Children stay put and may re-parent visually under the depth-0
    /// row now above them.</summary>
    Independent = 1,

    /// <summary>Remove the owned-run children, then apply the parent's completion policy to the
    /// parent alone. Unchecking the parent cannot restore the discarded children. Trash of a parent
    /// deletes the whole run (same document result as Bound trash).</summary>
    DiscardChildren = 2,
}
