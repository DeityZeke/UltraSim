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

Run from the repository root:

```bash
dotnet run --project \
  _Benchmarks/UltraSim.ECS.Benchmarks/UltraSim.ECS.Benchmarks.csproj \
  -c Release
```

The default report is `_Benchmarks/results/next-ecs-phase1.json`. Override it
with `--output <path>`. Do not treat a single local run as an accepted baseline.
Capture repeated isolated processes and environment/profile hashes before using
results as a performance gate.
