namespace Scribe;

/// <summary>
/// The 5 independently tunable box targets of <see cref="ScribeBoxTuning"/>, resolved per
/// <see cref="BlockEntityScribeWritingStation"/> via its <c>TuningTarget</c> hook. <see cref="Chalkboard"/>
/// tunes only its selection box (see <see cref="ScribeBoxTuning.HasCollisionBox"/>).
/// </summary>
public enum ScribeBoxTuningTarget
{
    Inbox,
    InboxWall,
    Scriptorium,
    AssignmentDesk,
    Chalkboard,
}
