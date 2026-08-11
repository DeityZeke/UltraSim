# ECS Next Structural Playback

Status: authoritative ECS 3.0.34 structural-playback architecture as of 2026-08-10.

The project version convention is defined in
[`ECS_VERSIONING.md`](ECS_VERSIONING.md). Historical `V31` means `3.0.31`.

This document describes the current runtime. Version-by-version measurements and
failed experiments belong in [`_Benchmarks/README.md`](_Benchmarks/README.md),
not in the runtime mental model.

## Purpose

The structural barrier converts a bounded, record-ordered command stream into the
least physical work that preserves the documented semantics. It is not a general
event bus and does not reduce commands to an unordered final state.

The machine remains:

```text
record bounded commands
        ↓
walk deterministic phase chains
        ↓
analyze one physical strategy per run
        ↓
reserve / initialize / transfer / remove / compact
        ↓
publish entity identity, location, and result counters
        ↓
leave every query-visible store dense
```

## Stable semantics

Playback order is:

1. live-entity destroys;
2. default creates;
3. destroys targeting same-barrier create tokens;
4. pure remove transitions;
5. add/replacement transitions.

Record order remains stable inside each phase. Duplicate, stale, wrong-source,
and capacity-failed commands are rejected and counted. Unexpected invariant
failure faults the world. Commands cannot be emitted during playback. No query or
public lookup can observe a temporary hole.

These semantics are protected by the scalar reference executor, randomized
canonical differential tests, focused strategy tests, and the V2/V3 canonical
comparison adapter. Optimized physical work may reorder memory operations only
after proving the same observable result.

## Executor modes

There are exactly three modes:

| Mode | Purpose | May use temporary holes |
| --- | --- | --- |
| Production | Shipping phase-indexed executor and adaptive physical strategies | Yes |
| Packed oracle | Same phase-indexed executor with barrier-local row reuse disabled | No |
| Scalar oracle | Five-pass semantic reference used by tests and benchmarks | No |

The oracles are controls, not alternative public APIs. Adding another production
path requires a material measured win, canonical equivalence, zero warmed managed
allocation, bounded memory, and a simpler explanation than extending an existing
primitive.

## Production decision table

| Work shape | Selected physical strategy | Reason |
| --- | --- | --- |
| Exact ordered full archetype destroy, at least 32 commands | Validate/reset occupied store and retire slots in record order | Avoid per-row swap removal |
| Exact ordered packed suffix destroy, at least 32 commands | Validate/shrink the physical tail and retire slots in record order | No surviving donor must move |
| Balanced same-archetype destroy/create, row width at least 64 bytes | Retire into barrier-local holes, initialize creates into those rows, finalize leftovers | Avoid wide donor relocation without exposing sparse queries |
| Other live destroys | Scalar packed removal with trusted donor/location commit | Preserves record-order conflict behavior and avoids speculative planner cost |
| Exact ordered transition evacuating a complete source, more than 256 commands, with full destination capacity | Validate the entire route once, stream trusted destination publication in 256-row blocks, reset the source once | Preserve bounded scratch and record order while eliminating per-block source removal/compaction and redundant liveness/location validation |
| Same prepared transition run, at least 8 commands | 256-entity bounded grouped execution; contiguous prepared prefixes use trusted location publication and donor repair | Amortize route, destination, copy, compaction, and already-proven EntityPool validation work |
| Short or high-entropy transition run | Scalar prepared transition | Grouping overhead is not justified |

Exact whole/tail destruction is analyzed and attempted before temporary-hole
demand is prepared. If exact validation fails without mutation, execution falls
back to the general path and may then select row reuse. There is no independent
“V28 path,” “V29 path,” or “V30 path” in the conceptual model; those names remain
historical benchmark milestones.

## Whole-source validate-once boundary

The exact whole-source coordinator is the only caller of the trusted block
migration primitive. Before the first destination row is reserved it proves the
complete homogeneous route, every entity's live generation and source location,
strict physical row order, exact source population, and capacity for the entire
destination. Structural playback is single-owner, so those facts cannot change
between preflight and commit.

Each bounded block therefore performs only the source-location table read needed
to assemble copy rows, then publishes replacement locations without repeating
public stale-handle, generation, and expected-location rejection passes. Debug
builds assert the full source and publication proof. Release builds trust the
coordinator.

After preflight, reservation, copying, and publication are commit work. Any
unexpected failure is an invariant violation: destination reservation is rolled
back where still possible, command playback is consumed, and the public world is
terminally faulted. It never falls back after partial mutation.

## Prepared-prefix validate-once boundary

ECS 3.0.34 extends the same commit rule inside the existing ordinary grouped
strategy, without adding a fourth executor mode or another structural strategy.
`TryPrepareContiguousSourcePrefix` remains the proof boundary: it validates every
entity's current identity, generation, source archetype, and ordered physical
row before prepared execution begins.

The non-planned prepared path must still reread each current row. An earlier
authoritative packed removal can relocate a later target into its donor hole, so
the preflight row may legitimately be stale. The reread trusts the already-proven
identity and source membership, Debug reasserts those facts, publication commits
the replacement directly, `Store.TryRemove` remains authoritative, and successful
donor repair commits the store-authored relocation directly.

The planned-compaction path retains its original preflight rows until the store
commits. It therefore publishes the prepared replacement batch and subsequent
store-authored donor relocations without repeating the complete EntityPool
liveness/generation/expected-location scans in Release. Debug validates the
entire publication and relocation set before writing it.

Immediate scalar migration and noncontiguous grouped fallback still use the
ordinary checked `Try*` EntityPool APIs. Temporary same-process controls found
only a borderline, inconsistent scalar movement and no scattered-path benefit,
so those experimental trusted calls were removed. Expected stale, duplicate,
wrong-source, and capacity rejection is consequently unchanged everywhere; the
trusted helpers are commit-only operations reachable from prepared coordinators
after complete proof.

## Barrier-local row market

Row reuse is conservative by design:

- only archetypes touched by recorded default creates are considered;
- the fixed row width must meet the benchmark-owned 64-byte threshold;
- create demand must cover every currently resolvable live destroy in that store;
- exact whole/tail removal takes precedence;
- a failed or unconsumed row is packed before later structural phases;
- every published replacement receives default component bytes, default enable
  state, fresh exact-row history, identity, and location.

The command result reports holes created, reused, compacted, donor relocations
during finalization, and fixed-row bytes represented by successful reuse. These
are topology facts, not claims that every reused byte would otherwise have been
copied.

Persistent sparse storage remains rejected for hot archetypes. A future cold or
`ChurnOptimized` policy must independently prove that saved structural relocation
repays fragmented traversal, retained chunks, and more complex capacity rules.

## Storage lifecycle boundary

Structural playback does not manipulate enable masks or change-history arrays
directly. Store-level row operations own those details:

- reserve/initialize a row before publication;
- transfer retained row state;
- remove or compact a row and report donor relocation;
- clear/reset occupied ranges;
- finalize a temporary hole.

This is the boundary that prevents exact change tracking from becoming a separate
branch in every structural optimizer. New structural work should compose these
row lifecycle operations rather than learn the representation of dense or exact
history.

## Bounded memory

Each command slot reserves:

- 24 bytes for the unmanaged command record;
- 12 bytes for a create result/entity scratch slot;
- 4 bytes for its stable phase or temporary-hole link.

Additional fixed archetype-indexed arrays hold hole heads, create demand,
selection state, and the touched-archetype list. `ReservedBytes` accounts for all
of these payloads. Playback uses bounded stack blocks for transition execution and
does not allocate managed memory after warmup.

## Consolidation rules

The 3.0.31 consolidation checkpoint intentionally did not redesign the cold capacity planner,
change-tracking representation, generated query families, or public facade API. The
audit found no evidence that those changes would simplify the hot structural
machine today.

Future work follows these rules:

- count runtime concepts, not files;
- prefer cold generation/planning over hot abstraction;
- add a strategy only when an existing lifecycle primitive cannot express it;
- investigate any stable regression above 2–3%; explain or reject changes above
  5% unless stronger semantics justify them;
- preserve scalar and packed controls until the optimized executor has extensive
  randomized and adversarial coverage;
- keep generated query-family growth frozen until a job-shaped generator design
  can replace combinatorial manual families.

## Required tests

Every strategy change must frequently run:

- scalar/production canonical differential command streams;
- exact whole/tail candidate success and failed-candidate fallback;
- exact-plan precedence over the row market;
- duplicate, stale, foreign, wrong-source, and same-entity conflicts;
- same-barrier deferred token resolution;
- destination and entity-capacity exhaustion;
- partial row reuse and mandatory finalization;
- donor location, component bytes, enable state, dense history, and exact history;
- Debug proof-boundary rejection for trusted identity, generation, source row,
  replacement row, and donor-location violations;
- Native/Pinned parity where the store primitive supports both;
- generation wrap, retired slots, epoch wrap, and token ABA protection;
- warmed zero-managed-allocation loops;
- canonical V2/V3 final-state comparison wherever contracts overlap.

The balanced-wide comparison added at 3.0.31 found that V2 deferred default creation
can expose stale donor-row payload after sparse batch destruction. Its performance
row is therefore explicitly provisional: survivor bytes, liveness, component
presence, and replacement lifecycle are canonical; replacement payload is
normalized for the cross-generation comparison, while V3 separately asserts
true default initialization. Tests-as-we-go may continue to reveal contract
differences and should narrow or redirect optimization work rather than hiding
them.

## Remaining structural work

Natural future work includes per-worker fixed command arenas, deterministic worker
merge, separated-run route grouping, typed add payloads, prefab expansion,
lifecycle publication, and tick budgets. Exact whole-source transition routes now
plan beyond the 256-command execution block without enlarging scratch. General
route-level source planning, exact suffix transitions, and source-chunk density
strategies remain experiments until the shared comparison shows a stable win
across entropy, locality, width, conflict, history, and evacuation sweeps.
