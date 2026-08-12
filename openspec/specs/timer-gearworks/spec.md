# timer-gearworks Specification

## Purpose
TBD - created by archiving change add-timer-gearworks. Update Purpose after archive.
## Requirements
### Requirement: The Timer tab shows an ambient clockwork gear-train

The Clockmaker's Notebook **Timer tab** SHALL display a train of two or more interlocking toothed gears, positioned **above** the timer form / countdown region, presented as a peek into a mechanism behind the page. The gear-train SHALL animate continuously the whole time the Timer tab is open and SHALL be **decoupled from timer state** — it SHALL be shown and moving whether the timer is Idle (no timer set), Running, or Fired. It SHALL NOT require a timer to have been created.

#### Scenario: Gears are shown with no timer set

- **WHEN** a player opens the Clockmaker's Notebook Timer tab while no timer is set (Idle)
- **THEN** the interlocking gear-train is shown above the set-timer form and is animating

#### Scenario: Gears run regardless of timer state

- **WHEN** the Timer tab is open and the timer transitions between Idle, Running, and Fired
- **THEN** the gear-train continues to animate across those transitions rather than starting or stopping with the timer (except the fired-state reaction defined below)

#### Scenario: Gears are only present on the Timer tab

- **WHEN** a player switches from the Timer tab to any other tab (Read, Editor, Pinned, History)
- **THEN** the gear-train is not shown on those tabs

### Requirement: The gears mesh and advance with a spring-wound per-tooth tick

The gears SHALL advance in **discrete per-tooth steps** (a spring-wound clock feel: a snap followed by a slight settle) rather than a purely linear continuous glide. Adjacent gears SHALL **counter-rotate** and remain visually **meshed**: each gear's per-step angular advance SHALL be inversely proportional to its tooth count (a smaller gear steps through a larger angle), so painted teeth appear to interlock. The reference tooth count SHALL follow the temporal gear's apparent **12 teeth** (a 30°/tooth step) for the primary gear, with other gears sized relative to it.

#### Scenario: Gears step and stay meshed over time

- **WHEN** the Timer tab is left open and observed over several seconds
- **THEN** the gears advance in visible discrete ticks, adjacent gears turn in opposite directions, and their teeth stay visually interlocked (no drift where teeth overlap or gap)

#### Scenario: The step motion snaps and settles

- **WHEN** a gear advances by one tooth
- **THEN** the motion reads as a spring-wound snap-and-settle (an easing curve with a slight overshoot/settle), not a constant-speed slide

### Requirement: One temporal gear carries the teal identity; the rest are steel

Exactly **one** gear — the central temporal gear — SHALL be rendered with the vanilla temporal gear's **teal metallic texture**, so it reads as the *temporal* heart of the mechanism, distinct from the plain steel cogs around it. All other gears (the two flanking small gears and the escape wheel) SHALL be rendered in a plain **steel** palette. No gear SHALL be drawn with an emissive/glow halo (an earlier glow was tried and removed).

#### Scenario: Only the temporal gear renders teal

- **WHEN** the gear-train is shown on the Timer tab
- **THEN** the central temporal gear appears in the teal material while every other gear appears steel, and no gear shows a glow halo

### Requirement: All gears render fully opaque

Every gear SHALL render **fully opaque** — the mechanism behind a gear SHALL NOT show through its solid body. In particular, when the escape wheel slides into place behind the temporal gear, no part of the wheel SHALL be visible through the temporal gear's solid surface.

#### Scenario: No see-through gears during the slide-in

- **WHEN** the escape wheel slides into its live position behind the temporal gear
- **THEN** the temporal gear's solid body fully occludes the wheel behind it (no partial movement is visible through the gear), and every gear reads as a solid opaque object

### Requirement: Gears read as three-dimensional via cast shadows

Each gear SHALL cast a **sharp, silhouette-accurate shadow** beneath itself so the train reads as three-dimensional objects rather than flat decals. The shadow SHALL follow the gear's actual round/toothed outline (not a rectangular box) and SHALL sit close beneath the gear (a small offset, not a soft smear). The temporal gear's shadow MAY be darker than the steel gears' shadows. Every shadow SHALL remain **within the clipped gear region** — no shadow SHALL paint outside the region's bounds.

#### Scenario: Gears cast contained silhouette shadows

- **WHEN** the gear-train is shown
- **THEN** each gear shows a sharp dark offset shadow matching its own gear silhouette, giving a sense of depth, and no shadow spills outside the clipped mechanism region

### Requirement: A faint tick-tock sound plays while the Timer tab is open

While the Timer tab is open, the mechanism SHALL emit a **faint tick-tock sound** — one beat per real second, alternating between two variations so it reads as a tick-tock cadence. The sound SHALL be routed through the base-game **Effect (Sound)** volume channel (so the effect-volume slider controls it), SHALL be **muted** when the player's "Mute Scribe UI sounds" preference is on, and SHALL play **only** while the Timer tab is open (not on other tabs and not when the dialog is closed).

#### Scenario: Tick-tock plays on the Timer tab

- **WHEN** the player is on the Timer tab with "Mute Scribe UI sounds" off and a non-zero effect volume
- **THEN** a faint tick-tock is heard once per second, alternating between two tones

#### Scenario: Tick-tock respects the mute preference

- **WHEN** the player has "Mute Scribe UI sounds" enabled
- **THEN** no tick-tock is heard on the Timer tab

#### Scenario: Tick-tock is scoped to the Timer tab

- **WHEN** the player switches away from the Timer tab or closes the dialog
- **THEN** the tick-tock stops

### Requirement: The gear-train is framed behind semi-visible glass

The gear-train SHALL be presented **behind a semi-transparent glass window** — a framed, slightly translucent overlay that reads as looking *into* the mechanism through glass.

#### Scenario: The mechanism appears behind glass

- **WHEN** the gear-train is shown
- **THEN** it is seen through a translucent framed window (the glass), rather than as bare gears drawn directly on the page background

### Requirement: A fired timer makes the mechanism shudder then lock in place

When the Timer tab's active timer reaches zero and enters the **Fired** state, the gear-train SHALL briefly **shudder** and then **lock** — freezing **at its current angle**, without rewinding or rotating back toward any starting position — pairing with the existing blinking `00:00` countdown. When the fired timer is subsequently **cleared** (dismissed or auto-cleared) or a new timer is set, the gear-train SHALL **resume** its normal ticking. The shudder-and-lock reaction SHALL be the only way timer state affects the gear-train's continuous motion (the escape wheel's engagement/slide, defined below, is the other permitted coupling).

#### Scenario: Gears shudder and lock on fire

- **WHEN** a running timer reaches zero while the Timer tab is open
- **THEN** the gear-train shudders and then stops advancing, freezing at the angle it had reached (it does NOT rotate back toward its starting position), while the blinking `00:00` display is unaffected

#### Scenario: Gears resume after the fired timer is cleared

- **WHEN** the fired timer is cleared (by the player or by auto-disappear) or the player starts a new timer
- **THEN** the gear-train resumes its normal continuous ticking

### Requirement: The escape wheel is always visible and engages on Start

The gear-train SHALL include a large **escape wheel** (regulator) that is **always visible** on the Timer tab regardless of timer state — it SHALL NOT appear from nothing or vanish on any state transition. While the timer is **Idle** the escape wheel SHALL rest in a fixed position and MAY be stationary; when a timer is **Started** (Idle→Running) the escape wheel SHALL **slide into its live position with an animation** (not an instant snap/teleport) and begin turning. When the timer **completes** (Running→Fired) the escape wheel SHALL **slide back out to its resting position with an animation**, reusing the same slide mechanism (this retraction is a position change and is independent of the rotation lock and shudder, which play concurrently). The escape wheel SHALL turn in the **opposite direction** to the temporal gear it meshes with. The escape wheel SHALL be rendered in a **metallic steel** palette (distinct from the teal temporal gear) with per-element colour variation.

#### Scenario: Escape wheel is present when Idle

- **WHEN** the Timer tab is open with no timer set (Idle)
- **THEN** the escape wheel is visible in its resting position (it does not appear only once a timer starts)

#### Scenario: Escape wheel slides in on Start

- **WHEN** the player presses Start Timer (Idle→Running)
- **THEN** the escape wheel animates (slides) into its live engaged position rather than instantly jumping there, and begins turning

#### Scenario: Escape wheel retracts on completion

- **WHEN** a running timer reaches zero (Running→Fired) while the Timer tab is open
- **THEN** the escape wheel animates (slides) back out to its resting position while the temporal gear locks in place and shudders

### Requirement: The gears are arranged as a symmetric train

The visible gear layout SHALL be **symmetric**: the temporal gear SHALL sit horizontally **centered** as the driver, flanked by **two** small steel gears — one on each side — that mesh with it and counter-rotate. The temporal gear SHALL be positioned **lower** than the two flanking gears so that, as it and the escape wheel slide into their live positions, the temporal gear overlaps the escape wheel behind it.

#### Scenario: Symmetric two-flanking-gear layout

- **WHEN** the gear-train is shown on the Timer tab
- **THEN** the temporal gear is centered with one small gear on each side, sitting lower than the two flanking gears, and the flanking gears turn opposite to the temporal gear

### Requirement: The gearworks scales with the Pixel Art Size setting

Every dimension of the gearworks (region, gear sizes, positions, tooth geometry) SHALL scale linearly with the player's **Pixel Art Size** setting, pegged so that `PixelArtSize == 540` is 100% and other values scale by `PixelArtSize / 540`. Changing the setting SHALL proportionally grow or shrink the whole mechanism on the next open.

#### Scenario: Gearworks scales proportionally to Pixel Art Size

- **WHEN** the player sets Pixel Art Size below or above 540 and reopens the Timer tab
- **THEN** the gearworks is uniformly smaller or larger in proportion to `PixelArtSize / 540`, with 540 rendering at the reference 100% size

### Requirement: The gearworks is presentation-only and does not obstruct timer controls

The gear-train SHALL be purely visual: it SHALL NOT change timer behavior, SHALL NOT require or consume any Core, codec, or network state, and SHALL NOT intercept pointer or keyboard input intended for the timer form, countdown, or Stop/Start controls.

#### Scenario: Timer controls remain fully usable

- **WHEN** the gear-train is shown and the player interacts with the label field, H/M/S steppers, mode radios, or Start/Stop button
- **THEN** those controls behave exactly as they do without the gear-train (the gears never swallow a click or keystroke meant for a control)

