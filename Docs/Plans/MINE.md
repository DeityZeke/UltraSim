# UltraSim Mine / Subterranean Excavation Plan

Status: Implementation-first architecture / engineering plan. Mine is the first concrete consumer of subterranean opportunity planning and excavation; Dungeon follows as the richer second consumer.

Project: UltraSim

Subsystem: Mine / Geology / Subterranean Planning / Excavation

Relationship to Dungeon: UltraSim_Dungeon_Lab_and_Dungeon_Core_Plan_FINAL.md

---

# 1. Executive Summary

UltraSim mines should be persistent world structures and economic systems, not Minecraft-style per-block digging and not static RPG caves with infinitely respawning ore nodes.

A mine is founded where geology and world-space constraints make one viable. It grows by excavating meaningful connected sections over days, weeks, or months. Completed excavation becomes persistent traversable world structure. Geological deposits exist independently of the mine, are revealed by excavation and prospecting, contain finite material, cross excavation boundaries naturally, and do not magically replenish unless a future explicit geological rule says otherwise.

Core loop:

~~~text
World geology exists
        ↓
prospecting discovers evidence
        ↓
mine founded
        ↓
generate connected underground opportunity plan
        ↓
select one legal excavation opportunity
        ↓
workers spend labor/time excavating
        ↓
section becomes persistent traversable space
        ↓
geology intersecting that section becomes exposed/known
        ↓
workers extract finite material
        ↓
material is stored / hauled / exported / consumed
        ↓
mine chooses the next opportunity from known geology + economics
        ↓
repeat
        ↓
decline / exhaustion / abandonment / repurposing
~~~

Mine deliberately precedes Dungeon implementation because it proves the difficult reusable spatial machinery with a smaller semantic surface.

Mine must prove:

- deterministic subterranean opportunity planning;
- world-space legality and CanFit-style validation;
- connected topology;
- shafts, tunnels, and vertical connectors;
- incremental excavation;
- persistent built underground structure;
- deterministic geology;
- geology exposure and finite depletion;
- bounded replanning;
- save/load and long-time advancement;
- worker/labor integration;
- hauling and output;
- lifecycle through exhaustion and abandonment;
- diagnostic/debug projection.

Dungeon later adds the harder organism-specific systems: Dungeon Core, Essence, personality/strategy, room purpose, monsters, treasure, traps, puzzles, illusions, defenders, bosses, visitor-pressure economics, and Core concealment/relocation/destruction.

Mine proves the bones. Dungeon later proves which bones are actually generic.

---

# 2. Architectural Position

## 2.1 World geology is world truth

Geology belongs to the world, not to a particular mine.

Do not generate ore because a mine happened to excavate a room.

Conceptually:

~~~text
WorldSeed
+ GeologicalRegionId
+ world position/depth
+ geology version/profile
        ↓
deterministic geological truth
~~~

A deposit may exist before a mine is founded, before a character knows about it, before the relevant region is actively loaded, and before excavation reaches it.

The implementation may lazily materialize or cache geological data, but the result must be deterministic from stable world identity.

Two mines approaching the same deposit must observe the same geology. An abandoned mine reopened decades later must observe the same remaining deposit state. A future Dungeon expanding into old workings must not regenerate different ore.

## 2.2 Mine state is human activity over world truth

Mine runtime/controller state owns what humans have done and know.

~~~text
MineRuntime
    MineId
    World/Origin
    FoundingTime

    Lifecycle

    OpportunityPlan
    BuiltTopology
    ActiveFrontiers

    ExcavationJobs / progress
    Support/maintenance state

    GeologicalKnowledge
    ExposedDeposits
    Depletion state / extraction ledger

    Storage / output summaries
    Current Goals / selected work
    History / diagnostics
~~~

The Mine runtime does not own the immutable geological field itself.

## 2.3 Ordinary world objects remain ordinary Game objects

Where concrete workers, tools, ore items, carts, storage containers, supports, doors, lamps, or structures are required, they should use normal authoritative Game/world semantics.

The Mine controller should reason in bounded mine-facing abstractions and must not become a parallel item/inventory/actor authority.

## 2.4 Mine is not Dungeon.Core with renamed resources

Hard prohibition:

~~~text
DungeonCore
    Personality = Miner
    Essence = Labor
    Treasure = Ore
~~~

Mine and Dungeon have different semantic reasons for changing space.

~~~text
Mine:
    people spend labor/time/capital to gain material value

Dungeon:
    autonomous organism spends Essence/strategy to survive, grow, attract, defend, and adapt
~~~

They may later share proven spatial planning/excavation machinery. They do not share organism/economy semantics by inheritance.

---

# 3. Implementation Order Relative to Dungeon

Mine is intentionally the first consumer.

~~~text
Mine implementation
    ↓
prove subterranean planning
prove excavation
prove persistence
prove geology/depletion
prove labor/output
prove abandonment
    ↓
review Mine implementation for candidate seams
    ↓
Dungeon implementation begins
    ↓
Dungeon becomes second consumer
    ↓
compare Mine/Dungeon requirements
    ↓
extract only what is actually common
~~~

Do not create a large generic SubterraneanFramework before the second consumer exists.

Likely future common concepts may include:

- opportunity volumes;
- connectors;
- connected topology;
- spatial legality / CanFit;
- occupancy/reservation;
- vertical connectors;
- built-vs-future state;
- incremental excavation/construction;
- bounded partial replan;
- persistent structural identity;
- diagnostic geometry;
- world-space realization.

Mine-specific concepts remain Mine-owned: geology knowledge, ore/deposit extraction, labor/equipment productivity, hauling, mine economics, depletion, and mine lifecycle.

Dungeon-specific concepts remain Dungeon-owned: Essence, Core, personality, room semantic purposes, monster populations, treasure, traps/puzzles/illusions, visitor pressure, and Core defense/relocation/death.

---

# 4. Identity and Lifecycle

Suggested persistent identities:

~~~text
MineId
MineSeed / planning seed where needed
FoundingTime
Origin
PlanRevision
LifecycleState
~~~

Possible lifecycle:

~~~text
Potential
    ↓
Prospected
    ↓
Founded
    ↓
Initial Excavation
    ↓
Producing
    ↓
Expanding
    ↓
Declining
    ↓
Exhausted
    ↓
Abandoned
    ↓
Repurposed / Ruined / Reopened
~~~

These exact enum values are not frozen.

Important rules:

- exhaustion is not deletion;
- abandonment is not deletion;
- built tunnels, shafts, and rooms remain persistent world structure;
- remaining geology remains world truth;
- a mine may later be reopened;
- another system may occupy or reuse the structure;
- a future Dungeon Core may arise in or consume old workings without replacing their history.

---

# 5. Deterministic Geology

## 5.1 Geological regions

World geology should be generated/addressed at a bounded region scale independent of Mine sections.

A geological region may contain:

- host rock/material;
- strata/layers;
- faults/fractures;
- deposit bodies/veins;
- material grade/density;
- hardness/resistance;
- future water/aquifer hints;
- future stability/collapse-relevant properties.

Exact region dimensions and representation are not frozen.

The important invariant is stable world-space truth.

## 5.2 Deposits are bodies, not per-room RNG

Do not roll:

~~~text
new room complete
    -> 20% chance iron
~~~

Instead, generate deterministic deposit geometry or fields.

A vein may cross many excavation sections:

~~~text
                  Iron Vein #17
        ───────────────────────────
          /          |            \
 Section 12      Section 13     Section 19
   exposure        exposure       exposure
~~~

Each section sees the part of the same deposit that intersects its excavated/exposed volume.

This naturally supports following a vein, partially clipping a vein, multiple mines meeting the same deposit, visible continuation direction, and finite depletion.

## 5.3 Candidate deposit representations

The first implementation should choose the simplest bounded deterministic representation that supports the consumer.

Possible representations include:

- coarse 3D volumes;
- splines with radius/grade;
- bounded fields;
- convex/primitive deposit shapes;
- region-local procedural functions.

Do not build a voxel geology simulator merely because underground resources are 3D.

---

# 6. Knowledge and Prospecting

Geological truth and character/mine knowledge are separate.

~~~text
World truth
    exact deposit exists

Mine knowledge
    what has been observed, exposed, surveyed, inferred
~~~

The Mine controller must make ordinary decisions from known or inferred geology, not hidden exact developer truth.

Possible knowledge levels:

~~~text
Unknown

Surface indication
    staining / float / terrain hints

Prospected
    likely material family
    rough confidence/direction/depth

Exposed
    deposit visible in excavated section

Surveyed
    better estimate of continuation/grade

Extracted
    measured production history improves estimate
~~~

A developer diagnostic viewer may display exact geology. Player-facing knowledge must obey normal Game disclosure/knowledge rules.

---

# 7. Mine Opportunity Blueprint

## 7.1 Purpose

At founding, perform the expensive broad spatial work once.

~~~text
Mine origin
+ world-space constraints
+ surface access
+ planning seed/version
+ allowed depth/range
        ↓
EXPENSIVE INITIAL PLANNING
        ↓
Mine Opportunity Blueprint
~~~

The blueprint represents legal connected places the mine could excavate. It is opportunity, not destiny.

## 7.2 Candidate opportunity types

Examples:

- entrance cut;
- main heading/tunnel;
- branch tunnel;
- shaft;
- ramp;
- chamber/work area;
- haulage bay;
- future storage/support room;
- ventilation/service connector where later earned;
- exploratory heading;
- vertical continuation.

Mine does not need Dungeon room archetypes such as Combat, Treasure, Puzzle, Boss, or Core.

## 7.3 Opportunity record

A compact opportunity may contain:

~~~text
MineOpportunityId
Parent/connector identity
planned volume/shape
connector(s)
depth
estimated excavation work
host-rock class known to planning
world-space legality state
frontier state
optional authored purpose hint
~~~

It must not contain hidden ore knowledge merely because developer geology is available.

## 7.4 Connected topology invariant

Every newly completed excavation must connect to legal existing built mine topology unless explicitly created as a separate authorized mine entrance or shaft operation.

No room or tunnel may simply appear underground at an unrelated point.

## 7.5 Spatial legality / CanFit

Planning and realization must respect:

- terrain/world boundaries;
- existing structures;
- protected or occupied underground volumes;
- other mines/dungeons where known to world authority;
- vertical clearance;
- minimum connector dimensions;
- required surface emergence legality;
- bounded search/work budgets.

The first Mine consumer should prove the actual API/ownership shape before any common underground planner is extracted.

---

# 8. Normal Operation Should Consume Opportunities Cheaply

Hard performance/design rule:

Normal excavation consumes already-planned opportunities and should not invoke a fresh global spatial search.

Routine modes:

~~~text
FOUNDING
    generate broad deterministic opportunity space

NORMAL EXPANSION
    choose / validate / consume existing opportunity

INTERFERENCE / EXHAUSTION
    bounded partial replan or extension from valid frontier
~~~

This matches UltraSim's broader approach of expensive cold preparation plus cheap bounded hot-path consumption.

---

# 9. Excavation Units Are Coarse Semantic Sections

Mine is not Minecraft.

The authoritative operation is not:

~~~text
destroy rock block 18492
destroy rock block 18493
...
~~~

It is:

~~~text
Excavate MineSection / Opportunity 42
~~~

The section may have an arbitrary bounded volume or shape appropriate to planning and construction.

Suggested lifecycle:

~~~text
Future
    ↓
Selected
    ↓
Reserved
    ↓
Excavating
    ↓
Open / Built
    ↓
Supported / Active
~~~

Later states may include Damaged, Flooded, Collapsed, Sealed, Abandoned, or Reopened. Only states earned by current gameplay should be implemented.

---

# 10. Excavation Progress

Excavation is a committed long-running action.

It should have:

- stable target opportunity;
- authoritative start/deadline or work requirement;
- labor/equipment contribution;
- current progress;
- completion transition;
- persistence through save/load;
- cancellation/failure policy;
- atomic final structural publication.

A save must never persist a half-validated candidate excavation.

~~~text
select candidate
    ↓
validate
    spatial legality
    connectivity
    capacity
    cost/work
    ownership
    current plan revision
    ↓
COMMIT excavation job
    ↓
workers/time advance progress
    ↓
completion validation
    ↓
publish built traversable structure atomically
~~~

Presentation may animate a tunnel or room deepening gradually, but progress visuals are not structural authority.

---

# 11. Geological Exposure

When a section becomes excavated/open, determine which geological bodies intersect newly exposed surfaces or volume.

The mine learns only what the excavation and prospecting rules expose.

~~~text
Section 18 completes
    ↓
intersects Iron Vein #17
    ↓
new exposed face/volume registered
    ↓
MineKnowledge:
    iron exposed on northeast face
    estimated grade/continuation
~~~

The deposit existed before this moment. Completion reveals it; it does not roll or create it.

---

# 12. Finite Resource Depletion

Extractable resources are finite.

A useful conceptual model:

~~~text
Deposit #17
    world-space deposit truth

Exposure A
    section 12
    recoverable amount remaining

Exposure B
    section 13
    recoverable amount remaining
~~~

The exact accounting may be per exposed sub-volume, deposit segment, or another bounded representation.

Hard rules:

- extraction reduces remaining material;
- save/load cannot recreate depleted resource;
- abandoning/reopening does not refill it;
- two accesses to the same geological body must conserve total material according to the chosen representation;
- resource generation does not happen when an extraction job starts.

If material later naturally forms or regenerates, that must be an explicit world-geology mechanic, not generic mine respawn.

---

# 13. Labor, Workers, and Jobs

Mine does not require full Sim Core for its first proof.

The Mine subsystem should expose bounded work requirements/jobs and consume a narrow labor capability.

Possible work classes:

- prospect;
- excavate;
- support;
- extract;
- haul;
- maintain;
- clear collapse;
- pump/drain later if earned.

A first harness may provide deterministic synthetic labor capacity.

Later Sim Core workers should perform ordinary jobs through standard world/actor systems.

Mine should not own NPC movement or general job scheduling.

---

# 14. Tools and Equipment

Tools and equipment can influence:

- excavation rate;
- extractable material rate;
- rock hardness capability;
- support construction;
- safety;
- hauling capacity.

Mine controller should consume bounded capability/effect summaries rather than concrete item prefab names.

Ordinary items remain Game/Inventory authority.

---

# 15. Hauling, Storage, and Output

A mine is not economically proven until material leaves the face.

~~~text
deposit exposure
    ↓ extraction
face/local pile
    ↓ hauling
mine internal storage
    ↓
surface stockpile / cart / warehouse
    ↓
settlement economy / construction / trade / smelting
~~~

Early implementation may collapse some physical intermediate steps while preserving conservation.

Eventually hauling distance should matter: deeper sections cost more, poor layout can reduce throughput, shafts and vertical routes matter, and transport infrastructure can improve output.

Mine should publish or consume ordinary material inventory through Game/economy integration rather than maintaining a magical independent ore counter forever.

---

# 16. Mine Decision Logic

The first controller can be simple and deterministic.

Inputs should be known information:

- known exposed material remaining;
- inferred continuation/confidence;
- opportunity excavation cost;
- haul distance;
- labor availability;
- tool/capability;
- support/maintenance cost;
- material value/demand;
- risk where implemented.

~~~text
ExpectedKnownValue
- ExcavationCost
- HaulCost
- Support/MaintenanceCost
= candidate utility
~~~

Do not let the mine secretly query exact hidden deposits to select the perfect tunnel.

A later manager, NPC, or ownership system may override or influence decisions.

---

# 17. Time Scale

Mine expansion should be slow enough that finite geology remains economically meaningful.

Illustrative only:

~~~text
small heading extension
    days

significant new section
    days/weeks

deep shaft/major expansion
    weeks/months

large deposit extraction
    weeks/months/years
~~~

Exact rates belong to balancing/content, not architecture.

The important rule is that excavation and depletion are not high-frequency churn.

---

# 18. Persistence and Resume Equivalence

Mine persistence must prove:

~~~text
run to state S
save
destroy runtime
load S
continue with identical future inputs/time
    ↓
same authoritative future
~~~

Persist or deterministically reconstruct:

- MineId;
- founding/origin identity;
- lifecycle;
- PlanRevision;
- built topology;
- remaining/future opportunities;
- committed excavation jobs/progress;
- known/exposed geology;
- depletion/extraction state;
- storage/output state required by Mine;
- relevant references to ordinary world objects;
- next due decision/work times;
- bounded history needed by decisions.

Do not persist transient viewer/native rendering state.

---

# 19. Bounded Dormant/Offline Advancement

Mines continue existing while inactive, but catch-up must not replay millions of tiny ticks.

Compressible progression may include committed excavation work under stable labor summaries, extraction under stable admitted work, maintenance timers, and cooldown/deadline progression.

Required discrete transitions include excavation completion, breakthrough, geology exposure, resource depletion threshold, collapse/flood event when later implemented, lifecycle transition, and new expansion decision.

Bounded catch-up may defer owed discrete work but must never silently drop it.

---

# 20. Transactional Mutations

Meaningful mine changes use validate-before-commit.

Examples:

- founding;
- select excavation;
- complete excavation;
- partial replan;
- open second entrance;
- seal branch;
- abandon/reopen;
- support/collapse transitions when implemented.

~~~text
candidate mutation
    ↓
validate
    identity/revision
    connectivity
    world-space legality
    capacity
    resources/work
    ownership
    search/work budget
    ↓
COMMIT atomically
    ↓
publish occurrence / structural change
~~~

Failure before COMMIT leaves authoritative Mine state unchanged.

---

# 21. Replanning and Interference

Built mine topology is historical world truth and should not be moved because future planning became inconvenient.

If future opportunities become invalid:

~~~text
built topology
+ current world constraints
+ Mine plan seed/revision
+ remaining valid frontiers
        ↓
bounded deterministic partial replan
~~~

Replan only Future or Unbuilt opportunities.

Reasons may include another structure now occupying space, terrain/world change, another excavation intersecting planned volume, collapse/sealing changing usable frontier, or original opportunity space exhaustion.

Rare plan extension may occur from legal built frontiers.

---

# 22. Support, Flooding, Collapse, and Safety

These are valuable Mine mechanics but should not explode the first implementation.

Preserve seams for:

- support requirements;
- rock stability;
- collapse;
- flooding and aquifers;
- drainage/pumping;
- ventilation;
- fire/gas later if desired.

Ownership split:

~~~text
Geology/world
    hardness
    fractures
    water/aquifer information
    material properties

Mine-built structure
    excavation
    supports
    drainage
    ventilation infrastructure

Simulation
    resulting risk/state
~~~

MIN0-MIN5 do not require the full hazard system.

---

# 23. Presentation and Structural Realization

Mine topology is authoritative structural truth. Rendering is derived.

A completed section may realize as terrain/void boundary changes, static shell geometry, combined or greedy-meshed surfaces, UO-like statics/Multis where appropriate, or primitive/debug volumes during early development.

Excavation progress may be visually represented by growing recesses, staged shell variants, ghost/outline plans, or construction timers/worker cues.

Do not represent each removed rock chunk as a persistent authoritative object unless a concrete feature earns it.

---

# 24. Debug / Diagnostic Projection

The Mine implementation should expose a bounded immutable diagnostic projection rather than letting tooling inspect mutable internals.

~~~text
MineDebugSnapshot
    Mine identity/lifecycle

    built sections
    future opportunities
    blocked/invalid opportunities
    active excavation
    frontiers

    known geology
    exact geology only in privileged developer projection
    exposed deposits
    depletion

    worker/job summary
    storage/output
    haul routes/cost summaries

    recent decisions
    plan revisions
    timing/work metrics
~~~

The viewer should consume diagnostic truth. It must not calculate Mine validity or geology.

---

# 25. Mine Viewer

The first useful viewer can be a general 3D subterranean structure diagnostic tool.

Required capabilities:

- free-fly/orbit;
- whole-mine zoom;
- floor/depth isolation;
- cutaway/clipping;
- built vs future volumes;
- blocked/invalid volumes;
- connectors/shafts;
- active frontier;
- active excavation progress;
- known deposit/exposure overlays;
- privileged exact geology overlay;
- remaining-resource visualization;
- hauling/path overlays;
- jump to entrance/frontier/job/deposit.

Player-facing disclosure is separate from developer diagnostics.

The viewer should be designed so Dungeon may later reuse the general geometry/diagnostic rendering pieces without becoming hard-coded to Mine ore/economics.

---

# 26. Abandoned Mines Are Persistent World Spaces

Abandonment preserves tunnels, shafts, chambers, supports/ruins, entrances, depleted and remaining geology, and historical identity.

Future uses may include bandit hideout, monster habitat, smuggler route, storage, mushroom farm, tomb, collapsed ruin, reopened mine, or Dungeon origin.

~~~text
Year 832
    Silver Mine founded

Year 851
    major vein exhausted

Year 858
    abandoned

Year 873
    creatures occupy lower workings

Year 880
    magical convergence / deaths / event
    creates Dungeon Core

Dungeon:
    consumes existing legal topology
    preserves old mine history
    expands beyond it
~~~

This is an important integration target for Dungeon.

---

# 27. Mine-to-Dungeon Handoff

Dungeon must be able to begin from existing underground world structure.

The handoff should not convert the Mine runtime into Dungeon.Core.

~~~text
Abandoned Mine built topology
        ↓
ordinary persistent world structural truth
        ↓
Dungeon origin event
        ↓
new DungeonId / Dungeon.Core
        ↓
Dungeon structural planner recognizes existing traversable/occupiable topology
        ↓
Dungeon assigns its own semantic meaning
        ↓
Dungeon may generate new expansion opportunities
~~~

Mine depletion and geology history remain independent world history.

Dungeon does not inherit Mine labor/output logic.

---

# 28. Proof Before Shared Extraction

The first implementation should use concrete Mine-domain names and types.

Do not prematurely create GenericSubterraneanOpportunity, UniversalUndergroundController, or a generic excavation framework.

Keep likely seams clean, but let Mine prove them.

When Dungeon becomes the second consumer:

1. compare Mine and Dungeon planning/realization requirements;
2. identify literally shared invariants;
3. extract the smallest stable common subsystem;
4. migrate both consumers to it;
5. preserve Mine-specific and Dungeon-specific semantics above it.

Possible future names include SubterraneanPlanning, UndergroundSpatialPlanning, or Excavation.Core, but no name or API is frozen.

Mine proves implementation. Dungeon proves generality.

---

# 29. Suggested Implementation Checkpoints

Checkpoint names are implementation labels, not public API.

## MIN0 — Identity / Persistence / Deterministic Time

Implement MineId, origin/founding identity, lifecycle skeleton, world time/due times, deterministic addressed randomness, persistence schema/version, state hashing, save/load-resume equivalence, bounded catch-up skeleton, transactional pre-COMMIT vs committed-action rules, and direct test/harness composition.

Accept when identical initial state, inputs, and time produce identical hashes across uninterrupted and save/load runs.

## MIN1 — Deterministic Geology / Prospecting

Implement geological region identity/version, deterministic host geology, the first bounded deposit/vein representation, finite material quantity, world-position query, Mine knowledge separate from exact truth, and simple prospecting.

Accept when the same world seed/region produces the same deposits independently of Mine creation order and prospecting reveals bounded knowledge without mutating geology.

## MIN2 — Mine Opportunity Blueprint

Implement entrance/root, connected future sections, branches, vertical opportunity/shaft, frontier records, compact opportunity identities, broad founding-time planning, and no geology cheating in path selection.

Add primitive/wire 3D plan diagnostics.

Accept when deterministic opportunity plans are connected, bounded, inspectable, and independent from hidden-deposit selection.

## MIN3 — Spatial Realization / CanFit

Implement mapping of opportunities into legal 3D volumes, world-space occupancy/constraint queries, connector clearance, vertical legality, existing-structure avoidance, bounded placement/search budgets, and route/connectivity queries.

Accept when the blueprint maps into legal world-space opportunity volumes and all future excavation is connected to valid roots/frontiers.

## MIN4 — Incremental Excavation

Implement Future/Selected/Reserved/Excavating/Open states, committed excavation jobs, work/progress accounting, completion, atomic publication of traversable built structure, built-topology persistence, and normal consumption of preplanned opportunities without spatial search.

Accept when synthetic labor can excavate several connected sections over time and save/load preserves identical completion/history.

## MIN5 — Geological Exposure / Finite Depletion

Implement completed-excavation intersection with geological deposits, exposure records, known-geology updates, extraction, finite remaining quantity, conservation across multiple exposures to the same deposit, and deterministic persistence.

Accept when a vein can cross multiple sections, extraction from each conserves the same deposit truth, and depletion survives reload/reopen.

## MIN6 — Labor / Hauling / Output Integration

Implement a narrow work/job interface, synthetic workers first, excavation/extraction work, haul distance/cost, mine storage/output, and ordinary Game material/inventory integration where currently available.

Do not build full Sim Core.

Accept when material is not considered economically produced merely because extraction occurred and accepted work moves conserved output through a bounded storage/export path.

## MIN7 — Expansion Decision / Bounded Replan

Implement candidate opportunity evaluation from known information, known deposit continuation, excavation/haul cost, deterministic tie break, plan invalidation, bounded partial replan, and rare plan extension.

Accept when mines follow exposed/inferred geology without omniscient hidden-truth access and world interference changes only future topology.

## MIN8 — Lifecycle / Exhaustion / Abandonment

Implement Producing/Declining/Exhausted/Abandoned transitions as earned, no automatic resource respawn, stop/slow expansion when economics no longer justify it, preserve built structure/geology, reopen/repurpose seam, and abandoned-mine world projection.

Accept when a mine can exhaust, stop operating, be destroyed/reloaded/reopened as world history, and its tunnels/resources do not reset.

## MIN9 — Integration / Stress / Dungeon Handoff Fixture

Verify multiple mines/regions, shared geological-deposit cases where legal, long dormant catch-up, deep/branching mines, repeated save/load, high excavation counts, and an abandoned-mine fixture suitable for Dungeon origin.

Accept when the system survives long-horizon deterministic runs and exposes an abandoned persistent subterranean structure that Dungeon can later consume without depending on Mine runtime semantics.

---

# 30. First Vertical Slice

Keep the first proof small.

~~~text
1 mine
1 surface entrance
1 main tunnel
1 branch
1 vertical connector or deeper continuation
~6–12 potential excavation sections
~3–6 completed sections

Geology:
    host rock
    1 common deposit
    1 rarer deposit/branch
    finite quantities

Work:
    synthetic excavation labor
    synthetic extraction labor
    simple hauling/storage

Decisions:
    continue main heading
    follow exposed vein
    choose branch
    stop when uneconomic/exhausted

Viewer:
    built/future/blocked
    active excavation
    known geology
    exact developer geology
    resource remaining
    haul route
~~~

This slice is enough to prove the subterranean machinery before Dungeon complexity is introduced.

---

# 31. Definition of Mine Works

Mine is fundamentally proven when:

- geology exists deterministically as world truth independent of Mine generation order;
- geological knowledge is separate from exact hidden geology;
- a mine has stable persistent identity;
- founding creates a connected bounded opportunity plan;
- spatial realization respects world constraints and existing structures;
- normal excavation consumes preplanned opportunities without fresh global spatial search;
- every built section connects legally to existing built topology;
- excavation is a persistent committed long-running action;
- completed excavation becomes ordinary persistent traversable world structure;
- geology is exposed rather than rolled at completion;
- veins and deposits can cross multiple sections;
- extraction conserves finite material;
- depletion persists across save/load, abandonment, and reopen;
- workers/labor integrate through narrow jobs rather than Mine owning Sim Core;
- output is hauled/stored/exported rather than magically credited;
- expansion decisions use known information rather than hidden developer geology;
- future invalidation uses bounded partial replan without moving built history;
- long inactive periods advance through bounded catch-up;
- save/load-resume produces the same future;
- mine exhaustion/abandonment preserves the physical site;
- diagnostics explain plan, work, geology knowledge, depletion, and decisions;
- an abandoned mine can later be consumed as ordinary existing world topology by Dungeon.

---

# 32. Deliberately Deferred

The first Mine implementation does not require:

- full Sim Core;
- detailed labor professions;
- full economy/market simulation;
- voxel terrain destruction;
- realistic geological finite-element simulation;
- full support engineering;
- flooding/ventilation/gas;
- mine carts/rails;
- advanced collapse physics;
- Godot-specific presentation;
- Dungeon Core;
- monsters/traps/treasure/puzzles;
- generic public subterranean framework extraction.

Those may be added when a real consumer earns them.

---

# 33. Permanent Mine Rules

~~~text
GEOLOGY IS WORLD TRUTH, NOT MINE RNG.

THE MINE REVEALS GEOLOGY; IT DOES NOT CREATE IT.

MINE KNOWLEDGE != EXACT GEOLOGICAL TRUTH.

MINE EXCAVATION IS COARSE SEMANTIC WORK, NOT BLOCK-BY-BLOCK VOXEL DESTRUCTION.

NORMAL EXPANSION CONSUMES PREPLANNED OPPORTUNITIES.

BUILT UNDERGROUND STRUCTURE IS PERSISTENT WORLD HISTORY.

ORE/STONE/COAL/ETC. ARE FINITE UNLESS AN EXPLICIT WORLD MECHANIC SAYS OTHERWISE.

DEPLETION SURVIVES SAVE/LOAD, ABANDONMENT, AND REOPENING.

LABOR/TIME CAUSE EXCAVATION; THE MINE DOES NOT SPEND DUNGEON ESSENCE.

MINE CONTROLLER USES KNOWN/INFERRED GEOLOGY, NOT HIDDEN DEVELOPER TRUTH.

OUTPUT MUST PASS THROUGH CONSERVED EXTRACTION/STORAGE/HAULING SEMANTICS.

ABANDONED != DELETED.

MINE IS NOT A DUNGEON CORE CONFIGURATION.

MINE PROVES SUBTERRANEAN IMPLEMENTATION; DUNGEON PROVES GENERALITY.

DO NOT EXTRACT A GENERIC UNDERGROUND FRAMEWORK BEFORE THE SECOND CONSUMER EXISTS.
~~~
