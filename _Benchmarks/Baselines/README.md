# Accepted benchmark artifacts

This directory contains immutable machine-readable evidence for accepted ECS,
Runtime, persistence, S0, comparison, experiment, and stress checkpoints.

JSON filenames and embedded workload/version metadata identify each result. Git
history records when and why it was accepted. This README intentionally does not
repeat every artifact's numbers or revision narrative.

## Policy

- Never overwrite, rename, or silently regenerate an accepted artifact.
- New evidence receives a new descriptive filename and capture date.
- Preserve old JSON for longitudinal analysis even after its implementation
  checkpoint is historical.
- Do not treat a result as comparable unless its embedded semantic classification
  admits a ratio.
- Do not copy performance tables into multiple Markdown reports.
- Record cadence, playback/adaptation, capacities, workers/process order,
  allocation scope, runtime/hardware, sample counts, and canonical validation.

Unless a workload says otherwise, Runtime performance evidence uses fixed 60 Hz,
1x playback, variable cadence disabled, and adaptive cadence disabled.

## Current anchors

Useful current anchors can be located by their embedded workload or filename:

- `client-f0.4-actor-presentation-2026-09-07.json` — bounded renderer-neutral actor
  definitions and cold per-instance metadata beside unchanged hot public movement, ABA-safe exact
  identity/application proof, shared Local/Online Godot realization, client-local Ctrl reveal,
  anchored overhead speech, primitive Contentless Online actors, and the actual exported cold/warm
  product journey. It also records the bounded round-robin public-scheduling hardening earned by
  real reliable-queue pressure;
- `client-f0.3c-profile-consent-2026-09-07.json` — authenticated protocol-v19 finite
  control/camera profile advertisement, exact realm/revision/hash consent persistence, application
  proof gating, layered settings/reserved-control preservation, and the actual exported Online
  cold/remembered/changed-profile product journey;
- `content-a0.8-native-export-closure-2026-09-07.json` — follow-up after the matching Godot Mono
  export templates became available: the actual Online Linux export, exact exported managed and
  content closure, and the full cold/warm external-deployment journey executed by that exported
  binary. It also freezes the adjacent one-project solution as a required Godot 4.7 .NET export
  input and promotes export inspection/execution into the permanent verifier;
- `content-a0.8-contentless-online-2026-09-06.json` — the physically Online-only native Godot
  product's external cold/warm journey against a separately restarted Direct Connect deployment:
  empty local catalog, authenticated World/UI acquisition, fresh application proof, World-owned
  binding, native primitive realization, gameplay/UI/Chat canaries, warm zero-byte reopen, and
  inspected dependency/payload absence. It records the missing local Godot export templates rather
  than claiming an export artifact that was not produced;
- `content-a0.7-worldpack-streaming-2026-09-06.json` — exact complete Concept Demo
  WorldPack acquisition through server-hosted HTTP and the applied realm catalog, one-allocation
  World stream open, typed pack/content binding, private atomic 162-chunk client publication,
  complete local semantic parity, capacity rejection with zero publication, and unchanged
  dynamic-public application;
- `content-a0.6-ui-schema-convergence-2026-09-06.json` — canonical typed `.usui` Inventory
  definition identity across rooted local, warm CAS, server HTTP, and external HTTPS, plus the
  authenticated content-backed HotOpen/action/application-proof canary;
- `content-a0.5-cache-hardening-2026-09-06.json` — bounded ordered mirror retry, canonical
  manifest/content-addressed blob acquisition, one-file SHA delta reuse, corrupt blob/manifest
  atomic repair, interrupted-source transactionality, Optional fallback/explicit hot upgrade,
  connection-time private DNS rejection, and real granular server-HTTP Cabin Add parity;
- `content-a0.4-remote-source-parity-2026-09-06.json` — frozen Cabin
  Default/Replace/Add semantic matrix across zero-transfer Default, rooted local package files,
  bounded server-hosted HTTP, and external TLS/HTTPS. It records canonical envelope/payload bytes,
  exact package/manifest/file/plan/catalog identities, source-policy bounds, and continued remote
  Game-consumer acceptance. The network observations use real loopback transports and are not
  public-WAN/CDN load or platform trust-store evidence;
- `network-hosted-h0.1-external-admission-*` — first real externally sourced
  IPv4 listener boundary: deployment-key pin copy, listener-lifetime stateless
  cookie, fixed per-source rate table, non-amplifying challenge, bounded expiring
  candidates, hostile containment, and zero warmed allocation. It stops before
  authenticated session promotion and is not WAN/CCU/N1 adoption evidence;
- `network-hosted-h0.5-dedicated-sharded-*` — one dedicated server process,
  independent externally admitted client processes, fixed connection-owner
  threads, exact packet-lease demultiplexing, H0.3 path rebind, N1 cross-chunk
  public convergence, mixed OwnerState/Event/Control/application-proof traffic,
  fresh reconnect, and split server/client/process evidence. Together with the
  prior N1.4 adoption matrix it closes current-game profile selection, but it is
  localhost evidence rather than public-WAN, production-CCU, N1.5, 2K/4K, or N0
  deletion authority;
- `network-p0.3g-dirty-chunk-*-2026-08-24.json` — clean Dirty Chunk-only sparse
  and dense scale reruns after bounded equal-lineage source-page reuse and recipient
  multi-chunk semantic packing. Entries retain independent proof and the profile retains
  dirty-time discovery with no global Chunk-to-Recipient graph. These are Game-core and
  canonical-codec-byte artifacts, not hosted transport or production-CCU evidence;
- `network-p0.3h-*-2026-08-24.*` — accepted Network ready-state selection A/B,
  cap-eight ChunkPublication regression control, and 270-tick <=4 ms server-role
  rated sweep. The selector retains per-lane reliable heads, IndependentState ready
  bitsets, and priority-ready lanes without changing Game or lane semantics;
- `network-p0.3i-chunk-proof-envelope-*-2026-08-24.*` — accepted MTU-safe
  reverse proof-message packing for ChunkPublication and DirtyChunk. Entries remain
  independently fenced and applied; causal and probes-off whole-flow controls retain
  semantics while separating proof-message, frame, datagram, byte, and CPU effects;
- `network-p0.3j-rated-server-role-probes-off-2026-08-24.*` — 270-tick cap-one
  rated whole-flow closure without diagnostic protocol/listener stage timing. It
  records the strict first-failure p95 boundary and supersedes only P0.3H's absolute
  rating, not its ready-selector causal evidence;
- `network-p0.3k-rated-server-role-*-probes-off-2026-08-24.*` — controlled 16 ms
  versus 33 ms internal semantic-cadence rated sweeps. The 33 ms capture records the
  derived `1/1/2` chunk cadence, matched-population CPU/traffic/reservation effects,
  and the strict first-failure 4 ms server-role boundary;

- `ecs-v2-v3-3.4.2-*` — current cross-generation ECS control;
- `ecs-v3-3.4.1-persistence-*` — current ECS persistence evidence;
- `terrain-t0.0-spatial-contracts-*` — accepted dependency-neutral integer
  spatial math and fixed game-position profile evidence, including warmed
  allocation for normalization, reconstruction, key/hash/order, AABB distance,
  and terrain-cell lookup;
- `terrain-t0.1-world-spatial-index-*` — accepted maintained dynamic membership
  split costs, exceptional full-rebuild cost/allocation, member reads, and warmed
  zero-allocation evidence for ordinary post-step movement/lifecycle maintenance;
- `terrain-t0.2-height-encoding-*` — fixed 16 m/0.5 m terrain geometry,
  global-vs-per-chunk-base height range/error evidence, synthetic multi-datum
  height/material distribution, reconstructed shared-edge duplication/equality,
  compiler scratch accounting, and allocation-free plane codec timings;
- `terrain-t0.3-worldpack-*` — canonical sectioned WorldPack compilation,
  bounded memory/file opening, immutable hash identity, exact container/directory
  accounting, and allocation-free random/copy base-terrain reads;
- `terrain-t0.4-query-mesh-*` — exact integer base-height/material queries,
  deterministic fixed-grid mesh payload identity, neighbor-aware seam frames,
  fixed output accounting, and warmed allocation-free timings;
- `terrain-t0.5-frozen-structures-*` — canonical optional frozen-static and
  static-Multi sections, stable cross-chunk logical identity, borrowed packed
  reads, compatible render-group extraction, and bounded transactional sparse
  plus dense multi-hit exact static-bounds query evidence. The `hardened`
  artifact adds the persistent yaw contract and dense result-write observation;
- `s0.8-integrated-acceptance-cadence-independent-*` — accepted integrated
  Runtime/game/persistence/cadence gate;
- `network-n0.1-game-projection-100k-*` — canonical changed-state capture and
  packet-free OfflineDirect semantic-apply evidence;
- `network-n0.2-kernel-foundation-*` — fixed-runtime generated codec, packet pool,
  producer ring, deterministic datagram, and connection reuse evidence; the
  `hardened` artifact includes checked signed codecs and the versioned fingerprint;
- `network-n0.3-secure-protocol-*` — fixed-runtime secure/reliable loopback delivery,
  fragmentation, and impaired convergence evidence; the `replay-hardened` artifact
  separates authenticated packet replay from transport-delivery acknowledgement,
  while `corrective-hardening` preserves the final accepted observation after
  whole-window liveness, message-family state isolation, and inbound-budget fixes.
  It is additional immutable evidence and does not replace the earlier performance
  reference;
- `network-n0.4-application-loopback-*` — paired OfflineDirect/secure-loopback
  CharacterSelect and Inventory workflow plus established semantic UI
  action→authority→delta→apply-ack observations;
- `network-n0.5-real-udp-application-*` — paired secure ProtocolLoopback/connected
  localhost UDP evidence for the same application semantics;
- `network-n0.6-controlled-input-owner-state-*` — paired complete input→authority
  tick→projection→client reconciliation evidence. The `semantic-hardened`
  artifact supplements the original accepted run after the Input supersession,
  complete-tombstone, and true client-local slot corrections;
- `network-n0.7-maintained-interest-*` — maintained region/relation/pending-state
  scheduling and immediate application-ACK evidence. The reference, dirty,
  region-edge, density, and fanout artifacts form one sensitivity matrix rather
  than competing replacement baselines. The `hardening-reference` artifact adds
  independent WorldState delivery semantics and all-recipient same-cell movement;
- `network-v3-sparse-current-topology-region-sensitivity-2026-08-17.*` — current
  send+receive sparse normalization plus cold exact-scope census and fixed-policy
  16/10/8 m region-edge sensitivity at 1,024 and 4,096 clients. It identifies
  10 m as the candidate CPU/memory knee;
- `network-v3-sparse-10m-confirmation-capacity-2026-08-17.*` — alternating
  probes-off/deep 16 m versus 10 m confirmation, exact-tier × maintained-tier
  accounting, and the separately isolated `legacy128` versus `current10`
  reservation experiment. This artifact adopts 10 m and its derived bounds only
  for the admitted sparse comparison profile;
- `network-v3-sparse-exact-admission-2026-08-17.*` — canonical 62 Hz/35%-active
  coarse-envelope versus exact-distance admission at 1,024/2,048/4,096. Exact
  admission removes coarse-only relations and lowers wire work, but its full
  candidate reconciliation raises interest-update and server-role p95; it is
  retained only as an opt-in diagnostic, not a production policy;
- `network-v3-sparse-worldstate-compaction-2026-08-17.*` — 4,096-client,
  15-logical-minute `current10` roaming acceptance for client-originated
  known-identity pressure, fixed round-robin baseline admission, repeated
  atomic compaction, bounded occupancy, exact convergence, and zero final
  maintenance debt. It is an opt-in lifetime checkpoint, not a 60 Hz CPU claim;
- `network-n0.8-atomic-world-baseline-*` — paired secure ProtocolLoopback/connected
  localhost UDP evidence for one bounded segmented baseline, private atomic client
  publication, application acknowledgement, allocation, and reservation;
- `network-n0.9-integrated-topology-acceptance-*` — cold rotated-order
  OfflineDirect/secure ProtocolLoopback/connected localhost UDP evidence with
  Admission/UI, input, owner/WorldState, Event, durable replacement, fresh-epoch
  recovery, continued authority, and deterministic shutdown composed at once.
  The `corrective-hardening` artifact is the protocol-v8 observation after
  reliable baseline cancellation, exact WorldState parity, participant-scoped
  Event retention, reclaimable client Event storage, and ACK/timeout liveness;
- `terrain-t0.6-chunk-publication-*` — game-owned post-ECS public projection,
  bounded observer-independent history, direct committed page reads, canonical
  snapshot/delta copy and semantic encoding, plus synthetic eager/on-demand
  construction-policy evidence. The `hardening` artifact adds chunk-bound cursor
  lineage, the 48-byte tick-free semantic header, and a cold 100K-slot reservation
  observation while preserving the original implementation-stop artifact. It is
  not Network N1 or client-streaming evidence;
- `terrain-t0.7-chunk-interest-local-oracle-*` — exact circular chunk/AABB
  selection with consumer-owned hysteresis, canonical complete capacity results,
  and fixed-capacity packet-free snapshot/delta/history-fallback convergence.
  The `1/2/4` cadence and roughly 500-key first-game scope are local evidence,
  not the frozen N1 scheduler, server fanout, or Godot resident-cache policy.
  The `hardening` artifact preserves the later fail-closed semantic-admission
  checks and is supplementary rather than a replacement for the implementation
  stop observation;
- `terrain-t0.8-client-semantic-cache-*` — Release/tiering-disabled production
  client semantic-cache lifecycle, borrowed reads, hashing, delta application,
  allocation, and client-resident reservation evidence. The `hardening` artifact
  adds the 1/32/128/512-resident and near-full-knowledge matrix after bounded
  identity/location indexing removed population-shaped public-apply scans. It
  excludes Network N1 and Godot resource cost;
- `terrain-t0.8-godot-world-streaming-*` — native Godot headless bounded-walk
  evidence for independent semantic/render/visibility state, grouped nodes,
  floating-origin presentation, frame work budgets, and complete resource
  retirement. The `hardening` artifact adds heterogeneous terrain/static/Multi/
  public content, public-only refresh, independent private render slots, expected
  capacity rejection, extreme-Y precision, longer churn, and native monitor
  observations. The separate `sliding-render-window-hardening` artifact proves
  X/Z strip diffs, long-walk private-slot reuse, player-local integer rebasing,
  camera no-op behavior, and a cold R=16 capacity shape. Its Debug first-use
  tails and minimal R=16 row profile are not exported-game/GPU or production
  view-distance claims;
- `terrain-t0.10-retained-direct-*` — the first persistent Direct-locomotion
  checkpoint: exact 20/60/120 Hz fixed-step meaning from one retained start
  command per run, zero-state stop, discrete Q15 unit-vector validation, and
  control-session epoch/replay fencing. The input-rate hardening artifact holds
  authority at 60 Hz while command delivery runs at 20/60/120 Hz and proves
  latest-admitted-state-before-step behavior. It preserves the joined native
  seam but is not Destination, camera/control-profile, launchable-client,
  broad-matrix, or N1 evidence;
- `terrain-t0.10-destination-*` — explicit command-level `Unchanged/Stop/Direct/
  Destination` mutation semantics plus the first latched, exact-arrival,
  straight-line terrain-following Destination authority. The initial artifact
  proves sampled exact arrival, supersession, action composition, invalid-target
  preservation, session cancellation, and non-durable restore behavior; it does
  not by itself prove equal in-flight progress;
- `terrain-t0.10-destination-temporal-hardening-*` — additive original-route
  cumulative-progress evidence. A still-in-flight non-cardinal route has exact
  equal X/Z after one authoritative second at 20/60/120 Hz, then exact arrival,
  without claiming pathfinding, control-profile, launchable-client, or N1
  completion;
- `physics-p0.0-simple-seam-*` — the semantic-extraction rerun after moving the
  accepted endpoint resolver into concrete generation-owned UltraSim Simple
  Physics. It preserves the integrated canonical hash, 20/60/120 Hz motion,
  effective door/chest collision, terrain-only relocation, bounded failure, and
  0 B warmed allocation. It is not sweep, relocation occupancy, support/step,
  native-backend, provider-replaceability, or richer-physics evidence;
- `physics-p0.1a-occupancy-relocation-*` — first-class clear/blocked/unprepared
  occupancy over the same endpoint oracle plus the intentional relocation behavior
  change. Blocked relocation preserves position and retained locomotion; a clear
  relocation commits and retires locomotion. It remains endpoint-only evidence,
  not sweep, support/step, actor-collision, spawn-policy, or provider evidence;
- `physics-p0.1b-swept-motion-*` — bounded exact swept-AABB blocking over the
  existing upright body and prepared static/projected-dynamic bounds. It proves
  thin-static/dynamic tunneling prevention, bidirectional diagonal-corner blocking,
  half-open contact, exact broadphase filtering, Direct/Destination policy, bounded
  failure, and 0 B warmed allocation. It is not support/step, sliding, continuous
  terrain-support, actor-collision, or provider evidence;
- `physics-p0.1c-support-clearance-*` — actual-height BaseTerrain/terrain-shell
  support selection, nearest-support occupancy, five-point footprint validation,
  Bottom-face clearance, explicit body-profile step-up/down, bounded grounded path
  sampling, intentional-void versus unavailable classification, transactional
  relocation, generation replacement, 20/60/120 Hz-equivalent agreement, and 0 B
  warmed allocation. It is not gravity, jump/fall/landing, sliding, arbitrary
  triangle time-of-impact, static/dynamic support, or provider evidence;
- `physics-p0.1d-vertical-motion-*` and later `physics-static-edge-*` /
  `physics-airborne-contact-terrain-fixture-*` hardenings — Game-owned vertical
  state, jump/gravity/landing/recovery, static-top support, independent feet/body
  footprints, bounded edge release, and the current captured Direct takeoff
  trajectory. The latest artifact adds a nine-chunk exactly flat terrain canary,
  thick cave geometry, and a warmed 0 B vertical-component decomposition that
  prevents a wall-blocked horizontal request from suspending gravity or producing
  a later snap. It does not claim general sliding, capsule contact, moving support,
  actor collision, or alternate-provider completion;
- `terrain-t0-acceptance-matrix-closure-*` and
  `terrain-t0.10-acceptance-closure-*` — cumulative T0.0-T0.10 profile mapping
  plus the current final Release integrated/native rerun. They freeze the
  launchable woods/cabin boundary without relabeling cold headless maxima as
  exported-GPU tails or rerunning unchanged component artifacts;
- `network-n1.0-offline-direct-chunk-recipient-*` — packet-free game-owned
  subscription/cursor reconciliation over accepted T0 interest/publication,
  including stable/crossing scale, cadence/no-change/absent reads, reservation,
  and warmed allocation. It is not wire, N0 replacement, public WAN, or accepted
  sparse-CCU evidence;
- `network-n1.1-canonical-chunk-codecs-*` — canonical bounded snapshot/delta/
  absence/application-proof wire schemas, hostile transactional codec gates,
  exact manifest/schema fingerprints, message sizes, and warmed zero-allocation
  encode/decode timings. It retains N1.0 controls but is not transport,
  loss/reorder, N0 replacement, public WAN, or production-CCU evidence;
- `network-n1.2-independent-chunk-delivery-*` — real authenticated
  independent-unreliable ordinary chunk delivery envelope, including the exact
  default 1,405-byte unfragmented message ceiling, 30-row/1,372-byte admitted
  delta, first rejected size, and bounded ticket-ledger reservation. Permanent
  tests own loss/duplicate/reorder/corruption and application-proof convergence;
  the artifact is not N1.3 snapshot recovery, N0 replacement, public WAN, or a
  production-scale claim;
- `network-n1.3-atomic-chunk-snapshot-recovery-*` — the bounded exceptional
  recovery envelope over reliable ordered Bulk while retaining the canonical
  N1.1 snapshot body: 64-row body bytes, MTU-safe segment payload/count,
  Begin/segment/Commit/Cancel widths, and server-adapter reservation. Permanent
  tests own atomic commit, absence/snapshot lineage, timeout, history advance,
  impairment, proof retry, and ordinary-WorldState independence; the artifact is
  not N1.4 adoption, N0 replacement, public WAN, or production-scale evidence;
- `network-n1.4-remaining-adoption-matrix-*` — the compact N1.4 scheduler's first
  separated level-1 server-semantic and level-2 secure deterministic replay
  evidence, plus dense populated-chunk recovery and attributed distributed/
  synchronized topology paths. It preserves zero warmed allocation and records
  the still-open crossing, mixed-private, socket/sharded-client, N0-adoption, and
  2K/4K gates; it is not an N1.5 or production-CCU artifact;
- `network-n1.4-final-adoption-gates-*` — representative independently phased
  boundary/corner/teleport topology, mixed WorldState/OwnerState/Event/Control
  replay with disclosure checks, and a real shared UDP listener with connected
  in-process clients. It explicitly leaves a genuine dedicated-server/sharded-
  client run to the separately authorized hosted-listener/security lifecycle and
  is not N1.5, N0 deletion, public-WAN, or production-CCU evidence;
- `network-v2-v3-bot-n010-entry-*` — first 32/64/128/256 owner-movement matrix
  in both isolated process orders. Canonical identity/input-sequence/velocity
  matches at every stage. Ratios remain provisional because V2 owns one shared
  server socket while V3 owns one connected UDP pair per bot. The observation
  includes stage-start cold work and is an N0.10 design input, not a frozen
  shared-listener performance target;
- `network-v2-v3-bot-n0101-shared-listener-*` — the bounded one-server-listener
  follow-up using the same bot script, capacities, protocol, and both process
  orders. It preserves exact canonical parity and shows a modest ~6.9% V3 slope
  improvement, reduced 256-client retransmission pressure, and higher managed
  demux reservation. It supplements rather than replaces the connected-pairs
  entry artifact;
- `network-v3-spread-dense-acceptance-*` — the first bounded 400-recipient
  observer-heavy V3-only acceptance observation. The `event-causal-pre-specialization`
  artifact preserves Full/WorldState-only/Event-only attribution before the
  Event-ledger change; `event-causal-participant-index` is the exact post-change
  causal control; and `event-index-hardened` is the probes-off dense/spread Full
  rerun. These supplement rather than rewrite the original artifact;
- `network-v3-dense-worldstate-causal-*` — the four-way Full/WorldState/Event/
  updates-only attribution before and after the per-tier bundle cursor. The
  `tier-cursor-hardened` artifact is the matching probes-off observation. These
  preserve the 80.1% pending-examination reduction and explicitly leave the
  remaining protocol/application-ACK and canonical relation-maintenance
  candidates open;
- `network-v3-dense-worldstate-application-ack-census-reliable-*` — the negative
  natural reliable-batch census; `reconstructible-ack-causal` is the matched
  protocol-v9 independent-unreliable proof experiment and
  `reconstructible-ack-hardened` is its probes-off observation. Together they
  preserve why no batch message was added and why only reconstructible
  WorldState application proof moved off reliable Control;
- `network-v3-dense-stable-far-fast-path-causal-*` — the one-pass stable
  same-lifetime/same-region `Far→Far` causal result, including 100% eligibility
  and the exact avoided visit counts; `stable-far-fast-path-hardened` is the
  probes-off observation;
- `network-v3-dense-bundle-expiration-*` — the matched full-array expiration
  census, exact-time sweep-cache causal result, and probes-off hardened
  observation. These preserve the 97,912-request/123-time diagnosis and leave
  eligibility-aware pending-debt organization as a separate question;
- `network-v3-dense-pending-eligibility-*` — the pending CPU/deadline census,
  rejected ready/deferred-wheel causal and probes-off artifacts, and the final
  attribution-only hardened observation. The rejected files are negative
  evidence, not accepted production baselines; the wheel implementation was
  removed after it reduced scans but worsened normal-path CPU;
- `network-v3-dense-receive-batch-*` and
  `network-v3-dense-native-receive-*` — the send-only receive-drain census,
  Linux `recvmmsg` causal/probes-off controls, and protocol-role tail
  observations. They preserve both the native-call collapse and the remaining
  p99 variability rather than promoting one favorable run to a universal tail
  claim;
- `ecs-extreme-sphere-*` — extreme CPU/Godot scale evidence.

Earlier `3.0.x`, `3.1.x`, experiment, and soak files are intentionally retained
as historical measurements, not active design documentation.

See [benchmark methodology](../README.md) for execution and acceptance rules.
