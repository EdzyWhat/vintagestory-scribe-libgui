# Scribe

Note-taking & task-management mod for Vintage Story 1.22.x (targets .NET 10). Full
project context lives in `openspec/config.yaml` and `README.md` — read those for
architecture, not this file.

## Guardrails

- **Spec-driven**: for any non-trivial change, use the `openspec-propose` skill first
  rather than implementing directly. Don't skip straight to code for anything beyond a
  typo/small fix.
- **`src/Core/` must never reference the Vintage Story API.** This is the load-bearing
  invariant that keeps `Core` unit-testable without a game install — don't add a
  `VintagestoryAPI` reference or `using Vintagestory.*` there to solve a problem faster.
- **No new mod dependencies.** Vanilla `VintagestoryAPI` only; `ConfigLib` is the sole
  planned exception, and only as an optional soft dependency gated by `IsModEnabled`. Ask
  before adding any NuGet/mod package.
- **Don't touch `.github/workflows/*.yml` or `Directory.Build.props`** without asking —
  cloud CI runs the `Core` suite + coverage (no game install on runners); the Atlas suite is
  gated locally by the opt-in pre-push hook (`build/install-hooks.sh`). Cloud-running Atlas is
  feasible-but-deferred, not impossible. Changes here can silently break release automation.
- **Persistence/sync follows the vanilla Sign block pattern** (`ToTreeAttributes` /
  `FromTreeAttributes`, `SendBlockEntityPacket`, `MarkDirty`, server-authoritative). Match
  this pattern for new synced state rather than inventing a new one.
- Secondary goal is the author building dev skills — favor clear, conventional,
  well-explained solutions over clever/terse ones.

## Modding references

Before inventing a pattern from scratch or researching an approach/bug externally, check
these first, in order:

1. **`VSAPI-NOTES.md`** — symptom-indexed facts about API internals we already learned the
   hard way (GUI composer lifecycle, `Lang` domain-prefixing, text wrapping). Check here
   first: several v1 bugs were misdiagnosed for multiple rounds (as staging issues, etc.)
   before someone read the source and found the real cause — don't repeat that.
2. **Read the game DLLs directly — this is a FIRST resort, not a fallback.** Decompile with
   `ilspycmd` (`~/.dotnet/tools/ilspycmd`) and read the shipped source + doc-comments; they
   are written for modders like us and are the most authoritative answer to "how does this
   actually work." **Prefer `VintagestoryLib.dll`** (`/Applications/Vintage Story.app/VintagestoryLib.dll`)
   — it holds the *implementations* (render API, scissor stack, client platform) and its
   comments are especially descriptive. `VintagestoryAPI.dll` holds the interfaces/base
   classes and their XML doc-comments. Do this BEFORE reaching for the wiki or guessing.
   When you learn something non-obvious this way, add it to `VSAPI-NOTES.md` so it's not
   re-derived next time. (`ilspycmd -t <FullTypeName> <dll>` dumps one type; `-l c <dll>`
   lists classes.)
3. The wiki and shipped-mod source below (useful for vanilla precedent and worked examples,
   but the DLLs above are the ground truth when the two disagree).

- **The wiki**: https://wiki.vintagestory.at/Category:Modding — start at `Modding:GUIs` for
  dialog/composer questions. Page-specific lookups (e.g.
  https://wiki.vintagestory.at/Ink_and_quill) are useful for finding vanilla precedent for a
  specific interaction (e.g. writable-book save/open patterns).
- **Real shipped-mod source**, more authoritative than the wiki when the wiki is thin:
  `anegostudios/vsapi`, `anegostudios/vssurvivalmod`, `anegostudios/vsessentialsmod` on GitHub.
  E.g. `BlockEntity/BESign.cs` (text-editing dialog + `Controls.ShiftKey` right-click modifier),
  `Gui/GuiDialogBlockEntityText.cs`, `Gui/GuiDialogTrader.cs` (`AddCellList`/`IGuiElementCell`
  usage — note cell lists are mouse-only and can't host a live `GuiElementTextInput`).
- **LibGUI** (only if we adopt it — assessed in `openspec/changes/explore-libgui-adoption/`,
  summarized in `docs/libgui-reference.md`): a local gitignored wiki clone lives at `./.wiki/`.
  Search all `.md` under `./.wiki/` with `ripgrep`. There is NO on-disk LibGUI source checkout —
  when the wiki is thin or you need ground truth, decompile the shipped `Gui.dll` with `ilspycmd`
  (same as the base-game DLLs above), which beats the wiki when they disagree. When you resolve a
  complex LibGUI layout bug or correct a LibGUI misconception, append a note to the `## LibGUI`
  section of `VSAPI-NOTES.md`.
