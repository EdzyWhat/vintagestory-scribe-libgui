## Context

A Link row's checkbox and icon are built separately, at two different call sites, on each of four
completion-checkbox render surfaces (`ScribeReadContent.cs`, `ScribeEditorContent.cs`,
`ScribePinnedContent.cs`, `HudScribePins.cs`): a checkbox via
`ScribeRowControlNudge.BuildTaskCheckbox` in a reserved leading slot, and an icon via
`ScribeLinkIcon.Build` further right, inline with the item name. Today `ScribeLinkIcon.IsBookGlyph`
treats a guide-page Link and a quest Link identically (both itemless, both render the
`scribebook` glyph); nothing distinguishes a quest Link's row from a plain Link's.

`ScribeAssignmentStageRow.cs` also builds a checkbox and an icon in the same shapes, but its
checkbox is NOT a completion toggle — it's a row-**selection** checkbox (`Selected`/
`OnToggleSelected`) for the assignment-creation picker. That checkbox is functionally required
(it's how a row gets included in the assignment) and is not the "does nothing" problem this change
addresses, so it is explicitly OUT of scope for checkbox removal — see D1a. The icon/color changes
still apply there, for visual consistency with the other four surfaces.

The mod already has an established pattern for a per-surface link-color override:
`ScribeRowStyle.LinkColor` (nullable), baked explicitly at theme-construction time only where the
ambient `colors.Primary` would be illegible as link text (`ScribeTheme.ForTabletLink` for the
tablet, `ScribeTheme.ChalkboardLinkText` for the chalkboard); every other surface leaves it null
and falls back to `colors.Primary` at render time. See proposal.md for why this change is wanted.

## Goals / Non-Goals

**Goals:**
- One shared code path decides "is this row's leading slot a checkbox or the quest-marker icon,"
  used identically by the four surfaces with a real completion checkbox, so the branch can't drift
  or get missed on one.
- A new `QuestLinkColor` override seam that mirrors `LinkColor`'s existing structure exactly, so
  the two stay easy to reason about side by side.
- Author a reasonable default quest-color value per surface/theme, explicitly flagged for
  in-game playtest tuning — matching this codebase's established practice for this kind of
  perceptual color decision.
- Zero behavior change to completion semantics, task counts, or TSV export.

**Non-Goals:**
- Not analytically nailing exact final RGB values for every theme's quest color — that's a
  playtest-driven tuning pass (tasks.md), same as every other per-theme color in `ScribeTheme.cs`.
- Not touching `ScribeLinkTarget`, `ScribeBlock.IsCompletable`, or any other Core-layer quest-link
  semantics — this is presentation-only.
- Not redesigning plain (non-quest) Link row rendering — it is unchanged.

## Decisions

### D1: One shared helper decides the row's leading-slot control
Add `ScribeRowControlNudge.BuildLeadingControl(context, style, linkTarget, done, onChanged)` in
`ScribeRowWidgets.cs`, returning either:
- the quest-marker icon (a `ScribeVsIconGlyph("scribequest", ...)`, sized to match the checkbox's
  footprint, colored via the new `QuestLinkColor` seam — see D3) when
  `ScribeLinkTarget.IsQuest(linkTarget)` is true, or
- the existing `BuildTaskCheckbox(...)` result otherwise (unchanged behavior).

Each of the four completion-checkbox render call sites (`ScribeReadContent.cs`,
`ScribeEditorContent.cs`, `ScribePinnedContent.cs`, `HudScribePins.cs`) replaces its direct
`BuildTaskCheckbox(...)` call, inside its existing `Completable`-gated `Padding` wrapper, with this
helper — the wrapper/padding/top-nudge around it is untouched per surface (surfaces already differ
slightly there, e.g. item-row vs. non-item-row top nudges), only the *inner* control changes
source.

**Alternative considered**: branch on `IsQuest` inline at each of the four call sites. Rejected —
duplicates the same conditional four times with no shared point of truth, which this codebase's
own docs elsewhere call out as a trap ("one implementation ... none of them fork this widget").

### D1a: `ScribeAssignmentStageRow.cs` keeps its checkbox
That row's checkbox is a **selection** control (`Selected`/`OnToggleSelected`), not a completion
toggle — it's how a row gets included in the assignment being created. It does not have the "does
nothing for a quest" problem this change addresses, and removing it would regress the ability to
select a Quest Link row for assignment. `BuildLeadingControl` (D1) is therefore NOT used here; this
file only picks up the icon (D2) and color (D3) changes, for visual consistency with the other four
surfaces.

### D2: The inline icon is skipped (not swapped) for a quest row
At each of the five call sites that currently unconditionally compute and add a `ScribeLinkIcon`
inline near the item name, guard that single `Add`/render step with `!ScribeLinkTarget.IsQuest(...)`
so a quest row simply omits it — the leading-slot icon from D1 is the row's only icon. This is a
one-line guard at each site rather than a shared helper, because the surrounding code at each site
already differs materially (e.g. `ScribeReadContent.BuildItemContent`'s Tracker/Craft counter
branch, `HudScribePins`' band sizing) — there is no single shared "add the inline icon" call to
hang a helper on the way there is for the checkbox.

`ScribeLinkIcon`/`IsBookGlyph` itself does NOT need to learn about quest vs. guide-page: since a
quest row never reaches the inline-icon call anymore, `ScribeLinkIcon.Build` keeps treating
guide-page and quest identically for the cases where it's still invoked (a guide-page Link's own
inline rendering, unchanged).

**Exception — the Assignment-stage row**: per D1a, that row's leading slot is not available for
the quest icon (it holds the selection checkbox instead). So on that one surface the quest icon is
SWAPPED into the inline position rather than skipped — replacing the generic `scribebook` glyph
`ScribeLinkIcon.Build` would otherwise render — so a Quest Link still shows its distinct icon
somewhere on that row.

### D3: `QuestLinkColor` mirrors `LinkColor`'s structure exactly
Add `ScribeRowStyle.QuestLinkColor` (nullable `Vector4?`), alongside the existing `LinkColor`.
Resolve it at each render call site the same way `LinkColor` is resolved today
(`Vector4 linkColor = style.LinkColor ?? colors.Primary;` becomes a parallel
`Vector4 questLinkColor = style.QuestLinkColor ?? ScribeTheme.QuestLinkAccent;`), and bake an
explicit override wherever a surface already bakes `LinkColor` (tablet's `DecorateRowStyle` via a
new `ScribeTheme.ForTabletQuestLink(material, state)`; chalkboard's `DecorateRowStyle` via a new
`ScribeTheme.ChalkboardQuestLinkText`) — anywhere `LinkColor` is left null (Lectern/Notebook,
Pinned, HUD, Assignment-stage on the plain Light theme) `QuestLinkColor` is also left null and
falls through to the new `ScribeTheme.QuestLinkAccent` default.

**Alternative considered**: repurpose `ColorScheme.Secondary` — rejected per prior discussion with
the user (recolors the pinned-row wash and every Secondary-variant button on every theme, unrelated
to Quest Links). **Alternative considered**: infer a "this theme is already blue" fallback via a
runtime heuristic on `colors.Primary`'s channel ratios — rejected as inconsistent with this
codebase's established idiom of explicit, named, per-material color authoring (see
`ClayColorsFor`'s explicit string switch) rather than inferring theme identity from color math.

### D4: Per-surface default quest-color values (playtest-tunable placeholders)
New constants in `ScribeTheme.cs`, following the exact naming/placement pattern `ForTabletLink`/
`ChalkboardLinkText` already use:

| Surface | Constant | Placeholder value | Rationale |
|---|---|---|---|
| Parchment/Light (default) | `QuestLinkAccent` | `rgb(66,107,183)` | User-specified steel-blue; the parchment palette has zero blue anywhere today, so this reads as a clean, deliberate accent. |
| Chalkboard | `ChalkboardQuestLinkText` | a **lightened** variant of `QuestLinkAccent` | Mirrors why `ChalkboardLinkText` lightens the dark green `ChalkAccent`: a mid-value color doesn't read as small text on dark slate. |
| Fire/Red/Wax clay | `ForTabletQuestLink` (shared branch) | a **darkened/more-saturated** variant of `QuestLinkAccent` | Mirrors why `TabletReadability.LinkInk` darkens `Primary` for these light-mid clay grounds. |
| Blue clay | `ForTabletQuestLink` (`clay-blue` branch) | a distinct **warm amber/gold**, NOT a blue | The blue clay's own `Primary` (`rgb(66,107,133)`) is already essentially this hue — a quest-blue there would blend into its normal link color instead of standing apart. |

Every value above is a starting default, not a final answer — tasks.md includes an explicit
in-game glance-check per surface (the same "Finalized in-game" practice `ScribeTheme.cs` already
follows for its other per-material colors) to confirm legibility and adjust if any reads poorly.

### D5: Icon registration
Register the already-drawn `quest.svg` in `ScribeModSystem.Assets.cs` as `"scribequest"`, next to
the existing `RegisterSvgIcon` calls (e.g. alongside `"scribebook"`), following the exact same
call shape as every other icon there.

## Risks / Trade-offs

- **[Risk]** Doubling the number of per-surface link-adjacent color functions in `ScribeTheme.cs`
  (one set for `LinkColor`, a parallel set for `QuestLinkColor`) could drift out of sync over time.
  → **Mitigation**: name and place each `QuestLinkColor` function directly next to its `LinkColor`
  counterpart (e.g. `ForTabletQuestLink` beside `ForTabletLink`) so the pairing stays obvious.
- **[Risk]** Skipping the inline icon for a quest row removes the height basis
  (`ScribeLinkIcon.VisualSize`) that item-name vertical centering currently derives from on some
  surfaces. → **Mitigation**: re-derive a plain-text-line-height basis for a quest row's name at
  each affected call site during implementation; verify no vertical misalignment appears on any
  surface before considering a file's task done.
- **[Risk]** The placeholder chalkboard/clay quest-color values in D4 are authored by inspection,
  not validated in-game. → **Mitigation**: explicit playtest task per surface in tasks.md.
- **[Risk]** Five separate render files increase the chance one surface is missed or handled
  inconsistently. → **Mitigation**: D1's shared helper keeps the checkbox/icon swap single-sourced;
  tasks.md still lists a separate task per file for the D2/D3 guards so none is silently skipped.
