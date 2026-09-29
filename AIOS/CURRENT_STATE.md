# UltraSim AIOS Current State

**Updated:** 2026-09-29

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

## Current platform / authoring architecture

| Area | Status | Reference |
|---|---|---|
| Framework Control Plane | CANONICAL implementation / P1 CLOSED | [Control Plane README](../Framework/ControlPlane/README.md) |
| Framework Runtime Host + C# server authoring | CANONICAL implementation / P2 CLOSED | [Runtime Host README](../Framework/RuntimeHost/README.md), [Server Authoring README](../Server/Authoring/README.md), [AIOS synthesis](topics/framework-runtime-packages-services-extensions.md) |
| Framework save-set persistence | CANONICAL implementation | [Framework Persistence README](../Framework/Persistence/README.md), [AIOS synthesis](topics/framework-persistence-and-control-plane.md) |
| Runtime Package / Runtime Service / Object Extension terminology | CANONICAL | [Architecture](../Docs/ARCHITECTURE.md), [AIOS synthesis](topics/framework-runtime-packages-services-extensions.md) |
| Framework adapter/runtime boundary migration | CANONICAL implementation / P3 CLOSED | [Pre-many-player roadmap](../Docs/Plans/UltraSim_Pre_Many_Person_Live_Testing_Roadmap.md), [P3 evidence](../_Benchmarks/Baselines/framework-p3-adapter-boundary-2026-09-29.md) |
| Definition ↔ presentation ↔ asset independence | CANONICAL typed representative P4 foothold; full art/portrait consumers remain later | [UI](../Docs/UI.md), [Architecture](../Docs/ARCHITECTURE.md), [AIOS synthesis](topics/definition-presentation-asset-independence.md) |
| Engine Contracts + typed definition facets | CANONICAL implementation / P4 CLOSED | [Definition README](../Framework/Definitions/README.md), [P4 evidence](../_Benchmarks/Baselines/framework-p4-typed-definitions-2026-09-29.md) |
| User/Admin/Developer control surfaces | CANONICAL representative implementation / P5 CLOSED | [P5 evidence](../_Benchmarks/Baselines/framework-p5-control-surfaces-2026-09-29.md), [UI](../Docs/UI.md) |
| Native shutdown lifetime assurance | R1 causal repair accepted; pre-P5 wrapper inventory hardened / current blocker CLOSED | [AIOS note](topics/godot-dotnet-native-shutdown-lifetime.md), [R1 evidence](../_Benchmarks/Baselines/client-native-lifetime-assurance-r1-2026-09-29.md), [follow-up](../_Benchmarks/Baselines/client-native-lifetime-pre-p5-hardening-2026-09-29.md) |

P2's closed vocabulary is precise: a **Runtime Package** is the authored/deployed composition unit; an **Engine Core** is a reusable domain capability; a **Runtime Service** is a live generation-fenced service object; an **Object Extension** (“Extension”) is sparse managed owner/global state or behavior outside ECS; and ECS remains the high-scale data-oriented substrate. Historical ACC “Module” maps to an UltraSim Object Extension, not to a Runtime Package.

P3 closed by proving the existing platform boundary was already narrow: authoritative Game/Framework/Server and engine-neutral Client code have no direct Godot dependency. Godot remains an explicit client adapter with read-only Control Plane diagnostics; dedicated ServerOnly composition remains headless. No generic `IPlatformAdapter` framework was added. P4 adds stable authored Definition identity, bounded typed facets/tags and alias remap, distinct dense handles, and an Iron Sword/compiled-package proof. P5 adds a generic owner-published F10 User surface, read-only Developer/Operator proof, neutral visual roles and a typed P4 image binding, while leaving F4 Game authority, P6 Inventory portraits and P7 remote operator routing with their proper owners. The reopened native shutdown-lifetime defect was traced to several unretired application-reachable Godot wrappers, including on the accepted P1 tree. R1 explicitly owns/retires them and passed 200/200 warm exits plus uninterrupted canonical Release on the repaired commit. P1–P5 remain closed; P6 is next and PF6 remains a later gate.

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
- data-driven extension points rather than exhaustive content taxonomies;
- Framework/OOP Extensions for sparse, rich, irregular behavior and ECS for numerous, homogeneous, hot data; do not grow Framework into a second ECS;
- rich/cold composition may allocate when useful, while steady runtime paths should avoid polling/reflection/allocation taxes that add no gameplay value;
- subsystems explicitly publish management semantics to the Control Plane; the Control Plane does not scan owners and is not a service locator;
- persistence participants own durable meaning/schema while Framework Persistence owns coordinated capture, staged restore, integrity, recovery, and atomic save-set publication;
- Game/content definitions should retain semantic/physical meaning independently from optional presentation assets; missing graphics should fall back rather than delete the object.
