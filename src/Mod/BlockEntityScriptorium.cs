using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// The Scriptorium's placed-block entity — the third placed writing-station tier after the Lectern
/// (v1.2, see <c>docs/specs/v7-scriptorium-and-task-types.md</c>). A thin subclass of
/// <see cref="BlockEntityScribeWritingStation"/>: it shares all document, persistence, editor-lock,
/// guestbook, and placement logic with the Lectern and supplies only its own identity/config.
///
/// <para>For v1.2 it reuses the Lectern's GUI page backdrop as a placeholder (the dedicated
/// Scriptorium art/backdrop is a tracked follow-up), and its dialog is a distinct subclass so the
/// v1.3 assignment system can attach the Scriptorium-only Assign &amp; History / Inbox nav buttons
/// without touching the Lectern.</para>
/// </summary>
public sealed class BlockEntityScriptorium : BlockEntityScribeWritingStation
{
    // Placeholder: reuse the Lectern page art until the dedicated Scriptorium backdrop is authored
    // (add-scriptorium-block design, Decision 3). Same 1024×1160 art size, so the same aspect.
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    protected override float PageAspect => 1160f / 1024f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-scriptorium";

    protected override string MeshCacheKeyPrefix => "scribescriptoriummesh";

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeScriptorium(Pos, this, capi);
}
