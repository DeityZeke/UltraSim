# P0.3J probes-off rated server-role sweep

Date: 2026-08-24

Harness checkpoint: `6969f43836693f14718b0d266d0e14d9431b4850`

## Scope

This is the P0.3H rated whole-flow sweep repeated with protocol-peer and shared-listener
stage timing disabled. Normal production counters, semantic validation, capacity/drop
tracking, Game replication, codecs, Network queues, security, real shared UDP sockets,
client application, reverse proofs, and server proof application remain active.

- Warmup: 30 ticks.
- Measured: 270 ticks per completed population.
- Rating criterion: server-role p95 `<= 4.000 ms`.
- Dense: `DirtyEntitySpatialFanout`, 1,000 public entities, cap one, start 384, step 64.
- Sparse: `MaintainedEntityRelation`, one moving public entity per recipient, cap one,
  start 384, step 128.
- Each series retains and finishes its first population above 4 ms, then stops.
- Cold construction/bootstrap and final semantic verification remain outside timed ticks.
- This is localhost evidence, not public-WAN capacity or a deployment SLA.

## Results

| Workload | Tick p95 | Server p50 | Server p95 | Server p99 | Server max | Capacity drops |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Dense-384 | 8.134 ms | 2.139 ms | 2.263 ms | 2.786 ms | 2.825 ms | 0 |
| Dense-448 | 9.527 ms | 2.507 ms | 2.670 ms | 3.193 ms | 3.294 ms | 0 |
| Dense-512 | 11.270 ms | 2.969 ms | 3.189 ms | 3.753 ms | 3.885 ms | 0 |
| Dense-576 | 12.630 ms | 3.206 ms | 3.444 ms | 4.038 ms | 4.117 ms | 0 |
| Dense-640 | 14.346 ms | 3.678 ms | **3.969 ms** | 4.473 ms | 4.759 ms | 0 |
| Dense-704 | 17.939 ms | 4.103 ms | **4.900 ms** | 5.498 ms | 7.078 ms | 0 |
| Sparse-384 | 7.364 ms | 1.641 ms | 2.080 ms | 2.158 ms | 2.195 ms | 0 |
| Sparse-512 | 10.262 ms | 2.390 ms | **3.032 ms** | 3.130 ms | 3.200 ms | 0 |
| Sparse-640 | 13.153 ms | 3.150 ms | **4.081 ms** | 4.207 ms | 4.342 ms | 0 |

Every row converged and retained its population-specific semantic digest. Forward Game
messages and application proofs match exactly in every row. The benchmark reports only
small measurement-thread allocations already present in prior whole-flow captures; no
capacity drop, retry failure, or semantic rejection occurred.

## Rating

```text
Dense DirtyEntity observed rating:       640 CCU at 3.969 ms p95
Dense first completed failure:            704 CCU at 4.900 ms p95

Sparse MaintainedEntity observed rating:  512 CCU at 3.032 ms p95
Sparse first completed failure:           640 CCU at 4.081 ms p95
```

Dense 640 is only 0.031 ms (0.8%) under the formal cutoff. It is the exact observed
rating under the frozen rule, but it is not a comfortable operating margin. Dense 576
is the last row with substantial p95 margin, although its p99 exceeds 4 ms. Sparse 512
has materially more p95 and tail margin.

## Relation to the P0.3H capture

The prior P0.3H rated artifact deliberately left deep protocol/listener timing enabled
and rated both shapes at 384. At matching populations, disabling those probes changes
server-role p95 as follows:

| Workload | P0.3H probes on | P0.3J probes off | Change |
| --- | ---: | ---: | ---: |
| Dense-384 | 3.274 ms | 2.263 ms | -30.9% |
| Dense-448 | 4.237 ms | 2.670 ms | -37.0% |
| Sparse-384 | 3.147 ms | 2.080 ms | -33.9% |
| Sparse-512 | 4.456 ms | 3.032 ms | -32.0% |

This does not represent a production optimization after P0.3H. It corrects the rating
methodology by removing diagnostic stopwatch/counter accumulation from the workload
being rated. P0.3H's causal attribution remains valid evidence for the selector change;
P0.3J supersedes only its absolute rated-CCU observation.

## Evidence

- Raw JSON: `network-p0.3j-rated-server-role-probes-off-2026-08-24.json`
- SHA-256: `d15bd98fe61d540655d3e600008ed90f1787df407b776b12ebfecb430b082b77`
- Benchmark build: zero warnings and zero errors.

## Verdict

P0.3J is accepted as the rated-server-role measurement closure before P0.4. The
observed p95 ratings are Dense 640 and Sparse 512 under the strict completed-row rule,
with the Dense 640 margin explicitly classified as narrow. It changes no replication,
Network, proof, or socket behavior.
