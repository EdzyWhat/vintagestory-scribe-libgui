using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

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

    /// <summary>The tablet's BASE material (<c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c>/<c>wax</c>),
    /// threaded from the open path so <see cref="GetLayout"/> can pick per-material interior proportions.
    /// The clay materials share one layout; <c>wax</c> nestles tighter to fit its own GUI art panel, whose
    /// beige writing area is inset further from the frame than the clay backdrops' (see the wax numbers in
    /// <see cref="GetLayout"/>). Null falls back to the clay layout.</summary>
    private readonly string? _material;

    /// <param name="slot">The player's held tablet slot. Passed straight to the base
    /// <see cref="NotebookHost"/> ctor, which reads/initializes the document + history on the stack.</param>
    /// <param name="backdrop">Interim backdrop; defaults to the notebook page since this change reuses the
    /// notebook dialog. Proposal C passes the tablet's own backdrop.</param>
    /// <param name="material">The tablet's base material, used only to pick the interior layout proportions.</param>
    public TabletHost(ItemSlot slot, ScribeBackdropSpec? backdrop = null, string? material = null) : base(slot, backdrop)
    {
        _slot = slot;
        _material = material;
    }

    public override string DefaultDocumentTitle => Lang.Get("scribe:doctitle-tablet");

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

    /// <summary>Re-proportion the tablet dialog per material so the content nestles inside that material's
    /// own GUI art panel. Aspect is unchanged (both reuse the notebook art's <c>1160/1024</c>); only the
    /// interior proportions differ. Overrides <see cref="NotebookHost.GetLayout"/> so the notebook's own
    /// layout is untouched.
    ///
    /// <para><b>Clay</b> fills more of the wide clay backdrop: a shorter title band (<c>0.11</c> vs the
    /// <c>0.13</c> default), a taller scrolling inner section (<c>0.83</c>), and narrow <c>0.06</c> side
    /// margins so the writing column grows to <c>0.88·W</c>.</para>
    ///
    /// <para><b>Wax</b> nestles tighter, because the bespoke wax art frames a smaller beige writing panel than
    /// the clay backdrops do. The gutters are deliberately NOT equal on both axes: the dialog art is taller
    /// than wide (<c>1160/1024</c>), so an equal fractional inset would read as a visually wider gap
    /// vertically than horizontally. Wax uses <c>SideColFrac = 0.08</c> (content column ≈<c>0.84·W</c>) for the
    /// horizontal gutter, and a taller title band (<c>0.15</c>) over a shorter inner section (<c>0.775</c>) so
    /// the vertical content is proportionally inset a little more than the sides — the two read as an even
    /// buffer by eye even though the fractions differ.</para></summary>
    public override ScribeLayout GetLayout(float pixelArtSize)
    {
        var props = _material == "wax"
            ? ScribeLayoutProportions.Default with
              {
                  TitleBarFrac = 0.15f,
                  InnerHFrac   = 0.775f,
                  SideColFrac  = 0.08f,
              }
            : ScribeLayoutProportions.Default with
              {
                  TitleBarFrac = 0.11f,
                  InnerHFrac   = 0.83f,
                  SideColFrac  = 0.06f,
              };
        return new ScribeLayout(pixelArtSize, 1160f / 1024f, props);
    }
}
