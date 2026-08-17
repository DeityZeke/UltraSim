# Sparse bounded unreliable multi-frame Data experiment

- Workload: `sparse_observer_movement`, accepted `current10` profile
- Runtime: Release, probes off for paired runs, shared listener with native
  send/receive batching
- Frozen semantics: 62 Hz wall clock, 20 Hz input, 310 measured ticks, 10 settle
  ticks, 64-wide grid, 35% active, 10,000 mm region edge
- Experiment: opt-in protocol manifest v11, same-peer unreliable Data only, a
  fixed seven-frame cap; reliable Data remains single-frame
- Selection is no-wait, lane-ordered, MTU-bounded, and transactional through
  endpoint admission. Receive validation is whole-aggregate before mutation.

## Paired probes-off result

| Clients | Order | Control tick p95 ms | Aggregate tick p95 ms | Control server p95 ms | Aggregate server p95 ms | Control unreliable Data datagrams | Aggregate unreliable Data datagrams | Canonical |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 1,024 | control → aggregate | 17.314 | 16.786 | 13.180 | 11.817 | 482,537 | 438,118 | MATCH |
| 1,024 | aggregate → control | 19.846 | 18.734 | 16.245 | 12.299 | 482,061 | 437,877 | MATCH |
| 4,096 | control → aggregate | 76.012 | 71.805 | 60.259 | 55.981 | 1,718,019 | 1,345,625 | MATCH |
| 4,096 | aggregate → control | 77.289 | 70.956 | 61.767 | 54.852 | 1,720,165 | 1,343,862 | MATCH |

At 4,096, the two order pairs agree on a roughly 6.9% tick-p95 and 9.2%
server-p95 improvement, with about 21.8% fewer unreliable Data datagrams.
At 1,024, packet reduction is real (about 9.2%) and the paired mean improves
about 4.4%, but the order spread is still large enough that this is not a
strong standalone acceptance claim. All four runs converged to the same
canonical owner hash for their population and reported zero capacity drops.

The observed sent-frame histogram at 4,096 was approximately:

```text
1 frame  ~992k
2 frames ~330k
3 frames ~23k
4 frames ~0.6k
5 frames a handful
6 frames rare
7 frames none observed in the paired runs
```

## Deep 4,096 causal run

The deep run repeated idle, active-ratio, and layer cases in forward and reverse
order. Every case matched its canonical state across orders. Because deep timing
adds phase instrumentation, its absolute timings are attribution evidence rather
than probes-off acceptance numbers. The full case averaged:

```text
tick p95       159.414 ms
server p95     117.475 ms
process CPU      0.946 cores
```

The run also retained the aggregate frame histogram, frame/datagram totals,
plaintext fill, frames per pump, and protocol-stage frame counts in the adapter
diagnostics.

## Decision

Retain the implementation as an opt-in sparse experiment and do not enable it in
the default profile. The 4,096 result earns a larger sustained-tail experiment;
the 1,024 result is not a general acceptance claim. No receive semantics,
reliable handling, capacities, AOI, cadence, worker topology, or send-batch size
was changed.
