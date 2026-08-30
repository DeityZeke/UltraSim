# UltraSim Simple Physics

`Game/Physics` owns the concrete authoritative physical resolver earned by Physics P0.0-P0.1a.
It is deliberately not a general engine or provider framework.

The current capability has adjacent motion and occupancy requests over one shared endpoint legality
implementation:

```text
SimpleMotionRequest
    current WorldPosition
    desired global X/Z endpoint
    fixed upright body extents
        ↓
UltraSimSimplePhysics
    prepared terrain height
    complete bounded static/Multi overlap query
    already-resolved dynamic blocking bounds
        ↓
SimpleMotionResult
    Resolved / Blocked / OutsidePreparedWorld

SimpleOccupancyRequest
    target WorldPosition
    fixed upright body extents
        ↓
same terrain/static/projected-dynamic legality
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
first-class query and makes authoritative relocation its first Game consumer:

- an upright 300 mm X/Z-radius, 1,800 mm-high playable body;
- terrain-following Direct and Destination endpoints;
- complete-or-fail prepared static queries with 16 candidate chunks and 64 hit rows;
- bounded effective dynamic bounds supplied by Game;
- no warmed allocation;
- all-or-nothing endpoint occupancy only;
- blocked/unprepared relocation leaves canonical position and retained locomotion unchanged;
- successful clear relocation commits the terrain-resolved position and retires locomotion.

There is no sweep, tunneling guarantee, step/support model, sliding, depenetration, gravity,
actor collision, navigation, Godot Physics, Jolt, or generalized provider
interface. Those remain later checkpoints in [`PHYSICS.md`](../../Docs/Plans/PHYSICS.md).
