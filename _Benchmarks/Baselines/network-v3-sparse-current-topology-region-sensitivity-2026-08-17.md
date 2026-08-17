# Network V3 sparse current-topology and region sensitivity

- Workload: `sparse_observer_movement`
- Cadence: 62 Hz authority/Network, phased 20 Hz input
- Window: 10 settle ticks + 310 measured ticks
- Activity/space: 35% active, 12 m spacing, width-64 grid
- Exact game policy: 7.5/23.5/39.5 m Immediate/Near/Far
- Transport: shared listener with platform send+receive batching
- Capacity/cadence/protocol: unchanged across observations

## Current accepted transport normalization

The ordinary probes-off four-point rerun replaces stale send-only transport as
the current sparse topology observation. Canonical owner state matches and all
rows preserve 1/2/3-step Immediate/Near/Far p95, zero capacity drops, and zero
nonviable recipients.

| CCU | V2 p95 ms | V3 p95 ms | Prior send-only V3 p95 ms | Maintained / exact / coarse-only relations | False positive |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1,024 | 6.587 | 19.404 | 22.433 | 76,180 / 33,672 / 42,508 | 55.80% |
| 2,048 | 12.962 | 42.944 | 52.080 | 165,252 / 70,728 / 94,524 | 57.20% |
| 3,072 | 21.014 | 72.009 | 86.799 | 254,324 / 107,784 / 146,540 | 57.62% |
| 4,096 | 28.540 | 100.483 | 121.154 | 343,396 / 144,840 / 198,556 | 57.82% |

Native receive batching removes about 13.5–17.6% from the corrected V3 p95,
but does not explain the remaining sparse gap. At 4,096 the 16 m envelope still
maintains 2.37x the exact 39.5 m relation set.

## Region-edge sensitivity

These rows enable deep phase probes and must be compared with each other, not
with the ordinary probes-off table above.

| CCU | Edge | Region edges | Relations / exact / coarse-only | False + | Integrated mean/p95 ms | Server-role mean/p95 ms | Interest mean / schedule mean ms |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1,024 | 16 m | 50,176 | 76,180 / 33,672 / 42,508 | 55.80% | 17.316 / 20.667 | 8.206 / 10.896 | 2.218 / 0.992 |
| 1,024 | 10 m | 82,944 | 48,760 / 33,672 / 15,088 | 30.94% | 14.610 / 18.067 | 6.552 / 9.300 | 1.511 / 0.707 |
| 1,024 | 8 m | 123,904 | 43,600 / 33,672 / 9,928 | 22.77% | 13.948 / 17.500 | 6.178 / 9.058 | 1.354 / 0.615 |
| 4,096 | 16 m | 200,704 | 343,396 / 144,840 / 198,556 | 57.82% | 89.220 / 109.697 | 44.565 / 58.236 | 12.773 / 7.327 |
| 4,096 | 10 m | 331,776 | 211,600 / 144,840 / 66,760 | 31.55% | 71.060 / 88.240 | 33.653 / 46.879 | 8.886 / 4.749 |
| 4,096 | 8 m | 495,616 | 190,096 / 144,840 / 45,256 | 23.81% | 66.288 / 82.306 | 32.092 / 44.948 | 9.992 / 4.189 |

At 4,096, 16→10 m removes 131,796 relations, 14.60M pending writes, 5.55M
emitted records, and 253.9 MiB of wire. Server-role p95 improves 19.5%.

The 10→8 m step removes another 21,504 relations and improves server-role p95
only 4.1%, while adding 163,840 region edges. Interest-update mean actually
rises from 8.886 to 9.992 ms. This makes 10 m the current evidence-backed knee,
not an automatic production-default change.

| CCU | Edge | Pending writes | Emitted records | Bundles | Wire datagrams / MiB | Network + replication reserve MiB | Working set MiB |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1,024 | 16 m | 8.138M | 3.508M | 252,305 | 583,448 / 188.0 | 229.84 | 363.19 |
| 1,024 | 10 m | 5.316M | 2.453M | 202,722 | 484,902 / 138.2 | 233.29 | 360.10 |
| 1,024 | 8 m | 4.672M | 2.078M | 182,346 | 444,334 / 119.8 | 237.31 | 364.36 |
| 4,096 | 16 m | 38.342M | 16.389M | 921,881 | 2,182,869 / 830.5 | 918.63 | 1,191.25 |
| 4,096 | 10 m | 23.746M | 10.843M | 707,613 | 1,758,885 / 576.6 | 932.89 | 1,188.79 |
| 4,096 | 8 m | 21.233M | 9.341M | 614,497 | 1,572,482 / 501.1 | 949.42 | 1,196.86 |

All six rows had zero capacity drops/nonviable recipients and bounded queue
occupancy. Exact-scope p95 is 37 identities at both scales. Maintained-scope p95
falls from 100 at 16 m to 64 at 10 m and 49 at 8 m. The current 128-identity
capacity was intentionally unchanged, so this checkpoint does not claim the
future reservation savings from an evidence-backed tighter scope profile.

## Interpretation

The sparse cost is dominated by legitimately admitted conservative work, not
the corrected pending-obligation mechanics or unhealthy transport. A 10 m cell
is the best current CPU/memory tradeoff candidate: it captures most of the 8 m
CPU and wire gain with materially fewer region edges. The 16 m production and
benchmark default remains frozen pending an alternating repeat and a separate
scope-capacity/reservation decision.

The cold census allocates temporary diagnostic storage and independently scans
all recipient/entity pairs outside measured tick time. Its allocation is
reported per artifact. Exact/coarse attribution of historical pending writes
and emitted records is explicitly unmeasured; the report does not infer it from
the final relation ratio.
