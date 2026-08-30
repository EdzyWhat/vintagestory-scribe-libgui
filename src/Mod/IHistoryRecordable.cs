using Scribe.Core;

namespace Scribe;

/// <summary>
/// A carried Notebook/Tablet a history event (Death, PvpKill, TemporalStorm) can be recorded on and
/// flushed back to its owner, regardless of HOW it's carried. Implemented by both
/// <see cref="NotebookHost"/> (a live <see cref="Vintagestory.API.Common.ItemSlot"/> — hotbar,
/// backpack, crafting grid, a mod-added bonus inventory, …) and
/// <see cref="CarryOnBridge.CarriedNotebookRef"/> (frozen inside a CarryOn-carried container's
/// block-entity data), so <c>ScribeModSystem.History.cs</c>'s three recording sites can fan out to
/// both sources with one loop.
/// </summary>
public interface IHistoryRecordable
{
    HistoryStore History { get; }
    void FlushHistory();
}
