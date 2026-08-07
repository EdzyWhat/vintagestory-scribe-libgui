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
      - **Resolved:** `attributesByType` REPLACES the base `attributes` (the file already
        re-declares `groundStorageTransform` in every branch). Added the four opt-in
        attributes to the base block AND all three branches (`*-hard`, `*-fired`, `*-wax`).
        Base block covers the three wet clay variants (which match no branch pattern).

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

- [ ] 3.1 Manually test: place a Notebook on a **general shelf** — it accepts, renders, and
      can be retrieved.
- [ ] 3.2 Manually test: place a Notebook on a **bookshelf** — accepts, renders, retrieves.
- [ ] 3.3 Manually test: place a Notebook in a **cabinet** — accepts (no "too large" error),
      renders, retrieves. If "too large" fires, shrink `displayable.shelf` `size`.
- [ ] 3.4 Manually test: repeat 3.1–3.3 for the Clockmaker's Notebook.
- [ ] 3.5 Manually test: repeat 3.1–3.3 for a **wet clay Tablet**, a **hardened** Tablet, a
      **fired** Tablet, and a **wax** Tablet (confirms every `attributesByType` branch is
      shelvable, per task 1.4).

## 4. Document identity preservation

- [ ] 4.1 Manually test: shelve a Notebook that has tasks + notes, retrieve it, reopen —
      confirm the same tasks and notes are present (docId survived the shelf inventory).
- [ ] 4.2 Manually test: shelve a **hardened** clay Tablet, retrieve it — confirm it is still
      hardened and its edit-lock behaves as before shelving.

## 5. Transform tuning pass

- [ ] 5.1 Tune each item's `onshelfTransform` in-game until the model sits within slot bounds
      on shelf, bookshelf, and cabinet without clipping/floating (break-and-replace to
      re-tesselate after each edit; screenshot-compare, same method as the ground-storage
      fix). Confirm the wax tablet's distinct mesh looks right with the shared transform, or
      give it its own.
- [ ] 5.2 Record per-surface verdicts in `TESTING.md` (via the `what-to-test` skill) and
      restage the final values.

## 6. Docs

- [ ] 6.1 Note shelf/bookshelf/cabinet placement on the wiki Items and Tablets pages (and the
      in-game handbook if it lists placement affordances). Not release-gating.
