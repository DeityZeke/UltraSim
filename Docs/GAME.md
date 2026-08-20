# Game composition

Game code is concrete composition over reusable kernels. UltraSim does not add a
generic framework merely because two game domains need coordination.

## Composition root

`Game/Composition` owns the closed generated game schema, compiled content, and
`GameGenerationState`. The generation state currently combines Accounting,
Inventory's derived acceleration, compiled content, and game-specific simulation
state beneath the Runtime generation root.

Component IDs are assigned by the one closed generated schema. Individual game
domains contribute component types; they do not own local numeric ID ranges or
standalone schemas that cannot compose.

Compiled content owns concrete prefab identities and policy such as Bread,
Apple, Iron Sword, Backpack, and World Chest. Inventory reasons about facts—item,
quantity, stack policy, equipment capability, container, and relationships—not
hard-coded item-kind switches.

## Accounting and Admission

Accounting owns durable accounts, characters, and trusted external-identity
bindings. Admission resolves trusted identity, verifies current account state,
presents character choice, reauthorizes on action, and enters the selected
durable character.

Sessions may retain presentation and durable identity, not generation-local
authority. Publication rebinds entered account-backed sessions to the new
generation or invalidates them. Accountless ephemeral sessions explicitly rejoin
after replacement.

## Inventory

Authoritative Inventory truth is ECS data. Container capacity is separate from
optional ownership. Item behavior comes from compiled prefab component policy.

`InventoryIndex` is a rebuildable accelerator with a bounded global packed child
pool; gameplay capacity does not reserve one managed list or every theoretical
child slot per container. Location/node details remain internal physical state.
Rebuild prepares and validates replacement maps before publication. Property and
fuzz tests protect donor-link repair.

UI projection is separate again. Split produces semantic row update/add deltas;
equip produces removal. Retained view identity uses `PersistentEntityKey`
(world + entity), preventing cross-world numeric-ID ABA after generation
replacement.

## Persistence ownership

`GameRuntimePersistence` is the forward whole-game path. Inventory contributes
rebuild/validation but does not own Accounting, Runtime publication, or a private
persistence artifact. The authoritative save set currently contains ECS,
Accounting, and Clock; Inventory is reconstructed from ECS truth.

Narrow historical integration helpers are not production composition roots.

## Replication projection

`Game/Replication` owns the canonical client-semantic projection introduced in
Network N0.1. It observes a complete committed generation through exact changed
rows and lifecycle cursors, publishes connection-independent
`ReplicationEntityId` slots within a `StateEpoch`, binds current Admission
participants, and retains projection lifecycle separately from explicit
application events.

The network-enabled composition profile alone reserves lifecycle history,
structural merge capacity, prepared observation, and bounded authoritative
ingress. The normal local profile remains free of this cost. `OfflineDirect` is
the packet-free semantic oracle; it receives no ECS entities or mutable world
authority and atomically applies only complete baselines/frames.

See [Game replication](../Game/Replication/README.md) for the current executable
contract and [Network V3](Plans/NETWORK.md) for the remaining protocol work.

## World position

Terrain T0.0 adds the game-owned [`WorldPosition`](../Game/World/README.md) value
over the dependency-neutral [Spatial contracts](../Kernels/Spatial/README.md).
The game profile uses signed 32-bit 16 m chunk X/Z, canonical `0..15,999` local
millimeters, signed 32-bit Y, 500 mm terrain cells, and a separate 100 mm authoring
snap. Dynamic position remains free on the 1 mm lattice.

This checkpoint contains no world store, ECS position component, terrain pack,
publication, or renderer. T0.1 introduces the persisted component wrapper and
maintained game-owned `WorldSpatialIndex` only after the T0.0 stop review.

## Simulation rules

Scheduled work selected by component topology uses generated chunk systems.
Bounded preselected operations use `RangeJob` plus declared lookup capabilities.
Structural mutation crosses the scheduler barrier.

Per-time behavior is proved at multiple fixed cadences. Per-step behavior is
explicit. Current S0 game logic is an integration substrate—not the final MMO
simulation, terrain, physics, networking, or content/modding product.
