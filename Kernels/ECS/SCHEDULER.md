# Bounded ECS scheduler

The scheduler is one sealed system graph over the generated chunk and range-job
execution machinery. It is not a task framework and does not change ECS storage.

## Cold model

`EcsSchedulerOptions` reserves systems, dependencies, work items, structural
segments/lanes, participants, profiling, partitions, and optional phase
staggering before world construction. Disabled options allocate no scheduler.

`SystemDefinition` declares stable identity, stage, cadence, execution policy,
dependencies, generated access (from registration), supplemental random lookup,
structural capabilities, thresholds, and optional persistence identity.

Persistent `ISystemRuntime<TJob>` state receives initialize/prepare/complete/
shutdown. Ephemeral job structs are copied into fixed participant slots. Seal
validates dependencies and compiles stages/waves/conflicts/routes. Runtime does
not topologically sort or allocate tasks each tick.

## Ordering and cadence

Stages are strict. Inside a stage, `Requires` and component read/write conflicts
produce integer waves. Read/read may share a wave. Structural declarations
authorize deferred commands; topology remains frozen, so they are not ordinary
in-graph data hazards. Semantic command-emission order uses `Requires`.

`SystemSchedule` is `(enabled, periodTicks, startOffset)`. A dependency orders
two systems when both are due; it neither forces a slow prerequisite to run nor
suppresses an independently due dependent.

Cadence is step-relative. `PeriodTicks = 10` means ten authoritative steps.
Continuous integration uses `DeltaSeconds`; exact deadlines and persisted facts
use authoritative fixed-point/world time. Per-time behavior proves its meaning
at multiple fixed cadences. Per-step behavior is deliberate.

Optional phase staggering is disabled by default. When enabled globally and for
an eligible system, Seal chooses a deterministic key-hash offset capped by
`MaximumStaggerPhases`. It is not cost-aware/even load balancing and hash
collisions are allowed. Manual, Movement, Combat, period-one, and explicitly
offset systems are not automatically staggered.

## Serial and parallel execution

Serial execution calls the same typed chunk/range work as direct jobs. Parallel
execution uses persistent dedicated workers plus optional caller participation.
Generated jobs partition occupied chunks; one chunk is not split into row ranges.
Range jobs partition their bounded index domain. `MaximumPartitionsPerSystem` is
global across all matching archetype routes.

`Auto` parallelizes only when the shape has usable partition units and meets the
declared row threshold. `Parallel` still requires more than one row/work unit.
Static deterministic partitions and logical ordinals define merge order; worker
completion order does not.

Supplemental random access requires declared capability masks. Parallel random
mutation defaults to serial and requires `PartitionDisjoint` when enabled. The
proof includes overlap with generated reads/writes and shared physical metadata.

## Changes and structure

One logical writable system invocation acquires one change-publication candidate.
Participants share that candidate and publish it once if any write succeeds.

Each participant records structure into its own bounded lane. Segments carry
preassigned logical ordinals. The scheduler merges segments into the main arena
in deterministic order and remaps lane-local deferred identities. Structural
effects become visible only at the barrier.

## Fault and lifetime

Worker contexts, jobs, chunk views, lookups, and structural writers are valid
only during their callback. Direct public world access is rejected while a graph
executes. Retained main command-buffer capabilities cannot bypass graph-time
structural lanes.

Expected registration/capacity/input rejection occurs before state commit.
Unexpected worker or post-proof failures fault the world, stop further logical
work/publication, wake participants, and discard unmerged lane output. Shutdown
joins persistent workers.

## Inspection

Public snapshots report system identity, stage, schedule, compiled wave,
execution policy, run counts/timing, rows/chunks/work items, and parallel status.
Scheduler snapshots report worker activity, dispatch, structural merge, and
reserved payload. Profiling is optional and capacity-bound.
