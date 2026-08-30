# UltraSim Simple Physics

`Game/Physics` owns the concrete authoritative physical resolver earned by Physics P0.0-P0.1b.
It is deliberately not a general engine or provider framework.

The current capability has adjacent motion and occupancy requests over one shared physical legality
implementation:

```text
SimpleMotionRequest
    current WorldPosition
    desired global X/Z endpoint
    fixed upright body extents
        ↓
UltraSimSimplePhysics
    target prepared-terrain resolution
    complete bounded swept broadphase
    exact integer/rational swept-AABB blocker test
        ↓
SimpleMotionResult
    Resolved / Blocked / OutsidePreparedWorld

SimpleOccupancyRequest
    target WorldPosition
    fixed upright body extents
        ↓
same terrain/static/projected-dynamic blocker legality at one endpoint
        ↓
SimpleOccupancyResult
    Clear / Blocked / OutsidePreparedWorld
```

The resolver owns no ECS entity, prefab identity, door state, input/control meaning,
locomotion retention, authoritative position write, publication, Network state, or native engine
object. `Game/Composition` projects current cabin-door/chest meaning into effective physical bounds
before invoking it. The Physics project therefore depends on `Game/World`, not on playable Game
composition.

P0.0 preserves the accepted T0.10 motion behavior exactly, while P0.1a makes occupancy a
first-class query and makes authoritative relocation its first Game consumer. P0.1b changes
motion from endpoint-only collision to a complete linear blocker sweep:

- an upright 300 mm X/Z-radius, 1,800 mm-high playable body;
- terrain-following Direct and Destination endpoints;
- complete-or-fail prepared static queries with 16 candidate chunks and 64 hit rows;
- bounded effective dynamic bounds supplied by Game;
- no warmed allocation;
- first-class all-or-nothing endpoint occupancy;
- blocked/unprepared relocation leaves canonical position and retained locomotion unchanged;
- successful clear relocation commits the terrain-resolved position and retires locomotion;
- thin static and projected-dynamic blockers cannot be crossed between clear endpoints;
- diagonal corner intersections block in either direction;
- exact half-open boundary contact remains non-overlap;
- broadphase candidates are filtered by exact swept geometry rather than becoming false hits;
- sweep candidate/hit pressure remains bounded and fails closed.

The sweep is all-or-nothing: a blocked request realizes no partial displacement. It resolves terrain
at the target but is not continuous terrain-support, step, or grounding logic. There is no sliding,
depenetration, gravity, actor collision, navigation, Godot Physics, Jolt, or generalized provider
interface. Those remain later checkpoints in [`PHYSICS.md`](../../Docs/Plans/PHYSICS.md).
