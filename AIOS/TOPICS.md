# UltraSim Topic Registry

This registry is intentionally broad. A topic does not need to be large or canonical to deserve a durable pointer.

| Topic | Status | Primary reference / note | Related |
|---|---|---|---|
| Dungeon Core / Dungeon Lab | CANONICAL | [Existing plan](../Docs/Plans/Content/UltraSim_Dungeon_Lab_and_Dungeon_Core_Plan_FINAL.md) | generation, simulation harness, tooling |
| Item identity grammar | CANONICAL | [Item/Crafting reference](../Docs/Plans/Content/UltraSim_Item_Crafting_Descriptor_Language.md) | Quality, Style/Characteristic, Scale/Size, Type/Form |
| Mutable ObjectProperties vs identity | CANONICAL | [Item/Crafting reference](../Docs/Plans/Content/UltraSim_Item_Crafting_Descriptor_Language.md) | durability, enchantments, condition |
| Gem naming | CANONICAL | [Item/Crafting reference](../Docs/Plans/Content/UltraSim_Item_Crafting_Descriptor_Language.md) | size, clarity, integrity, type |
| Potion naming | CANONICAL | [Item/Crafting reference](../Docs/Plans/Content/UltraSim_Item_Crafting_Descriptor_Language.md) | compact/full names, size/refinement/delivery/resource |
| Food naming / serving scale | CANONICAL | [Item/Crafting reference](../Docs/Plans/Content/UltraSim_Item_Crafting_Descriptor_Language.md) | bite/mouthful/snack/serving/meal |
| Craft quality degradation | CANONICAL | [Item/Crafting reference](../Docs/Plans/Content/UltraSim_Item_Crafting_Descriptor_Language.md) | failure lowers quality to floor |
| Materials / material families | EXPLORING | [materials-and-processing.md](topics/materials-and-processing.md) | recipes require Metal/Wood/etc.; exact material supplies properties |
| Mithril formation / manufacture | TENTATIVE | [materials-and-processing.md](topics/materials-and-processing.md) | iron+carbon+silver + extreme magic / catalyst |
| Bloodwood | TENTATIVE | [materials-and-processing.md](topics/materials-and-processing.md) | blood-fed/battlefield wood, Blood Magic affinity |
| Hides / leather normalization | TENTATIVE | [materials-and-processing.md](topics/materials-and-processing.md) | raw species hides → functional leather classes |
| Body Mail hybrid armor | TENTATIVE | [armor-body-mail.md](topics/armor-body-mail.md) | fitted plate + chain + leather articulation |
| Stats / attributes | CANONICAL | [Stats/Skills reference](../Docs/Plans/Content/UltraSim_Stats_Skills_Competency_Graph_Reference.md) | Body/Mind/Spirit |
| Competency Graph | CANONICAL | [Stats/Skills reference](../Docs/Plans/Content/UltraSim_Stats_Skills_Competency_Graph_Reference.md) | aptitude/domain/specialty/knowledge/technique/recipe/familiarity |
| Use-based training | CANONICAL | [Stats/Skills reference](../Docs/Plans/Content/UltraSim_Stats_Skills_Competency_Graph_Reference.md) | UO/Kenshi-style |
| Resource pools | CANONICAL | [Resource Pools reference](../Docs/Plans/Content/UltraSim_Resource_Pools_Reference.md) | Current/Maximum/PendingRestore/Impairment |
| Injury / recoverable ceiling | CANONICAL baseline | [Resource Pools reference](../Docs/Plans/Content/UltraSim_Resource_Pools_Reference.md) | ordinary healing vs special treatment |
| Wizardry / reification | CANONICAL baseline | [Magic v2](../Docs/Plans/Content/UltraSim_Magic_Elements_Reification_Semantics_v2.md) | Determination, conjuration, reification |
| Ancient elemental manipulation → Wizardry | TENTATIVE / latest refinement | [magic-post-v2-refinements.md](topics/magic-post-v2-refinements.md) | illusionist origin, historical transition |
| Blood Magic as substrate manipulation | TENTATIVE / latest refinement | [magic-post-v2-refinements.md](topics/magic-post-v2-refinements.md) | real blood + shaping/control |
| Black/Dark Magic as substrate manipulation | TENTATIVE / latest refinement | [magic-post-v2-refinements.md](topics/magic-post-v2-refinements.md) | shadow, darkness, corpses, negative space |
| Druidic magic | CANONICAL baseline | [Magic v2](../Docs/Plans/Content/UltraSim_Magic_Elements_Reification_Semantics_v2.md) | manipulate/cooperate with natural systems |
| Chivalry / Piety | CANONICAL baseline | [Magic v2](../Docs/Plans/Content/UltraSim_Magic_Elements_Reification_Semantics_v2.md) | scarce lifestyle-recharged resource |
| Malefic / truly malignant magic | CANONICAL baseline | [Magic v2](../Docs/Plans/Content/UltraSim_Magic_Elements_Reification_Semantics_v2.md) | rare/forbidden dungeon-granted tradition |
| Ritual magic as method | CANONICAL baseline | [Magic v2](../Docs/Plans/Content/UltraSim_Magic_Elements_Reification_Semantics_v2.md) | tradition-specific limit breaking |
| Physical damage language | CANONICAL baseline | [Magic v2](../Docs/Plans/Content/UltraSim_Magic_Elements_Reification_Semantics_v2.md) | Slash/Pierce/Blunt; Concussive as propagation |
| Sim Core reasoning | CANONICAL | [Sim Core reference](../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md) | Need/Perception/Effect |
| Attention / focus / observation / prediction | CANONICAL | [Sim Core reference](../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md) | bounded attention |
| Desire / Friction / Feasibility / Urgency | CANONICAL | [Sim Core reference](../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md) | contextual decision scoring |
| Bipolar byte weights | CANONICAL conceptual primitive | [Sim Core reference](../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md) | magnitude byte + sign bit |
| World History pools | CANONICAL | [History reference](../Docs/Plans/Content/UltraSim_World_History_Knowledge_Pools_Reference_v2.md) | domain-owned bounded byte pools |
| Rich → Summary → Trend | CANONICAL | [History reference](../Docs/Plans/Content/UltraSim_World_History_Knowledge_Pools_Reference_v2.md) | semantic compaction |
| Episodes / multi-part crisis history | CANONICAL | [History reference](../Docs/Plans/Content/UltraSim_World_History_Knowledge_Pools_Reference_v2.md) | bounded Parts/chapters |
| Working Knowledge / Personal Memory | CANONICAL | [History reference](../Docs/Plans/Content/UltraSim_World_History_Knowledge_Pools_Reference_v2.md) | pooled truth vs actor belief |
| Campaign stake / main historical crisis | EXPLORING | [campaign-stakes.md](topics/campaign-stakes.md) | optional narrative drive + decades-long continuation |
| Aspirations / wants / desires | PARKED | [actor-motivation-and-traits.md](topics/actor-motivation-and-traits.md) | future motivation layer |
| Predispositions / gifted-deficient traits | PARKED | [actor-motivation-and-traits.md](topics/actor-motivation-and-traits.md) | persistent skill bias/penalty |
| Project-wide AI memory / AIOS | CANONICAL workflow | [README.md](README.md) | editorial repo memory and topic registry |
