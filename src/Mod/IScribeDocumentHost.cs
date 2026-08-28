using System;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// Per-item proportions that parameterise <see cref="ScribeLayout"/>. All values are fractions of
/// the outer box dimensions. The <see cref="Default"/> singleton reproduces the Lectern's v1 layout.
/// A future item overrides only the fields it needs: <c>ScribeLayoutProportions.Default with { SideColFrac = 0.12f }</c>.
/// </summary>
public readonly record struct ScribeLayoutProportions
{
    public float TitleBarFrac { get; init; }
    public float InnerHFrac   { get; init; }
    public float SideColFrac  { get; init; }
    public float TitleBtnsWFrac { get; init; }
    public float TitleBtnsHFrac { get; init; }

    public ScribeLayoutProportions()
    {
        TitleBarFrac   = 0.13f;
        InnerHFrac     = 0.80f;
        SideColFrac    = 0.10f;
        TitleBtnsWFrac = 0.80f;
        TitleBtnsHFrac = 0.065f;
    }

    public static readonly ScribeLayoutProportions Default = new();
}

/// <summary>
/// The generalised dialog layout driven by one outer width <paramref name="W"/>, an art aspect ratio
/// <paramref name="AspectH"/>, and an optional <see cref="ScribeLayoutProportions"/> override.
/// Replaces the Lectern-only <c>LecternLayout</c>; all property formulas are identical when
/// <c>AspectH = 1160f/1024f</c> and <c>Props = ScribeLayoutProportions.Default</c>.
/// </summary>
public readonly record struct ScribeLayout(float W, float AspectH, ScribeLayoutProportions? Props = null)
{
    private ScribeLayoutProportions P => Props ?? ScribeLayoutProportions.Default;

    /// <summary>Outer box height derived from the art's aspect ratio.</summary>
    public float H => W * AspectH;

    /// <summary>The draggable title-bar band.</summary>
    public float TitleBarH => P.TitleBarFrac * H;

    /// <summary>The full outer width used as the inner section width (all three columns sum to W).</summary>
    public float InnerW => W;

    /// <summary>Inner section height (80 % of outer height by default).</summary>
    public float InnerH => P.InnerHFrac * H;

    /// <summary>Each side spacer / nav column.</summary>
    public float SideColW => P.SideColFrac * W;

    /// <summary>The centre tasks column: InnerW minus both side columns.</summary>
    public float TasksColW => (1f - 2f * P.SideColFrac) * W;

    /// <summary>Bottom-anchored title+buttons row width.</summary>
    public float TitleBtnsW => P.TitleBtnsWFrac * W;

    /// <summary>Bottom-anchored title+buttons row height.</summary>
    public float TitleBtnsH => P.TitleBtnsHFrac * H;
}

/// <summary>
/// The minimal surface the dialog layer needs from any Scribe block entity. Implemented by
/// <see cref="BlockEntityScribeLectern"/> today; the Notebook and Desk will implement it
/// without inheriting from the Lectern. The dialog operates entirely against this interface
/// — it never holds a concrete block-entity reference.
/// </summary>
public interface IScribeDocumentHost
{
    /// <summary>The server-authoritative document shown in the read and pin views.</summary>
    ScribeDocument Document { get; }

    /// <summary>True when another player holds the editor lock on this block.</summary>
    bool IsLockedByOther(string viewerUid);

    /// <summary>Update the client-side cache with a fresh optimistic copy after a flush.</summary>
    void ApplyLocalOptimisticEdit(ScribeDocument doc);

    /// <summary>Backdrop spec for this item's page (notebook art texture + fallback colour).</summary>
    ScribeBackdropSpec BackdropSpec { get; }

    /// <summary>Compute the dialog layout for a given pixel-art size setting.</summary>
    ScribeLayout GetLayout(float pixelArtSize);

    /// <summary>Item-specific fallback title when the player clears the title and saves
    /// (e.g. <c>"Lectern"</c>, <c>"Notebook"</c>). Distinct from <see cref="ScribeDocument.DefaultTitle"/>
    /// (<c>"Untitled"</c>) which is a Core-layer constant used only by the codec.</summary>
    string DefaultDocumentTitle { get; }

    /// <summary>The guestbook for this block — visitor entries recorded server-side on GUI open.</summary>
    GuestbookStore Guestbook { get; }

    /// <summary>The per-tier cap this host enforces on its document (max task blocks, max pins,
    /// read-only). Defaults to <see cref="ScribeDocumentPolicy.Unlimited"/> so every existing host
    /// (Lectern, Notebook, Clockmaker's) is uncapped and behaviorally unchanged; only the tablet tier
    /// (<see cref="TabletHost"/>) overrides it. The dialog consults <see cref="ScribeDocumentPolicy.CanAdd"/>
    /// before adding a task and <see cref="ScribeDocumentPolicy.CanPin"/> before pinning one, so the cap
    /// is applied at the editor mutation boundary rather than inside the shared <see cref="ScribeDocument"/>
    /// model (scribe-document-policy).</summary>
    ScribeDocumentPolicy Policy => ScribeDocumentPolicy.Unlimited;

    // ── Server-side document write-through (lock-free, called from completion/pin handlers) ──

    /// <summary>Server-side: set a task's done flag by stable TaskId. No-op if not found.
    /// For block hosts: marks dirty and resyncs. For item hosts: writes to the ItemStack.</summary>
    void SetTaskDoneFromReader(Guid taskId, bool done);

    /// <summary>Server-side: delete a task by stable TaskId. Returns true if removed.
    /// For block hosts: marks dirty and resyncs. For item hosts: writes to the ItemStack.</summary>
    bool DeleteTaskFromReader(Guid taskId);

    /// <summary>Server-side: persist the in-memory document after a Core mutation (range complete/delete)
    /// already applied to <see cref="Document"/>. For block hosts: marks dirty and resyncs. For item hosts:
    /// writes to the ItemStack.</summary>
    void PersistFromReader();

    /// <summary>Server-side: move a task to the bottom of the document. Returns true if moved.
    /// For block hosts: marks dirty and resyncs. For item hosts: writes to the ItemStack.</summary>
    bool MoveTaskToBottomFromReader(Guid taskId);

    /// <summary>Server-side: set a task's text by stable TaskId. Returns true if changed.
    /// Rejects blank/whitespace. For block hosts: marks dirty and resyncs. For item hosts: writes to the ItemStack.</summary>
    bool SetTaskTextFromReader(Guid taskId, string text);

    /// <summary>Server-side: set a Tracker's live <c>CurrentQuantity</c> by stable TaskId (clamped only to
    /// ≥ 0 by the Core model — overflow above the target is preserved, 7.14). Returns true if it changed. The client-side count
    /// engine drives this from the viewing player's carried inventory (add-tracker-link-tasks D5/4.3), so
    /// the count is server-persisted and multiplayer viewers converge — exactly like the Done flag.
    /// No-op for a non-Tracker or unknown TaskId. For block hosts: marks dirty and resyncs. For item hosts:
    /// writes to the ItemStack.</summary>
    bool SetTrackerCurrentQuantityFromReader(Guid taskId, int currentQuantity);
}
