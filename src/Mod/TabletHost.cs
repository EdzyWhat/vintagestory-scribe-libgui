using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>
/// <see cref="IScribeDocumentHost"/> adapter for the player-held clay/wax tablet, a thin variant of
/// <see cref="NotebookHost"/> that reuses its write-through, history, and persistence (same
/// <see cref="ScribeDocumentAttributes"/> and <c>ScribeNotebookSaveMessage</c> packet). It differs only in
/// the fallback title (<see cref="DefaultDocumentTitle"/>) and the tier cap (<see cref="Policy"/>:
/// <see cref="ScribeDocumentPolicy.Tablet"/>, at most 10 task blocks and 1 pin, consulted at the editor's
/// add/pin boundary).
///
/// <para>Layout aspect is inherited (<c>1160/1024</c>), as is the notebook-page default backdrop.</para>
/// </summary>
public sealed class TabletHost : NotebookHost
{
    /// <summary>The held tablet slot, kept so <see cref="Policy"/> can read the live stack's state (the base
    /// class's copy is private).</summary>
    private readonly ItemSlot _slot;

    /// <summary>The tablet's base material (<c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c>/<c>wax</c>),
    /// used by <see cref="GetLayout"/> to pick interior proportions. Null falls back to the clay layout.</summary>
    private readonly string? _material;

    /// <param name="slot">The player's held tablet slot, passed to the base ctor.</param>
    /// <param name="backdrop">The backdrop; defaults to the notebook page.</param>
    /// <param name="material">The tablet's base material, used only to pick the layout proportions.</param>
    public TabletHost(ItemSlot slot, ScribeBackdropSpec? backdrop = null, string? material = null) : base(slot, backdrop)
    {
        _slot = slot;
        _material = material;
    }

    public override string DefaultDocumentTitle => Lang.Get("scribe:doctitle-tablet");

    /// <summary>The policy enforced at the editor's add/pin boundary: a WET tablet gets the scratch-tier cap;
    /// a HARD or FIRED one gets <see cref="ScribeDocumentPolicy.UneditableTablet"/>, denying outright. This
    /// backs up the dialog's own <see cref="ItemScribeTablet.IsEditable"/> check so no mutation slips through.</summary>
    public override ScribeDocumentPolicy Policy =>
        ItemScribeTablet.IsEditable(_slot.Itemstack)
            ? ScribeDocumentPolicy.Tablet
            : ScribeDocumentPolicy.UneditableTablet;

    /// <summary>Re-proportions the dialog per material so content nestles inside that material's GUI art
    /// panel; aspect is unchanged.
    ///
    /// <para>Clay fills more of the wide backdrop (shorter title band, taller inner section, narrow side
    /// margins). Wax insets further because its art frames a smaller beige panel; its gutters are
    /// deliberately unequal per axis (the art is taller than wide), so the buffer reads as even by eye.</para></summary>
    public override ScribeLayout GetLayout(float pixelArtSize)
    {
        // TitleBtnsWFrac is the fraction of window width used by the title+buttons row; higher wraps a
        // long title later. Wax wraps sooner because its panel is narrower.
        var props = _material == "wax"
            ? ScribeLayoutProportions.Default with
              {
                  TitleBarFrac   = 0.15f,
                  InnerHFrac     = 0.775f,
                  SideColFrac    = 0.08f,
                  TitleBtnsWFrac = 0.82f,
              }
            : ScribeLayoutProportions.Default with
              {
                  TitleBarFrac   = 0.11f,
                  InnerHFrac     = 0.83f,
                  SideColFrac    = 0.06f,
                  TitleBtnsWFrac = 0.86f,
              };
        return new ScribeLayout(pixelArtSize, 1160f / 1024f, props);
    }
}
