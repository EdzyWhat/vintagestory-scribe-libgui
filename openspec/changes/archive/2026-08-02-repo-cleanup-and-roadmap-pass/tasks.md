## 1. Docs & roadmap accuracy (lowest risk — commit first)

- [x] 1.1 `README.md`: replace all three `gui_2.0.0.zip` references (`:64`, `:66`, `:67`) with
      `gui_3.1.0.zip`, matching `modinfo.json` (`"gui": "3.1.0"`) and the Integration.Tests refs.
- [x] 1.2 `README.md:11`: replace the "early development" status line with an accurate one
      (shipped / v0.2.0 / live on the mod DB) and keep a single clear pointer to the roadmap.
- [x] 1.3 `openspec/config.yaml:38`: update `Current focus: v1` to reflect the current focus
      (post-v0.2.0), keeping the `See ROADMAP.md for later tiers` pointer.
- [x] 1.4 Reorganize the roadmap: move `RELEASE.md`'s fully-shipped v0.1.0 plan and its historical
      "Critical path"/"Settled decisions" prose into `CHANGELOG.md` (summarized, not pasted
      wholesale — git history is the exhaustive record). Leave `RELEASE.md` holding only the
      in-flight v0.2.0 cut.
- [x] 1.5 Move `ROADMAP.md`'s strikethrough / "Done / superseded" history into `CHANGELOG.md`
      (or drop where already recorded there); leave `ROADMAP.md` as a pure forward tier-map.
- [x] 1.6 Re-read README / RELEASE / ROADMAP / config.yaml for cross-references and fix any
      pointer left dangling by the moves; confirm all internal links still resolve.

## 2. Dead code & orphaned-asset removal

- [x] 2.1 Remove `ScribeBackdrops.LecternSettings` (`src/Mod/ScribeBackdrop.cs`) and its dangling
      `lecternsettingsbackdrop.png` reference.
- [x] 2.2 Remove `ScribeBackdrop.Wrap(...)`; if that leaves the `ScribeBackdrop` class empty,
      delete the class. Fix/remove any `<see cref>` doc-comment that pointed at the removed members
      so the build stays warning-clean.
- [x] 2.3 `git rm src/Mod/assets/scribe/textures/gui/lecternbackdrop.png` (tracked but unwired —
      stops it shipping in the release zip).
- [x] 2.4 Delete the local source/backup artifacts from the working tree (`scribe-sm-OG.png`,
      `scribe-sm-OG.psd`, `sketchbook-cover-og.png`, `sketchbook-mod.psd`, and the
      `textures/block/lectern (OG)/` dir). They are already gitignored.
- [x] 2.5 Delete stray `.DS_Store` files (including the two inside archived openspec dirs) and
      ensure `**/.DS_Store` (or equivalent) is gitignored so they don't reappear tracked.
- [x] 2.6 Build (`dotnet build src/Mod/Mod.csproj -c Debug`) — zero new warnings/errors.
- [x] 2.7 `bash build/restage.sh Debug` and confirm `lecternbackdrop.png` is no longer staged and
      the three live backdrops still are.

## 3. Comment simplification (moderate, comment-only)

- [x] 3.1 `src/Core/ScribePlayerSettings.cs`: deduplicate the four near-identical "plain bool
      needing no clamp" summaries and drop dated `(2026-..)` asides; keep each property's
      substantive one-line "what/why" and its `(change-name)` tag.
- [x] 3.2 `src/Mod/ScribeDialogBase.cs`: collapse multi-line crash/incident post-mortems (e.g. the
      `_pendingTitleFocus` 14-line summary) to a single explanatory line; drop dated asides.
- [x] 3.3 `src/Mod/ScribeModSystem.cs` and other large Mod files (`HudScribePins.cs`,
      `ScribeMultilineField.cs`, `NotebookHost.cs`, `ScribeBackdrop.cs`): same moderate trim —
      shorten paragraph-length doc-comments to their essential "why," keep `(change-name)` tags.
- [x] 3.4 Verify the whole track is comment-only: `git diff` shows only comment (`//` / `///`)
      lines changed — no signatures, names, or logic. Build stays clean.

## 4. Validate

- [x] 4.1 `openspec validate repo-cleanup-and-roadmap-pass --strict` passes.
- [x] 4.2 Confirm the out-of-scope changes were left untouched: the two near-complete changes
      (`v1-release-checklist`, `scribe-0-2-0-release-content`) and the separately-tracked god-file
      split (`split-large-gui-files`).
