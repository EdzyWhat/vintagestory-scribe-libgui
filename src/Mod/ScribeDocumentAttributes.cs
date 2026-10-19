using Scribe.Core;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Reads/writes a <see cref="ScribeDocument"/> on an <see cref="ItemStack"/>'s saved+synced attributes using the same codec and key
/// as the block entity, so a lectern's document (with its stable DocId/TaskIds) survives break and re-place and per-player pins
/// keep resolving. Despawning the dropped item is the only true content-loss point.
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
