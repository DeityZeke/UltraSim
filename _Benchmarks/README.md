# UltraSim Next Benchmarks

These projects measure the root Next implementation. Frozen V2 Current and
Legacy runners remain beneath `_V2/_Benchmarks` and run in separate processes.

The initial ECS primitive/schema suite is intentionally marked non-comparable.
It establishes allocation, validation, layout-candidate, generated component-ID,
runtime schema-lookup, and metadata-access measurements before a semantically
equivalent Current/Next scenario adapter exists.

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
The entity pool now combines that row with a four-byte world-local `ArchetypeId`
in a 12-byte `EntityLocation`; final packing remains benchmark-driven.

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
