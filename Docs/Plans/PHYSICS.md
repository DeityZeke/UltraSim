# Physics P0: injected authoritative motion plan

Status: P0.0 through P0.1d accepted/closed. P0.0 extracted the
accepted endpoint behavior into generation-owned UltraSim Simple Physics, P0.1a
added first-class occupancy-aware relocation, and P0.1b added bounded exact swept
blocker collision. Terrain T1.1 supplies multiple immutable structural surface
candidates per X/Z column; P0.1c now selects actual support, checks footprint and
clearance, and enforces bounded grounded steps. P0.1d adds Game-owned grounded/airborne
state, jump admission, deterministic gravity, horizontal air control, terrain-shell
landing/ceiling contact, restore reconstruction, and Game-owned fall recovery. Sliding,
general triangle time-of-impact, FrozenStatic landing support, and actor collision remain
unstarted.

Concept Demo D0.0 is the accepted first integrated consumer of this chain: it traverses beneath a
MountainShell, jumps and lands on a detached Sky shell, filters Jump from OrbitCommand, invokes an
authored telepad through occupancy-aware relocation, and falls into Game-owned recovery. D0.0 adds
no new Physics behavior; further provider work remains evidence-driven.

Target repository path: `Docs/Plans/PHYSICS.md`.

This document defines the intended boundary between Game locomotion, world
collision truth, and optional physics implementations. It deliberately does not
define a universal rigid-body engine abstraction. UltraSim first needs a small,
authoritative, deterministic motion/occupancy capability for the simulation it is
actually building. Richer providers such as Godot Physics, Jolt, PhysX, or a
game-specific platforming/combat implementation may be injected behind the seam
when concrete gameplay earns those capabilities.

The default implementation is **UltraSim Simple Physics**: a modern continuous-
world descendant of the movement/fit model used successfully by UO/RunUO. It is
not a miniature Jolt and it is not engine middleware.

## Decision

UltraSim separates four concerns:

```text
locomotion / gameplay intent
    what physical action an actor wants to perform

physics / motion resolution
    what part of that requested physical action is geometrically realizable

game transition / effects
    what gameplay consequences follow from the realized move

presentation / prediction
    how the result is rendered or predicted on a client
```

The authoritative flow is:

```text
control / AI / gameplay
        |
        v
Game locomotion / movement policy
    desired displacement / physical request
        |
        v
injected authoritative physics world
    terrain
    support
    static blockers
    dynamic collision projection
    sweep / occupancy
        |
        v
realized physical result
        |
        v
Game commits canonical world truth
    WorldPosition
    public orientation/state
    gameplay consequences
        |
        v
Spatial / publication / Network
        |
        v
client projection / renderer / optional prediction
```

Physics never owns actor existence, persistence identity, Game permission, input
semantics, camera policy, or Network authority.

The provider may be selected by a server composition root, a Godot server frontend,
a headless host, or a client executable that is also hosting an internal server.
The host selecting a provider does not move authority into the host UI or into the
client.

## Current T0 collision evidence

T0.10 already proved enough physical behavior to justify this system:

- authoritative Direct and Destination movement use exact simulation Delta;
- terrain height is resolved from prepared world truth;
- world statics carry `Collidable` bounds;
- walls, trees, cabin pieces, a closed door, and a chest can block movement;
- an open door can stop blocking;
- straight-line Destination stops when its route becomes impossible;
- explicit relocation terminates retained locomotion so an obsolete route cannot
  pull the actor back;
- collision queries are bounded and fail closed rather than interpreting partial
  results as clear space.

That implementation is accepted **prototype evidence**, not the final ownership
model.

Before P0.0, the T0 path still contained playable-world-specific collision policy
inside `GameWorldActions`, including concrete actor dimensions and a bounded
dynamic-blocker set for the cabin door/chest. P0.0 preserved that behavior while
moving physical resolution behind the smallest earned concrete boundary.

Do not rewrite the world, terrain, collision bounds, locomotion, or publication
architecture merely to create a new namespace. Extract only what has earned
independent ownership.

## P0.0 implementation checkpoint — 2026-08-28

P0.0 extracts the accepted T0.10 endpoint resolver without changing its physical
meaning.

The executable ownership shape is now:

```text
GameWorldActions
    command/control lineage
    retained Direct / Destination
    fixed-point desired endpoint
        |
        +--> GamePlayableCollisionProjection
        |        cabin door/chest Game state
        |            -> complete effective blocking bounds
        |
        v
UltraSimSimplePhysics
    prepared terrain height
    complete static/Multi bounds query
    effective dynamic blocking bounds
        |
        v
SimpleMotionResult
    Resolved / Blocked / OutsidePreparedWorld
        |
        v
GameWorldActions
    Direct/Destination blocked policy
    authoritative WorldPosition commit
```

[`Game/Physics`](../../Game/Physics/README.md) is a concrete project depending on
`Game/World`. It contains `SimplePhysicsBody`, `SimpleMotionRequest`,
`SimpleMotionResult`, and `UltraSimSimplePhysics`. It does not reference
`Game/Composition`, ECS entity identity, playable prefab constants, door state,
Network, Godot, or another native backend. No public universal physics/provider
interface was introduced.

The Game-owned projection remains deliberately finite. It retains at most sixteen
door/chest entities and reconstructs their effective bounds from current canonical
ECS state. Closed doors contribute bounds, open doors do not, and chests contribute
bounds. Contradictory door state fails closed. Generation initialization rebuilds
the projection; neither it nor the resolver's 64-hit static-query scratch is
persistence truth.

Exact preserved behavior includes:

- the 300 mm X/Z-radius, 1,800 mm-high upright playable body;
- 16 candidate static chunks and 64 complete-or-fail hit rows;
- all-or-nothing endpoint occupancy, with no sweep or substeps;
- retained blocked Direct and retired blocked straight-line Destination;
- unchanged fixed-point remainder and 20/60/120 Hz movement results;
- locomotion commit before current-world Spatial/publication maintenance;
- terrain-only relocation that may still enter occupied geometry and cancels
  retained locomotion;
- zero warmed caller-thread allocation.

The added dynamic-bound scratch raises the measured 128-slot playable aggregate
`GameWorldActions` reservation from 13,568 B to 14,336 B. This 768 B is sixteen
fixed `WorldStaticBounds` rows; it does not scale with active motion or allocate
on the hot path.

The accepted Release artifact is
[`physics-p0.0-simple-seam-2026-08-28.json`](../../_Benchmarks/Baselines/physics-p0.0-simple-seam-2026-08-28.json).
Its canonical integrated world hash is unchanged from the same-turn pre-extraction
baseline. The measured integrated tick remained neutral within ordinary run noise:
43.211 us mean / 52.270 us p95 before, and 43.203 us mean / 53.059 us p95 after.
Both observations report 0 B warmed allocation. These are semantic-extraction
regression observations, not a claim that P0.0 improves Physics throughput.

P0.0 does not claim continuous collision, relocation occupancy, support/step,
sliding, actor collision, gravity, navigation, Godot Physics, Jolt, or provider
replaceability. Those remain later, separately reviewed checkpoints.

## P0.1a implementation checkpoint — 2026-08-30

P0.1a adds one behavior capability without widening the resolver into a character
controller or provider abstraction.

At that checkpoint the concrete seam exposed adjacent queries:

```text
ResolveEndpoint(SimpleMotionRequest)
    desired locomotion endpoint
        |
        v
ProbeOccupancy(SimpleOccupancyRequest)
    target + fixed upright body
        |
        v
shared endpoint legality
    prepared terrain resolution
    complete static/Multi query
    complete effective dynamic projection
        |
        v
Clear / Blocked / OutsidePreparedWorld
```

`ProbeOccupancy` is mutation-free and fail-closed. A default request is invalid, a
default result is not clear, incomplete static/dynamic input is blocked, and an
unprepared location remains distinguishable from collision. Motion delegates to
the same implementation rather than maintaining a second physical oracle.

`GameWorldActions.RelocateActor` is the first consumer. Its transaction is:

```text
validate current actor
    -> capture complete Game-owned dynamic bounds
    -> probe target occupancy

blocked / outside prepared world
    -> return Blocked / OutsidePreparedWorld
    -> position unchanged
    -> retained Direct/Destination unchanged

clear
    -> commit terrain-resolved WorldPosition
    -> retire retained Direct/Destination
```

The actor remains excluded from its own projected bounds. Closed doors, chests,
and prepared static walls reject relocation; an open doorway accepts it. The
existing motion result, body dimensions, query capacities, generation rebuild,
and fixed reservation remain unchanged.

The accepted Release artifact is
[`physics-p0.1a-occupancy-relocation-2026-08-30.json`](../../_Benchmarks/Baselines/physics-p0.1a-occupancy-relocation-2026-08-30.json).
It reports all twenty integrated gates passing, unchanged canonical motion/world
hashes, 0 B warmed motion and direct occupancy allocation, and the unchanged
14,336 B aggregate `GameWorldActions` reservation. The integrated observation was
44.571 us mean / 55.149 us p95 versus the P0.0 artifact's 43.203 us mean /
53.059 us p95; this small single-run movement is retained as regression context,
not presented as a causal throughput change.

P0.1a does not add sweep, substeps, thin-wall guarantees, support categories,
step-up/down, low-ceiling semantics beyond current AABB overlap, sliding,
depenetration, actor collision, gravity, spawn policy, or alternate providers.

## P0.1b implementation checkpoint — 2026-08-30

P0.1b changes blocker collision intentionally while retaining the same body,
authoritative target-terrain resolution, Game-owned dynamic projection, bounded
scratch, and Direct/Destination policy.

The motion entry point is now named `ResolveMotion`. It builds the upright body
bounds at the current canonical position and terrain-resolved target, queries one
complete union broadphase, and exact-tests each collidable candidate over the
linear request:

```text
body A ------------------------------> body B
        bounded union broadphase
                    |
                    v
        exact swept-AABB intervals
        static/Multi + projected dynamic bounds
                    |
          +---------+---------+
          |                   |
       no hit              any hit / pressure
          |                   |
       Resolved              Blocked
```

The interval solver keeps time as rational values and compares cross-products in
`Int128`. It does not use floating point, millimeter substeps, or a rectangular
broadphase as the final collision answer. Half-open world bounds retain their
existing semantics: exact boundary contact is not overlap. Both positive and
negative motion use the same calculation, so diagonal cabin-corner traversal is
blocked in either direction. A deliberately off-path dynamic bound inside the
union rectangle remains clear after the exact test.

Occupancy is the zero-displacement use of the same `IsSweepClear` physical
legality implementation. Motion and occupancy therefore still share terrain,
static-query, projected-dynamic, capacity, and overlap semantics rather than
growing separate physical oracles.

The result remains deliberately all-or-nothing:

```text
clear complete sweep
    -> commit requested terrain-resolved target

blocked sweep / incomplete query
    -> realize no partial displacement
    -> Direct remains retained
    -> impossible straight Destination retires
```

There is no time-of-impact/contact payload, partial advance, sliding, or
depenetration. Terrain is authoritatively sampled at the target as before; P0.1b
does not claim continuous support, void/gap traversal, steps, or grounding along
the segment.

Permanent regressions prove:

- a clear endpoint 4 m beyond the 300 mm cabin wall cannot tunnel through it;
- a 2 mm projected dynamic blocker cannot be crossed in either X direction;
- both endpoints around the cabin corner are clear but the diagonal sweep blocks
  in both directions;
- exact half-open contact remains clear;
- a candidate inside only the union rectangle does not become a false hit;
- broadphase coverage beyond the fixed capacity fails closed;
- a deliberately large one-second Game step keeps blocked Direct retained and
  retires blocked straight Destination;
- accepted 20/60/120 Hz locomotion/canonical results remain unchanged;
- 1,000 warmed clear sweeps allocate 0 B.

The accepted Release artifact is
[`physics-p0.1b-swept-motion-2026-08-30.json`](../../_Benchmarks/Baselines/physics-p0.1b-swept-motion-2026-08-30.json).
It reports all twenty-two integrated gates passing, the unchanged canonical
`B764...F220` playable-world hash, 0 B warmed motion/occupancy/sweep allocation,
and the unchanged 14,336 B aggregate `GameWorldActions` reservation. Its retained
integrated observation is 47.387 us mean / 70.489 us p95 versus P0.1a's 44.571 us
mean / 55.149 us p95. Three additional clean Release observations measured
47.077-49.511 us mean and 70.519-72.839 us p95, so the higher tail is recorded as
real checkpoint context rather than hidden as one-run noise. The absolute cost is
accepted for the new continuous blocker guarantee; it is not presented as a
throughput improvement or as evidence for later support/response features.

P0.1b does not add support categories, step-up/down, low-ceiling behavior beyond
the existing swept AABB, sliding, contact response, depenetration, continuous
terrain support, actor collision, gravity, navigation, or alternate providers.

## P0.1c implementation checkpoint — 2026-09-03

P0.1c consumes the accepted T1.0/T1.1 structural surface facts without adding an
authoring-layer concept to Physics. `SimplePhysicsBody` now requires explicit
upright extents and maximum step-up/down values; the Cabin human profile remains
300 mm in X/Z radius, 1,800 mm high, with 500 mm step-up and step-down. There is
no provider-global hidden step default.

The two entry points remain distinct while sharing the physical legality path:

```text
ProbeOccupancy(requested X/Y/Z)
    -> nearest Base/Top support by physical elevation
       exact tie prefers the lower support
    -> footprint + Bottom-face clearance
    -> static/projected-dynamic blocker legality

ResolveMotion(current -> target X/Z)
    -> current support must still match canonical current Y
    -> sample grounded path at <=250 mm spacing
    -> at each sample choose highest support reachable within body step limits
    -> footprint + Bottom-face clearance
    -> exact P0.1b swept static/projected-dynamic blockers
```

The result carries the selected `WorldTerrainColumnSurface`. `Resolved`/`Clear`
requires valid support; intentional resident void returns `Unsupported`, missing
prepared structure returns `OutsidePreparedWorld`, and incomplete bounded work
fails closed as `Blocked`. Authoritative relocation uses nearest-support occupancy:
unsupported, blocked, and unavailable requests preserve position and retained
locomotion; success commits the support-resolved Y and then retires locomotion.

Support is geometry-ordered. `WorldSurfaceId`, fragment order, and editor shell
labels never define vertical rank. Present BaseTerrain and additional Top faces
are support candidates. Additional Bottom faces are clearance candidates; the
Bottom belonging to a selected two-sided Top does not prevent standing on that
same shell. The center and four body-footprint corners must all find support within
the accepted height window. Exact body-top/ceiling contact retains half-open
non-overlap semantics.

Grounded path work is fixed: 250 mm maximum sample spacing, 512 path samples, five
footprint points, and stack scratch for 129 column faces (one BaseTerrain plus
Top/Bottom for the default 64 fragments/chunk). Custom packs with a wider column
fail closed in this provider rather than partially interpreting geometry. Existing
16-chunk/64-hit static scratch, the 16-entry Game collision projection, provider
heap reservation, and warmed allocation remain unchanged.

Permanent regressions prove geometry rather than identity selects mountain,
intermediate, BaseTerrain, and below-base support; nearest placement does not jump
to a farther upper shell; one shell acts as support above and clearance below;
intentional void is unsupported while a shell over void supports; low ceilings
block; exact top contact clears; 400 mm step-up/down succeeds; 1,000 mm step-up
blocks; a one-meter void cannot be skipped; oversized path work fails closed;
relocation is transactional; 20/60/120 Hz-equivalent traversal agrees; a replacement
generation owns a fresh resolver over the immutable shell pack; and 1,000 warmed
support/step/clearance resolutions allocate 0 B.

The accepted Release artifact is
[`physics-p0.1c-support-clearance-2026-09-03.json`](../../_Benchmarks/Baselines/physics-p0.1c-support-clearance-2026-09-03.json).
It reports all twenty-three integrated gates passing, the unchanged playable-world
`B764...F220` hash, 0 B warmed support-path allocation, unchanged 14,336 B
`GameWorldActions` reservation, and one integrated observation of 54.835 us mean /
82.309 us p95. That observation includes the intervening accepted T1.1/client
baseline and is regression context, not a causal P0.1c throughput claim.

P0.1c intentionally does not add persistent grounded ECS state, airborne motion,
gravity, jump, falling, landing, sliding, partial advance, arbitrary triangle
time-of-impact, or support supplied by FrozenStatics/Multis/dynamic blockers.
Those structures remain blockers at this checkpoint. The next reviewed behavior
checkpoint owns vertical motion over this accepted grounded foundation.

## P0.1d implementation checkpoint — 2026-09-03

P0.1d adds the first authoritative vertical lifecycle without moving policy into Physics:

```text
Client/Godot
    edge-triggered Jump intent only

GameWorldActions
    grounded/airborne state
    jump admission
    fixed-point vertical velocity + integration remainder
    gravity and fall-recovery policy
        ↓
UltraSimSimplePhysics.ResolveAirborneMotion
    bounded structural availability
    Top/Base landing
    Bottom-face ceiling contact
    exact static/projected-dynamic blocker sweep
        ↓
GameWorldActions
    authoritative WorldPosition commit
    landed/airborne transition
```

The playable policy uses an 11,000 mm/s jump speed, -18,000 mm/s² gravity, and a
Game-configured recovery threshold at -50,000 mm. Recovery targets
`GamePlayableWorld.PlayerStart`, but still passes through the accepted first-class occupancy
oracle before Game commits it. Physics knows neither the safe point nor why recovery occurred.

`GameVerticalMotionKind` and its velocity/remainder arrays are generation-owned operational state,
not ECS components and not persistence truth. A successful occupancy-aware relocation establishes
Grounded with zero vertical velocity. Generation initialization and restore classify an actor at
its exact saved support as Grounded; a structurally valid unsupported position becomes Airborne
with zero velocity so gravity resumes. Invalid or unavailable restored placement attempts the
Game-owned safe recovery and otherwise remains fail closed. ECS slot reuse is generation fenced,
including the case where an unsupported new actor replaces an airborne retired generation.

Jump is a replay-fenced `GamePlayerCommand` action edge. It succeeds only from Grounded after a
one-millimeter upward physical clearance probe. Fresh airborne Jump attempts return `NotGrounded`;
there is no coyote time, buffering, double jump, wall jump, stamina, or client-authoritative
velocity. ThirdPersonDirect and IndependentMoveLook expose the action; OrbitCommand intentionally
retains its no-jump grammar and may use future authoritative relocation/telepad content instead.

Airborne Direct/Destination intent supplies X/Z while Game vertical state supplies Y. The combined
request does not run through grounded support snapping. Descending motion lands on the first
geometry-ordered Base/Top support crossed with a valid five-point footprint and clearance;
ascending motion stops at the first Bottom face crossed by any footprint point, zeros upward
velocity, and remains airborne for gravity on the next step. Intentional T1.0 void supplies no
landing surface. Unavailable or incomplete structural data remains fail closed.

Vertical integration is exact integer fixed point over authoritative time quanta. It retains an
`Int128` trapezoidal displacement remainder per active actor, so accepted final jump/fall/landing
truth agrees at 20, 60, and 120 Hz. One fixed active-actor set processes retained horizontal intent
and airborne state exactly once per simulation step; idle grounded actors incur no scan.

The Simple airborne path remains bounded at <=250 mm sample spacing, 512 samples, five footprint
columns, 129 stack-resident column faces, 16 static candidate chunks, 64 static hits, and the
existing complete 16-entry Game dynamic projection. One thousand warmed terrain-shell airborne
resolutions allocate 0 B. The added generation-owned arrays raise the accepted integrated fixture's
`GameWorldActions` reservation from 14,336 B to 16,704 B.

P0.1d deliberately does not make FrozenStatics, Multis, or dynamic bounds into support surfaces.
They remain exact swept blockers, so a blocked airborne request realizes no partial displacement.
Terrain-shell side faces and arbitrary triangle time-of-impact are also outside this first vertical
contract. There is no slope sliding, depenetration, actor-vs-actor collision, moving platform,
buoyancy, navigation, or general rigid-body solver.

The accepted Release artifact is
[`physics-p0.1d-vertical-motion-2026-09-03.json`](../../_Benchmarks/Baselines/physics-p0.1d-vertical-motion-2026-09-03.json).
It reports all twenty-seven integrated gates passing, the unchanged playable-world
`B764...F220` hash, 0 B warmed airborne allocation, 16,704 B `GameWorldActions`
reservation, and one integrated observation of 57.049 us mean / 87.739 us p95.
The preceding P0.1c artifact observed 54.835 us / 82.309 us; these single-run integrated
figures retain the cost change as regression context and are not a causal throughput claim.

## RunUO archaeology

RunUO is useful here because its core game survived for years without a general
rigid-body simulator.

The relevant architecture is approximately:

```text
Mobile movement policy
        |
        v
Movement.CheckMovement(...)
        |
        v
replaceable IMovementImpl
        |
        v
terrain + statics + items + mobiles
        |
        v
support / step / clearance / occupancy
        |
        v
legal destination + new Z
```

RunUO also exposes a separate `Map.CanFit(...)` family for occupancy/spawn-style
queries.

The durable lessons are:

- a replaceable movement implementation is useful;
- movement resolution and occupancy queries are separate but adjacent concepts;
- terrain is primarily a support surface;
- `Surface` and `Impassable` are different ideas;
- actor height and step height are physical parameters;
- clearance above the support surface matters;
- movable/static/mobile blockers participate in occupancy;
- diagonal corner cutting must be prevented;
- traversal capabilities such as walking/swimming/flying alter what geometry is
  considered legal;
- movement speed/cadence lives outside the collision implementation;
- a simple world can provide good gameplay without masses, torques, joints,
  sleeping bodies, or contact manifolds.

Do **not** copy RunUO's historical coupling:

- no process-global mutable `Movement.Impl`;
- no physics API that receives a giant Game `Mobile`/entity object and inspects its
  class hierarchy;
- no physics special cases for specific body IDs, item graphic IDs, maps, ghosts,
  pets, spell fields, or regions;
- no arbitrary `OnMoveOver` gameplay callbacks fired from a physics solver;
- no assumption that movement is an eight-direction tile step;
- no giant tile flag enum defining every unrelated content property.

UltraSim carries forward the physical concepts, not the legacy object model.

## Frozen principles

Physics P0 inherits the existing UltraSim principles:

```text
GAME OWNS AUTHORITY.
PHYSICS RESOLVES PHYSICAL POSSIBILITY; IT DOES NOT OWN GAME TRUTH.
LOCOMOTION CHOOSES DESIRED MOTION; PHYSICS CHOOSES REALIZED MOTION.
PERSISTED WORLD TRUTH IS NOT A PHYSICS-ENGINE CACHE.
ENGINE OBJECTS ARE NOT GAME IDENTITY.
NETWORK DOES NOT OWN PHYSICS.
CAMERA DOES NOT OWN PHYSICS.
PRESENTATION COLLISION IS NOT AUTHORITATIVE COLLISION.
MEMORY IS BOUNDED.
HOT PATHS MUST NOT REQUIRE UNBOUNDED ALLOCATION.
TICK IDENTIFIES AUTHORITY STATE. DELTA DEFINES PHYSICAL DURATION.
```

P0 adds:

```text
A PHYSICS PROVIDER IS GENERATION/HOST COMPOSITION, NOT PROCESS-GLOBAL STATE.
THE DEFAULT PROVIDER IS ULTRASIM SIMPLE PHYSICS.
SUPPORT, BLOCKING, AND TRAVERSAL ARE DISTINCT CONCEPTS.
OCCUPANCY AND MOTION RESOLUTION ARE BOTH FIRST-CLASS.
GAME RULES ARE RESOLVED BEFORE/AFTER PHYSICS, NOT HIDDEN INSIDE THE SOLVER.
A PROVIDER MAY OWN TRANSIENT SOLVER STATE; GAME OWNS CANONICAL STATE.
BACKENDS MUST SATISFY GAME-SEMANTIC CONFORMANCE, NOT SHARE INTERNAL ALGORITHMS.
RICHER PHYSICS CAPABILITIES ARE ADDED ONLY WHEN A REAL GAME FEATURE EARNS THEM.
```

## Ownership and dependency shape

The intended dependency shape is:

```text
Game / Simulation
    locomotion
    gameplay permissions
    physical body/profile selection
    post-motion gameplay transitions
        |
        v
Physics contract / capability
        |
        +----------------------+-----------------------+
        |                      |                       |
        v                      v                       v
UltraSim Simple          Godot adapter             Jolt / other
Physics                  (optional host)           provider
        |
        v
Game/World collision view
    terrain support
    static bounds
    dynamic collision projection
    prepared-world limits
```

A backend may depend on a host/engine package. Game must not.

Examples:

```text
headless dedicated server
    -> UltraSim Simple Physics

Godot server frontend
    -> UltraSim Simple Physics

Godot server frontend
    -> Godot Physics provider

Godot server frontend
    -> Jolt provider

Godot client hosting an internal authoritative server
    -> selected provider injected into the internal server

custom dedicated host
    -> future native Jolt / PhysX / game-specific provider
```

The Godot server frontend is a **composition root**, not the owner of physics.

A future package should be able to install/reference a provider and change host
composition without changing Game locomotion code.

## Provider lifetime

Physics provider registration should not be a process-global singleton.

Conceptually:

```text
Server host / composition
        |
        v
PhysicsProvider
        |
        v
create one PhysicsWorld / PhysicsGenerationState
for the authoritative Game generation
```

Generation replacement, restore, or world replacement may rebuild provider-
specific transient state from canonical Game truth.

This allows multiple worlds/tests/providers in one process and avoids RunUO's
global replacement limitation.

The exact C# names and interface split are not frozen before P0.0 implementation
evidence.

## Canonical truth versus transient physics state

Game/persistence may own physical facts such as:

```text
WorldPosition / orientation
canonical velocity if gameplay requires it
body/shape definition
game-semantic material/traversal properties
durable dynamic-object state if later earned
```

A provider may own transient implementation data such as:

```text
native body handles
broadphase trees
contact caches
solver islands
sleep caches
engine resource identity
temporary sweep/query state
```

Provider transient state is rebuilt after restore/generation replacement.

A Jolt world, Godot `PhysicsServer3D` world, or future PhysX scene must never become
the save file or the only place that knows canonical entity state.

## First P0 contract

P0 begins with the smallest behavior the current game actually needs.

Conceptually:

```text
MotionRequest
    actor/body identity for self-exclusion
    collision shape
    traversal/collision profile
    current authoritative transform/position
    desired displacement
    authoritative Delta when needed

MotionResult
    realized transform/position
    realized displacement
    grounded/support result
    blocked/limited result
    bounded contact summary if gameplay actually needs it
```

and a query adjacent to motion:

```text
CanOccupy / Probe
    collision shape
    target transform/position
    traversal/collision profile
        ->
    clear / blocked / unsupported
    optional bounded reason/contact summary
```

These names are conceptual. Do not freeze a large public API before the first
implementation and conformance tests prove the required fields.

### Why occupancy is first-class

Motion and occupancy answer different useful questions:

```text
ResolveMotion
    "How far can this actor physically travel this step?"

CanOccupy
    "Can a body legally exist at this location?"
```

Occupancy is useful for:

- spawn placement;
- authoritative relocation/teleport;
- resurrection/respawn;
- NPC placement;
- future housing/decor validation;
- navigation probes;
- knockback targets;
- recovery/fail-closed checks.

Do not require callers to fake a zero-length movement just to ask an occupancy
question.

## Shapes

P0 should support only shapes earned by the first vertical.

The default playable character may begin as a simple upright shape:

```text
upright AABB
or
capsule-like semantic shape
```

The exact first representation is measured/implemented, not pre-decided for
backend convenience.

The important contract is semantic:

```text
body footprint
body vertical extent
support/ground reference
```

A provider adapter may map that shape into its native representation.

Do not expose Godot `Shape3D`, Jolt `Shape`, PhysX geometry objects, RIDs, or native
body IDs through the Game-facing seam.

## Blocking, support, and traversal

P0 should not reduce every physical surface to one `Collidable` boolean.

At minimum the design must distinguish:

```text
blocking
    this geometry may prevent body volume from occupying/crossing space

support
    this geometry may provide a valid standing/grounding surface

traversal category/material
    this surface may require or permit a capability such as ground/swim/fly
```

The exact compact representation is earned by T0 world data and P0 measurement.

A floor can support without behaving like a wall.

A wall can block without being a valid support surface.

Water may be traversable for one profile and invalid for another.

An open door may contribute no blocking geometry while the same entity in its
closed state does.

Physics should receive the effective physical projection. It should not inspect
game-specific types like `CabinDoor` to decide whether they block.

The same rule applies to future stateful or perceived world surfaces. Physics
must not interpret terrain material IDs as a combined topology/Game-state enum,
and it must not know that a surface is an illusion, secret wall, breakable wall,
spell effect, or damaged object. Game combines immutable authored truth with its
mutable state and supplies only the resulting physical geometry/behavior:

```text
combat / spell / interaction / world state
        -> Game surface state
        -> effective physical projection
        -> Physics
```

A physically absent surface may still have an observer-specific presentation or
occlusion projection. A physically present blocker may be hidden from rendering.
Those perception decisions do not enter the authoritative Physics world and do
not alter canonical physical truth. Stable identity and persistence for mutable
surfaces belong to World/Game, not to provider-native bodies or geometry slots.

## Traversal profile

RunUO mixed creature/game properties directly into movement code. UltraSim should
instead pass the physical result of Game policy.

Conceptually a motion profile may eventually contain fields equivalent to:

```text
shape
maximum step up
maximum step down if needed
ground traversal allowed
water traversal allowed
flight allowed
collision layers / masks
```

Only add fields when a concrete body/gameplay feature earns them.

Examples:

```text
human
    ground yes
    water maybe later
    flight no

fish
    ground no
    water yes

ghost
    Game selects a collision profile that ignores ordinary blockers

flying creature
    provider receives a flight-capable traversal profile
```

Physics does not ask whether the entity is dead, a pet, a GM, or a particular
species. Game chooses the profile.

## UltraSim Simple Physics

UltraSim Simple Physics is the default authoritative provider.

Its goal is:

> Resolve simulation-oriented movement, grounding, support, and occupancy cheaply,
> deterministically, and without requiring a full rigid-body simulation.

The expected first behavior is:

```text
desired horizontal/physical displacement
        |
        v
bounded candidate world query
        |
        +--> terrain/support
        +--> static blockers
        +--> dynamic collision projection
        |
        v
continuous/swept body test
        |
        v
step/support/clearance resolution
        |
        v
realized authoritative position
```

Simple Physics should be usable in:

- headless dedicated servers;
- offline/single-player internal servers;
- tests/benchmarks;
- hosted servers that do not need richer physics;
- future simulation games whose physical requirements remain UO/Zomboid-like.

It must not require Godot, Jolt, Unity, Unreal, PhysX, or GPU/native rendering.

## Support and grounding

The first provider should treat terrain and eligible world geometry as support
candidates.

A grounded move conceptually asks:

```text
what support exists under/at the candidate footprint?
what standing height does that support imply?
is the support valid for this traversal profile?
is there enough clearance above it?
can the actor reach it within step rules?
```

This is the continuous-world successor to RunUO's land/static/item support search
and `newZ` result.

Terrain interpolation remains authoritative Game/World truth. The provider should
not independently reconstruct a different terrain surface from render meshes.

## Step rules

Step-up is a real Simple Physics capability, not a camera/animation trick.

A small obstacle/stair may be traversable when:

```text
candidate support height - current support height <= maximum step up
```

A taller obstacle blocks.

Step-down/fall behavior should remain deliberately narrow until implementation
pressure earns it. P0 may begin with a bounded grounded descent rule rather than a
general gravity solver.

Actor step capability belongs to the body/motion profile, not a global hard-coded
constant.

## Clearance

A support surface is not enough by itself.

The provider must verify that the body's occupied volume at the candidate standing
transform does not overlap blocking geometry.

This covers:

- low ceilings;
- walking under structures;
- standing on raised surfaces;
- bridges/platforms;
- door frames;
- dense clutter;
- stacked structural geometry.

Clearance is physical. Gameplay permissions around the object are not.

## Swept motion and corner behavior

The T0 occupancy proof checks candidate positions. P0 should earn a true movement
resolver that prevents tunneling through thin blockers.

Conceptually:

```text
body at A
desired displacement A -> B
        |
        v
sweep / conservative continuous test
        |
        v
earliest physically valid realized motion
```

The exact sweep algorithm may differ per provider.

The Simple provider must prevent diagonal corner cutting without copying RunUO's
eight-direction side-cell checks. The invariant should fall out of continuous body
geometry.

Do not claim continuous collision for a provider until thin-wall/high-speed tests
prove it.

## Blocking behavior and sliding

P0.0 may preserve the current simple rule:

```text
blocked move
    -> no realized movement for that request
```

Sliding along surfaces, depenetration, stair smoothing, or iterative contact
resolution should be added only if human gameplay shows that all-or-nothing motion
is insufficient.

Do not implement a general character controller merely because richer engines
already contain one.

If sliding is earned later, it belongs in the provider's physical resolution
contract and must receive conformance evidence.

## Gravity and falling

RunUO mostly chooses a legal supported Z rather than simulating general ballistic
motion. UltraSim should start similarly.

P0 does not require:

```text
global Newtonian rigid-body gravity
ballistic jumping
air-control platforming
continuous falling for every world object
```

If the game earns falling:

```text
unsupported body
    -> Game/physics state establishes vertical desired velocity
    -> authoritative Delta advances vertical desired motion
    -> provider resolves collision/support
```

The exact ownership of persistent vertical velocity is decided when falling becomes
a real feature.

Do not hide durable/game-semantic velocity solely inside a backend cache.

## Knockback and displacement

Knockback is a likely early richer motion producer, but it should not require a
full rigid-body engine.

Conceptually:

```text
combat/gameplay effect
        |
        v
desired displacement / impulse-like game intent
        |
        v
same authoritative physics provider
        |
        v
realized displacement
```

Whether the first representation is distance-over-time, velocity, or another
semantic effect is deferred until combat/impact gameplay earns it.

Physics resolves geometry. Game determines why the actor was displaced and what
damage/stun/etc. means.

## Dynamic blockers

Dynamic entities may contribute physical geometry to the provider through a world
collision projection.

P0 should remove playable-world-specific switches such as:

```text
if CabinDoor ...
if WorldChest ...
```

from the physics implementation.

Instead, Game/World exposes effective collision shapes/properties:

```text
closed door
    blocking shape present

open door
    blocking shape absent or changed

chest
    blocking shape present

character/NPC
    dynamic body shape if the selected rules make actors block
```

The provider consumes physical data, not gameplay class identity.

Dynamic collision projection must remain bounded and generation-safe.

## Actor-versus-actor movement

RunUO combines physical overlap, shove rules, hidden/admin exceptions, stamina, and
movement callbacks.

UltraSim must separate them.

Possible future flow:

```text
Physics
    reports actor/body collision

Game
    decides:
        hard block?
        allow overlap?
        shove?
        stamina cost?
        combat effect?
```

Do not put reputation, GM access, hidden state, stamina, or social rules in the
physics provider.

Character-character blocking itself should not be added until gameplay decides
whether dense crowds should collide, overlap, soft-separate, or use another model.

## Triggers, regions, and move-over effects are not physics

Physics may detect physical contacts or transitions if a concrete capability is
needed, but it does not directly invoke arbitrary Game effects.

Examples that stay outside the solver:

```text
walk onto trap
enter poison field
cross region boundary
step on pressure plate
trigger quest volume
shove another character
door permission
interior transition
```

The preferred shape is:

```text
physical move resolves
        |
        v
Game commits canonical movement
        |
        v
domain systems evaluate relevant resulting transitions
```

Do not rebuild RunUO-style `OnMoveOver` callbacks inside the provider abstraction.

## Regions and permissions

A region/game rule may reject or transform a movement request, but region
authorization is not collision geometry.

Examples:

```text
jail boundary
private property restriction
scripted cutscene lock
incapacitation
server admin restriction
```

Game should resolve those permissions around the physics call.

Physics answers physical possibility, not legal/social/gameplay permission.

## Locomotion relationship

The movement architecture remains:

```text
Direct / Destination / future AI
        |
        v
Game locomotion
    speed/rules x authoritative Delta
        |
        v
desired displacement
        |
        v
physics provider
        |
        v
realized displacement
        |
        v
WorldPosition
```

Physics must not consume:

```text
WASD
mouse gesture
camera yaw
control profile name
Destination command sequence
gait input binding
Godot InputEvent
```

Locomotion remains the only layer that interprets those semantics into desired
motion.

## Desired versus realized remainder semantics

T0 deliberately deferred whether fixed-point motion remainder should represent
desired or successfully realized displacement when collision prevents a move.

P0 must make this explicit.

The default expectation should avoid storing blocked desired displacement as a
future "spring" that releases when the obstruction disappears unless a specific
motion type wants that behavior.

For ordinary walking:

```text
desired movement blocked by wall
    should not accumulate meters of hidden displacement
    that launch the actor forward when the wall opens
```

The exact fixed-point accounting must be deterministic and tested at 20/60/120 Hz.

Direct, Destination, sliding, and any future falling/knockback semantics may require
different retained state, but no behavior should inherit remainder policy by
accident.

## Destination relationship

Destination remains a locomotion goal, not a physics path.

For the simple no-navigation implementation:

```text
Destination
    -> straight desired motion
    -> physics provider
    -> obstruction makes route impossible
    -> Destination retires
```

Future navigation may instead do:

```text
Destination
    -> route/path/steering
    -> desired motion
    -> same physics provider
```

Navigation does not replace collision and physics does not become pathfinding.

## Relocation and teleport

An authoritative positional discontinuity remains a Game operation.

The operation should:

1. validate/resolve the requested target against prepared world truth;
2. use occupancy/physics validation as required;
3. terminate incompatible operational locomotion;
4. commit the canonical new transform;
5. rebuild/synchronize provider transient state if the backend requires it.

A provider must not silently teleport canonical Game state because a native body
was moved in an engine editor/frontend.

## Provider injection

The host selects the authoritative provider.

Examples:

```text
Server.Headless
    UseUltraSimSimplePhysics()

Server.Godot
    UseGodotPhysics()
or
    UseJoltPhysics()

Client.Godot hosting single player
    creates internal ServerRuntime
    injects selected provider into that server

custom game host
    installs provider package
    selects provider during composition
```

Exact configuration syntax is future implementation detail.

The same Game code above the seam should run against each provider.

### Client-hosted single player

When the client executable also hosts the authoritative server, it may select the
same provider for:

```text
internal authoritative server physics
client-side prediction physics
```

This is a deployment/composition convenience, not shared authority.

Even in one process, server and prediction should own separate physics worlds and
separate transient handles/caches.

## Provider conformance versus backend equality

Backends do not need identical internal algorithms.

They must satisfy the same **game-semantic conformance profile**.

Initial provider-neutral scenarios should include:

```text
flat supported motion
terrain height following
wall blocking
open doorway traversal
closed door blocking
chest/static blocking
step up within limit
step too high blocks
low ceiling blocks
diagonal/corner traversal does not tunnel
thin wall does not tunnel at accepted maximum displacement
occupancy succeeds/fails consistently
relocation occupancy
prepared-world boundary fails closed
```

Where exact canonical output is part of the Game contract, providers must match it.

Where richer providers inherently differ in contact detail, the conformance test
should assert only the semantic invariant actually required by Game.

Do not weaken a required exact invariant merely to make a backend pass.

## Provider canaries

P0 should prove the seam against multiple implementations, but not build all
physics features three times.

Target canaries:

```text
1. UltraSim Simple Physics
   default production/reference provider

2. Godot Physics provider
   optional engine-hosted portability canary

3. Jolt provider
   optional materially different solver canary,
   initially through Godot if that is the cheapest real integration
```

The Godot/Jolt canaries need only implement the P0 contract required by the
conformance suite.

Do not implement joints, ragdolls, vehicles, or general rigid-body gameplay merely
for provider parity.

If an external backend cannot express the P0 semantics without leaking its native
types through Game, improve the adapter/provider implementation before widening
the Game contract.

## Prediction and backend compatibility

Authoritative physics is server-owned.

Remote clients may use:

```text
no prediction
approximate prediction
matching-provider prediction
```

depending on the game and physics complexity.

Simple walk/collision may tolerate:

```text
server = Jolt
client prediction = UltraSim Simple
```

if measured reconciliation remains acceptable.

Complex platforming/combat may not.

A future Network/session capability may therefore advertise a physics behavior or
prediction-compatibility identity.

Conceptually:

```text
authoritative physics behavior profile/version
prediction compatibility requirement
optional exact implementation/config fingerprint when truly required
```

Do not make a backend brand such as `Jolt` universal Game truth unless actual
prediction evidence proves an exact backend requirement.

A remote server may require:

```text
matching provider/profile
or
prediction disabled
or
connection rejected for a physics-dependent game mode
```

The policy is earned during a real prediction milestone, not P0.

## N1 relationship

Network N1 should transport canonical authoritative results and control semantics;
it should not transport provider-native state.

N1 does not need to know:

```text
Godot RID
Jolt body ID
PhysX actor pointer
broadphase node
solver manifold
```

If P0 follows N1, the first N1 implementation naturally replicates canonical
`WorldPosition`/public physical state exactly as T0 already does.

When client prediction is later introduced, Network may negotiate prediction
compatibility but authority still remains with the server provider.

Physics P0 must not force N1 to retransmit static collision geometry that the
client already obtains through accepted world/chunk content mechanisms.

## Persistence and restore

Save data is canonical Game/world data.

Provider state is rebuilt after load:

```text
load canonical world
        |
        v
create physics generation/world
        |
        v
register/rebuild effective physical bodies/projection
        |
        v
continue authoritative simulation
```

Permanent tests should prove that restore does not depend on stale provider handles
or hidden solver state.

A world saved under Simple Physics should be loadable under another provider when
the game's declared physics behavior profile permits it.

A game that genuinely depends on a richer provider may declare that requirement
in game/host compatibility metadata rather than embedding native engine objects in
the save.

## Determinism and authoritative time

UltraSim Simple Physics should target deterministic integer/fixed-point semantics
where practical and preserve the existing authoritative time rule:

```text
Tick is identity.
Delta is time.
Frequency determines Delta.
```

The same semantic motion over the same elapsed authoritative time must not change
merely because authority runs at 20, 60, or 120 Hz unless the selected physics
profile explicitly declares a cadence-sensitive integration model.

External engines may use floating-point/native solvers. Their provider adapters
must still satisfy the accepted Game-semantic conformance tests for the profile
they claim to implement.

Do not pretend that two complex floating-point solvers are bit-identical if they
are not. Prediction compatibility and server authority exist precisely because
backend internals may differ.

## Boundedness and allocation

UltraSim Simple Physics must preserve the project's bounded-memory discipline.

Expected hot-path properties:

- world collision query capacity is explicit;
- candidate arrays/scratch are preallocated or bounded;
- overflow fails closed or follows an explicit fallback before mutation;
- warmed ordinary motion does not allocate;
- no per-contact heap graph;
- no unbounded callback/event accumulation;
- active physics state has measurable reservation/capacity.

External providers must have explicit host-side limits for bodies, contacts,
queries, and temporary buffers where their APIs permit them.

A provider that can silently allocate without useful bounds should not be claimed
as equivalent production evidence merely because it passes functional tests.

## Performance evidence

P0 benchmarks should separate:

```text
provider motion-resolution cost
world collision-query cost
Game locomotion + provider end-to-end step
provider synchronization/rebuild cost
```

Required profiles should include at least:

```text
flat unobstructed movement
dense static blockers
door/chest mutable blockers
chunk-boundary motion
blocked repeated Direct
Destination blocked by wall
step/clearance cases once implemented
representative actor counts
```

For UltraSim Simple, report:

- warmed mean/p95;
- timed allocation;
- fixed backing/scratch reservation;
- candidate-query pressure;
- maximum tested actor/collider density.

Godot/Jolt canary results should be reported separately rather than used to hide
Simple-provider regressions.

## P0 milestone sequence

### P0.0 — freeze the seam from current T0 behavior

Before adding richer behavior:

- extract the current physical resolution responsibility from
  `GameWorldActions`;
- define the minimum generation-owned physics capability;
- preserve existing terrain/wall/door/chest collision semantics;
- preserve authoritative Direct/Destination behavior;
- preserve relocation cancellation and save/restore behavior;
- add provider-neutral conformance fixtures;
- prove no Godot/Jolt/native type reaches Game/Core contracts.

**Gate:** T0 canonical gameplay remains unchanged using UltraSim Simple through the
new seam.

### P0.1a — first-class occupancy and authoritative relocation

Add only:

- a first-class bounded occupancy query sharing motion endpoint legality;
- explicit clear/blocked/outside-prepared-world results;
- occupancy-aware authoritative relocation;
- transactional failure that preserves position and retained locomotion.

**Gate:** clear, static-blocked, dynamic-blocked, unprepared, and relocation
lineage scenarios pass with zero warmed occupancy allocation.

### P0.1b — swept movement and corner behavior

Earn continuous movement semantics separately:

- thin-blocker/high-speed sweep evidence;
- no diagonal corner cutting;
- bounded deterministic candidate work;
- preserved Direct/Destination Game policy.

### P0.1c — support, step, and clearance — CLOSED

The T1.1 Sky Island/cave consumer now earns this checkpoint. Add only the bounded
physical foundation needed before vertical motion:

- explicit support versus blocking;
- body height/footprint profile;
- support/clearance selection from actual structural geometry and body position;
- bounded step-up if required by the accepted terrain grammar;
- grounded step-down rule if required;
- low-ceiling clearance.

World may return BaseTerrain plus several T1.1 Top/Bottom structural faces within
one X/Z column or body-height range. Physics must select by actual sampled Y,
body position, and geometry—not BaseTerrain-only height, `WorldSurfaceId`, fragment
order, or any authoring layer rank. The same two-sided shell may act as support
from above and clearance/blocking geometry from below. FrozenStatics, Multis, and
Game-owned dynamic collision projection remain separately owned structural
candidates rather than being reclassified as terrain layers.

Terrain T1.0 supplies the distinction between Present BaseTerrain, intentional
resident Absent surface, and Unavailable prepared data. P0.1c must treat Absent as
unsupported topology while preserving fail-closed handling for Unavailable.
T1.1's `TryCopyTerrainColumnSurfaces`, `TryFindHighestSupportAtOrBelow`, and
`TryFindLowestCeilingAtOrAbove` are bounded neutral World reads; they do not
pre-decide grounding, clearance, step response, or motion.

P0.1c does not add gravity, jump, falling, or landing. Those remain the following
vertical-motion checkpoint.

**Gate:** conformance scenarios pass at accepted 20/60/120 Hz semantics with
bounded zero-allocation warmed Simple hot paths.

### P0.1d — vertical motion, jump, fall, and landing — CLOSED

Built on the accepted support result rather than inventing a second ground oracle.
The accepted implementation adds explicit grounded-to-airborne transition, bounded
authoritative vertical integration, gravity, landing/support acquisition, and
Game-owned recovery while preserving intentional void and actual multi-surface
selection. It does not broaden into actor collision, navigation, rigid bodies, or
an alternate-provider campaign.

**Gate:** terrain Top/Bottom contacts, jump admission, hostile double-jump rejection,
horizontal air control, void fall/recovery, generation-local restore, ECS slot-reuse
fencing, 20/60/120 Hz final convergence, and zero warmed airborne allocation pass.

### P0.2 — Godot provider canary

Implement only the P0 contract needed by the conformance suite through a Godot
server/frontend composition.

**Gate:** Game remains engine-neutral and canonical P0 invariants hold.

### P0.3 — Jolt provider canary

Implement the same P0 contract with Jolt, initially through Godot if that is the
lowest-cost real host path.

**Gate:** provider replacement is real; no Jolt-specific widening of the Game
contract is required for P0 semantics.

### P0.4 — compare and freeze

Benchmark/reference:

```text
UltraSim Simple
Godot Physics
Jolt
```

on the same semantic fixtures.

Freeze:

- the first provider seam;
- Simple Physics as the default baseline;
- conformance semantics;
- provider lifetime/restore rules;
- the exact boundary between locomotion, physics, and post-motion Game effects.

Do not continue into generalized rigid bodies without a concrete game requirement.

## Explicit non-goals for P0

Do not build:

- a universal `IPhysicsEngine` mirroring every commercial engine API;
- a rigid-body ECS framework;
- joints/constraints;
- ragdolls;
- vehicles;
- cloth/fluid simulation;
- destructible rigid-body environments;
- arbitrary torque/angular momentum;
- full platforming/jumping solely to prove replaceability;
- a generic event bus for contact callbacks;
- physics-owned Game entities;
- Godot/Jolt/PhysX objects in persistence or wire messages;
- client-authoritative collision;
- camera-dependent physics;
- navigation/pathfinding inside the physics provider;
- gameplay permission/region logic inside collision;
- automatic adoption of every RunUO tile/movement special case.

## Future capability growth

A richer game may later earn additional capabilities such as:

```text
falling / vertical velocity
jumping
surface sliding
knockback
dynamic body contacts
moving platforms
physics props
combat impulses
vehicles
ragdolls
```

Each addition should answer:

1. Is this physical behavior actually required by a concrete game/domain?
2. What canonical Game truth must exist independent of the backend?
3. Can the current P0 provider contract express it cleanly?
4. Does Simple Physics need to implement it, or may a game require a richer
   behavior profile/provider?
5. What prediction compatibility does it require?
6. What persistence/rebuild semantics are authoritative?
7. What boundedness/performance evidence is required?

Extend the seam only after those questions have concrete answers.

## Acceptance definition

Physics P0 is accepted when:

```text
Game locomotion no longer contains playable-world-specific collision resolution.

UltraSim Simple Physics reproduces and improves the accepted T0 physical behavior
through a generation-owned injected capability.

World support/blocking/occupancy semantics are explicit and bounded.

The same Game locomotion code runs against at least one materially external
provider canary without engine-type leakage.

Provider transient state is rebuildable from canonical Game truth.

Authoritative results remain server-owned whether the server is headless, hosted
by a Godot frontend, or embedded inside a client process.

No unearned rigid-body/general-physics framework has been introduced.
```

At that point UltraSim has a real physics substrate: simple enough for its default
simulation games, replaceable enough for a game that genuinely needs Jolt,
Godot Physics, PhysX, or a specialized platforming/combat solver, and still
consistent with the authority, persistence, Network, and engine-portability
boundaries established by T0 and N1.
