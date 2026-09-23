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
- [Simulation time / resolution / control](../AIOS/canonical/simulation-time-resolution-and-control.md) —
  canonical cross-system design for world-time/playback separation, deterministic causal
  aggregation, Immediate/Near/Far simulation resolution, player autonomy/direct/command/
  governance control, and possession/controller binding. This is a design target, not a
  claim that all high-speed mechanisms are implemented.
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
  capacity, I0.12 persistent rolled world-item pickup/equip/drop, and I0.13 bounded typed
  Inspection Properties shared by Inventory and world hover.
- [Inventory plan/status](Plans/INVENTORY.md) — current accepted Inventory
  contracts plus explicitly deferred placement, capacity, nesting, and viewer work.
- [Chat](../Game/Chat/README.md) — packet-free authoritative spatial speech, the first routed
  Global audience, explicit non-player System broadcasts, command interception, exact hearing,
  and retained local/remote occurrence semantics.
- [Chat plan/status](Plans/CHAT.md) — accepted C0.0-C0.2 spatial/remote/routed occurrence
  semantics plus deliberately deferred additional channels, moderation, history, and richer
  extensibility work.
- [UltraSim Simple Physics](../Game/Physics/README.md) — the concrete generation-owned
  P0.0-P0.1d resolver: first-class occupancy, bounded swept blocking, multi-surface
  support/clearance, and airborne contact beneath Game-owned vertical motion.
- [Physics plan/status](Plans/PHYSICS.md) — accepted P0.0-P0.1d ownership, occupancy,
  sweep, support/step/clearance, jump/gravity/landing, and subsequent Concept Demo
  playability hardening; general contact sliding and alternate providers remain future.
- [Game replication](../Game/Replication/README.md) — canonical projection,
  five concrete dynamic-public profiles, selected `ChunkPublication` semantics,
  P0.4 conservative selection, the real public-actor consumer vertical, and M0.0's
  shared multi-participant authority above accepted Network/Hosted infrastructure.
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
  typed UI-definition resolution, A0.7 complete WorldPack resolution through the applied realm
  catalog with World-owned open/bind validation, and A0.8 native Contentless Online closure.
- [Client Standard UI](../Client/StandardUi/README.md) — the F0.5 passive stock-definition
  composition library, physically separate from Game UI/Inventory authority.
- [Built-in Test Worlds](../Client/BuiltInTestWorlds/README.md) — A0.1 local Cabin
  Default/Replace/Add package oracle, frozen catalog fingerprints, and exact ownership deltas.
- [Local client application bridge](../Client/Local/README.md) — the F0.0b
  Network-free authority-owner-thread composition with bounded command,
  semantic-debt, and application-proof handoff.
- [Client Network](../Client/Network/README.md) — F0.3a Direct Connect deployment trust/session
  establishment plus the F0.3b guest/control composition over public chunk state, Chat Events,
  owner state, and owner-private UI/Inventory, with A0.3 authenticated content requirement,
  A0.4 physical byte-source parity, A0.5 resilient granular acquisition, A0.6 content-backed
  remote UI HotOpen, A0.7 atomic initial structural publication, A0.8 native Godot composition,
  exact application-proof gating before guest admission, and F0.4 cold public-presentation
  metadata/application proof beside unchanged hot movement records.
- [Headless Network bots](../Tools/NetworkBots/README.md) — PF1's real ClientOnly remote-client
  swarm, deterministic mixed-chaos scenario, central pump, structured telemetry, and manual run
  commands.
- [Direct Connect Concept Demo server](../Server/DirectConnect/README.md) — external Hosted endpoint
  with durable deployment key/realm identity, authenticated Concept Demo World/UI content, and up
  to eight independent remote applications in one shared Game/World authority.
- [Concept Demo deployment composition](../Server/ConceptDemo/README.md) — presentation/content
  deployment data shared by dedicated and embedded hosting without a `Client.Local` dependency.
- [Complete local-host adapter](../Server/ConceptDemo.LocalHost/README.md) — Complete-only M0.0
  adapter which adds remote Network recipients to an existing packet-free local authority.
- [Godot client application](../Client/Godot/README.md) — the single maintained native adapter,
  persistent shell, and local/remote presentation project, with physically distinct Single Player,
  Online, and Complete published dependency products.
- [Client shell/composition plan](Plans/CLIENT.md) — the real-client reconstruction
  roadmap: Single Player/Online/Complete/Contentless compositions, local-vs-remote
  semantic parity, Network-free local authority/session extraction, Direct Connect
  identity onboarding, human-scale presentation calibration, nameplates/chat,
  layered settings, server-recommended profiles, and server-composed UI. F0.5 and M0.0
  are closed; F0.6 is closed by A0.2-A0.8, C0.2 closes Global/System Chat, D0.6 closes the
  read-only monitor, and D0.7a closes shared actor motion/equipment/action semantics plus the
  bundled X/Y animation canary. D0.7b closes bounded actor-GLB content validation plus real
  server Add/Replace, applied-catalog, lazy native X/Y realization. F0.7/F0.8 are closed, and PF1
  now closes the real headless bot plus two-exported-Online mixed correctness gate; PF2 wire
  accounting and PF2.5 ownership/observability are closed, and PF3 now closes authenticated
  performance policy plus independent render/input/submission/authority/Network clocks. PF4/PF5
  and PF5-R1 through R11 are closed; the F1 client-completion campaign is active before PF6.
- [Client completion campaign](Plans/CLIENT_COMPLETION_PLAN.md) — ordered F1.0-F1.12 pre-PF6 implementation plan covering unified content scopes, semantic-vs-presentation resolution, primitive fallback, removable Official content, resource generations, derived resources/atlases, UI/gumps, declarative profiles, scalable Godot RenderingServer/batch tiers, actor representation tiers, renderer stress consumers, and dogfooding/PF6 readiness.
- [Client presentation/content architecture](Plans/CLIENT_PRESENTATION_ARCHITECTURE.md) — durable
  dumb-terminal client model, unified content scopes with presentation precedence/fallback,
  removable Official content, realm-scoped authorization over shared CAS bytes, renderer-independent
  source/derived-resource boundaries, atlas policy, UI/gump layering and lifetime, declarative
  camera/control profiles, and the vegetation/cut-state seam for the pre-PF6 client-completion pass.
- [Visual art / lighting / Cabin benchmark](../AIOS/canonical/visual-art-lighting-and-cabin-benchmark.md) —
  canonical look-and-feel direction and the selected two-room Cabin benchmark for intended real art,
  environment profiles, direct light/shadows, cheap directional light-field work, opening
  transmission, and presentation-only time/profile testing before authoritative environment systems exist.
- [Asset streaming/content layering plan](Plans/ASSET_STREAMING.md) — stable logical
  content identity, activation/application proof, deterministic Default/Replace/Add,
  passive-content validation, content-addressed blobs with realm-scoped ownership,
  source-independent byte delivery, immutable catalog generations, Cabin parity,
  complete-WorldPack streaming, and the first bounded runtime actor-model consumer.
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

- [World / WorldPack structural composition](Plans/WORLD_COMPOSITION.md) — canonical sparse
  multi-Pack World model: centered fixed 256×256-chunk Pack addressing, immutable structural
  manifest identity, prepared/resident state separation, atomic World-level composition
  publication, per-Pack overlays, seamless cross-Pack identity/seams, demand-based structural
  streaming, and the planned W1-W10 migration from the current one-Pack runtime.
- [Terrain / World T0 plan/status](Plans/TERRAIN.md) — deterministic world/chunk
  coordinates, packed terrain/statics, UO-like Multi structure composition,
  preserved 3D region/property seams, dynamic public chunk publication, bounded
  dirty history, client streaming, Godot batching, and the playable woods/cabin
  vertical.
- [Mine / subterranean excavation plan](Plans/MINE.md) — Mine-first proof of deterministic world geology, connected underground opportunity planning, coarse incremental excavation, finite depletion, labor/hauling/output, bounded replanning, abandonment, and the future Mine-to-Dungeon handoff.
- [Dungeon Core / Dungeon Lab plan](Plans/Content/UltraSim_Dungeon_Lab_and_Dungeon_Core_Plan_FINAL.md) — persistent autonomous dungeon-organism architecture. Dungeon now follows the Mine spatial/excavation proof and becomes the harder second consumer that proves what subterranean machinery is actually generic.
- [Concept Demo / live-test plan](Plans/CONCEPT_DEMO.md) — concrete future consumer
  for intentional surface absence, upper/lower terrain surfaces, cave and sky
  topology, far-distance AOI/residency transitions, and human-facing streaming
  evidence without redefining the owning Terrain/Physics contracts.

## Network

- [Network kernel foundation](../Kernels/Network/README.md) — dependency-clean
  bounded connection/packet ownership, generated protocol, lane contracts,
  security/replay, reliability, connected/shared UDP, Hosted admission/path
  mechanisms, and connection-owner handoff. Its canonical/default package is Full;
  optional same-identity ClientOnly/ServerOnly builds physically strip one Hosted
  role without changing Core protocol behavior.
- [Network testing support](../Kernels/Network.Testing/README.md) — separately
  compiled deterministic impairment, controlled handshake, loopback, and two-ended
  transport fixtures that are absent from every production Network build.
- [Network V3 current architecture/status](Plans/NETWORK.md) — authoritative current
  Network/replication policy. It records the permanent ownership/identity/proof/lane
  rules, cadence independence, the five-profile dispositions, P0.4 conservative
  selection, `ChunkPublication` semantics, rating-versus-stress distinctions, and
  the current consumption-ready stop.
- [PF2.5 replication reconciliation](Plans/PF2_5_REPLICATION_RECONCILIATION.md) —
  exact current-game state routing, P0.4 provenance, alternative-profile readiness,
  fixed-owner versus runtime-transfer requirements, PF3 observability, and the proposed
  `ServerPerformanceProfile/v1` policy boundary.
- [PF3 cadence and performance policy](Plans/PF3_CADENCE_PERFORMANCE_POLICY.md) — authenticated
  bounded execution policy, engine-neutral stock input scheduling, authority enforcement,
  render/input/authority/Network clock separation, and mismatched-cadence evidence.
- [Hosted Network H0](Plans/HOSTED_NETWORK.md) — accepted H0.0-H0.5 external UDP
  lifecycle: deployment pinning, stateless bounded cookies/rates, authenticated
  promotion, explicit IPv4 path ownership/rebinding, exclusive connection-owner
  packet handoff, dedicated-process composition, physical Hosted role ownership,
  and explicit deployment non-claims.

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
- [Physics P0](Plans/PHYSICS.md) records the accepted P0.0-P0.1d Simple Physics
  extraction, occupancy-aware relocation, swept blocking, support/clearance/steps,
  jump/gravity/fall/landing, and bounded human-play hardening, with richer contact
  response and alternate-provider work still stopped.
- [Client F0](Plans/CLIENT.md) records the frozen real-client shell/composition,
  authority-thread/local-session extraction, Direct Connect trust, settings/control-
  profile, world-presentation, and standard-UI convergence work through F0.5, the A0-derived
  F0.6 closure, M0.0 shared authority, and the remaining lifecycle/parity roadmap.
- [Client presentation/content architecture](Plans/CLIENT_PRESENTATION_ARCHITECTURE.md) records the
  pre-PF6 client-completion direction: permanent primitives, unified Local/remote content scopes,
  presentation precedence/fallback, removable Official content, atlas/native realization, UI/gump
  categories, declarative profiles, renderer independence, and the future vegetation ownership seam.
- [Client completion campaign](Plans/CLIENT_COMPLETION_PLAN.md) records the active F1.0-F1.12
  execution order and exit gates before PF6 external human validation.
- [Asset Streaming A0](Plans/ASSET_STREAMING.md) records the passive content package,
  activation proof, CAS/cache, hosting/source, Default/Replace/Add, catalog/resource
  lifetime, Cabin parity, complete-WorldPack canary, Contentless Online, and deferred
  executable-extension boundary.
- [Mine](Plans/MINE.md) is the planned first implementation consumer for subterranean spatial planning/excavation. It owns geology, prospecting/knowledge, finite depletion, labor/hauling/output, and persistent mine lifecycle while deliberately deferring generic extraction until Dungeon becomes the second consumer.
- [Dungeon Core / Dungeon Lab](Plans/Content/UltraSim_Dungeon_Lab_and_Dungeon_Core_Plan_FINAL.md) remains implementation-ready for the richer autonomous dungeon organism, but its spatial planning/build checkpoints now follow the Mine proof and must reuse/extract only the common invariants demonstrated by both consumers.
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
