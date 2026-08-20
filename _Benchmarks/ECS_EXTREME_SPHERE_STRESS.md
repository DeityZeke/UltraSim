# UltraSim ECS extreme-sphere stress benchmark

Status: implemented and accepted as a diagnostic/integration benchmark on 2026-08-11  
ECS profile: `3.1.2 scheduler proof hardening`  
Version effect: none; adding a benchmark does not advance the ECS version

## Verdict

V3 is complete enough to reproduce the important parts of the historical
pulsating-sphere stress test today. The engine-neutral V2/V3 test is an exact
logical comparison, and the V3 Godot companion measures the renderer handoff and
GPU boundary separately.

The strongest current result is the isolated 8,000,000-entity run:

- V2, 7 background workers plus caller: `159.367 ms/tick` mean;
- V3, 7 background workers plus caller: `18.225 ms/tick` mean;
- V3, 15 background workers plus caller: `15.933 ms/tick` mean and
  `16.842 ms` p95;
- all compared V2/V3 final-state hashes match;
- V3 allocates zero managed bytes after warmup in every accepted run;
- V2 allocated about `2.75 GB` while spawning 8M entities and retained roughly
  `2.8 GB` working set, versus about `482 MiB` working set and `467 MiB` of
  known ECS reservations for V3.

The 15-worker V3 mean narrowly fits a 16.667 ms tick, while p95 narrowly misses
it. That is ECS-only and does not leave budget for other game work. At the
historical 7-background-worker/8-physical-core setting, 4M entities are
comfortably inside the budget and 8M are just outside it.

The visual result also resembles the historical observation. One million fully
updated visible low-poly spheres cost about `15.60 ms` of measured GPU time and
Godot reported about `49.6 FPS`. Two million visible spheres raised measured GPU
time to `30.42 ms` and reduced observed performance to about `25.1 FPS`.

## Projects

- `UltraSim.ECS.ExtremeSphere.Core` owns the deterministic workload, canonical
  hashing, timing, allocation, memory and JSON contracts.
- `UltraSim.ECS.ExtremeSphere.V2` compiles the frozen V2 ECS into an isolated
  process adapter.
- `UltraSim.ECS.ExtremeSphere.V3` uses the public V3 generated system/scheduler
  surface.
- `UltraSim.ECS.ExtremeSphere` launches isolated children, checks canonical
  equivalence and produces joined JSON/Markdown reports.
- `UltraSim.ECS.ExtremeSphere.Godot` hosts the same V3 systems in Godot 4.7.1
  Mono and measures transform packing, buffer submission and renderer telemetry.

The generations remain process-isolated because V2 and V3 intentionally use
conflicting namespaces and type names.

## Exact workload contract

Every entity owns 36 bytes of logical component state:

| Component | Fields | Bytes |
|:---|:---|---:|
| Position | X, Y, Z | 12 |
| Velocity | X, Y, Z | 12 |
| Pulse | speed, frequency, phase | 12 |

Spawn positions are deterministic and uniformly distributed through the volume
of a sphere. Speed, frequency and initial phase are deterministic functions of
the stable entity ordinal and seed. No entity array is retained merely to drive
the benchmark.

Each tick executes three strictly ordered systems:

1. advance pulse phase;
2. derive radial velocity from current position and sine of phase;
3. integrate position from velocity.

Every system visits the complete population, so one tick performs `3N` query
row visits. The reporting model counts 96 effective component bytes per entity
tick:

- phase: 12-byte read plus 12-byte write;
- velocity: two 12-byte reads plus one 12-byte write;
- movement: one 12-byte read plus a 12-byte read/write destination.

This is a deliberately hostile streaming test. It has no culling, activity LOD,
spatial filtering, structural churn, physics, network work, gameplay branching
or sleep cadence. The headless test does not include rendering. Entities are
seeded once, warmed once, then kept alive at a fixed population.

The canonical state includes stable entity index and the exact bit patterns of
all nine floats. The hash is commutative with respect to physical chunk/row
order, so V2 and V3 may lay out entities differently without hiding logical
differences. It also records population, a float-bit checksum, non-finite count,
and minimum/mean/maximum radius.

## Execution controls

Accepted headless child processes used:

```text
Release
DOTNET_TieredCompilation=0
DOTNET_ReadyToRun=0
DOTNET_GCServer=1
64 KiB ECS chunk target
fixed 1/60 second delta
seed 0xC0FFEE
```

Serial means caller-only traversal. Parallel means the configured number of
persistent background workers plus caller participation. The principal profile
uses seven background workers, matching the eight physical cores of the test
machine.

Hardware:

```text
AMD Ryzen 7 7800X3D, 8 cores / 16 threads, 96 MiB L3
32 GiB system memory
NVIDIA GeForce RTX 4070 Ti SUPER, 16 GiB
Linux x64
Godot 4.7.1 stable Mono, OpenGL compatibility renderer
```

## V2 versus V3 principal results

The following is the accepted V2-first pass. Positive speedup means V3 is
faster.

| Entities | Mode | V2 mean ms | V3 mean ms | V2 p95 ms | V3 p95 ms | V3 speedup | V2 ns/entity | V3 ns/entity |
|---:|:---|---:|---:|---:|---:|---:|---:|---:|
| 10,000 | Serial | 0.150 | 0.083 | 0.162 | 0.091 | 1.80x | 15.01 | 8.32 |
| 10,000 | Parallel | 0.211 | 0.058 | 0.227 | 0.066 | 3.66x | 21.12 | 5.77 |
| 100,000 | Serial | 1.496 | 0.853 | 1.516 | 0.867 | 1.75x | 14.96 | 8.53 |
| 100,000 | Parallel | 1.792 | 0.177 | 1.855 | 0.255 | 10.10x | 17.92 | 1.77 |
| 1,000,000 | Serial | 14.872 | 8.414 | 14.999 | 8.558 | 1.77x | 14.87 | 8.41 |
| 1,000,000 | Parallel | 18.470 | 1.581 | 19.407 | 2.171 | 11.68x | 18.47 | 1.58 |
| 2,000,000 | Serial | 30.447 | 17.023 | 30.855 | 17.271 | 1.79x | 15.22 | 8.51 |
| 2,000,000 | Parallel | 38.036 | 3.284 | 38.880 | 4.324 | 11.58x | 19.02 | 1.64 |
| 4,000,000 | Serial | 61.223 | 35.036 | 62.025 | 35.558 | 1.75x | 15.31 | 8.76 |
| 4,000,000 | Parallel | 76.980 | 7.733 | 78.360 | 9.343 | 9.96x | 19.25 | 1.93 |
| 8,000,000 | Serial | 142.743 | 70.075 | 144.994 | 71.485 | 2.04x | 17.84 | 8.76 |
| 8,000,000 | Parallel | 159.367 | 18.225 | 162.167 | 20.388 | 8.74x | 19.92 | 2.28 |

Every row completed with matching canonical state and zero non-finite values.

The reverse V3-first pass reproduced the result. Most mean ratios moved only a
few percent. One V3-first 4M serial child reported `43.912 ms`, while the
V2-first child and three immediate isolated reruns reported `35.036`, `34.362`,
`34.967` and `34.410 ms`. It is retained in the raw reverse-order artifact as
process interference/outlier evidence rather than silently deleted; it is not
used as the representative 4M result.

## Throughput and worker scaling

| Entities | Background workers | V2 mean ms | V3 mean ms | V3 p95 ms | V3 speedup | V3 effective GiB/s |
|---:|---:|---:|---:|---:|---:|---:|
| 1,000,000 | 3 | 18.973 | 2.371 | 2.656 | 8.00x | 37.71 |
| 1,000,000 | 7 | 18.470 | 1.581 | 2.171 | 11.68x | 56.55 |
| 1,000,000 | 15 | 18.186 | 1.482 | 1.881 | 12.27x | 60.31 |
| 8,000,000 | 3 | 168.615 | 21.269 | 22.520 | 7.93x | 33.63 |
| 8,000,000 | 7 | 159.367 | 18.225 | 20.388 | 8.74x | 39.25 |
| 8,000,000 | 15 | 157.183 | 15.933 | 16.842 | 9.87x | 44.89 |

SMT is measurably useful to V3 for this large streaming workload, although the
gain from 8 to 16 participants is much smaller than the gain from 4 to 8.
Worker-count policy should remain workload-driven; this result does not imply
that 15 background workers are appropriate for small jobs or a complete game
tick.

At 8M/7 workers, the V3 phase, velocity and movement systems average about
`0.381`, `1.315` and `0.582 ns/row`, respectively. The total scheduler tick
includes the three dispatch/barrier boundaries and averages `2.278 ns/entity`.

## Allocation, GC and memory

| Population/mode | Spawn allocation | Spawn GC0/1/2 | Timed bytes/tick | Working set | Private bytes | Known V3 ECS | V3 native chunks |
|:---|---:|:---:|---:|---:|---:|---:|---:|
| V2 1M serial | 327.8 MiB | 5/5/5 | 272 B | 333.4 MiB | 376.2 MiB | n/a | n/a |
| V3 1M serial | 0 B | 0/0/0 | 0 B | 99.3 MiB | 112.3 MiB | 58.5 MiB | 38.4 MiB |
| V2 1M parallel | 327.8 MiB | 5/5/5 | 528 B | 360.5 MiB | 446.3 MiB | n/a | n/a |
| V3 1M parallel | 0 B | 0/0/0 | 0 B | 99.8 MiB | 169.4 MiB | 58.5 MiB | 38.4 MiB |
| V2 8M serial | 2.562 GiB | 8/8/8 | 272 B | 2,714.3 MiB | 3,090.5 MiB | n/a | n/a |
| V3 8M serial | 0 B | 0/0/0 | 0 B | 481.3 MiB | 531.6 MiB | 467.0 MiB | 306.5 MiB |
| V2 8M parallel | 2.562 GiB | 8/8/8 | 528 B | 2,831.3 MiB | 3,044.3 MiB | n/a | n/a |
| V3 8M parallel | 0 B | 0/0/0 | 0 B | 481.8 MiB | 588.8 MiB | 467.0 MiB | 306.5 MiB |

V3's known 8M reservation is about `58.4 bytes/entity`, including the 36-byte
logical payload, entity/location metadata, scheduler storage, chunk slack and
other bounded world metadata. Native chunk payload itself is about
`38.3 bytes/entity`.

V2's small warmed tick allocation did not trigger a collection during the timed
intervals, but its spawn path allocates heavily and forces full collections at
large populations. V3 reports zero caller and scheduler-worker allocation.

## V2 scheduling defect exposed by the comparison

The first adapter attempt put all three V2 systems in one stage and expressed
the logical dependencies through `Requires`. V2 topologically sorts those
systems, but its subsequent batch packer places each system into the first
non-conflicting batch without preserving dependency depth.

For this pipeline the result can become:

```text
batch 0: phase + movement
batch 1: velocity
```

Movement does not conflict with phase, but it depends transitively on velocity.
It therefore consumes the previous tick's velocity. This produced a canonical
mismatch and is likely one concrete example of why the historical V2 test was
remembered as "slightly wrong."

The comparison adapter does not patch frozen V2. It assigns phase, velocity and
movement to three ordered V2/V3 stages, which is an existing public scheduling
mechanism in both kernels. This makes the measured logical workload exact while
keeping V2's storage, traversal, dirty publication and worker machinery intact.

## Godot renderer companion

The Godot companion uses:

- one six-radial-segment/three-ring sphere mesh;
- one `MultiMesh` for the visible subset;
- shadows, GI and occlusion culling disabled;
- one world-sized custom AABB, so there is no per-entity CPU culling;
- one 12-float/48-byte transform per visible entity;
- one complete transform pack and one `MultimeshSetBuffer` submission per frame;
- no dirty filtering or amortized render update;
- a separate hidden archetype whose entities remain fully simulated.

| Simulated | Visible | Workers | Sim mean/p95 ms | Pack mean ms | Submit mean ms | Script work mean ms | GPU mean ms | Observed FPS mean |
|---:|---:|---:|:---|---:|---:|---:|---:|---:|
| 100,000 | 100,000 | 7 | 0.214 / 0.299 | 0.442 | 0.266 | 0.922 | 1.581 | 197.6 |
| 1,000,000 | 0 | 7 | 1.529 / 2.083 | 0.000 | 0.000 | 1.529 | 0.028 | not meaningful |
| 1,000,000 | 1,000,000 | 7 | 2.064 / 2.708 | 4.689 | 5.914 | 12.668 | 15.605 | 49.6 |
| 2,000,000 | 2,000,000 | 7 | 4.794 / 5.415 | 9.469 | 12.158 | 26.421 | 30.422 | 25.1 |
| 8,000,000 | 0 | 7 | 17.808 / 19.714 | 0.000 | 0.000 | 17.808 | 0.025 | not meaningful |
| 8,000,000 | 1,000,000 | 7 | 19.965 / 21.695 | 4.762 | 7.838 | 32.566 | 15.488 | 30.2 |
| 8,000,000 | 1,000,000 | 15 | 17.842 / 18.669 | 4.785 | 7.968 | 30.596 | 15.324 | 31.1 |

All measured Godot intervals report zero managed allocation and zero collections.
The 8M/1M-visible run uses about `934 MiB` working set and `1.064 GiB` private
bytes with seven workers, including Godot, the 48 MB CPU transform buffer, GPU
driver mappings and the complete ECS world.

`MultimeshSetBuffer` measures CPU call duration; renderer work may remain queued.
`ViewportRenderGpu` is therefore the useful GPU-side metric. A 1M-visible
`ForceSync` control changed mean submit time from `5.914` to `6.233 ms` in this
driver/backend, while measured GPU work remained about `15.60 ms`.

Canonical cross-host checks also passed. The 100K and 2M Godot cases use the
same total tick counts as their headless counterparts and produce identical
hashes. An additional 8M/360-tick console run produced the same
`8091369D9A39A7B5C9568FC5D70D0B24` hash as the two-archetype 8M/1M-visible Godot
run.

The measured Godot run is editor-hosted with Release-managed assemblies. The
installed 4.7.1 export-template directory is non-Mono; Godot expects
`4.7.1.stable.mono/linux_release.x86_64`. A true exported Release executable
therefore remains unavailable until the matching Mono templates are installed.
This limitation is recorded rather than relabeling the editor host as an export.

## V1 disposition

The historical implementation and intended test were found, including:

- `_Archive/_V2/_OLD/_OLD/Server/ECS/Systems/EntitySpawnerSystem.cs`;
- `_Archive/_V2/_OLD/_OLD/Server/ECS/Systems/OptimizedPulsingMovementSystem.cs`;
- `_Archive/_V2/_OLD/_OLD/Server/ECS/Systems/OptimizedMovementSystem.cs`;
- `_Archive/_V2/_OLD/_OLD/Client/ECS/Systems/AdaptiveMultiMeshRenderSystem.cs`;
- `_Archive/_V2/_OLD/_OLD/tests/OldECS.Tests/MovementBenchmarkTests.cs`.

They confirm the remembered shape: on-demand random sphere-volume spawning,
pulse/velocity work, movement work, a manual reusable thread pool, low-poly
MultiMesh rendering, a one-million visual cap, and continued ECS spawning beyond
that cap.

The new contract deliberately preserves the historical *shape*, not every V1
instruction. V1 fused phase advancement and radial-velocity calculation into
one pulsing system, used `FastInvSqrt`, and used a random fallback for positions
near zero. The V2/V3 comparison splits phase advancement into a third ordered
system and uses the same deterministic square-root and zero-position fallback
in both generations. V2 and V3 are therefore exact peers; V1 is only a
conceptual/historical reference unless its original project is reconstructed.

V1 was not admitted as a live metric. Its test project currently compiles the
stale monolithic `GodotECS.csproj`, which recursively includes unrelated RunUO,
network, generated `obj`, terrain and old API generations. A direct Release test
attempt fails with hundreds of errors: duplicate network types and assembly
attributes, conflicting `World`/`SystemManager` generations, missing
`System.Drawing`, `CodeDom` and `PerformanceCounter` assemblies, and stale
override contracts. Repairing that honestly is a reconstruction project, not a
few reference updates.

Copying only the two old loops into a new adapter would measure a new port, not
V1. Historical comments claiming roughly 5-8 ms pulse and 2-3 ms movement at 1M
remain source comments, not accepted evidence, and are not included in the
comparison table.

## Reproduction

Build and run the principal V2/V3 suite:

```bash
dotnet build _Benchmarks/UltraSim.ECS.ExtremeSphere.V2/UltraSim.ECS.ExtremeSphere.V2.csproj -c Release
dotnet build _Benchmarks/UltraSim.ECS.ExtremeSphere.V3/UltraSim.ECS.ExtremeSphere.V3.csproj -c Release
dotnet build _Benchmarks/UltraSim.ECS.ExtremeSphere/UltraSim.ECS.ExtremeSphere.csproj -c Release

dotnet run --project _Benchmarks/UltraSim.ECS.ExtremeSphere/UltraSim.ECS.ExtremeSphere.csproj \
  -c Release --no-build -- \
  --profiles=10000:120:600,100000:120:600,1000000:60:300,2000000:40:200,4000000:30:120,8000000:20:80 \
  --workers=7 \
  --output=_Benchmarks/results/extreme-sphere-current
```

Add `--v3-first` for the reverse process order. Add `--reuse-results` to
regenerate the joined reports without rerunning children.

Run the Godot companion with Release-managed assemblies:

```bash
_Benchmarks/UltraSim.ECS.ExtremeSphere.Godot/run-release-managed.sh \
  --entities=1000000 --visible=1000000 --warmup=120 --frames=600 \
  --workers=7 --output=_Benchmarks/results/godot-extreme-sphere.json
```

Set `GODOT_BIN` to override the executable. Put `--godot-headless` first for a
headless host. Benchmark arguments also include `--serial`, `--no-upload`,
`--sync-gpu`, `--seed`, `--radius`, `--delta` and `--chunk-kib`.

## Accepted evidence

- [`ecs-extreme-sphere-v2-v3-3.1.2-v2-first-2026-08-11.json`](Baselines/ecs-extreme-sphere-v2-v3-3.1.2-v2-first-2026-08-11.json)
- [`ecs-extreme-sphere-v2-v3-3.1.2-v3-first-2026-08-11.json`](Baselines/ecs-extreme-sphere-v2-v3-3.1.2-v3-first-2026-08-11.json)
- [`ecs-extreme-sphere-v2-v3-3.1.2-workers3-2026-08-11.json`](Baselines/ecs-extreme-sphere-v2-v3-3.1.2-workers3-2026-08-11.json)
- [`ecs-extreme-sphere-v2-v3-3.1.2-workers15-2026-08-11.json`](Baselines/ecs-extreme-sphere-v2-v3-3.1.2-workers15-2026-08-11.json)
- Godot artifacts use the `ecs-extreme-sphere-godot-4.7.1-*` prefix in
  [`Baselines`](Baselines/).

The joined JSON preserves every comparable metric available to the adapters:
mean/min/p50/p95/p99/p99.9/max/stddev, per-system distributions, ns/row,
entity-ticks/sec, effective GiB/s, spawn cost, all-thread/caller/instrumented
worker allocation, GC counts, managed/heap/committed/working/private memory,
known ECS/native payload, scheduler dispatch/wake/park counts, canonical state,
and runtime metadata. Godot artifacts add frame-work, transform-pack, submission,
renderer CPU/GPU, FPS, adapter, buffer-size and cross-host state metrics.

## Interpretation boundaries

This benchmark proves that V3 can host and materially outperform V2 on this
specific extreme, homogeneous, full-population scheduled workload. It does not
replace the mixed-operation soak, structural comparison, scheduler adversarial
tests, change-tracking decomposition or eventual real gameplay vertical.

It also does not prove that V3 is universally 9-12x faster. V2's parallel path is
particularly poorly matched to three sequential lightweight full-world systems,
where its repeated delegate/worker dispatch and dirty publication cost more than
its parallelism saves. V3's static generated partitions are unusually strong in
exactly this shape. The serial 1.75-2.04x advantage is the more conservative
single-thread kernel comparison.
