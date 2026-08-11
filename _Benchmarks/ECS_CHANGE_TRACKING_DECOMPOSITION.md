# ECS Change-Tracking Cost Decomposition

Status: accepted ECS 3.0.35 instrumented investigation; no production kernel,
strategy, public API, or version change

## Question

The first 100K mixed-operation soak attributed about 280.4 microseconds per tick
to 5,000 random exact-tracked writes and 191.0 microseconds to consuming about
5,095 exact changes. This investigation separates the costs that the soak had to
report together:

```text
entity/location resolution
    + component storage access
    + change-version advancement
    + dense chunk publication
    + exact-row publication
    + changed-chunk rejection
    + exact row-version discovery
    + contiguous changed-range dispatch
    + useful visitor work
```

It is a diagnostic benchmark, not a proposed production API. ECS remains 3.0.35.

## Method

The executable mode is implemented in
`UltraSim.ECS.Benchmarks/ChangeTrackingDecomposition.cs` and selected with
`--change-decomposition`. Its 100,000-entity world has one packed 32 KiB
archetype containing four equal-width components: logical identity, untracked
raw value, dense-tracked value, and exact-tracked value.

The accepted layout contains 62 occupied chunks at 1,616 rows/chunk. Dense
change metadata occupies 6,528 bytes. Opting one column into exact row history
adds 2,638,944 bytes. These are reserved-capacity facts for this diagnostic
world, not the two-archetype soak footprint.

Writes are measured at 100, 1K, 5K, 10K, 25K, 50K, and dense 100K populations
using:

- one physically contiguous region;
- 256-row clusters distributed across chunks;
- deterministic unique random rows;
- the complete dense population.

The focused 5K-random decomposition measures:

- prebound physical store write without tracking;
- checked entity/location resolution plus write without tracking;
- dense and exact metadata stamping without component access;
- ordinary checked dense and exact tracked writes;
- an experimental single-version batch that still stamps each row;
- an experimental chunk-grouped batch that shares one version, stamps the chunk
  gate once, and stamps exact rows directly.

The grouped input is prepared outside timing. Its result is therefore an upper
bound on a future already-chunk-ordered job or batch, not proof that sorting or
partitioning arbitrary public writes is profitable.

Consumers compare dense changed-chunk delivery with exact changed-row delivery.
Every result records changed rows, changed chunks, resident rows in those chunks,
false-positive deliveries, and physical contiguous changed runs. The latter is
important: exact traversal currently dispatches one visitor range per contiguous
run, not merely once per changed chunk.

Every tracked prototype is validated through an independent cursor after timing.
Exact paths must publish precisely the selected rows; dense paths must publish
all resident rows in touched chunks while containing every selected row. Timed
paths allocate zero managed bytes. Cold selection construction and correctness
validation remain outside timing.

The accepted captures use 64 pre-measurement JIT priming calls, eight per-case
warmups, 31 measured samples, fully optimized non-tiered JIT, and two fresh
processes with opposite outer pass order. Each process also executes both forward
and reverse case order. The 5K write cases execute two repetitions per sample,
so report tables normalize the measured 10K operations back to nanoseconds/write.

## Accepted artifacts

- `Baselines/ecs-v3-3.0.35-change-tracking-decomposition-forward-first-2026-08-11.json`
- `Baselines/ecs-v3-3.0.35-change-tracking-decomposition-reverse-first-2026-08-11.json`

Together they contain 628 measured rows. All rows report zero warmed managed
allocation and zero Gen0/Gen1/Gen2 collections. All publication and consumer
correctness gates pass.

## 5K random write decomposition

The table averages the four order observations across the two accepted
processes. `Worst p95` is the largest p95 observed in those four controls.
Projected 5K time is the normalized mean multiplied by 5,000, not the benchmark's
two-repetition elapsed sample.

| Control | Mean ns/write | Observed mean range | Worst p95 | Projected 5K |
| --- | ---: | ---: | ---: | ---: |
| prebound store write, no tracking | 16.84 | 16.53-17.02 | 21.45 | 84.2 us |
| checked location + write, no tracking | 36.29 | 35.20-37.00 | 42.87 | 181.5 us |
| dense stamp only | 4.89 | 4.80-4.94 | 6.66 | 24.4 us |
| exact stamp only | 6.62 | 6.53-6.69 | 7.78 | 33.1 us |
| checked dense tracked write | 42.96 | 41.34-43.75 | 49.11 | 214.8 us |
| checked exact tracked write | 48.97 | 47.63-49.85 | 56.04 | 244.9 us |
| exact, one shared version, per-row publication | 42.77 | 42.00-43.92 | 47.94 | 213.9 us |
| exact, one version, chunk-grouped publication | 33.38 | 32.54-34.95 | 38.44 | 166.9 us |

`ChangeVersion.Next()` alone averages 0.432 ns/advance. Reusing one version is
semantically attractive for a non-interleavable system/job scope, but advancing
the version is not the main cost. It represents less than one percent of the
ordinary random exact write.

The more useful decomposition is:

- random checked location and store access adds about 19.45 ns over the prebound
  physical control;
- dense publication adds roughly 6.67 ns over checked untracked mutation;
- exact publication raises the ordinary checked total by another 6.01 ns over
  dense tracking;
- exact tracking is therefore about 14% of the checked dense-plus-exact write
  total, not the majority of its 48.97 ns;
- sharing a version while retaining per-row random publication improves the
  experimental control by about 12.7%, but this control also bypasses the lookup
  route used by the ordinary checked operation;
- already chunk-grouped publication improves the experimental exact path by
  about 31.8%, before charging any grouping cost.

Locality is more important than the version increment. At 5K writes:

| Locality | Checked dense | Checked exact | Grouped exact prototype |
| --- | ---: | ---: | ---: |
| contiguous | 28.21 ns | 30.66 ns | 29.23 ns |
| clustered 256 | 28.21 ns | 30.73 ns | 29.28 ns |
| random | 42.96 ns | 48.97 ns | 33.38 ns |

Moving ordinary exact writes from random order to chunk-local order reduces the
measured cost by about 37%. The grouped prototype adds little to inputs that are
already local; its opportunity is avoiding random entity/store/metadata traffic,
not eliminating a 0.43 ns version increment.

The 100K soak's 56.08 ns/write remains credible. This smaller one-archetype
fixture preselects targets outside timing, while the soak includes unique random
selection, two live archetype routes, long-running churn, and the public facade.
The diagnostic ordinary exact control projects about 244.9 microseconds for 5K
writes versus the soak's 280.4 microseconds; it explains most, but deliberately
not all, of that production-shaped phase.

## Consumer decomposition

At 5K changes:

| Locality | Chunks | Resident rows inspected | Exact runs | Dense delivered/time | Exact delivered/time |
| --- | ---: | ---: | ---: | ---: | ---: |
| contiguous | 4 | 6,464 | 4 | 6,464 / 6.19 us | 5,000 / 10.69 us |
| clustered 256 | 20 | 32,320 | 20 | 32,320 / 28.92 us | 5,000 / 32.30 us |
| random | 62 | 100,000 | 4,776 | 100,000 / 107.36 us | 5,000 / 142.20 us |

The soak's earlier `37.49 ns/delivered row` did not mean that exact tracking
performed work only for about 5K rows. With 5K random writes, every one of the 62
chunks changes, so exact discovery examines version metadata for all 100K
resident rows and then dispatches 4,776 mostly tiny ranges. The accepted
diagnostic exact result is approximately 28.44 ns/changed row, but only about
1.42 ns/resident row inspected.

Exact delivery remains economically useful. In the 5K-random case it costs about
34.84 microseconds more than dense delivery while preventing 95,000 false rows
from reaching the consumer. Any additional downstream work above roughly
0.37 ns per false row repays that difference. Most real consumers do more than
that. Exact is also already faster than dense at 100 random changes and essentially
tied at 1K random changes while filtering tens of thousands of false rows.

The worst current shape is high-density random change because it maximizes
fragmentation:

| Random changes | Exact runs | Dense consume | Exact consume |
| ---: | ---: | ---: | ---: |
| 100 | 100 | 67.30 us | 59.26 us |
| 1K | 993 | 95.28 us | 95.30 us |
| 5K | 4,776 | 107.36 us | 142.20 us |
| 10K | 9,061 | 136.75 us | 209.26 us |
| 25K | 18,832 | 227.02 us | 418.55 us |
| 50K | 24,981 | 346.66 us | 673.25 us |
| dense 100K | 62 | 90.56 us | 193.39 us |

The nonmonotonic dense figures also include realistic cache state left by each
untimed write preparation. They should not be treated as a pure query-dispatch
microbenchmark. The stable conclusion is topological: rows inspected scales with
touched-chunk residency, while range dispatch scales with changed-run count.

## Decision and next paths

Do not change the exact-history representation and do not introduce ECS 3.0.36
from this investigation alone.

The evidence rejects version advancement as the next optimization target. It
also argues against weakening checked entity safety: the random access cost is
real, but high-volume systems should avoid paying it per row by using generated
chunk-local execution.

Recommended order:

1. Build the public generated chunk-job/system surface as the next natural ECS
   capability. Its prepared chunk order already provides locality, one version
   per invocation, and once-per-chunk dense publication without an extra sort.
   Rerun the stable soak with its 5K activity writes expressed through the new
   surface before optimizing scalar random writes.
2. If arbitrary entity-list writes remain production-important, prototype a
   bounded batch writer that includes target resolution, conflict/duplicate
   policy, partitioning cost, chunk-local execution, one observation version,
   once-per-chunk gate stamps, and exact row stamps. Measure the full record,
   partition, and commit cost. The pre-grouped 33.38 ns result is only its ceiling.
3. Prototype a masked exact-consumer contract before changing metadata. Give one
   generated visitor a changed mask per chunk or 64-row word instead of invoking
   one range visitor for every run. The random 10-50% cases are the admission
   tests. Preserve independent cursors, filters, exact semantics, and zero
   allocation.
4. Only after the masked-dispatch experiment, profile/vectorize row-version mask
   construction if it remains dominant. AVX2 or another specialized path must
   keep a portable scalar implementation and the half-range/wrap semantics.
5. Profile random lookup internally only if real-system replays still use it
   heavily. Separate entity generation/liveness, location-table access, route
   lookup, store resolution, and component access, but do not trade away stale or
   foreign handle rejection for a microbenchmark.

Tests must accompany every candidate. Batch publication needs independent slow
cursors, cursor creation before/after a scope, exception visibility, duplicate
targets, mixed archetypes, enable state, wraparound, Native/Pinned parity, and
zero-allocation coverage. Masked consumers need scalar-oracle equivalence for
every density/locality/filter combination, fragmented singletons, contiguous
runs across 64-row words, exact history after relocation, and canonical state.
Tests and the full end-to-end partition cost may still recommend a different
path.

## Running

```bash
DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 \
dotnet run --project \
  _Benchmarks/UltraSim.ECS.Benchmarks/UltraSim.ECS.Benchmarks.csproj \
  -c Release -- --change-decomposition
```

Use `--change-reverse-first` for the opposite outer pass order. Other controls
are `--change-output`, `--change-entities`, `--change-warmup`,
`--change-samples`, `--change-minimum-writes`, and `--change-seed`. The accepted
density contract assumes at least 100,000 entities.
