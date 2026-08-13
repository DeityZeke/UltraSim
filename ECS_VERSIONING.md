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
The fixed-capacity lifecycle-publication timeline, narrow public generated-query
filter bridge, and first real Slice 0 lifecycle integration form the next coherent
capability milestone, so the accepted checkpoint is `3.2.0`.
The cold compiled-content authoring facade, immutable numeric prefab catalog,
and ordinary immediate/deferred ECS instantiation path form the next capability
milestone, accepted as `3.3.0`.
The generated, whole-world, durable-identity persistence participant; bounded
transactional staging restore; persistent system-state framing; and crash-durable
multi-participant save-set coordinator form the next capability milestone,
accepted as `3.4.0`.
The adversarial persistence correctness pass is accepted as `3.4.1`. It retains
the 3.4 architecture while making publication attempts terminal before host code,
bounding actual array-backed preflight ownership, recovering safely from malformed
`CURRENT`, serializing cross-process writers, enforcing forward-only generations,
validating participant/snapshot identities, tightening codec/catalog/container
canonicality, and assigning durable IDs at successful entity creation rather than
during a fallible capture.

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

ECS 3.2.0 adds one explicitly provisioned, globally ordered lifecycle history
with independent value cursors and explicit lag/resynchronization semantics.
Successful create, destroy, and component-membership commits publish runtime
facts; value changes, enable state, rejected commands, and physical row movement
do not. The same checkpoint exposes immutable required/excluded component filters
through every public generated job family without adding filter terms to scheduler
access masks or hot per-row branches. Simulation Slice 0 consumes both features,
replaces its four data-bearing tier selectors with storage-free tags, and validates
independent presentation-, replication-, and persistence-cadence lifecycle readers.

ECS 3.3.0 adds RunUO-shaped `BaseClass`/Object Script authoring that exists only
before `Seal`. Explicit registration or direct-DOD definitions compile into one
world-bound immutable catalog: sorted stable `PrefabKey` metadata, 12-byte dense
`PrefabHandle` values, 32-byte entries, 12-byte bound default descriptors, and
one default byte blob. Canonical final component signatures share ordinary ECS
archetypes irrespective of script class, key, display metadata, or default
values. Immediate and scheduler/deferred instantiation use the ordinary entity
allocation, structural barrier, change-history, and lifecycle machinery; one
prefab spawn is one command and one `Created` fact, never a component-added
storm. No authoring class, reflection, callback, or runtime inheritance graph is
consulted during spawn or simulation.

ECS 3.4.0 adds optional 64-bit world-local durable identity backed by a fixed
side table, 32-bit snapshot-local reference ordinals, complete runtime-shape
records distinct from authoritative payload columns, generated canonical
little-endian codecs, direct old-version readers, aliases/drops, explicit N:M
shape migration, derived reconstruction, enable bitplanes, stable prefab-key
provenance, and framed opt-in system state. Restore validates hostile bytes and
capacity before constructing a new world, allocates every identity before
reference fixup, hydrates without change/lifecycle publication, and returns a
complete staging generation. The engine-neutral server coordinator captures all
participants at one host-owned logical epoch and uses checksummed generation
directories, durable flushes, atomic `CURRENT` publication, and
Prepare/Resolve/Validate/single-Commit load semantics. Incremental snapshots,
merge/append restore, compression, and a V2 binary importer remain future work.

ECS 3.4.1 is a correctness/hardening revision, not a new persistence milestone.
The 3.4.0 artifacts remain immutable historical baselines. The active container
is explicitly array-backed and capped at `Array.MaxLength`; multi-gigabyte or
streaming snapshots require a future segmented/streaming format rather than an
unhonored configuration value. Compatible save-set recovery skips only recognized
corruption/compatibility failures and never hides arbitrary implementation,
resource, or transient I/O exceptions behind an older generation.

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
