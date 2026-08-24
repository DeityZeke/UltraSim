# Five-profile dense scaling matrix

- Source commit: `173c4cc171d94a34602617b9ed8e78ab27e82cec`
- Runtime: .NET 8.0.29, Release, tiered compilation and ReadyToRun disabled
- Host: Unix 7.1.5.1, 16 logical processors
- Workload: 1,000 public entities at 4 m spacing; 2 percent spatial dirt per tick; 120 ticks; 30 warmup ticks
- Scale axis: 128 recipients initially, increasing by 64
- Stop rule: each profile stops after its first completed run above 16 ms p95
- Scope: Game replication profile core, canonical Game-codec byte measurement, synthesized payload-derived client, and immediate application proof
- Excluded: socket I/O, transport framing, security, packetization, and client engine/world storage
- Correctness: every retained run converged

## First completed run above 16 ms

| Profile | Recipients | p50/p95/p99/max ms | Records | Game payload MiB | Known reserve MiB |
| --- | ---: | --- | ---: | ---: | ---: |
| DirtyEntitySpatialFanout | 384 | 11.473/16.025/16.750/16.750 | 1,272,458 | 52.34 | 103.04 |
| DirtyEntityChunkBatchFanout | 384 | 12.478/17.665/18.281/18.281 | 1,272,458 | 52.34 | 103.07 |
| DirtyChunkSpatialFanout | 704 | 13.820/17.195/18.230/18.230 | 2,393,600 | 140.54 | 179.18 |
| MaintainedEntityRelation | 2,304 | 15.642/16.164/16.380/16.380 | 7,713,871 | 317.29 | 786.08 |
| ChunkPublication | 3,520 | 12.928/16.139/17.693/17.693 | 11,968,000 | 702.73 | 988.02 |

## Full p95 matrix

Times are milliseconds. `—` means the profile had already crossed the stop threshold.

| Recipients | Maintained entity | Chunk publication | Dirty entity | Entity/chunk batch | Dirty chunk |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 128 | 0.797 | 0.526 | 5.063 | 5.098 | 1.519 |
| 192 | 1.227 | 0.794 | 7.653 | 7.730 | 2.642 |
| 256 | 1.638 | 1.101 | 11.255 | 10.534 | 3.807 |
| 320 | 2.005 | 1.380 | 14.520 | 12.960 | 5.183 |
| 384 | 2.504 | 1.703 | **16.025** | **17.665** | 6.827 |
| 448 | 2.872 | 1.908 | — | — | 8.700 |
| 512 | 3.355 | 2.212 | — | — | 10.464 |
| 576 | 3.720 | 2.423 | — | — | 12.751 |
| 640 | 4.163 | 2.715 | — | — | 14.785 |
| 704 | 4.632 | 3.050 | — | — | **17.195** |
| 768 | 5.112 | 3.443 | — | — | — |
| 832 | 5.631 | 3.608 | — | — | — |
| 896 | 6.046 | 3.765 | — | — | — |
| 960 | 6.191 | 4.250 | — | — | — |
| 1,024 | 6.675 | 4.410 | — | — | — |
| 1,088 | 7.037 | 4.793 | — | — | — |
| 1,152 | 7.561 | 5.025 | — | — | — |
| 1,216 | 7.932 | 5.591 | — | — | — |
| 1,280 | 8.478 | 5.572 | — | — | — |
| 1,344 | 8.942 | 5.910 | — | — | — |
| 1,408 | 9.561 | 6.202 | — | — | — |
| 1,472 | 9.901 | 6.735 | — | — | — |
| 1,536 | 10.700 | 7.923 | — | — | — |
| 1,600 | 11.396 | 7.890 | — | — | — |
| 1,664 | 11.715 | 7.991 | — | — | — |
| 1,728 | 11.446 | 8.620 | — | — | — |
| 1,792 | 15.263 | 8.661 | — | — | — |
| 1,856 | 12.449 | 9.189 | — | — | — |
| 1,920 | 13.052 | 9.752 | — | — | — |
| 1,984 | 13.653 | 10.374 | — | — | — |
| 2,048 | 14.833 | 11.587 | — | — | — |
| 2,112 | 14.932 | 9.480 | — | — | — |
| 2,176 | 15.205 | 9.749 | — | — | — |
| 2,240 | 15.730 | 10.351 | — | — | — |
| 2,304 | **16.164** | 10.219 | — | — | — |
| 2,368 | — | 11.015 | — | — | — |
| 2,432 | — | 10.468 | — | — | — |
| 2,496 | — | 11.397 | — | — | — |
| 2,560 | — | 11.313 | — | — | — |
| 2,624 | — | 11.921 | — | — | — |
| 2,688 | — | 11.663 | — | — | — |
| 2,752 | — | 12.066 | — | — | — |
| 2,816 | — | 12.285 | — | — | — |
| 2,880 | — | 13.070 | — | — | — |
| 2,944 | — | 13.737 | — | — | — |
| 3,008 | — | 12.991 | — | — | — |
| 3,072 | — | 13.542 | — | — | — |
| 3,136 | — | 13.682 | — | — | — |
| 3,200 | — | 14.344 | — | — | — |
| 3,264 | — | 14.166 | — | — | — |
| 3,328 | — | 15.533 | — | — | — |
| 3,392 | — | 15.505 | — | — | — |
| 3,456 | — | 15.327 | — | — | — |
| 3,520 | — | **16.139** | — | — | — |

The non-monotonic rows are retained as observed rather than smoothed. The stop rule is based on the first completed run whose measured p95 exceeds 16 ms.

No V2 dense run was repeated: the retained historical V2/V3 artifact contains only the sparse observer-movement matrix and has no comparable dense row. Manufacturing a comparison from that workload would conflate density, AOI policy, and full-network versus semantic-core scope.
