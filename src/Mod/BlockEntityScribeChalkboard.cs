using System;
using Scribe.Core;               // ScribeDocumentPolicy (task cap)
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// The Chalkboard's placed-block entity. A thin subclass of <see cref="BlockEntityScribeWritingStation"/>
/// supplying only its page art, layout aspect, fallback title, mesh-cache key, and dialog.
/// </summary>
public sealed class BlockEntityScribeChalkboard : BlockEntityScribeWritingStation
{
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.ChalkboardPage;

    /// <summary>The Chalkboard GUI background is 128×145 (aspect 145/128); sizing the dialog to it keeps
    /// LibGUI's stretch-to-fill distortion-free.</summary>
    protected override float PageAspect => 145f / 128f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-chalkboard";

    /// <summary>Selection-only tuning target: <see cref="ScribeBoxTuning.HasCollisionBox"/> excludes the
    /// Chalkboard, so only <c>RotatedSelectionBox</c> is live-tunable; collision stays <c>null</c> (walk-through).</summary>
    protected override ScribeBoxTuningTarget? TuningTarget => ScribeBoxTuningTarget.Chalkboard;

    protected override string MeshCacheKeyPrefix => "scribechalkboardmesh";

    /// <summary>Caps the chalkboard at <see cref="ScribeDocumentPolicy.StandardMaxBlocks"/> task blocks via
    /// <see cref="ScribeDocumentPolicy.CanAdd"/>. Deliberately NOT the <see cref="ScribeDocumentPolicy.Tablet"/>
    /// preset: that also caps pins at 1, but chalkboard pins are per-player, so <c>MaxPins</c> stays null.
    /// The dialog surfaces the refusal via <c>scribe:chalkboard-full</c>.</summary>
    protected override ScribeDocumentPolicy HostPolicy => new() { MaxBlocks = ScribeDocumentPolicy.StandardMaxBlocks };

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeChalkboard(Pos, this, capi);

    /// <summary>
    /// Fixed facing from the <c>side</c> block variant. The board's authored front is SOUTH (+Z) at 0°;
    /// <c>HorizontalAttachable</c> names the variant for the ATTACH direction (into the wall), so the front
    /// must face the opposite way, away from the wall. Front direction for mesh angle θ is
    /// <c>(sinθ, cosθ)</c> in (X, Z): 0→+Z(south), 90→+X(east), 180→−Z(north), 270→−X(west).
    /// Computed from the variant (not the shape's <c>rotateYByType</c>, which the block-entity mesh path
    /// does not consult). 180° from vanilla paintings, whose art faces −Z.
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

    /// <summary>The Chalkboard's layout proportions for its wood-framed slate art; adjust a field here
    /// (e.g. <c>Default with { SideColFrac = 0.14f }</c>) to reshape ONLY this dialog. See
    /// <see cref="ScribeLayout"/> for what each fraction drives.</summary>
    protected override ScribeLayoutProportions? LayoutProportions => ScribeLayoutProportions.Default with
    {
        // TitleBarFrac/InnerHFrac carry the same 0.015 offset as the shared Default (they sum to 0.90),
        // keeping the Read/Edit/Pinned tabs consistent with every other surface's Row 2 subtitle.
        TitleBarFrac   = 0.135f,  // top drag band height, as a fraction of window height
        InnerHFrac     = 0.765f,  // content region height (title band + tasks), fraction of window height
        SideColFrac    = 0.078f,  // EACH side margin/nav column width, fraction of window width
        TitleBtnsWFrac = 0.82f,   // bottom title+buttons bar width, fraction of window width
        TitleBtnsHFrac = 0.065f,  // bottom title+buttons bar height, fraction of window height
    };
}
