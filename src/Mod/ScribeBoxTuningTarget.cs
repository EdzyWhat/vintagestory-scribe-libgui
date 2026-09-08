namespace Scribe;

/// <summary>
/// The 5 independently tunable box targets exposed by <see cref="ScribeBoxTuning"/>
/// (add-scribe-block-box-tuning). Resolved per placed <see cref="BlockEntityScribeWritingStation"/>
/// instance via its <c>TuningTarget</c> hook. <see cref="Chalkboard"/> is the one target whose
/// collision box is NOT driven by tuning — see <see cref="ScribeBoxTuning.HasCollisionBox"/> — only
/// its selection box is.
/// </summary>
public enum ScribeBoxTuningTarget
{
    Inbox,
    InboxWall,
    Scriptorium,
    AssignmentDesk,
    Chalkboard,
}
