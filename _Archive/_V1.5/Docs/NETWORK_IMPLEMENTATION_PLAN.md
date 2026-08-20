# Network vNext - Implementation Plan

This plan operationalizes `NETWORK_PRD.md` into phased execution tasks, acceptance gates, and change-tracking rules.

Status: **in progress**.

---

## 1) Scope Lock

Implement all requirements in `NETWORK_PRD.md` (Phases 1-5), with strict module boundaries:

- `ECS/` does not reference `Network/`.
- `Network/` does not reference `ECS/`.
- integration/bridge code lives in:
  - `Server/Engines/Networking/`
  - `Client/Engines/Networking/`
  - `Shared/Engines/Networking/` (or `Common/Engines/Networking/`)

---

## 2) Baseline Invariants (Must Stay the Same)

These are expected to remain stable through implementation:

- Barrier authority model (`World` mutation gateway, no out-of-band writes).
- Engine-agnostic core modules (`ECS/`, `Network/`).
- Tick-driven deterministic server flow (`poll -> decode -> pre-barrier apply -> sim -> replicate -> flush`).
- Per-connection budget model (not global-only).
- Observability-first requirement (stats are first-class, not optional afterthought).
- Hot-path discipline (no avoidable allocations, no interface boxing in packet fast paths, no per-packet wall-clock calls).

---

## 3) Expected Mutation Zones

These are expected to change during implementation:

- `Network/` folder (new runtime implementation).
- `Server/Engines/Networking/`, `Client/Engines/Networking/`, and `Shared/Engines/Networking/` bridge layers for ECS+Network integration.
- Test projects/harnesses for unit/integration/load/soak.
- PRD addenda for concrete defaults discovered during implementation.

---

## 4) Tracking Rules (What Changed vs What Stayed)

For each phase completion, record:

1. **Stable assumptions** kept unchanged (from section 2).
2. **Mutations** (behavioral/API/schema changes introduced).
3. **Why changed** (constraint, perf, correctness, operability).
4. **Evidence** (tests, perf numbers, soak logs).

Recommended per-phase note format:

```md
## Phase X Review
- Stable: ...
- Mutated: ...
- Rationale: ...
- Evidence: ...
- Follow-ups: ...
```

---

## 5) Work Breakdown by Phase

## Phase 0 - Preflight Decisions (Design Freeze)

Objective: lock concrete defaults before coding.

Tasks:

- [ ] P0.1 Define replay window size policy (ticks + memory cap per connection).
- [ ] P0.2 Define per-connection budget defaults (bytes/tick, packets/tick, lane priorities).
- [ ] P0.3 Define overflow policy defaults (defer/drop/coalesce by lane).
- [ ] P0.4 Define stats sampling/cardinality defaults for production.
- [ ] P0.5 Define compatibility gate constants (protocol/schema/content hash policy).
- [ ] P0.6 Define framing/header constants (length prefix, max frame size, validation limits).
- [ ] P0.7 Define monotonic timing source policy (timeouts/retransmits/stat windows).

Acceptance gate:

- [ ] Documented defaults added/confirmed in `NETWORK_PRD.md`.
- [ ] No unresolved "must decide before implementation" items remain.
- [ ] Frozen-registry approach is documented (startup build -> immutable tables).

Expected mutation:

- `NETWORK_PRD.md` only.

Expected stable:

- Module boundaries and barrier model.

---

## Phase 1 - Foundation

Objective: runnable network core without socket complexity.

Tasks:

- [x] P1.1 Implement `INetworkTransport` core contracts.
- [x] P1.2 Implement loopback transport with buffer lifetime contract.
- [x] P1.3 Implement packet registry and O(1) dispatch table.
- [x] P1.3a Implement O(1) packet type -> ID/channel metadata lookup (no linear scans).
- [x] P1.4 Implement typed packet serialization/deserialization contracts.
- [x] P1.5 Implement handshake/version/content-hash gate.
- [x] P1.6 Add baseline transport/protocol stats + poll/decode/flush timings.
- [x] P1.7 Implement length-prefixed framing parser with ring-buffer/segmented reassembly.
- [x] P1.8 Add unknown/invalid frame policy hooks and counters.

Acceptance gate:

- [ ] Unit tests: packet roundtrip + unknown packet handling + registry collisions.
- [ ] Unit tests: framing parser correctness (partial/multi-frame/malformed/wrap-around cases).
- [ ] Integration tests: handshake pass/fail matrix (version/hash mismatch cases).
- [ ] No steady-state allocations in hot poll/decode dispatch loop (profiling evidence).

Expected mutation:

- `Network/Transport/*`
- `Network/Protocol/*`
- `Network/Session/*` (minimal handshake path)
- `tests/*` for packet and handshake tests

Expected stable:

- No direct dependency from `Network/` to `ECS/`.

---

## Phase 2 - Server Integration

Objective: server session lifecycle and barrier-safe command ingestion.

Tasks:

- [x] P2.1 Implement connection/session manager with lifecycle states.
- [x] P2.2 Implement inbound command queue pipeline feeding pre-barrier apply.
- [x] P2.3 Implement outbound queue flush hook at end of host tick.
- [x] P2.4 Add session lifecycle counters with reason codes.
- [x] P2.5 Add keepalive/auth timeout and duplicate session policy behavior.
- [x] P2.6 Enforce session boundary contract (no gameplay object references inside `Network/` session state).
- [x] P2.7 Add stable session IDs to logs/stats/event surfaces.
- [x] P2.8 Add dual-endpoint loopback transport pair with directional latency/jitter/drop simulation for bridge integration and harness testing.

Acceptance gate:

- [ ] Integration tests: connect/auth/ready/disconnect paths + timeout paths.
- [ ] Integration tests: inbound packet -> command -> barrier apply (no out-of-band write).
- [ ] Structured session stats/logs emitted and parsable.
- [ ] Verification pass confirms `Network/` does not contain ECS/gameplay object references.

Expected mutation:

- `Network/Session/*`
- `Server/Engines/Networking/*` and `Shared/Engines/Networking/*` bridge layers for host tick integration
- integration tests

Expected stable:

- ECS mutation gateway is still exclusively `World` APIs.

---

## Phase 3 - Replication

Objective: scalable deterministic replication pipeline.

Tasks:

- [x] P3.1 Implement dirty drain input contract consumption.
- [x] P3.2 Implement interest filter interface (spatial + explicit subscriptions).
- [x] P3.3 Implement per-connection cursors/replay window store.
- [x] P3.4 Implement per-connection budget lanes + overflow policy.
- [x] P3.5 Implement deterministic encode queue path.
- [x] P3.6 Add replication stats (dirty/interested/encoded/deferred/dropped/bytes).
- [x] P3.7 Implement interest-group/shared-delta fanout path (build once, send many).
- [x] P3.8 Enforce stable deterministic encode ordering within a tick.
- [ ] P3.9 If UDP reliable lanes are enabled: implement piggyback ACK bitfield and per-connection RTT/RTO retransmit policy.

Acceptance gate:

- [ ] Integration tests: deterministic tick output under same input stream/config.
- [ ] Load tests: budget pressure does not stall tick; overflow counters increase explicitly.
- [ ] Evidence that per-connection limits are enforced independently.
- [ ] Load tests show shared-delta fanout reduces duplicate per-client filter/encode cost.
- [ ] If UDP reliable lanes are enabled: sequence wrap-around, duplicate suppression, and retransmit behavior verified.

Expected mutation:

- `Network/Replication/*`
- `Network/Protocol/*` (delta/snapshot payloads)
- load/soak tests and benchmark harness updates

Expected stable:

- Replication still follows barrier and queue discipline.

---

## Phase 4 - Client Runtime

Objective: prediction/reconciliation/interpolation on projected client world.

Tasks:

- [x] P4.1 Implement input send with sequence tracking.
- [x] P4.2 Implement client prediction path.
- [x] P4.3 Implement reconciliation via world queue/barrier (no direct storage mutation).
- [x] P4.4 Implement interpolation systems for remote entities.
- [x] P4.5 Add client correction stats (rollback counts/magnitude/apply latency).
- [x] P4.6 Add combined loopback harness controls for directional latency/jitter/drop profiles and end-of-run summary metrics.
- [x] P4.7 Add loopback preset sweep mode (Local/Lan/Wan/Harsh) with per-stage summary output.
- [x] P4.8 Add stage-local stats reset + explicit transport drop counters (avoid conflating in-flight with packet loss in sweep summaries).
- [x] P4.9 Add summary warmup window + correction age metrics (server tick age p50/p95/max) + CSV summary output.
- [x] P4.10 Add per-stage drain ticks + input ACK RTT metrics (p50/p95/max) in periodic and summary outputs.
- [x] P4.11 Add delay-aware stage drain wait to reduce stage-boundary in-flight residue before summary emission.
- [x] P4.12 Add attempted/delivered percentages in sweep summaries to make drop math explicit under impairment profiles.
- [x] P4.13 Add optional sweep matrix mode (preset x tick_ms x input cadence) with stage labels in structured and CSV summaries.
- [x] P4.14 Add sweep aggregate summary line (best/worst/mean) across stage outputs for faster comparison.
- [x] P4.15 Add impaired-only aggregate + threshold verdict line, with weighted loss percentages and configurable limits.
- [x] P4.16 Add matrix aggregate/transition reporting and impaired-gated verdict summaries per matrix cell.
- [x] P4.17 Add ms-normalized verdict gating/output (ack RTT + correction age) so mixed tick-rate matrix results are comparable.
- [x] P4.18 Add verdict headroom percentages and split aggregate best/worst RTT reporting for ticks vs ms to avoid mixed-unit ambiguity.
- [x] P4.19 Add primary bottleneck/fail-reason fields and worst-stage impaired verdict output for faster triage.
- [x] P4.20 Add per-cell worst-stage matrix verdict lines and per-tick matrix recommendation output.
- [x] P4.21 Add strict per-tick recommendation/transition output based on worst-stage verdicts (`MATRIX_*_STRICT`) while preserving average-based recommendations.
- [x] P4.22 Add strict pass-only recommendation line (`MATRIX_RECOMMEND_STRICT_PASS_ONLY`) to expose when no fully-safe strict profile exists.
- [x] P4.23 Add strict gap diagnostics (`MATRIX_STRICT_GAP`) showing the best non-pass candidate and improvement needed to reach strict pass.
- [x] P4.24 Add strict matrix summary/tuning lines (`MATRIX_STRICT_SUMMARY`, `MATRIX_STRICT_TUNING`) to quantify dominant bottlenecks and required threshold deltas.
- [x] P4.25 Add strict profile suggestion line (`MATRIX_STRICT_PROFILE`) that consolidates per-metric tuning into a copyable pass-target threshold set.
- [x] P4.26 Add selectable verdict threshold profile (`Baseline` / `IslandSliceCandidate`) and log active profile at startup.
- [x] P4.27 Add input-to-visible latency telemetry with component breakdown (`LATENCY_INPUT_VISIBLE`).
- [x] P4.28 Add rollback depth histogram and burst telemetry (`ROLLBACK_HEALTH`).
- [x] P4.29 Add interpolation buffer health telemetry (`INTERP_HEALTH`) with underrun/overrun and buffer-span tracking.
- [x] P4.30 Add replication debt telemetry with per-lane deferred/coalesced counts and oldest deferred age (`REPL_DEBT`).
- [x] P4.31 Add packet ordering and late-correction telemetry (`ORDER_HEALTH`).
- [x] P4.32 Add correction hotspot ranking telemetry (`CORR_HOTSPOT`) for top entities by correction pressure.
- [x] P4.33 Add spawn sync health telemetry (`SPAWN_SYNC`) and missing-reference counters.
- [x] P4.34 Add clock/tick drift telemetry (`CLOCK_HEALTH`) including client-server tick delta distribution.
- [x] P4.35 Add fixed-island playable loopback bootstrap scene/script (`_tmp/island_slice.tscn`, `_tmp/ECSNetworkIslandSliceNode.cs`) using the shared bridge/replication path.

Acceptance gate:

- [ ] Integration tests: sequence/ack wraparound and reconciliation correctness.
- [ ] Integration tests: correction path obeys barrier mutation rules.
- [ ] Soak test: correction churn remains stable and measurable.

Expected mutation:

- `Client/Engines/Networking/*` and `Shared/Engines/Networking/*` client bridge
- client runtime systems
- client integration tests
- client host/presentation boundary adapters (semantic presentation/UI outputs, no engine resource paths in client core)
- client UI command/path prepared for future schema/prefab menu protocol (server-driven UI with local prefab cache)

Expected stable:

- Server remains sole gameplay authority.
- `Network/` and client runtime core remain engine-agnostic (engine-specific rendering/UI/input stays in host/bridge layers).

---

## Phase 5 - Hardening

Objective: production safeguards and long-run operational confidence.

Tasks:

- [x] P5.1 Implement inbound datagram rate limiting and abuse strike policy with disconnect suggestion callbacks.
- [x] P5.2 Implement connection caps/back-pressure policy with capped session admission, explicit rejection reasoning, and rejection telemetry.
- [x] P5.3 Add failure recovery scenarios (disconnect/reconnect/shutdown behavior) with session-level disconnect APIs, bridge shutdown helpers, and reconnect-safe inbound session resolution.
- [x] P5.4 Validate production stats profile (sampling/cap/top-N) via profile presets, protocol stats profile wiring, and top-disconnect-reason capping tests.
- [x] P5.5 Run soak/load coverage and capture leak/allocation/timing evidence via dedicated soak evidence tests (elapsed/tick avg/GC/heap growth/work units).
- [x] P5.6 Run explicit hot-path regression audit (allocations/steady-state overhead) with bounded-allocation audit tests for packet dispatch and inbound poll/decode/apply loops.

Acceptance gate:

- [ ] Soak test pass with stable memory and bounded timing regressions.
- [ ] Abuse/rate-limit tests pass with explicit counters/log reasons.
- [ ] Production stats profile validated for acceptable overhead.
- [ ] Hot-path regression audit passes with no unresolved P0 findings.

Expected mutation:

- session/security/policy modules
- soak/load harnesses
- operations docs and runbooks

Expected stable:

- API-level contracts from phases 1-4 (unless explicitly versioned and documented).
- Phase 5 verification evidence captured in `NETWORK_PHASE5_EVIDENCE.md`.

---

## 6) Cross-Phase Test Matrix

- [ ] Unit: packet codec, registry, sequence arithmetic.
- [ ] Integration: handshake/session lifecycle, barrier-safe command application.
- [ ] Determinism: repeated run hash parity under same build/config/runtime profile.
- [ ] Load: high connection synthetic traffic with budget pressure.
- [ ] Soak: long-run memory/GC/timing stability.
- [ ] Performance: transport/decode/replicate stage timing and p50/p95 reporting.
- [ ] Performance: framing parser throughput and queue fanout efficiency under burst traffic.
- [ ] Performance: allocation profile (Gen0 rate + pool miss counters) under representative load.

---

## 7) Phase Exit Checklist (Required Each Phase)

- [ ] Tests for phase pass in CI/local.
- [ ] Stats counters for new features are present and documented.
- [ ] Structured logs include new failure/overflow paths.
- [ ] Mutation review completed (stable vs changed vs rationale vs evidence).
- [ ] PRD/docs updated if behavior deviated from plan.
- [ ] Hot-path checklist reviewed (allocations, boxing, wall-clock usage, linear scans).

---

## 8) Initial Task Queue (Execution Order)

1. P0.1 -> P0.7 (design freeze)
2. P1.1 -> P1.8
3. P2.1 -> P2.7
4. P3.1 -> P3.9
5. P4.1 -> P4.5
6. P5.1 -> P5.6

---

## 9) Done Definition (Program-Level)

Program is complete when:

- All phase acceptance gates are satisfied.
- Module dependency rules are still intact (`ECS` <-> `Network` isolation preserved).
- Core perf/alloc targets are met under representative load.
- Observability contract is fully implemented with production-safe cardinality controls.
- Implementation notes and final operational defaults are reflected in PRD/docs.

---

## 10) Phase Reviews (Completed to Date)

## Phase 1 Review
- Stable: `ECS`/`Network` isolation and barrier ownership model were preserved.
- Mutated: transport/protocol foundations were added (loopback transport, packet registry/dispatch, framing parser, handshake gates, baseline protocol/transport stats).
- Rationale: establish a deterministic, low-overhead network runtime base with explicit framing/validation and observability.
- Evidence: Network transport/framing/packet/handshake test suite passing in local runs; no regressions in broader `Network|Loopback` suite.
- Follow-ups: acceptance gate items remain open for formal framing/handshake matrix sign-off in this document.

## Phase 2 Review
- Stable: inbound gameplay mutation still occurs only through pre-barrier world queue application.
- Mutated: session lifecycle manager, inbound command pipeline, session stats/events, duplicate/timeout policies, and server bridge integration hooks were implemented.
- Rationale: make session state transitions explicit and auditable while keeping host-tick integration deterministic.
- Evidence: session lifecycle/policy tests and inbound pipeline integration tests passing; stable session IDs surfaced via lifecycle events.
- Follow-ups: formal phase acceptance checklist items in section 5 remain to be closed here.

## Phase 3 Review
- Stable: per-connection replication budget model and barrier/queue discipline remained intact.
- Mutated: deterministic replication pipeline (dirty drain, interest filter, per-connection cursors, budget lanes/overflow behavior, shared fanout, replication stats) was implemented.
- Rationale: support scalable deterministic replication under pressure with explicit defer/drop/coalesce telemetry.
- Evidence: replication pipeline unit/integration/soak tests passing in local runs; debt and overflow behavior surfaced by stats/harness outputs.
- Follow-ups: UDP reliable-lane ACK/RTO path (P3.9) remains conditional and not yet implemented in this plan.

## Phase 4 Review
- Stable: server authority model and client-side barrier-safe correction apply path were preserved.
- Mutated: client input sequencing, prediction/reconciliation, interpolation, extensive loopback sweep matrix telemetry (including strict profile diagnostics and recommendation outputs), and a fixed-island playable loopback bootstrap scene were implemented.
- Rationale: validate playable network behavior under controlled impairment and make bottlenecks/measurable headroom explicit.
- Evidence: loopback matrix/sweep output lines and network/client test suite passing; strict/aggregate verdict outputs now available for tuning.
- Follow-ups: phase acceptance checklist items remain open in-document pending explicit sign-off updates.

## Phase 5 Review
- Stable: phase 1-4 API boundaries remained intact while hardening policies were layered on top.
- Mutated: inbound rate limiting with strike/disconnect suggestions, session admission caps/back-pressure rejection telemetry, failure recovery APIs (disconnect/reconnect/shutdown), production stats profile presets, soak evidence, and hot-path allocation audits were added.
- Rationale: shift from feature completeness to operational resilience and regression protection.
- Evidence: hardening-related tests pass in local runs (`NetworkSoakTests`, `NetworkSession*`, `NetworkInbound*`, full `Network|Loopback` filter); summary evidence captured in `NETWORK_PHASE5_EVIDENCE.md`.
- Follow-ups: convert remaining unchecked acceptance items in sections 5-7 to explicit CI-gated criteria once the island-slice implementation pass is complete.

## 11) Island Slice Runtime Baseline (Empirical)

Note: this section is a chronological log of measured checkpoints. Older entries are retained as historical snapshots; the most recent authoritative baseline is the last checkpoint in this section.

Pre-AOI / pre-chunking socket-ramp measurements for the fixed island slice (Feb 2026):

- Dense/everyone-active synthetic profile (20 Hz movement + chat): ping p95 SLO pass through **56 CCU**, fail at **64 CCU**.
- More realistic synthetic profiles (10 Hz movement-only or ~40% active movers/chatters): ping p95 SLO pass through **80 CCU**, fail at **96 CCU**.
- Fresh vs warm server runs produced the same knee; no meaningful uptime drift observed in tested windows.

Implementation priority derived from this evidence:
1. Server spatial chunking + AOI interest filtering
2. Shared delta fanout optimization within chunk/interest groups
3. Client sliding render windows / chunk streaming integration

Reference evidence and commands: `Docs/NETWORK_BASELINE_2026-02-22.md`.

### Phase A Update (Spatial Chunks + AOI Interest Filtering)

Phase A was implemented and validated with the socket-ramp harness:

- Mixed profile (~40% active movers/chatters) improved from **80 pass / 96 fail** pre-AOI to **325 pass / 350 fail** post-AOI (`ping p95 <= 40 ms` SLO).
- Benchmark note: server debug logging materially perturbs high-CCU p95; use non-debug server + longer settle for SLO runs, and reserve debug mode for AOI telemetry snapshots.
- AOI telemetry at 500 bots showed:
  - `aoi_sub_chunks_avg/max=9` (expected radius-1 square AOI)
  - `aoi_visible_avg=13`, `aoi_visible_max=24`
  - `aoi_filter_selected_pct=2.6` (AOI rejects ~97.4% of broad fanout checks)

Follow-up priority after Phase A:
1. AOI telemetry retention and regression checks
2. Incremental AOI subscription recompute (only on chunk crossing) and additional hotspot/spread profile comparisons
3. Shared delta fanout optimization within chunk/interest groups

### Phase A.5 Update (Transport Flush Batching)

After AOI, telemetry showed the dominant cost moved to synchronous transport flush (`perf_flush_ms_avg` dominated total server step time).

Implemented follow-up optimization:
- batched outbound writes per connection in `TcpFramedNetworkTransport.FlushOutbound()`

Observed impact (mixed profile, ~40% active movers/chatters):
- pre-batching post-AOI checkpoint: **325 pass / 350 fail**
- post-batching checkpoint: **PASS through 500 CCU** (no fail observed in tested range)

Additional telemetry signal after batching:
- `perf_flush_ms_avg` dropped from tens of ms (previously ~74 ms at high load) to low single-digit ms in sampled debug runs.

Next priority remains:
1. Hotspot-vs-spread validation under AOI + batching
2. Async socket ramp harness (>500 sockets on Windows)
3. Later: server transport async/IOCP refactor for long-term CCU ceiling

### Phase A.6 Update (Historical Snapshot - 2026-02-23)

Completed validation steps:

- Hotspot (dense AOI) profile under AOI + batching:
  - **PASS at 300**
  - **FAIL at 400**
  - Confirms dense hotspot behavior still collapses earlier than spread/mixed runs (expected).

- Async socket ramp harness (`_tmp/run_island_socket_ramp_async.py`) implemented to bypass Windows `select()` FD limits.
  - Enabled clean >500 local socket tests.

- Mixed profile (~40% active movers/chatters) revalidated beyond 500:
  - **PASS at 575**
  - **FAIL at 600** (repeat fail confirmed; multiple 600 repeats fail in authoritative async report)
  - `SrvCPU95 ~ 8.7-9.2%` near the knee (authoritative repeated validation)

Interpretation:
- AOI + fanout + transport batching improvements moved the spread/mixed knee far enough that the harness had to be upgraded.
- The next long-term CCU lever is server transport architecture (current `_tmp` transport remains thread-per-connection for receives, despite batching and async harness improvements).

### Phase A.7 (Historical Snapshot - 2026-02-23)

Objective:
- Replace the current thread-per-connection receive/connect loops in the TCP framed transport with async socket/network-stream operations (IOCP-backed on Windows) while preserving the existing `INetworkTransport` polling interface.

Rationale (at the time):
- Mixed-profile knee was ~575/600 while sampled server CPU remained low (~7%), indicating the next ceiling was transport/runtime I/O architecture rather than ECS/AOI compute.

Initial staged work (in progress):
- Convert accept/connect/receive loops in the local TCP framed transport (`_tmp/TcpFramedNetworkTransport.cs`) from dedicated blocking threads to async `Task`/`ReadAsync`/`AcceptTcpClientAsync` loops.
- Retain existing outbound batching and `INetworkTransport` queue semantics to reduce rollout risk.

Validation plan for this stage:
1. Build verification (`dotnet build`)
2. Smoke ramp with local auto-launched server (small stages)
3. Compare mixed-profile `575/600` behavior against pre-refactor transport baseline

Status:
- build verification: complete
- small async smoke ramps: complete
- mixed-profile edge behavior after async receive/connect changes remains in the same envelope (`575` pass, `600` threshold/fail range)

Next workload-side optimization (parallel track to transport refactor):
- Priority replication LOD (server-side cadence lanes on top of AoI) to improve hotspot density performance without reducing long-range visibility outright.

### Phase A.8 Update (Historical Snapshot - 2026-02-24)

Completed:

- Hybrid TCP+UDP transport MVP wired in local island runtime path:
  - TCP for reliable lanes
  - UDP for `Unreliable` / `UnreliableSequenced`
  - UDP association handshake (offer/hello/ack)
  - UDP batching + tokenized UDP payload support

- Async ramp harness upgraded for UDP validation:
  - UDP hybrid client path
  - shared UDP socket mode with token demux (`--udp-shared-socket`)
  - UDP-specific harness metrics (assoc rate, batching stats, parser cost)
  - launched-server `SERVER_METRIC` capture (`--capture-server-metrics`)

Validated baselines at that checkpoint (non-debug authoritative runs):
- **UDP mixed profile (shared-socket harness): PASS 575 / FAIL 600**
  - practical parity with TCP mixed baseline knee
- **UDP hotspot profile (shared-socket harness): PASS 300 / FAIL 350**

Priority LOD Phase 1 impact (UDP hotspot, outer cadence `4`):
- **PASS 350 / FAIL 400**
  - confirms LOD cadence shaping improves dense hotspot behavior in UDP path as well
  - no-debug authoritative report captured: `_tmp/reports/island_socket_ramp_async_20260224_024725.json`

Operational notes:
- UDP association SLO remains stable (`assoc_slo=PASS`, typically 100% associated at tested stages)
- Debug/capture runs are useful for transport/perf correlation but not authoritative p95 baselines because server debug logging perturbs latency at high CCU

Next priorities from this point:
1. Priority LOD Phase 1.1 fairness/aging (avoid starving outer entities)
2. Shared delta fanout / recipient-group serialization reuse
3. Continue server transport async/IOCP refactor hardening and higher-CCU validation

### Phase A.9 (In Progress) - Priority LOD Fairness/Aging + Fanout-Reuse Groundwork

Local island harness/runtime path (`_tmp`) work started:

- Priority LOD Phase 1.1 fairness/aging:
  - outer-lane sends now use deterministic phase scheduling per `(connection, entity)` with an aging escape hatch (`max silence ~2x cadence`)
  - goal: smooth outer-lane bursts while preventing stale outer entities from being gated indefinitely
  - added telemetry fields:
    - `lod_phase_sent`
    - `lod_aged_sent`

- Shared fanout reuse groundwork (telemetry):
  - AOI subscriber-set grouping telemetry added to estimate recipient-group reuse potential before pipeline serialization reuse is implemented
  - added telemetry fields:
    - `aoi_sub_groups`
    - `aoi_sub_group_max_chunks`

Notes:
- These changes are in the local island runtime path (`_tmp/ECSNetworkIslandSliceNode.cs`) and require rebuild/export to validate in executable-based ramps.
- Full shared delta serialization reuse in `NetworkReplicationPipeline` is still pending; current work focuses on measuring reuse opportunity and reducing hotspot burstiness first.

### Phase A.10 (Started) - Pipeline Recipient-Selector Fast Path Scaffold

Tracked replication pipeline work has started to support exact recipient-group fanout (needed for chunk/AOI subscriber-set reuse):

- Added optional `ReplicationRecipientSelector` delegate (`Network/Replication/ReplicationDelegates.cs`)
  - delegate fills a scratch span with exact recipient connection IDs for a delta
  - supports resize/retry semantics (returns required count when scratch is too small)
- `NetworkReplicationPipeline` now has an optional recipient-selector dispatch path (`Network/Replication/NetworkReplicationPipeline.cs`)
  - when present, delta visitors dispatch to the selector-provided recipients first
  - can optionally skip `interestFilter` (`assumeRecipientSelectorDefinesInterest=true`) for exact AOI recipient sets
  - existing behavior remains unchanged when no recipient selector is provided
- Added pipeline unit coverage for exact recipient-selector fanout without invoking `interestFilter`

Why this matters:
- The existing single `ConnectionState.GroupId` model cannot represent overlapping AOI chunk subscriber sets.
- Recipient-selector fanout is the first pipeline-side step toward true shared recipient-group reuse for chunk/AOI-driven replication.

Local island runtime progress:
- `_tmp/ECSNetworkIslandSliceNode.cs` now wires AOI chunk subscribers into the pipeline recipient selector
- selector applies Priority LOD gating before returning recipients and runs in trusted mode (`assumeRecipientSelectorDefinesInterest=true`)
- full benchmark validation is pending rebuild/export + ramp reruns

### Phase A.11 (Historical Snapshot - 2026-02-28) - UDP Hotspot + Priority LOD + Selector

Latest no-debug hotspot validation with:
- UDP shared-socket harness
- `--spawn-spacing-grid=1 --spawn-wrap-count=20`
- `--priority-lod --priority-lod-outer-cadence=4`
- `--server-recipient-selector=true`

Locked baseline at that checkpoint (2026-02-28):
- sanity profile (`300,300,350,400`): PASS through 400
- threshold profile (`425,425,450,450,475,500,525`):
  - `425`: PASS
  - `450`: FAIL (repeat fail)
  - `475+`: FAIL

Current hotspot knee under this profile:
- **PASS at 425**
- **FAIL at 450**

Reports:
- `_tmp/reports/island_socket_ramp_async_20260228_001547.json`
- `_tmp/reports/island_socket_ramp_async_20260228_002443.json`

Next optimization step started from this lock:
- adaptive Priority LOD outer cadence scaling under load pressure (server-side), with explicit telemetry, while keeping existing fixed-cadence behavior as the floor.

### Phase A.12 (Current Baseline - 2026-03-05)

Current authoritative no-debug runnable baseline (UDP shared-socket harness, lite server metrics capture):

- Mixed threshold profile (recent threshold runs in this branch):
  - observed knee is now around **800/825**
  - latest run (`_tmp/reports/island_socket_ramp_async_20260305_134512.json`): `800` failed (`p95=41.66`), `825+` failed
  - nearby prior run (`_tmp/reports/island_socket_ramp_async_20260304_220843.json`): `800` passed, `825+` failed
  - practical interpretation: **800 is borderline; 825 is the first consistent fail range**

- Hotspot profile (current tested range):
  - latest run (`_tmp/reports/island_socket_ramp_async_20260305_134939.json`) passes through `600` (`p95=32.88`)
  - current statement is therefore **PASS through tested max 600** (next threshold run should extend to find first fail stage)

Pipeline/telemetry status at this checkpoint:

- selector fallback remains effectively eliminated in current tuned path for these profiles
- fanout copy counters remain at/near zero in current path (copy-elimination work is holding)
- transport-side multicast enqueue path is now part of the active optimization track and should be treated as current behavior, not future-only scaffolding
