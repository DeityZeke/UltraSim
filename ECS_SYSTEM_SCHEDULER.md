# ECS Bounded System Scheduler

Status: authoritative ECS 3.1.1 system/scheduler contract as of 2026-08-11.

ECS 3.1.0 added one bounded system graph above the existing 3.0.36 generated
chunk machine. It does not introduce another storage, query, task, or structural
executor. ECS 3.1.1 hardens its capability boundaries, makes the partition cap
global across every matching archetype route, and repairs the longitudinal 100K
benchmark evidence without changing the scheduler architecture.

```text
register systems and fixed reservations
        ↓
Seal compiles stages, dependencies, conflicts, routes, and waves
        ↓
PreSimulation structural playback
        ↓
run due generated chunk jobs and RangeJobs
        ↓
merge worker segments in stable logical order
        ↓
PostSimulation structural playback
        ↓
tick++
```

## Public model

`EcsSchedulerOptions` is an optional member of `EcsWorldOptions`. Its default
value disables the scheduler completely: no scheduler object, worker, scheduler
array, structural lane, or scheduler payload is created. Enabled options fix
system, dependency, work-item, segment, worker, per-producer command/payload,
partition, profiling, and optional phase-stagger capacities before construction.

Cold registration separates three concepts:

- `SystemDefinition` is immutable cold metadata: canonical key, stage, schedule,
  execution policy, manual flag, optional persistence ID/schema version,
  dependencies, supplemental random-access masks, structural component routes,
  parallel thresholds, and explicit disjoint-random-write proof.
- a user-owned `ISystemRuntime<TJob>` object is persistent state. It is allocated
  and registered once, receives owner-thread `Initialize`, `Prepare`, `Complete`,
  and `Shutdown`, and may own cursors, timers, configuration, accumulators, or
  future serialized state. There is no runtime object per entity, chunk, or job.
- `TJob` is an ephemeral struct copied into fixed participant slots. Generated
  chunk jobs and `IRangeJob` use constrained calls; no boxing, captured delegate,
  reflection, or per-row interface dispatch occurs.

The public world surface includes scheduler presence/count/reservation/tick/run
mode properties; generated `Register...System` methods for the supported access
families; `RegisterRangeSystem`; `Tick`; `RunManual`; enable/schedule controls;
and bounded system/scheduler snapshot copies. `SystemId.FromRegistrationIndex`
allows forward cold graph references, which `Seal` validates.

The generated scheduled access catalog is:

```text
1R0W, 0R1W, 1R1W, 2R0W, 3R0W,
2R1W, 1R2W, 3R1W, 2R2W
```

Writable families also have tracked variants. Exact one-watch changed-row systems
are scheduled serially so cursor acknowledgement remains success-only.

## Tick, graph, and cadence semantics

Stages are strictly ordered as Input, Movement, SpatialIndex, Perception, Combat,
AI, WorldSim, Networking, and Cleanup. Within a stage, `Seal` compiles flat dense
system arrays and integer wave numbers. Explicit `Requires` edges are hard order
barriers. Missing references, cycles, reverse-stage dependencies, and duplicate
system/persistence identity fail cold.

Generated read/write masks and declared supplemental random access create automatic
conflicts. Conflicting systems cannot occupy the same wave. Read/read systems may
coexist; any overlapping write serializes unless the definition supplies the
narrow explicit disjoint-random-write proof. Structural component declarations
compile source-independent routes and authorize deferred command emission; they
are not in-graph memory hazards because topology remains frozen until the barrier.
Systems requiring semantic command-emission order declare `Requires`; otherwise
stable logical merge and barrier phase order define the result.

Cadence is `(enabled, periodTicks, startOffset)`. Dependencies mean ordering when
both systems are due; they do not make a slow prerequisite run on a dependent's
tick. Optional automatic staggering is deterministic, opt-in, capped, and excludes
manual, Movement, and Combat systems. Runtime schedule/enabled changes do not
change sealed topology.

`Tick(delta)` consumes external commands before systems, freezes topology while
the graph executes, merges graph output, applies it at the post barrier, then
increments the tick. Commands recorded during one tick therefore cannot alter
the graph currently reading the world. `RunManual` runs one declared manual node
without incrementing the tick; its structural output is merged but intentionally
waits for the next external tick's pre-barrier.

## Serial and parallel execution

Serial scheduled chunk execution calls the same prepared route and generated
stack-only chunk view as direct `ExecuteJob`. The scheduler adds one cold generic
system-boundary dispatch, preparation/completion, cadence/graph checks, and
profiling only when enabled. There is no scheduler-specific component view.

Parallel execution uses persistent dedicated threads plus optional owner-thread
participation. `BackgroundWorkerCount` is exactly the number of created threads;
`ExecutionConcurrency` is background workers plus the caller when enabled. The
worker set starts at `Seal` and shuts down synchronously during world disposal.
Dispatch uses one fixed work-item array, static contiguous chunk/range partitions,
participant-local job copies, a short bounded spin, and fixed kernel events.

`Auto` stays serial below the declared row threshold and avoids waking background
workers. Above it, partition count is bounded by reserved work items, execution
concurrency, maximum partitions per system, and minimum rows per partition.
That maximum is global across all matching archetype routes. A 16-route query
with an eight-partition maximum produces at most eight work items. The unchanged
16-byte partition stores a starting route/chunk and logical occupied-chunk span;
execution advances across route boundaries without another work-item type.
Independent nodes in one wave and partitions of one node use the same runtime;
there is no nested scheduler. Work stealing was not retained because the measured
uniform chunk/range matrix scales strongly with static partitioning, while a
queue/stealing protocol would enlarge hot state and determinism cost without
evidence that it is needed.

`IRangeJob.Execute(context,start,count)` provides the same bounded worker runtime
for non-ECS index spaces. Every index is assigned exactly once by static
partitions. It is not a general task graph.

## Worker capabilities and change publication

Workers never receive `EcsWorld`. An expiring `SystemJobContext` contains only
tick/delta/system/participant/logical-work identity and routes operations back
through declaration checks. It provides optional paid `uint` index-to-`Entity`
reconstruction, declared prepared random read/write/enable lookup, and one
participant-owned structural writer. Default, stale-epoch, foreign-thread,
undeclared, and post-callback use is rejected.

Generated chunk-column access and supplemental random lookup are separate
capabilities. A generated `Write<Position>` span does not authorize an arbitrary
`context.TryRead/TryWrite(positionLookup, entity)`; only `AdditionalReads` and
`AdditionalWrites` do. Combined masks still drive graph conflicts, and
`RandomWriteTargetsAreDisjoint` governs only explicitly declared supplemental
writes.

Persistent runtimes may use the facade during `Initialize` and `Shutdown`, but
direct public runtime access is rejected throughout graph execution, including
owner-thread `Prepare` and `Complete`. A retained `EcsWorld` therefore cannot
bypass declared lookups, structural lanes, logical segment order, or dependency
metadata.

Generated views retain the cheap world-local entity-index span. Reconstructing a
full 12-byte generational `Entity` is explicit and measured separately. It is for
relationships, structural targets, and occasional random work—not ordinary dense
traversal.

Each writable system invocation owns one `SchedulerChangePublication` gate.
Every participant publishes the same lazily acquired `ChangeVersion`; the first
actual tracked write wins a single CAS and no-write tracked jobs do not advance
the version. Dense ranges remain conservative. Exact row stamps remain precise,
and participant-local job results are presented to `Complete` in stable
participant order rather than reduced with hot-loop atomics.

## Deferred structure

Every execution participant owns one fixed `StructuralCommandBuffer` lane and
fixed typed byte-payload arena. A job's `SystemStructuralWriter` can create,
destroy, execute prepared transitions, and issue source-independent typed
`Add<T>`/replacement or `Remove<T>` only for components declared before sealing.
The route table maps `(component, current archetype)` directly to the already
existing prepared transition and physical destination column. Payloads are copied
unboxed with their cold-known size/alignment.

The command remains 24 bytes. Its result plus phase link retains the 40-byte total
slot. `DeferredEntity` remains 16 bytes. An add payload stores its byte offset in
the existing command union; no new command-side array was introduced.

Each nonempty logical work item becomes one segment. Segment merge sorts nothing:
the coordinator walks the already assigned system/wave/work ordinals, copies each
lane segment into the main bounded arena, and remaps local deferred references to
their canonical slots. This makes command order stable across worker counts.
Deferred identities are intentionally valid only inside their originating work
segment. Cross-segment or expired use faults rather than acquiring schedule-order
meaning accidentally.

Capacity exhaustion is explicit. There is no growth after sealing and no worker
heap fallback. Playback remains the existing scalar/packed-oracle/production
engine described in [`ECS_STRUCTURAL_PLAYBACK.md`](ECS_STRUCTURAL_PLAYBACK.md).

## Fault and lifetime contract

Preparation, worker execution, completion, structural merge, and both barriers
form one owner-coordinated terminal transaction. A worker captures the original
exception with system, stage, logical work, and participant identity. The
coordinator joins the dispatch, discards all worker lanes, skips post-simulation
playback, releases the graph scope, and terminally faults the world. A failed
tick does not increment `TickCount`. Conservative change stamps already published
for exposed writable memory remain visible.

Initialization unwinds already initialized systems in reverse order on failure.
Disposal shuts systems down in reverse registration order and joins every worker.
Contexts and lane epochs are invalidated at every boundary.

## Fixed representation and inspection

Current locked sizes are:

| Value | Size |
| --- | ---: |
| `SystemId` | 4 bytes |
| `SystemSchedule` | 16 bytes |
| `EcsSchedulerOptions` | 44 bytes |
| internal chunk partition | 16 bytes |
| internal partition shape | 8 bytes |
| structural command | 24 bytes |
| structural command slot | 40 bytes |
| `DeferredEntity` | 16 bytes |

`SchedulerReservedBytes` accounts fixed scheduler array/arena payload including
node participant-job slots, masks, work/partition/segment arrays, participant
state, the main command arena, and every producer lane. It deliberately excludes
CLR object/array headers, event objects, and OS thread stacks, which are runtime
implementation overhead rather than reserved ECS payload. Snapshot counters
include dispatch/wake/park totals, deterministic merge counts and payload bytes,
paid identity resolutions, worker-thread managed allocation, and optional
cumulative pre-barrier/graph/merge/post-barrier timings. Per-system snapshots
include identity, stage, cadence, policy, wave, last/average/peak duration, last
rows/chunks/work items, and serial/parallel choice.

`WorldCapacityPlanner` accepts an explicit managed scheduler reservation and
reports entity metadata, scheduler bytes, total managed bytes, total native
bytes, and the combined bound. The exact input is the cold registered world's
`SchedulerReservedBytes`; options alone cannot predict generic participant-job
payload sizes before systems are registered.

Profiling is fixed at construction. Disabled profiling avoids hot atomic identity
accounting and timestamp reads. Snapshot collection is owner-only and copies into
caller-provided spans.

## Admission evidence

The original 3.1.0 fixed-runtime-policy benchmark contains 143 canonical,
allocation-free timing rows. Its 60,000-tick scheduler-shaped 100K workload measures serial
mean/p95/p99 `981.229/1036.430/1105.880 us` and parallel
`409.010/508.556/613.015 us`; both modes produce the same state hash, allocate
zero caller/worker managed bytes, and collect no GC generation. Seven independent
10,000-tick replays at concurrency 1/2/4/8/16 plus repeated 8-way runs converge
to the same hash and changed-row count.

That workload remains a scheduler stress profile, not a longitudinal replacement
for the 3.0.35/3.0.36 soak: it scans 100K rows to choose 5K writes and uses a
200-destroy/200-create structural mix. ECS 3.1.1 adds a separate parity profile
that preserves the original deterministic 5K arbitrary writes, 500 RNG reads,
and exact 100 destroy + 100 create + 200 transition mix. The public 3.0.36
control, scheduled serial, and scheduled parallel variants must match one logical
canonical hash and every workload counter. Observation-version hashes are
reported separately and intentionally differ because scheduled tracked writes
publish one shared version per logical invocation rather than one version per
scalar public write.

The isolated V2-first and V3-first comparison orders each contain 102 comparable
contracts, zero state mismatches, and 10 explicit unsupported rows. V3 allocates
zero timed bytes in 102/102 rows. The complete API, design, timing matrices,
validation totals, and original full 102-row V2/V3 table are in
[`ECS_3_1_0_ACCEPTANCE_REPORT.md`](ECS_3_1_0_ACCEPTANCE_REPORT.md). The 3.1.1
audit verdict, corrected longitudinal parity profile, typed structural evidence,
and current regression links are in
[`ECS_3_1_1_HARDENING_REPORT.md`](ECS_3_1_1_HARDENING_REPORT.md).

## Deliberate boundary after 3.1.1

This milestone does not include dynamic systems/schema/archetypes, work stealing,
NUMA/affinity, fusion, a general reduction framework, arbitrary public query
filters, lifecycle event rings, persistence serializers, prefab expansion,
replication, networking, or admin UI. Stable persistence IDs and runtime-state
ownership are present so a later serializer can bind state without making
scheduler timing or physical worker details durable.
