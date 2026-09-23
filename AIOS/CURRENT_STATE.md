# UltraSim AIOS Current State

**Updated:** 2026-09-23

This is a compact design-memory snapshot, not a replacement for implementation plans or source truth.

## Current canonical gameplay/system design references

| Area | Status | Reference |
|---|---|---|
| Dungeon Lab / Dungeon Core | CANONICAL / implementation-ready plan | [Dungeon Core plan](../Docs/Plans/Content/UltraSim_Dungeon_Lab_and_Dungeon_Core_Plan_FINAL.md) |
| Items / crafting language | CANONICAL design language | [Item/Crafting reference](../Docs/Plans/Content/UltraSim_Item_Crafting_Descriptor_Language.md) |
| Stats / skills | CANONICAL design shape | [Competency Graph reference](../Docs/Plans/Content/UltraSim_Stats_Skills_Competency_Graph_Reference.md) |
| Resource pools | CANONICAL design model | [Resource Pools reference](../Docs/Plans/Content/UltraSim_Resource_Pools_Reference.md) |
| Sim reasoning / AI | CANONICAL conceptual baseline | [Sim Core reference](../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md) |
| Simulation time / resolution / control | CANONICAL conceptual model | [Simulation Time / Resolution / Control](canonical/simulation-time-resolution-and-control.md) |
| Art / lighting / Cabin visual benchmark | CANONICAL visual direction + selected milestone | [Visual Art / Lighting / Cabin Benchmark](canonical/visual-art-lighting-and-cabin-benchmark.md) |
| World history / knowledge | CANONICAL conceptual baseline | [History/Knowledge reference](../Docs/Plans/Content/UltraSim_World_History_Knowledge_Pools_Reference_v2.md) |
| Magic | CANONICAL baseline with later refinements captured in AIOS | [Magic v2](../Docs/Plans/Content/UltraSim_Magic_Elements_Reification_Semantics_v2.md), [follow-up](topics/magic-post-v2-refinements.md) |

## Recently explored but not yet promoted into a large canonical plan

- Materials and resource processing: [materials-and-processing.md](topics/materials-and-processing.md)
- Body Mail hybrid armor form: [armor-body-mail.md](topics/armor-body-mail.md)
- Campaign stakes / optional main historical crisis: [campaign-stakes.md](topics/campaign-stakes.md)
- Aspirations, wants/desires, predispositions and gifted/deficient traits: [actor-motivation-and-traits.md](topics/actor-motivation-and-traits.md)

## Design bias

Across current systems, prefer:

- bounded/deterministic mechanisms;
- sideways progression over automatic tier escalation;
- shared generic infrastructure with category-specific semantics;
- simulation consequences over arbitrary status labels where practical;
- pooled/shared history over duplicated actor memory;
- persistent intent and bounded reconsideration rather than constant global replanning;
- data-driven extension points rather than exhaustive content taxonomies.
