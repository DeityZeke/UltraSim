# Five-profile dense/sparse scale matrix

> **CPU provenance warning (2026-08-24):** a later process audit found an unrelated stale
> benchmark consuming a full core whose lifetime overlapped these P0.3E captures. Git provenance,
> semantic convergence, work counts, bytes, and bounded-memory evidence remain valid. CPU timing
> rows are operationally contaminated and are superseded by the clean P0.3F scheduler-floor
> matrix; do not use them for controlled ratios or selection.

- Commit: `cb274575fe17a67781af1476fcfacd23282c02a0`
- Runtime: .NET 8.0.29, Release, tiered compilation and ReadyToRun disabled
- Host: Unix 7.1.5.1, 16 logical processors
- Scope: Game replication profile core, canonical Game-codec byte measurement, synthesized payload-derived client, and immediate application proof
- Excluded: socket I/O, transport framing, security, packetization, and client engine/world storage
- Result: every retained run converged; the entity-family and chunk-family semantic digests matched within their declared AOI policy

The historical V2/V3 sparse matrix is a full owner/input/network loop, while this matrix isolates the five Game replication profiles. Historical timing is included only as context and is not used as a ratio denominator.

## Dense matrix

The dense workload uses 400 recipients, 1,000 public entities, 120 ticks, 30 warmup ticks, two percent spatial dirt per tick, and 4 m entity/recipient spacing.

| Profile | p50 ms | p95 ms | p99/max ms | Records | Game payload MiB | Known reserve MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ChunkPublication | 1.518 | 1.865 | 2.089/2.089 | 1,360,000 | 79.85 | 114.30 |
| DirtyChunkSpatialFanout | 5.953 | 7.459 | 7.719/7.719 | 1,360,000 | 79.85 | 102.51 |
| DirtyEntityChunkBatchFanout | 11.542 | 16.395 | 17.546/17.546 | 1,325,477 | 54.53 | 108.24 |
| DirtyEntitySpatialFanout | 11.449 | 16.630 | 17.117/17.117 | 1,325,477 | 54.53 | 108.20 |
| MaintainedEntityRelation | 2.547 | 2.727 | 2.890/2.890 | 1,339,214 | 55.09 | 139.82 |

## Sparse matrix

The sparse workload starts at 512 recipients/entities and increases both by 128. It runs 320 ticks with 10 warmup ticks, moves 35 percent of active entities and matching recipients per tick, uses 12 m spacing and grid width 64, and stops each profile after its first completed run above 16 ms p95.

Times below are p95 milliseconds. `—` means the profile had already crossed the stop threshold.

| Recipients | Maintained entity | Chunk publication | Dirty entity | Entity/chunk batch | Dirty chunk |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 512 | 2.454 | 1.174 | 8.776 | 8.931 | 8.115 |
| 640 | 3.405 | 1.697 | 13.401 | 13.634 | 13.613 |
| 768 | 4.062 | 2.045 | **18.476** | **18.628** | **20.348** |
| 896 | 4.911 | 2.622 | — | — | — |
| 1,024 | 5.758 | 2.801 | — | — | — |
| 1,152 | 6.543 | 3.111 | — | — | — |
| 1,280 | 7.505 | 3.470 | — | — | — |
| 1,408 | 8.307 | 4.095 | — | — | — |
| 1,536 | 9.035 | 4.550 | — | — | — |
| 1,664 | 9.762 | 4.963 | — | — | — |
| 1,792 | 10.656 | 5.194 | — | — | — |
| 1,920 | 11.507 | 5.455 | — | — | — |
| 2,048 | 12.802 | 6.145 | — | — | — |
| 2,176 | 13.532 | 6.497 | — | — | — |
| 2,304 | 14.244 | 7.188 | — | — | — |
| 2,432 | 15.353 | 7.307 | — | — | — |
| 2,560 | **16.024** | 7.625 | — | — | — |
| 2,688 | — | 8.264 | — | — | — |
| 2,816 | — | 8.691 | — | — | — |
| 2,944 | — | 9.225 | — | — | — |
| 3,072 | — | 9.713 | — | — | — |
| 3,200 | — | 10.235 | — | — | — |
| 3,328 | — | 10.289 | — | — | — |
| 3,456 | — | 11.100 | — | — | — |
| 3,584 | — | 12.186 | — | — | — |
| 3,712 | — | 12.676 | — | — | — |
| 3,840 | — | 12.983 | — | — | — |
| 3,968 | — | 13.970 | — | — | — |
| 4,096 | — | 14.907 | — | — | — |
| 4,224 | — | 14.497 | — | — | — |
| 4,352 | — | 14.576 | — | — | — |
| 4,480 | — | 14.566 | — | — | — |
| 4,608 | — | **16.143** | — | — | — |

## First completed run above 16 ms

| Profile | Recipients | p50/p95/p99/max ms | Records | Game payload MiB | Known reserve MiB |
| --- | ---: | --- | ---: | ---: | ---: |
| DirtyEntitySpatialFanout | 768 | 18.113/18.476/18.598/19.177 | 781,632 | 34.05 | 130.01 |
| DirtyEntityChunkBatchFanout | 768 | 18.264/18.628/18.849/27.886 | 781,632 | 34.05 | 130.04 |
| DirtyChunkSpatialFanout | 768 | 20.047/20.348/20.440/20.675 | 1,792,407 | 157.79 | 55.18 |
| MaintainedEntityRelation | 2,560 | 15.829/16.024/16.115/16.201 | 3,914,653 | 165.14 | 1,454.52 |
| ChunkPublication | 4,608 | 15.149/16.143/16.716/23.917 | 12,488,110 | 1,069.31 | 1,188.65 |

## Historical full-network context

The retained V2/V3 historical sparse artifact begins at 1,024 and uses 256-client increments, so there are no historical V2 rows for 512–896. It also has no equivalent dense row. The values below include the old full owner/input/network loop and therefore must not be compared as direct speedup ratios with the new semantic-core timings.

| Recipients | Historical V2 p95 ms | Historical V3 p95 ms | New Maintained core p95 ms | New ChunkPublication core p95 ms |
| ---: | ---: | ---: | ---: | ---: |
| 1,024 | 6.988 | 22.433 | 5.758 | 2.801 |
| 1,280 | 8.655 | 30.372 | 7.505 | 3.470 |
| 1,536 | 10.744 | 37.992 | 9.035 | 4.550 |
| 1,792 | 12.736 | 43.498 | 10.656 | 5.194 |
| 2,048 | 14.569 | 52.080 | 12.802 | 6.145 |
| 2,304 | **16.801** | 61.678 | 14.244 | 7.188 |

Historical V2 first exceeds 16 ms at 2,304 in its full-network run. The new semantic-core matrix first exceeds 16 ms at 2,560 for MaintainedEntityRelation and 4,608 for ChunkPublication. These thresholds characterize different measurement scopes.

## Correctness note

The first sparse run exposed a maintained-profile exact-distance bug: a Far entity could enter exact range while remaining inside the same coarse replication region, but the coarse-only stable-Far shortcut skipped entrant discovery. `GameInterestService` now restricts that shortcut to coarse-envelope admission, and a permanent same-region exact-boundary regression covers the case.
