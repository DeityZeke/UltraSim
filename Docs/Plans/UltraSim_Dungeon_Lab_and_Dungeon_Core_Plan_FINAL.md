UltraSim Dungeon Lab — Dungeon Core, Generator, Simulation Harness, and Tooling Plan

Status: Implementation-ready architecture / engineering plan — Dungeon implementation follows the Mine/subterranean proof campaign
Project: UltraSim
Subsystem: Dungeon Core / Dungeon Generator / Dungeon Lab
Purpose: Define a coherent, testable dungeon-organism subsystem and a dedicated lab application/tooling environment that can be built before the full Sim Core is available, while remaining compatible with later UltraSim integration. The simpler Mine subsystem now precedes Dungeon implementation and proves the first subterranean planning/excavation machinery; see MINE.md.

1. Executive Summary

UltraSim dungeons are not static levels and are not player-triggered content instances.

A dungeon is a persistent, autonomous world participant that:

appears after world events or environmental conditions make one possible;

has a stable identity, seed, origin, personality, goals, resources, topology, and history;

grows over time even when nobody is watching;

builds rooms, floors, traps, puzzles, treasure, monsters, champions, bosses, and defenses;

attracts or repels visitors based on its strategy and reputation;

gains power from passive regeneration, traffic, combat, injury, death, and possibly assimilated outside matter;

spends that power on growth, inhabitants, rewards, defenses, repairs, and adaptation;

can become overpopulated or unstable;

can push monsters into the surrounding world or lose control of them;

can be discovered, tolerated, exploited, farmed, feared, contained, or deliberately destroyed;

dies permanently when its Core is destroyed;

may leave persistent ruins, surviving monsters, abandoned treasure, structural remains, and history behind.

The Dungeon Lab exists to prove this organism before connecting it to full UltraSim people, economy, politics, history, regions, and world simulation.

The Dungeon Lab must use the real Dungeon Core and generator, but fake the surrounding civilization only as much as necessary to apply useful pressure to it.

The synthetic adventurers are test fixtures. The dungeon is not.

1.1 Mine-First Implementation Order

Dungeon is no longer the first implementation consumer of subterranean planning.

The Mine campaign in MINE.md intentionally runs first because Mine needs a much smaller semantic surface:

~~~text
Mine:
    where can excavation go?
    what has been excavated?
    what geology has been exposed?
    what finite material remains?
    how much work/progress exists?
    who is working?
    how is output hauled/exported?
~~~

Dungeon then becomes the harder second consumer:

~~~text
Dungeon:
    everything above that is genuinely reusable
    +
    autonomous Core/organism semantics
    Essence
    personality/strategy
    semantic room purposes
    monsters
    treasure
    traps
    puzzles
    illusions
    visitor-pressure economics
    Core protection/relocation/death
~~~

Implementation direction:

~~~text
Mine proves subterranean implementation
        ↓
Dungeon consumes/reuses the proven spatial concepts
        ↓
Mine + Dungeon comparison proves what is actually generic
        ↓
only then extract a shared subterranean subsystem
~~~

Hard rule:

Dungeon.Core must not become a Mine controller with renamed resources, and Mine must not subclass/configure Dungeon.Core.

Mine and Dungeon may share spatial planning/excavation machinery only after the second consumer demonstrates literal common invariants.

2. Core Architectural Position

2.1 Dungeon Core ownership

The dungeon itself should not primarily be an ECS entity. It fits better as sparse Game-owned runtime/controller state with a stable DungeonId.

Conceptually:

DungeonRuntime / DungeonModule
    DungeonId
    Seed
    Origin
    CoreLocation

    Personality
    Strategy State

    Resources
    Capacities
    Threat / Reputation Inputs

    Logical Plan
    Built Topology
    Growth Frontiers

    Monster / Reward / Trap investment state

    Current Goals
    Candidate Decisions
    History / summaries

Why:

few dungeons exist relative to ordinary actors/items;

each dungeon owns variable-sized topology and planning data;

strategic cadence is slow;

graphs, room sets, floors, and history are sparse;

the state is long-lived but not dense-per-entity data.

A tiny ECS anchor may exist later if useful:

DungeonCoreEntity
    DungeonId
    WorldPosition

but the dungeon graph itself should not be forced into dense ECS storage.

2.2 Dungeon-owned world objects

The dungeon may create/control:

monsters;

champions;

bosses;

chests;

items;

doors;

traps;

interactive mechanisms.

In final UltraSim integration these should use ordinary Game/world semantics whenever appropriate.

Example:

Dungeon creates Champion
    ↓
Champion becomes normal actor/entity
    ↓
Champion receives real generated weapon
    ↓
Weapon exists as persistent item

2.3 Core Agnosticism

Dungeon.Core owns dungeon semantics.

It must know nothing about:

EngineHost;

Game bootstrap/composition;

Network;

Godot;

Dungeon Lab;

SideCar hosting;

concrete UltraSim item/container/actor prefabs;

vendor/economy UI;

client debug presentation.

The Core exposes its own native API and requires only narrow dungeon-domain capabilities that concrete consumers have actually earned.

Hard prohibition:

Dungeon.Core
    must not call
EngineHost.Current
Game.Current
Network
Godot

and should not accept IEngineHost merely to discover unrelated services.

Conceptually:

Dungeon.Core
    owns:
        survival
        growth
        strategy
        topology
        Essence economics
        personality
        Core defense
        planning/replanning
        persistence semantics

It does not own the concrete gameplay realization of:

Chest
Backpack
IronSword
GoblinPrefab
Vendor
CorpseContainer
GodotScene

Those belong to integration layers.

2.4 EngineHost v0 — Composition and Lifecycle Only

Mine may now become the earlier concrete composition/lifecycle consumer. Dungeon remains a strong consumer of the same minimal EngineHost direction, but Dungeon must not be used to force EngineHost ahead merely to prove subterranean spatial planning.

EngineHost exists to own composition and lifecycle, not Dungeon semantics.

Desired dependency direction:

                    EngineHost
                        │
                        │ lifecycle/composition
                        ▼
                Dungeon.HostAdapter
                 /       |       \
                /        |        \
               ▼         ▼         ▼
      Dungeon.Core   Gameplay   Definitions

Important:

Dungeon.Core must not implement an EngineHost interface.

A thin host adapter/bootstrapper implements the host contract and translates host lifecycle into Core-native operations.

EngineHost v0 should remain deliberately small.

Initial responsibilities:

registration
dependency/order validation
construction/bootstrap
start
deterministic/timed update hooks where applicable
stop
dispose
fault containment/reporting
diagnostics enumeration

Checkpoint/save lifecycle may be added only as concretely earned by Dungeon persistence.

Do not use Dungeon as an excuse to immediately build:

dynamic plugin loading
hot replacement
reflection-heavy discovery
generic command bus
generic event bus
generic persistence framework
cross-engine RPC
arbitrary script loading
large DI container

The Dungeon and later Sim Core should refine EngineHost through actual requirements.

2.5 Dungeon.HostAdapter

Dungeon.HostAdapter is the composition adapter between EngineHost and the Core.

Conceptually it:

constructs/configures Dungeon.Core;

supplies world-facing ports/adapters;

translates EngineHost lifecycle;

surfaces diagnostics/faults;

coordinates persisted checkpoint ownership if/when required;

does not add dungeon gameplay semantics of its own.

Example conceptual direction:

EngineHost lifecycle
        ↓
Dungeon.HostAdapter
        ↓
Dungeon.Core native API

The Core should remain directly runnable from:

Game/server
Dungeon Lab
unit/integration tests
deterministic replay harness
benchmark harness
offline validation tools
future SideCar/tool composition

without pretending EngineHost exists.

That direct-run capability is an explicit agnosticism test.

2.6 Dungeon.Gameplay Integration Layer

The missing middle layer between abstract dungeon strategy and concrete UltraSim gameplay is Dungeon.Gameplay.

The Core should make abstract dungeon-domain intentions.

The Gameplay layer translates those intentions into ordinary authoritative UltraSim semantics.

Example — reward:

CORE:
RewardInvestment
    RoomId = 42
    Budget = 35 Essence
    Intent = Attraction
    QualityBias = High

        ↓

GAMEPLAY:
Profile resolves:
    container/placement
    generated item(s)
    ordinary goods

        ↓

real UltraSim item/container creation

        ↓

CORE receives bounded result:
RewardPlacementResult
    Success
    ActualEssenceCost
    ExternalWorldReferences

The Core knows about:

reward investment

not:

treasure chest prefab.

Example — defenders:

CORE:
DefenderInvestment
    Budget = 50
    Room = 17
    DesiredRole = HeavyGuard

        ↓

GAMEPLAY:
Profile chooses concrete actors/content
        ↓
ordinary Game actor creation

The Core knows about:

defender capacity/value

not:

OrcChampionScript.

Example — assimilation:

GAMEPLAY reports:
Room 18 has reclaimable external assets
    A = 12 Essence-equivalent
    B = 7
    C = not assimilable

CORE decides:
    Assimilate A

GAMEPLAY:
    validates live object/lifetime
    validates ownership/rules
    commits ordinary Game destruction/transfer

CORE receives:
    AssimilationCommitted
    EssenceGranted = 12

The Core reasons about strategy and value.

The Gameplay layer reasons about actual items, actors, containers, ownership, and Game rules.

2.7 Minimal Gameplay Assumptions in Core

Dungeon Core may legitimately understand concepts intrinsic to being a dungeon organism:

RewardInvestment
DefenderInvestment
TrapInvestment
PuzzleInvestment
HabitatInvestment
ExternalAssimilationValue
VisitorActivity
VisitorInjury
VisitorDeath
CoreThreat
Threat
Traffic

It should not understand concrete world implementation types unless the concept is genuinely fundamental to dungeon semantics.

This is the filter:

If the dungeon needs the concept to decide how to survive/grow, Core may know the abstract concept. If the concept only explains how UltraSim realizes it physically, Gameplay owns it.

2.8 Profiles

Profiles are configuration/content policy, not another authority layer.

A dungeon profile may provide:

personality defaults
growth costs
passive-regeneration curve
room/shape biases
reward-investment profile
defender-investment profile
trap/puzzle preferences
entrance style
Core visual profile

Core consumes deterministic dungeon-facing configuration.

Gameplay consumes concrete content-facing configuration.

Profiles must not silently mutate authoritative state outside normal Core/Game mutation paths.

2.9 Scripts / Concrete Customization

Future ObjectScript-like/custom behavior should sit toward the concrete gameplay edge.

Example:

Crypt Dungeon Profile

Core:
    wants medium defender investment

Gameplay/Profile:
    Undead theme

Concrete behavior:
    skeleton / wraith / sarcophagus semantics

Avoid:

Dungeon.Core
    → CryptDungeonScript.OnSpawnSkeleton()

The Core requests an abstract action.

The concrete integration decides how the game realizes it.

2.10 SideCar Boundary

Establish the SideCar architectural boundary now, but do not make full SideCar development a prerequisite for Dungeon.

Conceptual symmetry:

                    Dungeon.Core
                      ▲       ▲
                      │       │
              In-process   Tool/SideCar
                  Host        Host

Potential future SideCar/tool uses:

heavy dungeon-generation analysis;

offline validation;

telemetry aggregation;

asset processing;

administrative indexing;

benchmark orchestration.

Do not move authoritative Dungeon simulation out of Game merely because SideCars exist.

Authoritative world semantics remain in Game unless a future explicit distributed-authority contract proves otherwise.

SideCar is not:

"anything expensive runs somewhere else."

2.11 Proof Before Extraction

The extraction rule is now two-stage.

Stage 1 — Mine proves the first implementation

Build the Mine subsystem concretely in UltraSim first.

Mine should use concrete Mine-domain concepts while keeping likely spatial seams clean:

MineOpportunity
MineConnector
MineExcavationSection
MineBuiltTopology
MinePlanRevision
Mine spatial legality / CanFit queries

Do not invent a public generic subterranean package before Dungeon exists as a second consumer.

Stage 2 — Dungeon proves generality

When Dungeon implementation begins, compare its actual needs against the Mine implementation.

Extract only concepts that are literally shared, such as proven candidates:

opportunity volumes
connectors
connected topology
world-space legality / CanFit
occupancy/reservation
vertical connectors
built-vs-future state
incremental excavation/construction
bounded partial replan
persistent structural identity
diagnostic geometry

Do not extract Mine-specific semantics:

geology knowledge
ore/deposit depletion
labor
hauling
mine economics

Do not extract Dungeon-specific semantics:

Essence
Core
personality
room purpose
monsters
treasure
traps
puzzles
illusions
visitor pressure
Core defense/relocation/death

Dungeon.Core itself still follows Build → Prove → Harden → Extract → Publish.

Recommended current conceptual ownership remains:

UltraSim/
    Game/
        Dungeons/
            Core/
            Host/
            Gameplay/
            Profiles/
            Scripts/
            Diagnostics/

    Tools/
        DungeonLab/
            App/
            Harness/
            Synthetic/
            Viewer/
            Benchmarks/
            Regression/

The exact physical names should follow repository conventions.

Important ownership rule:

Dungeon.Core should not live under Tools/DungeonLab/.

Dungeon Lab consumes the Core.

Game consumes the Core through its Host/Gameplay composition.

If Mine + Dungeon prove a stable shared subterranean package later, both should migrate to that one implementation. Do not copy one planner into the other and allow two spatial truths to diverge.


3. Dungeon Identity and Lifecycle

Suggested persistent identity:

DungeonId
DungeonSeed
CreationTime
CreationCause
CoreLocation
LifecycleState
PlanRevision

Possible lifecycle:

Potential
    ↓
Born / Forming
    ↓
Active
    ↓
Struggling / Starved / Damaged
    ↓
Core Destroyed
    ↓
Dead
    ↓
Ruins / Historical Remains

The Lab does not need all of these as frozen enum values immediately.

Hard semantic rule:

Dungeon death is permanent semantic death, not an ordinary reset.

Persistence / Resume Equivalence

Persistence is a first-class Dungeon Core contract, not a later Lab convenience.

Hard invariant:

Run to authoritative state S
    ↓
Save S
    ↓
Destroy runtime
    ↓
Load S
    ↓
Continue with the same future inputs/time
    ↓
Produce the same authoritative future

The persisted dungeon state must contain enough canonical truth to resume without reconstructing a different organism.

At minimum, persistence is expected to cover or reconstruct:

DungeonId
DungeonSeed
CreationTime
Current World Time / next due times
Lifecycle
Resources / capacities
Personality / strategy state
Current commitments
PlanRevision
Structural blueprint identity/state
Built topology
Future-plan state / invalidation state
Current Core Sanctum
Entrances
bounded dungeon-history summaries needed by decisions
references/provenance required for owned world objects

Transient Lab/UI/cache data is not save truth.

External Reference Lifetime Safety

Dungeon-owned references to ordinary world objects must be lifetime-safe.

Examples may eventually include:

Champion actor
generated weapon
entrance structure
trap mechanism
Core plinth
door / switch
treasure container

Hard invariant:

Every Dungeon-owned external reference must either resolve to the expected live object/lifetime or be explicitly represented as absent/destroyed/stale.

The Dungeon Core must treat disappearance or replacement of referenced world objects as normal authoritative input.

A stable ID alone must not imply that the referenced object still exists or still belongs to the same generation/lifetime.

The exact cross-system identity mechanism is deferred to the owning UltraSim architecture; the Dungeon Core contract is simply that references are validated rather than blindly trusted.

Save/load determinism should be proven in the earliest Dungeon Core checkpoint before the graph becomes large.

Bounded Time Advancement / Offline Catch-Up

A dungeon grows while nobody watches, but catch-up must not mean replaying every skipped live tick.

Hard rule:

Long elapsed time is integrated according to subsystem semantics; skipped high-frequency ticks are not replayed literally.

Conceptually:

Passive Essence
    integrate analytically over elapsed world time

Scheduled dungeon decisions
    execute at deterministic due times

Construction / relocation / cooldowns
    advance from authoritative deadlines

External visitor / damage / traffic pressure
    consume authoritative occurrences or summaries

Catch-up
    bounded by explicit work/admission budgets

If a dungeon is inactive for fourteen simulated months, the Core should not require millions of tiny updates to discover that passive Essence increased and several strategic decisions became due.

The same temporal model should power:

normal UltraSim dormant progression;

Lab 1000× / Max;

save/resume continuation;

headless long-horizon regression.

Catch-up work must distinguish compressible continuous progression from required discrete semantic work.

Compressible examples:

passive Essence accumulation
cooldowns
construction duration
continuous recovery/decay

These may be integrated analytically or with an explicitly proven equivalent coarse representation.

Required discrete semantic work includes things such as:

strategic dungeon decisions
room/floor completion
entrance opening
Core relocation
monster stampede
Champion/Boss death
Core attack
Core destruction

Budget exhaustion must not silently drop, approximate away, or forget required discrete semantic occurrences.

Instead:

Unprocessed required discrete work remains deterministic owed debt and is resumed later unless a separately proven equivalent summary representation exists.

The catch-up budget exists to bound work per activation/update, not to authorize semantic loss.

Transactional Dungeon Mutations

All meaningful dungeon changes should follow a validate-before-commit pattern.

Conceptually:

Choose / Generate Candidate
        ↓
Construct bounded DungeonMutation / PlanDelta
        ↓
Validate
    resources
    topology
    spatial legality
    capacity
    connectivity
    Core reachability
    search/work budgets
        ↓
COMMIT atomically
        ↓
Emit occurrence / accounting entry

Failure before COMMIT leaves authoritative dungeon state unchanged.

Persistence boundary:

Candidate / validation transaction
    transient
    never persisted

COMMITTED long-running action
    authoritative
    persisted

Examples of committed long-running authoritative state include:

BuildingRoom
PreparingEntrance
ExcavatingEntrance
MovingCore
ConstructingFloor
DecommissioningRoom

A save/load must never need to recover a half-validated pre-COMMIT DungeonMutation.

This applies especially to:

room construction;

floor construction;

entrance creation;

room decommissioning;

alternate-link construction;

partial replanning;

Core relocation;

monster/champion/boss investment;

treasure/reward spending;

assimilation;

repairs.

This mirrors UltraSim's broader Try/validate → COMMIT → consequence discipline.

Economic Conservation and Audit Ledger

Dungeon economics should be mechanically auditable, not merely visible in charts.

For DUN0, Essence should use an exact deterministic integer domain.

Do not use casual floating-point Essence accounting.

If later design needs fractional Essence, move deliberately to a deterministic fixed-point representation while preserving exact accounting semantics.

The initial contract is therefore:

Essence = integer units

so accounting identities can be asserted bit-for-bit.

For each accounting interval or transaction:

Closing Essence
=
Opening Essence
+ Passive
+ Activity
+ Harvest
+ External Assimilation
+ Salvage
- Construction
- Creature Investment
- Rewards
- Repair
- Other Spend

Every Essence mutation must carry a stable reason/category.

The Lab should continuously assert:

Opening
+ recorded income
- recorded spend
= Closing

within exact deterministic arithmetic.

Item/value accounting should also distinguish:

Dungeon-generated creation value
External acquired value
Exported value
Hoarded value
Redistributed value
Assimilated value
Salvaged value

This turns rules such as:

A dungeon may not create an item and profit by consuming its own item.

into provable conservation properties instead of informal balancing assumptions.

4. Dungeon Origins

Later UltraSim dungeon births may come from:

magical convergence;

battlefield deaths;

failed ritual;

cursed mine;

ancient Core awakening;

corrupted forest;

artifact activation;

geological breach;

dimensional fault;

authored/world-event definitions.

Dungeon Lab should support direct/manual creation for testing.

5. Dungeon Resources

Keep the economy legible.

Essence / Growth Points

Primary investment currency.

Earned from:

passive Core regeneration;

visitor entry/activity;

time spent exploring;

puzzle/trap interaction;

combat;

damage;

serious injury;

death;

assimilation of outside items/materials.

Spent on:

rooms;

floors;

expansion;

monsters;

champions;

bosses;

traps;

puzzles;

treasure;

RNG items;

repair;

control/sustainment infrastructure;

special events.

Control

How many creatures/constructs the dungeon can effectively command.

Example:

Monster Population = 60
Control Capacity   = 45

Sustainment

How many inhabitants the dungeon can support materially/magically.

A dungeon may control 60 but support only 40.

Space / Footprint

Developed active dungeon space.

May affect:

inhabitant capacity;

visitor throughput;

traps/puzzles;

reward capacity;

passive generation.

Stability / Core Health

Represents dungeon integrity and Core survival.

Do not overbuild this before concrete consumers exist.

6. Essence Income Model

The conceptual income families are:

PASSIVE
    ambient Core regeneration

ACTIVITY
    entrance / visit
    time exploring
    puzzle attempt
    trap interaction
    combat participation

HARVEST
    damage
    injury
    death

MATERIAL
    outside item/resource assimilation

The exact point values are content/tuning data.

A visit can be worth a few points. Damage can be worth some points. Death can be worth a great deal.

7. Passive Regeneration

The Core must generate Essence slowly even with zero traffic.

This prevents permanent economic deadlock.

A catastrophically depleted dungeon may need weeks/months to recover, but it can recover while alive.

8. Growth: Depth and Footprint

Growth should improve the dungeon no matter which useful direction it takes.

Depth

Depth may give nonlinear passive generation plus defensive value.

Illustrative only:

Depth 1    +1/day
Depth 2    +3/day
Depth 3    +7/day
Depth 4    +12/day
Depth 5    +18/day

Depth also places more architecture/encounters between visitors and the Core.

Footprint

Active rooms/space may give:

passive generation;

Control/Sustainment;

traffic capacity;

trap/puzzle capacity;

treasure/reward capacity.

Illustrative only:

1 room      +1
5 rooms     +2
11 rooms    +6

Structural vs Active Footprint

Distinguish:

Structural Footprint
    all physically existing/excavated dungeon space

Active Footprint
    currently participating in the dungeon organism

An abandoned wing can remain physical while no longer contributing full dungeon benefits.

9. Dungeon Personality

Personality should modify decision weights rather than hard-script behavior.

Candidate dimensions:

Aggression
Greed
Patience
Predation
Curiosity
Deception
Hospitality
Territoriality
Puzzle Affinity
Monster Affinity
Trap Affinity
Treasure Generosity
Expansion Drive
Depth Preference
Self-Preservation
Vengefulness
Adaptability
Pride

The implementation should begin with a smaller proven subset.

Example archetypes:

Aggressive Predator

high Aggression;

high Monster Affinity;

low Patience;

lower Hospitality.

Expected:

heavy monster spending;

high mortality;

raids;

rapidly increasing Threat.

Puzzle Patron

high Puzzle Affinity;

high Patience;

moderate/high Treasure Generosity;

high Self-Preservation.

Expected:

traffic-oriented survival;

low/moderate mortality;

challenge investment;

low Threat;

long life.

Greedy Bait Dungeon

high Greed;

reward-focused;

adaptive;

high Deception.

Expected:

optimizes return on reward spend;

moves rewards deeper when farmed.

Proud Fortress

high Pride;

high Self-Preservation;

high Depth Preference;

champion/boss bias.

Expected:

concentrated strong encounters;

deep Core defense.

10. Dungeon Decision Architecture

Current state
+ recent history
+ personality
+ resources
+ pressure
+ commitments
        ↓
Bounded candidate generation
        ↓
Utility scoring
        ↓
Commitment / hysteresis
        ↓
Chosen investment/action
        ↓
Authoritative Dungeon Core mutation

The dungeon must not evaluate every possible action continuously.

Suggested first-pass cadence:

Encounter reactions:
    event-driven

Minor allocation:
    every few in-game hours

Growth decision:
    every 6–24 in-game hours

Strategic review:
    daily / every few days

Plan review:
    weekly or on invalidation

These are tunable and must not depend on render FPS.

11. Economic Self-Assessment

Useful rolling inputs:

Treasure Expenditure
Essence Income
Visitor Count
Visitor Time
Visitor Deaths
Monster Deaths
Champion Deaths
Boss Deaths
Clear Frequency
Core Approach Frequency
Core Damage
Extraction Pressure
Threat Trend

Possible derived summaries:

Traffic             Low / Normal / High
Mortality           Low / Normal / High
Extraction Pressure Low / High / Severe
Profitability       Positive / Neutral / Negative
Core Exposure       Low / Rising / Severe

These drive decisions.

There should be no bespoke:

if PlayerFarmingDungeon:
    become_harder()

Instead farming naturally produces bad ROI and high extraction pressure.

12. Monster Population Pressure

Track:

Population
Control Capacity
Sustainment Capacity

If population exceeds capacity, candidate responses may include:

cull;

absorb;

combine weak monsters into Elite;

expand habitat/control;

raid;

intentionally expel;

lose control;

stampede.

Personality decides which response is attractive.

Monster Stampede

Should emerge from unstable state rather than arbitrary timer.

Possible pressure:

Population > Control;

Population > Sustainment;

food/resource shortage;

dungeon damage;

chaotic personality;

prolonged neglect.

The Lab should expose:

Stampede Risk
Control Deficit
Sustainment Deficit
Overpopulation

13. Loot as Dungeon Investment

Treasure is not free.

Example:

Dungeon spends 100 Essence on loot
Visitors extract it
Dungeon earns 12 Essence

The strategy is losing.

The dungeon may respond by:

reducing reward frequency;

moving rewards deeper;

increasing traps;

increasing monster count;

creating elites;

sealing shortcuts;

increasing puzzle difficulty;

closing rooms;

saving for a deeper floor.

This is the dungeon economy, not a reset timer.

Adjacent-System Compatibility Scope

The following item-generation and structural/building details exist in this document as integration constraints and proving fixtures, not as a requirement to finish UltraSim's entire final Item or Construction architecture before Dungeon Core can proceed.

Dungeon Core should depend on narrow earned seams such as:

DungeonItemAdapter
WorldConstraintProvider
StructuralRealizationSink

Dungeon Lab may provide deliberately bounded implementations sufficient to prove dungeon economics and geometry.

Later UltraSim systems replace those adapters without changing the organism's semantics.

14. Item Reclaim and Assimilation

The dungeon may reclaim items left behind by dead/retreated adventurers.

Possible choices:

Redistribute

Give equipment to monsters.

Hoard

Place items in treasure rooms.

Bait

Expose attractive items to draw traffic.

Assimilate

Destroy item and convert some value into Essence.

Hard invariant:

A dungeon must not create an item and then profit by consuming its own generated item.

Dungeon-created items should return less than or at most their original creation investment.

Outside matter can be genuinely profitable.

Player-facing item-loss mercy/recovery rules belong outside Dungeon Core.

15. Procedural Dungeon Items

Legitimate RNG item sources include:

chests;

Champions;

Elites;

minibosses;

bosses;

deep-floor rewards;

rare dungeon events.

A Champion’s item should be generated when the Champion is created, used by the Champion, and become loot only if ownership changes after defeat.

Generated items should use the same future item grammar as crafted items:

Base item
+ material
+ craftsmanship
+ inherited material effects
+ intrinsic effects
+ sockets
+ socket occupants

No separate “loot-only item” architecture.

16. Intrinsic Effect Capacity and Sockets

Intrinsic effect capacity

Hard conceptual maximum:

16 intrinsic effect records

Higher slot counts are exponentially rarer.

Duplicate effects remain separate records even when presentation aggregates them.

Example:

Slot 1: +10 Strength
Slot 2: +10 Strength
Slot 3: +10 Strength

Display:
    +30 Strength

Sockets

Sockets are separate.

A socket consumes one host socket, but the inserted Jewel/Gem/Rune may contribute several effects without consuming extra intrinsic host-effect slots.

Sockets should be considerably less absurdly rare than extreme intrinsic capacity.

17. Effect Families and Magnitudes

First-pass effect families:

Attribute
Skill
Offense
Defense / Resistance
Sustain / Regeneration
Speed
Utility
Special / Proc / Rule Change

Orthogonal effect shape:

Flat
Percent
Range
Periodic
Conditional
Conversion
Triggered
Aura
RuleChange

Origin:

Material Inheritance
Craftsmanship
Blessing
Enchantment
Alchemy
Rune
Socket Item
Dungeon Generation
Other World Generation

Suggested discussed magnitude bounds:

Attributes:
    +1 to +10 per slot

Skills:
    +1 to +10 per slot

Resistance:
    +1% to +25% per slot

Resource Capacity:
    Health / Stamina / Mana
    approximately +1 to +100 per slot

Higher values should become exponentially rarer.

Cumulative magnitude roll

Once an effect type is chosen:

+1 guaranteed
try +2
try +3
try +4
...
stop on first failure

Different families use different continuation curves.

18. Dungeon Generator Architecture

Dungeon generation is now a second consumer of the subterranean spatial concepts first proven by Mine.

Where Mine and Dungeon require the same spatial invariant, prefer reusing or extracting the Mine-proven implementation rather than independently inventing a second planner. Where Dungeon requires organism-specific semantics, keep those above the shared spatial layer.

In particular:

~~~text
Mine-proven / candidate shared spatial machinery
    connected opportunity volumes
    connectors
    spatial legality / CanFit
    vertical transitions
    built/future topology
    partial replan
    incremental realization

Dungeon-specific generator semantics
    room archetypes/purposes
    Core-capable sanctums
    defensive rings
    decoys
    encounter/treasure/puzzle intent
    personality-biased planning
~~~

The Dungeon generator should have two Dungeon-facing layers.

Logical Plan

What the dungeon wants to become:

room graph;

room archetypes;

branches;

loops;

dead ends;

floor transitions;

major landmarks;

Core approach.

Spatial Plan

How that logical graph fits into available world space:

planning cells;

room footprints;

floors;

connectors;

blocked nodes;

growth frontier.

19. Dungeon Macro Planning Cell

Use approximately:

2m × 2m horizontal macro cell
~3m nominal vertical band

This is generator graph paper only.

It is not the UltraSim construction grid.

20. Construction-System Compatibility

UltraSim construction remains much more flexible:

freeform and snapped structural placement;

ordinary building pieces of varying dimensions;

fine detail/object snap around 0.1m;

floor/wall pieces can be smaller/larger than dungeon macro cells.

A 2×2 dungeon floor model can simply be an ordinary building piece.

The generator emits space/topology intent, not a requirement to spawn one model per macro cell.

21. Room Shapes

A Room is a connected set of macro cells, not a rectangle primitive.

Examples:

2×2
2×3
3×5
L
T
cross
irregular orthogonal / Doom-like

Example:

..XXX..
..XXX..
XXXXXXX
XXX.XXX
XXXXXXX

Hallways are constrained narrow connected regions.

Minimum initial width:

1 macro cell

Length can vary.

22. Room Archetypes

Archetypes constrain usable shape/behavior rather than selecting exact art.

Examples:

Entrance
Hall
Combat
Puzzle
Treasure
Monster Den
Champion
Boss
Stairs
Core
Secret
Control
Arcane Well

Possible definition properties:

MinCells
MaxCells
MinEntrances
PreferredEntrances
ShapeComplexity
AllowedDepth
RequiredClearance
TrafficBias

23. Built Adjacency Invariant

Hard rule:

Every newly built room must connect to already-existing built dungeon space.

A planned room cannot simply appear 30m away.

Growth happens through legal frontier connections.

24. Growth Frontier

Maintain legal attachment points such as:

unused boundary;

corridor end;

stair-down slot;

unfinished planned connection.

The dungeon chooses one of these when expanding.

25. Vertical Connectors

Logical vertical connectors should be first-class:

Stairs
Ramp
Ladder
Shaft
Pit
Portal

Concept:

VerticalConnector
    SourceRoom
    SourceCell(s)
    TargetFloor
    TargetRoom / PlannedNode
    ConnectorType

New floors must connect to built topology.

Dungeon Entrances

A dungeon may have one or many entrances.

Multiple entrances are strategic tools rather than generator accidents.

Possible benefits:

increased visitor traffic;

higher exploration throughput;

easier bait/reward access;

alternative routes;

emergency monster egress;

regional connectivity;

deliberate shortcut behavior.

A tolerated dungeon may eventually function as a risky shortcut between surface locations if adventurers/civilians can traverse from one entrance to another.

Entrances are part of the structural opportunity blueprint and/or may be added through later legal expansion opportunities.

Entrance Creation Must Be Physical

A new entrance must not simply pop into existence as a finished building.

Possible entrance emergence modes:

Excavated Opening / Stair

Dungeon builds upward stair/ramp/tunnel
    ↓
reaches near-surface boundary
    ↓
final excavation / controlled collapse
    ↓
opening becomes exposed

This can appear suddenly at the final breakthrough while still having a real construction history.

Constructed Surface Entrance

For a mausoleum, ruin, tower, shrine, or other building-like entrance:

prepare underground connection
    ↓
construct/emerge surface structure over time
    ↓
complete access opening

The presentation may show the structure slowly raising/growing/emerging from the ground or being physically assembled according to the dungeon's theme.

The authoritative rule is simply:

A building-like entrance has construction time/state; it is not materialized instantly as a finished structure.

Possible lifecycle:

Planned
Preparing
Emerging / Excavating
Open
Sealed
Destroyed
Abandoned

Entrance growth must obey ordinary DungeonMutation validation and world-space legality.

Reusable Dungeon Reachability Query

Core reachability, decommissioning, collapse, entrance creation, alternate-link construction, and relocation all need the same graph reasoning.

Do not bury this logic inside Core relocation.

Provide a reusable bounded query over dungeon topology.

At minimum distinguish:

Structurally Reachable

A topological route exists through built dungeon space/connectors, ignoring temporary door state.

Mechanically Satisfiable

A legal route can exist when required mechanics are satisfied.

Examples:

puzzle can be solved;

switch can be activated;

lock has a valid key/mechanism source;

illusion can be discovered/dispellable;

trap can be survived/bypassed according to its rules.

Currently Accessible

A route is traversable under the current momentary states:

doors currently open/unlocked;

bridges currently extended;

no temporary collapse blocking the route.

The Core invariant is primarily:

At least one valid entrance/root has a mechanically satisfiable route to the true Core Sanctum.

It is not required that the route be currently open at every moment.

A locked puzzle door therefore does not invalidate the dungeon if the puzzle has a satisfiable legal solution.

Bounded Mechanic Satisfiability Contract

The reachability layer must not become a general gameplay/puzzle solver.

Mechanics expose a bounded prerequisite description to Dungeon reachability.

Example:

Gate 42 requires:
    Switch A activated
    AND Key B obtainable

Switch A:
    reachable from Entrance 1

Key B:
    obtainable in Room 18

The reachability layer evaluates finite prerequisite relationships such as:

RequiresSwitch
RequiresKey
RequiresRoomState
RequiresMechanicState
RequiresOneOf(...)
RequiresAllOf(...)

It does not simulate or solve arbitrary mechanic gameplay such as:

Sudoku;

arbitrary physics puzzles;

NPC dialogue;

arbitrary script execution;

unbounded search over gameplay state.

Those systems must project a bounded satisfiability contract if they want to participate in Core reachability proof.

The query should support:

Root/Entrance Set
Target Room / Core
Traversal Policy
Mechanic Satisfaction Policy
Work Budget

and return bounded diagnostic evidence/path information for the Lab.

26. Long-Horizon Structural Blueprint — Opportunity, Not Destiny

At birth, perform the expensive broad planning work once.

Conceptually:

Dungeon Seed
+ Origin
+ Personality
+ World Constraints
        ↓
EXPENSIVE BIRTH GENERATION
        ↓
Long-Horizon Structural Opportunity Blueprint

The blueprint may contain far more potential rooms than the dungeon will ever build.

For example:

Current built rooms:       8
Future opportunities:    140

That is acceptable because Future rooms are compact planning records, not physical world entities.

The blueprint should distinguish:

STRUCTURAL PLAN
    legal/likely future spatial opportunities
    adjacency
    branch skeleton
    loops
    room footprints
    vertical transitions
    entrances / potential entrances
    Core-capable sanctums
    future landmarks
    alternate continuations

SEMANTIC INTENT
    what a future opportunity is currently intended to become
    Combat
    Puzzle
    Treasure
    Habitat
    Champion
    Decoy
    etc.

Structural opportunity is relatively stable and expensive to generate.

Semantic intent is cheap and adaptive.

A room opportunity planned as Combat may later become Puzzle, Treasure, Habitat, or remain unused without invoking spatial generation if the underlying opportunity remains legal.

Hard performance/design invariant:

Ordinary dungeon growth consumes already-planned opportunities and must not require fresh procedural spatial search.

The generator therefore has three routine modes:

BIRTH
    generate broad deterministic opportunity space

NORMAL GROWTH
    no spatial generator
    choose/validate/consume an existing planned opportunity

INTERFERENCE / EXHAUSTION
    bounded deterministic partial replan or plan extension

A long-lived dungeon may eventually consume most of its original opportunity space. In that exceptional case, the plan may be extended from valid frontiers.

Pre-generated structure is potential, not destiny.

The dungeon's decisions determine which potential becomes physical history.

27. Replanning

If future space becomes invalid:

Built Topology
+ current constraints
+ Dungeon Seed
+ Plan Revision
+ Personality
+ remaining goals
        ↓
deterministic replan

Hard invariants:

Replanning may alter future potential, never rewrite or move already-built rooms.

Unaffected Future plan remains intact whenever practical; invalidate and regenerate only the affected subtree/volume/frontier.

Example:

Built:
A──B──C

Future:
      └──D──E──F

If D/E/F becomes invalid, retain A/B/C, discard or invalidate only the affected Future branch, and deterministically generate a replacement continuation from a legal frontier.

Inputs may include:

Original Seed
PlanRevision
Built Graph
Built Transforms
Blocked Volumes
Current Frontiers
Personality
Current Strategy

28. Room Lifecycle and Scrapping

Possible room states:

Planned
Reserved
Building
Built
Decommissioning
Stripped
Collapsed
Removed
Blocked
Invalid

Initial implementation may collapse these.

Scrapping is an explicit dungeon action.

Possible outcomes:

Reclaim

Consume active magical infrastructure.

Strip

Physical room remains but stops being active dungeon space.

Collapse

Convert to blocked rubble/ruin.

Repurpose

Change room function without full demolition.

Build cost must not be fully recoverable by default.

Example:

Build Cost 30
Salvage    12

29. Connectivity During Removal

Removing a leaf is easy.

Removing a transit/articulation room may disconnect a branch.

Possible policy:

disallow;

recursively decommission dependent branch;

build alternate link first;

preserve minimal corridor.

Dungeon Lab must visualize articulation dependencies.

Hard graph invariant:

Every Active/Built dungeon room must be reachable from at least one valid dungeon root/entrance unless it is explicitly marked Isolated, Abandoned, Collapsed, or Ruined.

Articulation/decommission mutations must validate this invariant before COMMIT.

30. Shrinking to Grow Down

A valid strategy:

Depth 3
17 rooms
Essence 42

Floor 4 cost 120

Dungeon may:

wait;

reduce treasure;

increase danger;

scrap rooms for immediate partial return.

Temporary horizontal shrinkage can finance a stronger long-term depth investment.

31. Structural Realization

Dungeon Core owns:

room volume
connections
closed boundaries
open boundaries
vertical links

A structural realization adapter maps that into ordinary UltraSim building pieces.

32. Wall Placement Convention

Canonical wall semantics:

The wall anchor lies on the structural boundary and thickness extends completely along the wall’s local inward/positive normal.

Do not center thickness on the boundary.

Example:

Anchor X = 0
Thickness = 0.1
Facing +X

Occupied:
0.0 → 0.1

Rotate 180°:

-0.1 → 0.0

Benefits:

no half-wall trespass;

predictable replacement;

clean shared room boundaries;

clean land/property boundaries;

orientation determines occupied side.

33. Shared Wall and Independent Room Faces

Prefer one structural wall with independently addressable faces.

Concept:

Room A
  Face A
    ↓
[finish] [structural wall] [finish]
                              ↑
                            Face B
                              Room B

This supports:

wood finish in one room;

brick/plaster on the other;

one collision/structural wall;

independent painting.

34. Greedy Meshing

Greedy meshing is only a presentation/cache optimization.

Authoritative faces remain separate.

Example:

Face 1001
Face 1002
Face 1003
Face 1004

may render as one combined surface.

Paint Face 1003 and only affected render sections split/rebuild.

Merge rendering aggressively; never merge authoritative identity just because rendering can.

35. Paint/Surface Compatibility

Future building controls may use:

Normal

Paint targeted face/section.

Ctrl

Flood-fill connected surfaces that currently share the same material/texture ID.

Alt+Ctrl

Paint all relevant faces inside the same Room/Volume regardless of existing finish.

Room identity and independent face identity must therefore be preserved.

36. Future Region-System Compatibility

Dungeon Core should remain ignorant of Custom-Region semantics.

It should preserve enough identity for a future adapter:

DungeonId
RoomId
ParentDungeonId
Actual Built Bounds / Volume
Lifecycle
Semantic Tags

Later projection:

Dungeon Region
    ├── Room Region
    ├── Puzzle Room Region
    └── Core Room Region

Region rules, house-room permissions, rentals, containment overrides, and Town-Houses-style rights remain separate systems.

37. Dungeon Lab Composition

Dungeon Core
    real dungeon logic

Dungeon Generator
    logical/spatial plan
    growth/replan

Synthetic Test Simulation
    visitors
    simplified encounters
    external pressure

Dungeon Viewer
    reusable 3D diagnostic consumer

Dungeon Diagnostics
    inspectors
    logs
    charts
    decision traces

Scenario / Batch Runner
    seeds
    traffic matrices
    personality matrices
    regressions

The Lab is a consumer composition around the real Core.

It must not become a second implementation of dungeon semantics.

Reusable Dungeon Debug Projection

The Viewer should not reach directly into mutable Dungeon Core internals or synthetic-Lab-only objects.

Core/Diagnostics should expose an immutable/bounded inspection projection, conceptually:

DungeonDebugSnapshot
    Dungeon identity
    resources
    personality/goal summary

    built rooms
    future opportunities
    invalid/blocked opportunities
    building/decommissioning state

    connections
    entrances
    Core / decoys
    growth frontiers

    reachability paths/results
    recent decisions
    threat / Core Exposure
    bounded metrics

The same projection can eventually support:

Dungeon Lab 3D Viewer
        ↓
DungeonDebugSnapshot

and later:

UltraSim in-game developer/admin Dungeon Viewer
        ↓
authorized Game debug disclosure
        ↓
DungeonDebugSnapshot / disclosed projection

The viewer is therefore designed for reuse from the start even if the first implementation lives under Tools/DungeonLab/Viewer.

In-Game Debug Reuse and Disclosure

The eventual in-game debug viewer must not cause secret dungeon information to be published to ordinary players.

Lab mode may inspect everything.

Real UltraSim must use Game-owned authorization/disclosure.

Do not:

send full blueprint/Core location to every client
    ↓
hide it in Godot

Prefer:

Dungeon diagnostics truth
        ↓
Game debug disclosure / authorization
        ↓
authorized developer/admin client
        ↓
reusable 3D Viewer

The visualizer remains presentation-only.

It does not calculate dungeon validity, Core reachability, or strategy.

It renders diagnostic truth produced by authoritative systems.

38. Synthetic Adventurer Model

Keep deliberately simple.

Example:

CombatPower
Defense
PuzzleSkill
TrapSkill
Curiosity
Greed
Courage
Health
RetreatThreshold
ExplorationBias
LootInterest

Optional archetypes:

Novice;

Veteran;

Treasure Hunter;

Puzzle Solver;

Reckless Fighter;

Cautious Explorer;

Guild Party;

Hero/Core Hunter.

Do not recreate the full Sim Core here.

39. Synthetic Actor State Machine

Enter
    ↓
Choose route
    ↓
Encounter
    ├── Fight
    ├── Puzzle
    ├── Trap
    ├── Treasure
    ├── Secret
    └── Empty
    ↓
Reassess
    ├── Continue
    ├── Retreat
    └── Die

Continue/retreat can use:

Health
Greed
Curiosity
Known Danger
Known Reward
Depth
Party State
Retreat Threshold

40. Traffic Profiles

Required presets:

Ignored

Near-zero traffic.

Low

Occasional visitors.

Normal

A few parties/visitors per day.

Busy

High stable traffic.

Farmed

High competence, low mortality, high extraction.

Hostile Guild

Coordinated Core-destruction parties.

Hero Pressure

Rare strong visitors.

Seasonal

Traffic changes with calendar/time.

41. Synthetic Reputation Stub

Lab-only model:

KnownThreat
KnownReward
KnownDepth
KnownMortality
KnownCompletionRate

Survivors update it.

Traffic responds.

Later UltraSim replaces this with real:

History
→ actor-relative claims
→ knowledge
→ real decisions

42. Threat vs Reputation

Keep separate.

Threat

Objective effect on surrounding society.

Inputs:

escaped monsters;

raids;

traveler deaths;

nearby destruction;

regional danger.

Reputation

What the synthetic society believes.

Inputs:

survivors;

guild reports;

successful expeditions;

observed rewards.

43. Core Room and Core Sanctum

The dungeon Core should live in an explicit Core Room / Sanctum, not as an abstract point hidden nowhere.

Recommended first-pass concept:

a small dedicated room or chamber;

typically compact and defensible;

contains a clear plinth, pedestal, post, or display fixture;

the physical Core rests visibly on that fixture;

the room has at least one real doorway/opening into the surrounding topology.

This room is the dungeon's semantic heart.

The structural realization may begin very simply in Lab mode:

small room
surrounding walls
one doorway
plinth at center or rear
visible Core on plinth

The exact art treatment can evolve later.

44. Core Accessibility Invariant

Hard gameplay/design invariant:

The Core must always remain theoretically reachable and destructible.

The dungeon may hide or protect the Core aggressively, but it must not place it in a permanently impossible location.

Allowed protection examples:

behind a very difficult puzzle;

behind a locked door that requires a distant switch or mechanism;

behind illusion walls;

past dangerous trap corridors or pits;

behind composite multi-step gating;

behind deep branching paths and layered defenses.

Not allowed:

no legal path exists at all;

permanent world-state softlock with no satisfying route;

Core moved into sealed inaccessible void.

A Core can be hidden, gated, misleading, or punishing.

It cannot be truly unreachable.

45. Core Concealment and Defense Layers

The dungeon should be able to treat Core protection as a strategic design problem.

Useful protection layer families:

Distance

more floors between entrance and Core;

deeper branch placement;

decoy wings.

Obfuscation

false treasure branches;

illusion walls;

concealed passages;

misleading route presentation.

Access Gating

puzzle locks;

switch locks;

key-like mechanisms;

multi-room progression chains;

hidden triggers.

Hazard

traps;

pits;

gauntlets;

elite guards;

boss chambers;

collapse hazards.

Access Control by Intent

The route is still legal, but the cost/risk/knowledge burden is high.

This should create a spectrum between:

easily found Core

and:

technically reachable but very difficult to locate and survive

46. Core Relocation

The Core Room should be allowed to shift over time.

This can be a real strategic behavior when the dungeon feels exposed.

Possible relocation directions:

sideways;

deeper/downward;

upward if that somehow improves strategy;

across branches.

The important rule is that relocation is not a cosmetic teleport.

It should be a meaningful dungeon action.

Recommended first-pass requirements:

the target Core Room must be part of legal built topology or a valid buildable plan opportunity;

a new Core Sanctum may need to be prepared/built first;

Core relocation should cost resources and time;

the old Core Room should transition to another state:

abandoned sanctum,

decoy sanctum,

elite guard room,

treasure room,

puzzle room,

collapsed/stripped site.

Hard invariant:

At no point should the dungeon exist with a Core that is physically nowhere or in two true locations at once.

A practical transactional sequence is:

Prepare new sanctum
    ↓
Validate reachability and legality
    ↓
Commit Core transfer
    ↓
Downgrade old sanctum

This should be relatively rare and strategically motivated.

47. Core Exposure and Relocation Pressure

The dungeon should track not just objective Threat, but specifically Core Exposure.

Useful inputs:

number of visitors reaching near-Core depth;

number of visitors entering Core-adjacent rooms;

Core chamber discovery frequency;

Core damage attempts;

successful boss/champion clear rates near the Core;

shortcut discovery rates;

information leakage via survivors.

Possible derived states:

Core Exposure: Low / Rising / High / Critical

High Core Exposure may motivate:

more defenses;

route obfuscation;

decoy investment;

relocation;

sealing shallow approaches;

deeper expansion before more treasure spending.

48. Core Room in the Generator / Blueprint

The blueprint should reserve/provide Core-capable opportunities.

That means the plan is not just generic rooms plus a single fixed endpoint.

A dungeon's long-horizon structural opportunity space should include:

one current active Core Room;

future possible sanctum candidates;

possible decoy sanctum candidates;

likely defensive rings around the Core;

possible relocation branches.

This preserves adaptation without requiring fresh full generation whenever the dungeon wants to defend itself better.

Conceptually:

STRUCTURAL PLAN
    includes Core-capable nodes / sanctum opportunities

SEMANTIC PLAN
    decides which of those is the true active Core
    which are decoys
    which are treasure / boss / puzzle / guard spaces

That fits the "opportunity, not destiny" model very well.

49. Core Visual Identity

The Core should be visually readable as a special dungeon object.

At minimum, the Lab should render it as a distinct artifact rather than another cube.

A useful future-facing direction is:

profile/personality-influenced shape;

profile/personality-influenced color identity;

visually distinct silhouette per broad dungeon temperament.

Illustrative ideas:

spiky / starburst red form for hostile or violent tendencies;

smooth green sphere for greed/hoarding themes;

faceted gold/gem-like form for puzzle/treasure-oriented personalities;

cold blue crystalline form for patient/arcane dungeons.

This should begin as cosmetic/presentation identity only.

The gameplay semantics remain driven by the real dungeon state.

50. Core Visual Evolution (Later / Nice-to-Have)

Later, the Core may visually change over time as the dungeon's history and temperament evolve.

Possible visual drivers:

accumulated deaths;

hoarded Essence;

aggression;

puzzle focus;

corruption;

age;

instability;

damage.

Potential presentation techniques:

color shifts / ramps;

vertex color changes;

added spikes/protrusions;

glow intensity;

surface cracking;

pulsing patterns.

Examples:

redder / more violent after heavy harvest and aggression;

greener or denser after prolonged hoarding;

more faceted/ornate with puzzle specialization;

cracked or dimmed after repeated assaults.

This is explicitly a later polish feature and should not block the functional Core implementation.

51. Core-Related Tooling

Dungeon Lab should expose Core-specific diagnostics.

Useful views/fields:

Current Core Room
Sanctum Age
Core Exposure
Discovery Count
Near-Core Visitor Count
Core Damage Attempts
Current Defensive Layers
Relocation Candidates
Relocation Cooldown / Cost
Decoy Sanctums

Useful overlays:

highlight the true Core room;

highlight decoy sanctums;

show shortest current legal route(s) to the Core;

show recent traffic heat near the Core;

show layers of protection between entrance and Core.

These are critical for debugging whether "hide/protect the Core" behavior is actually working.

52. Dungeon Death and Ruins

Core destruction:

Dungeon.State = Dead

Then:

passive regen stops;

generation stops;

adaptive planning stops;

no new monsters/rewards;

existing items remain;

existing monsters remain according to world/test rules;

physical structure remains;

site becomes ruins/history.

No ordinary respawn.

Main Dungeon Lab Views

The Lab does not require a separate 2D viewer.

The dungeon exists in true 3D space, and the primary visual development tool should therefore be one flexible 3D Structure Viewer with debug volumes, outlines, ghost plans, overlays, and point-of-interest navigation.

A future orthographic/top-down camera mode may be useful, but it should remain a camera/view option over the same 3D truth rather than a separate authoritative visualization pipeline.

3D Structure Viewer

Required capabilities:

free-fly camera;

orbit selected dungeon/room/object;

zoom from whole-dungeon view to room/object detail;

jump/hop to:

Core;

entrance;

selected room;

growth frontier;

invalid plan node;

monster/visitor;

recent event;

floor isolation;

vertical cutaway / clipping;

hide/show realized structural geometry;

show full potential footprint from the structural blueprint.

The viewer should be able to zoom far enough out to understand:

entire built dungeon
+
entire planned structural opportunity space
+
invalid / rebuilding sections
+
entrances
+
Core / decoys

without converting the dungeon into a separate 2D representation.

3D Room / Volume State Overlays

Rooms and planned volumes should have clear outline/ghost representations for states such as:

Future / Planned
Reserved
Building
Built / Active
Invalid / Waiting for Replan
Blocked
Decommissioning
Stripped / Inactive
Collapsed / Destroyed
Ruined

Exact colors/styles remain presentation policy.

The important requirement is immediate visual differentiation.

Planned topology may be rendered as:

wire volumes;

translucent boxes/prisms;

outlined cell masks;

ghost structural geometry;

connection lines.

Built topology remains solid.

Dungeon Inspector

Show:

DungeonId
Seed
Age
Lifecycle
PlanRevision

Essence
Passive Regen
Recent Income
Recent Spend
Accounting Balance Check

Depth
Rooms
Active Footprint
Structural Footprint

Entrances
Monster Population
Control
Sustainment

Threat
Reputation
Traffic

Current Core Room
Core Exposure
Current Goal
Current Commitment
Next Strategic Due Time

Time-Series / Metrics View

Plot:

Essence;

income by source;

spend by category;

rooms;

floors;

entrances;

active footprint;

monster population;

Control;

Sustainment;

visitors;

average visitor depth;

deaths;

monster deaths;

Champion/boss deaths;

loot created;

loot extracted;

Threat;

reputation;

Core approaches;

Core damage;

Core relocations;

profitability.

This may be a side panel/window associated with the 3D viewer rather than another world view.

Blueprint / Potential-Footprint Overlay

The 3D Structure Viewer should be able to display simultaneously:

Actual Built
Planned Future
Invalid Future
Blocked
Decommissioned / Collapsed

A developer should immediately see why replanning happened.

53. Dungeon Decision Inspector

Example:

Current Goal:
    Improve profitability

Inputs:
    Treasure Spend: High
    Essence Income: Low
    Extraction: Severe
    Mortality: Low
    Core Exposure: Low

Candidates:
    Restock Treasure         14
    Spawn Monsters           32
    Add Puzzle               47
    Add Traps                61
    Save for Floor 4         73

Selected:
    Save for Floor 4

Record bounded recent decision history.

54. Room Inspector

Show:

RoomId
Archetype
Floor
Lifecycle
Built Time

Build Cost
Salvage Value

Traffic
Combat Count
Puzzle Attempts
Deaths
Treasure Spend
Essence Income

Lifetime ROI
Recent ROI

Connections
Articulation / dependency state

This is required for understanding scrapping behavior.

55. Synthetic Actor Inspector

Show:

Type
Health
Combat Power
Puzzle Skill
Greed
Courage

Current Room
Current Goal
Known Reward
Known Threat

Loot
Damage Dealt
Damage Taken
Depth Reached
Retreat Decision

56. Item Inspector

Show:

Persistent Test ID
Origin
Source Dungeon
Creation Time
Base Item
Intrinsic Effect Capacity
Effects
Socket Count
Socket Contents
Aggregated Display
Assimilation Value
Creation Essence Cost

Useful for conservation/provenance tests.

57. Event / Occurrence Log

Bounded log of meaningful events:

Dungeon born
Room built
Floor opened
Room scrapped
Entrance preparation started
Entrance opened
Entrance sealed/destroyed
Plan invalidated
Plan revised
Monster spawned
Champion spawned
Boss spawned
Treasure created
Treasure extracted
Visitor entered
Visitor retreated
Visitor died
Monster escaped
Stampede
Core approached
Core damaged
Core relocation started
Core relocated
Core destroyed
Item assimilated

Do not log every movement step by default.

58. Simulation Speed Controls

Required:

Paused
1×
10×
100×
1000×
Max

Step:

Step Encounter
Step Dungeon Decision
Step Hour
Step Day

At low speed, observe actors.

At high speed, observe years/decades.

59. Deterministic Controls

Expose:

Dungeon Seed
Traffic Seed
Synthetic Society Seed
Simulation Version
Test Profile Version

Use addressed deterministic randomness by stable semantic domain wherever practical.

Examples:

DungeonSeed
+ PlanRevision
+ PlannedRoomId
+ "RoomShape"

DungeonSeed
+ DungeonDecisionDueTime
+ "StrategicTieBreak"

DungeonSeed
+ ItemId
+ "IntrinsicEffect:3"

Avoid making important results depend on one implicit global RNG call sequence.

Visualization, logging, diagnostics, and unrelated new random consumers must not perturb authoritative dungeon evolution.

60. Batch / Headless Runner

Required for serious tuning.

Example:

100 dungeon seeds
50 simulated years
Aggressive profile
Normal traffic

Output:

JSON/CSV metrics;

deterministic checkpoint hashes;

failures;

lifespan;

mean depth;

mean rooms;

mortality;

threat;

profitability.

61. Personality Matrix Runner

Same seed/traffic, different personalities.

Compare:

lifespan;

depth;

rooms;

Essence;

threat;

deaths;

visitor traffic;

ROI;

Core attacks;

reward spend;

overflow events;

plan revisions.

Goal:

prove personalities produce materially different organisms.

62. Traffic Matrix Runner

Same dungeon/personality under:

Ignored
Low
Normal
Busy
Farmed
Hostile Guild

Goal:

prove strategy adapts to different pressure.

63. Metrics

Resources

Essence current;

passive income;

activity income;

harvest income;

assimilation income;

spend total;

spend by category;

net income;

rolling ROI.

Growth

room count;

active rooms;

depth/floor count;

active footprint;

structural footprint;

room builds;

room removals;

salvage recovered;

plan revisions;

invalid nodes;

planned opportunity count;

entrance count;

entrance construction events.

Population

monsters;

Control;

Sustainment;

overpopulation;

culls;

upgrades;

raids;

expulsions;

stampedes.

Visitors

entries;

visit duration;

average depth;

puzzle attempts;

treasure found;

retreats;

injuries;

deaths;

Core approaches;

Core kills.

Combat

damage dealt to visitors;

damage dealt to monsters;

monster deaths;

Champion deaths;

boss deaths.

Loot

reward spend;

items generated;

items extracted;

outside items acquired;

redistributed;

hoarded;

assimilated;

imported/exported value.

Threat

objective Threat;

synthetic reputation;

Core Exposure;

Core relocation count;

raids;

escapes;

synthetic outside casualties;

guild response count.

Performance

simulation steps/sec;

dungeon decisions/sec;

generation duration;

replan duration;

room placement duration;

item-generation duration;

memory per dungeon;

memory per room;

event volume;

batch throughput;

catch-up duration and work consumed;

save/load duration;

persistence-resume hash equivalence failures;

accounting invariant failures;

HostAdapter lifecycle/update overhead;

standalone-vs-host parity failures;

debug snapshot generation time;

debug snapshot size;

3D realization time.

Track mean/p95/p99/max for expensive operations.

64. Boundedness Requirements

Every generator/search operation must have explicit budgets:

Max room candidates per decision
Max placement attempts
Max path nodes
Max replan attempts
Max blueprint revisions per period
Max room-shape complexity
Max floor extent
Max active rooms
Max depth
Max synthetic actors
Max event-log records
Max recent decision records

On exhaustion:

defer;

choose fallback;

revise plan;

fail gracefully.

Bounded memory is not enough; work must also be bounded.

65. Test Plan

Core Dependency / Agnosticism

Prove at project/dependency level that Dungeon.Core does not reference:

EngineHost
Game bootstrap
Network
Godot
Dungeon Lab
SideCar host

Run the same Core under:

standalone deterministic harness;

Game/EngineHost adapter composition.

Expected:

identical authoritative semantics for identical inputs.

Host Lifecycle Parity

Drive initialize/start/update/checkpoint/stop through the Host adapter and compare against direct Core/harness execution.

Expected:

Host composition changes ownership/lifecycle only;

no Dungeon semantics differ because EngineHost exists.

Viewer Projection / Disclosure

Produce a DungeonDebugSnapshot/equivalent projection.

Expected:

Lab viewer renders only from the projection;

disabling viewer changes no authoritative state;

an in-game debug composition can consume the same projection shape;

unauthorized clients are not automatically given secret blueprint/Core information.

External Reference Integrity

Create dungeon references to ordinary world/test objects, then independently destroy/retire/replace those objects.

Expected:

stale references are detected;

stale IDs are not silently treated as live objects;

dungeon state transitions to explicit missing/destroyed semantics;

save/load preserves correct reference state.

Persistence / Resume Equivalence

Run to checkpoint S, persist, destroy runtime, reload, continue with identical future inputs.

Expected:

resumed authoritative state and future hashes match uninterrupted simulation.

Test across:

built topology;

future blueprint;

pending construction;

Core relocation;

multiple entrances;

invalid/replan state;

item/Essence accounting.

Bounded Catch-Up

Advance a dungeon through long unloaded intervals.

Expected:

passive/continuous resources integrate exactly or via proven equivalent math;

strategic due times resolve deterministically;

work remains within explicit budgets;

no literal replay of high-frequency skipped ticks;

required discrete semantic work is never silently dropped;

unprocessed discrete work remains owed deterministic debt;

result matches reference semantics within defined fidelity policy.

Transaction Rejection

Force mutations to fail:

insufficient Essence;

invalid room volume;

disconnected decommission;

unreachable Core;

blocked entrance;

over-budget replan.

Expected:

failure before COMMIT changes no authoritative dungeon state.

Pre-COMMIT candidate/validation objects are transient and are not persisted.

Economic Conservation

At every accounting checkpoint:

Opening + income - spend = Closing

Expected:

exact integer balance with no epsilon tolerance;

stable reason/category for every mutation;

no self-generated item assimilation profit cycle.

Entrance Construction

Create:

excavated stair entrance;

surface-building/mausoleum entrance;

second/third entrance.

Expected:

construction/emergence lifecycle is visible;

entrance does not instantly appear as finished structure;

built topology connects legally;

traffic can route through new entrance.

Reachability

Validate the reusable query under:

open path;

closed but solvable puzzle door;

switch-controlled door;

illusion wall;

impossible mechanism;

collapsed link;

multi-entrance topology.

Expected:

structural, mechanically-satisfiable, and currently-accessible results remain distinct;

true Core always has at least one mechanically satisfiable route from a valid entrance;

bounded prerequisite graphs are evaluated without invoking arbitrary gameplay/puzzle solvers;

malformed/cyclic prerequisite descriptions fail within explicit work bounds.

Determinism

Same seed + same traffic + same versions = same authoritative result.

Use checkpoint hashes.

Plan Growth

Verify:

rooms connect to built topology;

no floating rooms;

valid vertical links;

no overlap with blocked volumes;

search budgets obeyed.

Replanning

Block planned future space.

Expected:

built rooms unchanged;

invalid nodes marked;

deterministic replan;

PlanRevision increments.

Scrapping

Verify:

leaf removal;

articulation handling;

salvage bound;

active footprint update;

future plan adaptation.

Depth vs Width

Compare depth-biased and width-biased personalities.

Expected:

visibly different geometry;

different income mix;

both viable in appropriate environments.

Passive Recovery

Deplete dungeon, then stop all traffic.

Expected:

slow recovery while Core lives.

Farming Response

Use high extraction / low mortality.

Expected:

falling ROI;

changed strategy;

no bespoke farming flag.

Puzzle Dungeon

Expected:

lower mortality;

stable traffic;

reward spending;

manageable Threat;

long survival.

Aggressive Dungeon

Expected:

greater monster spend;

higher deaths;

higher Threat;

more hostile responses;

higher chance of eventual Core attack.

Ignored Dungeon

Expected:

passive growth;

gradual expansion;

possible overpopulation;

eventual raid/stampede pressure.

Overpopulation

Force Population > Control/Sustainment.

Verify possible:

cull;

upgrade;

expand;

expel;

raid;

stampede.

Loot Conservation

Verify:

dungeon cannot profitably eat its own created rewards;

outside items may provide real value;

generated loot costs resources.

Champion Lifecycle

Weapon exists at Champion creation, is used, then changes ownership after defeat.

Core Destruction

Verify:

no more regen/spawn/adaptation;

physical ruins/items remain.

Visualization Independence

Same headless result with all views disabled/enabled.

Structural Realization

Verify:

logical spaces can realize using universal small pieces;

larger compatible modules can substitute;

wall anchor remains boundary/inward;

paint only invalidates local render cache.

66. Failure Injection

Lab should intentionally support:

blocked expansion;

impossible floor;

replan exhaustion;

zero traffic;

extreme traffic;

Essence starvation;

Control collapse;

Sustainment collapse;

repeated boss kills;

repeated treasure extraction;

item-generator failure/fallback;

forced Core attack;

forced Core relocation;

blocked Core relocation candidate;

unsatisfiable Core route attempt;

entrance construction obstruction;

save/load during pending mutation/construction;

long offline catch-up;

forced overpopulation;

stale/destroyed referenced world object;

cyclic/invalid mechanic prerequisite graph.

Failures become evidence.

67. Debug Overlays

Useful toggles:

Room IDs;

Archetypes;

Built vs Planned;

Invalid/Blocked;

Growth frontiers;

Connection graph;

articulation rooms;

floor bounds;

vertical connectors;

entrances / entrance construction volumes;

true Core / decoy sanctums;

structurally reachable Core routes;

mechanically satisfiable Core routes;

current-access routes;

traffic heatmap;

death heatmap;

combat heatmap;

treasure heatmap;

room ROI;

monster density;

puzzle success rate;

threat contribution;

actor routes.

68. Lab Rendering Strategy

Start ugly.

Use a single 3D debug realization as the primary world view.

A dedicated 2D renderer is not required.

Orthographic/top-down analysis, if useful later, should reuse the same 3D room/volume data and camera rather than becoming a second world representation.

Use primitives:

floor boxes/quads;

wall boxes;

simple opening markers;

stairs/ramps;

cubes for monsters;

cubes for visitors;

boxes for treasure;

distinct Core primitive.

Do not spend early effort on art.

The visualizer must be replaceable by later content realization.

69. Integration Ports and Composition Boundaries

Potential Core-facing ports should be introduced only as concrete requirements appear.

Examples:

DungeonWorldConstraints
    can this volume be occupied/excavated?

DungeonStructurePort
    commit/query structural realization state

DungeonGameplayPort
    realize abstract reward/defender/trap/assimilation intentions

DungeonTimeSource
    authoritative world time

DungeonHistorySink
    project meaningful occurrences

DungeonRegionAdapter
    future nested-region realization

DungeonDiagnosticsProjection
    produce bounded immutable inspection snapshots

These names are conceptual rather than frozen APIs.

The rule is:

Core defines only the narrow capabilities it actually needs; Host/Gameplay adapters implement them using UltraSim.

Do not make Core depend on EngineHost to discover those services.

Do not speculatively create an interface for every conceivable future interaction.

Composition Overview

                         EngineHost
                             │
                       Dungeon.Host
                             │
             ┌───────────────┴───────────────┐
             │                               │
             ▼                               ▼
       Dungeon.Core                   Dungeon.Gameplay
             ▲                               │
             │ abstract ports                │ real Game semantics
             └───────────────────────────────┘

Definitions / Profiles
    configure both layers where appropriate

Scripts / concrete behaviors
    remain toward Gameplay/content edge

Dungeon Lab Composition

Dungeon Lab may construct the Core directly with deterministic fake/test ports:

Dungeon.Core
    │
    ├── FakeWorldConstraints
    ├── SyntheticGameplayPort
    ├── TestClock
    └── DiagnosticSink

This is a deliberate test that Dungeon.Core has no hidden EngineHost/Game dependency.

Suggested Implementation Path

Checkpoint names remain placeholders.

Pre-Dungeon gate:

The Mine plan (MINE.md) should first close enough of MIN0-MIN9 to prove:

- deterministic persistent subterranean identity/time;
- deterministic world geology;
- connected opportunity planning;
- spatial realization / CanFit;
- incremental excavation;
- built-topology persistence;
- finite deposit exposure/depletion;
- bounded replan;
- an abandoned persistent mine fixture suitable for Dungeon origin.

Dungeon implementation may begin before every future Mine feature is polished, but it should not be the first code proving those spatial primitives.

Lab observability is intentionally interleaved with Dungeon Core implementation. Debug visualization, state dumps, event logs, and decision traces are development infrastructure, not post-Core polish.

HOST0 — Minimal EngineHost v0 / Composition Seam

Implement only the minimum proven host boundary:

registration
dependency/order validation
construction/bootstrap
start
timed/deterministic update hook
stop/dispose
fault reporting
diagnostics enumeration

Create a thin Dungeon.HostAdapter.

Hard dependency proof:

Dungeon.Core
    references no EngineHost
    references no Network
    references no Godot
    references no DungeonLab

Also prove:

Core can be constructed/run directly in a test/harness composition;

Game composition can drive the same Core through Dungeon.HostAdapter;

host failure/lifetime does not leak host concepts into Core semantics.

Do not add generic plugin buses, hot-loading, arbitrary RPC, or large DI machinery.

SIDECAR0 — Boundary/Skeleton Only

Document/establish:

process/lifetime boundary;

fault ownership;

restart expectations;

serialization/transport ownership where later needed.

Do not make SideCar execution a prerequisite for Dungeon.

Do not move authoritative Dungeon semantics out of Game.

Accept when: the architectural process boundary is explicit, while all Dungeon Core tests still run without a SideCar.

DUN0 — Identity / Time / Persistence / Accounting

Implement:

DungeonId;

seed;

lifecycle;

authoritative world time / due times;

integer Essence domain;

small initial personality state;

addressed deterministic RNG domains;

persistence schema/version;

external-reference lifetime validation contract;

save/load-resume equivalence;

bounded catch-up skeleton with discrete owed-debt semantics;

transient pre-COMMIT vs persisted committed-action boundary;

exact Essence accounting ledger;

state hashing;

Core-native lifecycle/API sufficient for direct harness execution;

no dependency on EngineHost/Game bootstrap.

LAB0 — Headless Runner / State Dump

Implement immediately with DUN0:

create dungeon from seed/profile;

pause/run/step;

state dump;

accounting display;

deterministic hash checkpoints;

event log.

Accept when: same initial state/input produces same state hashes, including across save/load, whether the Core is driven by the standalone harness or the in-process host adapter.

DUN1 — Dungeon Semantic Opportunity Blueprint over Mine-Proven Spatial Planning

Implement/extend from the Mine-proven spatial planner:

logical dungeon room graph over connected subterranean opportunities;

Dungeon room-opportunity records;

branches/loops;

depth skeleton;

future landmarks;

future entrance opportunities;

Core-capable sanctum opportunities;

growth frontiers;

Dungeon structural vs semantic-plan separation;

explicit adapter/mapping between shared spatial opportunity identity and Dungeon room semantic identity.

Do not re-implement Mine geology, labor, hauling, or depletion semantics.

Hard invariant:

normal growth does not invoke spatial search.

LAB1 — 3D Blueprint Viewer

Implement:

primitive/wire 3D view;

free camera;

whole-blueprint zoom-out;

Built/Future/Invalid overlays;

room IDs;

connection lines;

Core/entrance markers.

Accept when: deterministic blueprints are inspectable and connected.

DUN2 — Dungeon Spatial Specialization / Reachability

Implement/extend the Mine-proven spatial realization where Dungeon needs richer structure:

Dungeon macro planning cells / room masks where still useful;

irregular orthogonal room masks;

hallways;

bounded floor packing;

vertical connectors;

multi-entrance roots;

Dungeon-specific structural/mechanical/current reachability query;

bounded mechanic-prerequisite projection/evaluation;

bounded placement/search budgets;

compatibility with existing excavated Mine topology as an initial built/root structure.

If Mine's proven spatial representation makes the old 2×2 macro-cell assumption unnecessary, revise Dungeon around the proven representation rather than preserving a duplicate planner for historical reasons.

LAB2 — Spatial / Reachability Diagnostics

Add:

room-volume outlines;

floor isolation/cutaway;

blocked-volume overlay;

route overlays;

root-to-Core route diagnostics;

generator timing metrics.

Accept when: structural plan maps into legal 3D opportunity space and Core routes are provable.

DUN3 — Dungeon Built Growth / Transactional Mutation

Reuse the proven Mine excavation/construction state machine where the invariant is literally the same; layer Dungeon Essence/economic semantics above it rather than duplicating spatial mutation machinery.

Implement:

Planned/Reserved/Building/Built states;

DungeonMutation / PlanDelta validation;

atomic COMMIT;

frontier consumption;

room/floor cost;

passive regen;

depth/footprint bonuses.

LAB3 — Economy / Decision Inspector

Add:

Essence time series;

exact accounting equation;

strategic candidate trace;

next due time;

build-state animation/overlays.

Accept when: dungeon grows from preplanned opportunities without generator search and all Essence is auditable.

DUN4 — Replanning / Plan Extension

Implement:

future invalidation;

affected-subtree detection;

PlanRevision;

bounded deterministic partial replan;

rare plan extension when opportunity space is exhausted;

built topology immutable.

LAB4 — Plan Diff / Invalidation View

Add:

invalid Future volumes;

old/new branch overlay;

replan work/timing;

replan reason;

affected vs retained opportunity counts.

Accept when: world interference changes only Future topology and never moves built rooms.

DUN5 — Core Sanctum / Protection / Relocation

Implement:

true Core Room;

accessibility invariant;

Core Exposure;

concealment/gating metadata;

decoy sanctums;

Core relocation;

transactional transfer;

old-sanctum downgrade.

LAB5 — Core Analysis

Add:

Core Exposure panel;

true/decoy sanctum overlays;

shortest structural/mechanical/current routes;

relocation candidates/cost;

sanctum history.

Accept when: dungeon can make the Core difficult to find/reach while preserving a satisfiable destruction route.

DUN6 — Entrance Lifecycle / Multi-Entrance Strategy

Implement:

multiple active entrances;

traffic value;

planned entrance opportunities;

excavated stair/path emergence;

building-like entrance construction/emergence;

sealing/destroying entrances.

LAB6 — Entrance Visualization

Add:

entrance build-state outlines;

emergence progress;

traffic by entrance;

entrance-to-entrance traversal metrics.

Accept when: entrances are real topology/construction, never instant finished pop-ins.

DUN7 — Decommission / Salvage / Repurpose

Implement:

articulation analysis;

strip/collapse/remove/repurpose;

salvage;

active-footprint reduction;

connectivity validation.

LAB7 — Topology Surgery Tools

Add:

articulation highlighting;

dependent-branch preview;

mutation before/after preview;

salvage/accounting trace.

Accept when: dungeon can shrink without invalid active topology or unreachable Core.

DUN8 — Synthetic Visitors

Implement:

minimal visitor model;

traversal;

combat/puzzle/trap/loot;

retreat/death;

traffic profiles;

multi-entrance selection.

LAB8 — Traffic / Heat Diagnostics

Add:

visitor routes;

traffic heat;

death heat;

depth distribution;

entrance usage;

Core approach counts.

Accept when: synthetic traffic creates repeatable pressure without becoming a second Sim Core.

DUN9 — Monster Population / Overflow

Implement:

monster investment;

Control;

Sustainment;

cull;

upgrade;

raid;

expel;

stampede.

LAB9 — Population Diagnostics

Add:

density;

Control/Sustainment deficit;

stampede risk;

monster-flow overlay.

DUN10 — Reward / Gameplay Integration Economy

Implement:

abstract Core reward-investment intent;

abstract defender/asset-value inputs as required;

Dungeon.Gameplay translation seam;

bounded Lab gameplay/test adapter;

reward placement result contract;

external asset discovery/assimilation result contract;

bounded test item generator behind Gameplay adapter;

provenance;

hoard/redistribute/assimilate;

anti-self-profit conservation.

Core must not learn concrete chest/item/vendor/prefab semantics.

LAB10 — Item / Value Audit

Add:

item inspector;

creation/assimilation provenance;

imported/exported value;

reward ROI.

The final UltraSim item system is not required here; the adapter only needs enough real semantics to prove dungeon accounting.

DUN11 — Threat / Reputation / Personality Adaptation

Implement:

objective Threat;

synthetic reputation;

hostility response;

profitability adaptation;

proven personality dimensions;

Core-hunter/guild pressure.

LAB11 — Personality / Traffic Matrices

Add:

headless batch runner;

personality matrix;

traffic matrix;

CSV/JSON metrics;

survival distribution.

Accept when: same opportunity blueprint under different personalities produces materially different life histories.

DUN12 — Core Death / Ruins

Implement:

Core damage/destruction;

permanent dungeon death;

no further regen/adaptation;

preserve structural ruins/items/remaining actors.

LAB12 — Golden Regression Suite

Establish representative deterministic seeds:

aggressive;

puzzle;

wide;

deep;

multi-entrance;

constrained terrain;

frequent replan;

starvation;

overpopulation;

Core relocation;

heavily farmed.

70. First Vertical Slice

Precondition: the Mine campaign has already proved the basic subterranean planning/excavation/persistence path and provides an abandoned/existing-workings fixture.

Keep the first Dungeon end-to-end proof small.

1 dungeon
Depth target ~3
Maximum ~20 active rooms

Resources:
    Essence
    Control
    Sustainment

Rooms:
    Entrance(s)
    Hall
    Combat
    Puzzle
    Treasure
    Stairs
    Core Sanctum

Synthetic visitors:
    Novice
    Veteran
    Treasure Hunter

Dungeon choices:
    Build Room
    Build Floor
    Spawn Monster
    Create Treasure
    Create Puzzle
    Save Essence
    Scrap Room

Views:
    Single reusable 3D Structure Viewer
        consumes DungeonDebugSnapshot / diagnostic projection
        built/planned/invalid/building/destroyed volume overlays
        free-fly/orbit/zoom/jump-to-interest
    Dungeon Inspector
    Decision Inspector
    Core/Reachability Inspector
    Time Series
    Event Log

This is enough to prove the organism.

71. Definition of “Dungeon Core Works”

The Dungeon Core is fundamentally proven when:

the Mine-first sequencing has been respected: Dungeon is not the first implementation of basic subterranean planning/excavation;

shared Mine/Dungeon spatial machinery is reused or extracted only where the second consumer proves literal common invariants;

Dungeon.Core remains semantically independent from Mine labor/geology/economy logic;

Dungeon.Core is runnable without EngineHost, Game bootstrap, Network, Godot, Dungeon Lab, or SideCar hosting;

the same Core semantics can be driven directly by a harness or through Dungeon.HostAdapter;

EngineHost owns composition/lifecycle only and contains no dungeon-specific semantics;

save/load-resume produces the same authoritative future as uninterrupted simulation;

external world-object references are lifetime-safe and stale references are handled explicitly;

long inactive periods advance through bounded world-time catch-up rather than replaying skipped live ticks;

analytically compressible progression may be integrated, while required discrete semantic work is never silently dropped and remains deterministic owed debt when deferred;

it produces a long-horizon connected structural opportunity blueprint;

ordinary growth consumes preplanned opportunities without procedural spatial search;

it grows only from existing built topology/frontiers;

it can grow deeper, wider, and through multiple entrances;

building-like entrances emerge through construction rather than instant finished pop-in;

depth and footprint create different economic incentives;

passive generation prevents economic deadlock;

Essence uses exact deterministic arithmetic and every balance change is auditable by reason/category;

every meaningful dungeon mutation validates before atomic COMMIT;

pre-COMMIT validation state is transient while committed long-running actions persist through save/load;

Core strategy uses abstract reward/defender/assimilation concepts while Dungeon.Gameplay owns concrete actors/items/containers;

Profiles configure behavior without becoming a second authority path;

visitors create useful economic pressure;

reward creation is a real cost;

self-created items cannot create assimilation profit loops;

outside items can be hoarded, redistributed, or consumed;

monster population pressure produces meaningful responses;

farming causes adaptation through economics, not a bespoke flag;

personalities materially alter long-run behavior while sharing the same opportunity blueprint;

future invalidation causes bounded deterministic partial replan without moving built rooms;

rooms can be decommissioned safely;

every Active/Built room remains connected to a valid root unless explicitly abandoned/ruined;

structural, mechanically satisfiable, and currently accessible reachability remain distinct;

mechanic satisfiability is evaluated through bounded prerequisite descriptions rather than arbitrary puzzle solving;

the Core lives in a real Sanctum and always retains at least one mechanically satisfiable destruction route;

Core Exposure can drive defenses, decoys, and transactional relocation;

Core destruction ends the organism permanently;

physical ruins remain;

the Lab can simulate years/decades rapidly;

major decisions, mutations, accounting, reachability, persistence state, and host composition are inspectable and explainable;

the reusable debug projection supports both Dungeon Lab and future authorized in-game inspection;

unauthorized clients are not implicitly disclosed hidden blueprint/Core information;

visualization can be disabled without changing semantics.

Frozen Core Architecture Summary

The architecture to freeze before implementation is:

Dungeon Identity / Persistence
        ↓
Logical Opportunity Blueprint
        ↓
Spatial Opportunity Realization
        ↓
Cheap Semantic Selection during normal life
        ↓
Transactional mutation / COMMIT
        ↓
Built Topology
        ↓
Ordinary Structural Realization

Exceptional path only:

World interference
or
Opportunity exhaustion
        ↓
Bounded deterministic partial replan / extension

Composition boundary:

EngineHost
    ↓ lifecycle only
Dungeon.HostAdapter
    ↓ composition
Dungeon.Core
    ↔ narrow abstract dungeon ports
Dungeon.Gameplay
    ↓
ordinary UltraSim world semantics

Dungeon.Core must remain directly runnable without EngineHost.

Cross-cutting invariants:

exact integer Essence accounting
bounded deterministic world-time catch-up
required discrete work is never silently dropped
pre-COMMIT candidates are transient
committed long-running actions persist
external references are lifetime-safe
Core always has a mechanically satisfiable destruction route
mechanic satisfiability uses bounded prerequisite descriptions
ordinary growth performs no spatial search
Core knows abstract dungeon intent, not concrete item/actor/container types
Profiles configure; they do not bypass authority
Dungeon Lab consumes the real Core
debug viewer consumes immutable/bounded diagnostic projections
in-game debug disclosure remains Game-owned
visualization never changes semantics

These are implementation contracts, not tuning preferences.

72. Final Thesis

The dungeon is not a level.

It is a persistent organism that expresses itself as architecture.

Its loop is:

Exist
    ↓
Generate passive Essence
    ↓
Attract / encounter visitors
    ↓
Gain or lose resources
    ↓
Evaluate strategy
    ↓
Build / spawn / reward / defend / adapt
    ↓
Change topology and ecology
    ↓
Alter traffic / threat / profitability
    ↓
Repeat

Its architecture is:

Dungeon Identity
    ↓
Long-Horizon Structural Opportunity Blueprint
    ↓
Cheap Semantic Selection / Normal Growth
    ↓
Built Topology
    ↓
Ordinary Structural Realization

Exceptional only:
    world interference / exhausted plan
        ↓
    bounded partial replan / extension

The Dungeon Lab exists to prove that organism in isolation, using enough synthetic pressure to expose its strengths and failures, while running the same Dungeon.Core that later participates in UltraSim.

The composition goal is equally important:

same Dungeon.Core

    ├── standalone Dungeon Lab / deterministic harness
    ├── Game through Dungeon.HostAdapter / EngineHost
    ├── benchmark / regression process
    └── future tool or SideCar composition where appropriate

If those environments can drive the same dungeon semantics without host/game/viewer assumptions leaking back into the Core, Dungeon becomes the first real proof of the EngineHost/core-adapter architecture that Sim Core can later challenge and refine.