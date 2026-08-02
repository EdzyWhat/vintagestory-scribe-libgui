namespace Scribe;

/// <summary>
/// Marker for the player-held item classes that persist a Scribe document on their ItemStack (the
/// Notebook, the Clockmaker's Notebook, and the clay/wax Tablet). The server-side save/pickup/pin/history
/// paths and the dialog's active-slot check gate on this interface rather than an exhaustive
/// <c>is (ItemScribeNotebook or ItemClockmakerNotebook or …)</c> type list, so a new document-bearing
/// item is recognized everywhere just by implementing it — without which its edits are silently dropped
/// server-side (the stack is never written, so a drop/pickup round-trip wipes it).
///
/// <para>Purely a type tag: it declares no members because the shared behavior is already reached through
/// <see cref="ScribeDocumentAttributes"/> on the stack and the <see cref="NotebookHost"/> family. It exists
/// only to make "is this one of Scribe's document items?" a single, extensible check.</para>
/// </summary>
public interface IScribeDocumentItem
{
}
