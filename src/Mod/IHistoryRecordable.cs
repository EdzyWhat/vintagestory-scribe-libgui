using Scribe.Core;

namespace Scribe;

/// <summary>
/// A carried Notebook/Tablet a history event can be recorded on and flushed back, however it is carried:
/// <see cref="NotebookHost"/> (a live slot) or <see cref="CarryOnBridge.CarriedNotebookRef"/> (frozen in a
/// CarryOn container), so the recording sites fan out to both with one loop.
/// </summary>
public interface IHistoryRecordable
{
    HistoryStore History { get; }
    void FlushHistory();
}
