# ECS Next Benchmark Recommendations

Status: future benchmark contract; adopt incrementally as equivalent V2 and V3
features become executable

This document records benchmark work that should accompany the natural ECS Next
implementation. It is not a reason to implement features out of dependency order
or optimize incomplete semantics. Each comparison must first prove that V2 and V3
performed equivalent useful work and reached equivalent canonical state.

## Canonical final-state validation

Every correctness-sensitive benchmark should produce both its ordinary execution
checksum and a canonical final-state hash. A checksum is an inexpensive guard
against dead-code elimination, but two implementations can produce the same
arithmetic checksum while disagreeing about:

- entity locations;
- component enable states;
- packed-row ownership;
- added-component defaults;
- removal behavior;
- archetype population;
- entity generation/version state.

The canonical hash should be independent of physical chunk allocation, packed-row
order, arena backend, process-local world IDs, and other intentionally transient
representation details unless those details are the subject of the benchmark.
Canonicalization should sort or otherwise visit entities by a stable benchmark-local
identity, then hash the entity's expected lifecycle state, logical archetype
signature, component bytes, enable states, and generation state. V2 and V3 adapters
must use the same hash specification and test vectors.

For migration and lifecycle benchmarks, development and correctness runs should
validate the complete state before and after the timed measurement. Production
performance runs should keep expensive validation outside the timed region and use
only the cheaper checksum and/or a preallocated canonical-hash pass around that
region. A benchmark result is invalid if its canonical state disagrees, regardless
of how fast it ran.

Tests must cover the canonicalizer itself: reordered packed rows and different
chunk placement must hash identically, while a changed location owner, component
byte, enable bit, default, removal result, archetype count, or generation must
change the hash. Collision resistance is still not a proof, so small fixtures
should also receive exact field-by-field comparison in tests.

## Report effective byte throughput

Nanoseconds per row remain useful, but component width changes their meaning.
Every traversal benchmark should also report the effective bytes read, effective
bytes written, total effective bytes, and effective component bandwidth:

```text
effective component bytes/second =
    (bytes read + bytes written) / elapsed seconds
```

For example, if a multi-archetype query reads 16 bytes and writes 32 bytes per
row, its effective traffic is 48 bytes per row:

```text
48 bytes / 2.7 ns = approximately 17.8 GB/s
```

The result record should retain the byte accounting rather than only the derived
rate so the comparison tool can verify that two adapters claim equivalent work.
Report logical component traffic separately from estimated physical traffic such
as write allocation, read-for-ownership, enable masks, entity-index streams, and
change-version writes. Do not imply that logical throughput is a direct hardware
memory-controller measurement.

Together with hardware counters where available, byte throughput helps distinguish
whether a traversal is limited by:

- query dispatch and iteration overhead;
- CPU instruction execution;
- cache bandwidth;
- main-memory bandwidth.

It also identifies the point where further ECS dispatch optimization stops helping
because the workload has reached the hardware's bandwidth ceiling. Tests should
verify byte accounting for every generated query shape and reject overflow when
row counts and component widths are multiplied.

## Characterize the memory-hierarchy crossover

Every representative traversal shape should eventually run over a working-set
ladder, not only the current 100,000-row guard. At minimum, include:

- 1,000 rows;
- 10,000 rows;
- 50,000 rows;
- 250,000 rows;
- 1,000,000 rows;
- 10,000,000 rows.

Record the exact logical component bytes, entity-index bytes, enable-word bytes,
chunk metadata, and total retained arena payload for each point. Classify results
against measured hardware cache sizes rather than assuming a row count maps to a
particular cache. The intended coverage is a data set that fits in L1, one that
fits in L2, multiple points within the processor's last-level cache, the first
point beyond that cache, and a substantially larger main-memory streaming case.
Add smaller shape-specific boundary points when even 1,000 rows exceed L1 or L2;
the common ladder is for cross-shape comparison, not a claim that its first point
fits every cache for every component width.

This distinction is especially important on large-cache processors such as the
Ryzen 7 7800X3D. Mid-sized ECS cases may remain resident in its large L3 and make
query throughput look like memory throughput when it is actually cache throughput.
The ladder should separate:

- fixed query-plan and per-chunk dispatch cost;
- L1/L2-resident loop throughput;
- last-level-cache-resident throughput;
- true main-memory streaming throughput;
- mixed read/write effects such as write allocation and read-for-ownership.

Report nanoseconds per row and logical GB/s at every point. Where supported, also
capture cycles, instructions, cache misses, memory-controller bandwidth, branch
misses, and writeback traffic in isolated processes. Avoid interpreting the
expected decline from read-only to mixed-write bandwidth as an ECS regression
unless equivalent working sets and hardware counters show excess query overhead.

Tests must validate identical results and row coverage at every size, including
chunk-boundary and capacity-boundary neighbors. The 10-million-row cases require
an explicit opt-in or separate large-world profile so the ordinary development
suite remains fast and does not reserve multi-gigabyte fixtures accidentally.
Tests and measurements may move, add, or remove ladder points when actual cache
boundaries differ from the initial hypothesis.

## Scheduler crossover is a primary future benchmark

When the scheduler arrives, do not report only maximum parallel throughput.
Measure the crossover point at which scheduling becomes profitable for each
representative component width and operation cost.

An illustrative report is:

| Rows | Single-thread | Parallel | Winner |
| ---: | ---: | ---: | --- |
| 1k | 3 us | 12 us | Single |
| 10k | 30 us | 25 us | Parallel narrowly |
| 100k | 300 us | 90 us | Parallel |
| 1m | 3 ms | 700 us | Parallel |

A strong scheduler should:

- avoid parallelization below the measured profitable threshold;
- split large chunks without creating tiny jobs;
- adapt to component width and operation cost;
- avoid forcing every system through the same partition size;
- maintain bounded worker wake-up and dependency overhead.

The matrix should include read-only, write-heavy, compute-heavy, sparse-filtered,
and bandwidth-saturated systems at multiple entity counts. Measure cold workers,
warm/spinning workers, worker-count scaling, uneven tail chunks, dependency chains,
fan-in/fan-out, false sharing, and unrelated host contention. Report mean, median,
p95, p99, maximum, allocations, collections, worker wake latency, jobs created,
rows per job, and useful worker utilization.

Tests should lock the scheduler decision rather than a universal row threshold:
given calibrated cost metadata and workload size, it must select serial or
parallel execution consistently, cover all rows exactly once, create no undersized
jobs except an unavoidable tail, honor dependencies, and remain correct when
measurements suggest that a previously expected crossover is wrong.

## V2-to-V3 comparison adapters

Add exact cross-generation adapters as corresponding V3 features become real.
Run each generation in a clean process, join results by stable scenario ID, and
record semantic-profile and canonical-hash versions. Initial parity candidates
are one-component traversal, two-component read/write traversal, checked scalar
access, enable toggles, one exact archetype transition, and one exact
create-transition-destroy lifecycle.

Equivalent adapters require the same:

- component layouts and flags;
- initial logical world state;
- entity count and operation definition;
- component reads, writes, and enable operations;
- setup/prepare/timed/validation boundaries;
- warmup, iteration, process-isolation, and GC methodology;
- checksum and canonical-state hash specification.

Comparisons must label semantic mismatches instead of presenting their ratios as
speedups or regressions. Preserve earlier V3 measurements in reports so each
feature can show V2 baseline, first V3 implementation, and current V3 result.

## Expected direction and evidence policy

The expected direction is lower runtime, zero steady-state managed allocation,
bounded native memory, and lower p95/p99/max latency without state divergence.
Tests, disassembly, counters, allocation traces, and production-shaped replays may
recommend a different implementation than this document anticipates. Follow that
evidence, add the smallest test or benchmark that distinguishes the alternatives,
and update this contract when the semantics or measurement methodology changes.
