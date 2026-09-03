using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>One Accept-placement candidate (assignment-state-machine's placement requirement) — an
/// eligible (writeable Scribe document) item's slot identity, exactly what
/// <see cref="ScribeAssignmentActionMessage"/>/<see cref="ScribeAutoLinkQuestMessage"/> needs to name the
/// target, plus a display label for the picker shown when more than one candidate exists.</summary>
internal readonly record struct ScribeAcceptCandidate(string InventoryId, int SlotId, string Label);

/// <summary>Formats an Accept-placement candidate's (or, once Accepted, the actual destination's)
/// display label as `&lt;Type&gt; "&lt;Title&gt;"` (playtest feedback 2026-08-30: the bare item name alone
/// doesn't distinguish two carried Notebooks) — e.g. `Notebook "Book of Nick"`. Falls back to the bare
/// item name when the stack carries no document yet or still has the untitled default, matching
/// <see cref="ScribeDocumentSlot.BuildSummaryCard"/>'s same "don't imply a title that isn't there" rule.
/// Shared by <see cref="ScribeAcceptCandidates.Compute"/> (picker labels, pre-Accept) and
/// <c>TryPlaceAcceptedAssignment</c> (the captured <see cref="ScribeAssignment.AcceptedIntoLabel"/>,
/// post-Accept — capture-assignment-accept-destination).</summary>
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
/// Computes this player's current Accept-placement candidates (assignment-state-machine's placement
/// requirement) — shared by Assignment's Inbox Accept control AND Quest auto-link's Accept flow
/// (add-progression-framework-quest-support Decision 3, extracted out of
/// <c>ScribeDialogBase.ComputeAcceptCandidates</c>, which both backends previously diverged from): (1) the
/// player's own hotbar + backpack ONLY (<see cref="ScribeModSystem.EnumerateCarriedSlots"/> — never
/// ground/chest/creative), filtered to eligible (writeable) Scribe document items; (2) EVERY eligible
/// carried item is listed — with more than one, the caller always offers a picker (triage 2026-09-01: a
/// prior version let the book matching a "last opened" hint win outright and skip the picker entirely when
/// 2+ items were carried, which silently accepted onto whichever book you'd last had open without ever
/// asking — dropped in favor of always checking once when there's a real choice to make). A match for
/// <paramref name="preferDocId"/> is still ordered FIRST as a convenience default (so the picker's initial
/// selection is usually the one you want), it just no longer bypasses the picker. An empty result means the
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
