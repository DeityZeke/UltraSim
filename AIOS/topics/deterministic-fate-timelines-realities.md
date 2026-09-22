# Deterministic Fate, Timelines, and Parallel Realities

**Status:** CANONICAL conceptual model

Related:
- [Project Shape](project-shape-and-player-experience.md)
- [Sim Core](../../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md)
- [World History / Knowledge](../../Docs/Plans/Content/UltraSim_World_History_Knowledge_Pools_Reference_v2.md)
- [Persistence](../../Docs/PERSISTENCE.md)
- [Simulation Time / Resolution / Control](../canonical/simulation-time-resolution-and-control.md)

## Deterministic Fate invariant

UltraSim's single-player world should be fixed/repeatable in the absence of changed authoritative inputs.

```text
Same world seed
+ same simulation/content definitions
+ same initial/anchor state
+ same ordered external inputs/interventions
=
same authoritative history
```

If a baker makes a particular cake on Day 32 in the no-interference history, the same seed/rules should lead to that same result again unless something causally changes the circumstances.

This is stronger than seeded world generation: **the simulation history itself is repeatable.**

## Authoritative randomness

Authoritative randomness should be semantically/addressedly deterministic rather than one giant mutable RNG stream.

Conceptually:

```text
RandomResult =
Hash(
    WorldSeed,
    Domain,
    SemanticIdentity,
    Occurrence,
    Purpose
)
```

Consequences:

- unrelated logging/diagnostics/random calls cannot scramble Fate;
- reloading does not reroll an existing creature's possessions or a specific craft attempt;
- persistent creation identity must be stable;
- deterministic tie-breaking/order is required where authoritative order matters;
- parallel execution must not alter semantic outcomes.

To obtain a different result, change meaningful circumstances rather than reload until the RNG changes.

## Resolution invariance and causal compression

Deterministic Fate also enables safe temporal/causal aggregation.

Given the same authoritative starting state, rules, ordered external inputs, and semantic Fate, a safely collapsed interaction should reach the same meaningful authoritative result as fine-grained resolution.

If an interaction is causally sealed, the simulator may replace its internal deterministic trace with an equivalent authoritative state transition.

If an outside actor/event could observe or alter the interaction, aggregation may advance only to that next causal boundary before state is materialized/escalated.

This means authoritative randomness must belong to semantic occurrences rather than to the number of ECS ticks or resolver calls that happened to execute.

See [Simulation Time / Resolution / Control](../canonical/simulation-time-resolution-and-control.md).

## Canonical Fate

**Canonical Fate** is the deterministic history produced by the default/current seed when no player intervention changes it.

Fate is not necessarily a hand-authored schedule. Ordinary simulation systems deterministically produce it.

> **Fate is what would have happened. History is what did.**

The player can learn recurring history across runs and exploit that knowledge in a Majora's Mask-like way, while intervention can create systemic downstream divergence.

## Anchor

Normal play may begin from an **Anchor** after prior world history has already been generated.

Earlier history can remain authoritative historical truth without requiring the timeline mechanic to traverse before the Anchor.

Exact Anchor date/era remains content-level policy.

## Reality and Timeline

A **Reality** is one independent save/world history.

A Reality can contain multiple live **Timelines**:

```text
Reality / Save
    ├── Prime Timeline
    ├── Branch A
    │    ├── A1
    │    └── A2
    └── Branch B
```

The first lived history in that save is its **Prime Timeline**.

Prime is protected and should not be silently discarded.

## Delta/replay representation

Because history is deterministic, an alternate Timeline can initially be represented as:

```text
Parent history
+ fork time
+ changed external inputs / interventions
+ optional materialized checkpoints
```

Downstream consequences can be regenerated from those causes.

Snapshots/checkpoints are loading/performance tools, not the conceptual definition of the branch.

## Divergence and capacity

A branch has **historical divergence** from its parent/reference history.

Small causal changes can remain cheap to maintain.

Large cascading differences become increasingly expensive to keep linked/materialized inside the current Reality.

Gameplay-facing divergence/stabilization cost should be based on deterministic historical semantics, not literal serialized file size.

Potential divergence inputs include changed:

- births/deaths;
- persistent identities;
- major events;
- relationships;
- ownership;
- factions/settlements;
- downstream historical significance.

Exact scoring remains open.

## Full Stabilization / Parallel Reality

A sufficiently divergent Timeline can be **fully stabilized** as an independent **Parallel Reality**.

```text
Timeline branch
    ↓
Full Stabilization
    ↓
materialize independent authoritative state/save
    ↓
branch history becomes that Reality's established historical record
    ↓
branch continuation becomes the new Reality's Prime Timeline
```

The new Reality:

- no longer consumes the parent Reality's live Timeline capacity;
- can create its own new branches;
- retains lightweight lineage/provenance metadata if useful;
- must not require the parent save to continue functioning.

Moving between Timelines inside one Reality is a timeline-traversal mechanic.

Moving between independent Realities is effectively loading another save/world.

## Local Fate after stabilization

The original world retains its original Canonical Fate.

A fully stabilized Reality has a new deterministic no-further-interference future from its stabilized state: its **local Fate**.

## Persistent multiplayer

Persistent multiplayer should use one shared irreversible history.

Timeline rewind/branch manipulation is a single-player mechanic unless a future design explicitly proves otherwise.

## Thematic rule

> **Timelines are possibilities you are still holding open. Realities are histories you have committed enough power to make independent.**

This allows the technical cost of heavily divergent branch retention to become part of the fiction/mechanic instead of an arbitrary save-slot limit.

## Open engineering questions

- exact divergence metric;
- checkpoint/materialization cadence;
- branch replay/cache policy;
- definition/rules version compatibility and migration;
- maximum Timeline capacity;
- stabilization resource/gameplay terminology;
- branch/collapse/archive UI;
- exact historical lineage metadata.
