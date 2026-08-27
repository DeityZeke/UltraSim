# UltraSim

UltraSim is a bounded, deterministic simulation and game-runtime foundation for
very large persistent worlds. The repository contains the accepted ECS 3.4.2
kernel, authoritative Runtime/hosts, transactional persistence, neutral UI
protocol and Godot renderer, concrete game composition, Accounting, Admission,
Inventory, the accepted Terrain/World T0 vertical, and the completed hosted
remote-authority foundation.

The original Network N0 path is frozen and retained as the explicit
`MaintainedEntityRelation` Game replication profile. It includes secure
ProtocolLoopback and UDP, authenticated application binding, controlled
input/owner state, observer WorldState, retained Events, atomic baseline recovery,
generation replacement, shared-listener/native socket batching, long-lived
WorldState identity-ledger compaction, and accepted dense/sparse evidence. Its
final exact-AOI experiment removed substantial relation/record/wire work but
worsened server-role p95 because continuously maintaining precise moving
recipient/entity topology cost more. That result closed open-ended N0 tuning and
helped motivate the chunk-native world/replication investigation rather than
another packet/codec optimization cycle.

Terrain / World T0 is accepted/frozen. It provides the chunk-native world
substrate originally intended for terrain, statics, streaming, and AOI:
deterministic chunk-relative coordinates, packed terrain and UO-like statics,
logical Multi structure composition, a separate future 3D region/property seam,
derived dynamic ECS membership, one shared revisioned dynamic-public publication
page per chunk, bounded dirty history, local snapshot/delta streaming, and
efficient Godot terrain/static rendering. The acceptance vertical is a launchable
woods/cabin world rather than another isolated kernel demo.

Network N1.0-N1.4 and Hosted H0.0-H0.5 are accepted. The complete five-profile
comparison program through P0.4 is also closed: `ChunkPublication` remains the
selected **current-game dynamic-public** profile;
`MaintainedEntityRelation` and `DirtyEntitySpatialFanout` are retained
alternatives; `DirtyEntityChunkBatchFanout` and `DirtyChunkSpatialFanout` are
retained experimental/reference controls. The catalog covers dynamic public state
only—terrain/base and frozen structural/static content remain WorldPack/static
ownership. Runtime cross-profile ownership transfer is unsupported until a real
gameplay feature earns a stable cross-profile Game public identity, ownership
lineage, client ordering/deduplication, and transactional handoff. N1.5 shared
packing, another profile tournament, public-WAN readiness, and production-CCU
claims remain deliberately stopped.

The first real remote public-actor vertical now consumes the selected stack from
ECS authority through `ChunkPublication`, secure Network, production client
semantic cache, and Godot presentation. It covers movement, leave/re-entry,
reconnect, destruction, and save/load generation replacement and permanently
regresses the canonical remote-removal decoder rule. The I0.8 Inventory vertical
also proves a different owner-private UI path through the same lower Network
machinery without entering public AOI.

## Design

> Build the smallest predictable machine that can move a very large simulation
> forward efficiently.

```text
AUTHORING / MODDING
GAME COMPOSITION
RUNTIME / BRIDGES / ORCHESTRATION
KERNELS
```

Identity is a value. Components are data. Storage is layout-optimized. Traversal
is contiguous. Mutation crosses a barrier. Barrier work is batchable. Runtime
memory is bounded. Normal use cannot corrupt invariants.

One `ServerRuntime` owns one coherent active `ServerGeneration`: ECS world,
authoritative clock, and game state. Clients and hosts do not share mutable world
authority. Bridges translate, route, and synchronize.

Authoritative time follows:

> Tick is identity. Delta is time. Frequency determines Delta.

Fixed 60 Hz, 1x playback, with cadence adaptation disabled is the historical
uncompromised Runtime performance baseline. The engine proves semantic cadence
independence across multiple fixed rates and live rate changes. Network correctness
must not assume one Network pump per authority tick; authority and delivery cadence
are independently configurable host/application policy. Current Network stress
tuning uses a roughly 16 ms server-step envelope because it is the harder case,
while slower production cadence deliberately trades freshness for more deadline
headroom and fewer ticks per second.

## Current project shape

- `Kernels/ECS*` — generated schema/query APIs, packed ECS, scheduler,
  structural/lifecycle/content/persistence mechanisms.
- `Kernels/Network*` — dependency-clean identity/lane contracts, fixed connection
  and packet ownership, generated codecs/manifests, security, reliability,
  deterministic impairment, connected/shared UDP, externally admitted Hosted
  lifecycle, and bounded connection-owner handoff.
- `Kernels/Spatial` — dependency-neutral integer spatial identity, geometry, and
  canonical math; no runtime world storage or kernel coupling.
- `Kernels/UI` — neutral definitions, models, deltas, actions, semantic port,
  and bounded transport-independent semantic wire codec.
- `Server/*` — authority lifecycle, hosts, and durable save-set coordination.
- `Client/*` — retained client projection, remote network bridge, and Godot
  presentation.
- `Game/*` — concrete game schema/content/state, authoritative workflows,
  canonical replication projection, persisted world-position authority,
  generation-owned dynamic spatial membership, and game-owned network binding.
- `_Tests`, `_Benchmarks`, `_Tools` — executable correctness, evidence, and gates.
- `_Archive/_V2` — frozen historical/reference source used only by explicit
  comparisons. Other archived generations and reference material live beside it
  under `_Archive` and are excluded from active Godot project discovery.

Terrain T0.0 accepted the tiny Spatial contracts seam and concrete game position
profile. T0.1 accepted the persisted component wrapper and first game-owned,
generation-coherent maintained spatial index after full-capacity migration and
reference-model hardening, without creating a generic world storage kernel.
T0.2 accepted the compact dense base-terrain contract using a per-chunk int32
base plus unsigned q5 offsets, and T0.3 accepted the canonical sectioned
immutable WorldPack/source. T0.4 accepted exact integer base queries and the
bounded deterministic base-mesh payload. T0.5 accepted optional packed
frozen-static and static-Multi sections, stable logical identities, deterministic
render groups, and bounded exact static-bounds queries. T0.6 accepted the
explicit dynamic-public component, generation-owned packed chunk publication,
bounded dirty history, canonical snapshot/delta reads, and direct committed SoA
views. T0.7 implements exact hysteretic chunk-interest rings and a bounded
packet-free local snapshot/delta streaming oracle and is accepted/frozen after
consumer hardening. T0.8 implements the one bounded production client semantic
world cache plus an independently budgeted Godot terrain/grouped-instance
renderer. Its hardening separates immutable structural builds from dynamic public
refresh, maps semantic handles onto independent private render slots, validates
snapshot lineage canonically, and uses integer-relative vertical anchors before
float conversion. Sliding-window hardening makes same-chunk X/Z movement common-
root-only, coalesces rapid/reversed transitions toward the latest desired center,
and provides bounded retire/reuse reset for fast travel. A portability pass moved
desired-window candidate/diff/reset policy into a transactional engine-neutral
Client Core planner; the Godot class is its native admission bridge. T0.8 is
accepted/frozen. T0.9 implements the exact immutable pack plus bounded complete
terrain/static/Multi overlay view, per-chunk structural revision/hash identity,
and the fourth `worldview` save-set participant. Prepared client meshes honor
overlays; empty/no-op overlay lineage is save/restore-closed; and ordinary identity
validation uses cold bound-pack indexes rather than rescanning the immutable world.
T0.10 is accepted: retained Direct/Destination authority and cadence semantics are
closed, and `Client/Playable.Godot` provides the first real OfflineDirect woods/
cabin application. Human acceptance and the broad performance matrix closed T0.

Network N1.0-N1.4 layer bounded recipient chunk subscriptions, canonical chunk
messages/proofs, MTU-admissible independent-unreliable delta/absence delivery,
private atomic Bulk snapshot recovery, and dirty-driven scheduling onto the
accepted world/publication/client-cache contracts. Hosted H0.0-H0.5 prove external
cookie admission, pinned secure promotion, authenticated IPv4 path rebinding,
exclusive owner-thread packet handoff, and dedicated-process composition.
P0.3H/P0.3I close lower ready-selection/proof-envelope multiplicative costs;
P0.3J separates the strict <=4 ms server-role rating from larger stress envelopes;
P0.3K records independent 16/33 ms server-step and AOI cadence controls. At matched
population, 33 ms does not make work/tick cheaper—it supplies roughly twice the
deadline and about half the ticks/second, reducing per-second wire/work rate while
increasing freshness intervals. P0.4 then closes replication selection
conservatively rather than inventing per-object profile switching.

The native T0.10 remote-actor acceptance composes one real ECS woodland actor
through the selected dirty-work scheduler, canonical secure chunk protocol,
production client semantic cache, and Godot renderer. Spawn, movement, interest
leave/re-entry, destruction, reconnect/resync, and save/load generation
replacement are exercised without a benchmark-only world adapter.

The I0.8 native acceptance likewise consumes the existing private-state path:
authenticated Admission authorizes an owner-bound backpack, the generic remote
UI protocol carries granular Inventory move/split/equip/unequip operations, and
the production Godot renderer presents and submits them. Foreign-container
rejection, stale/duplicate intent, reconnect, and save/load generation
resynchronization are permanent gates; Inventory contents never enter public AOI.
I0.9 adds compiled per-container versus uniform rule selection, separate capacity
dimensions, and viewer-neutral placement value contracts. I0.10 makes one 5x5
Grid backpack real: optional persisted instance placement, content-authored item
footprints, transactional Grid/list/equipment transitions, save/load rebuild, and
owner-private remote Godot repositioning. I0.11 adds independently mutable
per-instance item-count and Grid dimensions bounded by compiled profile maxima,
rejects invalidating shrink transactionally, and reprojects upgrades through the
remote UI. Weight, nesting, Slot, Freeform, rotation, automatic packing, and
capacity-effect policy remain explicitly unsupported.

Chat C0.0 adds a third concrete Game semantic without changing Network: trusted
entered actors can produce bounded packet-free Whisper/Say/Yell occurrences using
current authoritative world position and exact X/Z hearing. Commands are
intercepted before speech; `/help` and `/where` return private results. Remote
protocol, client state, and Godot presentation remain deliberately deferred to
C0.1.
The verifier continues to enforce the exact active production project inventory
and dependency directions.

## Verify

Requirements are .NET 8 and, for the Godot gate, Godot 4.7.1 Mono. Set
`ULTRASIM_GODOT` or pass `--godot=/path/to/executable` when it is not in the
known local location.

Run the complete canonical gate:

```bash
./verify.sh --all-configurations
```

Useful focused variants:

```bash
./verify.sh --architecture-only
./verify.sh --configuration=Release --skip-godot
./verify.sh --all-configurations --no-restore
```

Launch the current T0.10 client with a Godot 4.7 .NET build:

```bash
godot --path Client/Playable.Godot
```

The controls and save location are documented in
[`Client/Playable.Godot/README.md`](Client/Playable.Godot/README.md).

The architecture-only gate also validates canonical documentation, relative
links, root-document policy, XML-document generation policy, and stale claims.

## Documentation

Start with the [documentation map](Docs/README.md).

- [Whole-engine architecture](Docs/ARCHITECTURE.md)
- [Runtime, authority, and cadence](Docs/RUNTIME.md)
- [Persistence](Docs/PERSISTENCE.md)
- [UI and client projection](Docs/UI.md)
- [Game composition](Docs/GAME.md)
- [Inventory](Game/Inventory/README.md)
- [ECS](Kernels/ECS/README.md)
- [Network kernel foundation](Kernels/Network/README.md)
- [Game replication](Game/Replication/README.md)
- [Client Network](Client/Network/README.md)
- [Benchmark methodology](_Benchmarks/README.md)
- [Terrain / World T0 accepted plan/status](Docs/Plans/TERRAIN.md)
- [Network V3 current architecture/status](Docs/Plans/NETWORK.md)
- [Hosted Network H0 current lifecycle/status](Docs/Plans/HOSTED_NETWORK.md)

Source and tests are executable truth. XML comments define public API semantics.
Current Markdown records current architecture, rationale, accepted decisions, and
explicitly stopped/future work. Immutable benchmark artifacts preserve accepted
measurements, while git history preserves superseded plans, experiments, and
archaeology.
