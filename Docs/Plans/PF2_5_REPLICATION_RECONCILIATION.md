# PF2.5 / P0.5A — Replication Ownership Reconciliation and PF3 Observability

**Status:** implementation and long-run evidence in progress on the accepted PF2 base
`6f0fa93fac1d13d30aeacc9af712c03f982f01d6`. The repaired pre-freeze candidate remains
`fdb468698d07ccafa17e1dcc51fc1546b26c9fee` unless this checkpoint finds a product-correctness
defect. This checkpoint changes diagnostics only: no public owner, cadence, AOI, lane, wire format,
aggregation, compression, or participant bound changes.

## Decision

PF2.5 selects **Option A**: keep `ChunkPublication` as the one current-game dynamic-public owner
through Concept Demo freeze. This is a product-composition decision, not a claim that chunk
publication is the fastest profile for every semantic class.

The result that should reopen this decision is a real post-freeze consumer—such as a materially
larger actor population, housing/mutable structural overlays, or another independently addressable
dynamic class—which fails an explicit CPU/freshness/wire budget under the current owner. A
benchmark-only winner is not enough. That consumer must bring the identity, proof, Client Core,
local-parity, and transition requirements that determine a safe fixed-owner composition.

## P0.4 provenance

Immediately before P0.4, normalized P0.3 evidence supported this *hypothesis*:

```text
immutable/frozen structural world              -> WorldPack / structural layers
independently addressable dirty public props   -> DirtyEntitySpatialFanout
mobile actors / moving observers               -> MaintainedEntityRelation
```

`DirtyEntityChunkBatchFanout` and `DirtyChunkSpatialFanout` were comparison/reference variants.
The hypothesis was never an accepted routing table. P0.4 commit `df7d2ca2` deliberately selected
the already-composed `ChunkPublication` path for all current dynamic-public state because:

- `WorldPublicEntityId` and the entity profiles' `ReplicationEntityId` were separate lineage
  domains;
- the real client understood chunk epoch/revision/snapshot/delta/absence semantics, not a merged
  cross-profile lifetime/order domain;
- application proof was profile-local and no client could deduplicate or order two owners;
- no transactional old-owner/new-owner handoff existed;
- there was no real multi-class product consumer proving that the extra composition was needed;
- implementing a universal or per-object composition layer would have expanded P0.4 beyond its
  measured selection scope.

Later work has solved the former absence of real consumers. Players, the Woodland actor, the
world/equipped Iron Sword, interactive props, nameplates, actor action/equipment metadata, native
actor realization, exported Online, multiplayer, bots, and PF2 all use the current path. It has
also made the immutable WorldPack versus mutable dynamic-public boundary concrete. It has **not**
created a shared cross-profile public identity, a merged client cache, cross-owner ordering/proof,
or duplicate-owner prevention. Those remain the decisive blockers for changing production now.

P0.4's `RuntimeOwnershipTransitionsSupported == false` speaks specifically to one logical object
changing owners. It does not logically prohibit several fixed owners for disjoint semantic classes.
Fixed concurrent composition is nevertheless not production-ready today because the common
identity/convergence seam described below still does not exist.

## Current production semantic routing

The table names identities at each boundary deliberately. Similar visible objects do not imply
identical storage, wire, or lifetime identities.

| Semantic state | Authority/domain | Current replication and proof | Game / wire identity | Local and remote consumers |
| --- | --- | --- | --- | --- |
| Player hot position/orientation | Game ECS + Physics; public latest state | selected `ChunkPublication`; chunk applied proof | persistent `Entity`/`PersistentEntityId` maps to generation-bound `WorldPublicEntityId`; wire also carries publication epoch, chunk key/revision | packet-free `OfflineDirectChunkRecipient` or `RemoteChunkWorldStateServer`; both converge on `ClientWorldSemanticCache`, then actor presentation/Godot |
| Woodland actor hot state | Game ECS; public latest state | same selected chunk owner | its own persistent entity and `WorldPublicEntityId` | same semantic cache and actor presenter |
| Actor identity, visibility and prefab | `GameWorldState` public descriptor | rows inside chunk snapshot/delta/absence | `WorldPublicEntityId` is the public lifetime; prefab is presentation selection, not identity | semantic cache -> `ClientActorPresentationState` -> Godot actor root |
| Display label/nameplate eligibility | Game cold public-presentation read model | reliable public-presentation metadata; explicit applied proof | delivery/revision plus the same `WorldPublicEntityId` | local semantic projection or `RemotePublicPresentationMetadataClient` -> bounded metadata state -> actor presenter/nameplate |
| Body, vertical phase, accepted action sequence, public Main Hand prefab | Game cold actor-presentation read model | same metadata path/proof, separate from hot movement | participant/control lineage is authoritative; metadata attaches by `WorldPublicEntityId` | actor-presentation state -> semantic animation mapper, socket/equipment visual |
| D0.7b model/animation bytes | authenticated Content catalog; passive presentation | not dynamic public replication; fresh `ContentSetApplied` proves exact realm catalog | logical content definition + package/file hash; actor prefab chooses logical presentation | local/preinstalled or CAS/content resolver -> Godot resource realization; actor root lifetime is retained |
| Iron Sword on ground | Game item/ECS + public descriptor | selected chunk owner for world presence/position | persistent item identity maps to a current `WorldPublicEntityId` only while public in-world | semantic cache -> ground-item presenter/hover/interaction nomination |
| Sword interaction target | Game world-action authority | command nominates generation-bound public target; server resolves and validates | wire `RemotePublicEntityTarget` is the current `WorldPublicEntityId`, never a private ECS handle | local adapter or remote playable command -> Game validates distance/state/ownership |
| Sword in backpack/equipped | Inventory authority; owner-private latest state | typed UI/model semantic path, not public AOI | persistent item identity and Inventory placement/equip slot; UI exposes bounded semantic element/action identities | local UI bridge or remote authenticated UI -> `UiClientRuntime`/Godot UI |
| Sword public equipped appearance | Game cold actor-presentation read model | reliable metadata/proof on the owning actor | public Main Hand prefab attached to actor `WorldPublicEntityId`; exact item/rolls remain private | actor presenter socket; never a separate pick/hover target |
| Inspection Properties | Game recipient-authorized cold metadata | public metadata only for disclosed world items; private Inventory model for owned item | stable typed property IDs; presentation order/labels are client policy | world tooltip and Inventory converge on the typed property formatter |
| Door, sign, telepad, Far gates and other interactive props | Game ECS/world-action authority; dynamic public latest state where visible | selected chunk owner; cold inspection/UI facts remain separate | persistent entity plus `WorldPublicEntityId` while disclosed | semantic cache supplies nomination/presentation; local/remote Game action validates; sign UI uses typed UI path |
| Terrain/base, detached terrain and frozen statics | World-owned immutable structure | WorldPack/content application, never dynamic-public profile | content SHA, `WorldPackFingerprint`, `WorldContentFingerprint`, chunk key | `BoundWorldPack` -> `ClientWorldSemanticCache` structural layer -> Godot renderer |
| Mutable structural overlay | no general Concept Demo overlay artifact exists yet | not silently represented by dynamic chunk publication | future owner/lineage must be explicit | future earned consumer |
| Playable OwnerState | Game control authority; owner-private latest state | `playable-owner-state` on OwnerState lane; no public proof | application epoch + control epoch + command/state sequence + public actor ID | local semantic result or `RemotePlayableClient`; client command completion uses `LastProcessedCommandSequence` |
| Inventory / private UI | Game Inventory/UI authority | authenticated typed UI operations/actions/results | UI definition identity, view lifetime, element/action IDs; model embeds authorized private item facts | local packet-free UI bridge or remote `RemoteUiClientBridge` -> same `UiClientRuntime`/Godot renderer |
| Spatial/Global Chat | Game Chat authority; ordered fact | Event occurrence + recipient applied proof | application epoch + recipient sequence; player occurrence may carry participant/public anchor | local or `RemoteChatClient` -> bounded Chat state -> feed/overhead projection |
| System/Test occurrence | authority-originated ordered fact | same Chat occurrence/proof, with no forged player speaker | recipient sequence and explicit system source | feed plus transient banner |
| Server Monitor | sanitized read-only Game/UI projection | typed UI semantics, owner-authorized | view/model revision and participant rows; no mutable admin identity | local/remote UI runtime -> Godot panel |
| Profile/content/application proof | deployment/application authority | reliable Control/Event protocol, outside public AOI | application realm/session epoch plus exact revision/hash/catalog fingerprint | Direct Connect composition gates semantic consumer activation |

Identity relationships are therefore:

```text
PersistentEntityId        durable Game identity (where assigned)
Entity                    one ECS-generation handle
WorldPublicEntityId       current Game public-presentation/interaction lifetime
ReplicationEntityId       retained entity-profile lifetime, not currently mapped into the above
Inventory identity        private persistent item + placement/equipment state
presentation delivery ID  proof lineage for cold metadata attached to WorldPublicEntityId
```

## Alternative-profile readiness

| Capability | Maintained Entity Relation | Dirty Entity Spatial Fanout |
| --- | --- | --- |
| Game source/AOI | production-shaped `GameProjectionService` + `GameInterestService`; maintained exact recipient/entity relations | production-shaped entity latest state, dirty-time spatial discovery and recipient topology reconciliation |
| Identity/lifetime | `StateEpoch` + `ReplicationEntityId` | `StateEpoch` + `ReplicationEntityId` |
| Knowledge/proof/retry | implemented, bounded, application-proved; snapshot/compaction recovery exists | implemented, bounded, application-proved; retry/epoch recovery exists (no separate chunk snapshot stage) |
| Generation/re-entry | covered in profile/integration tests | covered in profile/hostile tests |
| Remote server/client | `RemoteWorldStateServer` / `RemoteWorldStateClient` | `RemoteDirtyEntitySpatialFanoutServer` / wrapper over `RemoteWorldStateClient` |
| Local semantic oracle | profile-local offline/conformance traces | profile-local offline/conformance traces |
| Multi-recipient/bounds | implemented and benchmarked | implemented and benchmarked |
| Current product composition | **not composed** | **not composed** |
| Current Client Core/Godot cache | old entity-state client, not `ClientWorldSemanticCache` | same old entity-state client, not `ClientWorldSemanticCache` |
| Current actor/interaction/cold-metadata linkage | absent | absent |
| Disposition | maintained production-capable kernel alternative; product adapter missing | maintained production-capable kernel alternative; product adapter missing |

Neither alternative is obsolete, but neither can be selected by changing one enum. Current Online,
Complete, Direct Connect, bots, actor presentation, hover/interaction, and native rendering all
consume the chunk semantic cache and `WorldPublicEntityId` lineage.

## Fixed composition versus runtime transfer

**A. Fixed concurrent owners** is possible in principle without runtime transfer. It still needs:

1. a Game-owned stable public identity or explicit mapping shared by hot state, cold metadata,
   interaction nomination, Chat anchors, and actor presentation;
2. a fixed semantic-class-to-owner table established at composition/publication time;
3. a source-owner tag/epoch and one-owner admission invariant;
4. Client Core storage which merges disjoint owner streams into one ordered public lifetime;
5. application proof and reconnect/snapshot rules for every owner without confusing transport ACK
   with semantic knowledge;
6. local packet-free and remote adapters producing identical client meaning.

**B. Runtime cross-profile transfer** additionally needs an ownership epoch, a transactional
old-owner remove/new-owner present boundary, cross-source order/dedup rules under loss/reordering,
rollback/recovery when either half cannot publish, and snapshot/generation replacement semantics.
A dynamic-to-frozen move also crosses into WorldPack or an explicit mutable-overlay owner.

PF2.5 implements neither feature.

## PF2 message attribution and workload explanation

PF2.5 retains exact manifest-indexed per-message counters in both bot and server summaries. Every
descriptor records numeric ID, canonical key, version, lane, delivery/retention contract, sent and
received logical messages, application bytes, and encoded frame bytes. UDP payload remains a
connection/datagram metric because security/framing and aggregated physical cost cannot be assigned
honestly to one logical message.

The accepted PF2 broad totals already establish the important explanation. Moving-50 accepted
16,500 movement commands but visited 1,635 distinct chunks; Crowd-50 accepted 65,950 movement
commands but visited only 266 distinct chunks. Distributed movement therefore explored **6.15x**
as many distinct chunks and created much more chunk subscription/snapshot/delta/absence/proof work.
It produced 2,342,350,464 server-to-client UDP payload bytes versus Crowd-50's 1,911,382,410
(**1.226x**), despite Crowd accepting about **4.0x** as many movement commands. The cost is driven
by current chunk-publication audience/entry churn and changed shared pages, not command count alone.
The final evidence artifact records the exact message-ID split from new unchanged-cadence runs.

## Observability contract

Diagnostics are subsystem-owned immutable snapshots; there is no universal diagnostics runtime.

| Level | Cost/meaning |
| --- | --- |
| `Off` | no detailed timestamps; semantics and accounting remain intact |
| `Basic` | bounded service/tick interval and duration observations |
| `Detailed` | Basic plus named protocol, Game application, replication, client apply, command completion, and native presentation stages |

Hot recording uses owner-local primitive counters, fixed arrays, and finite histograms. It performs
no reflection, strings, dictionaries, global locks, or per-observation allocation. Snapshot/report
construction is cold. Network uses 64 logarithmic Stopwatch-tick buckets. Game, Client, bot and
native timing uses a 256-sample rolling percentile window with window-wide count/total/max. All
elapsed times use monotonic `Stopwatch`; no protocol message acquired a benchmark timestamp.

The finite vocabulary covers:

- native frame interval/duration, input events/samples, semantic resolutions, commands considered/
  submitted/suppressed, continuous/action-edge classification, presentation update time and actor/
  label/chunk/resource high-water;
- client Network pump interval/duration and protocol emit/select/encode/encrypt/send/receive/
  decrypt/admit/compaction stages;
- client receive processing, chunk decode+semantic-cache apply, proof drain, OwnerState receive/apply
  interval, simulation-tick gaps, and command-submit-to-semantic-owner-state completion;
- authority tick interval/duration/lateness, deadline misses, catch-up ticks and skipped opportunity;
- per-application receive/progress, command apply, proof processing, public-world drive, OwnerState
  publication, and ChunkPublication reconcile/schedule/progress/proof stages;
- protocol queue current/high-water for reliable, independent-unreliable, received, and in-flight
  work, plus existing retained-byte, retransmission, ACK, keepalive, lane/message/family accounting;
- process CPU per wall second, RSS/managed heap high-water, allocation delta, GC generations and
  thread high-water for server/bots/native clients.

Configured cadence and measured cadence remain distinct. Some clocks have no catch-up or skipped
semantics, so their corresponding fields are not invented. Cross-process public source-to-client
latency is not claimed: server queue stages and client receive/apply stages are separate. Command
latency is client-observable semantic completion through `LastProcessedCommandSequence`, never a
transport ACK.

## PF3 `ServerPerformanceProfile/v1` design

PF3 should add a finite, canonical, application-session-scoped profile only when it implements the
policy. Recommended classification:

| Field | Class |
| --- | --- |
| `SimulationFrequencyHz`, `NetworkServiceCadenceHz` | informational description of server policy; client cannot select it |
| `MaximumInputHz` plus bounded burst/window | hard admission/security limit enforced server-side |
| `RecommendedInputHz` | stock-client transmission recommendation, always bounded by the hard maximum |
| expected OwnerState and Immediate/Near/Far delivery cadence | informational/recommendation and diagnostics expectation, not a freshness guarantee |
| interpolation-buffer hint | optional stock-client presentation recommendation |
| protocol/compression requirement | belongs in negotiated protocol capability/manifest unless it is genuinely application policy |

The exact canonical bytes, revision, and hash should be authenticated after deployment trust and
fenced by application realm/session epoch. Hard policy does **not** require user consent. Performance
recommendations are not control bindings/camera preferences and should not reuse F0.3c's human
consent decision; the client settings resolver may apply them below user preference only where the
setting remains user-owned, while hard transmission limits always cap a modified client.

`MaximumInputHz` should be enforced by a server-owned per-control-session token bucket (or exact
fixed-window equivalent) using monotonic time, a small explicit burst capacity, saturating bounded
counters, and rejection/rate-limit diagnostics. Tokens and debt must be fenced to the application
and control epoch and discarded on retirement. Stale replay remains rejected by existing sequence/
session checks before it can earn new authority. PF2.5 adds attempted/admitted/rejected and queue
measurement seams but does not add this policy.

Never place executable policy, arbitrary script, client render FPS, required user key bindings,
private deployment capacity, or a promise of exact packet arrival time in this profile.

