# UltraSim documentation

This is the map for current UltraSim documentation. Source and tests are the
executable truth; XML comments define public API contracts; these documents
explain the current architecture and rationale. Completed plans and acceptance
reports live in git history. Accepted measurements live in immutable JSON under
[`_Benchmarks/Baselines`](../_Benchmarks/Baselines/README.md).

## Engine

- [Architecture](ARCHITECTURE.md) — layers, dependency direction, authority,
  boundedness, and the source-of-truth hierarchy.
- [Runtime](RUNTIME.md) — `ServerRuntime`, generations, hosts, lifecycle,
  publication, and authoritative cadence.
- [Persistence](PERSISTENCE.md) — ECS snapshots, save-set transactions, game
  participant composition, recovery, and generation publication.
- [UI](UI.md) — neutral UI protocol, authoritative workflows, client state, and
  Godot presentation.
- [Game](GAME.md) — concrete game composition, schema/content ownership,
  Accounting, Admission, and Inventory.
- [Game replication](../Game/Replication/README.md) — canonical projection,
  capture/history, authoritative ingress, WorldState/Events, and the accepted N0
  replication contracts.
- [Spatial contracts](../Kernels/Spatial/README.md) — dependency-neutral integer
  spatial identity, geometry, bounds, and canonical math.
- [Game world](../Game/World/README.md) — the fixed 16 m / 0.5 m / 0.1 m / 1 mm
  world-position profile and accepted T0.0–T0.7 authority/publication/interest
  contracts.
- [Client Core](../Client/Core/README.md) — retained engine-neutral UI and
  production semantic-world projections plus transactional presentation-window
  planning.
- [Godot client adapter](../Client/_Godot/README.md) — native UI/world
  presentation, bounded render residency, grouping, and floating origin.

## ECS

- [ECS overview](../Kernels/ECS/README.md)
  - [execution and API selection](../Kernels/ECS/EXECUTION.md)
  - [scheduler](../Kernels/ECS/SCHEDULER.md)
  - [structural mutation](../Kernels/ECS/STRUCTURAL.md)
  - [compiled content](../Kernels/ECS/CONTENT.md)
  - [lifecycle publication](../Kernels/ECS/LIFECYCLE.md)
  - [persistence](../Kernels/ECS/PERSISTENCE.md)

## World / terrain

- [Terrain / World T0 active plan](Plans/TERRAIN.md) — deterministic world/chunk
  coordinates, packed terrain/statics, UO-like Multi structure composition,
  and a preserved separate seam for future 3D regions/property semantics,
  dynamic chunk membership, revisioned chunk publication, bounded dirty history,
  client streaming, Godot batching, and the playable woods/cabin vertical.

## Network

- [Network kernel foundation](../Kernels/Network/README.md) — bounded byte,
  protocol, reliability, security, and transport mechanisms accepted during N0.
- [Network V3 N1 forward plan](Plans/NETWORK.md) — deferred chunk-native public
  spatial replication. N1 begins only after T0 accepts its chunk/publication
  contracts; current N0 remains the executable reference meanwhile.

## Evidence and tools

- [Benchmark methodology](../_Benchmarks/README.md)
- [Accepted baseline policy](../_Benchmarks/Baselines/README.md)
- [Local AI tooling](../_Tools/LocalAI/README.md)

## Active plan

- [Terrain / World T0](Plans/TERRAIN.md) is the active major implementation plan.
  T0.0 accepted the value/coordinate seam and T0.1 accepted the maintained
  world-spatial index after saturated-capacity/model-oracle hardening. T0.2
  accepted the per-chunk-base 5 mm terrain height and 4,230-byte dense base
  terrain foundation, and T0.3 accepted the canonical sectioned immutable
  WorldPack/source. T0.4 accepted exact base-terrain queries plus the bounded
  deterministic engine-neutral mesh payload after single-space admission,
  current-world query ownership, and four-way seam hardening. T0.5 accepted
  optional packed statics/Multi sections, stable cross-chunk logical identities,
  deterministic render grouping, and bounded exact static-bounds queries. T0.6
  accepted explicit public eligibility and bounded observer-independent chunk
  publication; T0.7 accepted exact interest and the packet-free convergence
  oracle. T0.8 accepted the distinct production client semantic cache and bounded
  native Godot render owner. The window candidate/diff/reset state machine now
  lives in Client Core behind a plan/admit/commit seam, leaving Godot with native
  realization only. T0.9 implements exact-pack durable overlay
  staging/persistence plus that portability hardening and is stopped for review.
  Its invariant hardening makes prepared terrain meshes overlay-aware, closes
  empty/no-op overlay save/restore lineage, proves Multi replacement persistence,
  and makes ordinary identity validation proportional to retained changes rather
  than immutable-world size.
  The next implementation gate is the small playable woods/cabin vertical.
- [Network N1](Plans/NETWORK.md) is intentionally deferred. After T0 is accepted,
  N1 will test recipient-to-chunk subscriptions/cursors against the frozen N0
  sparse relation graph: snapshot on first interest, query bounded dirty history
  thereafter, cadence by chunk tier, client-side fine public visibility.

## Source-of-truth hierarchy

```text
source + tests                 executable invariants
XML / IntelliSense            public API semantics
current Markdown              architecture, rationale, usage
Docs/Plans                    work not yet implemented
benchmark JSON                accepted measured evidence
git history                   completed plans and archaeology
```

New acceptance reports do not belong at the repository root. Promote lasting
contracts into the appropriate current document, preserve accepted measurements
as machine-readable artifacts, and let git retain the historical narrative.
