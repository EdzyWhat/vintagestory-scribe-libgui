## 1. Add shelf/bookshelf/cabinet opt-in attributes to item JSON

- [x] 1.1 In `scribenotebook.json`, add to `attributes`: `"shelvable": "Quadrants"`,
      `"bookshelveable": true`, a `"displayable": { "shelf": { "size": { ... } } }` block
      (seed `size` from vanilla `book.json`), and an `"onshelfTransform"` (seed from the
      item's existing `groundStorageTransform`).
- [x] 1.2 In `scribeclockmakernotebook.json`, add the same four attributes (identical mesh to
      the notebook — reuse the notebook's values as the seed).
- [x] 1.3 In `scribetablet.json`, add the same four attributes to the base `attributes` block,
      seeding `onshelfTransform` from the tablet's tuned `groundStorageTransform`
      (`translation {-0.018, 0, -0.025}`, rotation y:35, origin {0.5, 0.05, 0.5}).
- [x] 1.4 Determine whether the tablet's `attributesByType` branches (`*-hard`, `*-fired`,
      `*-wax`) inherit or shadow the base `attributes`. If they shadow, duplicate the four
      new attributes into each branch (matching how `groundStorageTransform` is already
      duplicated per branch in that file).
      - **Resolved (corrected 2026-08-07):** an earlier note here claimed `attributesByType`
        REPLACES the base `attributes` — that was WRONG. Verified against
        `RegistryObjectType.solveByType` (VSEssentials.dll) and the game's own Newtonsoft
        DLL: the matched branch is **deep-merged onto** the base block
        (`base["attributes"].Merge(branch)`, no merge settings). Object keys OVERWRITE
        (so a branch value identical to base is a no-op), but ARRAYS CONCATENATE.
      - Consequence 1: the per-branch transform duplicates (`groundStorageTransform`,
        `displayable`, `onshelfTransform`) bought nothing for `*-hard`/`*-fired` — those
        share the clay mesh, so they now inherit from base. Only `*-wax` keeps its own
        transform block (distinct, smaller mesh: X0..10/Z0..11 vs clay X0..12.4/Z0..15.2).
      - Consequence 2 (latent bug this refactor fixed): because arrays concat, the old
        per-branch `handbook.extraSections` was APPENDING to base's — hard/fired resolved
        to 6 sections (about/hud-ref listed twice), wax to 5. Converted `handbook` →
        `handbookByType` with a `*` fallback so each variant gets exactly its own list
        (`...ByType` replaces instead of concatenating). Verified: wet=3, hard=3, fired=3,
        wax=2, no dupes.

## 2. Stage and load-test

- [x] 2.1 Restage the mod (`bash build/restage.sh Debug`) and confirm the game loads with no
      JSON parse errors or asset warnings for the three items.
      - **Done 2026-08-06:** all three JSONs parse (`json.load` clean); `restage.sh Debug`
        built with 0 warnings / 0 errors, 93 files staged. In-game asset-load confirmation
        is part of the manual §3 tests below.
- [x] 2.2 Confirm `dotnet build` / verify.sh stays green (this is JSON-only, so no code
      regression is expected — sanity check only).
      - **Done 2026-08-06:** restage's `dotnet build` (Core + Mod) succeeded, 0 warnings /
        0 errors. JSON-only change, no code touched.

## 3. In-game placement verification (all three surfaces)

- [x] 3.1 Manually test: place a Notebook on a **general shelf** — it accepts, renders, and
      can be retrieved.
- [x] 3.2 Manually test: place a Notebook on a **bookshelf** — accepts, renders, retrieves.
- [x] 3.3 Manually test: place a Notebook in a **cabinet** — accepts (no "too large" error),
      renders, retrieves. If "too large" fires, shrink `displayable.shelf` `size`.
- [x] 3.4 Manually test: repeat 3.1–3.3 for the Clockmaker's Notebook.
- [x] 3.5 Manually test: repeat 3.1–3.3 for a **wet clay Tablet**, a **hardened** Tablet, a
      **fired** Tablet, and a **wax** Tablet (confirms every `attributesByType` branch is
      shelvable, per task 1.4).
      - **Confirmed 2026-08-07** (live playtest): all three items place/render/retrieve on
        shelf, bookshelf, and cabinet. The CM Notebook needed its `displayable.shelf` height
        lowered 6→5 (was the only one too tall for a general-shelf slot); all tablet variants
        (wet/hard/fired/wax) are shelvable. `randYRotAngle:0` locks the cabinet placement angle.

## 4. Document identity preservation

- [x] 4.1 Manually test: shelve a Notebook that has tasks + notes, retrieve it, reopen —
      confirm the same tasks and notes are present (docId survived the shelf inventory).
- [x] 4.2 Manually test: shelve a **hardened** clay Tablet, retrieve it — confirm it is still
      hardened and its edit-lock behaves as before shelving.
      - **Confirmed 2026-08-07** (live playtest): documents keep all their data across
        shelve → retrieve → reopen; hardened tablets retain their state and edit-lock.

## 5. Transform tuning pass

- [x] 5.1 Tune each item's `onshelfTransform` in-game until the model sits within slot bounds
      on shelf, bookshelf, and cabinet without clipping/floating (break-and-replace to
      re-tesselate after each edit; screenshot-compare, same method as the ground-storage
      fix). Confirm the wax tablet's distinct mesh looks right with the shared transform, or
      give it its own.
      - **Confirmed 2026-08-07** (iterative screenshot tuning): final values — clay lift y:0.12
        scale 1, wax lift y:0.12 scale 1.15, shared rotation `{x:-90, z:90}` for the
        narrow-edge-forward (spine-out) look matching books/notebooks. Wax got its OWN
        transform block (smaller mesh) via `attributesByType."*-wax"`; clay wet/hard/fired
        share the base block through the `attributesByType` deep-merge.
- [x] 5.2 Record per-surface verdicts in `TESTING.md` (via the `what-to-test` skill) and
      restage the final values.
      - **Done 2026-08-07:** 8 items recorded under the `add-shelf-placement-for-scribe-items`
        group in `TESTING.md`, all Confirmed. Final values restaged and pushed (commit 50f92f6).

## 6. Docs

- [x] 6.1 ~~Note shelf/bookshelf/cabinet placement on the wiki Items and Tablets pages (and the
      in-game handbook if it lists placement affordances).~~ **DESCOPED 2026-08-12.**
      - **2026-08-07 decision:** in-game handbook SKIPPED — the tablet/notebook "about" entries
        don't currently list any placement affordance (not even ground storage), so this would
        be net-new content, not an addition to an existing list.
      - **2026-08-12 decision (author):** wiki note DESCOPED entirely. Putting objects on shelves
        is a natural, self-discoverable Vintage Story interaction — it doesn't warrant a dedicated
        wiki callout. Not release-gating; nothing left to do. Change is code-complete + playtested.
