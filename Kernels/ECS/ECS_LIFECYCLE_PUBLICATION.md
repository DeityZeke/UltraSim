# ECS lifecycle publication proof contract

Status: authoritative ECS 3.2.0 contract, accepted 2026-08-12. This document was
written before implementation and remains the observable proof boundary.

## Public model

Lifecycle is one fixed-capacity, globally ordered world timeline with many
independent value-type cursors. It reports successful runtime existence and
component-membership commits only:

```text
Created
Destroyed
ComponentAdded
ComponentRemoved
```

Creation emits only `Created`; destruction emits only `Destroyed`. A general
archetype transition emits the exact source/destination signature difference:
removed component IDs in ascending order, followed by added component IDs in
ascending order. A typed add of an already-present component is a value
replacement and emits no lifecycle record. Failed, stale, duplicate, no-op,
enable-state, value-write, and physical relocation work emits nothing.

Records contain a runtime `Entity` tombstone and, for membership events, the
schema-local `ComponentId`. They contain no physical storage, persistent,
prefab, or component-value data.

## Proof boundary

Lifecycle is an observation product of logical structural commit:

```text
immediate facade mutation
    -> checked core mutation
    -> success
    -> publish logical lifecycle fact(s)

deferred recording / worker-lane merge
    -> no publication
    -> canonical structural playback
    -> successful logical command or proven run commits
    -> publish command-order lifecycle fact(s)
```

The internal storage/migrator/entity-pool layers do not publish. Consequently
donor relocation, temporary-hole reuse, whole-store reset, packed-tail shrink,
chunk reuse, and scalar/grouped/whole-source/suffix strategy choice cannot
invent events. Optimized runs publish the same per-command logical records as
the scalar oracle after the physical run has committed.

Unexpected post-proof failure keeps the world's existing terminal-fault policy.
Facts for earlier successful commits remain; the failed mutation publishes
nothing. Lifecycle history exhaustion never fails or blocks a mutation: the
ring overwrites its oldest retained record and lagging cursors receive an
explicit `ResyncRequired` result.

## Ordering

The sequence follows actual owner-thread commit order. For a scheduled tick this
means pre-system main-arena playback, followed by deterministic graph execution
and lane merge, followed by post-system playback. Worker wall-clock completion
order is never observable. Within a transition, membership deltas follow the
stable removed-then-added component order above.

`RunManual` lane merge alone does not publish; later playback does.

## Ring and cursor proof

Capacity is explicit and defaults to zero. Zero disables both storage and
publication work except for the successful-commit null check. A provisioned
world owns one fixed `LifecycleRecord[]`; it never grows and records do not own
payloads.

The ring tracks an unsigned next sequence and retained count. With capacity
bounded well below half the sequence domain, `unchecked(next - cursor)` is the
unambiguous pending distance through wrap. A cursor is valid only for its world.
If pending is zero the result is `NoData`; if pending exceeds retained count it
is `ResyncRequired` and remains unchanged; otherwise the ring copies the
available prefix into the caller span and advances only by the delivered count.
Reset/resynchronization is an explicit new cursor capture.

Reading is owner-thread-only in 3.2.0, matching the public world facade. There
are no subscribers, producer atomics, backpressure, callbacks, or hot-path
allocation.

## Mutation-path inventory

All logical topology entry points covered by this proof are:

- immediate default/initialized create, prepared transition, and destroy;
- main structural-buffer create/destroy/transition playback;
- scheduler-lane commands after deterministic merge and actual playback;
- deferred create/target combinations;
- source-independent typed add/replacement/remove;
- scalar oracle, packed oracle, grouped transition, exact whole-source,
  exact packed-suffix, exact whole/tail destroy, and temporary-hole execution.

The optimizer-specific paths remain physical strategies beneath the same
logical publication contract.

## Fixed representation and capacity ownership

`LifecycleRecord` is 24 bytes and `LifecycleCursor` is 16 bytes. The world owns
exactly `LifecycleRecordCapacity * 24` managed payload bytes when capacity is
nonzero; the amount appears once in both the preconstruction capacity plan and
the post-Seal reserved-payload snapshot. The history also exposes retained
range, high-water, overwrite, and reservation diagnostics. Capacity sizing is a
caller/workload policy: consumers either remain inside the provisioned history
window or receive `ResyncRequired`; the kernel does not guess a subscriber list
or dynamically grow storage.

Transition membership deltas are immutable cold data derived from the registered
canonical source and destination signatures. They are computed once per prepared
route and reused by scalar and batch publication, so wide signatures do not cause
per-entity signature scans during structural playback.
