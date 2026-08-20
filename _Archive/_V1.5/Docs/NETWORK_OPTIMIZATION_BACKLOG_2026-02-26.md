# Network Optimization Backlog (Triage Snapshot - 2026-02-26, maintained through 2026-03-08)

Purpose: preserve the recent broad optimization assessment (Claude summary / 75 unique findings from a larger 97-item pass) so we do not need to re-run analysis before continuing targeted pipeline work.

This is a triage/backlog document, not a commitment to implement every item as stated.

## 1) Current Context (Measured Bottlenecks / Recent Work)

- AOI + transport batching + async harness + UDP shared-socket/tokenized demux are in place.
- Pinned baseline comparisons must use the UDP shared-socket harness profile. Plain TCP quick checks are not comparable and were the source of a false regression signal during debt cleanup.
- Current measured baseline envelope has moved materially since the original snapshot:
  - mixed profile has now reached at least **1300 PASS** in focused verification runs
  - hotspot profile has now reached at least **875 PASS** in focused verification runs
- Latest release-like server metrics indicate the dominant server-side cost in mixed ramps is now:
  - `perf_flush_ms_avg ~= 6-7 ms`
  - `perf_aoi_ms_avg ~= 0.8-0.9 ms`
  - `perf_repl_ms_avg ~= 0.25-0.35 ms`
- Interpretation:
  - the main ceiling has shifted away from AoI/selector work
  - transport flush/send work and payload volume are now the primary optimization target
  - a newer cheap selector-side policy pass is now in flight: dense/global observer cadence reduces non-owner observer fanout without reintroducing expensive per-recipient LOD checks
- Historical checkpoints retained for context:
  - Feb 2026 mixed profile: PASS 575 / FAIL 600
  - Feb 2026 hotspot snapshots: PASS 350 / FAIL 400, then PASS 425 / FAIL 450
- Recent active work is in `NetworkReplicationPipeline` and the island `_tmp` runtime:
  - recipient-selector fast path
  - pipeline `lodFilter` split
  - selector A/B toggles and telemetry
  - transport-side multicast fanout enqueue path

### 1.1 Transport Experiment Log (2026-03-07)

These were tested so we do not repeat dead ends:

- Kept:
  - `_tmp/TcpFramedNetworkTransport.cs`
  - removed per-flush rebuild of active batch connection scratch
  - flush now keeps stable active batch lists (`connectionId + batch`) and iterates those directly
- Tested and reverted:
  - active flush-route snapshot dictionaries/lists for peer + UDP route resolution
  - per-peer async TCP writer queue
  - forced UDP send scratch reuse
- Why reverted:
  - all three either regressed the mixed 750 stage directly or failed to improve it enough to justify added complexity
  - the async TCP writer in particular increased tail latency under the current workload
- Current transport takeaway:
  - the flush path remains the main bottleneck
  - the simple batch-list cleanup is worth keeping
  - future transport work should focus on reducing tiny send/batch frequency or reducing payload volume, not adding more flush-side indirection

## 2) Assessment Summary (Preserved)

The external review grouped ~75 unique optimization findings (collapsed from 97 raw findings) into:

- 7 MAJOR
- 5 MODERATE-MAJOR
- 16 MODERATE
- 22 MINOR
- 22 MICRO
- 3 correctness issues

The review identified `NetworkReplicationPipeline.cs` as the dominant optimization surface (roughly ~40% of findings), which aligns with our current work and measurements.

## 3) High-Value Items We Agree Are Worth Pursuing

### 3.1 Replication Pipeline (Near-Term)

1. Budget-exhausted early skip / early-out improvements (`M3`, `M4`)
   - High potential impact, but **must preserve overflow semantics** (`Drop/Defer/Coalesce`) and deferred correctness.
   - A naive early return was attempted and **rolled back** because it broke coalesce/deferred tests.
   - Needs a semantics-aware version.

2. `WasDeferredSentThisBuild` O(N) duplicate scan (`M1`)
   - Likely real hot-path cost under deferred pressure.
   - Low-risk optimization target and compatible with current architecture.
   - See section 4 for current status.

3. Selector/AoI/LOD pipeline split (structural)
   - Already in progress and partially implemented:
   - `interestFilter` = AoI
   - `lodFilter` = LOD
   - `recipientSelector` = AoI recipients
   - This was required to stop selector-path hotspot regressions.

4. Early termination in replay visitation (`M4`)
   - Promising once a semantics-safe exhausted-connection strategy exists.
   - Larger touch surface (visitor signatures / replay window traversal).

### 3.2 Secondary (Near/Mid-Term)

5. `LengthPrefixedFrameReader.AdvanceIndex` modulo -> bitmask (`M5`)
   - Potentially good hot-path framing win if buffer invariants are constrained to power-of-two.
   - Implemented as a safe hybrid fast path:
     - cache `_indexMask` when the current buffer length is power-of-two
     - use bitmask math in `AdvanceIndex` only in that case
     - fall back to modulo when capacity is not power-of-two
   - Can be strengthened later by enforcing power-of-two capacity end-to-end.

6. Dead session cache in `NetworkInboundCommandPipeline` (`Mo9`)
   - Good cleanup/fix candidate.
   - Not currently a measured CCU ceiling item compared to replication/fanout work.

7. Replay window targeted prune / occupancy improvements (`Mo8`, `MM2`)
   - Plausible wins, likely medium effort.

## 4) What Was Implemented During This Pass

### 4.1 Implemented

- `NetworkReplicationPipeline`:
  - Added `ReplicationLodFilter` support and split pipeline filtering order (`AoI -> LOD -> send/budget`)
  - Integrated `lodFilter` across selector, normal, grouped/ungrouped trusted paths
  - Recipient-selector dispatch fallback (`return < 0`) retained for rollout safety
- `NetworkReplicationPipeline`:
  - Replaced per-tick rebuild of selector connection lookup scratch map with a persistent `connectionId -> ConnectionState` dictionary maintained on track/untrack.
  - Removes per-build dictionary clear/reinsert churn in selector-enabled builds.
- `ReplicationReplayWindow`:
  - Added occupancy-aware iteration by tracking occupied ticks (`SortedSet<long>`).
  - `CopyRange`, `VisitRange`, `VisitRangeWhile`, and bucket-visit methods now iterate occupied ticks in range instead of scanning every tick in the range.
  - `Prune` now removes expired ticks by popping oldest occupied ticks until the retention boundary, avoiding full bucket scans.
- `NetworkReplicationPipeline`:
  - Added lightweight recipient-selection reuse cache for selector path:
    - caches the most recent selector result per computed selection key (coalesce key / entity+stream fallback)
    - reuses recipient list for consecutive deltas targeting the same selection key
    - reduces repeated selector callback work in clustered delta streams.
  - Expanded to build-local multi-key cache:
    - keyed cache of selector results and mapped `ConnectionState` arrays per selection key within `BuildTick`
    - avoids repeated selector callback + repeated `connectionId -> ConnectionState` lookup for recurring keys.
- `NetworkReplicationPipeline`:
  - `WasDeferredSentThisBuild` now uses `HashSet<ulong>` + hashed deferred sent keys instead of linear scanning `List<DeferredDelta>`
  - This is the practical implementation of the "M1" direction (with exact metadata+payload hash, not only `(entityId,tick)`)
- `NetworkReplicationPipeline`:
  - Partial semantics-safe M3 optimization:
  - after AoI/LOD passes, if `state.IsBudgetExhausted` and the overflow action for the current lane is `Drop`, the pipeline now fast-paths to `_stats.RecordDropped(...)`
  - `Defer/Coalesce` lanes still use the normal path to preserve overflow semantics
- `ReplicationReplayWindow` + `NetworkReplicationPipeline`:
  - M4 scaffolding + conservative expansion:
  - added early-stop capable replay visitation overloads (`VisitRangeWhile`, `VisitRangeBucketsWhile`)
  - pipeline build traversal now routes through the bool-return visitor path
  - conservative early-stop condition is enabled for the all-`Drop` overflow case:
    - if all tracked connections are budget exhausted and all overflow actions are `Drop`, replay visitation now stops early
  - added mixed-policy-safe replay range pre-counting for non-`Drop` lanes
  - replay visitation now also stops early in mixed overflow policies once:
    - all tracked connections are budget exhausted, and
    - no non-`Drop` overflow deltas remain in the replay range
  - guarded by unit tests (all-drop, mixed-no-stop, mixed-stop-when-only-drop-remains)
  - optimized mixed-policy M4 counting to be lazy:
    - non-`Drop` replay-range count is now computed only if/when all connections become exhausted
    - avoids unconditional per-build replay prepass in the common case where at least one connection remains non-exhausted

### 4.2 Attempted Then Rolled Back (Important)

- A naive "skip budget-exhausted connections before filters" optimization (M3-style) was tried and reverted.
- It broke coalesced/deferred behavior because budget-exhausted connections still need to process later deltas for overflow handling (`Defer` / `Coalesce`).
- This confirms M3 is **not** a trivial early-return patch in the current semantics.
- Full `M4` replay early termination is still pending.
  - The visitor plumbing is in place and conservative stop conditions are implemented.
  - Broader/cheaper early-stop conditions for mixed overflow policies are still pending (current version uses a replay-range prepass count).

## 5) Recommended Next Order (Tactical)

1. Continue selector-path stabilization and hotspot A/B validation
   - selector OFF remains reference baseline
   - selector ON is still under active tuning

   Historical update (2026-02-28):
   - hotspot + UDP + Priority LOD + selector baseline now locked at **425 PASS / 450 FAIL** using:
     - `_tmp/reports/island_socket_ramp_async_20260228_001547.json`
     - `_tmp/reports/island_socket_ramp_async_20260228_002443.json`
   - next tactical step is adaptive outer-cadence scaling (pressure-aware LOD) to flatten the 425->450 tail.
   - Current status (2026-03-05): hotspot profile now passes through tested 600; continue treating selector path as active tuning surface.

2. Revisit M3 with a semantics-safe design
   - Candidate approach:
     - fast path only when overflow action for delta lane is guaranteed `Drop`
     - or maintain an "all future lanes drop" shortcut state
     - avoid skipping deferred/coalesce capture

3. Implement replay early termination (`M4`) after M3 semantics are settled
   - Best ROI once exhausted-connection tracking is correct

4. Framing hot-path improvements (`M5`) and replay window prune/occupancy work
   - Good follow-up candidates after current replication-path structural work

## 6) Deferred / Bigger Design Lifts (Preserved)

These are likely valuable but not "quick wins" in the current phase:

- VarInt encoding for IDs/counts (`M6`)
- True bit-packing / quantized floats (`M7`)
- ConnectionState hot/cold split (`MM1`)
- Wider protocol/schema compaction redesign
- Broad stats false-sharing / micro-inlining style cleanup unless profiling proves impact

## 7) Correctness Notes Preserved from Assessment (Need Separate Hardening Pass)

The assessment also flagged correctness issues that are not strictly perf tuning:

- `ProtocolStats` decode failure bytes counted as decoded bytes (metrics bug)
- `ClientPredictionStore.CopyNewerThan` overlap concern (needs targeted verification in current code)
- `LengthPrefixedFrameReader` error reporting parameter mismatch (`required` value in exception)

These should be handled in a separate hardening pass so perf work stays focused.

## 8) Working Rule for This Backlog

- Use measured bottlenecks first (hotspot/mixed ramps + server perf telemetry).
- Treat static optimization lists as a source of candidates, not ordering truth.
- Prefer low-risk, testable pipeline changes over protocol redesign until current ceilings move again.

## 9) Current Status Snapshot (2026-03-07)

### 9.1 What is now in place

- Replication pipeline:
  - `Dictionary<int, ConnectionState>` + flat connection list replaced the older sorted container path.
  - Deferred-sent tracking now uses generation-stamped hash state instead of per-build `HashSet.Clear()`.
  - Deferred flush caps are global across connections for fairer draining.
  - Budget accounting now reserves first and commits after successful sink write.
  - Interest-cache and recipient-selection caches now have simple caps to avoid unbounded growth.
- Replay window:
  - Occupancy-aware iteration and targeted prune remain in place.
  - Replay-window pooled buffer clear-on-return was tested and rolled back for the perf branch because it materially regressed the current envelope.
- Inbound path:
  - Rate-limit clock uses a monotonic time source.
  - Session-cache path has poll-generation validation to reduce stale cache reuse risk without forcing the resolver on every datagram.

### 9.2 Current measured envelope

- Mixed profile:
  - verified `1275 PASS / 1300 FAIL`
  - reference evidence:
    - `_tmp/reports/island_socket_ramp_async_20260308_023311.json`
- Hotspot profile:
  - verified `875 PASS / 900 FAIL`
  - reference evidence:
    - `_tmp/reports/island_socket_ramp_async_20260308_021808.json`
    - `_tmp/reports/island_socket_ramp_async_20260308_022114.json`

### 9.3 What the latest shape implies

- Hotspot is no longer materially worse than mixed.
- The single-process Python harness became the active limiter before the server/runtime path.
- The sharded harness is now the correct upper-range instrument for mixed/hotspot scaling.
- Mixed and hotspot should now be treated as different synthetic profiles, not fixed "easy/hard" labels.
- That means AoI / selector / interest-filter work is no longer the dominant ceiling.
- The remaining ceiling is now mostly:
  - replicated UDP payload volume
  - client/server parse cost driven by that volume
  - only then residual transport flush/send overhead

## 10) Next Phase (Payload / Reuse / Cadence)

### 10.1 Immediate priority

1. Replication schema compaction
   - shrink hot correction payloads before transport sees them
   - first landed change:
     - moved correction `serverTick` from 64-bit to 32-bit in `EntityInt2StateAckOwnerTick32Codec`
     - reduced `ownerConnectionId` to `uint16` on the wire
     - reduced `NetworkEntityId` to `uint32` on the wire for the current runtime
     - compacted `x/z` to signed 24-bit values
     - correction payload size reduced from `30` bytes to `18` bytes
   - next compaction candidates:
     - quantized / bounded position encoding
     - smaller owner / metadata fields where safe

2. Shared encoded payload reuse
   - current multicast / borrowed-payload fanout is already in place
   - next work is reducing repeated payload materialization above the transport queue
   - aim to preserve one encoded payload per shared recipient set as long as possible

3. Hotspot-oriented cadence / adaptive LOD tightening
   - dense overlap now fails later, but hotspot still trails mixed materially
   - next policy work should focus on reducing low-value outer-ring replication volume under hotspot pressure
   - first landed change:
     - added visible-entity-pressure outer-cadence scaling in the island runtime
     - this composes with the existing connection-count adaptive outer cadence instead of replacing it

4. Revisit transport only if flush is still dominant after the above
   - transport is no longer the first lever
   - it becomes the next lever only after payload volume is flatter

### 10.2 Deferred but still important

- Async per-peer TCP writer / bounded outbound queue
- UDP send-path scratch / datagram segmentation cleanup if flush metrics show send-side copies still dominate
- Visitor consolidation inside `NetworkReplicationPipeline`
- More aggressive protocol compaction (`VarInt`, bit packing) once transport cost is flatter

## 11) Review Debt Closure Plan (2026-03-12)

This is the working plan for closing the remaining review debt before continuing into the larger architectural items.

Execution order:

1. finish the remaining low-risk review debt
2. validate after each debt batch
3. replace `CountNonDropOverflowDeltasInRange(...)` fully
4. refactor the TCP send path
5. do a bridge cleanup pass
6. update baselines/docs again from the cleaned state

### 11.1 Current Inventory

#### Fixed in the current branch

- `NetworkReplicationPipeline` while-visitors now preserve `SentKeyHash`
- `NetworkReplicationPipeline` byte-budget overflow guard
- `PacketRegistry` publish ordering via `Volatile.Write(...)`
- `ClientCorrectionStats` CAS comparison fix
- `ClientInterpolationBuffer` guard/assert cleanup
- selector cursor modulo-valid storage
- `LastSentTickByNetworkId` eviction on AoI exit
- `ClientPredictionStore.CopyNewerThan(...)` single-pass rewrite
- common replay-window range counting paths now have O(1) fast paths

#### Remaining low-risk debt

- session-cache stale-reuse edge in `NetworkInboundCommandPipeline`
  - reconnect and slot-collision churn tests do not reproduce it
  - keep as a watch item unless a runtime repro appears
- long-run cache growth / memory cleanup audit
  - `LastSentTickByNetworkId` exit eviction is fixed
  - interest/recipient caches are capped, but not true LRU
- bridge-layer hygiene hardening in `_tmp/ECSNetworkIslandSliceNode.cs`
  - runtime state reset / client correction apply reset helpers are extracted
  - client correction cache cleanup is centralized
  - resolve any still-valid assertions / cleanup opportunities as they are found

#### Remaining structural debt

- full replacement of `CountNonDropOverflowDeltasInRange(...)`
  - current branch only mitigates the common suffix/full-window cases
- `_tmp/TcpFramedNetworkTransport.cs`
  - blocking TCP writes under `peer.SendSync`
  - current synchronous path is now instrumented with:
    - `tcp_write_avg_us`
    - `tcp_write_max_us`
    - `tcp_batch_write_avg_us`
    - `tcp_batch_write_max_us`
  - latest measurements put average TCP write cost around `~8 us`, so transport work should proceed from measured attribution, not a blind async rewrite
- broader bridge cleanup / hot-path isolation in `_tmp/ECSNetworkIslandSliceNode.cs`

#### Weak, stale, or intentionally deferred items

- reconciliation queue race as previously described by external review
  - not supported by the current lock/swap model
- `FindUpperBound()` starting at `1`
  - no longer applicable after buffer cleanup
- replay-window clear-on-return for pooled buffers
  - intentionally deferred because the throughput regression was material on this branch

### 11.2 Validation Rule

After every debt batch:

- run targeted unit tests for the touched subsystem
- run embedded client checks if the client/runtime path changed
- run quick mixed/hotspot spot checks if the server/runtime path changed

This is required to keep the measured baseline trustworthy while the debt is being removed.

### 11.3 Network Phase State

Current practical position:

- server/network baseline is strong enough for the current phase
- upper-range mixed and hotspot limits are now primarily established with the sharded harness
- remaining structural items are intentionally deferred until they are justified by fresh evidence

Deferred structural items:

- deeper TCP send-path redesign
- larger bridge-layer decomposition inside `_tmp/ECSNetworkIslandSliceNode.cs`
- any further replay-window redesign beyond the currently restored, stable path

The next project phase should treat networking as stable infrastructure unless a new feature or runtime profile produces fresh evidence that one of the deferred items is again the active limiter.

### 11.5 Client Runtime Follow-on

Networking is no longer the active throughput/scaling bottleneck, but one network-adjacent client-runtime item is still active:

- the active Godot client/server movement path has now been cut over to:
  - float runtime movement state
  - quantized centimeter `X/Z` on the wire
  - explicit yaw in corrections
- local movement feel is materially improved and the old active `EntityInt2` movement path is no longer the main limiter

Current follow-on decision:

- continue validating and tightening the new Float movement path
- keep `Y` deferred on the wire until gravity/elevation becomes active
- defer chunk-coordinate + local-offset wire addressing until terrain/chunk work becomes active

This should be treated as client-runtime infrastructure work, not as a reopening of the broader server/network optimization phase.

### 11.4 Document Notes

- `Docs/NETWORK_BASELINE_2026-02-22.md` is retained as historical pre-AoI / early-AoI evidence only.
- `Docs/NETWORK_PHASE5_EVIDENCE.md` was folded into the current baseline/backlog state and is no longer needed as a separate active planning document.
