namespace Scribe.Core;

/// <summary>Discriminator for how a <see cref="HistoryEntry"/> should be displayed. Baked rows
/// carry a finished sentence in <see cref="HistoryEntry.Detail"/> (legacy v1–v3 payloads, Manual
/// text). Live rows carry structured facts and are formatted in the viewing client's locale.</summary>
public enum HistorySchema : byte
{
    Baked = 0,
    Live  = 1,
}
