# P0.3F five-profile scheduler-floor scale matrix

- Commit: `73a9e74ac14ea36f88d0b8bfb04ea951cdb2eec6`
- Runtime: .NET 8.0.29, Release, tiered compilation and ReadyToRun disabled
- Host: Unix 7.1.5.1, 16 logical processors
- Scope: Game replication profile core, canonical Game-codec bytes, payload-derived synthesized client, and immediate application proof
- Excluded: socket I/O, transport framing, security, packetization, and client engine/world storage
- Dense trace: 1,000 public entities, 2% spatial dirt/tick, 120 ticks, 30 warmup ticks
- Sparse trace: one entity and recipient per CCU, 35% matching entity/recipient motion/tick, 320 ticks, 10 warmup ticks
- Stop rule: complete the first run with p95 greater than 16 ms, or complete 4,096 CCU and stop even if still below 16 ms
- Capacity policy: sparse entity-family semantic rows use a bounded 64-row/recipient service envelope; dense retains the full 1,000-row/recipient envelope
- Validation: 250 scale artifacts, exact clean SHA in every artifact, zero warmed managed allocation, and semantic convergence/family equivalence in every run

Profile-native scheduling/source-operation counters are diagnostic and do not form a common additive work unit. Latency, materialized records, canonical messages/bytes, proofs, and reserved memory are the cross-profile comparison fields. Historical V2/V3 numbers include a different full owner/input/network scope and remain contextual controls, not direct ratio denominators.

## Implemented floor corrections

- Dirty Entity retains entity-linked old audiences, stages only new-anchor recipient entrants once, schedules through intrusive pending-tier and ready-recipient lists, and performs bundle expiry once per semantic time.
- Dirty Chunk uses an O(1) free delivery ledger, delivery-ID index, cadence-aware ready-recipient queue, and one expiration pass per cadence step. It retains dirty-time recipient discovery and does not add a Chunk-to-Recipient reverse graph.
- Maintained Entity uses its existing relation audience for exact-distance state-only or same-position horizon-shrink updates. Positional changes and horizon expansion still take exact discovery.
- Sparse comparison capacity is the scenario service envelope instead of a mathematical R×E reserve that the trace cannot occupy.

## Dense-400 historical direction and corrected result

After the P0.3F smoke run stalled, process inspection found an unrelated stale benchmark
that had consumed one full core for several hours. Its lifetime overlapped the P0.3E scale
captures. P0.3E has clean Git provenance and valid semantic/work/memory evidence, but its CPU
timings are operationally contaminated and must not be used as controlled speedup ratios. The
table preserves the historical values only to show the direction and magnitude that motivated
the scheduler-floor pass; P0.3F is the replacement CPU evidence.

| Profile | P0.3E p95 ms | P0.3F p95 ms | Observed change |
| --- | ---: | ---: | ---: |
| MaintainedEntityRelation | 2.727 | 2.634 | -3.4% |
| DirtyEntitySpatialFanout | 16.630 | 1.850 | -88.9% |
| DirtyEntityChunkBatchFanout | 16.395 | 2.082 | -87.3% |
| DirtyChunkSpatialFanout | 7.459 | 4.906 | -34.2% |
| ChunkPublication | 1.865 | 1.680 | -10.0% |

At sparse 768, service-envelope reservation reduced known memory from 142.49 to 52.28 MiB for Maintained Entity and from about 130.0 to 46.5 MiB for each dirty-entity profile. Chunk-family memory was effectively unchanged.

## Sparse scale matrix

Times are p95 milliseconds. `—` means that profile had already completed its stop run. ChunkPublication reached the 4,096 safety cap below 16 ms and was not run beyond it.

| CCU | Maintained | Dirty entity | Entity/chunk batch | Dirty chunk | Chunk publication |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 512 | 2.558 | 4.198 | 4.503 | 5.054 | 1.257 |
| 640 | 3.354 | 6.731 | 6.919 | 8.336 | 1.724 |
| 768 | 4.134 | 9.178 | 9.267 | 12.291 | 2.189 |
| 896 | 5.162 | 12.168 | 11.672 | **16.894** | 2.556 |
| 1,024 | 5.863 | 15.683 | 14.107 | — | 2.845 |
| 1,152 | 6.844 | **19.496** | **17.316** | — | 3.184 |
| 1,280 | 7.721 | — | — | — | 3.597 |
| 1,408 | 8.555 | — | — | — | 4.188 |
| 1,536 | 9.577 | — | — | — | 4.756 |
| 1,664 | 10.305 | — | — | — | 5.068 |
| 1,792 | 11.116 | — | — | — | 5.446 |
| 1,920 | 11.825 | — | — | — | 5.750 |
| 2,048 | 12.699 | — | — | — | 6.543 |
| 2,176 | 13.846 | — | — | — | 7.056 |
| 2,304 | 14.606 | — | — | — | 7.145 |
| 2,432 | **22.229** | — | — | — | 7.739 |
| 2,560 | — | — | — | — | 7.975 |
| 2,688 | — | — | — | — | 8.456 |
| 2,816 | — | — | — | — | 9.467 |
| 2,944 | — | — | — | — | 10.155 |
| 3,072 | — | — | — | — | 10.049 |
| 3,200 | — | — | — | — | 10.754 |
| 3,328 | — | — | — | — | 11.072 |
| 3,456 | — | — | — | — | 11.829 |
| 3,584 | — | — | — | — | 12.530 |
| 3,712 | — | — | — | — | 13.677 |
| 3,840 | — | — | — | — | 14.065 |
| 3,968 | — | — | — | — | 14.998 |
| 4,096 | — | — | — | — | **15.076 safety cap** |

## Dense scale matrix

Times are p95 milliseconds. `—` means that profile had already completed its first run above 16 ms.

| CCU | Maintained | Dirty entity | Entity/chunk batch | Dirty chunk | Chunk publication |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 128 | 0.969 | 0.595 | 0.684 | 1.536 | 0.511 |
| 192 | 1.342 | 0.930 | 1.001 | 2.294 | 0.819 |
| 256 | 1.745 | 1.288 | 1.440 | 3.095 | 1.075 |
| 320 | 2.108 | 1.578 | 1.695 | 3.989 | 1.373 |
| 384 | 2.660 | 1.899 | 2.074 | 4.624 | 1.644 |
| 448 | 3.073 | 2.195 | 2.493 | 5.439 | 1.986 |
| 512 | 3.471 | 2.570 | 3.027 | 6.370 | 2.239 |
| 576 | 3.740 | 2.808 | 3.145 | 7.422 | 2.511 |
| 640 | 4.275 | 3.093 | 3.597 | 7.849 | 2.809 |
| 704 | 4.696 | 3.548 | 3.854 | 8.529 | 3.088 |
| 768 | 5.200 | 3.893 | 4.349 | 9.365 | 3.380 |
| 832 | 5.809 | 4.046 | 4.598 | 10.423 | 3.644 |
| 896 | 6.309 | 4.434 | 4.884 | 10.843 | 3.971 |
| 960 | 6.596 | 4.595 | 5.150 | 11.563 | 4.274 |
| 1,024 | 7.149 | 4.995 | 5.769 | 12.462 | 4.539 |
| 1,088 | 7.425 | 5.381 | 6.118 | 13.174 | 4.786 |
| 1,152 | 8.042 | 5.557 | 6.220 | 14.114 | 5.406 |
| 1,216 | 8.529 | 5.740 | 6.522 | 14.462 | 5.396 |
| 1,280 | 9.144 | 6.312 | 6.993 | 15.841 | 5.623 |
| 1,344 | 9.854 | 6.367 | 7.320 | **16.319** | 6.136 |
| 1,408 | 10.443 | 6.858 | 7.922 | — | 6.591 |
| 1,472 | 11.013 | 7.013 | 8.559 | — | 6.466 |
| 1,536 | 12.308 | 7.588 | 8.977 | — | 6.818 |
| 1,600 | 12.098 | 7.647 | 9.015 | — | 7.166 |
| 1,664 | 12.923 | 7.990 | 9.088 | — | 7.786 |
| 1,728 | 11.558 | 8.527 | 10.046 | — | 7.671 |
| 1,792 | 12.288 | 8.919 | 10.505 | — | 8.052 |
| 1,856 | 12.750 | 9.166 | 10.855 | — | 8.275 |
| 1,920 | 13.220 | 9.652 | 11.174 | — | 8.468 |
| 1,984 | 15.361 | 10.181 | 11.683 | — | 8.863 |
| 2,048 | 14.885 | 10.522 | 12.925 | — | 9.149 |
| 2,112 | 15.108 | 10.889 | 13.152 | — | 9.566 |
| 2,176 | 15.016 | 11.228 | 13.220 | — | 9.809 |
| 2,240 | **17.728** | 11.444 | 13.865 | — | 10.436 |
| 2,304 | — | 12.296 | 14.101 | — | 10.790 |
| 2,368 | — | 11.625 | 15.408 | — | 10.599 |
| 2,432 | — | 12.468 | 14.496 | — | 11.163 |
| 2,496 | — | 13.638 | 15.715 | — | 11.554 |
| 2,560 | — | 13.519 | **16.055** | — | 11.646 |
| 2,624 | — | 14.141 | — | — | 11.422 |
| 2,688 | — | 14.420 | — | — | 13.639 |
| 2,752 | — | 15.032 | — | — | 12.505 |
| 2,816 | — | 15.294 | — | — | 12.858 |
| 2,880 | — | **16.118** | — | — | 13.407 |
| 2,944 | — | — | — | — | 14.095 |
| 3,008 | — | — | — | — | 13.926 |
| 3,072 | — | — | — | — | 14.590 |
| 3,136 | — | — | — | — | 14.841 |
| 3,200 | — | — | — | — | 15.358 |
| 3,264 | — | — | — | — | 15.757 |
| 3,328 | — | — | — | — | 15.624 |
| 3,392 | — | — | — | — | **16.969** |

## Stop-run breakdown

| Shape/profile | CCU | p50/p95/p99/max ms | Records | Game payload MiB | Known reserve MiB | Stop reason |
| --- | ---: | --- | ---: | ---: | ---: | --- |
| Sparse Maintained | 2,432 | 15.387/22.229/24.142/28.727 | 3,707,920 | 156.50 | 360.41 | p95 > 16 ms |
| Sparse Dirty entity | 1,152 | 18.755/19.496/19.677/19.887 | 1,239,913 | 53.54 | 91.38 | p95 > 16 ms |
| Sparse Entity/chunk batch | 1,152 | 16.545/17.316/17.485/17.992 | 1,239,913 | 53.54 | 91.44 | p95 > 16 ms |
| Sparse Dirty chunk | 896 | 16.498/16.894/16.996/17.089 | 2,154,477 | 193.38 | 69.56 | p95 > 16 ms |
| Sparse Chunk publication | 4,096 | 13.860/15.076/16.278/17.333 | 11,068,194 | 933.60 | 950.10 | 4,096 safety cap |
| Dense Maintained | 2,240 | 15.254/17.728/18.539/18.539 | 7,499,659 | 308.48 | 768.60 | p95 > 16 ms |
| Dense Dirty entity | 2,880 | 14.597/16.118/17.479/17.479 | 9,543,482 | 392.59 | 814.08 | p95 > 16 ms |
| Dense Entity/chunk batch | 2,560 | 14.915/16.055/16.614/16.614 | 8,483,086 | 348.96 | 725.77 | p95 > 16 ms |
| Dense Dirty chunk | 1,344 | 13.371/16.319/17.416/17.416 | 4,569,600 | 268.32 | 340.35 | p95 > 16 ms |
| Dense Chunk publication | 3,392 | 13.600/16.969/18.620/18.620 | 11,532,800 | 677.18 | 954.47 | p95 > 16 ms |

## Interpretation

The reviews' main diagnosis was correct: direct inspection proved that the dirty profiles
performed avoidable scheduler/ledger scans, and P0.3F measures the corrected implementations at
1.85 ms p95 for Dirty Entity, 2.08 ms for entity/chunk batch, and 4.91 ms for Dirty Chunk at dense
400 without changing semantic identity, proof, cadence, or wire format. The contaminated P0.3E
CPU rows are consistent with the diagnosis but are not quantitative before/after evidence.
Dirty Chunk's dirty-time recipient discovery remains materially more expensive than retained
ChunkPublication.

The workload crossover is now visible rather than normalized away. Dirty Entity is the best dense entity-family implementation here and crosses the dense 16 ms gate later than Maintained Entity, but it remains worse in sparse movement because recipient relocation must rediscover possible entity entrants. Entity/chunk batching adds overhead when spatial dirt is distributed rather than clustered. ChunkPublication remains the strongest current-game profile in both scale shapes; DirtyChunk's lack of a retained Chunk-to-Recipient graph is an observable cost, not a harness artifact.

The sparse Maintained stop at 2,432 includes a real p95 cliff (14.606 ms at 2,304 to 22.229 ms at 2,432, while p50 is 15.387 ms). The stop rule is intentionally mechanical, so the sweep did not cherry-pick a retry. This is a measurement boundary, not a production CCU claim.
