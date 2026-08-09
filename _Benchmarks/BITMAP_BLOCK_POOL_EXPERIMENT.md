# ECS bitmap block-pool experiment

Status: standalone mechanism prototype retained; its barrier-local conclusion is integrated in production V30 while persistent sparse storage remains experimental  
Captured: 2026-08-09  
Decision: accept conservative width-gated barrier-local row reuse in V30; do not replace packed storage with unrestricted persistent holes

## Executive verdict

The core idea is good, but the broad claim is too strong.

A row-occupancy bitmap can remove a large and width-dependent source of structural cost: packed removal copies a tail donor across every component column and repairs that donor's entity location. Clearing one occupancy bit avoids both. In the same-shell prototype, that changes random sparse destruction of wide archetypes by multiples, not percentages.

The same bitmap is not an across-the-board creation, modification, destruction, and query accelerator. Empty-world creation is usually slower for narrow rows, point writes are approximately tied to slightly slower, and persistent random holes can make sequential traversal several times slower. Component default initialization, retained transition copies, entity validation, and command recording also remain; a bitmap does not erase necessary work.

The strongest measured form is therefore:

1. keep logical holes during a structural barrier;
2. let later creates and destination reservations reuse those holes without donor relocation;
3. prefer empty 64-row blocks for homogeneous bulk destinations where that preserves dense query runs;
4. leave the barrier packed when cheap reuse consumed every hole;
5. compact only leftover holes when a measured cost model predicts that future traversal will repay the copy;
6. optionally permit persistent holes only for explicitly cold or structurally dominant archetypes.

The 10% destroy-plus-create cases are the most encouraging result. Every hole is reused before traversal, so the bitmap variants end with dense storage but avoid all packed donor moves. Across two shuffled 50,000-row captures, the same-shell prototype improved that operation by `1.05-1.07x` for an 8-byte archetype, `1.38-1.44x` for 128 bytes, and `1.74-1.81x` for 512 bytes. This is a credible optimization seam for real V3. It is not evidence for replacing all V3 chunk occupancy.

## What was built

The experiment lives outside `Kernels/ECS`; it cannot silently change V29 behavior.

- `UltraSim.ECS.BitmapExperimentCore` owns the fixed-capacity storage and entity/location shell.
- `UltraSim.ECS.BitmapExperiment` owns workload definitions, measurement, canonical validation, JSON, and Markdown reporting.
- `UltraSim.ECS.BitmapExperiment.Tests` owns direct invariant and randomized differential tests.
- Every storage policy uses the same native-aligned SoA payload, row-to-entity table, entity generation/state/location/free arrays, component initialization, retained-column transition copy, and canonical logical-state writer.
- Only occupancy, free-row selection, removal, optional compaction, and sparse traversal policy differ.
- Component payload is divided logically into shared 64-row occupancy words while each component remains a physically contiguous SoA plane. The prototype does not allocate one object or array per block.
- Payload is allocated once. All measured operations are fixed-capacity and warmed.
- Dead payload is not eagerly cleared. Every column is initialized before a reused row becomes live, matching V29's publication-time initialization direction.

The prototype is deliberately single-owner. It tests a storage mechanism underneath the future scheduler; it does not claim concurrent structural mutation is safe. Production per-worker command recording and deterministic barrier ownership remain separate scheduler requirements.

### Storage policies

| Policy | Removal | Allocation | Barrier | Query behavior |
| --- | --- | --- | --- | --- |
| `DensePacked` | swap-removes the physical tail and repairs its location | append | already packed | one dense range |
| `BitmapPersistent` | clears a bit; survivor rows do not move | first free row | leaves holes | full-word fast path, otherwise set-bit enumeration |
| `BitmapTopologyAware` | clears a bit | scalar first-free; bulk prefers completely empty words | leaves holes | full-word fast path plus occupied-run enumeration |
| `BitmapBarrierLocal` | clears a bit | first free row | compacts every leftover hole | dense after the barrier |
| `BitmapAdaptive` | clears a bit | bulk prefers empty words | current experimental density/partial-word threshold | compact or remain sparse |

`BitmapAdaptive` is intentionally a first policy candidate, not an accepted selector. Its unconditional density/partial-word rule frequently over-compacts. A production selector needs predicted future scan count, query width, source width, hole topology, empty-block reclamation value, and measured hardware constants.

### Correctness contract

Every logical case requires all five policies to produce the same canonical final-state hash. The hash includes entity index, generation, liveness, logical store membership, and every live component value while excluding physical row order and hole topology. Each warmup and measured iteration is validated outside the timed boundary.

The direct suite currently covers:

- 8-byte prototype entity layout;
- capacity not divisible by 64 and invalid tail-bit masking;
- packed donor relocation and value preservation;
- stable bitmap survivor locations;
- complete overwrite of every reused component column;
- barrier-local and adaptive compaction;
- topology-aware empty-word selection;
- traversal exclusion of dead rows;
- canonical state preservation through compaction;
- retained/default component transition behavior;
- stale and duplicate destroy rejection;
- transactional creation capacity failure and later slot reuse;
- transactional full-destination transition rejection;
- full row reinitialization after reset/republication;
- worst-case alternating-hole compaction within bounded scratch;
- randomized five-policy world-state equivalence;
- warmed zero-managed-allocation churn.

The exhaustive reduced-scale benchmark run is also a model/differential test: all `1,195` measured policy rows across `239` logical cases matched canonical state.

## Measurement boundaries

`Prepare()` resets and constructs the requested topology before timing. `Execute()` alone is timed and allocation-counted. Canonical hashing and topology capture occur afterward. This separation is intentional:

- destroy and transition cases include their requested barrier behavior;
- traversal-only cases measure the steady topology presented to a system after a barrier;
- mixed-tick cases time destruction, creation, barrier work, and 0/1/4/16 scans together;
- occupancy sweeps hold live rows constant while varying physical rows per occupancy word;
- production V2/V3 comparisons run in isolated child processes with their existing stronger shared semantic adapter.

The prototype runner reports mean/min/p50/p95/p99/max, ns/operation, managed allocation and collections, effective query bytes/second, payload and occupancy metadata, holes, high-water rows, full/partial/empty words, hole reuse, compaction passes, relocation count, and relocated component bytes.

All `425/425` rows in both reportable 50,000-row prototype captures measured zero managed allocation. This is current-thread allocation accounting for single-threaded cases, not a future scheduler-wide allocation claim.

## Reportable prototype results

The accepted working report for this experiment is generated under `_Benchmarks/results/bitmap-block-pool-baseline-randomized`. It uses 50,000 rows, two warmups, seven measured iterations, tiered compilation disabled, ReadyToRun disabled, and deterministic shuffled execution order. An alternate-seed complete capture lives under `_Benchmarks/results/bitmap-block-pool-baseline-alternate-seed`. Ratios below compare only policies within the identical prototype shell. Ranges quote those two shuffled captures; a single value means the direction was sufficiently stable that extra digits would not improve the decision.

The immutable primary and alternate-order JSON artifacts are
`_Benchmarks/Baselines/ecs-bitmap-block-pool-2026-08-09.json` and
`_Benchmarks/Baselines/ecs-bitmap-block-pool-2026-08-09-alt-seed.json`.
The runner's `--accept-baseline=PATH` option refuses to overwrite an existing
file.

Future same-policy captures can pass `--baseline=` with the primary artifact.
The runner refuses runtime, host, scale, warmup, iteration, filter, execution-
order, or seed mismatches; verifies descriptors and canonical state per row; and
emits current/baseline mean and p95 ratios plus allocation deltas.

### Creation

| Width | Packed range | Persistent bitmap range | Interpretation |
| ---: | ---: | ---: | --- |
| 8 bytes | 8.21-9.01 ns | 10.45-11.39 ns | bitmap is `1.26-1.27x` slower |
| 32 bytes | 10.09-10.51 ns | 12.65-12.86 ns | bitmap is `1.22-1.25x` slower |
| 128 bytes | 19.67-19.82 ns | 22.59-22.92 ns | bitmap is `1.14-1.16x` slower |
| 512 bytes | 71.97-82.97 ns | 76.17-77.41 ns | direction changes by order; within about 7% |
| 1,024 bytes | 135.65-138.31 ns | 151.75-153.99 ns | bitmap is `1.11-1.12x` slower |

Conclusion: the concept does not accelerate initial creation in the controlled experiment. Narrow creation pays bitmap search/update overhead. At large widths default initialization dominates and the percentage gap shrinks. Recycled-hole creation remains valuable because it prevents a preceding packed donor copy, as the churn result shows. The earlier fixed-order diagnostic had shown an apparent wide-row bitmap win; shuffled execution made that disappear, which is why order is now embedded in the report contract.

Homogeneous bulk creation has its own topology-aware path and forward empty-word
cursor. The benchmark caught and removed an initial quadratic empty-word rescan.
After that repair it remains slower than packed: approximately `1.24-1.32x` at
8 bytes, `1.23-1.27x` at 32 bytes, `1.13-1.17x` at 128 bytes, and about
`1.06-1.10x` at 512-1,024 bytes. Empty-block selection is useful for preserving
destination topology, not for making empty-world publication cheaper.

### Random destruction without compaction

| Width | Removed | Packed range | Persistent bitmap range | Speedup range | Packed relocated payload |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 128 bytes | 10% | 57.81-58.77 ns | 25.71-26.17 ns | `2.25x` | 640,000 bytes/sample |
| 512 bytes | 10% | 121.48-123.38 ns | 27.19-33.18 ns | `3.66-4.54x` | 2,560,000 bytes/sample |
| 1,024 bytes | 10% | 292.83-310.00 ns | 27.89-28.90 ns | `10.50-10.73x` | 5,120,000 bytes/sample |
| 1,024 bytes | 50% | 250.36-287.37 ns | 24.20-25.10 ns | `10.35-11.45x` | 25,600,000 bytes/sample |

The benefit scales with bytes no longer copied. This is the central confirmation of the proposal. It is also an upper-bound mechanism result: production V29 has richer identity, chunk, history, enable, lifecycle, command, and failure semantics.

Whole-population tail-ordered destruction does not benefit: packed removal already has no donors, and bitmap bit maintenance made it slightly slower. V29's exact-whole/suffix fast paths remain the correct algorithm for those shapes.

### Balanced 10% destroy/create churn

| Width | Persistent speedup range | Final holes |
| ---: | ---: | ---: | ---: | ---: |
| 8 bytes | `1.05-1.07x` | 0 |
| 128 bytes | `1.38-1.44x` | 0 |
| 512 bytes | `1.74-1.81x` | 0 |

This is more important than the maximum destruction speedup. The next creation reuses every vacated row, the final store is fully dense, no compaction runs, and the system avoids both donor copying and a future sparse query penalty.

### Query-to-mutation crossover

Each mixed tick performs 10% random destroy, equal replacement creation, then scans the entire live store. Because replacement creation fills every hole, the bitmap traverses a dense result.

| Width | Scans after churn | Persistent speedup over packed |
| ---: | ---: | ---: |
| 8 bytes | 0 / 1 / 4 / 16 | `1.05-1.07x / 0.99-1.01x / 0.97-1.03x / 0.84-0.95x` |
| 128 bytes | 0 / 1 / 4 / 16 | `1.38-1.44x / 1.33-1.34x / 1.19-1.21x / 1.04-1.07x` |
| 512 bytes | 0 / 1 / 4 / 16 | `1.74-1.81x / 1.69-1.79x / 1.52-1.56x / 1.26-1.27x` |

The narrow crossover is somewhere between roughly 4 and 16 scans and is noisy at this duration. Wider archetypes preserve more of the avoided structural copy benefit. Larger working sets and production query machinery may move these crossovers.

### Persistent-hole traversal cost

| Topology | Packed | Persistent bitmap | Relative throughput |
| --- | ---: | ---: | ---: |
| 10% random holes, 128-byte archetype, 16 queried bytes | 1.08-1.09 ns/live row | 1.33-1.42 ns | `0.77-0.81x` |
| 50% random holes, same query | 1.11-1.12 ns/live row | 1.48-1.54 ns | `0.72-0.76x` |
| 50% random holes, topology-aware run walker | 1.11-1.12 ns/live row | 3.59-3.70 ns | `0.30-0.31x` |
| fixed 8 live rows per 64 physical rows | 1.09-1.20 ns/live row | 2.34-2.50 ns | `0.46-0.48x` |

This rejects a universal no-compaction policy. Full occupancy words are cheap; randomly partial words and expanded high-water ranges are not. The run walker is actively bad on random 50% masks and must be selected from topology rather than used universally. The low-occupancy case is roughly `2.1x` slower than packed in both final shuffled captures.

### Topology-aware transition destination

For a 512-byte source transitioning into a destination containing both early sparse holes and later empty blocks, topology-aware bitmap was `1.45-1.57x` faster than packed across the final shuffled captures. With random destination holes it was `1.50x` in both orders. The direction is promising, but focused isolated processes and production retained-column/history semantics are still required before choosing this allocator.

This supports separating scalar allocation from homogeneous bulk allocation. Bulk routes may prefer completely empty words/chunks even when a scalar first-free policy would consume isolated holes.

### Point modification

At 50,000 rows, checked prototype point reads happened to measure about 10-12% faster through bitmap policies and point read/write about 1-4% slower. These small effects are shell/JIT/cache-order candidates, not a reason to choose the layout. The bitmap does not make component arithmetic or mandatory change stamping disappear. Production V3's actual checked lookup remains governed by the V2/V3 adapter.

### Why blanket barrier compaction failed

Compacting every leftover sparse destroy at the end of the timed operation frequently cost more than packed swap removal. For a 512-byte archetype with 10% random destruction:

- packed: `121.48-123.38 ns/destroy`;
- persistent holes: `27.19-33.18 ns/destroy`;
- compact-every-barrier: `509.37-518.63 ns/destroy`.

The compactor performs the avoided wide copy later and adds hole/donor discovery. Compaction is only sensible when reuse, empty-chunk reclamation, or enough future scans amortize it. `BitmapAdaptive`'s initial density threshold is retained as a negative/control candidate, not a production policy.

## Production V2, immutable V3 baseline, and V29 controls

Prototype results are directional because the experimental entity is eight bytes and omits production V3 world identity, change history, enable state, chunk lists, command results, and lifecycle semantics. The production comparison seam remains authoritative.

The production source reference for these controls is clean-tree commit
`d647521` (V29). The V3 adapter JSON retains its older
`ecs-next-phase1-planned-source-compaction-v27` kernel-profile string because the
current baseline validator treats that field as a stable environment/profile
key; it is not the source milestone label for this capture.

Both new process orders used four warmups and fifteen measured iterations and produced:

- 87 comparable V2/V3 rows;
- zero canonical-state mismatches;
- zero catalog/measurement mismatches;
- 15 explicitly unsupported parity rows;
- zero V29 timed managed allocation in all 87 rows.

Representative 50,000-row results:

| Scenario | V2, two orders | Immutable V3 baseline | V29, two orders | Current conclusion |
| --- | ---: | ---: | ---: | --- |
| 1R1W query | 3.014 / 3.011 ns | 1.166 ns | 1.144 / 1.130 ns | V29 remains about `2.6x` faster than V2 |
| immediate create | 49.15 / 47.75 ns | 43.86 ns | 45.45 / 43.63 ns | V29 approximately ties its baseline and beats V2 while allocating zero |
| full deferred destroy playback | 2.446 / 2.404 ns | 72.50 ns | 14.626 / 14.362 ns | V29 is about `5.98x` slower than V2, but about `5x` faster than immutable V3 |
| sparse 10% destroy playback | 15.24 / 15.40 ns | 85.03 ns | 61.04 / 57.14 ns | V29 remains `3.7-4.0x` slower than V2; largest relevant bitmap target |
| immediate destroy | 25.77 / 28.71 ns | 72.30 ns | 46.61 / 45.17 ns | V29 improved materially but remains slower |
| add-default playback | 20.68 / 19.86 ns | 82.70 ns | 77.80 / 78.36 ns | about `3.8-3.9x` slower than V2 |
| remove playback | 16.80 / 16.29 ns | 85.99 ns | 79.36 / 80.43 ns | about `4.7-4.9x` slower than V2 |

The bitmap prototype attacks exactly the dominant packed-source-copy cost in V29's remaining sparse destroy and transition gaps. It does not address V3's much richer command-recording cost, route validation, or random lookup gap.

## Memory and systems concerns

The occupancy mask itself costs one bit per physical row: about 6.1 KiB per 50,000-row store before alignment. That is small. The consequential memory costs are indirect:

- high-water rows may remain much larger than live rows;
- queries touch more occupancy words and can span more cache/TLB footprint;
- partially occupied chunks cannot be returned to the arena;
- stale unmanaged payload remains inaccessible until publication-time initialization;
- exact change history, enable masks, lifecycle publication, persistence, and replication must all honor occupancy;
- every row-based bulk operation needs dense/full-word and partial-word paths;
- topology summaries and list membership must stay O(1) or amortized, not rescan all masks during every operation;
- source/destination block policy can trade reuse against query density and empty-chunk reclamation.

The prototype uses one shared occupancy bitmap across all SoA columns. Per-component occupancy would be both incorrect for archetype membership and materially more expensive.

## Production implementation difficulty

The isolated prototype is straightforward. Correct production integration is moderately hard because V3 has more invariants than the prototype. The storage change affects:

- `ChunkRow` validity and entity locations;
- full/partial/empty intrusive store lists;
- query slicing and generated writable views;
- enable masks;
- dense and exact change history;
- migration rollback and destination capacity prefixes;
- deferred conflict resolution and deterministic command results;
- lifecycle streams when added;
- persistence and replication extraction;
- Native/Pinned backend parity;
- scheduler partitioning and safe structural ownership.

The dangerous implementation is a flag added to existing packed rows while old code continues assuming `[0, Count)` is live. The safe implementation makes occupancy an explicit storage mode/capability and refuses to expose a dense span unless a full-word/run proof exists.

## Production V30 outcome and remaining experiments

V30 accepted the smallest production mechanism supported by the evidence. It did
not add a persistent bitmap or change the dense query representation. Instead,
one impossible local-entity marker temporarily identifies a row while the
single-owner structural barrier is active. Fixed command-buffer scratch records
create demand by archetype; eligible wide stores retire sparse live destroys into
temporary rows; compatible default creates reuse those rows; mandatory
finalization packs every leftover before later structural phases or queries.

The production selector is deliberately conservative: fixed row width must be at
least 64 bytes, recorded creates must cover all currently resolvable live
destroys in the store, and V29's exact whole/suffix algorithms stay first. Narrow,
unbalanced, stale-only, and unsupported command shapes use the packed path. The
accepted same-build control found the narrow fallback within 0.3% of V29-style
packed playback while 524-byte balanced churn improved 24.5% at 10% and 23.7% at
50%. Failure-finalization, unbalanced fallback, logical state, token resolution,
default initialization, dense postcondition, and warmed zero allocation have
direct regression coverage.

The following broader ideas remain experiments, not implied V30 behavior:

1. allow transition destinations—not only same-archetype default creates—to
   consume compatible temporary rows;
2. prefer empty 64-row groups/chunks for homogeneous bulk destinations when that
   improves later query topology;
3. widen the selector using measured partial-refill and compaction crossovers;
4. consider explicit persistent `ChurnOptimized` storage only for cold archetypes
   whose observed structural-to-query ratio repays sparse traversal and retained
   chunks;
5. add any accepted observable workload to the V2/V3 adapter while retaining the
   immutable baseline, V28, V29, and V30 longitudinal columns.

Every extension must preserve V29 whole/suffix destruction, 1R1W query
throughput, zero-allocation guarantees, stale/foreign rejection, exact change
history, backend parity where applicable, and canonical command/world state.

## Additional required benchmarks

- component widths from 8 bytes through at least 1 KiB;
- fixed live working sets at 8/16/32/64 rows per occupancy word;
- 1%, 10%, 25%, 50%, 90%, and whole-population removal;
- random, clustered, suffix, full-word, mixed sparse-plus-empty-word, and adversarial one-hole-per-word topology;
- 0/1/4/16/64/256 full scans after each mutation barrier to locate real crossovers;
- same-barrier hole refill at 0%, 25%, 50%, 100%, and overfill;
- source locality independent of route entropy;
- 1/4/16/64/hundreds of transition routes;
- destination empty-block preference versus first-free reuse;
- exact history off/on and one/several watched columns;
- enabled-row filters and dense/noncontiguous run output;
- change-version wrap and repeated unchanged polling;
- chunk evacuation/reclamation rate and arena high-water retention;
- L1/L2/L3/above-L3 working-set ladder, including 1K through 10M rows where memory allows;
- hardware counters for cycles, instructions, branches/mispredicts, L1/L2/LLC/TLB misses, cache-line writebacks, and memory bandwidth;
- p95/p99/max barrier time under production-shaped command entropy;
- allocation/GC, retained native bytes, disposal/leak soak, and first-touch/NUMA behavior;
- eventual single-thread/parallel scheduler crossover using the real storage topology.

Each structural measurement should retain the canonical final-state hash and report bytes necessarily copied, bytes avoided, rows relocated, holes reused, blocks full/partial/empty, and compaction work. Tests may show that a proposed selector or block size is wrong; policy must follow the measured crossover rather than preserve this document's first estimate.

## Reproduction

Direct tests:

```bash
dotnet run --project _Tests/UltraSim.ECS.BitmapExperiment.Tests/UltraSim.ECS.BitmapExperiment.Tests.csproj -c Release
```

Reportable prototype matrix:

```bash
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
dotnet run --project _Benchmarks/UltraSim.ECS.BitmapExperiment/UltraSim.ECS.BitmapExperiment.csproj -c Release -- \
  --profile=baseline --scale=50000 --warmups=2 --iterations=7 \
  --order-seed=2977624245 \
  --output-dir=_Benchmarks/results/bitmap-block-pool-baseline-randomized \
  --accept-baseline=_Benchmarks/Baselines/ecs-bitmap-block-pool-2026-08-09.json
```

Exhaustive reduced-scale correctness/coverage matrix:

```bash
DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
dotnet run --project _Benchmarks/UltraSim.ECS.BitmapExperiment/UltraSim.ECS.BitmapExperiment.csproj -c Release -- \
  --profile=full --scale=10000 --warmups=1 --iterations=3 \
  --output-dir=_Benchmarks/results/bitmap-block-pool-full-coverage
```

Production V2/V29 comparison and immutable-V3 regression:

```bash
dotnet run --project _Benchmarks/UltraSim.ECS.Comparison/UltraSim.ECS.Comparison.csproj -c Release -- \
  --warmups=4 --iterations=15 \
  --baseline=_Benchmarks/Baselines/ecs-v2-v3-2026-08-09.json \
  --output-dir=_Benchmarks/results/ecs-v2-v3-bitmap-control
```

Repeat with `--v3-first` in a different output directory before accepting small differences.

For a future prototype regression, repeat the reportable command with the same
order seed, replace `--accept-baseline=...` with:

```text
--baseline=_Benchmarks/Baselines/ecs-bitmap-block-pool-2026-08-09.json
```

The emitted regression report suppresses ratios for catalog, descriptor,
canonical-state, environment, or measurement-policy mismatches.
