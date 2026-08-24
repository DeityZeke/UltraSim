# P0.3H Network ready-state selection floor

## Scope

This checkpoint changes only `NetworkProtocolPeer` work selection. Game replication
profiles, Game codecs, lane contracts, proof meaning, congestion/loss/retransmission,
security, listener behavior, and socket topology are unchanged.

The previous selector scanned every reliable and IndependentState capacity slot for
every lane-selection attempt. P0.3H replaces that mechanical work with:

- one intrusive FIFO per ordered reliable lane, indexing the same retained reliable
  message slots;
- one per-lane occupied bitset for IndependentState slots, preserving the existing
  slot-index round-robin starting point;
- one priority-ordered ready-lane mask, so empty lanes and impossible delivery kinds
  are not searched;
- exact empty-attempt and successful-ready-selection census counters.

Retransmission deadlines and loss processing are unchanged. A reliable lane whose
head is in flight remains represented; only that head may become eligible again.
The implementation adds 268 bytes per protocol peer under the whole-flow limits and
remains fixed-capacity and allocation-free after construction.

## Controlled A/B

The before controls were captured from clean `4def8da0`; the final controls were
captured from clean `27ab0024`. Both use 30 warmup ticks, 90 measured ticks, identical
whole-flow traces, cap one for entity profiles, and cap eight for the chunk regression
control. Times are milliseconds. Percentages compare the exact paired captures.

| Workload/profile | Server p95 before | Server p95 after | Change | Tick p95 before | Tick p95 after | Selection before | Selection after |
|---|---:|---:|---:|---:|---:|---:|---:|
| Dense-400 DirtyEntitySpatialFanout | 4.377 | **3.344** | **-23.6%** | 11.196 | **9.132** | 169.59 | **10.10** |
| Dense-400 MaintainedEntityRelation | 5.295 | **4.247** | **-19.8%** | 12.989 | **10.479** | 172.54 | **10.58** |
| Sparse-512 MaintainedEntityRelation | 5.671 | **4.470** | **-21.2%** | 13.685 | **11.259** | 214.73 | **12.89** |
| Sparse-512 DirtyEntitySpatialFanout | 6.512 | **5.421** | **-16.8%** | 14.189 | **12.086** | 209.36 | **12.73** |
| Dense-400 ChunkPublication, cap 8 | 8.985 | **7.041** | **-21.6%** | 34.605 | **30.730** | 275.45 | **21.85** |

The cumulative selector-clock reduction is 92.1-94.0%. This is a CPU result, not
merely a probe-count result; all five server-role and whole-flow rows improved.

## Selector work census

Counters below combine the server and client peers over each 90-tick trace and final
convergence drain.

| Workload/profile | Lane passes before | Lane passes after | Reliable probes before | Reliable probes after | Independent probes before | Independent probes after | Empty attempts | Ready selections |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Dense-400 Dirty Entity | 2,472,820 | **73,444** | 79,130,240 | **0** | 39,548,928 | **73,444** | 300,800 | 73,444 |
| Dense-400 Maintained Entity | 2,511,380 | **81,156** | 80,364,160 | **0** | 40,060,064 | **81,156** | 300,800 | 81,156 |
| Sparse-512 Maintained Entity | 3,136,956 | **94,092** | 100,382,592 | **0** | 50,191,296 | **94,092** | 380,928 | 94,092 |
| Sparse-512 Dirty Entity | 3,041,964 | **80,828** | 97,342,848 | **0** | 48,671,424 | **80,828** | 376,832 | 80,828 |
| Dense-400 ChunkPublication, cap 8 | 4,057,795 | **374,759** | 129,849,440 | **0** | 63,020,165 | **1,038,633** | 364,000 | 301,959 |

The cap-eight chunk control legitimately examines more ready IndependentState bits
than it sends frames because aggregate construction excludes already selected entries
while searching for another fitting frame. It nevertheless retains identical 3.656
frames/datagram and identical forward/proof/message/byte counts.

Across every A/B row:

- Game messages and application proofs are identical;
- data datagrams, frames/datagram, Game bytes, frame bytes, and wire bytes are identical;
- capacity drops remain zero;
- exact semantic digests match the corresponding before row;
- convergence remains true.

AES, endpoint, and listener work remain in the same range; the changed stage is the
selector. The selector structures raise Network reservation by exactly 268 bytes per
peer (214,400 bytes for 400 server/client pairs; 274,432 bytes for 512 pairs).

## Rated server-role sweep

The first 90-tick diagnostic placed Dense-448 on the measurement boundary (4.010 ms
then 3.960 ms). A 270-tick confirmation measured 4.810 ms, while Dense-384 remained
3.405 ms. The permanent rated harness therefore uses 270 measured ticks and retains
the first completed population over 4 ms p95.

| Workload/profile | CCU | Tick p95 | Server p50 | Server p95 | Server p99 | Server max | Result |
|---|---:|---:|---:|---:|---:|---:|---|
| Dense Dirty Entity | 256 | 5.963 | 2.132 | 2.298 | 3.215 | 3.342 | within |
| Dense Dirty Entity | 320 | 7.682 | 2.680 | 2.907 | 3.837 | 3.901 | within |
| Dense Dirty Entity | **384** | 8.734 | 3.114 | **3.274** | 4.266 | 4.305 | **rated** |
| Dense Dirty Entity | 448 | 11.049 | 3.750 | **4.237** | 5.166 | 5.232 | stop row |
| Sparse Maintained Entity | 256 | 5.028 | 1.572 | 1.833 | 1.863 | 2.017 | within |
| Sparse Maintained Entity | **384** | 8.284 | 2.674 | **3.147** | 3.285 | 3.485 | **rated** |
| Sparse Maintained Entity | 512 | 11.244 | 3.821 | **4.456** | 4.674 | 4.722 | stop row |

The defensible measured rating is therefore **384 CCU at <=4 ms server-role p95**
for both tested shapes. This is not a 16 ms stress ceiling, public-WAN rating, or claim
that the two workloads have equivalent semantics. P99/max are retained as tail context
and are not the stop criterion.

## Correctness and regression coverage

Permanent Network tests now additionally prove:

- a lone ready item in 64 reliable/128 independent capacity does not scan inactive slots;
- an empty retry performs no additional slot probes;
- higher-priority OwnerState preempts already-pending WorldState;
- sustained IndependentState slot reuse preserves round-robin fairness and no starvation;
- interleaved Control/Event reliable work remains lane-priority ordered;
- retransmission with a newer same-lane reliable message preserves exact order.

Existing regressions continue to cover transactional capacity rejection, reliable
fragmentation/reassembly, loss/duplication/reorder/corruption, slot and generation reuse,
cap-one/multiframe semantic equivalence, and warmed zero-allocation delivery.

Verification:

- all Network N0.0/N0.2/N0.3/N0.5/N0.10.1/H0.1-H0.5 tests passed;
- 69/69 Network application tests passed;
- 125/125 five-profile Game replication tests passed;
- 22/22 Client tests passed;
- all whole-flow and rated rows converged with zero capacity drops.

## Decision

P0.3H is accepted. Entity profiles remain cap one; cap-eight is not made universal.
The next isolated experiment may address chunk application-proof envelopes. It is not
part of this checkpoint, so reverse proof traffic and proof meaning remain unchanged.

Artifacts:

- [`network-p0.3h-ready-selection-entity-controls-2026-08-24.json`](network-p0.3h-ready-selection-entity-controls-2026-08-24.json), SHA-256 `50ae1348ff283a54440036ca5a44326ff7fb36747bccfa37c962650195085430`;
- [`network-p0.3h-ready-selection-chunk-control-2026-08-24.json`](network-p0.3h-ready-selection-chunk-control-2026-08-24.json), SHA-256 `96997f354d732e5aab4034960ecd4231a193a042588248d9e88cd8e2926537c5`;
- [`network-p0.3h-rated-server-role-sweep-2026-08-24.json`](network-p0.3h-rated-server-role-sweep-2026-08-24.json), SHA-256 `dca01d7307ce0040ee8b53f8575d4fb36a1059f2bae8f50ae29e03ecc1190365`.
