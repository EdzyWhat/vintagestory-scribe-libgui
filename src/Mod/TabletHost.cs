using Scribe.Core;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// <see cref="IScribeDocumentHost"/> adapter for the player-held clay/wax tablet item — a thin variant
/// of <see cref="NotebookHost"/>. It reuses the notebook host's server write-through, history, and
/// first-pickup machinery verbatim (the tablet persists a document + docId on its ItemStack exactly
/// like the notebook, through the same <see cref="ScribeDocumentAttributes"/> and the frozen
/// <c>ScribeNotebookSaveMessage</c> packet — no new persistence code, no new packet). It differs only
/// in the fallback title and the tier cap:
/// <list type="bullet">
/// <item><see cref="DefaultDocumentTitle"/> is <c>"Tablet"</c> instead of <c>"Notebook"</c>.</item>
/// <item><see cref="Policy"/> reports <see cref="ScribeDocumentPolicy.Tablet"/> (at most 10 task blocks,
/// 1 pin); the dialog consults it at the editor's add/pin mutation boundary. The Lectern/Notebook hosts
/// keep the default uncapped policy, so this caps the tablet tier without touching them.</item>
/// </list>
///
/// <para>Layout aspect is inherited unchanged (<c>1160/1024</c>, same as the notebook), and the interim
/// backdrop is the notebook page: a tablet crafted in this change opens the existing
/// <c>GuiDialogScribeNotebook</c> so it is testable before the bespoke tablet dialog (Proposal C) exists.
/// Proposal C supplies the tablet theme + backdrop.</para>
/// </summary>
public sealed class TabletHost : NotebookHost
{
    /// <summary>The player's held tablet slot, kept so <see cref="Policy"/> can read the live stack's
    /// hard/fired state. The base <see cref="NotebookHost"/> keeps its own private copy for its
    /// write-through; this is a second reference to the same slot (no new state, just visibility).</summary>
    private readonly ItemSlot _slot;

    /// <param name="slot">The player's held tablet slot. Passed straight to the base
    /// <see cref="NotebookHost"/> ctor, which reads/initializes the document + history on the stack.</param>
    /// <param name="backdrop">Interim backdrop; defaults to the notebook page since this change reuses the
    /// notebook dialog. Proposal C passes the tablet's own backdrop.</param>
    public TabletHost(ItemSlot slot, ScribeBackdropSpec? backdrop = null) : base(slot, backdrop)
    {
        _slot = slot;
    }

    public override string DefaultDocumentTitle => "Tablet";

    /// <summary>The document policy enforced at the editor's add/pin mutation boundary. A WET tablet reports
    /// the scratch-tier cap (10 task blocks, 1 pin); a HARD or FIRED tablet reports
    /// <see cref="ScribeDocumentPolicy.UneditableTablet"/> so <c>CanAdd</c>/<c>CanPin</c> deny outright
    /// (tablet-firing Decision 8). This is the policy half of the same read-only switch the dialog keys off
    /// <see cref="ItemScribeTablet.IsEditable"/> — belt-and-suspenders with the dialog dropping every edit
    /// affordance, so a mutation can't slip through even if some path reached the boundary.</summary>
    public override ScribeDocumentPolicy Policy =>
        ItemScribeTablet.IsEditable(_slot.Itemstack)
            ? ScribeDocumentPolicy.Tablet
            : ScribeDocumentPolicy.UneditableTablet;

    /// <summary>Re-proportion the tablet dialog to fill more of the clay: a shorter title band
    /// (<c>0.11</c> vs the <c>0.13</c> default), a taller scrolling inner section (<c>0.83</c> vs
    /// <c>0.80</c>), and narrower side margins (<c>0.06</c> per side vs <c>0.10</c>) so the center writing
    /// column grows to <c>0.88·W</c>. Aspect is unchanged (the tablet reuses the notebook art's
    /// <c>1160/1024</c>); only the interior proportions differ. Overrides <see cref="NotebookHost.GetLayout"/>
    /// so the notebook's own layout is untouched.</summary>
    public override ScribeLayout GetLayout(float pixelArtSize) =>
        new ScribeLayout(pixelArtSize, 1160f / 1024f, ScribeLayoutProportions.Default with
        {
            TitleBarFrac = 0.11f,
            InnerHFrac   = 0.83f,
            SideColFrac  = 0.06f,
        });
}
