# UltraSim Simple Physics

`Game/Physics` owns the concrete authoritative physical resolver earned by Physics P0.0-P0.1d.
It is deliberately not a general engine or provider framework.

The current capability has adjacent motion and occupancy requests over one shared physical legality
implementation:

```text
SimpleMotionRequest
    current WorldPosition
    desired global X/Z endpoint
    upright collision extents + central support/feet extents
    + explicit step-up/down limits
        ↓
UltraSimSimplePhysics
    bounded grounded path sampling
    actual BaseTerrain/terrain-shell support selection
    five-point footprint support + Bottom-face clearance
    complete bounded swept broadphase
    exact integer/rational swept-AABB blocker test
        ↓
SimpleMotionResult
    Resolved+Support / Unsupported / Blocked / OutsidePreparedWorld

SimpleOccupancyRequest
    requested X/Y/Z (Y is a support-selection hint)
    upright collision extents + central support/feet extents
    + explicit step-up/down limits
        ↓
nearest actual support + same footprint/clearance/static/dynamic legality
        ↓
SimpleOccupancyResult
    Clear+Support / Unsupported / Blocked / OutsidePreparedWorld

SimpleAirborneMotionRequest
    current WorldPosition
    desired global X/Y/Z endpoint
    upright body extents
        ↓
bounded structural-footprint sampling without ground snapping
Top/Base landing + Bottom-face ceiling contact
same exact swept static/projected-dynamic blocker test
        ↓
SimpleAirborneMotionResult
    Airborne / Landed+Support / CeilingBlocked+Contact /
    Blocked / OutsidePreparedWorld
```

The resolver owns no ECS entity, prefab identity, door state, input/control meaning,
locomotion retention, authoritative position write, publication, Network state, or native engine
object. `Game/Composition` projects current cabin-door/chest meaning into effective physical bounds
before invoking it. The Physics project therefore depends on `Game/World`, not on playable Game
composition.

P0.0 preserves the accepted T0.10 motion behavior exactly, P0.1a makes occupancy a first-class
query and authoritative relocation its first Game consumer, and P0.1b changes endpoint-only
collision to a complete linear blocker sweep. P0.1c adds bounded grounded terrain support,
clearance, and steps:

- an upright 300 mm X/Z collision radius, independent 150 mm X/Z support radius,
  1,800 mm height, and explicit 500 mm step-up/down;
- support selection uses actual sampled height, never `WorldSurfaceId`, fragment order, or an
  authoring layer rank;
- occupancy chooses the support physically nearest its requested Y, with lower support winning an
  exact distance tie;
- motion chooses the highest support reachable from the previous grounded Y;
- center plus four support-footprint corners must each have bounded reachable support;
- body-footprint samples remain authoritative for blockers, ceiling clearance, structural
  residency, and grounded elevation changes across a step face;
- Bottom faces provide low-ceiling clearance, while a shell's own Bottom does not prevent standing
  on its selected Top;
- Present BaseTerrain and additional Top faces support; intentional void is `Unsupported`;
- missing prepared structure is `OutsidePreparedWorld`; incomplete bounded queries are `Blocked`;
- grounded paths sample at no more than 250 mm spacing and fail closed beyond 512 samples;
- complete-or-fail prepared static queries with 16 candidate chunks and 64 hit rows;
- complete column scratch admits the default one BaseTerrain plus Top/Bottom for 64 fragments
  (129 faces) and fails closed if a wider custom pack exceeds that Simple Physics bound;
- bounded effective dynamic bounds supplied by Game;
- no warmed allocation;
- first-class all-or-nothing endpoint occupancy;
- blocked/unsupported/unprepared relocation leaves canonical position and retained locomotion
  unchanged;
- successful clear relocation commits the terrain-resolved position and retires locomotion;
- thin static and projected-dynamic blockers cannot be crossed between clear endpoints;
- diagonal corner intersections block in either direction;
- exact half-open boundary contact remains non-overlap;
- broadphase candidates are filtered by exact swept geometry rather than becoming false hits;
- sweep candidate/hit pressure remains bounded and fails closed.

P0.1d adds the structural-contact primitive consumed by Game-owned vertical motion. It samples at the
same no-more-than-250 mm spacing and 512-sample ceiling, keeps intentional resident void as clear
unsupported air, lands descending bodies on the first complete support-footprint Top/Base/static
face,
and stops ascending bodies at the first Bottom face reached by any footprint point. Structural data
must be completely resident for all five footprint columns or resolution fails closed. Airborne
requests do not snap to a nearby support merely because one exists.

The resolver remains all-or-nothing: a blocked or unsupported request realizes no partial
displacement. Fixed spatial samples are bounded Simple Physics traversal contracts, not a general
triangle time-of-impact solver or rigid-body character controller. Ordinary collidable
FrozenStatics/Multis support on top unless authored with the rare `NoTopSupport` override;
Game-projected dynamic bounds remain blockers rather than landing support. The resolver owns no
gravity, jump admission, vertical state, recovery policy, sliding, depenetration, actor collision,
navigation, Godot Physics, Jolt, or generalized provider interface. Game composition owns the
accepted P0.1d grounded/airborne lifecycle above this physical seam; see
[`PHYSICS.md`](../../Docs/Plans/PHYSICS.md).

## D0 playability hardening — ordinary static top support

Human play exposed an inconsistency after P0.1d: a collidable rock stopped downward motion but its
top could not become support, leaving the actor airborne and unable to start grounded traversal.
Simple Physics now considers the top of complete collidable `FrozenStatic`/Multi bounds alongside
terrain support. A `SimplePhysicsBody` separates its 600×600 mm collision/clearance footprint from
its 300×300 mm central support/feet footprint. A selected top must cover that support footprint, be
within the same bounded step/landing interval, satisfy full-body structural clearance, and pass the
existing exact swept blocker test. A grounded elevation change still requires full body-footprint
coverage so the controller cannot snap through the vertical face of a narrow step.

`Collidable` is the common-case compact physical contract: it blocks the body and supports on its
top. `NoTopSupport` is a rare fail-closed override for a solid top that must not establish standing
or landing; it is invalid without `Collidable` and does not weaken body blocking. This keeps ordinary
floors, roofs, crates, walls, shelves, rocks, broad logs, and planks geometry-driven without growing
a gameplay-object taxonomy in Physics.

`SimplePhysicalSupport` names whether a contact came from terrain or static geometry. Terrain
contacts retain their `WorldSurfaceId`/face; static bounds do not acquire false terrain identity.
This does not add moving-platform attachment, inherited velocity, arbitrary triangle support,
dynamic projected support, sliding/contact response, or actor-on-actor support. A tightrope or
similarly exceptional traversal remains a future Game-owned traversal mode with an explicitly
selected body/profile; the ordinary body is not made unrealistically narrow and Physics never learns
what a tightrope prefab is.

Permanent integrated regressions land and move on the authored Concept Demo rock, Cabin wall, and
Cabin shelf; walk off the wall into unsupported motion; reject a top smaller than the support core;
and prove `NoTopSupport` stays blocking without grounding. The warmed ordinary static-support path
continues to allocate 0 B.
