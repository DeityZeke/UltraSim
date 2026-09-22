# Simulation Time, Resolution, Causal Aggregation, and Control Authority

**Status:** CANONICAL conceptual design  
**Implementation status:** cross-system design target; existing Runtime already provides some clock/cadence primitives, but high-speed semantic aggregation and the complete control-authority model are not implied to be implemented.

Primary references:
- [Runtime](../../Docs/RUNTIME.md)
- [Sim Core Reasoning](../../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md)
- [Deterministic Fate / Timelines / Realities](../topics/deterministic-fate-timelines-realities.md)
- [Project Shape / Player Experience](../topics/project-shape-and-player-experience.md)

## 1. Purpose

UltraSim must support ordinary human-speed play and very large time acceleration without turning them into different simulations.

The core rule is:

> **Playback speed changes how quickly simulation time is consumed. Resolution changes how efficiently that time is evaluated. Neither is allowed to casually change authoritative causality.**

A second rule governs player/NPC behavior:

> **Simulation resolution and decision authority are independent axes.**

An actor can be evaluated at fine or coarse resolution regardless of whether its next Intent is chosen by a human, an autonomous Sim Core, a command queue, or a higher-level organizational policy.

## 2. Core invariants

### 2.1 ECS cadence is not world time

ECS/runtime cadence answers:

> When does code get an execution opportunity?

Simulation/world time answers:

> How much time has passed in the world?

They must not be treated as the same concept.

A server may continue using an ordinary bounded execution cadence while the world is viewed at:

~~~text
0.5x
1x
2x
5x
10x
100x
Other_x
~~~

The supported set, maximum rate, pause semantics, and exact numeric representation remain implementation policy.

High playback rates must not require proportionally increasing the number of full-detail ECS ticks.

### 2.2 Gameplay semantics use simulation time

Game systems should normally depend on elapsed simulation time, exact simulation deadlines, or semantic events—not wall-clock time and not the player's playback multiplier.

Examples:

~~~text
crop growth
hunger
healing
sleep
crafting
travel
aging
weather evolution
production
NPC schedules
resource recovery
~~~

A four-simulated-second action remains a four-simulated-second action. At 10x playback, an uninterrupted four-second action is observed over roughly 0.4 real seconds.

Systems should not generally branch on the playback multiplier; they should consume the simulation interval they are given.

### 2.3 Resolution invariance

Given:

~~~text
same authoritative starting state
+ same definitions/rules
+ same ordered external inputs/interventions
+ same deterministic Fate
=
same meaningful authoritative outcome
~~~

changing playback speed or safe simulation resolution must not arbitrarily change the result.

This does **not** require every unobserved intermediate animation, ECS publication, or transient micro-state to be materialized at coarse resolution.

It does require the authoritative consequences that matter to later causality to agree.

## 3. Time domains

UltraSim should conceptually distinguish at least:

### Host / real time

Monotonic machine/process time used for concerns such as:

- profiling;
- transport/network timeouts;
- UI responsiveness;
- scheduling the host itself.

Host time is not authoritative world chronology.

### Execution cadence / SimulationTick

The authoritative ordering identity for simulation steps and execution opportunities.

Current Runtime owns this mechanism.

### Simulation / World Time

The durable authoritative time inside the world.

This is what gameplay calendars, schedules, durations, history timestamps, aging, celestial state, and environmental systems ultimately consume.

### Playback / simulation speed

How quickly the host attempts to consume future simulation time from the human observer's point of view.

High playback speed may cause the scheduler to use larger safe semantic intervals and coarser resolution rather than executing every fine-grained state transition as a separately published ECS step.

## 4. Calendar, astronomy, and environment

There should be one authoritative world-time basis. Calendar and astronomy are deterministic interpretations of it rather than independent counters that can drift.

Conceptually:

~~~text
World Time
    ├── Calendar
    │     year
    │     month
    │     day
    │     time of day
    │
    ├── Astronomy
    │     sun state
    │     sunrise / sunset
    │     moon phase
    │     waxing / waning / full / new
    │
    └── Environment inputs
          season/climate
          regional modifiers
          weather
          local conditions
          world/magical events
~~~

Systems should be able to query relevant derived state without reimplementing calendar/astronomy math themselves.

Exact calendar length, lunar period, seasons, temperature formulas, and API names are content/engineering details not frozen by this document.

### Environmental derivation

Temperature or similar conditions may depend on:

~~~text
regional climate
+ season/month
+ time of day
+ current weather
+ elevation/terrain
+ local environment
+ world/magical events
~~~

The exact formula need not be purely additive.

Weather and temperature are not fields that belong directly inside the clock. They are world/environment state that can use time as an input.

## 5. Meaningful boundaries, not per-tick reconsideration

A persistent Intent should usually continue until something meaningful changes.

Example farmer:

~~~text
08:00 WorkField

Possible next causal boundaries:
    task completes
    hunger becomes decision-relevant
    tool breaks
    weather changes materially
    threat appears
    another actor interacts
    schedule changes
~~~

The actor does not need to ask "should I keep farming?" every ECS tick.

At high playback speed, the scheduler should attempt to advance directly toward the next meaningful boundary.

## 6. Three broad kinds of interval processing

These are semantic categories, not frozen API names.

### Analytically compressible

Processes that can safely consume an elapsed interval:

~~~text
crop growth
simple recovery
aging
sleep
ordinary production
uncontested work progress
~~~

### Boundary-sensitive

Processes that can consume intervals but must stop when an authoritative threshold is crossed:

~~~text
death threshold
item breakage
shop opening/closing
crop maturity
contract expiry
starvation threshold
sunrise
scheduled shift change
~~~

If an interval crosses a boundary, resolve to the boundary first.

### Interaction-sensitive

Processes where another actor/environmental event can change the outcome:

~~~text
combat
chases
crowds
contested resource use
complex social interactions
~~~

These may still be accelerated or collapsed when causally sealed, but they must expose/materialize state before an outside cause can intervene.

## 7. Next causal boundary

The central aggregation question is:

> **How far can this process advance before something outside it could matter?**

Potential boundaries include:

- another actor can perceive/intervene;
- another actor reaches the interaction;
- weather/environment changes materially;
- a scheduled event occurs;
- a participant crosses a decision-relevant threshold;
- an action completes;
- someone dies/flees;
- an interaction ends.

Example:

~~~text
Farmer vs Wolf begins 09:37:00
Guard could hear/see encounter at 09:37:12

Resolve Farmer vs Wolf only through 09:37:12
    ↓
materialize authoritative state at that moment
    ↓
Guard perception/reasoning may now participate
~~~

If no external cause can intersect before the encounter ends, the whole encounter may be resolved as one deterministic transition.

## 8. Deterministic causal aggregation

Because Fate and actor decision semantics are deterministic, an unobserved causally sealed interaction does not always need to publish every sub-reaction individually.

Fine trace:

~~~text
wolf attacks
farmer notices
farmer chooses response
farmer draws axe
wolf lunges
farmer dodges
farmer attacks
...
wolf dies
~~~

Equivalent collapsed result may be:

~~~text
Encounter:
    farmer defeated wolf
    duration = 43 simulated seconds
    farmer health delta
    farmer stamina delta
    injury changes
    axe durability delta
    skill-practice changes
    wolf corpse created
    local environment changes
    history-worthy occurrences
    final Intent/state changes
~~~

The optimization is **not** "store only the winner."

It is:

> **Replace an internally deterministic causal trace with an equivalent authoritative state transition when nothing can observe or alter the omitted interior.**

### Resolution forms

Conceptually the engine may support forms such as:

- **Expanded** — actions/reactions are individually materialized;
- **Headless accelerated** — the same detailed resolver runs internally without ordinary publication/presentation overhead;
- **Collapsed/Aggregated** — an equivalent larger deterministic transition is applied.

These names are descriptive, not frozen public API names.

### Memoization

Exact transition caching/memoization may eventually be useful because deterministic inputs imply deterministic outputs.

It is **not** required by this design and should not be assumed to produce frequent exact cache hits. The primary optimization is safe temporal/causal aggregation.

## 9. Immediate / Near / Far simulation resolution

Simulation resolution can mirror UltraSim's existing spatial/AOI/presentation thinking without becoming identical to it.

Conceptually:

~~~text
Immediate
    fine-grained causal resolution
    detailed perception/reactions/actions

Near
    larger deterministic steps
    same authoritative semantics

Far
    aggregate toward meaningful events/outcomes
~~~

This is similar in shape to Immediate/Near/Far render/AOI tiers, but the "size" being varied is **causal/temporal resolution**.

A process can be forced to higher detail when:

- a player can observe/intervene;
- causality becomes contested;
- precision is required for the result.

High playback speed can bias more of the world toward coarse resolution, but it must not override those causal requirements.

## 10. Decision authority is a separate axis

"Player" is not a different species of simulated actor.

An actor remains the same actor. What changes is who may choose its next Intent.

### Direct control

The human chooses intentions/orders.

The simulation may execute and aggregate already-selected work, but it must not invent a new player intention at an agency boundary.

At 100x:

~~~text
No Intent / no queue
    actor remains idle

Mine this vein
    aggregate mining until completion/interruption/decision boundary
~~~

### Autonomous + intervention

The Sim Core normally chooses the actor's intentions.

The player may intervene at any time and override current direction.

At 100x, the character continues to live autonomously: eat, work, socialize, sleep, react, and pursue goals according to the ordinary actor simulation.

This is the intended Sims-like play style.

### Command / queue control

The human supplies orders, goals, or queued work.

The actor autonomously resolves allowed tactical/subgoal details necessary to carry them out.

This supports individual orders and strategy-like/group play without making every actor a puppet requiring micro-actions.

### Governance / policy control

The human influences higher-level structures such as organizations, settlements, priorities, labor allocation, laws, or policies while individual actors remain simulated agents.

This is the SimCity/Age-of-Empires-ish control layer rather than a separate simulation.

## 11. Controller/provider model

A useful conceptual precedence is:

~~~text
Temporary direct override
    ↓
Explicit player order / queue
    ↓
Assigned command / job / goal
    ↓
Autonomous Sim Core
    ↓
Idle fallback
~~~

The actor's active control policy determines which providers may choose new Intents.

Orders should create the same semantic Intents that autonomous reasoning could create where applicable.

Example:

~~~text
Autonomous actor:
    decides MineDeposit

Player:
    orders MineDeposit
~~~

Execution semantics are shared. The **source/authority** of the Intent differs.

Potential Intent-source metadata may distinguish:

~~~text
Self
PlayerDirect
PlayerOrder
Employer
MilitaryCommand
Law
Possession
ScriptedEvent
~~~

Exact encoding is not frozen here.

## 12. Possession / takeover

Possession should not transform an NPC into a different player-specific entity type.

Conceptually:

~~~text
Harold
    Control = Autonomous
    Intent = WalkToBakery

possess Harold
    ↓
Control = chosen human/direct policy

release Harold
    ↓
Control = Autonomous
    ↓
Sim Core re-evaluates whether prior Intent is still relevant
~~~

This same controller-binding model supports ordinary character control, temporary intervention, Sims-style release, and future possession mechanics.

## 13. Agency boundaries and interruption

For directly controlled actors:

> **The simulation may cross execution boundaries, but not player agency boundaries unless control has been delegated.**

A long action may aggregate until:

- it completes;
- a threat becomes relevant;
- an important interaction occurs;
- the player's chosen order becomes invalid;
- a new choice is genuinely required.

At that point the game may pause, slow, notify, or otherwise return control according to future interrupt policy.

Exact interrupt categories and UX are not frozen here.

## 14. Presentation at accelerated time

Presentation should visibly express time acceleration rather than pretending the world is still moving at 1x.

Accepted direction:

- locomotion/work animations speed up;
- environmental/celestial motion speeds up;
- music/audio may reflect slower/faster time;
- animation playback has a sensible maximum rate;
- beyond that rate, presentation may skip loops, sample, or interpolate rather than making characters visibly spasm through hundreds of animation cycles.

Conceptually:

~~~text
display animation rate = capped function of simulation speed
extra simulated progress = interpolation / sampling / loop skipping
~~~

Presentation is allowed to omit visual cycles. It is not allowed to change authoritative simulation outcomes.

## 15. Relation to deterministic Fate

The Fate model provides the basis for repeatable coarse resolution.

A semantic random outcome must belong to the occurrence itself, not to "how many ECS ticks happened to execute."

Therefore:

- fine and coarse resolution consume the same semantic Fate;
- batching must not shift unrelated random streams;
- save/load must not reroll already-addressed occurrences;
- deterministic tie-breaking must remain stable;
- rule/version changes that alter coarse/fine equivalence require explicit versioning/migration policy.

See [Deterministic Fate / Timelines / Realities](../topics/deterministic-fate-timelines-realities.md).

## 16. Relation to Sim Core LOD

Sim Core's Active / Nearby / Background / Dormant tiers remain valid, but their deeper meaning is:

> **LOD changes the amount of causal detail materialized, not the underlying truth rules.**

An actor at Background or Dormant resolution is not supposed to use a fake alternate universe of rules merely because nobody is watching.

If detail could materially alter the result, the process must either:

- execute the detailed resolver headlessly; or
- materialize/escalate before the causal boundary.

## 17. Current implementation boundary

Current Runtime already provides important primitives:

- authoritative SimulationTick;
- fixed-point durable WorldTime;
- DeltaSeconds;
- simulation-frequency/cadence policy;
- PlaybackRate;
- frequency-independent restoration semantics.

Those are executable/current contracts documented in [Runtime](../../Docs/RUNTIME.md).

This document adds the accepted **game/simulation design target** for:

- high playback rates without proportionally more full-detail ECS work;
- semantic boundary scheduling;
- causal aggregation;
- resolution invariance;
- player/NPC control-authority modes;
- presentation behavior at accelerated time.

It does not claim those higher-level mechanisms are already implemented.

## 18. Open engineering details

The following remain implementation/design details rather than frozen requirements:

- exact representation of arbitrary playback multipliers;
- exact supported multiplier list and maximum;
- pause/0x semantics;
- exact scheduler data structures;
- exact interval-resolver APIs;
- exact calendar/month/year lengths;
- exact lunar period;
- exact weather/temperature equations;
- exact high-speed interrupt UX;
- exact memoization/cache strategy;
- exact spatial thresholds for Immediate/Near/Far resolution;
- exact persistence location for player control-mode preferences.

## 19. Core summary

~~~text
World Time
    ↓
semantic scheduler
    ↓
next meaningful / causal boundary
    ↓
choose safe resolution
    ↓
deterministic authoritative transition
    ↓
publish/materialize only as much detail as required
    ↓
presentation samples the result at the requested playback speed
~~~

And independently:

~~~text
Actor
    +
Decision Authority
    Direct | Autonomous+Intervention | Command/Queue | Governance
    +
Simulation Resolution
    Immediate | Near | Far/Aggregated
~~~

The two axes must remain separable.
