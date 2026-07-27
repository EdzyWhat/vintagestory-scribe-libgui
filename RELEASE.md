# Release plan — Scribe v1 (v0.1.0)

Tracked checklist for the first playable release. This is the **map**; per-task detail lives in
the linked OpenSpec changes / `docs/`. Update the boxes here as tracks land.

**Target:** `v0.1.0` — first public release. Lectern + pinned-task HUD, server-authoritative,
multiplayer- and survival-safe. Deps: `game 1.22.0`, hard `gui 2.0.0` (LibGUI).

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done.
**Dependency spine:** Track A (finish v1) → Track B (media) feeds → E (video), F (mod page).
Track C (reddit teaser) is being pulled FORWARD — it only needs one hero shot (B1) and goes out
ASAP, ahead of the rest (decided 2026-07-26). Track D (handbook) is independent.

---

## Track A — Finish v1 (product) — GATES EVERYTHING

The release can't be truthful until this is done. Ordered by dependency.

- [~] **A1. Clip over-long task text (CODE — v1-BLOCKING; approach REVISED 2026-07-26).** IMPLEMENTED +
      staged 2026-07-26 (Core `MaxTaskTextLength=1000` + clip-on-read in `TryDeserialize`; editor
      `ScribeMultilineField.MaxLength` on Task rows; 118 Core tests incl. clip-not-reject coverage).
      Awaiting in-game verification (TESTING.md). REVISION: the
      error/feedback surface is DEFERRED (the specific errors will be solved another way); instead of
      reporting an oversized edit, we simply CLIP. Task-block text is capped at 1000 chars; freeform Text/
      note sections keep the existing 10,000 hard cap. Clip happens in two places: (1) the editor field
      won't accept input past the task limit (maxlength UX), and (2) the codec CLIPS instead of REJECTS on
      deserialize (server-authoritative backstop) — which also fixes the silent-rejection bug (TESTING.md
      `fe168d81`) where an over-long edit was dropped whole with no feedback. This subsumes the old A2
      (per-task length limit). No error UI, no ToastLib. Core change (a new `MaxTaskTextLength` const +
      clip-on-read); implemented directly (small, settled) rather than via a full OpenSpec proposal.
- [ ] **A2. ~~Per-task soft length limit~~ — MERGED into A1** (the clip IS the length limit).
- [ ] **A2d. Lectern Pin Tab (CODE — v1-BLOCKING, decided 2026-07-26).** SCOPE CONSOLIDATED into the
      **retargeted `scribe-pin-editor` change** (2026-07-26): rewritten in place from the stale slide-out
      "Pin Tray" to a **nav-column Pin Tab view** (fills the stubbed `scribepin` nav slot; a peer of
      read/editor via `BuildCentralRegion`), extending the editor row rendering but sourced from `MyPins`.
      Full editor parity per row (complete + edit + unpin + delete + reorder), applied immediately (no undo
      window), plus the completion-policy picker on the tab. Server/store plumbing (`SetTaskTextFromReader`,
      `ScribePinStore` reorder, new edit/reorder messages) kept from the original change. Supersedes
      `scribe-animated-tabs` (tab-bar) and the stale `v5-backpack-hud.md` per-block cap. Validated 9/9.
      Ready for `/opsx:apply`. Own item; sequence after A1.
- [~] **A2b. Lectern placement orientation — face the player (CODE — v1-BLOCKING, decided 2026-07-26).**
      IMPLEMENTED + staged 2026-07-26 (Sign `MeshAngleRad` idiom): `TryPlaceBlock` sets the facing from
      player→block yaw snapped to 22.5°; BE persists `meshAngle`, rotates the mesh in `OnTesselation`
      (cached per angle) and the hitbox via `RotatedBox` surfaced by `GetCollision/SelectionBoxes`;
      `IRotatable.OnTransformed` for /we parity. Awaiting in-game facing verification (TESTING.md).
      _Original plan below:_
      `BlockScribeLectern` has NO placement orientation today (no `TryPlaceBlock`), so the open-book face
      ignores the placing player. **Recommended fix = the Sign `MeshAngleRad` idiom (Idiom B)**, which the
      BE already declares it mirrors: override `TryPlaceBlock` to set `BlockEntityScribeLectern.MeshAngleRad`
      from `Block.SuggestedHVOrientation`/player yaw, persist `meshAngle` in `To/FromTreeAttributes`, and add
      an `OnTesselation` that rotates the mesh (+ rotate the selection/collision box). Keeps block code
      `scribelectern` stable (no variant explosion). NOTE: `docs/specs/lectern-gui-polish.md` item 1 is WRONG
      — a horizontalorientation variant group does NOT auto-orient without the `HorizontalOrientable` behavior.
      Front offset is SETTLED (no calibration guessing): the `lecturn-book-open` reading face is SOUTH
      (+Z) at `rotateY=0`, so `MeshAngleRad = atan2(playerX−blockX, playerZ−blockZ)` (vanilla clutter
      formula, optionally 22.5°-snapped) points it straight at the player with ZERO offset. Cache
      tesselation by angle; rotate the hitbox. Trap: use the `bookshelves/lecturn-book-open` shape (root
      -90), not the plain `clutter/` copy (root -45 → diagonal). Files: `BlockScribeLectern.cs`,
      `BlockEntityScribeLectern.cs` (lectern.json needs no structural change). Promote to an OpenSpec
      change before coding. See VSAPI-NOTES "Block placement orientation".
- [~] **A2c. Lectern floor-only placement (CODE — v1-BLOCKING, decided 2026-07-26).** IMPLEMENTED +
      staged 2026-07-26: `CanPlaceBlock` override rejects placement unless the cell directly below has a
      solid up-face (`CanAttachBlockAt(..., BlockFacing.UP)` — the same test vanilla `UnstableFalling`
      uses), with `failureCode = "requiresolidground"` reusing the existing vanilla lang toast. Chose the
      override over the JSON `UnstableFalling` behavior deliberately: that behavior also turns the block
      into a falling physics entity when its support is mined, which would jeopardize the document/pin
      plumbing (pins resolve by position). Composes with the orientation `TryPlaceBlock` for free (base
      calls `CanPlaceBlock` first). Awaiting in-game verification (TESTING.md).
- [~] **A3. Drive the staged polish retests to done.** In-game verify + archive the confirmed items in:
      `refine-settings-and-window-chrome` (16/21), `scribe-notebook-frame` (19/21),
      `scribe-gui-backdrops` (11/18). Most remaining tasks are these retests. See TESTING.md.
- [ ] **A4. Multiplayer / lock / reorder verification (add-lectern-block 7.5–7.7).** Headless server +
      2nd client: live cross-session read-view sync; independent per-lectern docs; editor lock refuses
      2nd editor but allows 2nd reader; drag-reorder + tool panel + text-size persist. All in-game, no
      code — the last gate on "multiplayer-safe."
- [ ] **A5. Survival-safe sanity pass.** Confirm the lectern is craftable/obtainable and usable in a
      real survival world (not just Creative); no Creative-only reach quirks affect survival (ROADMAP
      notes survival open-reach 4.5 < close 5.0 — confirm live).
- [ ] **A6. Freeze scope.** Anything not landed here is v1.1+. Bump `modinfo.json` if needed; confirm
      `game`/`gui` dep versions are current.

## Track B — Media capture (shared input for C, E, F)

Do this ONCE, after the GUI is visually final (post-A3). Everything downstream reuses these.

- [ ] **B1. Hero screenshot** — the single most legible "what is this" shot (lectern open, tasks +
      notebook art). Feeds C (reddit) and F (mod page banner).
- [ ] **B2. Feature screenshots** — task list/check-off, pinned-task HUD in world, settings window,
      notebook backdrop. 3–4 clean shots. Feeds F.
- [ ] **B3. Store raw + final under a tracked path** (e.g. `docs/media/` or `press/`). Note: repo
      currently keeps playtest shots under `screenshots/` — decide press vs. debug separation.

## Track C — Reddit r/vintagestory concept teaser (DO ASAP — pulled to front)

**Priority: NOW (decided 2026-07-26).** Goes out ahead of everything else — "mod coming soon, here's
a picture, what do people think?" Only needs ONE hero shot, not a finished multiplayer pass or final
polish. Concept feedback can then shape the rest of v1. Only real prerequisite: the GUI looks good
enough for a single screenshot (it does — notebook art + settings landed).

- [ ] **C1. Grab a hero screenshot NOW** (the B1 shot, pulled forward) — lectern open with a few tasks +
      the notebook art. Doesn't need to be the final press shot; good enough to represent the concept.
- [ ] **C2. Draft post copy** — 1-line hook + short concept paragraph + the ask ("what do people think
      of the concept?"). Frame as teaser, not launch. No download link yet. Draft in `docs/media/reddit-teaser.md`.
- [ ] **C3. Confirm r/vintagestory rules** — image-post + self-promo / WIP-post rules before posting.
- [ ] **C4. Post & capture feedback** — fold any concept-level feedback into A6 scope-freeze or v1.1.

## Track D — In-game handbook guides (independent)

Follow VS handbook convention. Can run anytime after GUI is final.

- [ ] **D1. Research the convention** — decompile/inspect a vanilla handbook entry (guide pages +
      block `handbook` attributes) so ours matches the standard exactly. Add findings to VSAPI-NOTES.
- [ ] **D2. Draft guide content** — "Using the Lectern" (place, edit tasks/notes, pin to HUD) and a
      short "Pinned-task HUD" page. Match vanilla tone/length.
- [ ] **D3. Wire it in** — handbook pages/attributes + lang keys; verify in-game the entries render
      and link correctly. Promote to an OpenSpec change (touches assets + mod code).

## Track E — Release showcase video (NEW)

Short feature-showcase video with a follow-along script. Needs the final GUI (A3) and ideally A4.

- [ ] **E1. Write the script/shot-list** — beat sheet: hook → place lectern → add/check tasks → pin to
      HUD → HUD in the world → settings/themes → (multiplayer read-sync if shown) → outro/where-to-get.
      Keep it tight (~60–90s). See `docs/media/video-script.md` (to be created).
- [ ] **E2. Capture footage** per the shot list (reuse the B media session's world/setup).
- [ ] **E3. Edit + export**; embed/link on the mod page (F) and optionally the reddit follow-up.

## Track F — Official VS mod DB page draft (LAUNCH VEHICLE — last)

Assembles everything above. Draft the content in-repo (`docs/mod-page.md`), then paste into the mod DB.

- [ ] **F1. Hook** — one compelling sentence.
- [ ] **F2. Description** — what Scribe is, the progression concept, tasks-first framing.
- [ ] **F3. How to use** — obtain the lectern, edit tasks/notes, pin to HUD, settings.
- [ ] **F4. Dependencies & compat** — VS 1.22.x, **hard `gui` (LibGUI) dependency**, Universal /
      required on client+server, multiplayer-safe. Be explicit about the LibGUI requirement.
- [ ] **F5. Pictures + video** — embed B1/B2 and the E video.
- [ ] **F6. Housekeeping** — license (MIT), source-repo link, version, changelog stub.

## Track G — Ship (mechanical, after A–F ready)

- [ ] **G1.** `./build/package.sh` → `Releases/scribe_0.1.0.zip` (local; cloud can't build the mod).
- [ ] **G2.** `git tag v0.1.0 && git push origin v0.1.0` → `release.yml` creates the GitHub Release.
- [ ] **G3.** `gh release upload v0.1.0 Releases/scribe_0.1.0.zip`.
- [ ] **G4.** Publish the mod DB page (F) with the zip / release link; post reddit launch follow-up.

---

### Critical path (shortest route to a truthful launch)

**Now:** Track C (reddit teaser) — grab one hero shot + post ASAP, independent of the rest.

**Then, to launch:** `A1+A2 (error surface + length limit, both v1-blocking) → A3 (polish retests) →`
`A4/A5 (mp+survival verify) → B (final media) → E (video) + F (mod page) → G (ship)`.
Track D (handbook) is independent and can slot in anywhere after A3.

### Settled decisions (2026-07-26)
- A1 (error surface) **and** A2 (length limit) are **both v1-blocking**.
- Reddit teaser (C) is pulled to the **front** — post ASAP, ahead of finishing v1.

### Open decision still to settle
- Press-media location: reuse `screenshots/` or a new `docs/media/` (recommend separate from debug shots).
