using System;
using Scribe.Core;               // ScribeDocumentPolicy (task cap)
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// The Chalkboard's placed-block entity. A thin subclass of <see cref="BlockEntityScribeWritingStation"/>:
/// all document, persistence, editor-lock, guestbook, and placement logic lives in the base (shared with
/// the Lectern and Scriptorium). The Chalkboard supplies only its own page art, layout aspect, fallback
/// title, mesh-cache key, and dialog — the cosmetic deltas from the Lectern.
/// </summary>
public sealed class BlockEntityScribeChalkboard : BlockEntityScribeWritingStation
{
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.ChalkboardPage;

    /// <summary>The Chalkboard GUI background is 128×145 (aspect 145/128); the dialog is sized to it so
    /// LibGUI's stretch-to-fill renders it distortion-free.</summary>
    protected override float PageAspect => 145f / 128f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-chalkboard";

    protected override string MeshCacheKeyPrefix => "scribechalkboardmesh";

    /// <summary>Cap the chalkboard at 10 task blocks (refine-chalkboard), enforced through the shared
    /// <see cref="ScribeDocumentPolicy.CanAdd"/> path the wax tablet already uses. Deliberately NOT the
    /// <see cref="ScribeDocumentPolicy.Tablet"/> preset: that also caps pins at 1, but the chalkboard is a
    /// shared placed block whose pins are per-player, so it leaves <c>MaxPins</c> null (uncapped) and caps
    /// tasks only. Notes/text are not task blocks and so are uncapped. The dialog surfaces the refusal via
    /// the <c>scribe:chalkboard-full</c> notice (see <c>GuiDialogScribeChalkboard.TaskCapReachedLangKey</c>).</summary>
    protected override ScribeDocumentPolicy HostPolicy => new() { MaxBlocks = 10 };

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeChalkboard(Pos, this, capi);

    /// <summary>
    /// Fixed facing from the <c>side</c> block variant. The board's authored front is SOUTH (+Z) at 0°;
    /// <c>HorizontalAttachable</c> places the variant named for the ATTACH direction (into the wall), so the
    /// front faces the OPPOSITE way — outward, toward the player who placed it. Front direction as a function
    /// of the mesh angle θ is <c>(sinθ, cosθ)</c> in (X, Z): θ=0→+Z(south), 90→+X(east), 180→−Z(north),
    /// 270→−X(west) — the same convention the base's player-facing placement uses. So each side maps to the
    /// angle that turns +Z to face away from its wall:
    /// <list type="bullet">
    ///   <item>north (wall to the north / −Z) → face south (+Z) → 0°</item>
    ///   <item>east  (wall to the east / +X) → face west  (−X) → 270°</item>
    ///   <item>south (wall to the south / +Z) → face north (−Z) → 180°</item>
    ///   <item>west  (wall to the west / −X) → face east  (+X) → 90°</item>
    /// </list>
    /// Note these are 180° from the vanilla painting's <c>rotateYByType</c> because our shape's authored front
    /// is +Z (south) where vanilla painting art faces −Z (north). Computed from the variant directly (not the
    /// shape's resolved <c>rotateY</c>) so it is independent of the JSON <c>rotateYByType</c>, which the
    /// block-entity mesh path does not consult.
    /// </summary>
    protected override float? WallMountAngleRad
    {
        get
        {
            float deg = Block?.Variant["side"] switch
            {
                "north" => 0f,
                "east"  => 270f,
                "south" => 180f,
                "west"  => 90f,
                _       => 0f,
            };
            return deg * ((float)Math.PI / 180f);
        }
    }

    /// <summary>The Chalkboard's container proportions — the single place to refine how the wood-framed slate
    /// art divides into the title band, side margins, tasks column, and bottom title/button bar. Seeded to the
    /// shared <see cref="ScribeLayoutProportions.Default"/> values so nothing moves until tuned; adjust a field
    /// here (e.g. <c>Default with { SideColFrac = 0.14f }</c> to pull the tasks column in off the wood frame)
    /// to reshape ONLY the Chalkboard dialog. See <see cref="ScribeLayout"/> for what each fraction drives.</summary>
    protected override ScribeLayoutProportions? LayoutProportions => ScribeLayoutProportions.Default with
    {
        TitleBarFrac   = 0.15f,   // top drag band height, as a fraction of window height
        InnerHFrac     = 0.75f,   // content region height (title band + tasks), fraction of window height
        SideColFrac    = 0.078f,  // EACH side margin/nav column width, fraction of window width
        TitleBtnsWFrac = 0.82f,   // bottom title+buttons bar width, fraction of window width
        TitleBtnsHFrac = 0.065f,  // bottom title+buttons bar height, fraction of window height
    };
}
