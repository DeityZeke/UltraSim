# Choosing an UltraSim ECS execution path

UltraSim ECS is fastest and simplest when the unit of work matches the physical
shape of the data. Choose an execution path from the reason the work exists—not
from whether an API can technically reach the data.

## The decision tree

```text
Is the work selected because entities have a component/archetype shape?
    -> generated chunk job

Is it a bounded external/indexed workload or an already-selected list?
    -> RangeJob

Does it touch a few arbitrary entities reached through relationships/references?
    -> prepared ComponentLookup through SystemJobContext

Does it change entity existence or component membership?
    -> SystemStructuralWriter (or the owner structural arena outside a graph)

Does it consume values changed since one consumer last observed them?
    -> ChangeCursor plus a changed-row job

Does it need create/destroy/add/remove history or tombstones?
    -> LifecycleCursor over the world's fixed lifecycle history
```

## Execution-path inventory

| Public concept | Preferred use | Expensive or incorrect use | Preferred replacement |
| --- | --- | --- | --- |
| Generated chunk job | Dense or topology-selected ECS traversal | None when composition is the selector | — |
| Tracked chunk job | Visit a dense candidate set but mutate only some rows | Dense writable view when only a small subset changes | Tracked writable view |
| `RangeJob` | Arrays, spatial buckets, preselected targets, and bounded non-query index spaces | Scanning the entity population and resolving every row with lookups | Generated chunk job |
| `ComponentLookup<T>` | Sparse/irregular access to referenced entities | Per-row access for dense traversal | Generated chunk job |
| `TryGetEntity` | Reconstruct a full generation-safe `Entity` when a later API needs it | Reconstructing every row merely to identify it | Use the chunk's local entity index |
| `SystemStructuralWriter` | Deferred create/destroy/add/remove/transition from a system | Expecting topology changes to be visible during the same graph | Observe after the structural barrier |
| `ChangeCursor` | Independent observation of changed component values | Expecting destroyed identities, membership deltas, or every intermediate value | `LifecycleCursor` for topology history |
| `LifecycleCursor` | Independent observation of successful create/destroy/add/remove commits | Expecting component values or physical archetype/chunk changes | `ChangeCursor` for values; lifecycle intentionally hides storage |

The APIs do not have interchangeable costs. A legal full-population `RangeJob`
using checked lookups can be orders of magnitude slower than a generated job over
the same component shape because it discards contiguous SoA traversal and repeats
entity, generation, location, and route validation.

## 1. Generated chunk jobs: normal ECS work

Use a generated job when component composition selects the rows:

```text
Every alive actor with Position + Velocity
Every dead actor with Cooldown
Every effect with Lifetime
```

Prepare the typed plan and register the system before `Seal`. Sealing compiles
matching archetypes and physical columns once. Execution then walks occupied
chunks directly with stack-only spans and no per-row component lookup.

Read-only families expose read-only spans. Writable families have two modes:

- Dense writable jobs expose direct writable spans and conservatively publish
  the exposed writable range as changed.
- Tracked writable jobs publish only rows mutated through `GetTracked`,
  `SetTracked`, or tracked enable-state methods. Request `DenseSpan` only when the
  complete exposed range is actually being mutated.

Generated `EntityIndices` are world-local slot indices. Reconstruct a full
`Entity` through `SystemJobContext.TryGetEntity` only for accepted rows that need
structural or relationship work.

Use `PrepareQueryFilter(required, excluded)` before `Seal` when composition
selects a narrower topology than the generated data columns alone. Pass the
result to the filtered `Prepare...Job` overload. Required/excluded terms are
compiled into immutable archetype routes; no membership predicate runs per row.
Storage-free tags are the preferred fidelity/category selectors because filter
membership does not make them data columns, read/write capabilities, or scheduler
hazards.

The public 3.2 surface deliberately exposes required/excluded membership only.
Internal enabled/disabled row filtering remains available to kernel plans but is
not part of this narrow public handle. Do not replace a missing predicate with a
full-world checked `RangeJob` scan.

## 2. RangeJob: bounded work already selected elsewhere

Use `RangeJob` when the index space is not naturally an ECS query:

```text
Process 512 targets already selected by an AOI/spatial index
Update one external command array
Partition a bounded procedural-generation range
```

`IRangeSystemRuntime.GetRangeCount` supplies the bounded count. The scheduler
partitions that range and calls `IRangeJob.Execute(context, start, count)`.

If the job needs component data, prepare `ComponentLookup<T>` values before
sealing and declare them through `SystemDefinition.AdditionalReads` and
`AdditionalWrites`. Those declarations authorize supplemental random lookup;
they are not a replacement for the generated columns declared by a chunk job.

Avoid this shape:

```text
range = all entity slots
for every slot:
    TryGetEntity
    TryRead several components
    decide whether the row belongs to the system
```

That is composition-selected ECS work and belongs in a generated chunk job.

## 3. ComponentLookup: sparse checked access

`ComponentLookup<T>` is a prepared, world-bound route for a few arbitrary
entities reached through targets, parents, owners, inventory links, or other
relationships. Each operation intentionally pays generation/liveness, current
location, current archetype route, row, and capability checks.

Inside scheduled execution:

- list the component ID in `AdditionalReads` or `AdditionalWrites`;
- use only the lookup capability issued through `SystemJobContext`;
- treat the context as callback-local and non-retainable;
- prefer `SerialOnly` for supplemental random mutation unless a complete
  cross-participant ownership proof exists.

`PartitionDisjoint` is a correctness proof supplied by the caller, not a
performance hint. Different loop indices, distinct source entities, or distinct
worker partitions do not prove that target component rows are disjoint. Across
participants, every supplemental read domain must avoid conflicting generated or
supplemental writes, and every supplemental write domain must avoid conflicting
generated or supplemental reads and writes.

For example, operations 10 and 11 are not disjoint merely because their loop
indices and source actors differ: if `Target[10]` and `Target[11]` both resolve to
actor 42, both operations still write the same `Health[42]` component row. The
proof must cover resolved target domains for the complete invocation.

When in doubt, use `SerialOnly`. A wrong proof is a data race, not a benign loss
of speed.

## 4. Structural mutation: record now, become visible at a barrier

Creating/destroying entities and adding/removing components changes topology.
Choose one of three boundaries:

- Immediate `EcsWorld.TryCreate`, `TryDestroy`, or `TryTransition` is owner-thread
  mutation outside scheduler graph execution.
- `EcsWorld.StructuralCommands` is the main owner-thread deferred arena.
- `SystemJobContext.Commands` is the only structural writer available inside a
  scheduled job. It writes to a bounded participant/work-item segment that is
  merged deterministically.

Commands placed in the main arena before `Tick` are consumed by that tick's
pre-barrier. Scheduler-recorded commands are consumed by the same tick's
post-graph barrier. `RunManual` merges its recorded segments into the main arena
but deliberately leaves playback to an explicit call or the next tick.

Scheduler-recorded commands commit after the graph. A system later in the same
graph still sees the pre-barrier archetype topology. Component values written
through generated or lookup capabilities are immediate, but recorded membership
changes are not. This applies to every command recorded through a scheduler
writer: create, destroy, add, remove, and prepared transition.

`DeferredEntity` represents a create command in one recording epoch. Main-arena
tokens may be used by later commands in that epoch. Scheduler tokens are more
restrictive: they cannot cross logical work segments. Tokens are transient and
must never be persisted.

All structural capacities are fixed. `Try...` methods report recording-capacity
failure; throwing methods raise `StructuralCommandCapacityExceededException`.
Playback reports expected rejection counts. An unexpected post-proof structural
failure terminally faults the world.

## 5. Change observation: values, not lifecycle

Every `ChangeCursor` is an independent consumer position. One observer cannot
steal dirty state from another. Advance/acknowledgement occurs only after a
changed-row execution completes successfully, so an exception leaves the cursor
retryable.

Changed-row jobs answer:

```text
Which currently present rows have a watched component value newer than my cursor?
```

They do not answer:

```text
Which entity was created or destroyed?
Which component was added or removed?
Which archetype transition occurred?
What tombstone should a delayed consumer process?
What were all intermediate values between observations?
```

Those are lifecycle/event-history requirements, not value-change requirements.

## 6. Lifecycle observation: topology, not values

Lifecycle and change cursors answer different questions:

```text
ChangeCursor
    -> which currently present component values changed?

LifecycleCursor
    -> which entity/component topology commits succeeded?
```

Set `EcsWorldOptions.LifecycleRecordCapacity` to a positive fixed capacity to
enable retention; zero leaves publication disabled. Capture an independent cursor
from the oldest retained record or from now, then drain into caller-owned
`Span<LifecycleRecord>` storage. The one globally ordered timeline contains:

- one `Created` for successful creation, without an initial-component storm;
- one `Destroyed` runtime tombstone, without destruction-time removals;
- `ComponentRemoved` then `ComponentAdded` signature deltas for successful
  membership transitions, each component set in ascending ID order;
- no record for value replacement, enable mutation, rejected commands, donor
  relocation, compaction, or command recording/merge alone.

If a cursor falls behind the fixed ring, `ReadLifecycle` returns
`ResyncRequired` and does not advance it. The consumer must rebuild its own view
and explicitly capture a new cursor. Runtime `Entity` and schema-local
`ComponentId` values are observation facts, not durable persistence identities.
Reading remains owner-thread-only and allocation-free.

Pre-tick main-arena commands publish at pre-barrier playback. Scheduler commands
publish at deterministic post-graph playback. `RunManual` merge alone publishes
nothing.

## 7. World lifecycle and ownership

The normal lifecycle is:

```text
construct world
    -> register archetypes/transitions/systems and prepare lookups/job plans
Seal
    -> compile routes/graph and permanently close registration
Initialize
    -> initialize persistent runtimes and open owner-thread bootstrap mutation
BeginRun
    -> enter running state
Tick / RunManual
Dispose
```

The public `EcsWorld` facade is owned by its construction thread. Persistent
workers receive expiring capabilities, not the world. Direct world access is
rejected while the scheduler graph executes—even from `Prepare` or `Complete`—so
system code must use its declared job, lookup, and structural capabilities.

Failures during lifecycle or unexpected execution failures can terminally fault
the world. A faulted world exposes its cause through `FaultException` but rejects
further ECS execution.

## Four representative choices

```text
"Update Position for every actor with Velocity"
    -> generated read/write chunk job

"Damage 256 actors returned by the combat target selector"
    -> RangeJob over the selected operations + prepared lookups

"Read Health from the one entity referenced by Target"
    -> ComponentLookup<Health> declared as an additional random read

"Add Poisoned to an actor"
    -> SystemStructuralWriter.TryAdd from a job; visible after the barrier
```

When a workload does not fit one of these shapes, preserve correctness with the
checked/serial path first, then use tests and measurement to justify a more
specialized prepared batch or kernel capability.
