# P0.3K 33 ms semantic-cadence rated sweep

> Retained historical experiment: this capture changed the semantic clock and derived
> AOI periods from millisecond intervals. It did **not** independently hold AOI cadence
> constant while changing the server-step deadline. For that apples-to-apples matrix,
> see `network-p0.3k-server-tick-aoi-cadence-matrix-2026-08-24.md`.

Date: 2026-08-24

Harness checkpoint: `400e5d50f6187f97851987aae03c213cd79ea411`

## Scope

This is a same-checkpoint A/B of the P0.3J probes-off rated whole-flow sweep. The
experimental row advances the internal semantic clock by 33 ms per tick. The control
retains the frozen 16 ms clock. The benchmark is not wall-clock paced; 33 ms changes
Game interest eligibility and the interpreted simulation duration, not the duration of
the benchmark process.

- Warmup: 30 ticks.
- Measured: 270 ticks per completed population.
- Rating criterion: server-role p95 `<= 4.000 ms` in both captures.
- Dense: `DirtyEntitySpatialFanout`, 1,000 public entities, cap one, start 384, step 64.
- Sparse: `MaintainedEntityRelation`, one moving public entity per recipient, cap one,
  start 384, step 128.
- Protocol/listener stage probes are disabled. Normal counters, Game and Network work,
  real shared UDP sockets, semantic application/proof, capacity tracking, and final
  validation remain enabled.
- Each series retains and finishes its first population above 4 ms, then stops.
- This is localhost evidence, not a public-WAN CCU or latency SLA.

The dense entity policy is Near 33 ms / Far 50 ms. The sparse entity policy is Near
32 ms / Far 48 ms. At a 33 ms sampled clock, both resolve to the expected bounded
tick envelope. The corresponding chunk cadence is explicitly derived as:

```text
Immediate / Near / Far = 1 / 1 / 2 ticks
```

Entity profiles retain their millisecond, stable-phase eligibility rather than being
rewritten as a step counter. In practical sampled-clock terms, Immediate is eligible in
the current tick, Near by the next tick, and Far by the second tick. The JSON records
both the millisecond policy and derived chunk periods in every row.

## Rated results at 33 ms

| Workload | Tick p95 | Server p50 | Server p95 | Server p99 | Server max | Client p95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Dense-384 | 7.410 ms | 1.743 ms | 1.874 ms | 2.262 ms | 2.307 ms | 0.671 ms |
| Dense-448 | 8.665 ms | 2.030 ms | 2.175 ms | 2.549 ms | 2.586 ms | 0.801 ms |
| Dense-512 | 9.893 ms | 2.410 ms | 2.537 ms | 3.018 ms | 3.130 ms | 0.888 ms |
| Dense-576 | 11.151 ms | 2.638 ms | 2.779 ms | 3.296 ms | 3.310 ms | 1.012 ms |
| Dense-640 | 12.532 ms | 3.011 ms | 3.155 ms | 3.647 ms | 3.713 ms | 1.178 ms |
| Dense-704 | 14.986 ms | 3.224 ms | 3.641 ms | 3.857 ms | 3.925 ms | 1.399 ms |
| Dense-768 | 15.324 ms | 3.716 ms | **3.887 ms** | 4.264 ms | 4.374 ms | 1.423 ms |
| Dense-832 | 17.144 ms | 3.935 ms | **4.243 ms** | 4.478 ms | 4.880 ms | 1.614 ms |
| Sparse-384 | 6.958 ms | 1.544 ms | 1.951 ms | 1.971 ms | 1.993 ms | 0.278 ms |
| Sparse-512 | 9.530 ms | 2.179 ms | 2.751 ms | 2.806 ms | 2.837 ms | 0.393 ms |
| Sparse-640 | 12.121 ms | 2.786 ms | **3.652 ms** | 3.702 ms | 3.734 ms | 0.511 ms |
| Sparse-768 | 14.880 ms | 3.488 ms | **4.607 ms** | 4.698 ms | 4.759 ms | 0.632 ms |

Every row converged. Forward Game messages and application proofs match exactly in
every row. There were zero capacity drops and zero retransmissions.

## Controlled 16 ms versus 33 ms comparison

The 16 ms control was rerun immediately on the same executable and host because the
older P0.3J capture is not a controlled A/B. The strict ratings are:

```text
16 ms control:
    Dense passes 640 / 3.839 ms; fails 704 / 4.193 ms
    Sparse passes 640 / 3.678 ms; fails 768 / 4.675 ms

33 ms experiment:
    Dense passes 768 / 3.887 ms; fails 832 / 4.243 ms
    Sparse passes 640 / 3.652 ms; fails 768 / 4.607 ms
```

At matched populations:

| Workload | Server p95 16 ms | Server p95 33 ms | Change | Tick p95 16 ms | Tick p95 33 ms | Change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Dense-384 | 2.259 ms | 1.874 ms | -17.0% | 7.770 ms | 7.410 ms | -4.6% |
| Dense-512 | 3.153 ms | 2.537 ms | -19.5% | 10.529 ms | 9.893 ms | -6.0% |
| Dense-640 | 3.839 ms | 3.155 ms | -17.8% | 13.200 ms | 12.532 ms | -5.1% |
| Dense-704 | 4.193 ms | 3.641 ms | -13.2% | 14.659 ms | 14.986 ms | +2.2% |
| Sparse-384 | 1.936 ms | 1.951 ms | +0.8% | 6.886 ms | 6.958 ms | +1.1% |
| Sparse-512 | 2.764 ms | 2.751 ms | -0.5% | 9.523 ms | 9.530 ms | +0.1% |
| Sparse-640 | 3.678 ms | 3.652 ms | -0.7% | 12.190 ms | 12.121 ms | -0.6% |
| Sparse-768 | 4.675 ms | 4.607 ms | -1.5% | 15.193 ms | 14.880 ms | -2.1% |

Dense total message and byte work per measured tick is effectively unchanged. The
33 ms clock changes eligibility phase/burst shape and lowers the observed server-role
p95; it is not a 2x reduction in work. Sparse CPU is essentially unchanged. Sparse
wire bytes per 270-tick capture rise about 9.2% because faster sampled eligibility
materializes more intermediate current states instead of coalescing them, while its
per-simulated-second bandwidth still falls substantially.

## Traffic and reservation at the accepted 33 ms rows

| Metric | Dense-768 | Sparse-640 |
| --- | ---: | ---: |
| Game payload, 270 ticks | 176.10 MiB | 81.31 MiB |
| Bidirectional wire, 270 ticks | 202.90 MiB | 103.44 MiB |
| Bidirectional wire / simulated second | 22.77 MiB/s | 11.61 MiB/s |
| Wire datagrams | 419,660 | 346,302 |
| Forward Game messages | 209,446 | 173,151 |
| Reverse application-proof messages | 209,446 | 173,151 |
| Server profile reservation | 196.32 MiB | 98.28 MiB |
| Server Network reservation | 291.11 MiB | 242.60 MiB |
| In-process client profile reservation | 230.59 MiB | 24.56 MiB |
| In-process client Network reservation | 221.22 MiB | 184.35 MiB |
| Combined fixture reservation | 939.24 MiB | 549.79 MiB |

Reservation per population is cadence-independent; the higher dense rated row reserves
more memory only because it contains 768 rather than 640 recipients. At matched 640,
the 16 ms and 33 ms reservations are identical.

At matched 640, bidirectional wire rate changes as follows:

```text
Dense:  39.14 -> 18.98 MiB/s (-51.5%)
Sparse: 21.93 -> 11.61 MiB/s (-47.1%)
```

The workload dirties the same number of entities per tick, so 33 ms represents only
30.3 ticks/s versus 62.5 ticks/s for the control. These rate reductions must not be
misread as per-tick encoding compression.

## Latency interpretation

This harness is unpaced localhost and does not measure network ping/RTT. It pumps both
directions within each measured operation, so reporting that wall time as ping would be
misleading. It does retain whole-flow tick and client-role timing above. The cadence
policy itself permits up to approximately 33 ms Near and 66 ms Far sampled eligibility
delay at 33 ms ticks, before real network RTT and scheduling delay.

## Evidence

- 16 ms raw control: `network-p0.3k-rated-server-role-16ms-control-probes-off-2026-08-24.json`
- 16 ms SHA-256: `8fbca9c272e195249fffaa4c7b2709cfc82ecae63f88f529bd7960de7a749f71`
- 33 ms raw experiment: `network-p0.3k-rated-server-role-33ms-probes-off-2026-08-24.json`
- 33 ms SHA-256: `3cd4b4d12b8d5339711f09247b7f33928345e5791f534c3b92ff4f2f4604c34a`
- Benchmark build: zero warnings and zero errors.
- Relevant Game Replication, Network Application, and Network test projects: pass.
- A 33 ms all-five-profile smoke/regression run converged with zero drops.

## Verdict

At the unchanged 4 ms server-role rating, 33 ms cadence raises the observed dense
rating from 640 to 768 (+20%) in the controlled capture. It does not change the sparse
rating: 640 remains the last passing row. The result is favorable, but the full 33 ms
simulation tick is not a CPU entitlement: simulation, persistence, other Game systems,
OS jitter, and real WAN work still require budget.
