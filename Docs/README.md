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
- [Game Admission](../Game/Admission/README.md) — authoritative accountless, guest/demo, and
  account-backed entry, with session identity and playable binding kept distinct.
- [Inventory](../Game/Inventory/README.md) — authoritative mutation, owner-private
  UI authorization, the I0.8 remote Godot vertical, the I0.9 container-policy
  foundation, I0.10 authoritative Grid placement, and I0.11 dynamic instance
  capacity.
- [Inventory plan/status](Plans/INVENTORY.md) — current accepted Inventory
  contracts plus explicitly deferred placement, capacity, nesting, and viewer work.
- [Chat](../Game/Chat/README.md) — packet-free authoritative spatial speech,
  command interception, exact hearing, and retained occurrence semantics.
- [Chat plan/status](Plans/CHAT.md) — accepted C0.0-C0.1a spatial/remote occurrence
  semantics plus deliberately deferred routed-channel, moderation, history, and
  richer extensibility work.
- [UltraSim Simple Physics](../Game/Physics/README.md) — the concrete generation-owned
  P0.0-P0.1b resolver, first-class occupancy, and bounded swept blocker test.
- [Physics plan/status](Plans/PHYSICS.md) — accepted P0.0 ownership extraction and
  P0.1a relocation occupancy plus P0.1b swept movement, with support/step and
  providers unstarted.
- [Game replication](../Game/Replication/README.md) — canonical projection,
  five concrete dynamic-public profiles, selected `ChunkPublication` semantics,
  P0.4 conservative selection, and the real public-actor consumer vertical.
- [Spatial contracts](../Kernels/Spatial/README.md) — dependency-neutral integer
  spatial identity, geometry, bounds, and canonical math.
- [Game world](../Game/World/README.md) — fixed world-position profile and accepted
  T0 authority/publication/interest/world-layer contracts.
- [Client Core](../Client/Core/README.md) — retained engine-neutral UI and
  semantic-world projections, transactional presentation-window planning, and
  F0.1 shell/settings/focus policy.
- [Client Content](../Client/Content/README.md) — A0.0 passive package identities,
  exact payload validation, realm-scoped activation plans, deterministic Add/Replace
  resolution, immutable catalogs, application-proof vocabulary, the A0.2 persistent
  hash-addressed install/reopen/pinning/eviction substrate, A0.3 source-neutral acquisition, and
  A0.4 canonical local/server-HTTP/external-HTTPS source parity, A0.5 hash-level delta,
  repair, mirror, Optional-upgrade, bounded-concurrency, and DNS-target hardening, A0.6 canonical
  typed UI-definition resolution, and A0.7 complete WorldPack resolution through the applied realm
  catalog with World-owned open/bind validation.
- [Built-in Test Worlds](../Client/BuiltInTestWorlds/README.md) — A0.1 local Cabin
  Default/Replace/Add package oracle, frozen catalog fingerprints, and exact ownership deltas.
- [Local client application bridge](../Client/Local/README.md) — the F0.0b
  Network-free authority-owner-thread composition with bounded command,
  semantic-debt, and application-proof handoff.
- [Client Network](../Client/Network/README.md) — F0.3a Direct Connect deployment trust/session
  establishment plus the F0.3b guest/control composition over public chunk state, Chat Events,
  owner state, and owner-private UI/Inventory, with A0.3 authenticated content requirement,
  A0.4 physical byte-source parity, A0.5 resilient granular acquisition, A0.6 content-backed
  remote UI HotOpen, A0.7 atomic initial structural publication, and exact application-proof
  gating before guest admission.
- [Direct Connect Concept Demo server](../Server/DirectConnect/README.md) — external Hosted endpoint
  with durable deployment key/realm identity and one bounded F0.3b remote playable application.
- [Godot client application](../Client/Godot/README.md) — the single maintained native adapter,
  persistent shell, and local presentation project, with physically distinct Single Player,
  Online, and Complete published dependency products.
- [Client shell/composition plan](Plans/CLIENT.md) — the real-client reconstruction
  roadmap: Single Player/Online/Complete/Contentless compositions, local-vs-remote
  semantic parity, Network-free local authority/session extraction, Direct Connect
  identity onboarding, human-scale presentation calibration, nameplates/chat,
  layered settings, server-recommended profiles, and server-composed UI.
- [Asset streaming/content layering plan](Plans/ASSET_STREAMING.md) — stable logical
  content identity, activation/application proof, deterministic Default/Replace/Add,
  passive-content validation, content-addressed blobs with realm-scoped ownership,
  source-independent byte delivery, immutable catalog generations, Cabin parity,
  and complete-WorldPack streaming scope.
- [Concept Demo / live-test plan](Plans/CONCEPT_DEMO.md) — the cross-cutting
  super-pre-alpha target: floating central terrain, Sky Island/cave/Far Island,
  signs and fallback lab, guest entry, render-distance Settings, minimal Inventory
  and animations, Local/Global/system Chat, read-only server monitor, opt-in client
  diagnostics, server test metrics/markers, and staged multiplayer stress evidence.

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
- [Concept Demo / live-test plan](Plans/CONCEPT_DEMO.md) — concrete future consumer
  for intentional surface absence, upper/lower terrain surfaces, cave and sky
  topology, far-distance AOI/residency transitions, and human-facing streaming
  evidence without redefining the owning Terrain/Physics contracts.

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
- [Chat C0](Plans/CHAT.md) records accepted C0.0-C0.1a spatial/remote occurrence
  semantics and the stopped routed-channel/social/moderation work until a real
  consumer earns it.
- [Physics P0](Plans/PHYSICS.md) records the P0.0 Simple Physics extraction,
  P0.1a occupancy-aware relocation, P0.1b swept blocking, and stopped P0.1c+
  behavior/provider work.
- [Client F0](Plans/CLIENT.md) records the frozen real-client shell/composition,
  authority-thread/local-session extraction, Direct Connect trust, settings/control-
  profile, world-presentation, server-composed UI, and parity roadmap.
- [Asset Streaming A0](Plans/ASSET_STREAMING.md) records the passive content package,
  activation proof, CAS/cache, hosting/source, Default/Replace/Add, catalog/resource
  lifetime, Cabin parity, complete-WorldPack canary, Contentless Online, and deferred
  executable-extension boundary.
- [Concept Demo](Plans/CONCEPT_DEMO.md) records the accepted cross-cutting Round 1
  human demo/stress target and explicitly defers Combat damage/HP/death to Round 2;
  subsystem APIs remain owned by their respective plans.

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
