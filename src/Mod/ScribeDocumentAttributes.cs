using Scribe.Core;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Reads/writes a <see cref="ScribeDocument"/> onto an <see cref="ItemStack"/>'s saved+synced
/// attributes, reusing the same codec and attribute key the block entity uses for its tree
/// attributes. This is what lets a lectern's document survive break → re-place: breaking the block
/// writes the serialized document (with its stable <c>DocId</c>/<c>TaskId</c>s) onto the dropped
/// item; placing that item restores it. Because the ids ride inside the bytes, a re-placed lectern
/// keeps the same identity and per-player pins keep resolving.
///
/// The dropped item despawning is the only true content-loss point (the bytes go with it). This
/// same helper is the intended copy/paste-between-blocks mechanism on the roadmap.
/// </summary>
public static class ScribeDocumentAttributes
{
    /// <summary>Attribute key on the stack. Deliberately the same string the block entity uses for its
    /// tree attribute, so the document's on-item and in-world forms are byte-identical.</summary>
    public const string DocumentAttributeKey = "scribeDocument";

    /// <summary>Writes the document onto the stack's attributes (saved + synced with the stack).</summary>
    public static void WriteTo(ItemStack stack, ScribeDocument document)
    {
        stack.Attributes.SetBytes(DocumentAttributeKey, ScribeDocumentCodec.Serialize(document));
    }

    /// <summary>Reads a document off the stack's attributes. Returns false (and null) when the stack
    /// carries no document or the bytes are malformed, so the caller can fall back to an empty
    /// document rather than propagating a null.</summary>
    public static bool TryReadFrom(ItemStack stack, out ScribeDocument? document)
    {
        var bytes = stack.Attributes.GetBytes(DocumentAttributeKey);
        return ScribeDocumentCodec.TryDeserialize(bytes, out document) && document is not null;
    }
}
