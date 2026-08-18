# V3 sparse WorldState identity-ledger compaction — 4K long-lived acceptance

Date: 2026-08-17  
Scenario: `sparse_observer_movement`  
Clients: 4,096  
Measured window: 55,800 ticks at 60 Hz (15 logical minutes) plus 60 untimed settle ticks  
Topology: one shared UDP listener with platform send/receive batching  
Capacity: explicit `current10`; 112 known / 112 active capacity per client; observed active maximum 67
Compaction policy: headroom 16, safety margin 16, one baseline start per tick  

## Result

The run passed the N0.10 compaction acceptance gate. It completed 10,596
pressure requests and 10,596 maintenance baselines with zero scheduler debt at
the end. All baselines applied; none were rejected, timed out, abandoned, or
left in progress. The client known-identity histogram was p50 77, p95 79,
maximum 82, below the admitted capacity of 112. Active-entity maximum was 67.

The final canonical owner-state hash was `dbaab44b9267a342`. WorldState had
zero pending relations and zero outstanding bundles. Transport capacity drops,
retained-payload byte failures, nonviable recipients, and shared-listener
capacity drops were all zero.

| Metric | Result |
| --- | ---: |
| Tick p50 / p95 / p99 / max | 206.049 / 226.295 / 233.196 / 462.629 ms |
| Server-role p50 / p95 / p99 / max | 156.105 / 172.456 / 178.493 / 360.475 ms |
| Pressure → baseline start p50 / p95 / max | 157 / 927 / 1,219 ticks |
| Baseline duration p50 / p95 / max | 6 / 7 / 17 ticks |
| Baseline records queued | 610,045 |
| Baseline wire bytes queued | 27,762,282 |
| Tick deadline misses | 55,800 / 55,800 |
| Allocated bytes | 37,908,776,144 |
| GC Gen0 / Gen1 / Gen2 | 763 / 68 / 7 |
| Working set / private bytes | 2,192,408,576 / 4,609,712,128 B |

This is a lifetime/convergence result, not a 60 Hz feasibility result: the
4K workload remains over budget on every measured tick. The compaction path is
accepted as a bounded opt-in lifetime mechanism; the CPU ceiling and policy
headroom remain separate follow-up questions.

The machine-readable artifact is
[`network-v3-sparse-worldstate-compaction-2026-08-17.json`](network-v3-sparse-worldstate-compaction-2026-08-17.json).
