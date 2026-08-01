## Context

Vintage Story surfaces a collectible's info via two override points: `Block.GetPlacedBlockInfo`
(the look-at tooltip on a placed block) and `CollectibleObject.GetHeldItemInfo` (the hotbar/
inventory hover). Neither is currently overridden anywhere in the mod — the closest precedent is
the interaction-help pattern (`GetPlacedBlockInteractionHelp` / `GetHeldInteractionHelp`), which
uses `scribe:`-prefixed lang keys.

The title lives on `ScribeDocument.Title` (non-nullable `string`, defaults to `"Untitled"`, clamped
to 50 chars, normalized so empty/whitespace collapses to the default). For the placed Lectern it is
read live off the block entity (`BlockEntityScribeLectern.Document.Title`). For the Notebook items it
is read from the ItemStack via `ScribeDocumentAttributes.TryReadFrom`, which returns `false` for a
never-opened item that carries no `scribeDocument` attribute yet.

The "Burn temperature / Burn duration" lines come from vanilla `GetHeldItemInfo` rendering the
blocktype's `combustibleProps` — they exist only because `lectern.json` declares
`combustibleProps { burnTemperature: 600, burnDuration: 10 }`.

## Goals / Non-Goals

**Goals:**
- One consistent `Title: "<title>"` line on the placed Lectern and on both Notebook items.
- A single shared formatter so all three call sites render the line identically (label, quoting,
  untitled placeholder).
- Remove the irrelevant combustion lines from the Lectern.

**Non-Goals:**
- No change to how titles are set, stored, synced, or persisted (read-only feature).
- No tooltip changes to any other Scribe object.
- No new lang beyond the label + placeholder keys.

## Decisions

**1. Shared formatter over three divergent copies.** Add one small static helper (e.g.
`ScribeTooltip.FormatTitleLine(string? rawTitle)`) that returns the fully-formatted, localized line
`Title: "<title>"` (or the `(untitled)` placeholder). The Lectern block and both item overrides call
it. Rationale: three hand-written copies would drift; the quoting/placeholder rule is the actual
behavior worth centralizing. Alternative (inline at each site) rejected for that drift risk.

**2. "Untitled" detection = raw title is null/whitespace OR equals `ScribeDocument.DefaultTitle`.**
The document model already normalizes blank titles to `"Untitled"`, so treating a stored title equal
to `DefaultTitle` as "no meaningful title" makes the placed block (always has a document) and the
never-opened item (no document at all → helper receives null) render the same `(untitled)`
placeholder. The user chose the placeholder-text option, so the line is always present.

**3. Localized label + placeholder via `scribe:` lang keys.** Follow the existing lang convention:
`scribe:tooltip-title` = `"Title: \"{0}\""` and `scribe:tooltip-title-untitled` = `"(untitled)"`
(or fold both into one key set). The quotes are part of the format so a translator can adjust the
quoting style. This matches how interaction-help strings are already handled.

**4. Remove `combustibleProps` entirely rather than zeroing it.** Deleting the block is the clean way
to drop both lines; leaving a zeroed block could still render "Burn temperature: 0°C". The Lectern is
crafted, not fuel, so it loses nothing meaningful. (If the Lectern should remain burnable for realism
that is a separate decision — out of scope; the user asked to remove the noise.)

## Risks / Trade-offs

- [Removing `combustibleProps` also makes the Lectern non-flammable/non-fuel] → Intended; a bookstand
  being usable as furnace fuel was never a designed behavior, and the user explicitly wants the lines
  gone. Reversible by restoring the JSON block if ever wanted.
- [Placed-block title reads the live BE, so a title edited by another player shows immediately only
  after the client's BE syncs] → Acceptable; the tooltip reflects whatever the client's authoritative
  copy holds, same as every other synced field. No new sync path added.
- [A never-opened notebook shows `(untitled)` rather than a real title] → Correct by design; there is
  no document to name yet.

## Open Questions

None — scope, empty-title behavior, and target objects are confirmed with the user.
