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

- `ecs-v2-v3-3.4.2-*` — current cross-generation ECS control;
- `ecs-v3-3.4.1-persistence-*` — current ECS persistence evidence;
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
