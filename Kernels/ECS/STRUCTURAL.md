# Structural command and playback contract

Structural mutation changes entity existence or component membership. It is
immediate only through explicit owner-thread APIs at legal boundaries; scheduled
systems record commands into bounded writers and publish them at a barrier.

## Stable semantics

- Command record size is 24 bytes; a complete command slot is 40 bytes.
- Recording order is deterministic within a producer and logical segment order
  is deterministic across scheduler participants.
- `DeferredEntity` is a recording-epoch/producer-local token, not an `Entity`.
- Creates, destroys, transitions, typed add/remove, and replacement payloads use
  fixed command/payload capacities.
- Expected stale/wrong-world/duplicate/capacity cases retain scalar-equivalent
  rejection and prefix behavior.
- Public topology is dense and query-visible only after playback completes.

Playback phases preserve the command language's deterministic semantics. A
source-independent typed add/remove command has a discriminated route key; its
numeric argument can never alias a registered transition ID.

## Physical planner

The production planner recognizes exact topology before applying repeated local
mutation. These are physical strategies inside one executor, not public modes:

```text
destroy
    exact whole source       -> clear once
    exact packed tail        -> shrink once
    balanced same-barrier    -> temporarily reuse destinations
    general                  -> checked packed removal

transition
    exact whole source       -> copy/publish blocks, clear source once
    exact packed suffix      -> copy/publish blocks, shrink source once
    grouped prepared route   -> planned removal/compaction
    scalar fallback          -> ordinary checked migration
```

Whole-source and packed-suffix candidates validate the entire homogeneous route
and destination capacity before mutation. Destination placement remains command
record order even when suffix proof walks the physical donor tail in reverse.
Execution streams through bounded 256-row scratch blocks. A failed candidate
does zero mutation and falls back; an unexpected failure after trusted commit
begins is a terminal invariant failure.

Prepared migration validates expected rejection once, then uses narrowly scoped
trusted EntityPool publication/relocation commits in release builds. Debug keeps
the complete invariant assertions. Checked public/scalar APIs are not weakened.

## Barrier-local row reuse

Arbitrary removals may create temporary holes only when same-barrier demand can
consume them. Creates/transitions reuse those rows before finalization. Remaining
holes are packed before queries regain visibility. This is a short-lived row
allocation cache—not persistent sparse ECS storage.

Whole/tail simplifications take precedence. Query-visible stores remain packed;
there are no persistent occupancy bitmaps. The planner tracks holes created,
reused, relocations avoided, and final compaction for measurement.

## Row lifecycle and change state

Storage-owned row primitives initialize publication defaults, transfer retained
component values/enable state/dense and exact change history, invalidate removed
ranges, and repair donor locations. Structural strategies do not invent parallel
change representations.

Successful creates, destroys, and component membership changes publish lifecycle
records from the accepted authoritative playback point. Failed commands publish
nothing. See [LIFECYCLE.md](LIFECYCLE.md).

Compiled prefab creation uses the same create/default/payload and lifecycle
pipeline; content authoring does not introduce another structural executor.

## Oracles and invariants

Production playback is checked against packed and scalar oracles. Differential
tests cover stale/duplicate/wrong-source/mixed/scattered routes, capacity
fallback, enable state, values, dense/exact history, donor repair, retained-empty
chunk reuse, deferred tokens, Native/Pinned parity, lifecycle order, allocation,
and fault injection.

New optimizations must fit an existing proof/physical primitive or justify their
added conceptual state with measured material benefit.
