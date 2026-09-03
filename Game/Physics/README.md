# UltraSim Simple Physics

`Game/Physics` owns the concrete authoritative physical resolver earned by Physics P0.0-P0.1c.
It is deliberately not a general engine or provider framework.

The current capability has adjacent motion and occupancy requests over one shared physical legality
implementation:

```text
SimpleMotionRequest
    current WorldPosition
    desired global X/Z endpoint
    upright body extents + explicit step-up/down limits
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
    upright body extents + explicit step-up/down limits
        ↓
nearest actual support + same footprint/clearance/static/dynamic legality
        ↓
SimpleOccupancyResult
    Clear+Support / Unsupported / Blocked / OutsidePreparedWorld
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

- an upright 300 mm X/Z-radius, 1,800 mm-high playable body with explicit 500 mm step-up/down;
- support selection uses actual sampled height, never `WorldSurfaceId`, fragment order, or an
  authoring layer rank;
- occupancy chooses the support physically nearest its requested Y, with lower support winning an
  exact distance tie;
- motion chooses the highest support reachable from the previous grounded Y;
- center plus four footprint corners must each have bounded reachable support;
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

The resolver remains all-or-nothing: a blocked or unsupported request realizes no partial
displacement. P0.1c's fixed spatial samples are a bounded grounded traversal contract, not a
general triangle time-of-impact solver or airborne character controller. FrozenStatics/Multis and
Game-projected dynamic bounds remain blockers; this checkpoint does not newly make them support
surfaces. There is no sliding, depenetration, persistent grounded ECS component, gravity, jump,
falling, landing, actor collision, navigation, Godot Physics, Jolt, or generalized provider
interface. Those remain later checkpoints in [`PHYSICS.md`](../../Docs/Plans/PHYSICS.md).
