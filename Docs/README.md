# UltraSim documentation

This is the map for current UltraSim documentation. Source and tests are the
executable truth; XML comments define public API contracts; these documents explain
current architecture, accepted decisions, rationale, and explicitly stopped/future
work. Accepted measurements live in immutable artifacts under
[`_Benchmarks/Baselines`](../_Benchmarks/Baselines/README.md); superseded plans and
acceptance narratives remain available through git history.

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
  UI authorization, the I0.8 remote Godot vertical, the I0.9 container-policy
  foundation, I0.10 authoritative Grid placement, and I0.11 dynamic instance
  capacity.
- [Inventory plan/status](Plans/INVENTORY.md) — current accepted Inventory
  contracts plus explicitly deferred placement, capacity, nesting, and viewer work.
- [Chat](../Game/Chat/README.md) — packet-free authoritative spatial speech,
  command interception, exact hearing, and retained occurrence semantics.
- [Chat plan/status](Plans/CHAT.md) — accepted C0.0 contracts and the deliberately
  deferred remote, routed-channel, moderation, and extensibility checkpoints.
- [Game replication](../Game/Replication/README.md) — canonical projection,
  five concrete dynamic-public profiles, selected `ChunkPublication` semantics,
  P0.4 conservative selection, and the real public-actor consumer vertical.
- [Spatial contracts](../Kernels/Spatial/README.md) — dependency-neutral integer
  spatial identity, geometry, bounds, and canonical math.
- [Game world](../Game/World/README.md) — fixed world-position profile and accepted
  T0 authority/publication/interest/world-layer contracts.
- [Client Core](../Client/Core/README.md) — retained engine-neutral UI and
  production semantic-world projections plus transactional presentation-window
  planning.
- [Client Network](../Client/Network/README.md) — hostile authenticated protocol-
  to-client semantic application for public chunk state, retained entity controls,
  Events, and owner-private UI/Inventory.
- [Godot client adapter](../Client/_Godot/README.md) — native UI/world presentation,
  bounded render residency, grouping, and floating origin.
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

- [Terrain / World T0 plan/status](Plans/TERRAIN.md) — deterministic world/chunk
  coordinates, packed terrain/statics, UO-like Multi structure composition,
  preserved 3D region/property seams, dynamic public chunk publication, bounded
  dirty history, client streaming, Godot batching, and the playable woods/cabin
  vertical.

## Network

- [Network kernel foundation](../Kernels/Network/README.md) — dependency-clean
  bounded connection/packet ownership, generated protocol, lane contracts,
  security/replay, reliability, deterministic impairment, connected/shared UDP,
  Hosted admission/path mechanisms, and connection-owner handoff.
- [Network V3 current architecture/status](Plans/NETWORK.md) — authoritative current
  Network/replication policy. It records the permanent ownership/identity/proof/lane
  rules, cadence independence, the five-profile dispositions, P0.4 conservative
  selection, `ChunkPublication` semantics, rating-versus-stress distinctions, and
  the current consumption-ready stop.
- [Hosted Network H0](Plans/HOSTED_NETWORK.md) — accepted H0.0-H0.5 external UDP
  lifecycle: deployment pinning, stateless bounded cookies/rates, authenticated
  promotion, explicit IPv4 path ownership/rebinding, exclusive connection-owner
  packet handoff, dedicated-process composition, and explicit deployment non-claims.

## Current Network/replication status

Network is no longer in an open-ended profile-selection phase.

```text
ChunkPublication                 selected current-game dynamic-public profile
MaintainedEntityRelation         retained alternative
DirtyEntitySpatialFanout         retained alternative
DirtyEntityChunkBatchFanout      experimental/reference
DirtyChunkSpatialFanout          experimental/reference
```

All five catalog entries are dynamic-public only. Terrain/base and frozen
structural/static content remain WorldPack/static ownership. Runtime cross-profile
handoff is unsupported; a future feature must first earn a stable cross-domain Game
public identity, ownership lineage, client ordering/deduplication, and transactional
handoff.

N1.0-N1.4 and Hosted H0.0-H0.5 are accepted. P0.3H-I close lower ready-selection
and proof-envelope costs; P0.3J is the strict probes-off server-role rating closure;
P0.3K records independent server-step/AOI cadence and the integrated deadline
envelope; P0.4 closes selection conservatively. The real public-actor vertical then
proved ECS -> selected public replication -> secure Network -> production client
semantic cache -> Godot presentation.

N1.5 shared encoding, another profile tournament, cross-profile runtime switching,
2K/4K transport work for its own sake, public-WAN readiness, production remote CCU,
and IPv6/multi-host Hosted expansion remain deliberately stopped until a concrete
consumer/deployment need earns them.

## Evidence and tools

- [Benchmark methodology](../_Benchmarks/README.md)
- [Accepted baseline policy](../_Benchmarks/Baselines/README.md)
- [Local AI tooling](../_Tools/LocalAI/README.md)

## Active plan / status documents

- [Terrain / World T0](Plans/TERRAIN.md) is accepted/frozen through the launchable
  T0.10 woods/cabin vertical while preserving named future world/terrain seams.
- [Network V3](Plans/NETWORK.md) records the accepted current architecture plus
  explicitly stopped/future work; it is not a pre-selection candidate plan.
- [Hosted Network H0](Plans/HOSTED_NETWORK.md) records the accepted H0.0-H0.5 local/
  dedicated lifecycle plus explicit deployment gates still unclaimed.
- [Chat C0](Plans/CHAT.md) records accepted packet-free C0.0 semantics and the
  stopped C0.1+ remote/presentation/channel work.

## Source-of-truth hierarchy

```text
source + tests                 executable invariants
XML / IntelliSense            public API semantics
current Markdown              current architecture, rationale, accepted decisions
Docs/Plans                    plan/status contracts + explicitly stopped/future work
benchmark artifacts           accepted measured evidence for named checkpoints
git history                   superseded plans, reports, experiments, archaeology
```

New acceptance reports do not belong at the repository root. Promote lasting
contracts into the appropriate current document, preserve accepted measurements as
immutable baseline artifacts, and let git retain historical narrative/detail.
