## Context

See proposal.md - Why/What Changes for motivation and scope. Three relevant facts about the
current code:

- `assignmentdesk.json` and `inbox.json` both set `shape: { base: "scribe:block/lectern/lectern" }`
  directly — a reference into another block's shape file, with its own PLACEHOLDER MODEL comment
  explaining why (dated 2026-08-30, see `add-assignment-and-quest-support` §13.1/§13.2).
- `tasknotice.json` sets `shape: { base: "scribe:item/tasknotice" }`, one file, used for both
  the blank and sealed states; `ItemScribeTaskNotice.IsSealed(ItemStack)` already exists and is
  the sole source of truth for which state a given stack is in (used today only for tooltip
  text and interaction gating).
- Grepping this mod and the local vsapi/vssurvivalmod/vsessentialsmod clones confirms
  `Item.OnBeforeRender` has never been used in this codebase before. The closest working
  precedent is vanilla's `CollectibleBehaviorCustomTongedShape.OnBeforeRender`
  (`vssurvivalmod/CollectibleBehavior/CollectibleBehaviorCustomTongedShape.cs`): load an
  alternate shape by `AssetLocation`, `capi.Tesselator.TesselateShape(collObj, shape, out var
  meshdata)`, `capi.Render.UploadMultiTextureMesh(meshdata)`, cache the resulting
  `MultiTextureMeshRef`, and dispose all cached refs in `OnUnloaded`.

## Goals / Non-Goals

**Goals:**
- Every one of the four targets (Desk, Inbox, Task Notice blank, Task Notice filled) has its
  own shape file and its own texture file(s) that can be opened and edited in Blockbench without
  touching any other block/item's files.
- The Task Notice's blank/filled model swap is driven by existing state (`IsSealed`), with no
  new persisted data and no change to the item's code, stacking, or crafting.
- The mesh-swap mechanism is cheap: each of the two meshes is tesselated once and reused, not
  rebuilt every frame or every time a stack changes state.

**Non-Goals:**
- Producing final art for any of the four targets — see proposal.md's scope boundary.
- A general-purpose "per-stack shape variant" framework for other Scribe items. This change
  solves the Task Notice's specific two-state case; if a future item needs a similar swap,
  revisit then rather than generalizing preemptively.
- Changing how the Desk/Inbox blocks resolve their *textures* file naming beyond a straight
  clone — texture key names inside the cloned shape stay whatever the Lectern already used
  internally (e.g. `mahogany`, `ebony-ornate`); only the folder/domain they live under moves
  from `lectern` to `assignmentdesk`/`inbox`.

## Decisions

### 1. Folder layout for the four new/relocated model files

Match the Scriptorium's own existing convention (one folder per block, named after the block,
shape file inside it named after the block, `.bbmodel` Blockbench source kept alongside it):
- `shapes/block/assignmentdesk/assignmentdesk.json` + `assignmentdesk.bbmodel`,
  `textures/block/assignmentdesk/*.png`
- `shapes/block/inbox/inbox.json` + `inbox.bbmodel`, `textures/block/inbox/*.png`
- `shapes/item/tasknotice/blank.json`, `shapes/item/tasknotice/filled.json`,
  `textures/item/tasknotice/blank.png` (+ `blank-tie.png`), `textures/item/tasknotice/filled.png`
  (+ its own tie/seal texture)

The Task Notice differs slightly from the Desk/Inbox in that its shape file is named for the
*state* (`blank`/`filled`), not the item, since both live under the same item code and folder.
Alternative considered: keep `tasknotice.json` at its current path for the blank state and only
add a sibling `tasknotice-filled.json` next to it. Rejected — leaving the blank shape's filename
unchanged while adding a sibling with a different naming convention (hyphen-suffixed vs. its
own folder) would read as inconsistent next to the Desk/Inbox's folder-per-thing convention.

### 2. Per-stack mesh swap via `Item.OnBeforeRender`, two fixed cache keys

`ItemScribeTaskNotice` gains:

```csharp
public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack,
    EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
{
    string cacheKey = IsSealed(itemstack) ? TaskNoticeFilledMeshCacheKey : TaskNoticeBlankMeshCacheKey;
    renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate(capi, cacheKey,
        () => TesselateTaskNoticeVariant(capi, IsSealed(itemstack) ? FilledShapeLoc : BlankShapeLoc));
    base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
}

public override void OnUnloaded(ICoreAPI api)
{
    DisposeCachedMeshRef(api, TaskNoticeBlankMeshCacheKey);
    DisposeCachedMeshRef(api, TaskNoticeFilledMeshCacheKey);
    base.OnUnloaded(api);
}
```

Only two possible states exist (unlike `CollectibleBehaviorCustomTongedShape`'s per-material
dictionary, keyed because tonged shapes vary by metal), so two fixed string cache keys are
enough — no dictionary needed. `BlankShapeLoc`/`FilledShapeLoc` are `new AssetLocation("scribe",
"item/tasknotice/blank")` / `".../filled"` built directly, rather than reusing and mutating the
itemtype's own registered `Shape.Base` via `WithPathPrefixOnce`/`WithPathAppendixOnce` (the
vanilla precedent's approach) — those two methods mutate the `AssetLocation` in place and return
`this`, and the itemtype's `Shape` field is also used elsewhere for this item's default/fallback
rendering, so mutating it in place risks corrupting that shared reference. Building fresh
locations for the two alternates sidesteps the shared-mutation footgun entirely.

`OnBeforeRender` runs every render frame per visible stack, so re-evaluating `IsSealed(itemstack)`
each call is what makes the model swap "live" the instant a notice's document is populated or
cleared (satisfies the spec's second scenario) — no cache invalidation logic needed, since the
cache is keyed by state, not by stack identity.

Alternatives considered:
- **Split into two item codes** (`tasknotice-blank`/`tasknotice-filled`). Rejected per the
  clarifying answer already given: it would require rewriting the existing in-place
  attribute-mutation state-transition logic (seal/unseal), recipes, and creative-inventory
  entries for no benefit over the render-only swap.
- **Re-tesselate every `OnBeforeRender` call with no cache.** Rejected — tesselation + GPU
  upload is not something to redo every frame for every rendered stack; the vanilla precedent
  caches for the same reason.

### 3. Clone from the Scriptorium instead of the Lectern, including its `textures` override block

Switched mid-change from the originally-planned Lectern donor to the Scriptorium: the
Scriptorium already ships an editable `.bbmodel` Blockbench source next to its generated
`.json` (the Lectern has only a bare hand-authored/decompiled JSON, no editable source), so
starting from it means whoever does the real art pass later can open a real Blockbench project
immediately instead of hand-editing raw shape JSON first.

This changes the plumbing shape, not just the donor file: the Scriptorium's shape pulls its
texture keys from a blocktype-level `textures` override dict in `scriptorium.json` (its own
comment explains why — a shape's own embedded `textures` are editor-only for blocks; the
blocktype's dict is what actually resolves at render time), because its combined shape merges
four vanilla clutter shapes' worth of texture keys. The Lectern's shape didn't need this — its
textures self-resolve without a blocktype-level override. So cloning the Scriptorium means
`assignmentdesk.json`/`inbox.json` each need their own 9-key `textures` block added (pointing at
`scribe:block/assignmentdesk/<key>` / `scribe:block/inbox/<key>`), not just a `shape.base`
repoint.

Clone `scriptorium.json`, `scriptorium.bbmodel`, and its nine textures into each new folder
unmodified first (renaming the `.json`/`.bbmodel` to match each block's own name), verify the
block renders with the new local shape + textures wired in (same look as the Scriptorium,
since nothing about the geometry/textures changes yet — see Risks), then update each
blocktype's placeholder comment to say the model is now a locally-owned, Scriptorium-derived
starting point pending real art, rather than removing the comment outright.

## Risks / Trade-offs

- **[Risk]** Forgetting to dispose the two cached `MultiTextureMeshRef`s leaks GPU memory across
  world reloads (the vanilla precedent disposes explicitly in `OnUnloaded` for this reason).
  → Mitigation: implement `OnUnloaded` alongside `OnBeforeRender` in the same task, and verify
  via a manual double-reload smoke test (join world, leave, rejoin) that this doesn't error.
- **[Risk]** Cloning the Scriptorium's shape file verbatim carries over its combined
  book-stack/ink-and-quill geometry and all nine texture keys into the Desk/Inbox clones, none
  of which are Desk/Inbox-specific. → Mitigation: this is intentional per the updated scope (an
  editable starting point, not final geometry); any stripping/reshaping of unused Scriptorium
  elements happens in the later real art pass, not this change.
- **[Trade-off]** Assignment Desk, Inbox, and Scriptorium will render as three visually
  identical blocks until the later art pass diverges them — a direct consequence of starting
  both new models from a clone of the Scriptorium's shape. Accepted trade-off: the benefit (an
  editable `.bbmodel` source ready to modify directly, versus reworking a bare Lectern JSON
  with no editable source) outweighs the temporary visual duplication, and this is asset
  plumbing only — no player-facing release ships in this state.
- **[Trade-off]** The filled Task Notice's placeholder shape is intentionally minimal (a small
  structural difference, not real art), so testers/players will see a rough "is this sealed?"
  visual cue rather than a finished look during this window. Acceptable given proposal.md's
  explicit scope boundary.
