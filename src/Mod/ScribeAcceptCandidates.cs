using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>One Accept-placement candidate — an
/// eligible (writeable Scribe document) item's slot identity, exactly what
/// <see cref="ScribeAssignmentActionMessage"/>/<see cref="ScribeAutoLinkQuestMessage"/> needs to name the
/// target, plus a display label for the picker shown when more than one candidate exists.</summary>
internal readonly record struct ScribeAcceptCandidate(string InventoryId, int SlotId, string Label);

/// <summary>Formats an Accept-placement candidate's (or, once Accepted, the actual destination's)
/// display label as `&lt;Type&gt; "&lt;Title&gt;"` (the bare item name alone
/// wouldn't distinguish two carried Notebooks) — e.g. `Notebook "Book of Nick"`. Falls back to the bare
/// item name when the stack carries no document yet or still has the untitled default, matching
/// <see cref="ScribeDocumentSlot.BuildSummaryCard"/>'s same "don't imply a title that isn't there" rule.
/// Shared by <see cref="ScribeAcceptCandidates.Compute"/> (picker labels, pre-Accept) and
/// <c>TryPlaceAcceptedAssignment</c> (the captured <see cref="ScribeAssignment.AcceptedIntoLabel"/>,
/// post-Accept).</summary>
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
/// Computes this player's current Accept-placement candidates — shared by Assignment's Inbox Accept
/// control AND Quest auto-link's Accept flow: (1) the
/// player's own hotbar + backpack ONLY (<see cref="ScribeModSystem.EnumerateCarriedSlots"/> — never
/// ground/chest/creative), filtered to eligible (writeable) Scribe document items; (2) EVERY eligible
/// carried item is listed — with more than one, the caller always offers a picker, so an Accept is never
/// silently placed onto whichever book was last open. A match for
/// <paramref name="preferDocId"/> is ordered FIRST as a convenience default (so the picker's initial
/// selection is usually the one you want) but never bypasses the picker. An empty result means the
/// caller's Accept control has nothing to place onto and should render disabled; exactly one eligible item
/// still lets the caller render a plain Accept button (nothing to choose between). The server re-validates
/// the resolved slot itself regardless of what this returns, so a stale list is harmless.
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
