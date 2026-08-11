## ADDED Requirements

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

### Requirement: The gears carry the temporal gear's material identity

The gears SHALL be rendered with the vanilla temporal gear's **teal metallic texture** and an **emissive, self-illuminated glow**, so they read as *temporal* gears rather than plain cogs. The glow SHALL be a render effect applied in the GUI layer (it does not depend on world lighting).

#### Scenario: Gears render teal and glowing

- **WHEN** the gear-train is shown on the Timer tab
- **THEN** the gears appear in the temporal gear's teal material and are visibly self-illuminated (glowing) rather than flat and unlit

### Requirement: The gear-train is framed behind semi-visible glass

The gear-train SHALL be presented **behind a semi-transparent glass window** — a framed, slightly translucent overlay that reads as looking *into* the mechanism through glass.

#### Scenario: The mechanism appears behind glass

- **WHEN** the gear-train is shown
- **THEN** it is seen through a translucent framed window (the glass), rather than as bare gears drawn directly on the page background

### Requirement: A fired timer makes the mechanism shudder then lock

When the Timer tab's active timer reaches zero and enters the **Fired** state, the gear-train SHALL briefly **shudder** and then **lock** (stop advancing), pairing with the existing blinking `00:00` countdown. When the fired timer is subsequently **cleared** (dismissed or auto-cleared) or a new timer is set, the gear-train SHALL **resume** its normal ticking. The shudder-and-lock reaction SHALL be the only way timer state affects the gear-train's motion.

#### Scenario: Gears shudder and lock on fire

- **WHEN** a running timer reaches zero while the Timer tab is open
- **THEN** the gear-train shudders and then stops advancing, while the blinking `00:00` display is unaffected

#### Scenario: Gears resume after the fired timer is cleared

- **WHEN** the fired timer is cleared (by the player or by auto-disappear) or the player starts a new timer
- **THEN** the gear-train resumes its normal continuous ticking

### Requirement: The gearworks is presentation-only and does not obstruct timer controls

The gear-train SHALL be purely visual: it SHALL NOT change timer behavior, SHALL NOT require or consume any Core, codec, or network state, and SHALL NOT intercept pointer or keyboard input intended for the timer form, countdown, or Stop/Start controls.

#### Scenario: Timer controls remain fully usable

- **WHEN** the gear-train is shown and the player interacts with the label field, H/M/S steppers, mode radios, or Start/Stop button
- **THEN** those controls behave exactly as they do without the gear-train (the gears never swallow a click or keystroke meant for a control)
