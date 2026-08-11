# UltraSim Next Benchmarks

Future cross-generation validation, canonical final-state hashing, effective-byte
throughput, and scheduler-crossover requirements are recorded in
[`ECS_NEXT_BENCHMARK_RECOMMENDATIONS.md`](ECS_NEXT_BENCHMARK_RECOMMENDATIONS.md).
They should be adopted feature by feature without pulling implementation out of
dependency order.

The first executable Current/Next semantic adapter is documented in
[`ECS_V2_V3_COMPARISON.md`](ECS_V2_V3_COMPARISON.md). It now covers 96 matched
1K/10K/50K rows with a shared canonical logical-state hash, separate
record/playback/end-to-end structural boundaries, allocation/GC accounting,
process isolation, fixed steady-state JIT policy, and explicit unsupported parity
rows. The accepted 2026-08-09 V2-first and V3-first captures both produced zero
state mismatches. They are an as-is regression baseline, not complete V2 parity
or evidence that V3 currently wins most overlapping operations.

The original immutable accepted joined artifact lives at
[`Baselines/ecs-v2-v3-2026-08-09.json`](Baselines/ecs-v2-v3-2026-08-09.json).
The accepted V30 joined capture lives at
[`Baselines/ecs-v2-v3-v30-2026-08-09.json`](Baselines/ecs-v2-v3-v30-2026-08-09.json),
and its exact same-build barrier-local/V29-style packed control lives at
[`Baselines/ecs-v30-barrier-hole-reuse-2026-08-09.json`](Baselines/ecs-v30-barrier-hole-reuse-2026-08-09.json).
The accepted V31 consolidation and expanded-catalog capture lives at
[`Baselines/ecs-v2-v3-v31-2026-08-10.json`](Baselines/ecs-v2-v3-v31-2026-08-10.json).
Future runs can pass `--baseline=...` to emit current/baseline V2-control and V3
absolute means, mean/p95 ratios, V2-control-normalized V3 signals, allocation
deltas, and cross-time canonical-state checks. The
adapter records its pre-iteration full-collection policy and current-thread
allocation scope; future scheduler cases must broaden allocation accounting.

These projects measure the root Next implementation. Frozen V2 Current and
Legacy runners remain beneath `_V2/_Benchmarks` and run in separate processes.

The standalone bitmap block-pool experiment and its evidence are documented in
[`BITMAP_BLOCK_POOL_EXPERIMENT.md`](BITMAP_BLOCK_POOL_EXPERIMENT.md). It compares
five same-shell packed/hole/compaction policies across a 425-row reportable
50,000-row matrix and a 1,195-row reduced-scale coverage matrix, then anchors the
mechanism results against the existing V2, immutable V3, and V29 process-isolated
controls. It does not modify production V3 storage or present the smaller
prototype entity shell as a direct V2/V3 comparison.

The original ECS primitive/schema suite remains intentionally marked non-comparable.
It establishes allocation, validation, layout-candidate, generated component-ID,
runtime schema-lookup, and metadata-access measurements before a semantically
equivalent primitive-level Current/Next contract exists. Cross-generation results
live in the dedicated adapter rather than retroactively relabeling those guards.

The handle-layout cases include both one-million and ten-million element scans.
The 8-byte candidate uses `16` world bits, `24` index bits, and `24` generation
bits, while the implemented 12-byte candidate keeps full 32-bit fields. These
are not semantically identical yet; the results answer layout questions and do
not authorize reduced identity ranges without separate lifetime/capacity tests.

The schema cases measure the direct generated generic path separately from
instance type lookup, canonical-key lookup, stable-hash lookup, and metadata by
dense ID. Every case records managed bytes and Gen0/1/2 collections. These
microbenchmarks are useful for choosing mechanisms, but they do not replace
large-schema scaling or end-to-end world measurements.

Each JSON report also embeds nine reproducible layout facts: narrow, normal,
and wide signatures planned into 16, 32, and 64 KiB chunks with a 256-byte
reserved header, four-byte entity indices, 64-byte column-base alignment, and a
100,000-entity population projection. Planner, archetype-capacity, and
world-capacity execution are measured as separate zero-allocation scenarios.
These inputs are explicit hypotheses for the coming storage prototype, not a
final chunk-size decision.

The Phase 1 arena extension adds twelve scenarios and six cold arena facts:

- native-aligned versus pinned-managed acquire/clear/release with equal bytes
  cleared per iteration at 16, 32, and 64 KiB;
- equivalent 100,000-row `BenchmarkPosition` SoA traversal through a normal
  32 KiB layout;
- equivalent whole-32-KiB and ten-percent-active-row clearing;
- payload, backing allocation, slot-metadata estimate, and observed base-address
  alignment for both backends at all three chunk sizes.

The 2026-08-04 local directional run measured zero managed bytes and zero
Gen0/1/2 collections in all 36 scenarios. Native and pinned arena results were
within roughly three percent for every new paired case; that is a tie at this
methodology/noise level, not evidence that either backend is universally
faster. Native remains the expected server candidate because it avoids retaining
a large managed object and has explicit ownership, while pinned remains a real
engine/platform comparison candidate. Repeated isolated processes, server-scale
footprints, GC pressure, first-touch, cache/TLB counters, and disposal soaks must
decide the backend.

The executable archetype-row extension adds six paired scenarios over a normal
32 KiB layout:

- bulk zero-default creation of 100,000 rows, with removal/reset performed by an
  explicit untimed `Prepare` callback;
- 100,000 packed swap-remove-plus-add cycles with relocation-table maintenance;
- one million enable-bit writes through a physical column index bound once
  during cold fixture construction.

Reports also embed a cold fact for each backend. At 100,000 normal-layout rows,
the current plan uses 63 chunks with 1,600 rows each, 2,064,384 payload bytes,
1,764 bytes of 28-byte per-chunk records, and an eight-byte local `ChunkRow`.
The dense-version extension additionally reports 1,512 bytes for three component
columns by 63 possible chunks by eight bytes, or 3,276 total store metadata bytes
before managed object/array headers. The entity pool combines the row with a
four-byte world-local `ArchetypeId` in a 12-byte `EntityLocation`; final packing
remains benchmark-driven.

All six measured zero managed bytes and zero collections. Bulk creation is
approximately `1.3-1.5 ns/row` for both backends and remains sensitive to run
order. Three exact-filter isolated runs measured native swap churn at roughly
`64.6-67.8 ns/cycle` and pinned at `68.6-75.9 ns/cycle`. Prebinding the column
index improved the native enable path to roughly `11.3 ns/toggle`, versus roughly
`14-16 ns/toggle` for pinned storage. Whole-suite native swap p95 outliers did
not reproduce in exact-filter processes, so those spikes are treated as
scenario-order/OS noise rather than a backend conclusion.

The archetype-registry extension adds two non-comparable guards over all 16
signatures possible in the four-component benchmark schema: canonical span lookup
at one million operations and dense-ID-to-store routing at ten million operations.
The first focused run measured approximately `11.5 ns/lookup` and `1.07 ns/route`,
respectively, with zero managed bytes or collections. Signature lookup is intended
for bootstrap and transition-plan construction; hot entity access routes by ID.

The cross-archetype migration extension adds two non-comparable 100,000-move
cycle scenarios, one per backend. The source contains `BenchmarkPosition` and
`BenchmarkVelocity`; the destination retains velocity, adds an enableable tag and
`BenchmarkWide`, and removes position. Both directions use cold-built plans with
prebound physical columns, so the measured loop includes row reservation/default
initialization, retained-state copy, entity-location replacement, packed source
removal, and any donor-location repair—but no signature or component lookup.

The initial paired run measured native at roughly `77.7 ns/move` and pinned at
`115.8 ns/move`, with a conspicuous pinned tail. Exact-filter isolated reruns
measured native at approximately `76.9 ns/move` (`7.73 ms` p95 per 100,000 moves)
and pinned at `78.5 ns/move` (`7.90 ms` p95), both at zero managed bytes and zero
collections. The isolated result is effectively tied at this noise level; the
paired pinned result is retained as evidence of scenario/process interference,
not attributed to the storage backend. That extension brought the code suite to
40 scenarios before the composed-world cases below.

The minimal composed-world extension adds two non-comparable 100,000-cycle
create-default, transition, and destroy scenarios. These exercise the checked
sealed-world boundary, entity allocation/location publication, registry-owned
plan validation, retained-column migration, source donor handling, destruction,
location removal, and generation advancement as one complete lifecycle cycle.
Exact-filter isolated runs measured approximately `149.3 ns/cycle` native and
`149.4 ns/cycle` pinned, both with zero managed bytes and zero collections. The
paired run measured `147.7 ns/cycle` native and `205.8 ns/cycle` pinned; as with
the primitive migration guard, the pinned slowdown disappeared in isolation and
is classified as scenario/process interference. The code suite now contains 42
scenarios; the tracked accepted artifact remains historical until a controlled
full-suite capture is intentionally accepted.

The archetype-bound component-access extension adds two non-comparable one-million
round scenarios. One round performs four checked world calls against an
enableable data component: copied set, copied get, enable set, and enable get.
The 16-byte binding is created from the source-generated component ID before
sealing, so the measured path includes entity generation/location validation,
exact-archetype and world ownership checks, dense store routing, and row bounds,
but no component scan, metadata lookup, boxing, allocation, or mutable-ref escape.
Final exact-filter isolated runs measured approximately `51.0 ns/round` native
and `52.6 ns/round` pinned, with zero managed bytes and zero Gen0/1/2 collections.
The paired run measured `50.2` and `67.5 ns/round`; the larger pinned difference
again shrank materially in isolation. These cases brought the code suite to 44
scenarios; they are not yet comparable with Current entity access.

The first barrier-scoped chunk-query extension adds four non-comparable
100,000-row scenarios: enable-aware reads and two-float-plus-enable writes over
native and pinned stores. Exact generated-ID-backed bindings are prepared before
sealing; timed execution walks only occupied chunks and invokes constrained struct
visitors with stack-only spans. No entity generation/location lookup, delegate,
closure, materialized entity handle, or structural work occurs per row. The
benchmark visitors use `AggressiveOptimization`: the first ordinary paired run
showed a false late-scenario uplift from shared generic tiered PGO (`~35-39`
ns/row early versus `~2` ns/row late), and isolated processes confirmed that was
execution-order warmup rather than an arena difference. Three repeated optimized
focused processes measured reads at approximately `1.49-1.61 ns/row` and writes
at `2.08-2.23 ns/row`; native and pinned were effectively identical, with zero
managed bytes and zero collections. These figures are a steady optimized-loop
guard, not cold-JIT, scheduling, change-tracking, filter, multi-component, or
Current-comparison evidence. The code suite now contains 48 scenarios; the
tracked accepted artifact remains unchanged pending a controlled full capture.

The first seal-compiled multi-archetype plan adds two non-comparable 100,000-row
scenarios. Two different archetype layouts both require `BenchmarkPosition` for
read and enableable `BenchmarkVelocity` for write. Seal-time compilation prebinds
both columns for both layouts; timed execution reads position, writes two velocity
floats, updates one enable bit, and accumulates a checksum without signature scans,
component lookup, delegates, boxing, or entity-handle materialization. Three
repeated focused processes measured approximately `2.56-2.80 ns/row`; native and
pinned remained tied and every run reported zero managed bytes and zero
collections. The measured loop is still a hand-authored one-read/one-write shape,
not evidence for arbitrary generated arities, filters, change tracking, parallel
scheduling, or Current parity. The code suite now contains 50 scenarios, and the
tracked accepted artifact remains unchanged.

The normalized-filter extension adds four non-comparable 100,000-row scenarios.
One required-enabled tag and one required-disabled data component are reduced to
direct enable-word offsets at sealing, while a required/excluded signature pair
leaves exactly one of three archetypes routed. The dense cases match every row and
coalesce each occupied chunk into one visitor slice. The alternating cases match
50,000 rows but deliberately fragment them into single-row slices, guarding the
current safe-view worst case where unmatched rows are never exposed for writing.

Exact isolated runs after eagerly optimizing the range-dispatch generic measured
the dense native/pinned cases at approximately `2.23-2.29 ns/matched row` and the
alternating cases at approximately `10.56-11.09 ns/matched row`, all with zero
managed bytes and zero collections. Before the range helper was excluded from
tiered recompilation, alternating runs became bimodal at roughly `0.5` versus
`3.7-4.9 ms` per visit; a no-tier diagnostic isolated that effect and motivated
the measured optimization. Filter density, number of enable predicates, long
contiguous runs, random masks, and a future generated mask-consumer still require
their own matrix. The code suite now contains 54 scenarios, and the tracked
accepted artifact remains unchanged.

The first generated access-family extension adds two non-comparable 100,000-row
scenarios over two matching archetype layouts. The generated two-read/one-write
plan reads an eight-byte `BenchmarkPosition` plus a 64-byte `BenchmarkWide`,
writes an eight-byte enableable `BenchmarkVelocity`, touches every wide field,
and updates the enable bit. This is 80 effective component bytes per row before
entity-index and enable-word traffic. A focused run measured `4.679 ns/row`
native and `4.664 ns/row` pinned; the following full-suite run measured `4.438`
and `4.456 ns/row`. That corresponds to roughly `17.1-18.0 GB/s` of effective
component traffic, so the future report-level byte-throughput work is necessary
to distinguish query overhead from cache or memory-bandwidth limits. All 56
scenarios in the full run reported zero managed bytes and zero Gen0/1/2
collections. The new results remain temporary directional evidence; the tracked
accepted artifact was not replaced.

The generated-family write-axis extension adds a mirrored pair with one eight-byte
position read, one 64-byte wide write, and one eight-byte velocity write. Its
effective component width is also 80 bytes, but 72 bytes are written instead of
read. The focused native/pinned results were both `5.307-5.308 ns/row`; the
following full-suite results were `5.094` and `5.078 ns/row`, or roughly
`15.7 GB/s` before entity-index/enable-word traffic. In the final full pass after
the checked-boundary correction, 1R2W measured `5.048` and `5.021 ns/row`, or
roughly `15.8-15.9 GB/s`. In that same process the regenerated 2R1W family measured
`4.381-4.434 ns/row`, remaining consistent with
its pre-refactor focused guard. This is directional evidence that the common
data-driven emitter did not regress 2R1W and that the write-heavy kernel costs
roughly 13-14 percent more on this machine; hardware counters and repeated isolated
processes must determine how much is store pressure versus generated loop shape.
All 58 scenarios reported zero managed allocation and zero Gen0/1/2 collections.
The tracked accepted artifact remains unchanged.

The generated read-only-axis extension adds a 2R0W pair over the same two
archetype layouts. It reads an eight-byte `BenchmarkPosition` and every field of
a 64-byte `BenchmarkWide`, for 72 effective component bytes per row, without a
writable component or enable-word update. The focused run measured `3.643`
ns/row native and `3.692` pinned. The following full-suite run measured `3.495`
and `3.490 ns/row`, or approximately `20.6 GB/s` of effective component reads.
The existing 2R1W and 1R2W guards remained at `4.433-4.515` and
`5.119-5.147 ns/row`, respectively. All 60 scenarios reported zero managed
allocation and zero Gen0/1/2 collections. This is directional evidence from one
machine, not a cache/main-memory bandwidth classification; repeated isolated
processes and hardware counters remain required. The tracked accepted artifact
remains unchanged.

The representative wide-arity extension adds equal-width 3R1W and 2R2W pairs.
Both process 84 logical component bytes per row: 3R1W reads an eight-byte
position, 64-byte wide value, and four-byte scalar before writing eight-byte
velocity; 2R2W reads position and scalar before writing the wide value and
velocity. Focused runs measured 3R1W at `5.269-5.371 ns/row` and 2R2W at
`6.011-6.055 ns/row`. The following full-suite run measured `5.049-5.099` and
`5.807-5.811 ns/row`, approximately `16.5-16.6 GB/s` and `14.5 GB/s`,
respectively. Equal logical width makes this cleaner directional evidence of
write-distribution cost than comparisons between different component widths.
All 64 scenarios reported zero managed allocation and zero Gen0/1/2 collections.

The initial dense-change-version extension adds two unchanged-column scan
scenarios and makes all pre-existing writable query benchmarks pay the real
conservative stamp cost. Each store preallocates one eight-byte stamp per physical
component column per possible chunk; report facts expose change-version bytes and
total store metadata separately. One generated writable visit lazily advances one
world version and stamps every exposed writable column once per visited chunk.
Read-only execution performs no version work, and filtered execution stamps only
chunks with matching rows. The scan uses `BenchmarkPosition`, which is initialized
before its baseline and is not written by any preceding timed scenario, avoiding
fixture-order contamination.

Four v17 full captures measured tracked 3R1W across `5.029-5.234 ns/row` and
tracked 2R2W across `5.804-5.966 ns/row`, versus the documented v16 full ranges
of `5.049-5.099` and `5.807-5.811`. Paired native/pinned differences span roughly
`-1.4%` through `+3.7%` for 3R1W and `-0.1%` through `+2.7%` for 2R2W. The
spread is larger than the apparent cost, so these runs demonstrate no material
regression but do not prove zero overhead; repeated isolated A/B processes are
required for that claim.

The unchanged-position scenario performs 64 complete sweeps of 552 occupied
chunks per measured iteration (`35,328` checks) so timer granularity does not
dominate. Three isolated focused processes measured native at
`44.3-45.7 ns/chunk`; pinned measured `44.4-46.1` in two processes with one
`55.0 ns/chunk` tail. A later full-suite pass measured approximately `25.0`
ns/chunk, exposing substantial process/order/cache state rather than a credible
instant speedup. The current path includes world/binding validation per chunk and
therefore provides the deliberately heavier baseline for the v18 route. Every
66-scenario full capture reported zero managed allocation and zero Gen0/1/2
collections. The tracked accepted artifact remains unchanged.

The v18 changed-read extension adds four scenarios around the first independent
consumer cursor and seal-compiled direct route. The unchanged pair reads no rows:
it performs 64 sweeps over 552 chunks (`35,328` version checks), using a cursor
created after scalar initialization and a plan that prebinds `BenchmarkWide` for
read while tracking the distinct `BenchmarkScalar` column. Three isolated paired
processes measured the validation-heavy scalar polling guard at
`44.6-45.0 ns/chunk` and the trusted prebound cursor route at
`17.8-19.6 ns/chunk`, approximately `2.3-2.5x` the throughput without changing
version storage.

The all-changed pair resets its cursor and marks every scalar chunk during the
untimed prepare callback, then reads every field of the 64-byte wide component.
Isolated means were `3.07-3.17 ns/row`, approximately `20.2-20.9 GB/s` of
logical component reads before entity-index traffic. The final full-suite pass
measured the unchanged path at `16.35-16.50 ns/chunk` and all-changed delivery at
`2.95-2.98 ns/row`, again demonstrating process/cache-state sensitivity. The report
now records the 16-byte cursor layout alongside the eight-byte version. All 70
scenarios reported zero managed allocation and zero Gen0/1/2 collections. These
are V3-internal mechanism comparisons, not Current/V3 claims, and the accepted
artifact remains unchanged.

The v19 exact-row extension adds four scenarios and makes sparse row history
opt-in per tracked archetype column. The exact consumer retains the v18 chunk
gate, then constructs 64-row masks only inside changed chunks. Scalar writes mark
one physical row, filtered writable ranges mark their exposed rows, and dense
chunk writes remain a single dense stamp. Copied cursors can lag independently;
the implementation does not destructively drain shared dirty masks.

The final focused 100,000-row/two-archetype run reserved `1,495,040` bytes for the
one exact-tracked scalar column. Native and pinned unchanged scans measured
`17.599` and `17.649 ns/chunk` over 35,328 checks per iteration. Scanning all 552
chunks and returning exactly one 64-byte row measured approximately `10.06 us`
native and `10.23 us` pinned total. All four scenarios reported zero managed
allocation and zero collections. The suite now contains 74 scenarios. The
planned occupancy/change-density matrix must determine where that memory and row
refinement beat dense false-positive delivery; this focused guard alone does not.
The final full v19 pass measured unchanged exact polling at `15.18-15.21
ns/chunk` and one-row delivery at `9.06-9.38 us`; all 74 scenarios again reported
zero managed allocation and zero collections. As with v18, the focused/full
difference is retained as process/order/cache-state evidence, not averaged away.
The first implementation accidentally published every filtered range even when
the written column had no exact consumer, pushing the alternating guard to roughly
`13.1-14.6 ns/matched row`. The full suite exposed it. Caching the per-column
opt-in and retaining once-per-chunk publication for dense-only columns restored
the final focused guard to `10.54-10.62 ns/matched row`, within the earlier
documented band.

These 100,000-row figures may be strongly cache-resident on the test machine and
must not be labeled true main-memory bandwidth. The benchmark recommendations
now require an opt-in 1K through 10M working-set ladder, explicit retained-byte
accounting, hardware-cache classification, and counters where available. The
tracked accepted artifact remains unchanged.

The v21 public-world extension added three non-comparable native-backed scenarios
and brought the executable suite to 85 cases. `ComponentLookup<T>` uses a
seal-compiled two-byte route per registered archetype and follows the entity's
current archetype, so random access does not require a signature scan and remains
valid across migration. Its second and third scenarios acquired a stack-only
`ComponentAccessor<T>` before the timed loop. That accessor could outlive the
validation/execution boundary, so those measurements are retained as historical
evidence but the API and scenario semantics are intentionally superseded.

One same-process focused comparison measured the internal native exact-archetype
binding at `55.963 ns/round`, the safe public world-wide lookup at `75.333
ns/round`, and the scoped public accessor at `43.801 ns/round` before the public
values were detached from world ownership. After removing that hidden retention,
the final same-process guard measured `55.126 ns/round` internal, `61.618 ns/round`
safe public, and `43.775 ns/round` scoped public. A separate focused public-only
process measured `75.661 ns/round` safe, `46.247 ns/round` scoped, and `128.343
ns/round` scoped plus migration. All paths reported zero managed bytes.
The subsequent full 85-scenario process measured `64.746`, `43.705`, and
`134.524 ns/round` for those three public cases; every scenario reported zero
managed allocation and zero Gen0/1/2 collections. The retained lookup-route fact
was four bytes for the two-archetype fixture, and the public archetype handle was
eight bytes.
The result demonstrated why ownership/lifecycle checks should be amortized at a
system boundary, but it was still directional: tiered JIT, scenario order, cache
state, and the different exact versus world-wide route shapes prevent treating
one capture as a stable ranking.

The v22 semantic profile, `ecs-next-phase1-bounded-access-v22`, replaces the
escapable acquisition with `ExecuteAccess<T,TJob>`. A constrained struct job
receives the stack-only accessor only during one exclusive callback. Structural
mutation, mutation outside the supplied accessor, nested ECS execution, disposal,
and fault publication are rejected until `finally` releases the scope. The non-migration scenario validates once for
the complete one-million-operation job; the migration scenario executes one
bounded operation, exits, then legally migrates, repeated 250,000 times. A focused
public-only process measured `72.590 ns/round` for the fully checked route,
`57.291 ns/round` for the bounded job, and `151.609 ns/round` for bounded job plus
migration. All reported zero managed bytes and zero Gen0/1/2 collections. The
migration figure includes per-round scope entry/exit and is intentionally not
comparable with v21. The subsequent complete 85-scenario v22 process measured
`62.493`, `56.723`, and `163.288 ns/round`; every scenario reported zero managed
bytes and zero Gen0/1/2 collections. Keep both safe and bounded-job guards, repeat
them in isolated processes, and later measure the bounded API inside real
generated systems.

The v23 structural-command foothold adds three non-comparable public-world
scenarios and brings the executable suite to 88 cases. Each scenario includes
atomic span recording plus deterministic playback; create additionally resolves
every deferred token inside the timed operation. Fixture repair/reset occurs
outside timing. The implementation is the first scalar-playback baseline, not the
planned per-worker/grouped/bulk Phase 3 path.

A focused process measured 100,000 deferred transition, create-default, and
destroy commands at `105.690`, `41.671`, and `57.304 ns/command`. The subsequent
complete 88-scenario process measured `125.114`, `37.485`, and `69.338
ns/command`. Its transition/create/destroy p50 barriers were `12.360`, `3.689`,
and `6.767 ms`; p95 barriers were `13.558`, `3.960`, and `7.220 ms`; and p99
barriers were `13.830`, `4.013`, and `7.224 ms`. Every scenario reported zero
managed bytes and zero Gen0/1/2 collections. The v23 artifact also locks a 16-byte
public transition, 16-byte deferred token, 24-byte command record, 36-byte
playback result, and 3,600,000 fixed bytes for the 100,000-slot command fixture
(24-byte records plus 12-byte create-result slots). Keep focused and full-process
captures separate, then compare this scalar baseline against grouped/bulk playback
using equivalent canonical final state.

The v24 profile, `ecs-next-phase1-phase-indexed-structural-commands-v24`, adds a
four-byte stable phase link per command. Recording appends directly to ordered
live-destroy, create, deferred-destroy, remove, and add chains; production
playback visits only the selected chain, while the v23 five-scan executor remains
available internally as a differential and benchmark oracle. The arena grows
from 36 to 40 payload bytes per slot, or from 3,600,000 to 4,000,000 bytes for
the 100,000-command fixture. Epoch maximum now rotates the process-unique buffer
ID rather than reusing an old token identity.

Three comparison scenarios bring the suite to 91: scalar-oracle end-to-end,
scalar-oracle playback-only, and phase-indexed playback-only, alongside the
existing production end-to-end case. A focused same-process run measured
100,000 transitions at `112.722/101.265 ns/command` for scalar/indexed
end-to-end and `99.644/96.645 ns/command` for playback only. The complete
91-scenario process measured `133.079/123.049 ns/command` end-to-end, but the
playback-only pair tied at `117.953/118.144 ns/command`. All paths reported zero
managed bytes and zero Gen0/1/2 collections. The result is intentionally modest
and process-sensitive: stable phase chains are useful infrastructure for later
route grouping, but repeated isolated processes must justify their four-byte
cost, and true bulk store mutation remains the dominant opportunity.

The v25 profile, `ecs-next-phase1-grouped-structural-transitions-v25`, keeps the
v24 phase chains and groups only maximal contiguous runs of the same prepared
transition. Runs shorter than eight commands retain scalar playback. Longer runs
use fixed 256-entity stack blocks, bulk-reserve/default destination row ranges,
commit once in record order, and reverse-remove unused anonymous reservation
tails. Source packed removal, donor repair, and retained-column transfer are
still per entity; this is the first bulk-reservation layer, not final bulk source
compaction. The scalar executor remains executable as the semantic oracle.

For 100,000 homogeneous transitions, the focused v25 process measured
scalar/grouped end-to-end at `113.837/100.478 ns/command` and playback-only at
`101.412/95.830 ns/command`, improvements of about `11.7%` and `5.5%`. The full
91-scenario process measured `134.116/114.753 ns/command` end-to-end and
`120.296/110.077 ns/command` playback-only, improvements of about `14.4%` and
`8.5%`. Full-process grouped p50/p95/p99 barriers were
`11.458/11.621/11.636 ms`, versus `13.404/13.639/14.568 ms` for scalar
end-to-end; grouped playback-only measured `11.012/11.050/11.074 ms`, versus
`12.034/12.092/12.125 ms`. Every one of the 91 scenarios reported zero managed
bytes and zero Gen0/1/2 collections. These are same-process, homogeneous-route
guards, not V2/V3 parity evidence; route-entropy, fragmented-source, mixed-
conflict, component-width, and true source-compaction sweeps remain required.

The v26 profile, `ecs-next-phase1-contiguous-structural-copy-v26`, adds a strict
locality fast path above v25. It proves that the accepted source prefix is valid,
distinct, and physically contiguous without a hash table; copies retained data
column-at-a-time across maximal simultaneous source/destination runs; copies
enable bits in word-bounded ranges; merges dense history once per run; then
performs the existing record-ordered source removals and donor repairs. Invalid,
conflicting, or fragmented blocks retain the v25 path. Four new playback-only
guards bring the suite to 95 scenarios: scalar/grouped wide-retained columns and
scalar/grouped deliberately scattered source order.

For 100,000 transitions, the focused v26 process measured scalar/grouped
end-to-end at `113.755/80.228 ns/command` (`29.5%` faster), dense playback-only
at `98.475/72.863 ns/command` (`26.0%`), wide-retained playback at
`115.903/84.431 ns/command` (`27.2%`), and scattered-source playback at
`110.830/106.895 ns/command` (`3.6%`). The complete 95-scenario process measured
`143.200/103.458 ns/command` end-to-end (`27.8%`),
`131.506/97.152 ns/command` dense playback (`26.1%`),
`143.967/107.940 ns/command` wide-retained playback (`25.0%`), and
`153.217/141.493 ns/command` scattered playback (`7.7%`). Full-process grouped
dense p50/p95/p99 barriers were `9.705/9.790/9.995 ms`, versus
`13.126/13.445/13.479 ms` for scalar playback. All 95 scenarios reported zero
managed bytes and zero Gen0/1/2 collections. The scattered guard shows graceful
fallback, not a general locality sweep; source-chunk density, route entropy,
conflicts, exact-history cost, and source compaction remain explicit future axes.

The v27 profile, `ecs-next-phase1-planned-source-compaction-v27`, adds adaptive
source compaction above v26. It selects the plan only when a locality-proven
source set contains the current donor tail and either reaches 32 rows or fully
evacuates that donor chunk. The plan identifies the exact shrinking tail once,
skips leaving rows as donors, moves each surviving donor at most once, bulk-clears
tail storage and history, updates chunk-list state once per affected chunk, and
transactionally publishes migrated/survivor locations. Other batches retain the
measured v26 path.

Eight equal-work round-trip guards hold a 100,000-row resident source and 131,072
transitions per sample while sweeping tail-local batches of 8, 32, 128, and 256.
A controlled focused build with only planned compaction disabled measured v26/v27
grouped execution at `83.293/76.197`, `67.909/47.375`, `59.757/44.054`, and
`61.074/44.781 ns/transition`: approximately `8.5%`, `30.2%`, `26.3%`, and
`26.7%` faster. The 8-row case enters only when the reverse leg evacuates its
whole source chunk; it does not weaken the normal 32-row threshold.

The complete 103-scenario v27 process measured dense scalar/grouped end-to-end
at `131.628/91.420 ns/command`, playback-only at `117.810/86.828`, wide playback
at `135.582/97.454`, and scattered playback at `148.582/133.779`. Its equal-work
tail grouped means were `86.974`, `45.703`, `43.017`, and `44.374
ns/transition`. The tail-8 full-suite p95 contained a process-order outlier
(`17.220 ms`) despite a `10.587 ms` median, while the focused process measured a
`10.061 ms` p95; repeat isolated processes remain mandatory before treating that
tail as a latency property. All 103 scenarios reported zero managed bytes and
zero Gen0/1/2 collections. Broader partial-overlap/removal-density, exact-history,
component-width, route-entropy, conflict, and multi-chunk evacuation sweeps remain
future characterization rather than implied generality.

The v28 whole-destroy extension adds four scalar-oracle/production playback
guards, bringing the suite to 107 scenarios. Exact whole-archetype live-destroy
validates complete ordered coverage, resets occupied chunk/list plus dense/exact
change metadata without eagerly clearing inaccessible payload, and retires the
validated slots in record order. Every partial, duplicate, stale, mixed,
unordered, or sparse set retains scalar semantics. An attempted partial planner
was removed after the shared 10%-sparse case regressed from roughly `85` to `155
ns/destroy` and a narrowed donor-tail-ten-percent guard also lost to scalar.

Two focused processes measured 100,000-row exact-whole scalar/production
playback in the ranges `50.27-63.92/11.63-12.03 ns/entity`. Their distinct
partial cases kept 100,000 rows resident and destroyed the donor-tail 10,000
(10%) in the ranges `50.69-63.41/44.04-58.91 ns/entity` through
scalar/production fallback. These are equal-work executor guards inside each
pair, not full-versus-partial comparisons; all four allocated zero managed bytes
in both processes. In both process orders of the shared 50,000-row Current/Next
adapter, full V3 playback improved from the immutable `72.50 ns/entity`
baseline to `29.17/29.03`. The corresponding V2-control-normalized ratios were
`0.397/0.392`, so the roughly 60% improvement is repeatable. Ten-percent sparse
measured `89.36/86.91` versus `85.03`, with normalized ratios `1.014/0.978` in
the noise/tolerated band rather than a demonstrated improvement. Canonical state
matched and V3 remained allocation-free. V2 still leads full playback at
`2.45/2.47 ns/entity`, so the next partial-destroy design must be
benchmark-selected rather than assumed to follow transition compaction.

V29 expands the suite to 117 scenarios and makes the destroy matrix explicit:
whole population plus physical tail and sparse selections at 1%, 10%, and 50%
of a fixed 100,000-row resident store. Every selection has an equal-work scalar-
oracle/production playback pair. Sparse fixtures destroy all survivors and
rebuild creation-order-packed ownership outside the timed boundary before every
sample; otherwise repeated swap removal would silently turn the requested stride
into a different physical workload. Setup cost is intentionally excluded from
these playback-only cases and must not be compared with end-to-end barriers.

The complete run reported zero managed allocation in all 117 scenarios. Whole,
tail-10%, and tail-50% scalar/production means were respectively
`50.08/7.58`, `50.28/9.03`, and `50.18/7.72 ns/destroy`. Tail-1% measured a
`49.5/31.1` median but a `49.57/57.91` mean because rare interruptions dominate
one 1,000-command barrier; use median/p95 and repeated isolated processes for
that crossover. Sparse 1/10/50% scalar/production means were
`70.20/62.28`, `70.78/65.02`, and `70.97/63.46`. Those sparse differences come
from the lower scalar floor and executor preflight, not a new source-chunk bulk
compactor.

The complete shared comparison, repeated in both child-process orders, retained
87 comparable rows, zero state/descriptor/measurement mismatches, and zero V3
timed allocation. At 50,000 entities, immutable-baseline/current V3 playback was
`72.50 -> 14.37/13.36 ns/entity` for full destroy, `72.30 -> 45.64/44.19` for
immediate destroy, and `85.03 -> 56.65/56.27` for 10%-sparse destroy. Their
V2-control-normalized ratios were `0.199/0.185`, `0.643/0.624`, and
`0.644/0.623`. Add/remove transition playback also improved roughly 5-9% at
50,000 rows in both orders, which is the required evidence that moving clearing
to reservation did not buy destruction by making migration slower.

V30 expands the complete suite to 129 scenarios and integrates the bitmap
experiment's strongest narrow result without exposing sparse storage to queries.
It temporarily retains eligible destroyed rows during one structural barrier,
reuses them for balanced same-archetype default creates, and compulsorily packs
any leftovers before later phases. The selector requires at least 64 fixed bytes
per row and create demand covering every resolvable live destroy in the store;
narrow and unbalanced churn use the V29 packed executor. Exact whole/suffix
destroy paths remain ahead of hole reuse.

The accepted focused capture holds 100,000 resident rows constant and compares
the scalar oracle, an exact phase-indexed packed V29-style control, and production
V30 at 10% and 50% churn. At 12-byte rows, packed/V30 was
`52.444/52.273 ns/command` and `51.190/51.352`, within `0.3%`. At 524 fixed bytes
per row, it was `77.957/58.821` and `74.270/56.666`, improvements of `24.5%` and
`23.7%`. Every hole was reused, no final compaction or donor relocation occurred,
the command results were validated, and all 12 rows allocated zero managed bytes.

The complete 129-scenario run also reported zero managed allocation. Both
four-warmup/fifteen-iteration V2/V3 process orders retained 87 comparable rows,
zero canonical-state mismatches, and 15 explicit unsupported rows. Because the
shared adapter does not yet define balanced deferred destroy/create churn, those
captures protect existing behavior while the same-build packed control measures
V30's new feature directly.

Representative 50,000-row V3 longitudinal means are below. V28, V29, and V30
show the range across V2-first and V3-first isolated captures; the immutable
baseline has one accepted V2-first value. Host variation remains visible rather
than being averaged away.

| Scenario | V3 baseline | V28 | V29 | V30 |
| --- | ---: | ---: | ---: | ---: |
| 1R1W query | 1.166 | 1.132-1.149 | 1.130-1.144 | 1.118-1.134 ns/row |
| immediate create | 43.86 | 44.83-46.92 | 43.63-45.45 | 43.69-44.24 ns/entity |
| full deferred destroy playback | 72.50 | 29.03-29.17 | 14.36-14.63 | 13.40-14.26 ns/entity |
| 10%-sparse destroy playback | 85.03 | 86.91-89.36 | 57.14-61.04 | 56.42-56.63 ns/destroy |
| immediate destroy | 72.30 | 74.10-74.75 | 45.17-46.61 | 44.16-44.44 ns/entity |
| add-default playback | 82.70 | 80.99-84.13 | 77.80-78.36 | 76.97-77.38 ns/entity |
| remove playback | 85.99 | 86.39-89.28 | 79.36-80.43 | 78.57-78.75 ns/entity |

V30's balanced hole-reuse scenario is absent from the older adapter catalogs, so
baseline/V28 values for that feature would be fabricated. Its exact incremental
comparison is the same-binary packed control above; future accepted adapter
coverage should add the workload without rewriting historical artifacts.

V31 is a consolidation checkpoint, not a new structural algorithm. It replaces
boolean executor combinations and interleaved selection with explicit production,
packed-oracle, and scalar-oracle modes; named live-destroy and transition-run
plans; exact whole/tail selection before hole-demand scanning; and named temporary-
hole state constants. All 201 ECS tests and all 12 comparison-core tests pass, and
the complete 129-row Next suite remains zero-allocation.

The shared adapter now adds nine provisional balanced-wide rows across the three
scales and three timing boundaries. Each barrier destroys ten percent of one
72-byte archetype and creates the same count back into it. Both process orders
produced 96 comparable rows, zero state mismatches, and 15 unsupported rows. At
50,000 residents, V2/V3 playback measured `169.854/52.697` and
`166.281/53.343 ns/operation`, so V3 was about `3.22x/3.12x` faster. End-to-end
measured `168.730/61.752` and `167.588/62.455`, about `2.73x/2.68x` faster. V3
allocated zero timed bytes; V2 allocated roughly 604,160 bytes in playback and
684,208 bytes end-to-end per iteration.

Canonical capture discovered that V2's deferred default-create path can publish
stale donor-row payload after sparse batch destruction. The new comparison is
therefore explicitly provisional: it hashes every survivor byte plus replacement
liveness, component presence, and lifecycle, but normalizes replacement payload
between generations; V3 independently asserts true default initialization. This
is a contract difference found by the harness, not an accepted V2 behavior.

On the same host, the clean V30 commit's exact focused control measured narrow
10% packed/production at `32.600/36.468 ns/command`; V31 measured
`32.920/34.132` and `33.290/34.782` in two processes. The apparent 4% V31
production-to-packed penalty is therefore smaller than V30's same-host penalty,
not a consolidation regression. Narrow 50% remained tied; wide 50% retained a
material production win. Saved two-day-old absolute values are not substituted
for same-build controls.

Eight warmup iterations precede the 31 measured iterations. Forced collections
isolate each microbenchmark before sampling; reported collection deltas cover
the measured workload but are not production GC-pause distributions. Managed
allocation uses the current-thread counter because these kernels are deliberately
single-threaded. Correctness belongs to the unit/reference-model suites; the
benchmark checksum is only an execution guard.

The generated generic-ID loop is a code-generation floor and may be constant
propagated or hoisted by the JIT. Do not interpret its sub-nanosecond result as a
general schema-lookup throughput claim; the instance/type/key/hash cases are
reported separately. Read-only traversal deliberately reuses initialized data,
so initialization cost is excluded from its steady-state scan timing.

Run from the repository root:

```bash
dotnet run --project \
  _Benchmarks/UltraSim.ECS.Benchmarks/UltraSim.ECS.Benchmarks.csproj \
  -c Release
```

The default report is `_Benchmarks/results/next-ecs-phase1.json`. Override it
with `--output <path>`. Use `--filter <name-or-category-fragment>` for focused
repeated-process runs; the selected filter is recorded in JSON. Do not treat a
single local run as an accepted baseline.
Capture repeated isolated processes and environment/profile hashes before using
results as a performance gate.
