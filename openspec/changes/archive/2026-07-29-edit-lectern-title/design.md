## Context

`ScribeDocument` has no title today. All GUI views have no header above their central region —
`BuildCentralRegion()` dispatches directly to `BuildReadContent()`, `BuildEditorContent()`, or
`BuildPinnedContent()`. The full in-game document save flow goes through `FlushIfDirty()` →
`ScribeEditDocumentMessage` (document bytes) → server. The codec already has a version field
and handles older versions — this is a standard version bump.

## Goals / Non-Goals

**Goals:**
- Per-Lectern title visible in all views; editable via pencil-click in edit view only.
- 80-char cap enforced by the input widget and clamped on deserialize.
- Empty/blank title saves as `"Lectern"`.
- No new packet type — title travels in existing document bytes.

**Non-Goals:**
- Title shown outside the GUI (e.g. block tooltip, world label).
- Per-player private titles.
- Title history or undo.

## Decisions

### D1: Title on `ScribeDocument`, not a separate BE attribute
Putting `Title` on the document means it serializes through the existing codec and travels
in `ScribeEditDocumentMessage` for free. The alternative — a separate BE tree attribute with
its own packet — would add a new packet type and a second save path for a single field.
Rejected: unnecessary complexity.

### D2: Codec version bump — new version reads/writes Title; prior version supplies default
The codec already has a version byte and an "unsupported old version" guard. Bumping the
version and adding a `Title` field read/write is the established pattern. On deserializing
the previous version, `Title` is absent — supply `"Lectern"` as the default. Versions older
than the previous one already fail fast (existing guard unchanged).

### D3: `_isTitleEditing` bool flag on the dialog, not a new `ScribeLecternView` variant
The title row sits above the central region, not inside it. A bool + `ForceRebuild()` is
simpler and consistent with how other stateful UI conditions are handled (e.g. `lockHolderUid`
affecting the editor button state). No new enum variant needed.

### D4: Reuse `"scribeedit"` SVG — existing pencil icon
The `"scribeedit"` glyph is already registered as a pencil/edit icon and is used on the nav
column's Edit button. Using it for the title pencil is consistent and requires no new asset.
The nav column and the title row are spatially distinct — no visual ambiguity.

### D5: LibGUI stock `TextField` for the inline input
`ScribeNumericField` already uses LibGUI's `TextField` with a `TextEditingController` and
`FocusNode`. The same pattern applies here: a `FocusNode` listener fires `OnTitleBlur()` when
focus is lost, which trims, defaults, clamps, writes `scratch.Title`, calls `ForceRebuild()`,
and calls `FlushIfDirty()`. The `TextField` widget's `MaxLength` property (if available) or a
`onKeyDown` guard enforces the 80-char cap at the input layer.

### D6: `BuildDocumentHeader(bool editable)` — shared helper called from all three view builders
Rather than duplicating the title row in three `BuildXxxContent()` methods, a single
`BuildDocumentHeader(editable)` helper is called at the top of each. This keeps the three
view builders unchanged except for the header call insertion. `editable: true` only in
`BuildEditorContent()`.

## Risks / Trade-offs

- **Old saves show "Lectern":** Any Lectern placed before this change deserializes with
  `Title = "Lectern"`. This is the intended default — no data loss. [Acceptable]
- **`ForceRebuild()` on blur causes a full dialog recompose:** This is how all other state
  changes work in this dialog (tab switches, settings changes). The recompose is fast and
  invisible in practice. [Known, acceptable]
- **`"scribeedit"` icon already on the nav button:** Both uses are visually distinct by position
  (nav column vs. title row). If this ever feels confusing a dedicated icon can be added later.
  [Low risk]
