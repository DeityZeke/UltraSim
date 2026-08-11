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
`3.0.34`. The next accepted checkpoint in the same milestone will be `3.0.35`;
a major capability/stability milestone advances the middle field and resets the
revision, for example `3.1.0`.

This is an engineering/checkpoint scheme, not yet a public API compatibility
promise. A revision may change internal APIs while V3 is not a replacement.
Every accepted version still requires tests, allocation checks, canonical state
comparison where contracts overlap, and benchmark evidence appropriate to the
change.

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
