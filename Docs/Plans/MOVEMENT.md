# Movement / Camera / Control Direction

Status: active design direction for T0.10 and later client work.

Target repository path: `Docs/Plans/MOVEMENT.md`.

This document defines the intended boundary between camera, player control, and
authoritative locomotion. It is deliberately not a universal controller
framework. UltraSim will build and polish one primary control profile, while a
small number of deliberately incomplete reference profiles prove that the
underlying contracts are not accidentally tied to one camera or input style.

The primary influences are UO/Kenshi/Sims-style simulation control, with
Project Zomboid / Path of Exile-style independent look direction and
Valheim-style third-person direct control used as architectural counterexamples.

## Decision

UltraSim separates three concerns:

```text
camera
    how the player views/follows the world

control grammar
    how raw engine input becomes semantic player intent

authoritative locomotion
    how an actor actually moves through world truth over authoritative time
```

These concerns may cooperate, but none owns the others.

Camera mode is client policy. It is not authoritative Game state, world identity,
persistence identity, Spatial state, or Network state.

Control profiles are concrete client compositions. They may translate mouse,
keyboard, controller, camera basis, and pointer hits into neutral intent.

Game authority consumes world-space intent. It does not consume Godot events,
key names, screen pixels, camera-relative "forward", or a profile name such as
"third person".

UltraSim's primary shipped/polished profile is an orbital simulation/command
profile with hybrid mouse locomotion. Minimal third-person-direct and
independent-move/look profiles remain executable architecture proofs.

## Non-goals

Do not build:

- a universal `CameraMode` or `ControllerType` enum shared by Game/Network;
- a monolithic base controller with profile-specific branches;
- a generic mod/controller framework before concrete consumers earn one;
- a complete third-person camera with clipping, collision, shoulders, lock-on,
  aim assist, or combat;
- a complete Zomboid/Path-of-Exile combat controller;
- a production navigation/pathfinding framework solely to prove destination
  intent;
- client-selected physical speed in meters/second;
- movement defined as "N millimeters per input command".

Reference profiles exist to prove seams, not to become equally supported games.

## High-level flow

```text
                           CLIENT

raw engine input
    |
    v
engine adapter
    physical bindings
    device reads
    cursor / focus
    camera node realization
    screen -> world query
    |
    v
Client Core control profile
    gesture interpretation
    camera-relative -> world-space conversion
    input sequencing / control-session fencing
    |
    +---------------------+----------------------+
    |                     |                      |
    v                     v                      v
LocomotionIntent      LookIntent          interaction/selection
    |                     |                      |
    +---------------------+----------------------+
                          |
                          v

                    GAME COMMAND SEAM
                          |
                          v

                         GAME

                    command admission
                          |
             +------------+------------+
             |                         |
             v                         v
       direct intent              destination goal
             |                         |
             +------------+------------+
                          v
              authoritative locomotion
                 speed/rules x Delta
                          |
             terrain / collision / nav
                          |
                          v
                     WorldPosition
                          |
                 Spatial/publication
                          |
                          v

                    CLIENT PROJECTION
                          |
                  presentation window
                          |
                     engine renderer
```

## Neutral locomotion semantics

The architectural vocabulary should distinguish current direct control from a
latched destination.

The exact C# representation is earned by implementation, but the semantics are:

```text
LocomotionIntent

None
    no current locomotion request

Direct
    world-space X/Z direction
    analog magnitude
    semantic gait
    complete superseding current state

Destination
    canonical world-space destination
    semantic gait
    remains active until reached, cancelled, replaced, or impossible
```

`Direct` and `Destination` are not separate locomotion engines. They converge
below the intent boundary:

```text
Direct --------------------------+
                                 |
                                 v
                          desired motion
                                 |
Destination -> route/steering ---+
                                 |
                                 v
                     authoritative locomotion
                                 |
                      game speed x Delta
                                 |
                  terrain/collision/navigation
                                 |
                            WorldPosition
```

An AI system may eventually feed the same lower steering/locomotion layer without
pretending to be a keyboard or mouse.

## Time and cadence

Movement is per-time behavior.

The frozen rule remains:

> Tick is identity. Delta is time. Frequency determines Delta.

A client command must not mean "move 100 mm". A direct input establishes desired
motion; authority integrates it using the authoritative elapsed simulation time.

Equivalent sustained input over equivalent authoritative elapsed time must
produce equivalent locomotion across supported fixed cadences, subject only to
explicit deterministic integer-integration tolerance.

T0.10 must prove at minimum that one second of sustained direct movement at
multiple fixed cadences does not change the character's semantic movement speed.

Input sampling frequency and network delivery frequency must not define movement
speed.

## Gait and magnitude

Clients nominate intent, not authoritative physical speed.

A direct locomotion intent may contain:

```text
world-space direction
analog magnitude 0..1
semantic gait
```

The first likely gait vocabulary is conceptually:

```text
Sneak
Walk
Run
Sprint
```

The exact enum and which values T0.10 implements are implementation details until
earned. It is valid for the first real client to implement only Walk/Run if that
is all the playable vertical needs.

Game determines actual movement capability from authoritative state:

```text
requested gait
    + character state
    + encumbrance
    + injury/fatigue
    + terrain/effects
    + future movement rules
    =
authoritative speed
```

The client must not command an arbitrary authoritative meters-per-second value.

## Camera-relative controls end at Client Core

WASD/stick input may be camera-relative for the player, but Game receives a
world-space direction.

Conceptually:

```text
raw MoveX / MoveZ
    +
camera horizontal forward/right basis
    =
world-space Direct direction
```

The neutral camera/control boundary therefore needs enough engine-neutral camera
orientation to obtain a horizontal basis. It does not need Godot's `Camera3D`,
Unity camera objects, native matrices, or renderer resource identity.

A small client camera frame may eventually expose values such as:

```text
world target/focus
horizontal forward
horizontal right
yaw
pitch
distance
```

Only extract the fields actually required by the first profiles.

## Look/facing is independent from locomotion

Do not define movement direction as mandatory facing direction.

A future actor may:

```text
move southwest
face northeast
aim northeast
```

The independent-move/look reference profile exists specifically to prove that
the architecture permits this.

Conceptually:

```text
LocomotionIntent
    travel semantics

LookIntent
    facing/look direction

future AimIntent
    targeting/aim semantics if a real game feature earns it
```

T0.10 need not create a general aim framework. It only needs enough independent
look/facing state to prove the distinction.

## Hybrid mouse locomotion

UltraSim's primary mouse grammar combines manual steering and autonomous
destination movement.

The intended default semantics are:

```text
LMB quick single click on terrain/object
    selection / inspection / contextual client action
    does not inherently mean locomotion

LMB hold on terrain
    enter Direct mouse steering
    cursor position continuously determines world-space direction/magnitude

LMB release after steering
    stop Direct steering
    do NOT implicitly create a destination

LMB double click on valid terrain
    request Destination movement

RMB while steering
    cancel steering immediately
    do NOT create a destination

Escape / control-context interruption
    cancel steering/current client control as appropriate
```

A single click is deliberately left available for selection, inspection, radial
actions, or another simulation-oriented interaction grammar.

The exact mouse buttons remain profile/engine configuration. The architecture
freezes the semantic distinction, not one physical binding.

## Mouse gesture state

Raw pointer clicks are not sent directly to Game. The client interprets them as
gestures.

Conceptually:

```text
Idle
 |
 | pointer down
 v
Tap/Hold Candidate
 | \
 |  \ hold/movement threshold
 |   -> Steering
 |          |
 |          +-- release ------> Stop
 |          +-- cancel -------> Stop/Cancel
 |
 +-- quick release -----------> Single-click candidate
            |
            +-- second click within profile window -> Destination gesture
            |
            +-- otherwise -------------------------> Single-click action
```

The precise hold threshold, movement slop, and double-click timing are client UX
policy and must not leak into Game/Network contracts.

Steering should not be delayed by the full double-click window. The real client
may use a small hold/motion threshold, native double-click information, or another
profile-specific technique that keeps direct steering responsive.

## Point-and-hold steering

Mouse steering is another producer of `Direct` locomotion.

The client derives world direction from the pointer relative to the controlled
actor / world hit:

```text
controlled actor
      |
      |---------------> cursor/world point

angle
    -> world-space direction

distance / configured steering envelope
    -> analog magnitude, if the profile chooses analog mouse steering
```

Distance-to-cursor may influence analog magnitude in the primary profile, but
gait remains semantic policy. Do not make screen distance an authoritative speed
contract.

## Destination movement

Destination movement means:

> Move this actor toward this canonical world destination until completed,
> cancelled, replaced, or impossible.

A destination is not a teleport and is not repeated fixed displacement.

The first implementation may use deliberately simple steering sufficient to
prove the cabin vertical. T0.10 does not require a production navmesh, global
path planner, crowd system, or obstacle-avoidance framework.

When navigation becomes real, it lives below `Destination` semantics:

```text
Destination
    -> route/path/steering
    -> authoritative locomotion
```

The server remains responsible for validating the requested destination and any
movement that results from it.

## Direct movement is superseding current state

Direct input is complete current state, not a queue of historical movement
events.

Example:

```text
sequence 100  Direct(north, Run)
sequence 101  Direct(northeast, Run)
sequence 102  Direct(east, Walk)
sequence 103  None
```

A missed intermediate sample need not be replayed merely because it existed.

This matches UltraSim's broader temporal rule: semantic current state should
converge to latest truth rather than replay every missed scheduler/input sample.

A future remote transport may redundantly resend current direct state. Transport
reliability policy remains Network work.

## Destination and cancellation are latched semantics

A destination differs from direct current state. It persists at authority until
one of these occurs:

- target reached;
- explicit cancel/stop;
- newer destination replaces it;
- newer direct-control intent takes ownership;
- actor loses control eligibility;
- route becomes invalid/impossible under concrete game rules.

Direct steering should normally supersede/cancel an autonomous destination while
manual control is active. A profile may later choose a different policy, but the
default UltraSim client should never fight the player by simultaneously applying
manual steering and an old destination.

## Command admission and sequence fencing

Sequence identity belongs to player-control admission, not to individual gameplay
effect methods.

One admitted player command/sample may legitimately contain multiple semantics,
for example:

```text
move east
+
Interact pressed
```

The actor/control-session sequence is validated and consumed once before
individual effects are interpreted.

Do not independently consume the same sequence in `ApplyMovement`,
`ToggleDoor`, and other action methods.

A replayed admitted command must never acquire a new effect merely because world
conditions changed after its first processing.

Control sequence identity is operational/session state unless a concrete durable
game rule says otherwise. Restore or authority/session replacement must establish
a fresh control epoch/fence rather than treating an old client sequence as durable
world history.

## Focus and interruption

Losing control focus must suppress direct movement/actions.

Regaining focus while a physical action key/button remains held must not invent a
new edge-triggered gameplay press. The client should re-seed held state on focus
return and require a release/new press before emitting a new `Pressed` action.

Continuous movement axes may resume according to the selected profile, but
edge-triggered interactions must not fire merely because the application regained
focus.

The same interruption concept applies to:

- UI/modal control capture;
- switching controlled actor;
- changing control profile;
- fast travel/teleport;
- actor incapacitation;
- authority/session replacement;
- explicit RMB/Escape cancellation.

These interruptions may currently converge to `LocomotionIntent.None`; their
client-side reasons need not become authoritative enums unless gameplay earns the
distinction.

## Interaction is adjacent to, not part of, locomotion

Raw `Interact`/`Primary` action bits are useful control input, but a simulation
client will increasingly nominate concrete world targets.

Examples:

```text
Interact with this door
Open this chest
Use this object
```

The client may select/raycast a target. Authority must still validate:

- target identity/lifetime;
- range;
- ownership/permission;
- relevant world state;
- future line-of-use or collision rules.

Do not trust client raycast results as authority.

The first cabin door/chest interactions should earn the smallest concrete target
request shape. Do not build a universal interaction bus.

## Camera profiles are client composition

Do not put:

```text
CameraMode = UO | Kenshi | Zomboid | Valheim
```

into Game, Network, persistence, world state, or Spatial.

A camera/control profile is a concrete client composition of camera policy,
control grammar, and neutral intent producers.

The first three reference profiles are:

### `OrbitCommand`

Primary UltraSim profile. This is the one to polish.

```text
camera
    target-following free orbit
    free yaw
    bounded pitch
    useful zoom range
    high-angle simulation default

mouse
    single click -> select/context
    hold -> Direct mouse steering
    release -> stop
    RMB/Escape -> cancel
    double click terrain -> Destination

keyboard
    WASD optionally produces Direct through camera basis

look
    normally follows profile/actor policy unless a later feature requires
    independent look
```

This profile is inspired by UO/Kenshi/Sims interaction grammar without copying
one game's exact controls.

### `ThirdPersonDirect`

Valheim-like architecture proof, intentionally incomplete.

```text
camera
    close target follow
    lower pitch
    yaw/pitch control
    no camera collision
    no shoulder switching
    no clipping solver
    no lock-on

movement
    WASD/stick
    camera-relative -> world-space Direct

interaction
    simple action input
```

The proof is successful if rotating the camera changes the world direction
produced by the same relative "forward" input without changing Game locomotion
contracts.

This profile is not a promise of a production third-person controller.

### `IndependentMoveLook`

Project Zomboid / Path of Exile-like architecture proof.

```text
camera
    fixed isometric-style angle/height
    no orbit or zoom while this profile is active

movement
    world-axis WASD/stick -> Direct
    W remains north regardless of camera or actor facing

look
    cursor/world point -> independent authoritative yaw/public facing
```

The proof must demonstrate:

```text
movement direction != facing/look direction
```

No combat, aim assist, weapon system, or production animation controller is
required.

## Profiles are tests, not product promises

All three reference profiles should remain small, compilable, and executable.

They are architecture canaries.

A minimal deterministic proof matrix is enough:

```text
OrbitCommand
    orbit camera
    single click does not move
    hold steer
    cancel without destination
    double-click destination
    no WASD locomotion
    actor reaches/stops as expected

ThirdPersonDirect
    rotate camera 90 degrees
    press relative forward
    resulting authoritative world movement rotates accordingly

IndependentMoveLook
    move west
    look east
    prove authoritative/projected facing can remain east while locomotion is west
```

If a future change requires Game to understand camera yaw, Godot input names, or
profile identity to keep one of these proofs working, the seam has regressed.

## Engine boundary

Engine adapters own:

- physical key/button/controller bindings;
- native input polling/events;
- mouse capture/cursor policy;
- focus/window state source;
- native camera object realization;
- screen-ray construction;
- native physics/raycast calls;
- scene/resource implementation.

Client Core owns reusable semantic interpretation where multiple profiles prove
it is truly engine-neutral:

- normalized control samples;
- gesture state;
- camera-relative -> world-space conversion;
- control-session sequencing;
- neutral camera/control frame values;
- locomotion/look/action intent formation.

Game owns:

- authority;
- actor eligibility;
- movement speed/rules;
- authoritative Delta integration;
- terrain/collision/navigation decisions;
- interaction validation;
- committed `WorldPosition`;
- public projection truth.

## Godot and future Unity

Godot and a future Unity adapter should be able to implement the same reference
profiles without changing Game.

The engine-specific work may differ substantially:

```text
Godot Camera3D / InputMap / Physics query
Unity Camera / Input System / Physics query
```

but both should converge to the same neutral world-space intents.

The profiles therefore double as portability evidence.

## T0.10 retained Direct implementation boundary — 2026-08-21

The first authority checkpoint now implements the Direct half of the neutral
locomotion model. It deliberately stops before `Destination` and before the
launchable `OrbitCommand` client.

`GamePlayerCommand` carries:

```text
GameControlSessionEpoch
monotonic sequence within that epoch
complete world-space Direct Q15 X/Z state
held/pressed adjacent action state
```

Client Core normalizes and quantizes Direct input, then ensures the discrete Q15
vector remains inside the unit circle. Game validates the same bound before
admission. A trusted authority route must bind the exact control epoch to the
actor before commands can be admitted; replacing or ending that binding clears
retained Direct state immediately. Repeating the same bind is idempotent and
does not reset the accepted sequence floor. The epoch and sequence floor are
generation/session state and are not persisted as world history.
Sequence exhaustion fails closed; continuing control requires a newly bound
epoch rather than wrapping the sequence inside the old lineage.

`GameControlSessionEpoch.Create()` is only process-local identity generation for
OfflineDirect and the launchable local client. A future N1 route must establish
the epoch through trusted live session/connection authority, or otherwise prove
uniqueness within that authority's control lineage. A remote server must not
assume an arbitrary client-generated `ulong` remains unique across client
process restarts.

A nonzero Direct command replaces the actor's retained current direction and
magnitude. Zero/zero is the complete stopped state. Command arrival does not
write `WorldPosition`. Runtime supplies an exact `SimulationStepDelta` after the
ECS graph/barrier; game locomotion integrates every active Direct actor once,
then `GameWorldState` commits Spatial/publication truth. One Direct command can
therefore drive many steps, and a rejected malformed or stale command cannot
replace it.

Permanent evidence covers:

- one retained command for one authoritative second at 20/60/120 Hz, with exact
  equal final X/Z;
- fixed 60 Hz authority receiving the same Direct state at 20/60/120 Hz, again
  with exact equal final X/Z;
- `west`, then `east`, before one fixed step integrating east exactly once and
  proving latest-admitted-state semantics;
- explicit zero-state release and control-session end stopping on the next
  step without drift;
- over-unit Q15 rejection without replacing the last good Direct state;
- epoch replacement cancelling old movement and rejecting stale-epoch commands;
- idempotent rebinding preserving the sequence replay fence;
- one command composing retained Direct intent with a pressed door interaction.

Still unfrozen/unimplemented at this boundary:

- gait vocabulary beyond the current fixed game speed;
- `Destination` representation, latching, arrival, and cancellation;
- camera-basis conversion/control profiles;
- pointer gesture interpretation;
- the human-launchable Godot client.

At this retained-Direct checkpoint, the integer remainder represented integrated
desired displacement and advanced before an outside-prepared-world step was
rejected. The later playable-collision hardening below deliberately retains that
meaning when a step is blocked; it does not silently redefine remainder as
successfully realized displacement.

## T0.10 Destination implementation boundary — 2026-08-21

The command/update vocabulary is now frozen for the first playable vertical:

```text
Unchanged
    command does not mutate locomotion

Stop
    clear any retained locomotion

Direct
    replace retained locomotion with world-space signed-Q15 state

Destination
    replace retained locomotion with canonical global X/Z
```

`Unchanged` is not retained authority. `GameWorldActions` still owns exactly
`None`, `Direct`, or `Destination` per controlled actor. This distinction lets
an action-only command interact with a door while Destination travel continues.
The Direct raw-input convenience path still maps zero axes to explicit `Stop`;
future control profiles produce the semantic update directly before common
sequencing.

The first Destination implementation is deliberately straight-line and terrain-
following. Authority validates that the nominated endpoint is representable and
present in prepared terrain before replacing current locomotion. Admission
retains the original route start/target and integer ceiling route length. Every
fixed step advances one scalar cumulative progress value by game speed times
exact `SimulationStepDelta`, then derives X/Z parametrically from the original
route rather than adding a newly rounded direction to the previous integer
position. The final progress clamps to the exact target and retires the active
slot. A prepared-world gap encountered after admission makes the simple route
impossible and clears it.

Rejected malformed or out-of-world destinations consume an otherwise fresh
command but leave the previous valid locomotion state unchanged. Direct, Stop,
control-session replacement, and control-session end all cancel Destination.
Multiple admitted updates before one step remain latest-state-wins.

Direct, Destination, control epoch, sequence floor, integration remainder, and
active-locomotion membership are operational generation state. They are not
durable save state. Restore/session replacement begins at `None`; durable NPC
jobs/routes remain a later game-domain decision rather than an accidental
property of player controls.

Permanent evidence covers:

- one diagonal Destination command reaching the exact same target at 20/60/120
  Hz, retiring, and remaining stopped;
- `Unchanged + Interact` preserving active Destination travel;
- newer Destination, Direct, and Stop supersession;
- `Destination west`, then `Destination east`, before one tick moving east once;
- session replacement/end cancellation;
- an out-of-world Destination rejecting without replacing valid Direct;
- save/restore discarding a latched player Destination.

The initial exact-arrival matrix used a route short enough to finish well before
its one-second observation and therefore did not, by itself, prove equal speed
while still travelling. Temporal hardening adds a 30.809 m non-cardinal route.
After exactly one authoritative second, 20/60/120 Hz all remain active at the
same exact X/Z, then reach the same endpoint and retire without drift. This
freezes the pre-navigation rule:

> For an unchanged straight-line Destination, original route plus elapsed
> authoritative time determines X/Z; fixed-step partitioning does not.

The cumulative representation adds four `long` values per addressable ECS slot:
route start X/Z, route length, and progress. The restored 64-slot fixture's
known `GameWorldActions` backing is 6,720 B. This is explicit bounded evidence,
not a claim that an ECS-capacity-sized control map remains optimal at production
world capacity.

No pathfinder, navmesh, obstacle avoidance, crowd steering, gait expansion,
camera profile, or pointer gesture policy is implemented by this checkpoint.

## T0.10 launchable OfflineDirect client implementation stop — 2026-08-21

The first human-launchable client now lives at `Client/Playable.Godot`. It is a
composition root beside the reusable `Client/_Godot` adapter rather than a
reason to give the adapter authority dependencies. It starts the real local
`ServerRuntime`, populates the authored woods/cabin pack, consumes actual
`ChunkPublicationStore` snapshots into the production semantic cache/window,
and realizes them through `GodotWorldRenderer`.

The actual application InputMap is configured. `OrbitCommandProfile` in Client
Core owns the mouse grammar:

```text
single click             selection only
hold                     Direct toward current world point
release after hold       Stop
RMB / Escape             Stop without Destination
double click             one Destination nomination
```

Godot owns physical event capture, screen-ray construction, and `Camera3D`.
Core owns hold/double-click state, world-direction formation, profile policy,
and sequencing. Game still receives no camera, key, screen, or profile values.
The launchable OrbitCommand mode is mouse-only. The crude
`ThirdPersonDirectProfile` owns WASD and rotates camera-relative forward into
world Direct. `IndependentMoveLookProfile` instead emits fixed world-axis Direct
plus a separate look point. The launchable composition applies that point through
the concrete Game-owned `FaceActorToward` operation, changing persisted/public
yaw without rotating locomotion. An asymmetric character mesh makes facing
visible; no combat, aiming, or animation framework is implied.

The application also exercises chunk crossing, selected door interaction,
two-sided-authorized bread transfer between backpack/chest, and F5/F9 durable
save/restore. Restore rebuilds Client semantic/presentation state from the new
generation and binds a fresh non-durable control epoch.

An explicit Game-owned `RelocateActor` operation closes the retained-route
discontinuity hole: successful relocation resolves current prepared terrain,
writes the authoritative position, and clears Direct/Destination before the
next step. The lower-level `GameWorldState.TrySetPosition` remains policy-free.
Permanent integration evidence covers both active Direct and active Destination
followed by relocation and proves neither resumes on the next tick.

`GameWorldActions` is intentionally still the small concrete T0.10 vertical
authority surface. Its breadth is a watch item, not grounds for speculative
service interfaces. Split it only when locomotion, interaction, or another
domain acquires an independently substantial concrete rule set.

The launchable project has a headless native startup smoke, but T0.10 remains
active until human acceptance and the broad performance/evidence matrix are
recorded. Network N1 and the legacy remote `InputVelocity` path are unchanged;
N1 must deliberately bind the new command/control-epoch semantics later.

Human launch review found and corrected three presentation seams before freeze:

- the engine-neutral upward terrain triangles are reversed only in the Godot
  adapter because Godot front faces use clockwise winding;
- profile switching now realizes visibly distinct overhead/third-person/
  independent camera distances and atomically stops prior locomotion;
- the OfflineDirect snapshot bridge compares every resident semantic hash
  before/after the complete polling transaction, so cross-chunk public identity
  supersession refreshes both old and new public layers rather than leaving a
  stale character `MultiMesh` ghost.

A second human pass exposed two further composition seams. A finite map edge had
been using one chunk identity both for desired structural-window membership and
the continuous viewer origin; the off-map fringe therefore rejected the camera
update. The engine-neutral window now accepts a separately clamped structural
center while offsets and the common render root remain relative to the actual
player chunk. Native smoke relocates twice within the far corner chunk and proves
the root follows both positions. F3 is now the promised fixed-isometric,
world-axis move/look canary: W remains north while mouse position publishes an
independent actor yaw. Numeric 1/2/3 shortcuts and on-screen buttons accompany
F1/F2/F3 because embedded editors may reserve function keys.

The native terrain gate now inspects every face of one generated mesh for Godot
front-face winding. The launch smoke switches all three cameras and moves the
controlled public identity across a chunk boundary and back, asserting exactly
one presented character at each point.

### T0.10 cabin-authoring and minimum collision hardening — 2026-08-21

Human review proved that the old “cabin” was not useful acceptance content. Its
four alleged wall placements each owned a square footprint and the launchable
asset resolver drew every structural definition as the same one-metre box. The
fixture now authors one recognizable 8 m x 6 m shell:

```text
north wall       one thin 8 m segment
west/east walls  two thin 6 m segments
south wall       two thin 3 m segments
doorway          2 m gap with the public cabin door centered in it
roof             overhead render-only slab
```

Definition-local wall variants select matching grounded Godot meshes. Walls,
door, chest, shelf, book, trees, and the controlled/public actors no longer use
center-pivot boxes that bury half their height beneath terrain. The roof is
translucent presentation and its authoritative bounds begin above the standing
actor volume; it is not a horizontal blocker.

The first minimum authoritative collision has also been earned. It remains a
concrete T0.10 rule, not a general physics/controller/navigation framework:

```text
resolve proposed X/Z against prepared terrain
    -> build the standing actor AABB
    -> complete-or-fail query of collidable prepared static/Multi bounds
    -> check the generation-owned concrete closed-door/chest blockers

clear
    -> commit WorldPosition

blocked Direct
    -> retain Direct and remain in place

blocked Destination
    -> stop the straight-line Destination as impossible
```

Opening the door removes only its dynamic blocking effect; closing it restores
that effect. The implementation does not use Godot physics as authority, add
sliding/capsules/steps, or route around obstacles. Authoritative relocation is
still an explicit discontinuity and may place an actor without running the
ordinary locomotion blocker.

Permanent integrated gates prove the exact rectangular Multi layout, closed-door
blocking/open-door passage, frozen Multi-wall blocking, chest blocking, retained
Direct pressure, and retirement of a blocked straight Destination. Static query
pressure fails closed and the warmed integrated path remains allocation-free.
Because collision adds a two-pass exact static query to each moved actor, the
pre-collision movement timings and smaller `GameWorldActions.ReservedBytes`
observations are historical rather than like-for-like performance baselines.
The additive Release evidence is
[`terrain-t0.10-cabin-collision-hardening-2026-08-21.json`](../../_Benchmarks/Baselines/terrain-t0.10-cabin-collision-hardening-2026-08-21.json).

## T0.10 real-client requirement

T0.10 must stop manufacturing the entire playable path only from tests.

Before T0.10 freezes, there must be a launchable Godot client scene/application
that a human can start and use against the actual OfflineDirect authoritative
runtime.

The primary path must use `OrbitCommand`.

At minimum a human should be able to:

1. launch/start or load the woods/cabin world;
2. see the bounded streamed/rendered local world;
3. orbit and zoom the camera;
4. single-click/select or inspect without implicitly moving;
5. hold the mouse to steer the controlled actor;
6. release to stop;
7. cancel steering with RMB/Escape without creating a destination;
8. double-click terrain to create destination movement;
9. use WASD as an alternate Direct producer;
10. cross a chunk boundary and keep camera/presentation coherent;
11. interact with the cabin door;
12. open/use the chest and perform one inventory/world mutation;
13. save, diverge, restore/reload, and recover the same authoritative visible
    state.

A development-only profile switch may expose the three reference profiles. The
reference profiles need only prove their concept:

```text
OrbitCommand          polished enough to be the actual UltraSim client
ThirdPersonDirect     crude but functional
IndependentMoveLook   crude but functional
```

T0.10 does not require production animation, camera collision, combat, navmesh,
controller remapping UI, accessibility settings, or polished art.

## T0.10 semantic hardening before performance freeze

The launchable-client work must also close the semantics exposed by the first
T0.10 implementation stop:

- replace fixed-displacement-per-command movement with authoritative
  Delta-integrated locomotion intent;
- admit/sequence one player command once before interpreting movement and actions;
- ensure a replayed command cannot gain a new effect later;
- validate both sides of nearby container transfer according to actor ownership
  or world proximity;
- prevent focus regain from manufacturing a new edge-triggered interaction;
- define/reconcile one authoritative cabin-door semantic so persisted door state
  and public orientation cannot silently contradict;
- use actual configured Godot input actions in the launchable client;
- connect actual runtime publication to the real Godot client rather than relying
  only on synthetic public rows in native acceptance.

The broad T0 performance/evidence matrix should be run after these semantics are
settled, so accepted measurements describe the behavior intended to freeze.

## Future directions

The same seams should permit later additions without changing authority
fundamentals:

- richer click-to-move/pathfinding;
- group selection and multi-actor destinations;
- possession/direct-control mode;
- production third-person camera;
- independent aiming/combat;
- twin-stick/controller profiles;
- vehicles;
- flying/swimming/climbing 3D locomotion;
- spectator/debug/editor cameras;
- engine-specific enhanced camera profiles;
- game-specific control profiles in other projects.

These are possibilities, not T0 requirements.
