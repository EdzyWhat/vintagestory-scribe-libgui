## 1. Core: independent-clone primitive (D1)

- [x] 1.1 Add an API-free `ScribeDocument.CloneWithNewIdentity()` in `src/Core/` that deep-copies the
      document with a fresh `DocId` and a fresh `TaskId` for every block, preserving text/kind/done/depth
      and leaving the source unmodified.
- [x] 1.2 Add a Core task-count helper (or reuse an existing one) that returns the number of completable
      Task blocks in a document — used for the "overwrite N tasks" prompt. (`ScribeDocument.CompletableCount`)
- [x] 1.3 `tests/Core.Tests`: clone produces all-new Guids (DocId + every TaskId), identical content, and
      the source is untouched; task-count returns the expected N for mixed documents. `dotnet test` green.
      (380 pass)

## 2. Server-authoritative copy operation (D2)

- [x] 2.1 Define a `TranscribeCopy` request message (block position, source slot index, target slot index,
      `allowOverwrite` flag) and register it on the existing Scribe network channel in `ScribeModSystem`.
      (`ScribeTranscribeCopyMessage`, appended after `ScribeSetTrackerQuantityMessage` so wire ids are stable)
- [x] 2.2 Server handler: read the source slot's document, clone it via `CloneWithNewIdentity()` (§1.1),
      write it onto the target slot's item with `ScribeDocumentAttributes.WriteTo`, mark dirty, and let the
      existing inventory sync propagate. (`OnServerReceivedTranscribeCopy`)
- [x] 2.3 Server re-validation: if the target is non-empty and `allowOverwrite` is false, perform no copy
      (defensive gate independent of the client's confirm UX). No-op cleanly if either slot lacks a document.

## 3. Transcribe view layout + rename (D5, modifies scriptorium-inventory)

- [x] 3.1 Add lang key `scribe-tab-transcribe` and the stamp/overwrite/import-export strings; rename the
      Scriptorium document-slot nav button + view heading from "Inventory" to "Transcribe" (this realizes
      the `scriptorium-inventory` MODIFIED tab requirement).
- [x] 3.2 Rebuild `GuiDialogScribeScriptorium.BuildInventoryContent` into a titled `Column`: heading →
      copy section (`Row` of Original slot, seal button, Duplicate slot) → `Divider` → import/export section.
- [x] 3.3 Import/export section is a PLACEHOLDER (D6): render a greyed placeholder slot box (no
      `SlotController` binding, no backing `ItemSlot`) + disabled Export JSON / Export CSV / Import buttons
      with a "coming soon" tooltip; the block-entity inventory stays at its two real (copy) slots.
- [x] 3.4 Wrap the Transcribe body in the shared `Scrollbar`/`SingleChildScrollView` if it overflows the
      fixed central region at the minimum dialog size (mitigation for the overflow risk).
- [x] 3.5 Disabled import/export controls render greyed and no-op on click. (inert `Container`s, no gesture)

## 4. Copy interaction + overwrite confirm (D3)

- [x] 4.1 Wire the seal button to send `TranscribeCopy`; disable it (with an explainer) until both the
      Original and Duplicate slots hold Scribe items.
- [x] 4.2 Two-press state: on a non-empty target, first press → `ConfirmOverwrite` label
      ("Stamp again to overwrite N tasks", N from §1.2 on the synced target document); second press sends
      the copy with `allowOverwrite = true`. Empty target sends immediately on a single press.
- [x] 4.3 Reset the confirm state to `Idle` whenever either slot's contents change. (`OnSlotsChanged`)

## 5. Reusable wooden-stamp copy animation (D4 — metaphor revised to an ink stamp + "COPY" imprint)

- [x] 5.1 Add a pixel-art wooden rubber-stamp PNG (earthen/wood palette) at
      `assets/scribe/textures/gui/scribe-copy-stamp.png`, baked by a re-runnable, swappable generator
      (`build/gen-copy-stamp.py`, PIL). ("I bake it, you refine" — same path, no code change to repaint.)
- [x] 5.2 Build the reusable paint-only `ScribeStamp` widget (self-ticking off the `gui-row-animation-harness`
      registry, mirroring `ScribeSlideIn`'s survival discipline): the wooden stamp fades in, descends,
      squash/tilt press, then lifts + fades, leaving a procedurally-rendered tilted "COPY" block-text imprint
      that pops, holds, and fades. Nearest-neighbour PNG draw via `ScribePixelArtBackdrop`; Transform
      scale/rotate about centre + Opacity.
- [x] 5.3 Trigger the stamp over the Duplicate slot on a successful copy (`PlayStamp` on both send branches;
      generation-keyed so a re-copy replays). Non-load-bearing: the copy is server-authoritative and already
      landed before the flourish plays; a missing asset drops only the wooden image (imprint still plays), and
      the overlay never resizes/blocks the slot. Button relabelled from "Stamp…" to "Copy".

## 6. Verification

- [x] 6.1 `dotnet build` clean (0 warnings); `dotnet test` (Core) green (380). Restaged Debug (109 files).
- [x] 6.2 Copy onto an EMPTY target: single press copies; wooden stamp + "COPY" imprint play; Duplicate shows
      the copied contents. CONFIRMED in-game 2026-08-16.
- [x] 6.3 Copy onto a NON-EMPTY target: first press shows "overwrite N tasks", second press overwrites —
      CONFIRMED. Slot-change-between-presses cancel not explicitly exercised; folded into the backlogged
      multiplayer/polish retest pass (6.8) rather than blocking archive — the confirmed path works.
- [x] 6.4 Independence: after a copy, editing one item's document does not change the other. CONFIRMED
      in-game 2026-08-16.
- [x] 6.5 Manually verify the nav button + heading read "Transcribe" and the slots still reject non-Scribe items.
      CONFIRMED in-game 2026-08-16.
- [x] 6.6 Manually verify the import/export placeholder: section is visible, its slot is an inert greyed
      placeholder, and Export JSON/CSV and Import are disabled and do nothing. CONFIRMED in-game 2026-08-16.
- [x] 6.7 Manually confirm NO save-migration is needed: a Scriptorium placed before this change opens
      cleanly on the Transcribe tab with its two slots and contents intact (no resize occurred).
- [ ] 6.8 Multiplayer: two clients on one Scriptorium — a copy performed by one is reflected for the other;
      no dupe/desync.
      - **Backlogged 2026-08-17** — deferred to a dedicated multiplayer pass (single-client verification of
        the rest of the change is complete). Needs two clients on one world.

## 7. Post-playtest refinements (2026-08-16)

- [x] 7.1 Valid-target rules (client gate + server re-validation). Source may be any Scribe object (even
      empty). Target must be writeable (NOT hardened/fired) AND have room for the incoming task count.
      Modeled as one `ScribeDocumentPolicy`: added Core `ScribeDocument.TaskCount` (Task-kind only, matches
      the editor cap / `MaxBlocks`) and `ScribeDocumentPolicy.CanHold(taskCount)` (inclusive `<=`, false when
      `ReadOnly`). New `IScribeDocumentItem.DocumentPolicy(slot)` (default `Unlimited`; `ItemScribeTablet`
      returns `Tablet`/`UneditableTablet`). Server re-checks `DocumentPolicy(target).CanHold(source.TaskCount)`
      before cloning; client shows distinct disabled tooltips for read-only vs. over-capacity.
- [x] 7.2 Stamp + imprint scale with the Pixel Art Size setting: both drawn at `PixelArtSize × 0.2` px wide
      (600 → 120), spilling over the slot's sides (paint-only, unclipped); descend/lift distances scale too.
      `ScribeStamp` took an `artWidth` ctor param; the caller feeds `ClampPixelArtSize(MySettings.PixelArtSize) × 0.2`.
- [x] 7.3 Imprint text changed "COPY" → "Copied" (`scribe-transcribe-stamp-imprint`).
- [x] 7.4 Copy button moved BELOW the two slots (its own `BuildSealButton` row under the slot `Row`), not between them.
- [x] 7.5 Copy button uses the default theme `ButtonStyle` (same as "Done Editing"); non-`Expanded` so it hugs its text.
- [x] 7.6 Transcribe tab split 50/50 (two `Expanded` halves over the bounded `InnerH`): copy mechanic on top,
      import/export below; each zone's contents vertically centred (`Center`-wrapped Columns). Supersedes §3.4's
      scroll mitigation (the body no longer overflows the fixed region).
- [x] 7.7 All heading + button text on the Transcribe page render in Caudex (`ScribeTaskFont.ButtonFamily`).
- [x] 7.8 An arrow glyph (`ScribeArrowDigraph.RightArrow`, U+2192) sits between the two slots, in the Caudex font.
- [x] 7.9 Empty-slot hover fix: an empty `ScribeDocumentSlot` returns the bare gesture with NO `Tooltip` wrapper,
      so grabbing an item out of a slot immediately drops its (now-empty, tiny) hover card instead of leaving a ghost.
- [x] 7.10 `dotnet build` clean (0 warnings); `dotnet test` (Core) green (387 — added `TaskCount` + `CanHold` coverage).
- [x] 7.11 Manually verify the refinements in-game: button below slots + hugging its text + Caudex; arrow between
      slots; stamp/imprint enlarged & spilling and tracking the Pixel Art Size setting; "Copied" imprint; 50/50
      vertical split with centred zones; copy blocked (with the right tooltip) onto a hardened/fired or full target;
      empty-slot hover card vanishes the instant you grab the item out.

## 8. Post-playtest refinements — round 2 (2026-08-16)

- [x] 8.1 Transcribe nav-button ACTIVE color changed purple → golden-orange: added `ScribeRowConstants.NavActiveTranscribe`
      (`#bb7c31`, a saturated warm gold in the earthen amber family), used by the Transcribe button's `activeColor`
      (Guestbook keeps its plum).
- [x] 8.2 Import/export slot caption reworded "Note to export from, or import into" → "Export from,\nor import into"
      (explicit line break after the comma) and CENTRED (`TextStyle.Align = TextAlignment.Center`) within its column
      under the placeholder slot.
- [x] 8.3 Import/export buttons narrowed to 70% (`IoControlsWidth` 200 → 140) and their labels CENTRED within the
      stretched button (`LabelButton(center: true)` wraps the label in a `Center`).
- [x] 8.4 Slot help text relabelled for clarity: "Original"/"Duplicate" → "Copy from"/"Paste into" (lang keys
      `scribe-transcribe-original`/`-duplicate` renamed to `-copyfrom`/`-pasteinto`).
- [x] 8.5 Stamp pixel-art redrawn (`build/gen-copy-stamp.py`): higher resolution (32×40 → 48×66), taller/less-wide
      aspect (fixes "squished"), and a 3/4 ABOVE perspective — a wooden knob handle + turned neck + round base
      whose top face shows; the red rubber pressing face (previously visible "from underneath") is gone entirely.
- [x] 8.6 Stamp animation retimed: total 850 → 2400ms (all stamp beats ~2× slower); descend/lift travel increased
      (`art×0.25`/`0.22` → `art×0.45`/`0.40`); the press keeps a slight squash peaking at the bottom of travel.
- [x] 8.7 Imprint text ALL CAPS ("Copied" → "COPIED") and made to linger ~3× longer — it pops in as the stamp
      lifts, HOLDS fully visible for ~1s, and only begins fading after ≈1960ms, then a ~440ms fade-out.
- [x] 8.8 `dotnet build` clean (0 warnings); `dotnet test` (Core) green (387).
- [x] 8.9 Manually verify round-2 in-game: Transcribe tab reads golden-orange when active; caption centred & line-broken;
      import/export buttons ~30% narrower with centred labels; "Copy from"/"Paste into" captions; stamp reads as a
      wooden stamp from above (no red underside), less squished, slower, travels further with a slight squash at the
      bottom; "COPIED" imprint lingers ~1s before fading.

## 9. Post-playtest refinements — round 3 (2026-08-16)

- [x] 9.1 Import/export buttons no longer balloon to fill the container: `LabelButton(center: true)` now centres its
      label with a full-width `Row` (`MainAxisSize.Max` + `MainAxisAlignment.Center`) instead of a `Center`, which
      grew to fill BOTH finite axes and stretched the button to its `Expanded` zone's full height. Buttons stay their
      fixed `IoControlsWidth` and hug their labels' height.
- [x] 9.2 Transcribe nav-button ACTIVE color shifted yellower/brighter (`NavActiveTranscribe` `#bb7c31` → `#cf9d2e`,
      hue ~41°) so it's a distinct gold rather than reading like the theme Primary brown / `NavActiveHistory` amber,
      while keeping a warm orange cast.
- [x] 9.3 Added a horizontal `Divider` as the FIRST element of the Transcribe tab (under the title bar), matching the
      read view's leading divider, to separate the title from the two content sections (the mid divider between the
      copy and import/export zones stays).
- [x] 9.4 Added a bottom-right "Show / hide Transcribe features" info button — the Transcribe-tab peer of the editor's
      "Show / hide Editor Features" button (same theme icon Button, `scribeinfo` glyph, tight `All(7)` padding,
      explainer tooltip). It toggles a NEW handbook guide page `craftinginfo-scribe-transcribe`
      (`config/handbook/04-transcribe.json` + `craftinginfo-scribe-transcribe-title`/`-text` lang) describing how the
      tab works. Generalized the base's `ToggleEditorReferenceHandbook` into a reusable
      `ScribeDialogBase.ToggleHandbookPage(pageCode)`.
- [x] 9.5 `dotnet build` clean (0 warnings); `dotnet test` (Core) green (387). Restaged Debug (110 files).
- [x] 9.6 Manually verify round-3 in-game: import/export buttons sit at a tidy fixed width with centred labels (no
      giant Export JSON filling the lower half); the active Transcribe tab reads as a distinct bright gold; a divider
      sits directly under the title; the bottom-right ⓘ opens/closes a "The Transcribe Tab" handbook page and its
      hover tooltip reads "Show / hide Transcribe features". — CONFIRMED (visual review 2026-08-16): buttons sized
      right, gold tab, divider. The ⓘ handbook toggle + tooltip remain to poke; folded into the backlogged
      polish/multiplayer retest pass (6.8) rather than blocking archive.
- [x] 9.7 Info button nudged up-and-left off the corner by 3% of the Pixel Art Size (on top of the 4px base inset),
      so it clears the page edge at every art size (refinement).
- [x] 9.8 Scriptorium slot hover-card delay halved 350 → 175ms (`ScribeDocumentSlot.CardDelay`) so the document
      summary pops up in half the time (refinement). CONFIRMED in-game 2026-08-16 ("the hover is faster" / "faster
      inventory slot hover is good").

## 10. Post-playtest refinements — round 4 (2026-08-16)

- [x] 10.1 Overwrite-confirm text dropped the count: `scribe-transcribe-stamp-confirm` "Copy again to overwrite
      {0} tasks" → "Click again to overwrite all tasks" (no `{0}`); the now-unused `targetTasks` format arg was
      removed from the `Lang.Get` call (`BuildSealButton`). No cancel gesture added — the confirm still resets
      only when a slot's contents actually change (a safety re-arm, not a user-facing cancel; user: "there is no
      cancel — I also don't want there to be").
- [x] 10.2 Stamp animation retimed 2400 → 3000ms (25% longer) and made symmetric: the tilt was REMOVED (the
      wooden stamp presses straight down, `SKMatrix.CreateScale` only); the lift now travels back up the FULL
      descend distance (`liftDistance = descendDistance = art×0.45`) instead of `art×0.40`; and the fade-in and
      fade-out are now the SAME length (`FadeSpan = 0.155`, fade-out over the lift window) — previously 0.155 vs
      0.115, which read as "just fades in place." (`ScribeStamp`.)
- [x] 10.3 "COPIED" imprint: outline BorderThickness 2 → 4 (2× thicker); the classic rubber-stamp LEAN was
      removed so the box sits square; and its width is now pegged to the stamp's bottom via a shared
      `ScribeStampState.ImprintWidthFraction = 0.75f` (`imprintW = art × 0.75`). (`ScribeStamp`.)
- [x] 10.4 Stamp pixel-art base redrawn RECTANGULAR (`build/gen-copy-stamp.py`): the round wooden disk became a
      flat-bottomed rectangular mount block (top face + front thickness band + straight bottom edge) spanning
      `BASE_FRAC = 0.75` of the image width, centred — kept in lockstep with `ImprintWidthFraction` so the base's
      width equals the "COPIED" box's width. PNG regenerated.
- [x] 10.5 Info button offset nudged back down-and-right by 2% of the Pixel Art Size: `cornerInset` factor
      `4f + PixelArtSize×0.03` → `4f + PixelArtSize×0.01` (net inset now ~1% off the corner).
- [x] 10.6 `dotnet build` clean (0 warnings); `dotnet test` (Core) green (387). Restaged Debug (110 files).
- [x] 10.7 Manually verify round-4 in-game: overwrite prompt reads "Click again to overwrite all tasks" (no
      number); the stamp is 25% slower, presses straight down with NO tilt, and after the squish it BOTH fades
      AND lifts back up the same distance it came down (symmetric fade in/out); the stamp's bottom is a flat
      rectangle whose width matches the "COPIED" box; the "COPIED" outline is visibly thicker; the ⓘ button sits
      ~1% off the bottom-right corner.
- [x] 10.8 Tablet Tracker/Link rows now render their item NAME and the Tracker have/need COUNTER ("N / N") in
      the tablet's cuneiform strokes, matching the Task/Text rows (the cuneiform has digits + a slash, so the
      counter is fully representable). New shared `ScribeItemLabel.Build(label, color, style)` returns a
      `CuneiformText` on the tablet path (em/ink/jitter/rotation/glow from the row style) and a wrapping `Text`
      otherwise; `ScribeTrackerCounterText.Build` took an optional `ScribeRowStyle? cuneiform` that renders the
      counter as cuneiform (strong/muted COLOR carries the emphasis since strokes have no bold weight; the
      satisfied strikethrough overlay is kept). Wired into `ScribeReadContent.BuildItemContent` (name Primary,
      counter passed the style) and `ScribeEditorContent.BuildItemEditorContent` (name OnSurface); HUD/Pin callers
      pass no style and are unchanged.

## 11. Post-playtest refinements — round 5 (2026-08-16)

- [x] 11.1 "COPIED" imprint reverted to its pre-round-4 look: the classic rubber-stamp LEAN is restored
      (`ImprintTilt = -0.19f`), the outline is back to the thinner `BorderThickness = 2f`, and the width is back
      to the full art width (`imprintW = art`). Reverts 10.3. (`ScribeStamp`.)
- [x] 11.2 New hand-painted stamp art: `~/Desktop/stamp.png` moved in as
      `assets/scribe/textures/gui/scribe-copy-stamp.png` (80×83 RGBA), replacing the baked PNG. The rectangular-
      base baker edit (10.4) was reverted to the round-base version in `build/gen-copy-stamp.py`, which now carries
      a NOTE that the shipped PNG is hand-painted and re-running the baker would overwrite it.
- [x] 11.3 Squish now presses DOWNWARD into the page instead of squashing about the centerline: the stamp's scale
      Transform is anchored to `Alignment.BottomCenter` so the base stays planted and only the top compresses.
      Press duration shortened 15% (`PressLen = 0.115 × 0.85`) and squash magnitude reduced 40% (squashX
      0.05 → 0.03, squashY 0.08 → 0.048). (`ScribeStamp`.)
- [x] 11.4 Tablet Tracker/Link name + counter render in cuneiform (see 10.8 — implemented this round).
- [x] 11.5 `dotnet build` clean (0 warnings); `dotnet test` (Core) green (387). Restaged Debug (110 files).
- [x] 11.6 Manually verify round-5 in-game: the "COPIED" imprint leans again with the thinner outline at full
      width; the new hand-painted stamp art shows; the squish presses straight down into the page (bottom
      planted), is subtler (~40% less), and is a touch quicker; a tablet's Tracker/Link row shows its item name
      AND the "N / N" counter in cuneiform strokes (digits + slash), in both read and editor views, and HUD/Pin
      counters are unchanged.
- [x] 11.7 Stamp in/out animation reworked (resolves the 11.7 observation): total duration trimmed 30%
      (3000 → 2100ms); the ENTERING (descend) translation now eases `EaseInSine` (accelerates into the page) and
      the LEAVING (lift) translation eases `EaseInOutSine`; the fade-IN plays over the FIRST HALF of the entering
      translation (`[0, DescendEnd/2]`) and the fade-OUT over the SECOND HALF of the leaving translation
      (`[LiftStart + LiftSpan/2, LiftEnd]`), so the stamp is opaque by mid-descent and holds until mid-lift. The
      now-unused `EaseInCubic` helper was removed. (`ScribeStamp`.)
- [x] 11.8 "COPIED" imprint lean softened `-0.19` → `-0.12` (~ -7°). (`ScribeStamp`.)
- [x] 11.9 "COPIED" imprint now paints UNDERNEATH the wooden stamp (z-order swap): the imprint is the ink left ON
      the page, so it's added to the flourish `Stack` FIRST and the descending/lifting stamp passes OVER it.
      (`ScribeStamp`.)
- [x] 11.10 Stamp travel bumped 30% (`descendDistance` `art×0.45` → `art×0.585`; lift matches): the stamp enters
      from and retreats to 30% farther above the slot, traversing more distance in and out. (`ScribeStamp`.)
- [x] 11.11 "COPIED" imprint made 20% SMALLER (`ImprintSizeScale = 0.8` on the box width/height + text font size)
      and its on-screen LIFESPAN trimmed 20% (`ImprintLifeScale = 0.8`, applied by pulling its appearance later —
      start `0.29 → 0.432` — while keeping the fade finishing at t=1 so there's no dead tail). (`ScribeStamp`.)
- [x] 11.12 Manually verify the retimed flourish in-game: the stamp accelerates down into the page and eases
      smoothly back up from ~30% FARTHER away; it fades in over the first half of the descent and out over the
      second half of the lift; the "COPIED" imprint leans a touch less, sits BEHIND the wooden stamp, is ~20%
      smaller, and lingers ~20% less before fading out with the flourish.

## 12. Post-playtest refinements — round 6 (2026-08-16)

- [x] 12.1 "COPIED" imprint now APPEARS INSTANTLY on contact instead of fading/popping in: it snaps to full
      opacity the moment the stamp finishes its entering translation (`t >= DescendEnd`), with no fade-in ramp and
      no pop-scale (`imprintScale = 1`). The fade-OUT window is unchanged. (`ScribeStamp`.)
- [x] 12.2 "COPIED" imprint lean eased `-0.12` → `-0.06` (~ -3.4°, a subtle lean). (`ScribeStamp`.)
- [x] 12.3 Wooden stamp shifted 1% of the pixel-art width farther DOWN the page at every phase
      (`offsetY += art × 0.01`), so the imprint lands slightly lower. (`ScribeStamp`.)
- [x] 12.4 `dotnet build` clean (0 warnings). Restage Debug PENDING (client was running at build time).
- [x] 12.5 Manually verify round-6 in-game: the "COPIED" imprint pops on instantly the instant the stamp touches
      down (no fade-in), leans just a touch (~-3.4°), and the whole stamp sits a hair lower on the page.
- [x] 12.6 "COPIED" imprint label now renders in **Caudex** (`ScribeTaskFont.ButtonFamily`, the mod's title face),
      and its bordered box HUGS the text: the `Container` dropped its fixed width/height so it shrink-wraps to the
      Caudex label + padding (centred in the slot via a full-slot `Center`), keeping the outline correctly framed
      under the new font metrics. Padding bumped 4/2 → 6/3. (`ScribeStamp`.)

## 13. Post-playtest refinements — round 7 (2026-08-17)

- [x] 13.1 Fix "COPIED" imprint wrapping to ~2 chars per line: task 12.6's shrink-wrap + full-slot `Center`
      capped the text's max width at the slot (~48px), forcing the bold Caudex label to wrap. Widened the
      imprint's `Positioned` box from `slot` to `art` (~120px, the same footprint the wooden stamp uses),
      centred on the slot and spilling over its sides (unclipped, paint-only), so "COPIED" lays out on one
      line. (`ScribeStamp`.)
- [x] 13.2 `dotnet build` clean (0 warnings); restaged Debug (client not running).
- [x] 13.3 Manually verify in-game: the "COPIED" imprint renders on a SINGLE line (all six letters), centred
      on the Duplicate slot, still leaning ~-3.4° with the hugging outline.

- [x] 13.4 "COPIED" imprint enlarged 15% (`ImprintSizeScale` 0.8 → 0.92) and its fade-OUT window halved
      (`ImprintFadeStart` 0.8536 → 0.9268; same end point, twice as fast). (`ScribeStamp`.)
- [x] 13.5 "COPIED" imprint OUTER GLOW: a zero-offset, blurred (`art × 0.03`), slightly-spread (`art × 0.04`)
      rounded-rect `BoxShadow` in the theme's parchment `Surface` colour (`0.6` alpha) behind the mark. The
      transparent box fill lets the parchment blob back the letters AND halo outward, lifting the mark off busy
      content. New `glowColor` param threaded from `GuiDialogScribeScriptorium.BuildStampOverlay`. (`ScribeStamp`.)
- [x] 13.6 Stamp LANDING SHADOW: a static, soft, dark rounded-rect contact shadow (`BoxShadow`, black @`0.7`
      alpha, blur `art × 0.1`), sized to the stamp base (`art × 0.55` wide, `art × 0.25` tall), centred on the
      slot. Fades in over the descend `[0, DescendEnd]`, holds through the press, fades out over the FULL lift
      `[LiftStart, LiftEnd]` (starts the instant the up-translation begins); never moves. Drawn ABOVE the imprint,
      BELOW the wooden stamp. (`ScribeStamp`.)
- [x] 13.7 `dotnet build` clean (0 warnings); restaged Debug (client not running).
- [x] 13.8 Manually verify in-game (refine-together pass): (a) "COPIED" is 15% larger, reads clearly over the
      slot content thanks to the parchment glow, and its fade-out is snappier; (b) a soft dark shadow fades in
      under the descending stamp showing where it lands, holds during the press, and fades out as the stamp
      lifts — static throughout, sitting above the ink but below the stamp. CONFIRMED in-game 2026-08-17.

## 14. Package the stamp flourish for reuse (2026-08-17)

Generalize the single-slot, fixed-"COPIED" flourish so any slot can be stamped with any word — groundwork for
the Import/Export section stamping "Imported"/"Exported". No visual change to the existing copy flourish.

- [x] 14.1 `ScribeStamp.CopyLabel` already parameterizes the imprint text (no widget change needed); the caller
      now passes the label through instead of the widget hard-coding it.
- [x] 14.2 Generalize the host state in `GuiDialogScribeScriptorium`: replaced the single `stampPlaying` bool with
      `stampTargetSlot` (int, −1 = idle) + `stampLabel` (string). `PlayStamp(int targetSlot, string label)` sets
      both; `BuildStampOverlay(int slotIndex)` mounts the overlay only on the slot whose index matches
      `stampTargetSlot`, so the same call site serves the copy's Duplicate slot today and the import/export slot
      later. `OnStampEnded`/Dispose reset the target to −1.
- [x] 14.3 Copy path funnels through `StampCopy()` → `PlayStamp(TargetSlotIndex, Lang.Get("…-stamp-imprint"))`;
      the "COPIED" flourish is byte-identical to before.
- [x] 14.4 Added `…-stamp-imprint-imported` = "IMPORTED" and `…-stamp-imprint-exported` = "EXPORTED" lang keys,
      ready for the Import/Export section (not yet wired — that section is still the D6 placeholder).

## 15. Copy-mode radio: Overwrite / Append (2026-08-17)

A radio set under the Copy button chooses the copy BEHAVIOR: **Overwrite** (replace the target's tasks — the
original behavior) or **Append** (add the source's tasks onto the target, nothing deleted). Server-authoritative.

- [x] 15.1 Core: `ScribeDocument.AppendClonedBlocksFrom(other)` appends fresh-`TaskId` copies of `other`'s blocks
      onto the END of this document, leaving this doc's identity/title/existing blocks untouched. Shared block-clone
      helper `CloneBlockWithNewTaskId` extracted from `CloneWithNewIdentity`. API-free (`src/Core/`).
- [x] 15.2 Core.Tests: 4 new facts (append order, fresh-ids + kept target DocId, source-unchanged, empty-target
      behaves like copy). Full Core suite green (391).
- [x] 15.3 Packet: `ScribeTranscribeCopyMessage.Append` (ProtoMember 7). When true the server appends and ignores
      the overwrite gate (append is non-destructive); when false the original replace-with-overwrite-gate path runs.
- [x] 15.4 Server (`OnServerReceivedTranscribeCopy`): branch on `Append`. Append keeps the target's doc and calls
      `AppendClonedBlocksFrom(source)`; overwrite clones-with-new-identity as before. Capacity re-check is
      mode-aware: append checks target-blocks + source-blocks, overwrite checks source-blocks.
- [x] 15.5 Client radio: `CopyMode` enum (Overwrite default / Append); `BuildCopyModeRadio` builds two LibGUI
      `RadioButton<int>` (enum→int, since `RadioButton<T>` needs `IEquatable<T>`), themed to the parchment palette,
      label in the PLAYER'S chosen body font (`ScribeTaskFont.Resolve`), NOT Caudex. Sits directly under the Copy
      button. Each option carries an explainer tooltip.
- [x] 15.6 Client button logic: Append single-presses always (no confirm — non-destructive); Overwrite keeps the
      empty-target-single-press / non-empty-two-press-red-confirm UX. Capacity gate + resulting-count are mode-aware.
      Switching modes clears any armed overwrite confirm.
- [x] 15.7 `dotnet build` clean (0 warnings); Core tests green (391); restaged Debug (client not running).
- [x] 15.8 Manually verify in-game: (a) radio renders under the Copy button, labels in the player's body font,
      Overwrite selected by default; (b) OVERWRITE onto a non-empty Duplicate still shows the two-press red confirm
      and replaces; (c) APPEND onto a non-empty Duplicate copies on a SINGLE press and ADDS the Original's tasks
      below the existing ones (nothing deleted), stamping "COPIED"; (d) append respecting the tablet cap (target +
      source must fit, else the button greys with the "holds at most N" tooltip); (e) switching Overwrite→Append
      mid-confirm cancels the armed confirm. CONFIRMED in-game 2026-08-17 (radios in a single centred row).
