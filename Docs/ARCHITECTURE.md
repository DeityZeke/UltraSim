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

The dedicated/headless authority composition has no Godot SDK, native scene, renderer,
input, or Audio adapter requirement. Godot is a concrete client-side implementation:
its world realization, physical input capture and application shutdown remain within
`Client/Godot`. Engine-neutral Client Core owns semantic projection and input policy;
Godot does not become a Game or Framework type. P3 publishes the selected physical
client adapter and its lifecycle through an owner-thread, read-only Control Plane
provider. Implementation selection is fixed by composition; settings such as
vegetation remain distinct runtime/device configuration. The existing native
application owner is retained rather than wrapped in a universal platform interface.

## Gameplay realization: OOP Extensions, ECS, or hybrid

UltraSim does **not** require gameplay to be modeled as ECS.

An **Object Extension** (“Extension”) is sparse managed state/behavior attached to an owner
identity, or to a global owner, outside the ECS component model. Multiple named instances of
one Extension family may attach to the same owner. A Runtime Package may define an Extension
type; a live Runtime Service or Engine Core may manage instances. These are distinct concepts.

The ECS Kernel is the high-scale data-oriented storage/execution realization for state whose
cardinality, homogeneity, or update density earns it. The Framework Runtime Host may also
host ordinary managed C# Extensions/runtime objects, or hybrids that bind one rich object to an ECS
entity for selected high-volume state.

Conceptually:

```text
OOP-first
    BaseItem → BaseWeapon → BaseSword → Katana

ECS-first
    Entity + dense Components + Systems

Hybrid
    rich Extension object
        ↕ optional typed bridge
    EntityId with movement/spatial/health/etc.
```

An Extension/runtime-object identity should remain conceptually distinct from `EntityId`; an ECS
entity is not required merely to make an ordinary object exist unless the selected realization
earns that coupling.

One authoritative owner exists for each piece of state. If Health is ECS-owned, a convenient
Extension `Health` property may route through the Health capability, but must not maintain a second
unsynchronized authoritative copy.

Engine Contracts preserve gameplay meaning across realization changes. A feature may begin as a
small OOP implementation and later move hot/high-volume state into ECS without forcing every
consumer definition/package to change. The reverse is also permitted when a rich low-cardinality
object gains nothing from ECS representation.

P4's [typed Definition composition](../Framework/Definitions/README.md) is the cold meeting point
for independent Engine Contracts. Stable authored Definition IDs are distinct from ECS prefab
identity; dense Definition handles are local to one composition. An optional Core can disappear
without erasing its Contracts-owned facet meaning. Runtime Services, Object Extensions, ECS
components, and immutable Definition facets remain separate kinds of state.

This is intentionally compatible with both RunUO-like conventional C# authoring and large
Cities-Skylines-like data-oriented simulation.

## Authoring / extensibility seams

The preferred content-authoring/extensibility model is several small typed seams rather than one universal Mod API.

UltraSim's own Game Implementation should use these same seams wherever practical. Third-party “mods” are not a separate runtime species; they are external packages/content using the same authoring contracts. If ordinary first-party content requires privileged bypasses into internals, treat that as evidence that the authoring seam is incomplete.

Community/mod-layer ideas should be evaluated for the underlying semantic need they reveal, then folded into the appropriate Engine/Framework/Kernel contract only when that general seam is justified:

```text
package lifecycle
typed Engine/Framework Contracts
typed definition facets
Definition Tags/Sets
definition aliases/migrations
typed subsystem-owned hooks/events
runtime capabilities/attachments
Control Plane providers
bounded package networking where earned
presentation/audio contracts
```

Do not institutionalize access-transformer/private-member patching, a universal
`Dictionary<string, object>`, or one global cancellable event bus as the normal extension model.

The broader candidate catalog is maintained in
[Future Content & Engine Extensibility](Plans/FUTURE_CONTENT_AND_ENGINE_EXTENSIBILITY.md). Water/Liquid Bodies,
Definition Tags/Sets, migration/remap semantics, a deliberate typed hook/event registration
seam, and semantic presentation references/3D-derived UI portraits are the currently promoted items from that catalog.

## Semantic presentation identity

Game/content definitions should be presentable through typed semantic roles rather than requiring
every consumer to name a physical image/model resource directly.

Conceptually:

```text
DefinitionDomainId + StableDefinitionId + PresentationRoleId
```

may resolve roles such as World, InventoryPortrait, VendorPortrait, CharacterPortrait, Inspection,
or Thumbnail.

A dedicated 2D icon is an optional override. When a compatible role-specific asset is absent, a
client may derive a UI portrait from the already-authorized World/object presentation and cache it
as disposable renderer output. A cached portrait may later be atlased; a small bounded live/
inspection realization may rotate or animate locally.

The UI Kernel remains unaware of Item/Creature/etc. semantics, and derived portrait/cache identity
never becomes Game/content authority.

## Inbound HTTP Framework / Bridge seam

`Framework/Http/Endpoint` owns instance-based inbound request/response contracts, fixed
service-owned routing, per-service bounded admission and lifecycle. It is BCL-only Framework
infrastructure, **not an HTTP Engine Core**, and does not own outbound HTTP. `Bridges/Http.Kestrel`
realizes that contract through supported ASP.NET Core lifecycle APIs. `Server/Hosting` composes
them; Static Content is the first production provider. The Framework/Bridge references no
Game/Client/Server or content/discovery semantics. Shared binding does not imply shared service
authorization, queue, concurrency or deadline budgets.

This was the narrow F1.12-R3 correctness-driven extraction, not a generic package host. Future Discovery is an open transport-neutral application contract
for client, website and third-party consumers; registry/status providers may share this endpoint.
R3 implements neither Discovery, registry, outbound integrations nor federated Accounting.

## Framework Control Plane

P1 introduces the BCL-only [Framework Control Plane](../Framework/ControlPlane/README.md), not an
Engine Core. It publishes frozen Diagnostic/Control/Action/Collection descriptors separately
from owner snapshots; typed scalar values, capabilities, exact provider-generation fences and
provider-owned revisions route bounded cold operations. Requested/effective/pending values have
independent provenance; unsaved viewer drafts are not pending state. Persistence and mutation
remain with the owning subsystem, including coupled validation of provider-local batches.

The ECS adapter sits in Bridges above Framework and ECS; the Kernel gains no upward dependency.
The ServerRuntime adapter also sits above both sides, using a narrow immutable public policy/clock/
idle observation. P5 promotes the Client Settings adapter above existing Client Core and its
resolver/store into all three physical client editions. A generic viewer model contains no
concrete subsystem types. F10 now contains that owner-published User surface and a discoverable
read-only Developer view, while retaining its detailed Advanced display form for parity. The
ECS/ServerRuntime bridges remain opt-in and F4's sanitized authority-authored Server Monitor
remains Game UI; no remote operator command route is implied.

Registry operations are synchronous, owner-thread-bound and non-reentrant; cross-thread operator
transport remains future product work. No registry lookup, descriptor allocation, capability
check or polling is inserted into ECS, Network or rendering hot loops. P2 package composition
and server-owned authoring are now opt-in; Discovery and richer Web/Admin products remain future.

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
  geometry/bounds, floor normalization, checked wide-distance math, and checked
  fixed-table capacity sizing. It owns no world storage, membership, AOI,
  publication, or threads.
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
