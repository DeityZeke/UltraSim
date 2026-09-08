# UltraSim architecture

UltraSim is a bounded, deterministic simulation substrate for very large game
worlds. Its design target is the smallest predictable machine that can advance
that simulation efficiently—not a general framework for every possible game.

## Principles

> Identity is a value. Components are data. Storage is layout-optimized.
> Traversal is contiguous. Mutation crosses a barrier. Barrier work is
> batchable. Runtime memory is bounded. Normal use cannot corrupt invariants.

Other permanent rules are:

- design for the ecosystem; implement for the game;
- do not abstract the first instance;
- bridges translate, route, and synchronize;
- cold compilation may be sophisticated when it produces a small hot machine;
- expected rejection is transactional; an unexpected failure after trusted
  mutation begins faults the owning authority rather than pretending recovery.

## Four layers

```text
AUTHORING / MODDING
    source definitions and future tools
             |
GAME COMPOSITION
    concrete schema, content, rules, workflows
             |
RUNTIME / BRIDGES / ORCHESTRATION
    authority lifecycle, persistence, hosting, client projection
             |
KERNELS
    ECS, Network, UI, and dependency-neutral Spatial mechanisms/contracts
```

Dependencies point downward or sideways through narrow semantic contracts.
Kernels do not reference Server, Client, or Game. Server Runtime depends on ECS,
not Game. Client Core owns retained client projections: UI depends on the neutral
UI kernel, while the concrete world projection consumes immutable/semantic
`Game/World` values without receiving ECS or Server authority. Game Composition
is the concrete place where authoritative game-owned domains meet.

The repository verifier freezes the exact active project-reference graph. A new
reference is an architecture decision, not a convenience edit.

## Authority

One `ServerRuntime` owns one active `ServerGeneration`. A generation is the
coherent authority root:

```text
ServerGeneration
    ECS world
    authoritative clock
    game-owned generation state
```

Publication prepares and validates a complete candidate before one root swap.
The previous generation is retired only after publication. Game code borrows
authority through callback-scoped capabilities; hosts and clients never receive
the ECS world as shared mutable state.

Offline play remains server-authoritative. `Client/Local` owns one Network-free
application bridge: mutable Game/Server authority runs on a private owner thread,
while bounded commands and domain-specific semantic debt/proof cross to Client
state. It avoids fake packetization while preserving the same authority/
presentation separation proved by remote paths.

## Data and time boundaries

ECS topology is frozen while scheduled systems execute. Structural commands
become visible only at the documented barrier. Changed-value observation and
structural lifecycle observation are independent contracts.

Authoritative cadence follows:

> Tick is identity. Delta is time. Frequency determines Delta.

Per-time behavior uses elapsed or authoritative fixed-point time and must prove
its meaning at multiple fixed cadences. Per-step behavior is deliberate and
documented. Fixed 60 Hz, 1x playback, with cadence adaptation disabled is the
uncompromised performance baseline.

Authoritative existence is independent from simulation activity, world
residency, structural freshness, public interest, client semantic residency,
and presentation. Simulation fidelity is game-domain policy rather than a
Spatial or ECS-kernel tier. A reduced activity mode selects explicit
domain-defined semantics and reconciliation; it is not permission to run the
ordinary calculation on arbitrarily skipped ticks. The policy selecting a mode
may be reconstructible, but every resulting authoritative state transition
still commits through the owning domain.

Scheduler cadence grants an execution opportunity. It does not define the
semantic meaning of delayed persistent work. Persistent deadlines and temporal
facts use authoritative world time, and each concrete domain defines what late
execution means—for example elapsed-time integration, latest-state evaluation,
bounded replay, completion, or expiry. Replaying every missed scheduler tick is
never the default. Do not introduce a universal fidelity enum, due-work queue,
or catch-up framework before concrete game domains prove a common contract.

## Boundedness and allocation

World, scheduler, command, lifecycle, content, persistence, UI, Network, and
future spatial-world capacities are chosen explicitly. Hot paths use reserved
unmanaged/native or contiguous numeric storage and avoid warmed managed allocation
where their contract promises it. Capacity exhaustion is a named result or
exception; it is not permission to allocate an unbounded fallback.

Managed collections remain acceptable in cold game composition and small
derived/application indexes when their ownership and scale are explicit.

## Current project groups

- `Kernels/ECS*`: ECS runtime and source generators.
- `Kernels/Network`: one dependency-clean canonical Full Network V3 source/product,
  physically organized as shared Core plus Hosted Client and Hosted Server roles.
  Optional same-identity ClientOnly/ServerOnly builds remove an unused Hosted role
  without changing shared protocol behavior.
- `Kernels/Network.Testing`: deterministic impairment, loopback, controlled
  handshake, and two-ended transport evidence helpers; never a production product
  dependency.
- `Kernels/UI`: neutral view/model/action protocol.
- `Kernels/Spatial`: dependency-neutral integer spatial values, explicit
  geometry/bounds, floor normalization, and checked wide-distance math. It owns
  no world storage, membership, AOI, publication, or threads.
- `Server/Runtime`, `Server/Hosting`, `Server/Persistence`: authority,
  deployment, and durable save-set mechanics.
- `Client/Core`, `Client/Local`, `Client/Network`, `Client/Godot`: retained
  projection, packet-free local application composition, remote delivery, and
  the one native Godot application/script boundary.
- `Game/*`: concrete content, state, policy, authoritative workflows, the fixed
  game-world position profile, and replication composition.
- `_Tests`, `_Benchmarks`, `_Tools`: executable evidence and repository gates.
- `_Archive/_V2`: frozen historical/reference implementation, compiled only by
  explicit comparison projects. The rest of `_Archive` is likewise outside the
  active project/build and Godot discovery surfaces.

Network N0 is accepted as the retained `MaintainedEntityRelation` profile, including
bounded long-lived WorldState recovery and the measured dense/sparse evidence.
Its final sparse experiments showed that steady-state replication scales much
better than continuously mutating precise recipient/entity interest topology.
Terrain / World T0 is accepted/frozen. [Network N1](Plans/NETWORK.md) and Hosted
H0 are accepted through the dedicated-process gate; `ChunkPublication` is the
selected current-game profile while `MaintainedEntityRelation` remains supported.
P0.1 adds `DirtyEntitySpatialFanout` as a third Game-owned profile: dirty-time
spatial recipient discovery plus recipient topology reconciliation, with bounded
semantic knowledge/debt and no persistent Recipient×Entity AOI graph.
T0.0 accepted the dependency-neutral Spatial seam plus the 16-byte game-world
position/profile. T0.1 accepted persisted ECS position authority paired with a
bounded, generation-owned maintained index through a generic Runtime post-ECS-step
capability. Its full-capacity migration behavior is model-checked. Packed
terrain storage begins with T0.2's accepted 32 x 32 ushort material plane and a
33 x 33 unsigned-q5 offset plane with one int32 absolute base per chunk.
T0.3 sectioned WorldPack now stores those dense base planes behind canonical
sorted directories, bounded validation, immutable ownership, and content binding.
T0.4 adds exact integer base-height/material queries and a bounded deterministic
engine-neutral base mesh with neighbor-aware seam frames; it does not introduce
Godot or make the base sampler a general multi-surface ground contract.
T0.5 adds optional section-local frozen-static and static-Multi layouts without
changing the frozen terrain chunk row. Stable world/static/Multi/piece identities,
borrowed chunk views, deterministic compatible render groups, definition binding,
and bounded exact 3D static overlap now remain above the dependency-neutral
Spatial kernel. Static-Multi placement is frozen-world data, not one ECS entity
per piece and not a moving-aggregate contract.
T0.6 and T0.7 add observer-independent dynamic chunk publication plus the exact
packet-free interest/convergence oracle. T0.8 now implements the distinct
production client semantic cache and bounded Godot render owner: client capacity
is resident-scope-shaped rather than server-address-space-shaped, semantic and
render residency are independent, statics/public rows are grouped rather than
node-shaped, and floating-origin changes remain presentation-only. Its concrete
client presentation chain is:

```text
absolute authoritative world / SpatialChunkKey
        -> bounded client semantic residency
        -> engine-neutral ClientPresentationWindow desired operations
        -> thin concrete-engine admission bridge
        -> private pooled Godot render slots
        -> engine frustum/occlusion
```

The semantic handle, relative window offset, and private render slot never alias.
The window radius, candidate construction, Enter/Move/Exit diff, reversal, and
reset policy live in Client Core. Godot owns only native admission/realization;
a second engine consumes the same bounded operation semantics without cloning
the window state machine. The plan/admit/commit boundary preserves transactional
desired membership when a concrete backend rejects work.
Player-local subtraction happens in integer millimeters before float conversion;
camera orientation does not mutate semantic residency or resource lifetime.
The pack remains extensible to future sparse structural surfaces; world topology
is not restricted to one heightfield surface per X/Z.
Future `WorldRegion`, property, property-unit, Multi, terrain, and edit-lease
identities are likewise distinct. Regions are durable true-3D spatial-policy
metadata rather than chunk partitions or ECS entities by default, and concrete
policy domains may resolve different members of the bounded region set applying
at one location instead of relying on one universal winning region. T0 preserves
this seam without implementing a region/property framework.
T0.9 now adds a concrete exact-pack-plus-overlay `WorldPreparedView` and a fourth
`worldview` save-set participant. Immutable pack bytes remain content; bounded
complete terrain/static/Multi replacements carry independent structural
revision/hash identity and restore privately before whole-generation publication.
Prepared client mesh generation resolves that same view. Cold pack binding owns
compact immutable structural identity lookup, allowing ordinary overlay
validation to scale with retained replacements rather than the entire base pack;
complete logical Multi-root replacement remains intentionally global.
Runtime slots, publication cursors/history, client residency, and Godot resources
remain derived and absent from the artifact.
T0.10 closes the launchable local woods/cabin vertical. Network N1.0-N1.4 now
layer bounded recipient chunk subscriptions, canonical chunk messages/proofs,
MTU-admissible independent-unreliable delta/absence delivery, and private atomic
Bulk snapshot recovery plus dirty-driven scheduling onto those accepted
publication/client-cache contracts. Hosted H0.0-H0.5 prove external admission,
secure promotion, path rebinding, owner-thread handoff, and dedicated-process
composition. `ChunkPublication` has therefore passed the current-game adoption
gate; the N0 implementation remains the explicit `MaintainedEntityRelation`
alternate/reference profile rather than a removal target.
AOI is Game/Replication policy, not Network policy. The current UltraSim game is
using chunk subscriptions/publication history alongside the retained entity-
relation profile. Either profile must flatten after Game encoding into the
same dependency-clean Network boundary: connection, descriptor/lane, bounded
payload, security, delivery, and transport. Interest membership and replication-
source cursor semantics remain concrete Game concerns; no universal generic AOI
interface or separate entity/chunk Network stack is implied.
