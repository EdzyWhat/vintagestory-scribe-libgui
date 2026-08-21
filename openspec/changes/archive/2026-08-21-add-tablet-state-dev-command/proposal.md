## Why

A clay Tablet's life-cycle state — **wet** (editable) → **hard** (dried, read-only, reversible) →
**fired** (kiln-hardened, permanently read-only) — is reached only by slow in-world processes: the
~2-day `Harden` transition clock, or a firepit smelt. That makes the read-only states painful to
reach for testing. The 2026-08-20 playtest of `tablet-text-visibility` had to **backlog** its
fired/hardened readability check (`TESTING.md` item `00000016`) purely because the tester "has no
easy way to put a tablet into the fired/hardened state," and asked for a dev command to toggle it
(e.g. `.scribe tablet fired`).

We already have the exact machinery: state is carried by the `material` **variant** (`clay-red` →
`clay-red-hard` → `clay-red-fired`), and `ItemScribeTablet` already swaps between these siblings —
carrying the document/history bytes across the swap — in its `Soften`, `OnTransitionNow`, and
`DoSmelt` paths. A dev command is a thin wrapper over that same swap, letting a creative-mode player
set a held tablet's state instantly and run the read-only checks. The user is fine leaving it in for
players (it stays behind the same gate as `/scribe seed`).

## What Changes

- Add a `tablet <state>` sub-command to the existing server `/scribe` command tree
  (`ScribeModSystem.DevTools.cs`), alongside `seed`. `<state>` ∈ `wet` | `hard` | `fired`. It swaps
  the player's **held** tablet to the requested state's sibling `material` variant, carrying the
  stored document + history onto the new stack (the existing `CarryStackData` contract), marks the
  slot dirty (server-authoritative sync), and reports the result.
- Same gating as `/scribe seed`: `RequiresPrivilege(controlserver)` + an in-handler creative-mode
  check. No new privilege, no client hotkey, no GUI.
- Validity rules mirror the in-world mechanics, with one deliberate dev override:
  - **wax** has no `-hard`/`-fired` sibling → `hard`/`fired` error cleanly ("wax tablets never
    harden/fire"); `wet` on wax is a no-op success.
  - `fired → wet`/`fired → hard` is normally impossible in-world (firing is permanent). The dev
    command **allows** it as an explicit testing override, reported as such — it is a creative tool,
    not a mechanic change.
- No change to the natural `Harden` transition or firepit-smelt paths, no change to
  `ItemScribeTablet`'s player-facing behavior, no change to persistence format.

## Capabilities

### New Capabilities
- `tablet-state-dev-command`: a creative/admin dev command that sets a held clay Tablet's life-cycle
  state (wet/hard/fired) instantly by swapping its `material` variant sibling and carrying the
  document across, so read-only-state behavior can be tested without the in-world dry/fire processes.

### Modified Capabilities
_(none — this is an additive dev tool; it does not alter the `tablet-firing`/`tablet-clay-hardening`
mechanics or any shipped requirement.)_

## Impact

- **`src/Mod/ScribeModSystem.DevTools.cs`**: register the `tablet` sub-command under the existing
  `/scribe` root; add its handler (resolve the held tablet, validate the requested state, swap the
  variant + carry the document, `MarkDirty`, report).
- **`src/Mod/ItemScribeTablet.cs`**: expose the variant-swap + document-carry as a reusable seam if
  the handler can't reach the current `private static CarryStackData`/`ResolveMaterialState` cheaply
  (e.g. a `public static ItemStack? BuildStateVariant(ItemStack, TabletState, IWorldAccessor)` that
  the natural transitions can also route through). Design chooses between exposing a seam vs. a
  small local reimplementation.
- **`src/Mod/assets/scribe/lang/en.json`**: result/error strings for the command.
- Server-side only (held-item inventory is server-authoritative, matching `Soften`/`TryQuench`). No
  Core change, no VS API additions, no new dependency, no persistence-schema change.
- Unblocks `TESTING.md` `00000016` (fired/hardened readability retest) once shipped.
