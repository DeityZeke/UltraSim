# ECS execution and API selection

Choose work by how it is selected, not by which callback looks familiar.

```text
component topology selects rows
    -> generated chunk job

external/preselected bounded index set
    -> RangeJob

relationship/reference to a few arbitrary entities
    -> prepared ComponentLookup through SystemJobContext

entity existence or shape changes
    -> structural writer

values changed since a consumer cursor
    -> changed-row job / ChangeCursor

create/destroy/add/remove history
    -> lifecycle cursor
```

Scanning a complete entity population through checked lookup from a `RangeJob`
defeats contiguous traversal and can be orders of magnitude more expensive. A
`RangeJob` is appropriate for an already-selected combat list, spatial bucket,
external array, or other non-query index domain.

## Generated chunk jobs

Prepared generated plans bind component columns, matching archetypes, filters,
and change metadata against a sealed world. Execution receives stack-only typed
chunk views over contiguous SoA columns. Read-only and writable families cover
the supported component arities; source generation emits the repetitive public
surface without reflection or per-row interface dispatch.

Writable views conservatively publish their writable range as changed. Tracked
writable views are for dense traversal with sparse actual mutation: callers must
write through the tracked row wrapper so exact history reflects real writes.

Generated filters support required/excluded composition, enabled state, and
changed-since selection. Storage-free tags are filters/topology, not value
columns.

Direct serial execution and scheduled execution use the same prepared plans and
typed chunk ABI. A job/context is valid only for the issued callback and must not
be retained.

## Range jobs

`IRangeJob` executes a bounded `[start, start + count)` partition. The scheduler
may partition it statically across participants. Range iteration does not itself
grant ECS access. Supplemental `ComponentLookup<T>` capabilities are declared in
`SystemDefinition.AdditionalReads`/`AdditionalWrites` and exposed only through
the callback-scoped `SystemJobContext`.

Supplemental random writes are serial unless the caller supplies the strong
`PartitionDisjoint` ownership proof. That proof covers all conflicting generated
and random read/write domains at their physical writable granularity; distinct
loop indices alone are not proof.

Random enable mutation uses the safe supplemental path because different entity
bits can share one physical word. Generated chunk ownership remains the fast
non-atomic path.

## Identity and lookup

`Entity` is a world-bound generational value. An index alone is not a durable or
cross-world identity. `TryGetEntity` reconstructs a full live value only when a
job actually needs it. Prepared lookups perform checked sparse access and are not
a replacement for query traversal.

Persistent identity is separate and described in [persistence](PERSISTENCE.md).

## Barriers and visibility

Normal component writes are visible according to the current execution/order
contract. Topology does not change while a graph runs. Structural commands are
recorded into bounded lanes, merged deterministically, and applied at the
pre/post simulation barriers described in [STRUCTURAL.md](STRUCTURAL.md).

Changed values and structural lifecycle are separate observations. A change
cursor cannot infer that an entity was destroyed; a lifecycle cursor does not
carry arbitrary component values.

## Ownership and cost

- Prepared plans and lookups belong to one sealed world/schema.
- Chunk views, contexts, spans, and structural writers expire with their callback.
- Public checked operations reject stale/wrong-world identity transactionally.
- Hot generated traversal and admitted scheduled paths are allocation-free after
  warmup under their configured capacities.
- Capacity exhaustion is explicit; no unbounded fallback is substituted.
