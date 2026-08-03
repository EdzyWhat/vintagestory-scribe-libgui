## Context

Two Mod-layer files have become god-files:
- `src/Mod/ScribeDialogBase.cs` (~2357 lines, ~34% comments) — the base class behind every
  Scribe document dialog. It mixes title-editing, the guestbook tab, the pinned-tasks view,
  backdrop/layout wrapping, input-capture/focus management, and view-model state in one type.
- `src/Mod/ScribeModSystem.cs` (~1974 lines) — the mod system. It mixes SVG-icon and font
  registration, the document/host registry, network packet handlers, the backdrop-bitmap cache,
  and client/server lifecycle.

Both are single classes, so the natural, lowest-risk way to divide them without touching the type
surface is C# `partial class`. This change is a pure structural refactor: no behavior, API, or
spec change. It was split out of `repo-cleanup-and-roadmap-pass` precisely so the one
runtime-risky piece has its own gate (clean build + Core.Tests + Atlas) and its own revert
boundary.

## Goals / Non-Goals

**Goals:**
- No single Scribe Mod file is a ~2000-line catch-all; each partial file covers one cohesive
  concern with an obvious name.
- Guaranteed behavior preservation — the diff is member relocation only.

**Non-Goals:**
- No comment/doc rewriting (that is `repo-cleanup-and-roadmap-pass`); comments move verbatim with
  their members.
- No rename, no visibility change, no signature change, no logic change.
- No new standalone helper *types* by default (see Decisions); no spec/behavior change.

## Decisions

### `partial class` over member extraction
Relocating members between files of the same `partial class` cannot change the type's public
surface, field visibility, or call sites — it is the strongest available guarantee of behavior
preservation. Extracting members into brand-new helper classes would change the type surface and
every call site, introducing real risk for no legibility gain here.
- **Alternative — extract helper classes:** reserved for the rare cluster that is genuinely a
  standalone unit with no private-state coupling to the parent; used selectively, not as the
  default, and only if it clearly reads better.

### File naming: `<Type>.<Concern>.cs`
Each partial lives in `src/Mod/<Type>.<Concern>.cs` (e.g. `ScribeDialogBase.Guestbook.cs`,
`ScribeModSystem.Network.cs`), the conventional C# partial-class layout. The original file keeps
the ctor/core state and shrinks to the primary declaration plus whatever doesn't belong to a
named concern.

### Seams chosen against the live code
The exact concern boundaries are decided during implementation by reading the current
`#region`/method clustering, since the audit's candidate seams (title-edit, guestbook, pins,
backdrop/layout, input-capture for the dialog; registration, registry, network, cache, lifecycle
for the mod system) are approximate. The binding constraint is not the specific seam list but that
every move is pure relocation.

## Risks / Trade-offs

- **[A move silently changes behavior]** → partial-class relocation only; after each file's
  split, `dotnet build` clean + `dotnet test tests/Core.Tests` green, then an Atlas run and a
  manual in-game smoke of all three dialogs before the change is done. Commit per file so a
  regression is bisectable and revertible in isolation.
- **[Field-initializer / ctor ordering drift]** → moving a member never reorders field
  initializers within a partial class (the compiler concatenates them), but keep all field
  *initializers* and the ctor together in the primary file to avoid any reader confusion.
- **[Merge churn]** → large file moves conflict easily with other in-flight work; sequence this
  after `repo-cleanup-and-roadmap-pass` (which edits comments in these same files) so the comment
  pass lands first and this refactor moves the already-tidied members.

## Migration Plan

No data, user, or API migration — behavior is identical. Rollback is reverting the refactor
commit(s); each file's split is its own commit so a single file can be backed out without the
other.

## Open Questions

None blocking. The concrete partial-file seam list is an implementation detail resolved against
the live code.
