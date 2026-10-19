using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>
/// The Lectern's placed-block entity. A thin subclass of <see cref="BlockEntityScribeWritingStation"/>
/// supplying only its page art, layout aspect, fallback title, mesh-cache key, and dialog.
/// </summary>
public sealed class BlockEntityScribeLectern : BlockEntityScribeWritingStation
{
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    /// <summary>The Lectern page art is 1024×1160 (aspect 1160/1024); the dialog is sized to it.</summary>
    protected override float PageAspect => 1160f / 1024f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-lectern";

    protected override string MeshCacheKeyPrefix => "scribelecternmesh";

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeLecternLibGui(Pos, this, capi);
}
