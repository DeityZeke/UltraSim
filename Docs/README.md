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
- [Inventory](../Game/Inventory/README.md) — authoritative mutation, owner-private
  UI authorization, and the I0.8 remote Godot vertical.
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
- [Client Network](../Client/Network/README.md) — authenticated protocol-to-client
  semantic application, including N1 chunk delta/absence application proofs.
- [Godot client adapter](../Client/_Godot/README.md) — native UI/world
  presentation, bounded render residency, grouping, and floating origin.
- [Launchable T0.10 client](../Client/Playable.Godot/README.md) — real
  OfflineDirect woods/cabin application, controls, and save location.

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
- [Network V3 N1 forward plan](Plans/NETWORK.md) — selected chunk-native public
  spatial replication profile for the current game. AOI remains Game-owned policy
  above one shared Network delivery boundary. N1.0-N1.4 and Hosted H0.5 cover
  subscriptions, wire/proof semantics, ordinary/recovery delivery, dirty work,
  compact storage, mixed/adoption evidence, and the dedicated-process gate. N0
  remains compiled as the historical/reference profile.
- [Hosted Network H0](Plans/HOSTED_NETWORK.md) — externally admitted shared UDP,
  deployment pinning, bounded cookies/rates, authenticated promotion, IPv4 path
  ownership, connection-owner packet handoff, and dedicated-process H0.5 scope.

## Evidence and tools

- [Benchmark methodology](../_Benchmarks/README.md)
- [Accepted baseline policy](../_Benchmarks/Baselines/README.md)
- [Local AI tooling](../_Tools/LocalAI/README.md)

## Active plan

- [Terrain / World T0](Plans/TERRAIN.md) is accepted/frozen through the launchable
  T0.10 woods/cabin vertical.
- [Network N1](Plans/NETWORK.md) is selected as the current game's
  `ChunkPublication` profile through N1.4 and the Hosted H0.5 dedicated-process
  closure. Frozen N0 is retained as `MaintainedEntityRelation`; N1.5,
  profile-composition selection, and public-WAN/production-CCU claims remain stopped.
  P0.3G records an experimental Dirty Chunk delivery-packing floor without changing
  the selected profile or claiming hosted transport integration.

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
