using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// The standalone Inbox block's placed-block entity (add-assignment-and-quest-support §6). A thin
/// subclass of <see cref="BlockEntityScribeWritingStation"/>: it shares all document, persistence,
/// editor-lock, and guestbook logic with the Lectern/Scriptorium/Assignment Desk and supplies only its
/// own identity/config.
///
/// <para>Reuses the Lectern's GUI page art as a placeholder backdrop (§13.2 tracks the dedicated Inbox
/// art), and its physical block model/textures are cloned from the Scriptorium's shape (see
/// <c>inbox.json</c>) pending its own §13.2 asset.</para>
///
/// <para><see cref="PageAspect"/> is fixed at the design's Decision 8 ratio (<c>W × 1.2W</c>), same as
/// <see cref="BlockEntityAssignmentDesk"/> — see design.md Decision 8.</para>
/// </summary>
public sealed class BlockEntityInbox : BlockEntityScribeWritingStation
{
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    protected override float PageAspect => 1.2f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-inbox";

    protected override string MeshCacheKeyPrefix => "scribeinboxmesh";

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeInbox(Pos, this, capi);
}
