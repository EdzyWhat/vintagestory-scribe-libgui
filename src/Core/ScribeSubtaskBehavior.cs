namespace Scribe.Core;

/// <summary>
/// What completing, sinking, or deleting a <b>parent</b> (a depth-0 row plus its contiguous depth-1
/// owned run) does to those children. A per-player, client-local preference
/// (<see cref="ScribePlayerSettings.SubtaskBehavior"/>): the client carries it on complete and
/// standalone-delete requests and the server normalizes it (unknown → <see cref="Bound"/>).
/// Completing a depth-1 row is always a leaf — this picker does not walk siblings.
/// </summary>
public enum ScribeSubtaskBehavior : byte
{
    /// <summary>Complete, sink, or trash the parent together with its owned run as one range
    /// (parent first, then children in their prior order). The default — the tree-like behavior.</summary>
    Bound = 0,

    /// <summary>Mutate only the parent. Children stay where they are (and may re-parent visually
    /// under whatever depth-0 is now above them).</summary>
    Independent = 1,

    /// <summary>Remove the owned-run children, then apply the parent's completion policy to the
    /// parent alone. Unchecking the parent cannot restore the discarded children. Trash of a parent
    /// deletes the whole run (same document result as Bound trash).</summary>
    DiscardChildren = 2,
}
