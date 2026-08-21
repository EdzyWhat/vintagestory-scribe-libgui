## Context

Tablet life-cycle state is NOT a stack attribute — it is carried by the `material` **variant** itself
(`wire-tablet-clay-art-and-variants`): the wet tablet is the bare clay code (`clay-red`), and
hardening/firing swap it to a `-hard`/`-fired` sibling (`clay-red-hard`, `clay-red-fired`). Wax has no
`-hard`/`-fired` sibling (it never dries or fires). `ItemScribeTablet` already contains every piece a
dev toggle needs:

- `ResolveMaterialState(stack)` → `(baseMaterial, TabletState)` parses a stack's variant into its base
  clay color + `Wet`/`Hard`/`Fired`.
- `Soften(...)` builds a fresh stack of a sibling variant via
  `world.GetItem(stack.Collectible.CodeWithVariant("material", <targetMaterialVariant>))`, then
  `CarryStackData(from, to)` copies the document (`ScribeDocumentAttributes.DocumentAttributeKey`) and
  history (`scribeHistory`) bytes across — the exact carry the natural `Harden`/`DoSmelt` paths use.
- The `Soften`/`TryQuench` paths write the new stack back with `slot.Itemstack = ...; slot.MarkDirty()`
  server-side.

The existing `/scribe` command (`ScribeModSystem.DevTools.cs`) is a server command
(`api.ChatCommands.Create("scribe")`, `RequiresPrivilege(controlserver)`, `RequiresPlayer()`) with a
`seed` sub-command that checks creative mode in-handler. A `tablet` sub-command is a sibling of `seed`.

## Goals / Non-Goals

**Goals**
- Instantly set a held tablet to `wet`/`hard`/`fired` for testing the read-only-state behavior and
  cuneiform readability checks, without the ~2-day dry clock or a firepit.
- Preserve the tablet's document + history across the swap (same contract as the natural transitions).
- Server-authoritative sync, same gate as `/scribe seed`. Unblock `TESTING.md` `00000016`.

**Non-Goals**
- No client hotkey, no GUI, no new privilege.
- No change to the natural `Harden` / firepit-fire mechanics or to `ItemScribeTablet`'s player-facing
  interactions.
- Not a survival feature — it is a creative/admin dev tool (the user is fine leaving it in for
  players, but it stays behind the creative + `controlserver` gate).

## Decisions

**D1 — Sub-command, not a new command.** Register `tablet` under the existing `/scribe` root
(`BeginSubCommand("tablet")`), matching `seed`. Args: `parsers.WordRange("state", "wet", "hard",
"fired")`. Server command (`/`), not a client `.` command: the held item lives in server-authoritative
inventory, so the swap + `MarkDirty` must run server-side — exactly like `Soften`/`TryQuench`. (The
playtest ask phrased it `.scribe tablet fired`; it lands as `/scribe tablet fired` for the same reason
`/scribe seed` is a slash command.)

**D2 — Reuse `ItemScribeTablet`'s swap machinery via a new public seam.** The carry-across logic is
currently `private static` on `ItemScribeTablet`. Rather than reimplement variant math + document carry
in the command handler (drift risk), add one public static seam:
`public static ItemStack? BuildStateVariant(ItemStack current, TabletState target, IWorldAccessor world)`
that resolves the base material, maps `target` to the sibling variant suffix, `GetItem`s it, and
`CarryStackData`s the document/history across (returning `null` when the target sibling doesn't exist —
e.g. wax `hard`/`fired`, or an unregistered variant). `Soften` becomes a thin caller of this seam
(`BuildStateVariant(stack, TabletState.Wet, world)` guarded to hard-only) so the natural and dev paths
share one implementation and can't diverge. This keeps `CarryStackData` private and adds no new copy
of the byte-copy contract.

**D3 — Validity + the fired→wet override.** `wet`/`hard`/`fired` map to the bare/`-hard`/`-fired`
sibling. A `null` result from `BuildStateVariant` means "no such state for this material" → report a
clean error (wax hard/fired). `wet` on an already-wet (or wax) tablet is a no-op success. Setting `wet`
or `hard` on a `fired` tablet is impossible in-world (firing is permanent, `Soften` refuses it); the
dev command **intentionally allows** it and reports "(testing override)" so the tester can reset a
fired tablet — `BuildStateVariant` itself is state-agnostic (it just builds the requested sibling), and
only `Soften`'s own guard blocks fired→wet in the natural path, which we do not route through here.

**D4 — Target resolution.** Resolve the calling player's **held** tablet: the active hotbar slot's
stack whose collectible is `ItemScribeTablet` (fall back to scanning the player's hands if the active
slot isn't it). Mirrors `seed`'s `FindNotebookInInventory` helper shape; a dev toggle acts on what the
player is holding. If none found → clean "no held tablet" error.

**D5 — Report + strings.** Success reports the resulting variant + state and, for the override case,
notes the testing override; errors (wax, no tablet, non-creative) read like `seed`'s. Strings go in
`lang/en.json` (the sole shipped lang file), consistent with the rest of the mod.

## Risks / Trade-offs

- **Exposing `BuildStateVariant` publicly** widens `ItemScribeTablet`'s surface by one static method.
  Accepted: it consolidates the swap+carry contract that three call sites (`Soften`, `OnTransitionNow`,
  `DoSmelt`, and now the command) otherwise duplicate; a single seam is safer than a fourth copy.
- **fired→wet override diverges from the mechanic.** Intentional and clearly reported as a testing
  override; it exists only behind the creative + `controlserver` gate, so it cannot affect survival
  play.
- **"Leave it in for players."** The command ships (not stripped in Release), but the creative +
  `controlserver` gate means a survival player can't use it — matching `/scribe seed`. If a looser gate
  is ever wanted, that is a separate decision.
