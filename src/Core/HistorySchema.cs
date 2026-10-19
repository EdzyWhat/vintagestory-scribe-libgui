namespace Scribe.Core;

/// <summary>How a <see cref="HistoryEntry"/> is displayed. Baked rows carry a finished sentence in
/// <see cref="HistoryEntry.Detail"/> (legacy payloads, Manual text); Live rows carry structured facts
/// formatted in the viewer's locale.</summary>
public enum HistorySchema : byte
{
    Baked = 0,
    Live  = 1,
}
