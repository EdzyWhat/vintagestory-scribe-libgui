using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>One Accept-placement candidate: an eligible item's slot identity (as the assignment/quest
/// messages name their target) plus a picker display label.</summary>
internal readonly record struct ScribeAcceptCandidate(string InventoryId, int SlotId, string Label);

/// <summary>Formats a candidate's (or accepted destination's) label as `&lt;Type&gt; "&lt;Title&gt;"`, falling
/// back to the bare item name when the stack has no document or the untitled default. Shared by
/// <see cref="ScribeAcceptCandidates.Compute"/> and <c>TryPlaceAcceptedAssignment</c>.</summary>
internal static class ScribeAssignmentDestinationLabel
{
    public static string Format(Vintagestory.API.Common.ItemStack stack)
    {
        string name = stack.GetName();
        if (ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null
            && !string.IsNullOrWhiteSpace(doc.Title) && doc.Title != ScribeDocument.DefaultTitle)
        {
            return Vintagestory.API.Config.Lang.Get("scribe:scribe-assignment-candidate-label", name, doc.Title);
        }
        return name;
    }
}

/// <summary>
/// Computes the player's Accept-placement candidates (shared by Assignment's Inbox and Quest auto-link): every
/// writeable Scribe document item in the player's own hotbar + backpack, so with more than one the caller always
/// offers a picker. A match for <paramref name="preferDocId"/> is ordered first but never bypasses the picker.
/// Empty means render Accept disabled. The server re-validates the resolved slot, so a stale list is harmless.
/// </summary>
internal static class ScribeAcceptCandidates
{
    public static List<ScribeAcceptCandidate> Compute(ICoreClientAPI capi, System.Guid? preferDocId)
    {
        var candidates = new List<ScribeAcceptCandidate>();
        if (capi.World.Player is not { } player) return candidates;

        var eligible = ScribeModSystem.EnumerateCarriedSlots(player)
            .Where(slot => slot.Itemstack?.Collectible is IScribeDocumentItem item && item.IsSlotWriteable(slot))
            .ToList();
        if (eligible.Count == 0) return candidates;

        if (preferDocId is { } wanted)
        {
            eligible = eligible
                .OrderByDescending(slot =>
                    ScribeDocumentAttributes.TryReadFrom(slot.Itemstack!, out var doc) && doc?.DocId == wanted)
                .ToList();
        }

        foreach (var slot in eligible)
        {
            var inv = slot.Inventory;
            candidates.Add(new ScribeAcceptCandidate(inv.InventoryID, inv.GetSlotId(slot),
                ScribeAssignmentDestinationLabel.Format(slot.Itemstack!)));
        }
        return candidates;
    }
}
