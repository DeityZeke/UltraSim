# Sparse unreliable multi-frame pooled tail

- Workload: `sparse_observer_movement`, 4,096 clients, accepted `current10`
  profile
- Runtime: Release, probes off, shared listener with native send/receive
  batching, `DOTNET_TieredCompilation=0`, `DOTNET_ReadyToRun=0`
- Method: 51 independent valid `current10` windows, each 360 measured ticks
  plus 10 settle ticks; raw measured samples concatenated before percentile
  calculation
- Sample: 18,360 measured ticks per mode
- Interpretation: this is pooled sustained CPU/tail evidence, not a continuous
  five-minute roaming-lifetime acceptance run

## Pooled distributions

| Metric | Control | Aggregate | Change |
| --- | ---: | ---: | ---: |
| Tick p50 (ms) | 79.587 | 73.522 | -7.6% |
| Tick p95 (ms) | 103.566 | 94.773 | -8.5% |
| Tick p99 (ms) | 109.495 | 98.052 | -10.5% |
| Tick p99.9 (ms) | 116.930 | 109.078 | -6.7% |
| Tick max (ms) | 131.468 | 136.921 | +4.1% |
| Server-role p50 (ms) | 58.142 | 53.614 | -7.8% |
| Server-role p95 (ms) | 81.497 | 72.946 | -10.5% |
| Server-role p99 (ms) | 85.655 | 75.565 | -11.8% |
| Server-role p99.9 (ms) | 93.422 | 87.007 | -6.9% |
| Server-role max (ms) | 111.465 | 117.178 | +5.1% |

All 51 windows passed their per-window acceptance checks, and both modes
produced the same canonical owner-state hash (`c7802be647c3ecf9`). The pooled
server p95/p99/p99.9 results are consistently better with aggregation. The
single maximum is higher in the aggregate run, so this is not a claim of
unqualified improvement at every extreme-tail statistic.

## Semantics and pressure

| Metric | Control | Aggregate |
| --- | ---: | ---: |
| Immediate WorldState p95 age (ticks) | 1 | 1 |
| Near WorldState p95 age (ticks) | 2 | 1 |
| Far WorldState p95 age (ticks) | 3 | 2 |
| Input ACK p95 maximum (ms) | 352.693 | 323.158 |
| Worst-client input ACK p95 maximum (ms) | 393.344 | 356.139 |
| Worst-client WorldState p95 maximum (ms) | 368 | 304 |
| Capacity drops | 0 | 0 |
| Nonviable recipients (maximum) | 0 | 0 |
| Byte-capacity failures (maximum) | 0 | 0 |
| Retransmitted packets | 596,502 | 583,848 |
| Managed allocation | 4,239,035,448 B | 4,239,007,528 B |
| GC Gen0/Gen1/Gen2 | 95/95/9 | 92/92/6 |

Every measured tick exceeded the 62 Hz wall-clock budget in both modes
(18,360 deadline misses each). This is a shared property of this 4K workload,
not an aggregation regression; it is reported explicitly rather than treated
as a zero-deadline acceptance.

## Aggregation census

| Metric | Control | Aggregate |
| --- | ---: | ---: |
| Unreliable semantic frames | 101,291,122 | 101,386,242 |
| Unreliable Data datagrams | 101,291,122 | 79,159,327 |
| Datagrams with two or more frames | 0 | 20,827,773 |

Aggregation reduced unreliable Data datagrams by 21.85% while preserving the
semantic convergence and freshness gates. The full paired machine-readable
summary, including each window's tick/server p95 series, is
[`network-v3-sparse-unreliable-multiframe-pooled-tail-2026-08-17.json`](network-v3-sparse-unreliable-multiframe-pooled-tail-2026-08-17.json).

## Decision

The result is strong sustained CPU evidence for the opt-in v11 framing
capability: roughly 10–12% lower server-role p95/p99, better freshness and
input-ACK fairness, no capacity/nonviable/byte failures, and no retransmission
or allocation increase. Keep it opt-in for now because the maximum tick and
server-role samples moved upward and this pooled method does not prove a
continuous roaming lifetime.

The separate longer-run `legacy128` failure remains a WorldState identity-ledger
lifetime issue, not an aggregation failure. The next networking checkpoint is
pressure-triggered atomic baseline compaction for retained tombstone knowledge;
simply enlarging the known-identity arrays would only postpone exhaustion.
