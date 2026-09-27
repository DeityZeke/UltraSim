# UltraSim Future Content & Engine Extensibility Backlog

**Status:** Future implementation catalog / authoring-pressure list  
**Purpose:** Preserve candidate Engine capabilities, content-authoring primitives, package hooks, and extension seams that make UltraSim's own Game Implementation easier to create, update, compose and maintain. Third-party modding is a natural consequence of those same seams, not a separate architectural layer.  
**Canonical terminology:** Use the Kernel / Contracts & Bridges / Framework / Engine Core / Game Implementation vocabulary from UltraSim_Pre_Many_Person_Live_Testing_Roadmap.md and Docs/ARCHITECTURE.md.

This document is intentionally broader than the active roadmap.

It answers:

> “What reusable capabilities and authoring/extensibility seams would make normal UltraSim content easier to build?”

It does **not** answer:

> “What separate Mod API should UltraSim invent?”

and it does **not** answer:

> “What must be implemented before PF6?”

---

# 1. Governing rules

The backlog should follow these permanent rules:

- **Kernels make applications possible.**
- **Contracts preserve meaning.**
- **Framework composes capabilities, packages, lifetimes, controls, persistence and adapters.**
- **Engine Cores provide reusable gameplay/interactive capabilities.**
- **Game Implementation selects and configures those capabilities into a particular game.**
- **Content is authored concrete stuff.**
- **Bridges realize external technologies without becoming semantic owners.**

Authoring/extensibility design should also preserve:

> **Do not create one giant Mod API.**

UltraSim's first-party Game Implementation should dogfood the same author-facing contracts used by server packages. A community “mod” should ideally be architecturally ordinary: another package, profile, definition set, script/rule set, sidecar type, Engine capability or content bundle composed through the same mechanisms.

A useful test is:

> **If adding ordinary first-party content requires bypassing the intended authoring contracts and reaching into internals, the authoring architecture is incomplete.**

Forge/RunUO-style mod layers are useful as architectural archaeology: inspect what pressure forced a hook/API to exist, then decide whether the underlying semantic seam belongs in UltraSim's normal content-authoring architecture.

Prefer several small stable seams:

~~~text
package lifecycle
typed contracts
typed definition facets
definition sets/tags
typed hooks/events
runtime capabilities/attachments
Control Plane publication
bounded networking
persistence/migration
presentation/audio contracts
~~~

Do not expose arbitrary internals merely because a first-party or community author asks for access.

---

# 2. Immediate / promoted items

These items are important enough that they should influence the active architecture rather than living only as distant ideas.

## 2.1 Liquid Bodies MVP — SOON

Water/liquid bodies are needed relatively soon for:

~~~text
oceans
lakes
ponds
puddles
pools
basins
reservoir-like bodies
~~~

The first implementation should **not** be hard-coded “Water.”

The reusable capability is Liquid/Fluid.

Water is one Game-defined liquid.

Preferred conceptual split:

~~~text
Liquid.Contracts
    LiquidId
    LiquidBodyId
    body/query contracts
    amount/level vocabulary

Liquid.Core
    body occupancy
    surface/depth queries
    finite/source semantics
    fill/drain/transfer where earned

Game Implementation
    Water
    Lava
    Honey
    Acid
    LiquidMagic
    MoltenMetal
    ...
~~~

Permanent rule:

> **Water is content. Liquid is the capability.**

### Initial body semantics

The MVP should support enough authority truth for future gameplay without implementing hydrodynamics:

~~~text
LiquidBody
    LiquidId
    bounded spatial shape / region
    surface level
    bottom/depth relationship
    finite | source/unbounded
~~~

Useful queries:

~~~text
Contains(position)
SurfaceHeight(position)
DepthAt(position)
Submersion(bounds/point)
LiquidAt(position)
~~~

Possible forms:

~~~text
Ocean
    liquid = Water
    fixed sea level
    source/unbounded

Lake
    liquid = Water
    bounded body

Pond
    liquid = Water
    bounded shallow body

Puddle
    liquid = Water
    shallow local body

Honey Ocean
    liquid = Honey

Lava Pool
    liquid = Lava
    bounded body
~~~

Do not encode “ocean” as a special Water-only primitive.

### Amount versus level

Do not force every liquid store into one representation.

For a finite container:

~~~text
Amount
Capacity
~~~

may be authoritative.

For a basin/body:

~~~text
SurfaceHeight
shape/bottom
~~~

may be the natural authority.

For an ocean/source body:

~~~text
Source / effectively unbounded
~~~

is preferable to a fake enormous numeric amount.

### Pools / containers / tanks

The contracts should leave room for:

~~~text
bucket
bottle
barrel
tank
pool
trough
fountain
reservoir
~~~

with:

~~~text
LiquidId
Amount
Capacity
Level where geometry permits
~~~

The first World-water implementation does not need every portable/container interaction immediately.

### Presentation separation

Authority should say:

~~~text
Body 51
Liquid = Honey
Surface = 4.2m
~~~

Presentation chooses:

~~~text
material
color/tint
opacity
waves
foam
refraction
emission
bubbles
~~~

A missing liquid asset must not destroy semantic liquid truth.

### Typed liquid facets

Future definitions may compose typed facets:

~~~text
Liquid physical facet
    density
    viscosity

Presentation facet
    semantic material
    color
    opacity
    surface profile

Movement facet
    drag
    swim policy

Damage facet
    heat / acid / etc.

Temperature facet
Magic facet
Ignition facet
~~~

Do not put liquid-specific branching such as “if Lava / else if Water” inside generic Liquid.Core.

### Explicitly deferred from the MVP

~~~text
Navier-Stokes-like simulation
cell-by-cell flow
pressure
pipes
erosion
flood propagation
chemistry
automatic arbitrary mixing
thermal simulation
~~~

Those become separate earned capabilities.

### Scheduling note

Liquid Bodies may be pulled forward as soon as World/content work needs real oceans/lakes/ponds/puddles.

It does **not** need to wait for every P1–P4 task to finish if a narrow typed contract can be introduced safely.

Full fluid simulation remains future work.

---

## 2.2 Definition Tags / Sets — promote into P4

Typed facets answer:

> “What structured facts belong to this definition?”

Definition tags/sets answer:

> “What semantic groups is this definition a member of?”

Examples:

~~~text
material/metal
weapon/sword
food/meat
wood/hardwood
liquid/waterlike
ore/copper
crop/grain
~~~

A recipe or rule can then depend on:

~~~text
#material/metal
~~~

rather than enumerating every package's metal item.

Requirements:

- stable typed/set identity;
- deterministic cold registration;
- packages may contribute members to shared sets without directly referencing one another;
- optional contributions must not create hidden hard dependencies;
- diagnostics should identify contributor package and final membership;
- persistence should reference stable semantic IDs, not dense runtime indexes.

Avoid reducing tags to one global untyped string dictionary.

---

## 2.3 Definition aliases / migration / remapping — promote into P2/P4

First-party content updates and external package updates both need a deliberate answer for renamed or removed persistent identities.

Example:

~~~text
old:
    myweapons:long_sword

new:
    myweapons:longsword
~~~

A persisted save should not silently turn the old item into an unknown/deleted object.

Needed future vocabulary:

~~~text
DefinitionAlias
PackageAlias where justified
Schema/DataMigration
RemovedDefinitionPolicy
MigrationVersion
~~~

Cold composition should be able to:

1. load package migration metadata;
2. resolve old stable IDs;
3. detect conflicting remaps;
4. migrate before state becomes authoritative;
5. fail explicitly when required migration cannot be completed.

Do not make runtime dense IDs the persisted compatibility contract.

---

## 2.4 Typed subsystem hooks / events — include in P2 authoring seam

Packages need deliberate places to observe or modify semantic behavior.

Do not create one global:

~~~text
EventBus.Publish<object>()
~~~

Prefer subsystem-owned typed contracts.

Examples:

~~~text
Items
    ItemPickedUp
    ItemDropped
    ItemUsed

Inventory
    BeforeTransfer
    TransferCompleted

Combat
    BeforeDamage
    DamageResolved
    EntityDefeated

Crafting
    BeforeCraft
    CraftCompleted

World
    RegionEntered
    StructuralPublicationChanged

Accounting/Admission
    ParticipantAdmitted
    ParticipantDisconnected
~~~

Each hook must define whether it is:

~~~text
observe-only
replaceable
modifiable
cancellable
ordered
authoritative
presentation-only
~~~

Do not let every package cancel every event by default.

Hooks must have explicit owner, bounds, ordering and failure semantics.

---

## 2.5 Semantic presentation references / derived UI portraits — promote into P4/P5/P6

Content authoring should not require a unique hand-authored 2D icon for every definition that can
appear in UI.

Use a generic presentation-reference vocabulary:

~~~text
SemanticPresentationReference
    DefinitionDomainId
    StableDefinitionId
    PresentationRoleId
~~~

Representative roles may include:

~~~text
World
Equipped
InventoryPortrait
VendorPortrait
CraftingPortrait
CharacterPortrait
BestiaryPortrait
TooltipPreview
Inspection
Paperdoll
Thumbnail
~~~

Definition domains/registries may preserve imported numeric source IDs where useful, but durable
identity should remain typed/stable. Cold composition may assign dense runtime IDs for lookup/wire
efficiency.

The resolver should prefer:

~~~text
explicit role-specific presentation
    ↓
compatible general presentation
    ↓
derive from World presentation
    ↓
primitive semantic fallback
    ↓
missing-content checker/debug presentation
~~~

This means a new Katana, chair, potion, crop, chest, creature or statuette can become UI-displayable
from its existing 3D presentation with no extra icon authoring.

A dedicated 2D icon remains a normal optional override.

### Portrait profiles

3D-derived portraits should use bounded declarative profiles:

~~~text
source presentation
camera/framing
lighting profile
autofit/fixed scale
orientation
pose mode
optional animation sample
background role
~~~

Do not make arbitrary mouse callbacks part of every content definition.

### Interaction tiers

Presentation may provide:

~~~text
Cached Portrait
    normal cheap UI image

Live Portrait
    small bounded hover/selection realization
    rotate / animate

Inspection View
    small bounded interactive 3D view
    rotate / zoom / examine
~~~

These are presentation tiers, not Game-state copies. Hover animation/rotation and inspection camera
movement are client-local.

This enables Skyrim-like object examination, animated creature/item previews, vendor/crafting/
bestiary reuse, and automatic inventory portraits through one underlying presentation mechanism.

### Derived resource rule

A generated portrait is disposable derived output. Its cache identity must include the resolved
presentation/content generation, role/profile version, relevant source asset hashes, and renderer
profile. It may participate in the UI/Icon atlas after generation.

Deleting the portrait cache never deletes Game/content truth.

---

# 3. Runtime capability / attachment model

Capability/attachment patterns seen in Forge-like extension layers map well to UltraSim's hybrid OOP + ECS architecture, but should be adopted as normal strongly typed authoring/runtime seams rather than as a separate mod-only mechanism.

Potential attachment owners:

~~~text
runtime sidecar object
ECS EntityId
item instance
container
World / Realm
SpatialChunk / region
server/deployment
account/character
~~~

Examples:

~~~text
InventoryItem capability
Equipment capability
Durability capability
Enchantable capability
LiquidContainer capability
EnergyStorage capability
Interactable capability
~~~

The capability interface/contract should preserve meaning while the backing realization may be:

~~~text
normal C# sidecar/OOP state
ECS component-backed state
hybrid object ↔ entity bridge
derived read model
~~~

Permanent rule:

> **ECS is an optional scalable realization, not the mandatory object model.**

Avoid duplicate authoritative state.

If Health is ECS-owned, a rich sidecar's Health property should route through the owning capability rather than maintaining a second unsynchronized health field.

---

# 4. Hybrid OOP / ECS authoring model

UltraSim should support at least three legitimate authoring styles.

## OOP-first

RunUO/Ocarina-like:

~~~text
BaseItem
    → BaseWeapon
        → BaseSword
            → Katana

BaseMobile
    → BaseCreature
        → Goblin
~~~

No arbitrary requirement to create components/systems for every concept.

## ECS-first

High-volume simulation:

~~~text
Entity
    Position
    Velocity
    Health
    Needs
    Job
    Route

Systems
    Movement
    Needs
    Employment
    Traffic
~~~

## Hybrid

Likely normal UltraSim posture:

~~~text
Mobile
    ECS:
        movement
        spatial
        health
        high-volume state

    sidecar:
        dialogue
        bespoke behavior
        inventory relationships
        uncommon state
        scripts
~~~

Promotion into ECS should be evidence-driven.

If 20 rich quest objects are awkward in ECS, use sidecars.

If 3,000,000 crops require dense updates, move the hot crop state into ECS.

Engine contracts should survive that representation change.

---

# 5. Future Engine Core candidates

These are candidates, not mandatory pre-PF6 work.

## 5.1 Attributes / Stats

Reusable vocabulary for:

~~~text
STR
DEX
INT
Agility
Will
Health
Stamina
Mana
Rage
other game-defined stats
~~~

Support base/current/maximum values, modifiers, derived values, temporary changes and requirements.

Game Implementation chooses actual stat identities and formulas.

## 5.2 Effects / Buffs / Debuffs

Timed or conditional effects:

~~~text
poison
burning
stun
bless
curse
slow
haste
regeneration
disease
~~~

Needed semantics include source, duration, stacking, refresh policy, priority, dispel/cure and persistence.

## 5.3 Loot / Rewards

Reusable reward/drop capability for:

~~~text
creature drops
chests
harvesting
fishing
quests
event rewards
~~~

Support composable modifiers rather than package replacement.

Example:

~~~text
Goblin base loot
    coins
    cloth

WinterEvent
    + 5% Winter Token

RareWeapons
    + chance of named blade

ServerProfile
    currency × 0.5
~~~

Ordering/conditions must be deterministic and diagnosable.

## 5.4 Recipes / Processing

Broader than “crafting recipe.”

Potential forms:

~~~text
shaped
shapeless
station-based
smelting
cooking
brewing
refining
assembly
timed processing
catalyst-based
~~~

Inputs may refer to exact DefinitionId, Definition Tags/Sets, Liquid, Energy, or tool/action requirements.

## 5.5 Energy / Power

Generic gameplay energy capability:

~~~text
storage
production
consumption
transfer
capacity
rate
network participation
~~~

Game profiles could realize electricity, mana power, steam or mechanical power.

## 5.6 Interaction / Tool Actions

Semantic actions such as:

~~~text
Cut
Chop
Dig
Mine
Harvest
Strip
Till
Repair
Pour
Fill
Drain
Ignite
Extinguish
Lock
Unlock
~~~

Definitions declare supported/required actions. Tools provide actions/capabilities. Game rules decide outcomes.

## 5.7 Biomes / Environment

Typed environmental regions or profiles affecting:

~~~text
flora
fauna
weather
materials
ambient sound
spawning
resource distribution
temperature
lighting
~~~

Keep semantic biome/environment truth separate from presentation material choices.

## 5.8 World Generation

Procedural authoring/generation capability for:

~~~text
terrain passes
resource placement
vegetation
caves
structures
roads
rivers
points of interest
~~~

This should produce/modify canonical World structural artifacts through owned authoring seams.

## 5.9 Structures / Prefabs

Reusable structure placement rules over World/Multis:

~~~text
houses
ruins
dungeons
villages
bridges
camps
resource sites
~~~

Prefer structural composition over thousands of unrelated permanent entities where appropriate.

## 5.10 Points of Interest

Typed POI vocabulary for:

~~~text
AI jobs
vendors
beds
workstations
resource spots
navigation destinations
quests
settlement simulation
~~~

A POI need not imply a physical ECS entity.

## 5.11 Advancements / Achievements

Criteria/progress engine usable for game achievements, tutorial progression, server achievements, platform achievements and seasonal challenges.

Keep gameplay criteria separate from external-platform achievement bridges.

## 5.12 Affixes / Enchantments

Composable item enhancement semantics:

~~~text
prefix
suffix
enchantment
quality
rarity modifier
procedural property
~~~

Prefer typed effect/facet contributions rather than giant item subclasses.

## 5.13 Automation / Logistics

Potential high-level Engine connecting independent contracts:

~~~text
Inventory
Liquids
Energy
~~~

without making those Engines depend laterally on each other.

Possible gameplay:

~~~text
pipes
pumps
hoppers
conveyors
tanks
machine inputs/outputs
power cables
~~~

Automation owns orchestration. Inventory/Liquid/Energy continue to own their own semantics.

---

# 6. Future package / content-authoring hooks

## 6.1 Package lifecycle

P2 already plans explicit Register / Configure / Initialize / Stop or equivalent bounded lifecycle.

Do not rely on arbitrary static constructors for authoritative composition.

## 6.2 Registries

Stable cold registries may be needed for:

~~~text
definitions
facets
tags/sets
actions
controls
hooks
recipes
effects
capabilities
~~~

Persist stable IDs. Use dense runtime IDs only after composition.

## 6.3 Conditional definitions / optional integration

Allow packages to declare content that activates only when another capability or definition exists.

Examples:

~~~text
if Farming capability exists:
    enable Compost recipe

if Liquid capability exists:
    attach WateringCan liquid facet

if Magic package exists:
    register ManaInfusion recipe
~~~

Use explicit capability/definition predicates, not reflection or stringly-typed runtime probing.

## 6.4 Bounded package networking

Eventually packages may need versioned client/server application messages.

Requirements:

~~~text
stable message identity
version/compatibility
maximum encoded size
lane/delivery semantics
admission bounds
authorization
composition fingerprint awareness
~~~

Do not allow arbitrary packages to bypass the Network Kernel's safety/boundedness model.

This is reserved future work until a real package needs it.

## 6.5 Package-owned Control Plane publication

The [P1 foundation](../../Framework/ControlPlane/README.md) now provides the BCL-only typed
provider/descriptors/snapshots, bounded batches/actions/collections and generation-fenced
registration seam. The ECS, ServerRuntime and Client Settings proof adapters are real owner
consumers; the generic viewer contains no concrete owner branching. Dynamic package-owned
registration, Runtime/Module Host and operator product UI remain P2/P5 work, not implemented P1
features. There is no generic property bag or global settings store.

Packages should be able to publish diagnostics, controls, actions and collections through the same generic Control Plane as built-in providers.

Example:

~~~text
Farming/Growth Rate
Farming/Active Crops
Farming/Season Diagnostics
~~~

The viewer should not contain package-specific code.

## 6.6 Presentation hooks

Packages may contribute semantic presentation definitions such as asset IDs, materials, models, effects, particles and UI descriptors without receiving unrestricted Godot internals as the normal contract.

Godot remains an adapter.

## 6.7 Sound hooks

P8's Sound Engine should permit packages to contribute semantic cue definitions, event→cue mappings and ambience profiles without wiring package code directly to Godot audio nodes.

## 6.8 Input / actions

Packages may contribute semantic actions/bindings when appropriate.

They must not silently steal reserved controls.

Control Plane/Input ownership decides user-visible binding policy.

---

# 7. Authoring/tooling backlog

## 7.1 Data generation

Possible build-time tooling:

~~~text
stable/generated IDs
definition registries
tag/set indexes
recipe indexes
content manifests
documentation
diagnostic reports
~~~

Cold generation is acceptable if it produces a small predictable runtime machine.

## 7.2 Package-authored scenario tests

A future package-authored scenario-test surface, inspired by GameTest-style workflows, would be valuable for both first-party and community packages.

Example:

~~~text
MyPackage/
    Tests/
        KatanaHitsTarget.cs
        PumpMovesWater.cs
        VendorRestocks.cs
        CropGrowsUnderRain.cs
~~~

Tests should boot bounded deterministic fixtures and assert semantic outcomes.

## 7.3 Package profiling

Packages/Engines should be able to publish bounded diagnostics such as timing, work count, rows/entities processed, queue depths and allocation diagnostics where measured through Control Plane profiling seams.

Do not insert Control Plane dictionary lookups into hot loops.

## 7.4 Package update metadata

Later ecosystem tooling may describe:

~~~text
PackageId
Version
compatible contract ranges
upgrade notes
download/source location
signature/publisher identity
~~~

The forum/package page may display this metadata, but the forum is not the canonical runtime identity.

---

# 8. Fluids beyond the MVP

After Liquid Bodies prove real pressure, possible expansions include:

## Transfer

~~~text
fill
drain
pour
pump
container-to-container
body-to-container
~~~

## Flow fields / cells

Only if gameplay needs flooding, irrigation, dynamic rivers or factory systems.

## Mixing / reactions

Optional capability, not generic Liquid.Core default.

Examples:

~~~text
Water + Lava → Steam / Rock
Water + Honey → Diluted Honey
Acid + Metal → reaction
Mana + Water → game-defined result
~~~

## Temperature / phase

Potential later capability:

~~~text
freeze
boil
melt
evaporate
condense
~~~

Do not pull a chemistry/thermodynamics simulator into the first water implementation.

---

# 9. Things deliberately not copied from retrofit mod-layer APIs

## No Access-Transformer equivalent as the normal solution

If packages constantly need private/internal access, the architecture is missing a real contract/hook.

Add the correct seam instead of institutionalizing arbitrary private-member modification.

## No giant universal event bus

Subsystem owners define typed extension points.

## No giant string-keyed capability bag

Use typed capabilities/contracts and stable IDs.

## No mandatory inheritance hierarchy

OOP sidecars are supported, but Engine capabilities must not require every package to derive from one universal GameObject.

## No mandatory ECS representation

Use ECS when scale/update density earns it.

## No arbitrary hot unload promise

Startup-time composition remains the default safe model for packages that may own persistent/network/schema state.

## No frontend-owned semantics

A UI, forum, web page or Godot scene must not become the authority merely because it exposes a feature.

---

# 10. Candidate priority board

## Near / architectural

~~~text
[ ] Liquid Bodies MVP / Water
[ ] Definition Tags / Sets
[ ] Definition aliases + migration/remap policy
[ ] Typed subsystem hook/event registration seam
[ ] Semantic presentation references + 3D-derived UI portraits
~~~

## Important after P2/P4 pressure exists

~~~text
[ ] Runtime capability/attachment model
[ ] Conditional definitions
[ ] Package-owned Control Plane providers
[ ] Package scenario/GameTest harness
[ ] Bounded package networking
~~~

## Future Engine candidates

~~~text
[ ] Full Liquid transfer/flow/mixing expansions
[ ] Attributes / Stats
[ ] Effects / Buffs / Debuffs
[ ] Loot / Rewards + composable modifiers
[ ] Recipes / Processing
[ ] Energy / Power
[ ] Interaction / Tool Actions
[ ] Biomes / Environment
[ ] World Generation
[ ] Structures / Prefabs
[ ] Points of Interest
[ ] Advancements / Achievements
[ ] Affixes / Enchantments
[ ] Automation / Logistics
~~~

## Tooling / ecosystem

~~~text
[ ] Data generation
[ ] Package profiling
[ ] Package update metadata
[ ] Package signing/publisher identity if earned
[ ] External package index/repository if earned
~~~

---

# 11. Promotion rule

A backlog item moves into an active checkpoint only when one of these becomes true:

1. current UltraSim gameplay/content requires it;
2. several unrelated packages are independently reinventing it;
3. a current implementation cannot remain safe/bounded without it;
4. a planned product slice would otherwise use a misleading placeholder;
5. a package-author workflow demonstrates a concrete missing seam.

Examples:

~~~text
Need visible ocean for current World work
    → promote Liquid Bodies MVP

Three packages invent incompatible semantic tag systems
    → promote Definition Tags/Sets

One package wants Fishing
    → probably package/Engine-local first

Many Engines need the same persisted scheduler
    → likely Framework/Kernel pressure
~~~

---

# 12. Relationship to the active roadmap

The active sequence remains:

~~~text
P1 Control Plane
P2 Runtime/Module Host
P3 adapter/runtime boundary
P4 Engine Contracts/facets
P5–P11 productization
Gate 1
PF6
~~~

This backlog does not automatically reorder it.

Exceptions already accepted:

- **Liquid Bodies MVP** may be pulled forward in parallel when World/content needs real water.
- **Definition Tags/Sets** belong in P4.
- **Definition aliases/migrations** should influence P2/P4.
- **Typed hook/event registration** should be accounted for in the P2 server-authoring seam.
- **Semantic presentation references / derived UI portraits** participate in P4/P5/P6 because the representative Inventory/UI slice needs them; live hover/inspection elaboration remains bounded and may be staged.

Everything else remains future until real pressure promotes it.

---

# 13. Community / author feedback as architecture pressure

The eventual community should not need to create an UltraSim equivalent of a Script Extender merely because a legitimate use case lacks a seam.

Forums/community discussion can act as a design-intake surface:

~~~text
author has a use case
    ↓
describe desired outcome and current limitation
    ↓
classify the need
~~~

Use this decision ladder:

1. **Can the goal already be achieved cleanly?**  
   Document/example the intended path.

2. **Is the behavior package-local?**  
   Keep it package-local rather than enlarging the Framework.

3. **Is an existing Engine missing a legitimate semantic operation/hook?**  
   Extend that Engine contract deliberately.

4. **Are several unrelated packages/Engines reinventing the same primitive?**  
   Evaluate a Framework/Kernel/shared-contract seam.

5. **Is this a genuinely new reusable gameplay capability?**  
   Consider a new Engine Core.

6. **Would the requested hook violate authority, boundedness, persistence, dependency or hot-path invariants?**  
   Reject/redesign the requested mechanism while still trying to solve the underlying use case.

A proposal should explain the need, not merely demand a callback. Useful proposal context includes:

~~~text
goal / user-visible outcome
current approach
why the current approach is inadequate
expected scale
authority implications
persistence implications
network implications
example package/content
candidate general semantic solution
~~~

Once a new seam is accepted, it becomes normal UltraSim architecture:

~~~text
real use case
    ↓
architecture review
    ↓
general typed solution
    ↓
tests + diagnostics + documentation
    ↓
official contract/seam
    ↓
first-party and external packages use the same path
~~~

Forums are suitable for exploratory design discussion and support. GitHub issues/PRs remain the durable engineering/change-review record.

---

# 14. PF6 and content-production meaning

PF6 is not “the game is complete.”

It is the representative-slice validation that the selected architecture, authoring paths and product composition are stable and usable in a real scenario.

By that point, P2/P4 and the surrounding campaign should have proven enough of:

~~~text
package composition
normal C# authoring
typed definitions/facets
profiles/rules/scripts
stable IDs/migrations
Control Plane publication
Engine/Framework boundaries
presentation/content realization
persistence/network/runtime integration
~~~

that expanding the actual game increasingly becomes **content work** instead of repeated engine surgery.

After PF6 findings are repaired, ordinary additions such as:

~~~text
weapons
crops
creatures
quests
buildings
liquids
recipes
profiles
server packages
~~~

should normally use the established authoring substrate.

Later first-party content and community packages continue to pressure-test those seams. New architecture is added only when real use cases prove something genuinely missing.

---

# 15. Final intent

The desired content-authoring experience is not:

> “Here are all UltraSim internals; patch whatever you need.”

It is:

> **“Here are stable typed meanings and authoring/extension seams. Compose normal C# objects, Engine capabilities, ECS systems where scale earns them, definitions, profiles, scripts and content; the Framework handles lifecycle/composition and the rest of the platform keeps functioning.”**

The desired outcome is that:

~~~text
stock UltraSim content
our later expansions
custom server content
community packages
mods
~~~

all use substantially the same architecture.

“Easy to mod” is therefore not the primary architectural objective. It is a useful consequence—and a useful test—of making the game itself easy to author, extend and update.

That should preserve the productive RunUO/Forge feeling of substantial modification while avoiding dependence on every internal implementation detail.
