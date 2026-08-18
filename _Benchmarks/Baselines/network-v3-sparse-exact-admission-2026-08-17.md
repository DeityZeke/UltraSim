# Network V3 sparse exact AOI admission/tiering experiment

- Workload: `sparse_observer_movement`
- Populations: 1,024 / 2,048 / 4,096
- Cadence: 62 Hz authority/Network, phased 20 Hz input
- Window: 10 settle ticks + 310 measured ticks
- Activity/space: 35% active, 12 m spacing, width-64 grid
- Geometry: 10 m broadphase, 7.5/23.5/39.5 m exact policy
- Transport: shared listener with platform send+receive batching
- Runtime: Release, tiered compilation and ReadyToRun disabled, deep timing

## Decision

Reject exact admission as a production performance change. It removes the
measured coarse-only relation set and lowers records, datagrams, wire bytes, and
protocol work, but the exact-mode recipient reconciliation scans the broadphase
candidate set on anchor movement. Interest-update p95 therefore rises enough
that server-role p95 is worse at every tested population.

| CCU | Mode | Tick p95 | Server-role p95 | Interest p95 | Schedule p95 | Protocol p95 | Relations | Records | Datagrams |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1,024 | Coarse | 19.197 | 9.973 | 1.674 | 0.828 | 7.360 | 48,760 | 2.453M | 485,316 |
| 1,024 | Exact | 20.266 | 11.752 | 4.101 | 0.642 | 6.998 | 33,672 | 1.475M | 428,676 |
| 2,048 | Coarse | 41.689 | 22.106 | 4.591 | 1.938 | 15.328 | 103,040 | 5.266M | 921,131 |
| 2,048 | Exact | 41.241 | 24.671 | 9.512 | 1.459 | 13.752 | 70,728 | 3.140M | 754,827 |
| 4,096 | Coarse | 97.070 | 50.779 | 10.470 | 5.789 | 33.636 | 211,600 | 10.844M | 1,759,972 |
| 4,096 | Exact | 95.553 | 55.810 | 21.041 | 3.295 | 30.938 | 144,840 | 6.469M | 1,416,681 |

At 4,096, exact admission removes 66,760 coarse-only relations (31.55%),
reduces WorldState records by 40.36%, and reduces wire bytes by 35.14%.
Nevertheless, server-role p95 worsens by 9.91%; interest-update p95 doubles
from 10.470 to 21.041 ms. The server-protocol reduction is real but cannot
repay the admission cost.

All rows had zero capacity drops and nonviable recipients, preserved canonical
owner-state hashes between modes, and retained bounded freshness. Exact mode's
Near/Far p95 was 1/2 ticks at all three populations; coarse mode was 2/2/3 at
4,096. This is a semantic and accounting success, not a performance win.

The exact-distance mode remains available as an opt-in diagnostic for a future
candidate-index design. It is not enabled by default and does not justify
another scale soak or further tuning of this full-scan implementation.

Machine-readable evidence: [`JSON`](network-v3-sparse-exact-admission-2026-08-17.json).
