using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// The Assignment Desk's placed-block entity (add-assignment-and-quest-support §5). A thin subclass of
/// <see cref="BlockEntityScribeWritingStation"/>: it shares all document, persistence, editor-lock, and
/// guestbook logic with the Lectern/Scriptorium and supplies only its own identity/config.
///
/// <para>Reuses the Lectern's GUI page art as a placeholder backdrop (§13.1 tracks the dedicated
/// Assignment Desk art), and its physical block model/textures are cloned from the Scriptorium's shape
/// (see <c>assignmentdesk.json</c>) pending its own §13.1 asset — the same "placeholder now, restyle
/// later" precedent the Scriptorium itself set for its GUI backdrop.</para>
///
/// <para><see cref="PageAspect"/> is fixed at the design's Decision 8 ratio (<c>W × 1.2W</c>) rather than
/// an art-derived value — this dialog's layout is not blocked on final art (see the
/// <c>assignment-desk-block</c> spec and design.md Decision 8).</para>
/// </summary>
public sealed class BlockEntityAssignmentDesk : BlockEntityScribeWritingStation
{
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    protected override float PageAspect => 1.2f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-assignmentdesk";

    protected override string MeshCacheKeyPrefix => "scribeassignmentdeskmesh";

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeAssignmentDesk(Pos, this, capi);
}
