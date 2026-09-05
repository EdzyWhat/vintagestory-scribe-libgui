## Context

See proposal.md - Why. The existing prompt plumbing (`ScribeModSystem.Quest.cs`) already queues a
`ScribeQuestPrompt` (`Source`, `QuestCode`, `Title`, `IsCompletion`) into `PendingQuestPrompts` and
exposes `AcceptQuestPrompt`/`DismissQuestPrompt`; `HudScribePins` renders that queue today as one
fixed banner style via `HudScribePinsContent`. This change only touches rendering/policy-routing, not
detection or queuing (design's Impact in proposal.md already reflects this).

This change also depends on `fix-libgui-click-draw-order-mismatch` (Change 3, this session) for
`ScribeVanillaDialogGuard` — the modal style is not safe to auto-open without it (see that change's
design.md for why: vanilla dialogs always paint over Scribe regardless of `DrawOrder`, and click
dispatch order doesn't track paint order). If archive order ends up reversed relative to that change,
reconcile per the project's own documented archive-order gotcha (memory:
`openspec-archive-order-header-drift`).

`settings-tab`'s current merged spec still says "gated on VS Quest" — already superseded in the
actual codebase by `add-progression-framework-quest-support`'s change to `ScribeSettingsDialog.cs`
(gates on either backend, confirmed by reading that file this session). This change's `settings-tab`
delta is written against that actual current behavior, not the stale merged text — same archive-order
caveat as above.

## Goals / Non-Goals

**Goals:**
- Two selectable accept-prompt presentation styles; the existing (HUD) style visually polished either
  way.
- The new modal style is never unsafe (never contests a click against a vanilla dialog).

**Non-Goals:**
- `ScribeQuestCompletionPolicy` gains no fourth value and no modal style — a completion-prompt always
  uses the (now-polished) HUD banner. The user's request was specifically about the *accept* moment
  ("how should Scribe get the quest ingested"); completion notifications are a separate, unopened
  question left for later if it comes up.
- Per-pixel-perfect z-order between the modal and a vanilla dialog — the guard (Change 3) is a
  coarse "is anything vanilla open at all" check, not bounds-overlap detection (see that change's own
  Non-Goals).
- Exact color values beyond the three named in proposal.md (green/red/near-white) are an
  implementation detail for tasks.md, not a spec-level requirement (the spec requires "distinct
  colors conveying the described roles," not literal hex values).

## Decisions

**Reuse the existing row-animation infrastructure (`ScribeAnimatedList`/`ScribeRowSizeAnimation`) for
the banner's on-appear draw-attention animation, rather than inventing a new animation primitive.**
This codebase already has a proven, shipped pattern for "a row animates in" (memory: "Animate row
insertion," `gui-row-insertion-animation` capability) — the quest-prompt banner appearing is
structurally the same event (a new row entering the HUD's content).

**The modal is a `WindowFrame`-hosted `GuiDialog`, mirroring `ScribeSettingsDialog`'s own
construction** (a LibGUI `GuiBase` dialog opened via `TryOpen`), not a HUD-embedded overlay — it needs
to be dismissable/focusable independently of the HUD's own always-on presence, and `DrawOrder => 0.2`
(matching `ScribeDialogBase`'s existing band, per `ScribeSettingsDialog`'s own precedent) keeps it
correctly stacked against other Scribe/LibGUI windows.

**The modal is gated and deferred, never dropped, by re-checking the guard each tick while a
`PromptPopup`-styled prompt is pending and not yet shown.** Mirrors the existing tick-driven detection
posture (`ScribeQuestWatcher`'s `OnTick` scans) rather than introducing an event-driven "dialog
closed" hook — simpler, and the guard check itself is cheap (a list scan over currently-open dialogs,
typically small).

**Routing by policy happens at render time, not at queue time.** `PendingQuestPrompts` stays a single
list with no style tag on each `ScribeQuestPrompt` — `HudScribePins`' render path checks
`!prompt.IsCompletion && MySettings.QuestAcceptPolicy == PromptPopup` to decide whether a given queued
prompt renders as the modal or the banner, so a player changing the setting mid-session immediately
changes how any still-pending prompt renders, with no need to re-queue or tag anything at detection
time.

**`Prompt` → `PromptHud` is a rename, not a new value — the underlying byte (2) is unchanged.**
Existing saved settings (`ScribePlayerSettings`, persisted by enum value, not name) continue to
resolve to the same behavior with zero migration needed. `PromptPopup` is a genuinely new value (3).

**Accept/dismiss button label text is decoupled from the button's accent color; Settings' is not.**
`ScribeQuestPromptActions.AccentButton` gains an optional `textColor` parameter (defaulting to
`accent`, so the existing Settings call site is unaffected) so the "Track Quest" and "Not Now"
buttons can render a fixed label color while their background tint/border stay tied to their
(now more heavily tinted) accent color. This became necessary once hands-on tuning (post-ship,
via `tools/quest-prompt-colors/index.html`) raised the per-state fill alpha from 0.16/0.28/0.38 to
0.29/0.48/0.68 — at that higher fill, accent-colored label text lost contrast against its own
button's now much more solid background, where the original low-alpha wash had left it legible.
Settings keeps text == accent (near-white-on-near-white is a no-op there either way, so no visible
change).

**The decoupled text color is the HUD's standard near-white row-text color, not pure white — and
pure white was a real, shipped bug.** First implementation passed `Vector4.One` (pure opaque white)
as `textColor`; in-game it rendered **black**. Root cause (documented in `VSAPI-NOTES.md`'s
"`DefaultTextStyle` + `TextStyle.Merge`" note): LibGUI 3.1.0's `Text` widget resolves its style as
`override.Merge(ancestorDefault)`, and `Merge` treats a field equal to `new TextStyle()`'s own
property-initializer value as "unset" and inherits the ancestor's value instead — and `Color =
Vector4.One` (white) IS that initializer default, so an explicit pure-white override is
indistinguishable from "no color set" and silently falls through to whatever `DefaultTextStyle`
ancestor is in scope around the button (evidently a dark one). The fix — and the better outcome per
user direction — is to use the same near-white constant (`(0.93, 0.93, 0.93, 1)`,
`ScribeRowConstants.HudStandardTextColor`) the HUD's own row text already uses: it both reads as
"standard HUD text" atop the button (matching the rest of the HUD) and, incidentally, is not the
sentinel value, so it overrides correctly. The HUD banner's two buttons also now carry the same
`GlowWidth`/`GlowColor` the rest of the banner's text uses, so the label reads identically to
ordinary HUD text, just sitting on a colored fill; the center modal's buttons use the same near-white
color but no glow (an opaque dialog backdrop has no legibility-over-the-world need for one).
**Alternative considered**: hardcode white directly in the two button builders instead of adding a
parameter. Rejected — `AccentButton` is the single shared helper both the HUD banner and the center
modal call through; a parameter keeps that one chokepoint intact rather than duplicating the
white-text choice at each call site.

**A further follow-up split the HUD banner's title into two lines with two colors — HUD-banner-only,
per direct user design feedback.** The title previously rendered as one gold `Text` built from a
single `{0}`-placeholder lang string ("Add quest to Scribe?: {name}" / "Mark quest done: {name}?").
It now renders as two stacked `Text` widgets inside a tight (`spacing: 0`) `Column`: a gold label
line (the lang string minus its placeholder) and an off-white name line
(`ScribeRowConstants.HudStandardTextColor` — the same "standard HUD text" color already used for
button labels, so the quest's own name reads as data distinct from the prompt's boilerplate
wording). This needed the lang keys split too: `-title`/`-complete-title` dropped their `{0}` and
now hold only the label, with new `-title-suffix`/`-complete-title-suffix` keys holding per-kind
trailing punctuation (empty for accept, `"?"` for completion) appended after the name. Both title
lines keep the existing glow, unchanged.

A same-session follow-up briefly dropped `glowWidth`/`glowColor` from the accept/dismiss button-label
`AccentButton` calls (button text momentarily without glow, title still with it), then reverted that
— per direct user follow-up ("bring back the shadow on the button text") — back to passing
`glowWidth: GlowWidth, glowColor: glow` at both call sites, same as 6.4a originally shipped. Net
effect: button-label glow was never actually removed from the shipped behavior; the color fix from
6.4a (`HudStandardTextColor` + glow) stands as originally implemented. The center modal was never in
scope for the title split (it doesn't build its title from these lang keys — it composites
`WindowFrame`'s own static title plus a separate `shownPrompt.Title` Text) and never had
button-label glow to begin with (opaque backdrop, no in-world legibility need).

**Investigated, then abandoned, replacing the stock `Gui.Widgets.Basic.Button` widget to fix a
user-reported "buttons grow downward on hover" bug — decided to accept the stock behavior instead.**
Decompiling the actually-shipped `gui@3.1.0` `Gui.dll` (not the stale 2.0.0 local clone at
`reference/vslibgui/`, whose `Button.cs` has no hover-scale logic at all) showed the real mechanism
is two effects stacked: `ButtonState.Build` wraps its content in `AnimatedScale(scale, ...,
Alignment.Center, ...)` for the 1.03×/0.96× hover/press grow — and that IS mathematically centered
(`RenderTransform._UpdateEffectiveMatrix` composes translate(-pivot) → scale → translate(+pivot)
using `Alignment.CalculateOffset`, and `Alignment.Center.CalculateOffset(size, Vector2.Zero)` =
`(size/2, size/2)` — a textbook symmetric pivot, confirmed by reading the decompiled IL directly, not
a bug). Separately, `ButtonState.BuildShadows` adds a hard-coded `BoxShadow` **only when hovered and
not pressed**, with `Offset = new Vector2(0f, 3f)` — a fixed 3px-downward shadow with no
corresponding upward counterpart, and no field on `ButtonStyle`/`ButtonVariantStyle` exposes or
overrides it (confirmed: both are closed `readonly struct`s with only
color/border/corner-radius/padding fields — no `BoxShadow`). On a large flat full-width button (the
Read view's "Task Editor" button, `ScribeReadContent.cs`) this shadow exists too but is visually
negligible; on our small colored pill buttons, the asymmetric downward shadow reads as the whole
button growing downward, which is what the user actually observed.

Since the shadow is private, hard-coded logic inside `ButtonState` with no public override, the only
way to remove it without forking LibGUI (against this project's own "no gui fork" precedent, memory:
`forcerebuild-vs-reconciling-libgui`) is to stop using the stock `Button` widget for these buttons. A
first attempt did exactly that: a custom `StatefulWidget`/`State<T>` pair (`ScribeAccentButton`),
composed directly from public LibGUI primitives (`MouseRegion`, `GestureDetector`, `AnimatedScale`,
`AnimatedContainer`, `Padding`) reproducing `ButtonState`'s own hover/press scale values,
background/border color transition, and click-sound-on-press feel, with no box-shadow at all. In
playtest it still read as broken (asymmetric growth persisted). Further investigation (re-checking
`RenderBox.Paint`/`PaintOuterShadow` and `RenderClip.Paint`) also confirmed that even wrapping the
stock `Button` from *outside* with a `Clip` widget sized to its resting layout box wouldn't cleanly
solve this either: `RenderClip` fixes its clip mask in device space before painting descendants, so
the same clip that removes the shadow would also chop off the hover-grow effect's own edges (the
parts of the enlarged button that extend past the resting box) — there's no external-wrapping trick
that removes only the shadow while preserving the desired centered growth.

**Decision (final): revert to the stock `Gui.Widgets.Basic.Button`, shadow and all.** Per direct
user direction: a bespoke reimplementation of library button internals isn't worth maintaining for
one button's visual quirk, especially since the replacement didn't even reliably fix the symptom in
practice. `ScribeAccentButton` was deleted; `AccentButton` builds `Gui.Widgets.Basic.Button` again,
unchanged from before this investigation. The downward-hover-shadow look on these three buttons is
an accepted stock-LibGUI quirk, not a Scribe-side bug.
**Alternatives considered and rejected**: (1) keep the stock `Button` and try to visually cancel the
downward shadow by adding an equal-and-opposite upward shadow via some other wrapping widget —
LibGUI has no public per-widget way to suppress a shadow already painted by a descendant, and
stacking a second shadow on top only compounds the asymmetry risk instead of removing it; (2) wrap
the stock `Button` in an outer `Clip` sized to its resting bounds — removes the shadow but also
visibly truncates the hover-grow effect itself (see above), trading one visual defect for another.

## Risks / Trade-offs

- **[Risk]** A player who selects Popup and then spends a long session with a vanilla dialog
  frequently open (e.g. living in the Handbook) could see accept-modals pile up / feel delayed.
  → **Mitigation**: none needed beyond "never dropped" (already required) — this is an inherent,
  disclosed trade-off of choosing the more intrusive style; the HUD-banner style remains available and
  is the default.
- **[Risk]** The color-coded button convention (green/red/near-white) needs to read correctly
  against Scribe's theme presets (fired/wax/clay backdrops span pale-to-dark, per existing tablet
  theme-contrast notes). → **Mitigation**: use the theme's own semantic color roles where the current
  theming system defines them, falling back to fixed colors only where it doesn't; verify visually
  against at least the default theme in manual playtest (Task 3.x). The post-ship retune (fill-alpha
  + decoupled text color, above) was itself driven by this exact concern surfacing in practice.

## Migration Plan

Additive; the enum rename is source-only (same byte value), so no persisted-setting migration is
needed. Rollback is a plain revert. No player-facing action required — existing Prompt selections
keep working identically as `PromptHud`.

## Open Questions

- Should `ScribeQuestCompletionPolicy` eventually gain the same two-style split? Deferred — not
  requested, and nothing in this change's design blocks adding it later (the same routing pattern
  would apply).
