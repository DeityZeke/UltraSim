# ECS 3.1.1 Scheduler Capability and Parity Hardening

Status: accepted on 2026-08-11 under semantic profile
`ecs-3.1.1-scheduler-capability-parity-hardening`.

ECS 3.1.1 keeps the 3.1.0 scheduler architecture and fixes four boundary/evidence
defects found by an adversarial source audit. It adds no scheduler strategy,
worker model, command kind, command-buffer array, or public execution abstraction.

## Audit verdict

| Finding | Verdict | 3.1.1 action |
| --- | --- | --- |
| generated column access authorized arbitrary random lookup | confirmed safety defect | separate lookup-only read/write masks |
| retained initialization facade usable during graph execution | confirmed capability escape | reject direct facade runtime access for the entire graph window |
| scheduled 100K soak differed from the longitudinal workload | confirmed evidence defect | retain stress profile; add exact parity profile |
| per-system partition maximum was route-local | confirmed boundedness defect | globally partition occupied chunks across routes |
| structural declarations should create graph conflicts | audit documentation was wrong; code was right | document capabilities/routes plus explicit `Requires` ordering |
| tag Add consumed payload capacity | disproved | zero-payload regression test; no runtime change |
| large source-independent Add/Remove lacked evidence | confirmed | permanent measured cases added; optimization deferred |
| static capacity plan omitted scheduler bytes | confirmed reporting gap | explicit managed scheduler input/accounting |

## Capability separation

The scheduler now owns two mask pairs:

```text
combined read/write masks
    generated columns + supplemental lookup declarations
    → graph conflict analysis

lookup read/write masks
    AdditionalReads / AdditionalWrites only
    → SystemJobContext random lookup authorization
```

A generated writable chunk span no longer grants permission to read or write the
same component through an arbitrary entity lookup. Supplemental random writes
remain serial unless the definition explicitly promises disjoint targets.
Hostile tests attempt undeclared random reads and writes of a generated writable
component and verify rejection without state mutation.

Persistent runtimes may retain the facade supplied during initialization for
lifecycle use, but all direct public ECS runtime access is rejected while the
scheduler is executing. This includes owner-thread `Prepare` and `Complete` as
well as job execution. Scheduler internals continue to use the internal core and
issued capabilities. Hostile tests cover direct lookup, write, and main
`StructuralCommands` access from every execution phase while proving
initialization and shutdown access remain valid.

## Global partition bound

Generated and handwritten scheduled partition builders now accumulate occupied
chunks globally across the complete matched-route sequence. A partition retains
the existing 16-byte representation:

```text
start route + start physical chunk + logical chunk count + row count
```

Execution advances through intrusive occupied-chunk order and then into the next
matched route. No route list, heap allocation, or new work-item type is needed.
A 16-archetype fixture with one occupied chunk per route and an eight-partition
maximum produces exactly eight work items and visits every row once. This closes
the old capacity failure where the same fixture could produce 16 work items.

## Structural graph semantics

Structural declarations are deferred capabilities and cold route-compilation
metadata. They do not imply an in-graph component memory hazard because topology
does not change until the post barrier. Systems needing semantic ordering of
their emitted commands declare `Requires`. Otherwise registration/work-item order,
deterministic segment merge, and barrier phase order define the result. A focused
test locks two structural-only systems into the same wave.

Tag `Add<T>` already slices its serialized payload to the schema payload size.
For a storage-free tag that size is zero; a scheduler configured with zero
payload bytes successfully adds the tag and reports zero merged payload bytes.

## Longitudinal 100K parity repair

The 3.1.0 scheduler-shaped soak remains useful, but its workload is now labeled
honestly. It chooses 5K writes by scanning all 100K entities, uses fixed lookup
targets, and records 200 destroys plus 200 creates with no transitions.

The new parity profile runs the exact older contract in three worlds:

| Per tick | Work |
| --- | ---: |
| generated query passes | 4 / 400K rows |
| deterministic arbitrary checked writes | 5K |
| deterministic RNG random reads | 500 |
| destroys | 100 |
| creates | 100 |
| prepared transitions | 200 |
| total structural commands | 400 |

The 5K write target set and 500 reads use the original deterministic RNG and
selection arrays. Scheduled writes execute as a `RangeJob` through an explicitly
declared prepared lookup with disjoint targets. Structural selection, recording,
playback, and replacement initialization retain the existing public longitudinal
path so command count and physical work are identical instead of pretending
extra create-initialization commands are equivalent.

Three hashes/counter gates are reported:

- public 3.0.36-style generated-job control;
- scheduled serial;
- scheduled parallel.

All must match a normalized logical hash covering component values, enable state,
entity generations/ownership, archetype membership, lifecycle revisions,
structural results, changed-row observations, checksums, and workload counters.
Observation-version hashes are separate. They intentionally differ because
5,000 public scalar writes publish 5,000 versions while one scheduled logical
system invocation publishes one shared version. Serial and parallel scheduled
observation hashes must still match each other.

The initial 300-tick admission run measured:

| Surface | Mean | p95 | p99 | Allocation |
| --- | ---: | ---: | ---: | ---: |
| public generated-job control | 1,176.095 us | 1,205.478 | 1,214.048 | 0 B |
| scheduled serial | 1,214.002 us | 1,262.058 | 1,271.268 | 0 B |
| scheduled parallel (8 participants) | 573.363 us | 652.614 | 681.974 | 0 B |

All counters and the logical hash
`D8520A2102D53CC945889F0BEA90EF98` matched. Caller and worker allocation and
GC counts were zero.

The accepted 2,000-warmup/30,000-measured-tick run measured:

| Surface | Mean | p95 | p99 | Allocation |
| --- | ---: | ---: | ---: | ---: |
| public generated-job control | 1,188.522 us | 1,235.919 | 1,289.798 | 0 B |
| scheduled serial | 1,271.007 us | 1,322.398 | 1,376.587 | 0 B |
| scheduled parallel (8 participants) | 577.820 us | 656.644 | 744.593 | 0 B |

All counters and the final logical hash
`55107F0D2B7B1F2F76BF7E710E9C6AD5` matched. Scheduled parallel is `2.06x`
faster than the public serial control and `2.20x` faster than scheduled serial.
Scheduled serial's measured orchestration/capability cost over the control is
about `6.9%`; later API work should keep that visible rather than hiding it
inside the parallel result.

## Source-independent typed structural evidence

The scheduler benchmark now alternates homogeneous typed `Add<Scalar>` and
`Remove<Scalar>` over a resident population and validates the returned
archetype/component state. The 100K accepted control measured `156.9 ns/entity`
Add (mean/p95 `15,689/15,939 us`) and `133.3 ns/entity` Remove
(`13,332/13,964 us`) at four participants, with zero allocation. This is much
slower than prepared grouped transition paths and is a
credible future optimization target. 3.1.1 does not optimize it: source-independent
component commands still preserve scalar semantics, and a future grouped route
implementation must first prove canonical conflict/capacity behavior.

## Capacity accounting

`WorldCapacityPlanner.Plan` accepts `managedSchedulerBytes`. `WorldCapacityPlan`
now reports `ManagedSchedulerBytes` and `TotalManagedBytes` in addition to entity
metadata/native totals. The exact scheduler value is available after cold system
registration through `EcsWorld.SchedulerReservedBytes`; callers planning before
registration may provide an explicit conservative reservation.

## Compatibility and locked representation

Unchanged:

- public scheduler API and execution model;
- persistent worker count/caller participation;
- 24-byte structural command and 40-byte slot;
- 16-byte chunk partition and 8-byte query shape;
- deterministic segment merge and deferred-entity restrictions;
- generated query/chunk ABI;
- dense/exact change semantics;
- terminal fault policy;
- scalar/packed/production structural executors;
- zero warmed managed allocation.

The scheduler reservation grows by exactly two component-mask arrays: one read
and one write lookup mask per system. This is cold, fixed, and included in
`SchedulerReservedBytes`.

## Validation and artifacts

The permanent 3.1.0 baselines remain immutable historical evidence. 3.1.1 adds
new benchmark artifacts rather than overwriting them. Accepted validation:

- 218 kernel/runtime + 29 scheduler tests in both Debug and Release;
- 23 generator, 12 comparison-core, and 18 bitmap tests;
- 141 permanent kernel rows, 86 public-job rows, 314 change-decomposition rows,
  and 145 scheduler timing rows, canonical and allocation-free;
- 60,000-tick scheduler stress profile: serial mean/p95/p99
  `938.808/988.891/1,042.630 us`, parallel
  `377.664/463.376/579.174 us`, zero caller/worker allocation;
- corrected 30,000-tick parity profile above with exact logical/counter equality.
- both isolated V2/V3 process orders: 102 comparable rows, zero state or catalog
  mismatches, 10 explicit unsupported rows, and zero timed V3 allocation.

Against the immutable 3.1.0 joined baseline, serial scheduled traversal is flat
(`-0.4%` V2-first, `+0.6%` V3-first) and parallel generated traversal improves
in both orders (`-15.1%` and `-10.0%`). The parallel RangeJob control is
order-sensitive (`+2.2%` and `+6.4%`). Focused 9-warmup/31-sample reruns measured
`+2.7%` with V2 first and `+6.3%` with V3 first, so this is retained as a modest
real follow-up signal rather than dismissed as noise. It is accepted here because
the capability-safety fix is required and does not add work to RangeJob execution;
production RangeJob-heavy workloads should remeasure it before further scheduler
expansion.
Other percentage triage flags are dominated by sub-nanosecond rows or movement
in the rerun V2 control; no semantic/allocation regression is suppressed.

Accepted artifacts:

- [`_Benchmarks/Baselines/ecs-v3-3.1.1-scheduler-capability-parity-hardening-2026-08-11.json`](_Benchmarks/Baselines/ecs-v3-3.1.1-scheduler-capability-parity-hardening-2026-08-11.json)
- [`_Benchmarks/Baselines/ecs-v2-v3-3.1.1-2026-08-11.json`](_Benchmarks/Baselines/ecs-v2-v3-3.1.1-2026-08-11.json)
- [`_Benchmarks/results/ecs-v2-v3-3.1.1-v2-first/ecs-v2-v3-comparison.md`](_Benchmarks/results/ecs-v2-v3-3.1.1-v2-first/ecs-v2-v3-comparison.md)
- [`_Benchmarks/results/ecs-v2-v3-3.1.1-v3-first/ecs-v2-v3-comparison.md`](_Benchmarks/results/ecs-v2-v3-3.1.1-v3-first/ecs-v2-v3-comparison.md)

## Next direction

The next major product work should resume only after this hardening gate. The
typed source-independent Add/Remove result is now a measured optimization lead,
not authorization to add another structural strategy. Prefabs, persistence, and
lifecycle streams remain larger parity slices. Scheduler work stealing, fusion,
and general task graphs remain deferred until production-shaped evidence demands
them.
