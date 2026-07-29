using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Lectern block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>.
/// All view state, build methods, lock orchestration, autosave, title editing, scroll management,
/// and nav-button layout live in the base class. The Lectern adds no extra nav buttons.
/// </summary>
public sealed class GuiDialogScribeLecternLibGui : ScribeDialogBase
{
    public GuiDialogScribeLecternLibGui(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        : base(pos, host, capi)
    {
    }
}
