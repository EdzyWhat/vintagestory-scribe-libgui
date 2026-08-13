## Context

The editor footer today has a single **"Add task"** button (`ScribeEditorContent.cs:477`)
wired straight to `ScribeDialogBase.OnClickAddTask()` → `scratch.AddTask("")`
(`ScribeDialogBase.Editor.cs:425`). The Core document model already supports both block
kinds — `ScribeBlockKind.Task` (checkbox) and `ScribeBlockKind.Text` (freeform note, no
`Done`) — and `ScribeDocument.AddTextSection(...)` already exists (`ScribeDocument.cs:66`).
The editor already **renders** a `Text` block correctly: rows branch on `data.IsTask`, so a
note gets no checkbox, no "New task…" placeholder, and no length cap
(`ScribeEditorContent.cs:722`, `:738`, `:767`). Read view and persistence already round-trip
mixed kinds (spec `task-note-document`). So the *only* gaps are (1) no UI entry point to
create a `Text` block outside dev tools, and (2) the empty-row self-destruct is task-only.

This change is the interim, note-only slice of the already-resolved **picker-keystone**
design (memory `picker-keystone-resolved`; `docs/vnext-ideas.md §Picker`): upgrade the "New
Task" action to a kind picker (decision 2.1 there), which is the shared entry point future
Tracked/Linked kinds plug into. Those kinds — and the item-picker sub-view / handbook-pin
they need — are explicitly **out of scope** here; we build the entry point extensibly and
register only Task + Note.

Constraints that shape the design:
- `src/Core/` must not reference the VS API — no change needed there anyway (model already
  supports both kinds).
- No new mod dependencies; use the `gui` dep's existing widgets.
- macOS native-button hit-test bug (memory `macos-native-button-hittest-quadrant-bug`) — use
  LibGUI controls, never native chrome, for the affordance.
- `reconcile-animating-surfaces` is converting editor mutations to `RebuildBody()`; the add
  paths already route through it, so the note add must reuse that path (no new ForceRebuild).
- The <500px lectern width makes footer real estate scarce — the picker must add capability
  without adding a second always-visible button (the driving constraint behind choosing a
  dropdown over a row of per-kind buttons, per the resolved design).

## Goals / Non-Goals

**Goals:**
- A player can add a plain **Note** (no checkbox / no completion) from the editor footer.
- The add control is a **kind picker** whose available kinds come from an extensible
  registry, so Tracked/Linked slot in later with no footer restructuring.
- One-click "add a task" stays a one-click task add (no regression, no extra step).
- An abandoned empty **note** self-destructs on blur / switch-to-read / close, exactly like
  an empty task does today.
- Behavior is shared across every `ScribeEditorContent` surface (Lectern, Notebook,
  Clockmaker's Notebook, always-edit tablet).

**Non-Goals:**
- Tracked / Linked task kinds, the item-picker sub-view, and the handbook-pin entry — these
  stay in the follow-up picker change; we only leave the registry seam for them.
- Any Core model or codec change (kinds already exist and round-trip).
- Changing the tablet task-count cap semantics beyond deciding notes are uncapped.
- A distinct visual treatment / styling pass for note rows beyond "no checkbox" (they render
  as text sections today; refining note typography is not in scope).
- Insert-below (Enter) creating notes — Enter stays task insert-below, matching the row flow.

## Decisions

### D1 — Control shape: a segmented "add" button group (primary action + caret) with a floating drop-**up** kind menu, not a stock `Dropdown<T>`
The stock `Gui.Widgets.Input.Dropdown<T>` is a **selection** widget: it displays the current
value and fires `onChanged` when the selection *changes*. That models a persistent setting
(like the font selector), not a repeatable *action* ("add one of kind X now"). Re-selecting
the already-selected kind wouldn't fire `onChanged`, so a stock dropdown can't drive "add
another task" on a repeat pick.

Chosen shape (two parts):

**(a) The affordance is a segmented button group** — a **primary Add button** (label reflects
the last-picked kind, defaults to Task) that performs the add on click, joined flush to a
small **caret toggle** (▾) that opens/closes the kind list. The two read as **one control**
separated only by a thin divider line (no inter-button gap), not two padded buttons. This
preserves the one-click task add (spec requirement "the default add is a task") while adding
note capability in one extra click, and fits the <500px footer width (one grouped control +
narrow caret vs. two full buttons).

*How the segmented look is built (LibGUI has no native segmented/button-group widget, and
`Button` hardcodes a **uniform** corner radius + a **single all-four-sides** border, so a
"one pill, one interior divider" cannot come from `Button` config alone):* wrap a zero-gap
`Row(spacing: 0)` — `[ primary Button (square corners) ][ 1px divider Container (theme Border
color) ][ caret Button (square corners) ]` — inside an outer `Container` with `CornerRadius`
= the footer's standard radius (4) and a rounded-rect **clip**, so only the group's *outer*
corners round while the interior seam stays a straight divider. This reuses stock `Button`'s
hover/press/click-sound for free. *Verify in-game:* that the rounded clip masks the square
child corners crisply at pixel-art scale. Fallbacks if the clip reads soft: (i) square outer
corners too (two flush rects + divider — simplest, but inconsistent with the rounded footer
buttons), or (ii) custom `Container`+`GestureDetector` halves with per-corner `Vector4` radii
(`BoxStyle.CornerRadius` supports per-corner) — the "proper" pill, at the cost of
reimplementing `Button`'s hover/press/sound.

**(b) The kind list is a *floating drop-up* menu that grows upward, *over* the scroll body.**
Because the control lives in the **footer**, a downward menu would clip off the dialog's
bottom edge, so the menu opens *upward*. It is a **floating overlay**, not an inline layout
element: the caret opens a menu anchored above the button that **paints over the scroll body**,
so the scroll area keeps its **exact height** and nothing reflows when the menu opens or closes
(product correction 2026-08-12 — an earlier inline-reveal draft that shrank the scroll body was
rejected; see alternatives). Mechanically this is exactly LibGUI's own `Dropdown` pattern: a
`LayerLink` ties the segmented group (wrapped in a `CompositedTransformTarget`) to a
`CompositedTransformFollower` inserted into the `Overlay`, with **`showAbove: true`** pinning
the follower's *bottom* edge to the button's *top* edge; a full-screen barrier `OverlayEntry`
closes the menu on any outside tap. Picking a kind both sets the primary button's kind *and*
performs that add immediately, then closes the menu. Re-tapping the caret (or activating the
primary button) closes it. The menu grows in via a small scale+fade anchored at `BottomCenter`
(a drop-up twin of `Dropdown`'s downward `DropdownMenu`).

*Alternatives considered:* (a) stock `Dropdown<T>` as the whole control — rejected: action-vs-
selection mismatch above, and it reads as "which kind is selected" not "add one". (b) An
**inline, upward-expanding reveal** (an `AnimatedSize`-wrapped `Column` inserted between the
scroll body and the footer, growing the footer and shrinking the scroll body) — initially
drafted, then **rejected 2026-08-12**: it changes the scroll area's height as it opens, which
the product owner explicitly did not want ("I want the scroll area to remain the same height…
the list to expand from the button over the scroll area"). The floating drop-up above delivers
the same upward reveal without disturbing the scroll body. *(Note: an earlier draft claimed
`CompositedTransformFollower.ShowAbove` had "zero call sites" and was an in-game risk — that
was wrong. `Dropdown` constructs `CompositedTransformFollower`, and `Dropdown` is already used
in the Scribe dialog tree, e.g. the Settings font/policy/anchor selectors — so this path is
well-exercised.)* (c) Two side-by-side buttons "Add task" / "Add note" — rejected: doesn't
scale to 4 kinds within the <500px footer. (d) A **cycling toggle** (caret cycles Task→Note,
no list) — rejected on discoverability and poor scaling to 4 kinds. (e) A right-click /
long-press menu on the Add button — rejected on discoverability (same reason the resolved
design rejected right-click-a-row for item pick).

### D2 — An extensible kind registry drives the menu and the add behavior
Define a small kind descriptor (identifier, display-label lang key, and an `Action`/delegate
that performs the add against `scratch`) and a registry listing the live kinds. The footer
builds its menu from the registry; `OnClickAdd(kind)` dispatches to the descriptor's add
delegate. This release registers exactly two: `Task` → `scratch.AddTask("")` (today's path)
and `Note` → `scratch.AddTextSection("")`. Adding a future kind = one registry entry + its add
delegate; the footer, the widget, and the other kinds are untouched (spec requirement "a
future kind is added without changing the footer contract").

The registry lives in `src/Mod/` (it references `ScribeDocument` mutations and, later, VS-API
item pickers), not Core. Keep it a plain data list of descriptors — no premature interface
hierarchy; the two entries are enough to prove the seam.

*Alternative considered:* hard-code a `switch (kind)` in `OnClickAdd`. Rejected: the whole
point of this change is the extensible entry point; a switch re-introduces the footer-edit
churn each future kind would cause. A flat descriptor list is the minimum that satisfies
"register, don't restructure" without over-engineering.

### D3 — Generalize the empty-row self-destruct from "empty task" to "empty task or note"
Four editing-layer sites gate on `IsTask && string.IsNullOrWhiteSpace(text)` and must widen to
"blank text, either kind":
- `PurgeEmptyTasksFromScratch()` (`Editor.cs:489`) — terminal purge on switch-to-read / close.
- The `pendingEmptyRowRemoval` guard in `OnRenderGUI` (`Lifecycle.cs:125`) — `block.IsTask &&
  IsNullOrWhiteSpace && !stillFocused`.
- `FocusedRowIsEmptyTask()` (`Editor.cs:477`) — autosave-skip predicate for the transient
  empty focused row.
- `OnRowBlurred` scheduling (the site that sets `pendingEmptyRowRemoval`).

The Core model stays neutral (stores verbatim, per `task-note-document`); this is purely the
editing layer. Because the removal path is `DeleteEditorBlock` (already reconcile-aware and
kind-agnostic), widening the *predicate* is the whole job — no new delete/collapse path. Note
this **reverses** the current lectern-gui-shell scenario "Empty text section is not removed";
the delta modifies that requirement accordingly (per the product decision that abandoned empty
notes should not linger).

Rename opportunity: the now-misnamed `PurgeEmptyTasksFromScratch` / `FocusedRowIsEmptyTask` /
`autoFocusRowOnRebuild` comments referencing "task" only — rename to kind-neutral
(`PurgeEmptyRowsFromScratch`, `FocusedRowIsEmptyBlock`) for clarity, since the author-dev-skill
goal favors names that match behavior. Low-risk mechanical rename; do it in the same change.

### D4 — Notes are uncapped by the tablet task-count policy
`scribe-document-policy` caps a tablet at 10 **tasks**. Notes are a different kind and the cap
is task-scoped; the Note add path SHALL bypass `CanAddTaskUnderPolicy()` (which counts tasks).
Task adds keep the existing cap check + `NotifyTabletFull()` surfacing. This matches the
policy's existing framing (it counts tasks and pins, not text sections) and avoids a surprise
"tablet full" on a note when the player has zero tasks.

*Open sub-point folded here:* if a future release wants a total-block cap, that's a policy
change, not this change; we deliberately don't add a note cap now.

## Risks / Trade-offs

- **[Split-control complexity vs. a plain button]** → The split affordance is more UI than the
  one button it replaces. Mitigation: keep it minimal (one button + narrow caret), lean on
  existing LibGUI menu/overlay building blocks, and share it through `ScribeEditorContent` so
  all four surfaces get it from one place. If the caret proves fiddly at small pixel-art
  sizes, fall back to a single "Add ▾" button that always opens the menu (loses the one-click
  task add but is simpler) — a documented fallback, not the default.
- **[Reversing "empty text section is not removed"]** → Any existing content that relied on an
  intentionally-blank spacer note would now lose it on blur. Mitigation: this is a fresh
  interim feature; empty notes were only creatable via dev tools, so no shipped player flow
  produces one. Called out explicitly in the delta so archive-time review sees the reversal.
- **[Empty-note self-destruct races with the reconcile work]** → The blur/purge widening
  touches the same `pendingEmptyRowRemoval` / `RebuildBody` machinery
  `reconcile-animating-surfaces` is actively changing. Mitigation: this change is predicate-
  widening only (no new rebuild trigger), and should land after or be rebased onto the
  reconcile branch's editor-path changes to avoid double-editing `OnRenderGUI`.
- **[Lang / label churn]** → New lang keys for "Note" and the kind menu. Low risk; additive.

## Migration Plan

- No data migration: existing documents already store both kinds; no format or codec change.
- No save-compat concern: a note added by the new UI is a `Text` block, already serialized by
  the shipped codec and rendered by the shipped read/editor views.
- Rollback = revert the `src/Mod/` footer + predicate changes; documents with notes created in
  the interim remain valid (they were always representable).

## Open Questions

- **Primary-button kind memory:** should the primary Add button remember the last-picked kind
  for the rest of the session (so a note-heavy session gets one-click notes), or always reset
  to Task? Leaning "remember within the open dialog, reset to Task on reopen" — cheap, matches
  user momentum — but confirm during implementation against how it reads at the footer.
- **Note placeholder hint:** the task field shows a dimmed "New task…" ghost. Should an empty
  note show an analogous "New note…" hint, or stay blank? Leaning a parallel "New note…" hint
  for symmetry; trivial to add a second lang key. Decide when wiring the row (it's a
  `data.IsTask` branch at `ScribeEditorContent.cs:738`).
- **Menu affordance glyph:** reuse an existing registered SVG caret, or a text "▾"? Resolve
  against the registered icon set during implementation (icon registration lives in
  `lectern-gui-shell` "Custom row-control icons are registered as SVG assets").
