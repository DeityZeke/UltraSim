# ECS Versioning

Status: active convention beginning with ECS `3.0.31` on 2026-08-10.

UltraSim's rewritten ECS is the third ECS generation. Its development versions
use three numeric fields:

```text
generation.milestone.revision
```

- `generation` identifies the ECS generation. This rewrite remains `3`.
- `milestone` advances when a coherent architectural capability becomes stable,
  such as the usable single-thread ECS slice, scheduler integration, prefab and
  persistence integration, or replacement readiness.
- `revision` advances for an accepted implementation, optimization,
  consolidation, or benchmark checkpoint inside the current milestone.

The V31 consolidation checkpoint is therefore `3.0.31`. The exact whole-source
transition work is `3.0.32`, and its validate-once trusted commit optimization is
`3.0.33`. Prepared ordinary migration's measured trusted commit optimization is
`3.0.34`. Exact route-level packed-suffix transition planning is `3.0.35`;
the public serial generated chunk-job execution surface is `3.0.36`. The bounded
generated system/job scheduler is the first coherent major capability checkpoint,
so it advances the middle field and resets the revision as `3.1.0`. The next
accepted revisions are `3.1.1`, the scheduler capability/parity hardening pass,
and `3.1.2`, the scheduler/structural proof-hardening pass.

This is an engineering/checkpoint scheme, not yet a public API compatibility
promise. A revision may change internal APIs while V3 is not a replacement.
Every accepted version still requires tests, allocation checks, canonical state
comparison where contracts overlap, and benchmark evidence appropriate to the
change.

Diagnostic benchmark additions do not advance the revision by themselves. The
3.0.35 100K mixed soak and its change-tracking cost decomposition establish
longitudinal evidence while leaving that production kernel behavior unchanged.
ECS 3.0.36 is a production API/capability checkpoint: it exposes the existing
seal-compiled generated chunk machine through the public facade, including dense
and conditional tracked writes plus the first public exact changed-row job. It is
not itself a scheduler. ECS 3.1.0 is the accepted bounded system-runtime milestone:
it compiles a staged dependency/conflict graph, runs the 3.0.36 chunk ABI and
bounded range work on persistent workers, merges fixed structural lanes in stable
logical order, owns cadence/manual/lifecycle/fault semantics, and retains zero
warmed managed allocation.

ECS 3.1.1 separates generated column access from supplemental random-lookup
capabilities, rejects retained-facade access during graph execution, makes the
partition maximum global across matching archetype routes, restores an honest
3.0.36-equivalent scheduled mixed-soak profile, measures typed source-independent
Add/Remove, and includes exact scheduler bytes in static capacity reports. It is
a hardening revision, not a second scheduler milestone. The 3.1.0 immutable
benchmark baselines remain historical controls; 3.1.1 adds new artifacts.

ECS 3.1.2 closes the independently reproduced late-ready graph conflict,
mixed typed/ordinary structural-run collision, retained-main-arena capability
escape, registration/dependency transaction leaks, RangeJob exhaustion error,
shared enable-word update loss, and unsynchronized random-write change metadata.
It defines one complete `ParallelRandomAccessPolicy`, adds scheduled tracked
handwritten 1R1W parity, corrects changed-row diagnostics and benchmark
accounting, hardens partial worker startup, and separates preconstruction memory
planning from a post-Seal reserved-payload snapshot. It is a correctness
revision over the same 3.1 scheduler and structural executor, not a new
execution architecture. The 3.1.0 and 3.1.1 artifacts remain immutable.

The post-3.1.2 consumer-readiness pass does not advance the kernel version. It
changes no runtime behavior or public signature. It adds generated and
handwritten XML guidance plus `Kernels/ECS/ECS_USAGE_GUIDE.md` so consumers can
distinguish topology-selected chunk work, bounded `RangeJob` work, sparse checked
lookup, deferred structure, and value-change observation before 3.2.0 adds
lifecycle publication.

Historical shorthand such as `V28`, `V29`, `V30`, and `V31` remains valid when
referring to immutable reports and filenames. New prose should prefer `3.0.28`
through `3.0.31`, optionally giving the old label once. Historical artifact
filenames are never renamed merely to adopt this convention because their paths
are part of the longitudinal record.

The active version is recorded once in
`Kernels/ECS/Core/EcsKernelVersion.cs` and consumed by benchmark semantic
profiles. A profile names
both the numeric checkpoint and the material behavior under test; it must change
when a new checkpoint is accepted, while immutable baselines retain their
original profile strings.
