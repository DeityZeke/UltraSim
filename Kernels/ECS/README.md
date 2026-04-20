# ECS Kernel Guidance

This document is the ECS-specific engineering guide for `Kernels/ECS`.

It is derived from the old `_OLD/AGENTS.md`, but adapted for this new kernel port.

## Mission

Build an engine-agnostic ECS kernel that is:

- correct,
- explicit,
- testable,
- portable,
- scalable where it matters.

## Priority Order

Do not violate this order without a clear reason:

1. Correctness
2. Security and safety
3. Simplicity
4. Maintainability
5. Performance

Performance matters in the ECS, but only after correctness and clarity are secure.

## Kernel Rules

- Keep the kernel engine-agnostic.
- Do not couple ECS core to UI, Godot nodes, file formats, or transport details.
- Do not use process-global mutable registries in the kernel.
- Prefer sealed schema/runtime objects over ambient static state.
- Keep public mutation APIs explicit.
- Do not expose public write paths that can bypass dirty tracking.
- If ordering is required, use `Stage` or explicit dependencies, not hidden heuristics.
- Do not carry dead API forward.

## Architecture Guidance

- Prefer explicit bootstrap and ownership.
- Keep hot-path structures array-based and allocation-aware.
- Avoid speculative abstractions.
- Minimize public surface area.
- Keep persistence and prefabs outside the core until needed, but preserve clean seams.
- Keep framework and IO concerns at the edges.

## Port-Specific Rules

- Do not blindly copy `_OLD/UltraSim/ECS`.
- Preserve proven runtime behavior where it is still sound.
- Intentionally change architecture where the old design had debt:
  - global static registries,
  - dead scheduler priority,
  - implicit mutable write paths,
  - tightly coupled second-wave subsystems.
- Make structural progress in small testable slices.

## Performance Guidance

- Default to clear code first.
- Treat schema lookup, archetype iteration, structural apply, dirty tracking, and scheduler execution as likely hot paths.
- Do not introduce heavy immutable collection libraries on the hot path.
- Use immutable snapshots built from arrays and tables, not allocation-heavy immutable containers.
- When a low-level optimization makes code harder to read, justify it with actual need.

## Testing Guidance

- Add executable coverage as the kernel grows.
- Test isolation explicitly: schemas and worlds must not leak state across instances.
- Test serial correctness first, then parallel parity.
- Every new public mutation surface should have a test proving dirty tracking cannot be bypassed.

## Scope Guidance

- Make the smallest useful kernel change that keeps the port moving.
- Do not drag prefabs or persistence into the core early.
- Add extension seams before subsystem imports.
- Keep scaffolding honest: placeholders are acceptable, fake completeness is not.

## Current Port Position

The kernel is now in Phase 1E validation and hardening of the port plan in `_TMP/ECS_PORT_IMPLEMENTATION_PLAN.md`.

Implemented so far:

- sealed schema/bootstrap flow with no process-global registries,
- entity/archetype storage bound to schema-owned column factories,
- explicit dirty-aware mutation paths through `World` and the structural queue,
- explicit patch/update mutation path without public mutable refs,
- explicit world lifecycle with seal/initialize/run/shutdown state,
- batched structural enqueue APIs for create-default, destroy, add, and remove,
- mixed structural barriers keep deterministic phase order: destroy, create-default, remove, add,
- scheduler primitives with `Requires`, stage batching, manual systems, execution modes, and phase staggering,
- shared deferred structural command capture is safe under parallel batch execution,
- read-only query and chunk iteration surfaces,
- explicit in-query patch surfaces for dirty-aware write-heavy systems without public mutable refs,
- read-only archetype traversal and persistence adapter hooks,
- lightweight persistence snapshot views in `Extensions/Persistence`,
- serial vs parallel parity coverage for explicit writes and deferred structural commands,
- randomized model-based mutation fuzz coverage,
- multi-tick serial vs parallel determinism soak coverage,
- opt-in tick profiling snapshots for pre-systems, systems, and post-systems phases,
- versioned query-plan caching for read-only query, patch-query, chunk, and archetype traversal entry points,
- targeted benchmark coverage against the old ECS, including read-only vs write-heavy scheduler scenarios,
- prefab/persistence seams held for second-wave import,
- executable incremental tests.

Still intentionally deferred:

- prefab runtime port,
- persistence/runtime save-load port,
- broader integration helpers and higher-level tooling.
