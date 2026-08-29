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

Container content also selects a stable Inventory rule profile. The server may
retain those per-container selections or force one uniform profile. Effective
item-count capacity is validated against content policy and generation limits;
placement, weight, and nesting have separate typed dimensions rather than
overloading the legacy capacity integer.

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
cross-container move produces a removal from the current view; and owner-private
equip/unequip updates the retained item row between contained and equipped state.
`OpenOwned` proves the source and explicit transfer destinations share the
Admission-authorized owner, then retains and revalidates complete
`PersistentEntityKey` values (world + entity) for every action and generation
replacement. This prevents foreign-container disclosure and cross-world numeric-ID
ABA. The unrestricted `Open` route remains trusted composition for ownerless
world containers, not remote participant authorization.

The [Inventory subsystem contract](../Game/Inventory/README.md) records the I0.8
authenticated remote-to-Godot vertical and the I0.9 rule/placement foundation.
I0.9 keeps the current unplaced/list presentation operational while future
grid/slot/freeform, weight, and nesting semantics remain explicit fail-closed
contracts rather than falsely implemented behavior.

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

Every Spatial-enabled composition profile now reserves lifecycle history; the
Network profile additionally reserves structural merge capacity, prepared
replication observation, and bounded authoritative ingress. `OfflineDirect` is
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

T0.1 adds a flattened, persisted `WorldPositionComponent` and a bounded
generation-owned `WorldSpatialIndex`. Exact ECS changed-row and lifecycle cursors
are consumed after the complete ECS graph/structural barrier and before clock
advancement. Same-home motion retains its membership row; chunk crossings relink
that row; lifecycle overrun rebuilds privately from current ECS truth. Terrain
packs, public chunk projection, and rendering remain absent from that checkpoint.

T0.6 adds the separate persisted `WorldPublicComponent` eligibility contract and
generation-owned `ChunkPublicationStore`. `GameWorldState` consumes Position,
public-component, and lifecycle changes at the same post-step barrier, then
publishes final compact SoA rows with separate public identities/entity versions,
chunk revisions, and bounded observer-independent dirty history. Snapshot/delta
and borrowed-page reads are game-world semantics; no Network/session state or
watcher list lives in the store.

T0.4 binds an optional immutable `WorldPack` to the same concrete game space and
content meaning before generation publication. Pure pack queries describe that
artifact; authoritative gameplay queries route through generation-owned
`GameWorldState`. T0.9 fills that seam with a concrete `WorldPreparedView`: the
exact bound pack plus one immutable bounded durable overlay of complete changed-
chunk terrain/static/Multi replacements. Gameplay still asks `GameWorldState`;
no generic terrain/provider registry was introduced. Client semantic staging and
engine-neutral terrain mesh preparation consume the same prepared view. A fresh
mutation-free overlay remains revision 0, canonical no-ops do not advance content
lineage, and ordinary overlay identity validation uses cold bound-pack indexes
rather than rescanning the immutable world.

## Simulation rules

Scheduled work selected by component topology uses generated chunk systems.
Bounded preselected operations use `RangeJob` plus declared lookup capabilities.
Structural mutation crosses the scheduler barrier.

Per-time behavior is proved at multiple fixed cadences. Per-step behavior is
explicit. Current S0 game logic is an integration substrate—not the final MMO
simulation, terrain, physics, networking, or content/modding product.

Simulation activity is a separate game-domain concern from authoritative
existence, Spatial membership, replication interest, structural freshness, and
client presentation. Combat, NPC schedules, crops, ecology, and economy need not
share one fidelity vocabulary or reduced representation. Each concrete domain
must define what reduced/dormant operation means and how it reconciles elapsed
authoritative world time before it earns an implementation. Arbitrary scheduler
tick skipping is not a simulation model.

Physics P0.0 extracts the accepted T0.10 endpoint collision from `GameWorldActions` into the
generation-owned concrete [`UltraSimSimplePhysics`](../Game/Physics/README.md) capability.
Locomotion still owns Direct/Destination interpretation and Game still commits `WorldPosition`.
The playable composition separately projects cabin-door/chest state into effective blocking bounds,
so Simple Physics depends on prepared World truth rather than gameplay prefab/state meaning. P0.0
adds no sweep, support/step, relocation occupancy, native provider, or general physics API.

Temporal work likewise belongs to its domain. The authoritative facts may name
an effective/due world time, while a reconstructible index identifies what is
ready for service. Processing time, semantic due time, expiry, and preferred
service cadence are not assumed to be the same. A recurring domain explicitly
chooses whether its next deadline anchors to the prior due time or to current
time so delayed execution cannot introduce accidental drift.

Game semantics keep these categories distinct:

```text
state       current coalescible truth
change      bounded notice to reread current truth
fact        an occurrence that cannot be inferred from later state
command     explicit authority-directed intent/control flow
notice      consumer-facing presentation derived from state or facts
```

Fact meaning does not by itself select retention. Some owner-scoped facts need
only bounded reliable delivery; selected provenance may be durable because
gameplay depends on it. Commands never become an implicit global event bus, and
events are not the primary control path. Concrete construction, crafting,
employment, or resource-claim domains must exist before any shared project or
reservation primitive is extracted.
