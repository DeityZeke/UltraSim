# ECS Public Serial Chunk-Job Execution

Status: authoritative ECS 3.0.36 execution contract as of 2026-08-11.

ECS 3.0.36 exposes the existing generated query machine through the public,
engine-neutral `EcsWorld` facade. It is a serial owner-thread execution primitive,
not a scheduler or a complete V2-equivalent system runtime.

```text
prepare typed job plan
        ↓
Seal compiles direct archetype/column routes
        ↓
ExecuteJob validates world + plan once
        ↓
struct job receives stack-only chunk views
        ↓
finally releases the synchronous execution scope
```

There is no per-row entity lookup, signature lookup, component lookup, dictionary,
delegate, boxing, query construction, scheduler dispatch, or managed allocation.
The public job and the internal reference visitor use the same constrained
generated `Execute(in Chunk)` contract and the same route/chunk loop.

## Public surface

Cold preparation uses the existing world-owned `MaximumPreparedPlans` table:

- `PrepareReadWriteJob<TRead,TWrite>()` for the handwritten 1R1W family;
- generated `PrepareReadReadJob`, `PrepareReadReadWriteJob`,
  `PrepareReadWriteWriteJob`, `PrepareReadReadReadWriteJob`, and
  `PrepareReadReadWriteWriteJob` methods for the existing 2R0W, 2R1W, 1R2W,
  3R1W, and 2R2W catalog;
- `PrepareChangedRowsJob<TRead,TChanged>()` for the existing exact one-watch
  changed-row consumer.

Each public plan is an opaque eight-byte typed capability over the existing
internal seal-compiled plan. Default plans, foreign-world plans, execution before
initialization, owner-thread violations, use after disposal, and capacity
exhaustion are rejected. Preparing a plan before later archetype registrations is
supported; `Seal` compiles the final registered archetype set.

Ordinary execution is:

```csharp
var plan = world.PrepareReadWriteJob<Velocity, Position>();

world.Seal();
world.Initialize();
world.BeginRun();

var job = new MovementJob(deltaSeconds);
world.ExecuteJob(plan, ref job);
```

Jobs are value types constrained by the matching `I...ChunkJob` interface.
Passing by `ref` permits serial caller-owned checksums or small outputs. This is
not the future parallel reduction contract; workers will require scheduler-owned
job copies, worker-local state, and a bounded reduction/merge.

Chunk views are `readonly ref struct` values. They expose only declared typed
spans, count, enable-state operations allowed by the access declaration, and the
existing world-local `ReadOnlySpan<uint>` entity indices. Full generational
`Entity` values are not materialized. Arena/store objects, handles, physical
column indexes, enable words, `ChunkRow`, and mutable metadata remain internal.
Safe managed code cannot retain the view or its spans after the callback.

The entity-index span is the cheap identity route already present in bound chunk
storage. A future full-entity operation must be an explicit paid capability that
reconstructs or looks up the generation only when requested.

## Dense and conditional writes

Writable generated jobs deliberately have two contracts rather than one wrapper
that taxes the dense loop.

`ExecuteJob` is the dense contract. Its generated chunk exposes direct writable
spans. Before invoking a callback, the kernel publishes the complete exposed
writable chunk/range using one invocation-shared `ChangeVersion`. Publication
happens before user code, so an exception cannot hide a potentially exposed
write. A callback that ultimately chooses not to mutate still has conservative
dense publication semantics.

`ExecuteTrackedJob` is the conditional contract for the writable generated
families. Its writable slots are `TrackedWritableComponentChunk<T>` values:

- `DenseSpan` publishes the exposed range and then returns direct contiguous
  mutable access;
- `GetTracked(row)` and `SetTracked(row, value)` publish exactly that row before
  mutable access;
- `SetEnabledTracked` and generated writable enable setters use the same exact
  publication contract;
- the first actual write lazily acquires one version shared by every writable
  column and chunk in the invocation;
- dense chunk metadata is stamped at most once per writable column/chunk, while
  enabled exact history records touched rows without a temporary bitmap, sort,
  or dirty-index list;
- a no-write conditional job does not advance the world change version.

Tracked job preparation reuses the ordinary generated plan. It adds no plan
slot, route array, command-buffer array, or persistent reservation. Public filter
authoring is deliberately deferred; the current tracked entry only accepts the
unfiltered plans that the public preparation API creates.

## Changed-row jobs

`ChangedRowsJobPlan<TRead,TChanged>` and `ExecuteChangedRowsJob` expose the
existing exact one-watch consumer without exposing the internal world. Cursors
remain independent values. A cursor acknowledges only after every callback
succeeds; exceptions retain at-least-once retry behavior. The current contiguous
changed-run callback remains the execution shape. Generated two-watch `Any`/`All`
public authoring and a mask-bearing exact consumer are separate measured
extensions, not part of 3.0.36.

## Barrier and ownership contract

One public job call validates lifecycle, owner thread, and plan, then enters the
existing execution scope once for all routes/chunks. While it is active:

- nested job/access execution is rejected;
- immediate scalar and structural mutation is rejected;
- read-only prepared random lookup remains an explicit paid operation;
- recording into the current serial structural command arena remains permitted,
  but playback is rejected until the job returns;
- every exit, including a job exception, releases the scope in `finally`.

The facade remains single-owner. There are no worker tokens, tasks, thread-pool
dispatch, hidden parallelism, job queues, or per-worker structural arenas.

## Admission evidence

The accepted focused matrix compares public and internal execution in both
orders for every generated family at 1K, 10K, and 100K rows. All rows have zero
managed allocation and zero collections, and paired worlds finish with identical
canonical component checksums.

- At 100K, public/internal mean deltas ranged from `-0.14%` to `+0.33%`; the
  median was `+0.03%`.
- At 10K, deltas ranged from `-1.43%` to `+1.57%`, with a median of `+0.02%`.
- At 1K, the fixed facade/owner/lifecycle boundary is visible: median overhead was
  `3.42%`, with a maximum `3.82%`. This is tens of nanoseconds
  per invocation, not a per-row abstraction layer.
- The eight-byte plan wrapper adds zero persistent reservation.

On the 100K tracked-write matrix, representative steady-state means were:

| Pattern | Changed rows | ns/visited row | ns/actual write |
| --- | ---: | ---: | ---: |
| contiguous | 5,000 | 0.954 | 19.07 |
| clustered | 5,000 | 1.741 | 34.82 |
| random in chunk | 5,025 | 1.959 | 38.99 |
| dense | 100,000 | 0.722 | 0.722 |

Every exact consumer delivered the independently computed unique changed-row
count. Serial dispatch controls measured about `37.5 ns` for a no-match plan,
`49.6 ns` for one cold-sized one-row invocation, and roughly `24.6-29.5 ns` per
invocation when 4-64 tiny jobs were issued sequentially in the warmed loop. The
100K 2R0W public job measured about `1.156 ns/row`. These are the fixed serial
controls that the Phase 5 scheduler must beat before parallel dispatch is useful.

The public 100K mixed-soak profile retains the original workload: four full
generated passes, 5K arbitrary checked activity writes, 500 random reads, exact
change consumption, and 400 structural commands. It moves the system-like query
passes, exact consumer, canonical ownership traversal, cursors, and store
inspection onto public APIs. The internal profile remains unchanged as the
longitudinal 3.0.35 control.

Reproduce the focused admission matrix and public soak from the repository root:

```bash
dotnet run --project \
  _Benchmarks/UltraSim.ECS.Benchmarks/UltraSim.ECS.Benchmarks.csproj \
  -c Release -- --public-jobs --output <report.json>

DOTNET_TieredCompilation=0 DOTNET_ReadyToRun=0 \
dotnet run --project \
  _Benchmarks/UltraSim.ECS.Benchmarks/UltraSim.ECS.Benchmarks.csproj \
  -c Release -- --soak --soak-public-jobs --soak-output <report.json>
```

Accepted artifacts are
`_Benchmarks/Baselines/ecs-v3-3.0.36-public-generated-chunk-jobs-2026-08-11.json`
and
`_Benchmarks/Baselines/ecs-v3-3.0.36-100k-mixed-soak-public-jobs-2026-08-11.json`.

## Deliberately deferred system capabilities

3.0.36 is not called a complete system surface. A V2-equivalent system still
requires persistent runtime state, tick/delta context, optional full identity,
source-independent structural commands, manual invocation, cadence, stages,
dependencies, range jobs, parallel workers, reductions, fault semantics,
pre/post simulation barriers, persistence identity, controls, and profiling.

Phase 5/3.1.0 should orchestrate this same chunk primitive rather than wrap it in
a second hot execution framework.
