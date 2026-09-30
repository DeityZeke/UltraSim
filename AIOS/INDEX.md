# UltraSim AIOS Index

Start here. This file is intentionally small.

> **Before contributing to AIOS or promoting design decisions, read [AGENT_GUIDE.md](AGENT_GUIDE.md). Discussion is not a decision.**

## AIOS usage

- [Agent / contribution guide](AGENT_GUIDE.md) — required rules for reading, capturing, status, and promotion.
- [AIOS overview](README.md)
- [Current state snapshot](CURRENT_STATE.md)

## Repository documentation map

- [Repository documentation map](../Docs/README.md) — existing current documentation authority/map.
- [Architecture](../Docs/ARCHITECTURE.md)
- [Game composition](../Docs/GAME.md)
- [Persistence](../Docs/PERSISTENCE.md)
- [Runtime](../Docs/RUNTIME.md)
- [UI](../Docs/UI.md)
- [Full current Markdown inventory](REPOSITORY_DOCS.md)

## Cross-system canonical design references

- [Simulation Time / Resolution / Control Authority](canonical/simulation-time-resolution-and-control.md) — independent world time/playback, deterministic causal aggregation, Immediate/Near/Far resolution, player autonomy/direct/command/governance control, and possession/controller binding.
- [Visual Art / Lighting / Cabin Benchmark](canonical/visual-art-lighting-and-cabin-benchmark.md) — canonical art/material direction, environment/lighting resolver seam, two-room Cabin visual benchmark, and staged direct-light/probe/opening tests.

## Current gameplay/content references

- [Dungeon Lab / Dungeon Core](../Docs/Plans/Content/UltraSim_Dungeon_Lab_and_Dungeon_Core_Plan_FINAL.md)
- [Mine / Subterranean Excavation](../Docs/Plans/MINE.md)
- [Items, Crafting, and Descriptor Language](../Docs/Plans/Content/UltraSim_Item_Crafting_Descriptor_Language.md)
- [Magic, Elements, Reification, and Traditions](../Docs/Plans/Content/UltraSim_Magic_Elements_Reification_Semantics_v2.md)
- [Resource Pools](../Docs/Plans/Content/UltraSim_Resource_Pools_Reference.md)
- [Sim Core Reasoning / Decision Making](../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md)
- [Stats / Skills / Competency Graph](../Docs/Plans/Content/UltraSim_Stats_Skills_Competency_Graph_Reference.md)
- [World History / Knowledge Pools](../Docs/Plans/Content/UltraSim_World_History_Knowledge_Pools_Reference_v2.md)

## Major implementation plans

- [Client](../Docs/Plans/CLIENT.md)
- [Inventory](../Docs/Plans/INVENTORY.md)
- [Movement / Camera / Control](../Docs/Plans/MOVEMENT.md)
- [Network](../Docs/Plans/NETWORK.md)
- [Physics](../Docs/Plans/PHYSICS.md)
- [Terrain / World](../Docs/Plans/TERRAIN.md)
- [Asset Streaming](../Docs/Plans/ASSET_STREAMING.md)
- [Chat](../Docs/Plans/CHAT.md)
- [Generalization](../Docs/Plans/GENERALIZATION_PLAN.md)

## Durable side-topic registry

- [TOPICS.md](TOPICS.md) — nearly every project-relevant topic discussed or discovered.
- [Framework Runtime Packages / Services / Object Extensions / ECS boundary](topics/framework-runtime-packages-services-extensions.md)
- [Framework Persistence / Control Plane publication](topics/framework-persistence-and-control-plane.md)
- [Godot / .NET native shutdown lifetime assurance](topics/godot-dotnet-native-shutdown-lifetime.md)
- [Definition / presentation / asset independence](topics/definition-presentation-asset-independence.md)
- [Inventory viewer profiles / cached derived portraits](topics/inventory-viewer-profiles-and-derived-portraits.md)
- [Shared Inventory presentation world / impostor hybrid](topics/inventory-shared-presentation-world.md)
- [Server Operator Product](../Server/Operator/README.md)
- [Audio Kernel + Sound Engine](../_Benchmarks/Baselines/framework-p8-audio-kernel-sound-engine-2026-09-30.md)
- [Renderer impostor LOD / far-field cards](topics/renderer-impostor-lod.md)
- [Materials and processing](topics/materials-and-processing.md)
- [UI nine-slice art / frame families](topics/ui-nine-slice-art-and-frame-families.md)
- [Body Mail / hybrid armor](topics/armor-body-mail.md)
- [Campaign stakes / long simulation](topics/campaign-stakes.md)
- [Actor motivations / aspirations / predispositions](topics/actor-motivation-and-traits.md)
- [Post-v2 Wizardry history / substrate manipulation](topics/magic-post-v2-refinements.md)
- [Project shape / player experience](topics/project-shape-and-player-experience.md)
- [Deterministic Fate / timelines / Parallel Realities](topics/deterministic-fate-timelines-realities.md)
- [Social conversation / claims / overhearing](topics/social-conversation-and-information-reveal.md)
- [Socketables / gems / Augment crafting](topics/socketables-gems-and-augments.md)

## Open / parked

- [Parked ideas](open/PARKED_IDEAS.md)
- [Unresolved questions](open/UNRESOLVED_QUESTIONS.md)

## Evidence and historical reference

- [Benchmark methodology](../_Benchmarks/README.md)
- [Accepted benchmark artifacts](../_Benchmarks/Baselines/README.md)
- [Archived prior versions](../_Archive/README.md)

## Memory maintenance rule

When a discussion creates a project-relevant idea:

1. add or update its entry in `TOPICS.md`;
2. capture useful detail in a focused topic file if no suitable canonical document exists;
3. link to the existing canonical document when one does exist;
4. promote settled material rather than duplicating it across multiple sources;
5. mark superseded material instead of silently losing it.
