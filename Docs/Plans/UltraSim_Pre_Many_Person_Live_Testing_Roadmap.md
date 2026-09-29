# UltraSim Pre–Many-Player Live Test Roadmap

**Proposed status:** Candidate canonical roadmap / architecture checklist\
**Current reference baseline:** F1.12-R3 / Gate 0 production closure — `e3a63ea21f8f5ea4fa69e3768a055c409edc8616`; documentation-only commits may advance `main` beyond that closure SHA.\
**Immediate checkpoint:** P1 Framework Control Plane is **CLOSED** after the [native-lifetime repair acceptance](../../_Benchmarks/Baselines/framework-p1-native-lifetime-closure-2026-09-28.md) and uninterrupted canonical Release on merged `main`. P2 Runtime Host and C# Server Authoring Environment is **ACTIVE**. Typed Package/Runtime Service/Object Extension vocabulary, bounded dynamic C# authoring, compiled Contracts-only proof, Framework-only save sets and explicit Control Plane publication are implemented. Alias/remap, typed authoring registrations/facets/hooks, operator integration and first-party Game dogfooding remain; P2 closure is not implied by this foundation. P3–P11 and Gate 1 remain **PLANNED**; PF6 remains **PLANNED / NOT READY / NOT STARTED**.\
**End state of this document:** UltraSim is ready to move from individual/trusted dogfooding and closed validation into a many-player live test where human feedback is primarily about the intended game rather than known development-harness limitations.

---

# 1. How to use this document

This document is both:

1. a **canonical architecture direction** for the post-F1.12 productization work; and
2. a **task/checklist** that can be worked sequentially until UltraSim is ready for many-player testing.

It deliberately does **not** require every long-term UltraSim feature to be complete first.

The campaign should stop broad scope from leaking into pre-test work. In particular, W8–W10 World editing/topology work remains an independent campaign unless a specific productization or live-test requirement proves that it must move earlier.

## 1.1 Status vocabulary

Use these statuses consistently:

- **CLOSED** — implemented, verified, documented, and accepted.
- **IN PROGRESS** — currently being implemented or repaired.
- **NEXT** — immediate next checkpoint.
- **PLANNED** — accepted roadmap item, not started.
- **DEFERRED** — explicitly not required for this campaign.
- **BLOCKED** — cannot proceed until a named dependency is resolved.

## 1.2 Control/apply vocabulary

For controls/settings/configuration, use these apply modes:

- **Live** — takes effect without rebuilding a session or process.
- **SafePoint** — takes effect at an explicit owner-thread/runtime boundary.
- **SessionRestart** — process remains alive but the affected gameplay/presentation session must be rebuilt.
- **ClientRestart** — client process restart required.
- **ServerRestart** — server process/runtime restart required.
- **BuildTime** — currently requires source/build/package composition change.
- **ReadOnly** — diagnostic only.

## 1.3 Human-test stages

Do not conflate these phases:

```
Individual dogfood
    ↓
Trusted/internal dogfood
    ↓
PF6 closed external validation
    ↓
Many-player live test
```

F1.12 being green means the current client architecture/product paths are mechanically credible. It does **not** mean UltraSim is already representative enough for broad human testing.

---

# 2. Canonical architectural terminology

This terminology should replace older ambiguous uses of “engine,” “core,” “host,” and “script.”

## 2.1 Kernel

A **Kernel** is foundational reusable machinery that is not intrinsically UltraSim gameplay.

A practical test is:

> If this package were extracted into an independent repository tomorrow, could a substantially different application use it without importing UltraSim gameplay?

If yes, it is probably Kernel territory.

Representative Kernel candidates:

```
ECS
Network
Physics
Content / CAS
Persistence primitives
Audio, if implemented as generic device/mixing/spatial machinery
possibly generic AI/navigation/job infrastructure if it later earns that classification
```

A Kernel must not know what a Katana, backpack, goblin, Farming crop, skill cap, or player damage formula is.

### Kernel rule

> **Kernels make applications possible.**

Being widely used, important, performant, or bundled by default does **not** make something a Kernel.

---

## 2.2 Contracts / Bridges

**Contracts** preserve stable typed meaning between independently removable pieces.

**Bridges / adapters** connect one architectural boundary or technology to another without making either side own the other’s semantics.

Examples:

```
Physics.Contracts
Inventory.Contracts
Skills.Contracts
Control Plane contracts
presentation contracts
application protocol bridges
Godot presentation/input/audio adapters
```

Contracts may remain valid even when an optional Core implementation is absent.

Example:

```
Katana
    InventoryGridFacet = 1×3
    SkillRequirement = Swordsmanship 25
```

Those authored facts may remain valid even if `Inventory.Core` or `Skills.Core` is not active, provided the package/game composition declared those capabilities optional.

### Contract rule

> **Contracts preserve meaning. Cores provide behavior.**

Do not turn contracts into a giant `Dictionary<string, object>` or universal `Variant` property bag.

Prefer:

- strongly typed values/interfaces;
- stable/generated IDs where persistence needs identity;
- dense runtime IDs after cold registration;
- typed stores/records for hot-path access.

---

## 2.3 Framework

The **Framework** is the reusable platform layer that puts Kernels, Contracts, Bridges, capability registration, lifecycle, content, persistence integration, control publication, and runtime hosting back together into a coherent application/server platform.

Framework does **not** mean Gameplay.

Conceptually:

```
Kernels
   \
    \
    Framework
    /
Contracts / Bridges
```

The Framework answers questions such as:

```
Which Kernels are present?
Which Bridges connect them?
Which runtime capabilities exist?
How are packages/modules discovered and validated?
How does a server boot?
How do providers register and retire?
How are failures rolled back?
How are controls published?
How is content resolved?
How is persistence integrated?
```

It should not need to know:

```
what a backpack is
what a goblin is
what Swordsmanship means
which Inventory viewer UltraSim prefers
how much damage a Katana does
```

### Framework rule

> **Framework makes the Kernels cooperate as a platform.**

---

## 2.4 Engine Core

An **Engine Core** is an optional reusable interactive/gameplay capability built on Framework/Kernels/Contracts.

Representative Engine Cores:

```
Items
Inventory
Equipment
Chat
Skills
Combat
Farming
Crafting
Housing
Vendors / Economy
Spawning
Quests
Parties / Guilds
Trade
NPC/AI gameplay
Weather gameplay
```

An Engine Core is allowed to depend downward on Framework/Kernels/Contracts.

It should avoid lateral dependencies on unrelated Engine Cores.

Good:

```
Inventory.Core
    → Inventory.Contracts
    → Items.Contracts
    → ECS/Framework/Persistence contracts
```

Bad:

```
Inventory.Core
    ↔ Combat.Core
    ↔ Farming.Core
    ↔ Skills.Core
    ↔ Housing.Core
```

unless a very specific shared contract proves that dependency unavoidable.

### Engine rule

> **Engine Cores make interactive/gameplay capabilities possible.**

An Engine becoming stock/default does not make it a Kernel.

---

## 2.5 Game Implementation

The **Game Implementation** is the layer that composes Engine capabilities into the actual game being built.

This layer contains:

```
Scripts
Profiles
Definitions
Rules
Game-specific composition
Game-specific presentation policy
Game-specific content references
```

It answers:

```
What items exist?
Which Inventory profile does this game use?
What skills exist?
What are the skill caps/gain curves?
What does hunger do?
What makes a Katana different from a club?
How does this game's damage formula work?
Which crops grow under which conditions?
Which factions and NPC behaviors exist?
What visual/presentation style is selected?
```

### Game Implementation rule

> **Game Implementation decides which capabilities participate, how they are configured, and what game the player is actually playing.**

Engine Cores should define possibility rather than final game design.

For example:

```
Inventory.Core
    supports containers, placement policies, transfer, capacity, stacking hooks

UltraSim Game Implementation
    selects D2-ish Grid as the normal backpack viewer/profile
```

Similarly:

```
Skills.Core
    provides skill identity, value storage, requirements, modifiers, advancement hooks

UltraSim Game Implementation
    selects caps, curves, decay, aptitude, training rules
```

---

## 2.6 Content

**Content** is the concrete authored “stuff” consumed by the Game Implementation and presentation/content systems.

Examples:

```
Katana
pine tree
log cabin
goblin
crop
sound cue asset
material
texture
mesh
animation
UI asset
```

Content identity must remain distinct from runtime implementation availability where contracts allow dormant authored facets.

---

## 2.7 Adapter / external technology realization

Godot is not an UltraSim gameplay Engine in this vocabulary.

Prefer terms such as:

```
Godot Bridge
Godot Presentation Adapter
Godot Runtime Adapter
Godot Input Adapter
Godot Audio Adapter
```

Example:

```
UltraSim presentation/audio/input contracts
        ↓
Godot adapter
        ↓
Godot RenderingServer / AudioServer / Input APIs
```

Godot types must not become permanent engine-neutral contracts merely because Godot is the current realization.

---

# 3. Canonical dependency direction

The preferred conceptual dependency direction is:

```
Game Implementation
        ↓
Engine Contracts + Engine Cores
        ↓
Framework
        ↓
Kernel Contracts / Bridges
        ↓
Kernels
```

Dependencies generally flow **down**.

Composition happens primarily at the top.

The Framework makes independent verticals coexist without forcing them to depend directly on each other.

## 3.1 Independent-repository test

Treat every major package as though it might be extracted tomorrow.

Ask:

> What would this package need to reference, and could it be used without importing unrelated UltraSim gameplay?

Bad:

```
UltraSim.Physics
    → UltraSim.Game.Combat.SwordDamage
```

Good:

```
Combat.Core
    → Physics.Contracts
```

The monorepo may remain physically convenient while project/reference rules become increasingly extraction-safe.

## 3.2 Directory location does not define architectural role

Something under `Scripts/` could be:

```
custom Engine
extension Kernel
bridge
profile
definitions
```

Something compiled into the host could be:

```
Kernel
stock Engine
Framework infrastructure
bridge
```

Classification describes responsibility/generalization, not filesystem prestige.

---

# 4. Target logical repository/package shape

The monorepo does not need to be physically split before this campaign finishes.

It should increasingly behave like:

```
Kernels/
    ECS/
    Network/
    Physics/
    Content/
    Audio/
    Persistence/

Bridges/
    KernelContracts/
    ApplicationContracts/
    Presentation/
    Godot/

Framework/
    RuntimeHost/
    Modules/
    Capabilities/
    ControlPlane/
    DefinitionFacets/
    Composition/

Engines/
    Items/
        Contracts/
        Core/
        Profiles/        # optional stock/default profiles owned by the Engine
        Scripts/         # optional reusable Engine-level scripts/policies
    Inventory/
        Contracts/
        Core/
        Profiles/        # e.g. generic Grid/List viewer/placement profiles
        Scripts/
    Equipment/
    Chat/
    Skills/
    Combat/
    Farming/
    Crafting/
    ...

Game/
    Profiles/            # selects/customizes Engine-provided capabilities for this game
    Definitions/
    Scripts/
    Content/
```

Do not create one enormous `UltraSim.Contracts` assembly that becomes a new monolith.

Prefer contracts to remain near their owning vertical where practical:

```
UltraSim.Inventory.Contracts
UltraSim.Skills.Contracts
UltraSim.Physics.Contracts
UltraSim.Control.Contracts
```

with only genuinely universal/shared contracts pulled into smaller common packages.

### 4.1 Engine-owned defaults versus Game Implementation

An Engine Core may ship reusable stock/default `Profiles/` or `Scripts/` alongside its Contracts/Core. That does **not** make those defaults the Game Implementation.

Example:

```
Inventory Engine
    Profiles/
        Grid
        List

UltraSim Game Implementation
    selects D2-ish Grid as the normal backpack experience
    customizes rules/presentation where needed
```

The Engine defines reusable capability/default possibilities. The Game layer selects and composes them into a particular product. Another game may use the same Engine Core but choose different profiles, scripts, rules, and presentation.

---

# 5. Package runtime model

The previous “Engine Host” wording is retired because `Engine` now means gameplay/interactive Engine Core.

Use **Framework Runtime Host** for BCL-only composition/lifecycle; `Server/Authoring` discovers and compiles source before handing selected packages to it. Retire “Module Host” as a current UltraSim term. An ACC “Module” is historical vocabulary for what UltraSim calls an **Object Extension**: sparse managed state/behavior attached to an owner or global identity outside ECS.

Canonical distinctions: **Package** is deployment/composition, **Engine Core** is a domain capability, **Runtime Service** is a live exclusive object, **Extension** is sparse owner-attached OOP state, and **Contribution** is a participant in a bounded multi-contributor seam. Package `Provides` is declarative vocabulary, not a demand to construct a runtime object. A Contracts-only package may have no lifecycle entrypoint. `RuntimePackageClass.Kernel`/`Framework`/`Schema` classify packages; they are not Object Extensions.

## 5.1 Initial composition rule

“Plug and play” initially means:

```
add/remove/replace packages before startup
    ↓
discover
    ↓
validate dependency graph
    ↓
compile/load
    ↓
register capabilities/contracts/facets/controls
    ↓
validate whole composition
    ↓
start World/server atomically
```

It does **not** initially promise arbitrary hot unloading of packages that may own:

- persistent schemas;
- ECS component types;
- network protocol messages;
- durable definition identities;
- save codecs;
- authoritative state.

## 5.2 Package manifest

A package manifest should describe at least:

- stable package ID;
- version;
- contract/schema dependencies;
- required capabilities;
- optional capabilities;
- provided capabilities;
- ordering constraints (`Before` / `After`) where actually required;
- conflicts where required;
- entry assembly/source roots;
- lifecycle category if special restrictions apply.

Current unshipped package classes:

- Runtime;
- Definition Package;
- System Package;
- Engine Core Package;
- Kernel;
- Schema;
- Framework.

## 5.3 Runtime composition fingerprint

Produce a stable:

```
RuntimeCompositionFingerprint
```

for the resolved package/capability/schema composition.

Use it in:

- diagnostics;
- bug reports;
- persistence compatibility checks where appropriate;
- compile cache invalidation;
- server/operator views;
- PF6/live-test evidence.

Do not confuse stable composition fingerprint with a process/session generation ID.

## 5.4 All-or-nothing startup

A selected package composition must not leave a half-registered authoritative runtime.

If required capability resolution, compilation, registration or composition validation fails:

```
World/server must not start
```

and diagnostics must identify the failing package/capability relationship.

## 5.5 Server authoring environment

The Runtime Host plus server-owned authoring adapter are not only a loader. Before many-player testing they should establish the beginning of the intended RunUO-style **server authoring experience** for normal C# source packages.

A server deployment should be able to own a source tree such as:

```
MyServer/
    Scripts/
        Chat/
            Contracts/
            Core/
            Profiles/
            Scripts/
        Inventory/
            Contracts/
            Core/
            Profiles/
            Scripts/
        Items/
            Definitions/
        MyGame/
            Profiles/
            Definitions/
            Scripts/
```

Required first-stage capabilities:

- deterministic discovery of the selected source/package tree;
- staged compilation in dependency order;
- Contracts before dependents, then Core/runtime packages, then Scripts/Profiles/Definitions where applicable;
- a compile cache keyed by source/dependency/runtime-composition identity so stale output cannot silently become authoritative;
- source file/line compiler diagnostics usable by a server operator/author;
- explicit package lifecycle hooks such as registration/configuration/initialization rather than arbitrary static side effects;
- standard registration seams for commands/actions, Control Plane providers, definition facets, definitions, ECS systems, Engine capabilities, and UI/control providers;
- one sample server/shard package that demonstrates the complete author workflow.
- use first-party/reference content/packages to dogfood the same public authoring seams expected of external server authors; do not maintain a privileged hidden authoring path for stock Game content without an explicit architectural reason.

This is normal C# authoring. Do not replace it with a limited pseudo-language merely to make dynamic loading easier.

Hot source/package reload is not required for the initial campaign. Startup-time compilation/composition with deterministic restart is sufficient.

---

# 6. Typed definition/facet composition

Definitions are an intentional composition point where independent Engine Contracts can meet without making Engine Cores depend laterally.

Example concept:

```
KatanaDefinition
    Items facet
    Presentation facet
    Inventory Grid footprint
    Equipment/handedness facet
    Combat weapon facet
    Durability facet
    Skills requirement facet
```

The owning Engine Cores should query their own typed facet stores.

Do not grow a permanent god-object like:

```
ItemDefinition
    Name
    Weight
    GridWidth
    Damage
    Durability
    SkillRequirement
    CraftingMaterial
    VendorPrice
    ... forever
```

Prefer cold registration into typed stores such as:

```
ItemFacetStore
InventoryGridFacetStore
WeaponFacetStore
DurabilityFacetStore
SkillRequirementFacetStore
```

Use semantic/generated asset IDs rather than hard-coded filesystem paths where practical.

Dormant authored facets remain part of authored identity unless the composition explicitly declares that capability required and rejects startup without it.

---

# 7. Framework Control Plane

The Control Plane is the canonical management substrate for user settings, admin controls, developer controls, diagnostics, actions and bounded collections.

It belongs in the Framework/bridge substrate.

Product-specific controls do not.

## 7.1 Ownership rule

> **The viewer is not the owner of the control it displays.**

The owning subsystem must own:

- validation;
- legal bounds/options;
- canonical effective value;
- safe apply lifecycle;
- persistence semantics;
- authorization semantics;
- actual mutation/application.

The Control Plane discovers, describes and routes.

## 7.2 Publication kinds

Formalize at least:

1. **Diagnostic** — read-only value/status.
2. **Control** — mutable stateful value.
3. **Action** — command/button/request.
4. **Collection** — bounded list/table such as participants, active Packs or systems.

## 7.3 Descriptor vocabulary

P1's implemented contracts are in [Framework/ControlPlane](../../Framework/ControlPlane/README.md).
Mostly-static descriptors and dynamic snapshots are separate. The older combined census below
is vocabulary, not one mutable object: bounds/options/defaults/audience/capabilities/apply/persistence
belong to descriptors; requested/effective/pending values, independent provenance, writable/reason,
provider generation and revision belong to owner snapshots. ControlValue is the closed
Bool/Int64/UInt64/Float64/Text/Option machine; no arbitrary object/JSON or reflected CLR enum contract.

A control descriptor should support:

```
ControlId
ProviderId
CategoryPath
Label
Description
ValueType
RequestedValue
EffectiveValue
PendingValue
DefaultValue
Bounds / Options
ValueProvenance
Audience / Visibility
RequiredReadCapability
RequiredWriteCapability
ApplyMode
PersistenceOwner/Class
CurrentWritableState
DisabledReason
ProviderGeneration
```

## 7.4 Requested / effective / pending

Do not model every setting as one value.

Examples:

```
Render Distance
    requested = 3
    effective = 2
    reason = current authored World/window limit
```

or:

```
Maximum Participants
    effective = 64
    requested = 128
    pending = 128
    apply = ServerRestart
```

The UI must not imply that a saved/requested value is already effective when it is not.

## 7.5 Value provenance

Support provenance such as:

```
BuiltInDefault
UserGlobal
UserRealm
ServerRecommendation
ServerColdConfig
AdminRuntimeOverride
DeveloperOverride
TestOverride
```

Not every provider needs every source.

The point is to let diagnostics explain **why** a value is effective.

## 7.6 Hierarchical categories

Use category paths rather than one hard-coded tab enum.

Examples:

```
ECS/Scheduler
ECS/Movement
Server/Runtime
Server/Network
Server/Replication
Server/Content
World/Simulation
World/Persistence
Client/Presentation
Client/Renderer
Client/Audio
Developer/Motion
Developer/Vegetation
```

Providers may publish into multiple categories.

## 7.7 Authorization

Visibility and write authority are separate.

A control may be:

- hidden from normal users;
- readable to diagnostics roles;
- writable to an operator/admin capability;
- fully inspectable/overridable in local developer mode.

The server grants server-authoritative capabilities.

A client must never become admin merely because it knows a `ControlId`.

## 7.8 Admin action contract

Use one typed semantic action path for console, GUI, in-game admin and automation.

Candidate shape:

```
ControlActionRequest
    RequestId
    Principal
    CapabilityProof/Context
    ActionId
    Typed arguments
    Target generation/revision

ControlActionResult
    RequestId
    Accepted / Rejected / Failed
    ResultCode
    Human diagnostic
    New revision/generation where relevant
```

## 7.9 Audit identity

P1 uses the generic ControlPrincipal, not an Admin-only principal. Trusted composition grants
bounded capabilities/context; categories are audience filters, not privilege. The optional bounded
audit observation seam retains request identity, provider/operation/target, candidates, optional
owner timestamp/tick and result. It is not a persistent audit database.

Every authoritative admin mutation should be attributable.

Retain enough information to answer:

```
Who requested it?
What did they request?
Against which generation/revision?
Was it accepted?
What changed?
```

Candidate audit vocabulary:

```
AdminPrincipal
AdministrativeRequestId
CapabilitySet
Timestamp / SimulationTick
Action
Target
Result
```

Local/Complete mode may use a local operator principal.

## 7.10 One semantic plane, many viewers

The same providers/descriptors/actions should feed:

```
F10 User Settings
F4 Server Monitor / Admin
Developer controls
RunUO-style server console
Godot server GUI
Tests/automation
future web admin
```

Do not implement Save/Kick/etc. separately in every frontend.

## 7.11 Persistence rule

The Control Plane is **not** one giant universal `settings.json`.

Persistence remains subsystem-owned.

Examples:

```
Client settings → Client settings store
Server cold config → server config
World state → World persistence
resource packs → presentation/content ownership
scheduler/runtime state → owning runtime
```

---

# 8. Current control/settings census at F1.11 baseline

This section is the starting backlog for P1/P5.

## 8.1 Normal client settings already exposed

| Control                               | Current state                                 | Current apply behavior                                   |
| ------------------------------------- | --------------------------------------------- | -------------------------------------------------------- |
| Simple / Advanced display             | Implemented/exposed                           | Live on Save                                             |
| Windowed / Borderless / Fullscreen    | Implemented/exposed                           | Live on Save                                             |
| Aspect preset                         | Implemented/exposed                           | Live on Save                                             |
| Simple resolution                     | Implemented/exposed                           | Live on Save                                             |
| Advanced application resolution       | Implemented/exposed                           | Live on Save                                             |
| Gameplay Frame Fill / MaxFit / Custom | Implemented/exposed                           | Live on Save                                             |
| Custom Gameplay Frame size            | Implemented/exposed                           | Live on Save                                             |
| Gameplay Output resolution            | Implemented/exposed                           | Live on Save                                             |
| Internal 3D scale 50–100%             | Implemented/exposed                           | Live on Save                                             |
| UI scale                              | Implemented but incomplete universal scaling  | Live on Save                                             |
| Master volume                         | Schema/UI exists; real audio consumer missing | Live setting but currently effectively inert             |
| Pointer sensitivity                   | Implemented/exposed                           | Live on Save                                             |
| Invert Y                              | Implemented/exposed                           | Live on Save                                             |
| Control grammar                       | Implemented/exposed                           | Live on Save                                             |
| Camera profile                        | Implemented/exposed                           | Live on Save                                             |
| Camera distance                       | Implemented/exposed                           | Live on Save                                             |
| Camera FOV                            | Implemented/exposed                           | Live on Save                                             |
| Render distance 1–3                   | Implemented/exposed                           | Live/session presentation rebuild without semantic reset |
| Physical bindings                     | Persisted/resolved                            | Interactive editor missing                               |
| Named control profiles                | Architecture/persistence exists               | Management UI incomplete                                 |
| Per-Realm overrides                   | Implemented                                   | Live/current session                                     |
| Server profile consent                | Implemented                                   | Session scoped                                           |
| Reset global / Realm settings         | Implemented                                   | Live                                                     |

## 8.2 Hidden/developer controls that have real implementation

| Control                                  | Current access       | Current apply mode                          | Intended classification                           |
| ---------------------------------------- | -------------------- | ------------------------------------------- | ------------------------------------------------- |
| Vegetation enable                        | `--f1-11-vegetation` | ClientRestart                               | User graphics control                             |
| Diagnostic time of day                   | CLI                  | ClientRestart today; backend can Apply live | Developer/Admin/Game later                        |
| Diagnostic atmosphere                    | CLI                  | ClientRestart today; backend can Apply live | Developer/Admin/Game later                        |
| Environment shadows                      | CLI                  | ClientRestart today                         | User/Developer graphics depending final ownership |
| User resource packs                      | programmatic API     | Live                                        | User presentation manager                         |
| Primitive-only presentation              | test/composition     | Session/Client restart                      | Developer fallback diagnostic                     |
| Official-content absent                  | test CLI             | ClientRestart                               | Test/Developer only                               |
| Official content root                    | CLI                  | ClientRestart                               | installation/developer config                     |
| Content cache root                       | CLI                  | ClientRestart                               | storage/developer config                          |
| Renderer backend NodeMultiMesh/DirectRID | code/test            | SessionRestart                              | Developer only                                    |
| Instance upload PerRow/Bulk              | code/test            | SessionRestart                              | Developer only                                    |
| Actor tiering enable                     | code-only            | SessionRestart                              | Developer/performance                             |
| Detailed actor soft budget               | code-only, 64        | SessionRestart                              | Developer/performance                             |
| Actor promote/demote ranges              | code-only, 20m/28m   | SessionRestart                              | Developer/performance                             |
| Motion prediction policy                 | code-only            | Build/Session                               | Developer diagnostics                             |
| Remote interpolation policy              | code-only            | Build/Session                               | Developer diagnostics                             |
| Structural/cache capacities              | code-only            | Session/ServerRestart                       | Cold/developer config                             |

## 8.3 Vegetation-specific current parameters

| Parameter                            | Current value       | Current state          |
| ------------------------------------ | ------------------- | ---------------------- |
| Enabled                              | Off by default      | hidden CLI             |
| Patches per eligible base Grass cell | 1                   | code policy            |
| Maximum patches/chunk                | 1,024               | code policy            |
| Blades/patch                         | 8                   | code policy            |
| Vertices/blade                       | 12                  | code policy            |
| World seed                           | `0xF1112026`        | code policy            |
| Profile version                      | 1                   | code policy            |
| Grass casts shadows                  | Off                 | code policy            |
| Wind                                 | shader/presentation | implemented            |
| Cut factor                           | programmatic/debug  | live presentation-only |
| Intentional void exclusion           | yes                 | semantic rule          |
| T1.1 additional-surface vegetation   | not implemented     | deferred               |

## 8.4 Server DirectConnect cold startup controls

Current implemented startup options include:

```
--bind=
--content-bind=
--identity-dir=
--official-content-root=
--client-profile=
--authority-hz=
--maximum-input-hz=
--recommended-input-hz=
--input-burst=
--input-burst-window-ms=
--network-service-delay-ms=
--maximum-participants=
--same-address-admission-burst=
--telemetry=
--unreliable-frame-cap=
--unreliable-aggregation-scope=
--public-representation=
--public-cadence=
--public-compression=
```

These are currently **ServerRestart** values.

Do not convert all of them into casual live sliders merely because the Control Plane can describe them.

## 8.5 Server APIs that are live-capable underneath

When explicitly permitted by runtime construction policy:

- playback rate may change live at owner-thread safe boundaries;
- simulation frequency may change live at owner-thread safe boundaries.

Current default server posture keeps those capabilities disabled/fixed.

The future operator surface should display writability based on the active runtime capability, not assume it.

## 8.6 Current Server Monitor

The current in-game Server Monitor is deliberately **read-only**.

It exposes sanitized runtime/participant/timing information.

Its only admitted action is Close.

Writable admin controls are not hidden behind it today; they still need an authority/control implementation.

## 8.7 Current player chat commands

Current player slash-command registry contains only:

```
/help
/where
```

This is not an admin command system.

## 8.8 Missing high-value control surfaces

Before many-player testing, prioritize:

- Vegetation normal setting;
- interactive key binding editor;
- Inventory viewer profile;
- resource-pack manager if scope permits;
- real audio category controls after audio exists;
- admin capability model;
- writable Server Monitor/Admin viewer;
- canonical server command/action registry;
- RunUO-style operator console;
- Godot server GUI;
- Save/Kick/Pause/Playback/Teleport/Spawn-style actions;
- coherent developer controls for renderer/motion/network experiments.

---

# 9. Inventory viewer profiles

Inventory authority and Inventory viewer are separate concepts.

The existing authoritative Inventory supports operational Unplaced and Grid semantics. Slot/Freeform vocabulary may exist but must not be faked as operational UI behavior until the authoritative modes are real.

## 9.1 Initial profiles

Implement:

```
D2 Grid
List
```

**D2 Grid** becomes the intended normal UltraSim backpack experience.

**List** remains an alternate/accessibility/large-container/fallback view.

## 9.2 Permanent invariant

Given:

```
Sword
    Grid position = (2,1)
    Footprint = 1×3
```

then:

```
D2 Grid → List → D2 Grid
```

must preserve `(2,1)` and the exact authoritative footprint/state.

Viewer switching changes presentation/input grammar, not Inventory truth.

## 9.3 Compatibility resolver

A preferred viewer may be incompatible with an authoritative placement kind.

Example:

```
Preferred = D2 Grid
Container = Unplaced
```

must resolve to a compatible List viewer rather than inventing Grid coordinates.

Future viewers may include:

- Freeform;
- Slot;
- Bank;
- Vendor;
- Trade;
- specialized container views.

## 9.4 Equivalent-journey testing

Run equivalent semantic journeys through supported viewers:

- open;
- inspect;
- reposition;
- split stack;
- equip/unequip;
- move between containers;
- drop/pickup;
- capacity changes;
- save/load;
- Remote reconnect;
- switch viewer while open.

Game outcomes must match.

---

# 10. Server command authority and frontends

Server operation before many-player testing must no longer require debugger-only harnesses or source edits.

## 10.1 Canonical command/action authority

Build one typed authoritative server action system.

Representative initial actions:

```
save
shutdown
pause/resume
playback speed
kick participant
teleport
spawn
set time / environment where domain ownership permits
content/presentation reload where safe
```

The exact first set should remain bounded.

Console, Godot GUI and in-game admin must route to the same semantic actions.

## 10.2 RunUO-style console

Provide an exported/headless console host that has:

- normal server log/status stream;
- actual command input line;
- parsing into typed validated action requests;
- clear success/rejection output;
- graceful shutdown/save behavior;
- useful startup/failure UX.

## 10.3 Godot server GUI

Provide a simple exported GUI server host with:

- status;
- participants;
- tick/runtime state;
- logs;
- command input;
- buttons for common actions;
- published Control Plane categories;
- restart-required indicators;
- capability-aware disabled controls.

It must remain a frontend, not server authority.

## 10.4 In-game Server Monitor evolution

Evolve the existing Server Monitor into another Control Plane viewer.

Normal diagnostic roles may see read-only values.

Authorized operator/admin capability may reveal writable controls/actions.

Do not build a separate unrelated admin architecture.

---

# 11. Audio Kernel + Sound Engine

Silence materially distorts movement, interaction, UI and environmental feedback.

Audio is therefore required before many-player testing.

## 11.1 Layer split

Canonical conceptual split:

```
Audio Kernel
    device
    mixing
    buses/channels
    streaming
    spatial source/listener
    volume/mute
    resource lifetime
        ↓
Sound Engine
    semantic sound presentation
    event→cue mapping
    ambience behavior
    game-facing sound logic
        ↓
Game Implementation / Content
    footsteps on grass
    cabin door sound
    katana hit cue
    forest ambience profile
```

Godot is the current Audio adapter, not the architecture owner.

## 11.2 Minimum representative vocabulary

Implement enough sound that the slice no longer feels silent:

- footsteps;
- jump;
- land;
- door open/close;
- pickup/drop/equip;
- primary action/weapon cue;
- UI click/open/close;
- forest/wind ambience;
- useful chat/notification cue where appropriate.

## 11.3 Settings

Make existing Master Volume real.

Add categories as earned:

```
Master
SFX
Ambient
UI
Music, when music exists
```

Publish them through the Control Plane.

Do not wire Settings widgets directly to Godot nodes as the permanent ownership model.

---

# 12. Representative visual vertical slice

Before many-player feedback, Cabin in the Woods should stop reading primarily as a renderer/blockout benchmark.

This is not a final-art requirement.

It is a representative-language requirement.

## 12.1 Minimum pass

Target:

- recognizable log cabin exterior/interior;
- roof/window/door presentation;
- pine-tree silhouettes instead of brown rectangles;
- multiple irregular rock forms instead of cubes;
- terrain/grass material treatment;
- discoverable vegetation setting;
- representative props/clutter;
- intentional daylight/lighting;
- readable interior/exterior transitions;
- existing actor/body presentation integrated coherently.

## 12.2 Preserve primitive fallback

Do not delete the primitive/blockout substrate.

It remains the emergency missing-asset/debug fallback.

Representative art must continue to prove:

```
missing asset
    ↓
obvious safe fallback
    ↓
semantic interaction still works
```

---

# 13. UI/product polish

The target is not final UX perfection.

The target is to remove obvious development-harness friction from the journeys external testers will actually use.

Required targets:

- Settings organization driven by User controls;
- restart/apply-mode labels;
- requested/effective/pending visibility where useful;
- interactive binding editor;
- D2 Inventory viewer;
- Server Monitor/Admin viewer;
- consistent gump/window behavior;
- readable errors/failures;
- reasonable spacing/type hierarchy;
- useful tooltip/help text;
- visible connection/session state;
- basic accessibility/UI scaling cleanup;
- no intended ordinary feature accessible only by secret CLI prefix.

UI remains downstream of semantic ownership.

---

# 14. World campaign relationship

W8–W10 remain important but are not automatic blockers for this campaign.

Current independent World roadmap remains:

```
W8 — atomic authoritative Pack Add / Replace / Remove
W9 — editor/private authoring, create/delete/fill/paint across Pack boundaries
W10 — durable runtime mutable topology
```

The current canonical World already supports sparse multi-Pack composition and seamless Pack-boundary traversal. Crossing a Pack boundary remains a storage/composition event, not a gameplay transition.

Do not smuggle W8–W10 into productization unless a specific live-test requirement proves them necessary.

---

# 14.1 Near-term Liquid Bodies / Water capability

Water is needed sooner than most of the future Engine backlog because oceans, lakes, ponds, puddles and pools are ordinary World/content needs.

Do **not** implement a Water-only primitive.

The reusable capability should begin as a narrow Liquid/Fluid contract:

```text
Liquid.Contracts
    LiquidId
    LiquidBodyId
    surface / depth / occupancy queries
    amount / capacity / level vocabulary

Liquid.Core
    bounded body ownership
    finite vs source/unbounded semantics
    fill/drain/transfer only as earned

Game Implementation
    Water
    Lava
    Honey
    Acid
    LiquidMagic
    ...
```

Permanent rule:

> **Water is content. Liquid is the Engine capability.**

The first Liquid Bodies MVP should be sufficient to author and query:

```text
ocean
lake
pond
puddle
pool / basin
```

with arbitrary `LiquidId`, surface level, body shape/depth relationship, and finite or source/unbounded policy.

It does **not** initially require:

```text
cell-by-cell flow
pressure
pipes
flood simulation
mixing/chemistry
temperature/phase simulation
```

Liquid authority remains separate from rendering. A body can semantically contain Water, Honey or Lava even if presentation falls back to a debug material.

This work may be pulled forward in parallel with P1–P4 when World/content authoring needs real water; it is not required to wait for every Framework/Engine extraction checkpoint if its contract remains narrow and typed.

The broader authoring/extensibility catalog is tracked separately in [FUTURE_CONTENT_AND_ENGINE_EXTENSIBILITY.md](FUTURE_CONTENT_AND_ENGINE_EXTENSIBILITY.md). It studies useful lessons from Forge/RunUO-style extension layers, but the objective is to improve UltraSim's normal first-party content/Engine authoring path rather than create a separate Mod API. Only items explicitly promoted into this roadmap become current campaign requirements.

---

# 15. Implementation campaign

The exact checkpoint numbering may evolve, but preserve the dependency order.

---

## Gate 0 — F1.12 product dogfood / architecture-readiness closure

**Status:** CLOSED by F1.12-R3 / Gate 0 closure at production final `e3a63ea21f8f5ea4fa69e3768a055c409edc8616`. P1 is now in progress below.

### Goal

Finish known client mechanical/product blockers in the existing F1 campaign before beginning the broader post-F1 Framework/productization campaign.

### Checklist

- F1.12 full dogfood matrix completed.
- Every discovered issue classified as architecture/mechanical, missing product behavior, polish, or deferred.
- All reproducible architecture/mechanical blockers repaired.
- Blocking missing product behavior repaired.
- SinglePlayer exported journey passes.
- Online cold journey passes.
- Online warm/reconnect journey passes.
- Complete Local/Remote/Local lifecycle passes.
- Settings/display/profile changes preserve semantics.
- UI/Chat/context/pointer/camera/movement paths mechanically pass.
- actor tiering/product presentation passes.
- multi-Pack/Far Gate/W7 controls remain green.
- primitive-only fallback remains usable mechanically.
- vegetation opt-in journey remains mechanically correct.
- current product workload shows no unexplained monotonic resource leak.
- architecture verifier passes.
- uninterrupted canonical Release passes.
- worktree clean.
- F1.12 marked CLOSED.

### Gate outcome

F1.12 green means:

> the current product architecture is credible enough to build the representative product slice on top of it.

It does not yet mean broad testing should begin.

---

## P1 — Framework Control Plane foundation

**Status:** ACTIVE — implementation and focused owner/viewer proofs pass; canonical Release closure is blocked by the [exported Single Player native crash](../../_Benchmarks/Baselines/framework-p1-control-plane-2026-09-27.md), independently reproduced with unchanged pre-P1 source. Its precise native lifetime cause remains unresolved.
See the [implemented contract](../../Framework/ControlPlane/README.md). The registry is bounded,
synchronous and owner-thread-bound; provider generations fence lifetimes while revisions describe
owner state. Batch atomicity is provider-local, not global. ECS Kernel dependencies remain unchanged.
The proof adapters are opt-in; P1 has not redesigned F4/F10 or begun P2.

### Goal

Build the stable provider/control/action/collection substrate before wiring large amounts of new Settings/Admin UI.

### Tasks

- Define stable Provider IDs.
- Define stable Control IDs.
- Define category paths.
- Define Diagnostic / Control / Action / Collection descriptors.
- Define typed `ControlValue` contracts.
- Define requested/effective/pending semantics.
- Define provenance semantics.
- Define apply modes.
- Define read/write capability requirements.
- Define provider lifecycle/generation fencing.
- Define action request/result contract.
- Define admin principal/audit contract.
- Implement registry/routing.
- Prove duplicate IDs fail cleanly.
- Prove stale provider generations reject commands.
- Prove invalid values reject before mutation.
- Prove unauthorized reads/writes fail correctly.
- Prove registry work does not enter hot ECS/network/render loops.
- Add ECS provider adapter.
- Add ServerRuntime provider.
- Add current Client settings provider/adapter.
- Build one explicit **ECS Control Panel proof** from the generic provider model: enumerate registered systems and expose their identity/stage/schedule/execution metadata, timing/work diagnostics, and only those controls proven safely writable. Cold graph/read-write/dependency facts remain read-only.

### Exit criteria

- Viewer enumerates providers/categories without concrete-type knowledge.
- Read-only and writable surfaces both work.
- Owners remain owners; UI cannot mutate internal fields directly.
- provider retirement is safe.
- the ECS proof requires no ECS-specific hard-coded tabs in the viewer; ECS publishes and the viewer organizes/renders.
- no hot-path allocation/lookup regression.

---

## P2 — Framework Runtime Host + Server Authoring Environment

### Goal

Create deterministic RunUO-like startup composition **and an actual C# server-authoring environment**, rather than only a package loader.

### Package/composition tasks

- Package manifest format.
- Stable package IDs/versions.
- Definition/package alias and migration/remap policy for renamed or removed persistent identities; runtime dense IDs are never the persistence compatibility contract.
- Deterministic migration ordering/conflict detection so required remaps complete before World/server authority starts.
- required/optional/provided capability vocabulary.
- dependency graph validation.
- deterministic load/compile order.
- Contracts/Core/Scripts conventions.
- Kernel/Framework/Engine package classification metadata where useful.
- startup composition generation identity.
- stable `RuntimeCompositionFingerprint`.
- rollback/failure handling.
- package diagnostics through Control Plane.
- policy for schema-extending packages.
- source compiler/load isolation appropriate to deployment.
- compile cache that cannot make stale output authoritative.
- built-in and dynamically compiled packages participate in the same capability/lifecycle vocabulary where practical.

### Server authoring tasks

- Establish a per-server `Scripts/` source/package tree.
- Compile dependency stages deliberately: Contracts/schema vocabulary first; Core/runtime providers next; Scripts/Profiles/Definitions after their dependencies are available.
- Preserve normal strongly typed C# authoring, generics/value types/source-generated IDs where useful.
- Return compiler errors with package, source file, line/column, and dependency context.
- Define explicit package lifecycle hooks such as Register/Configure/Initialize/Stop rather than relying on uncontrolled static initialization.
- Define standard registration seams for:
  - commands/actions;
  - Control Plane providers;
  - definition facets and definition stores;
  - definitions/content declarations;
  - ECS systems;
  - Engine capabilities;
  - typed subsystem-owned hook/event subscriptions and publications, with explicit observe/modify/cancel/order/failure semantics where earned;
  - server-composed UI/control providers.
- Make compile/cache manifests diagnosable through the Control Plane/operator product.
- Ensure failed compilation or registration leaves no partially authoritative composition.

### Contracts-only survivability proof

Treat this as a named permanent architecture acceptance:

```
optional Engine package installed
    Contracts + Core + Scripts
        ↓
remove Core + Scripts
retain Contracts
        ↓
unrelated packages still compile/load
authored optional facets retain identity/meaning
capability is simply inactive
```

A consumer that truly **requires** the missing runtime capability must fail composition explicitly. A consumer that merely references optional Contracts/facets must remain valid.

### Proof package

- Load at least one real dynamic C# package end-to-end.
- Provide one small sample shard/server package demonstrating edit source → start server → compile → register → initialize → appear in diagnostics/Control Plane.
- Remove an optional Core while keeping declared optional Contracts-valid consumers loadable.
- Missing required capability fails before World start.
- Failed composition leaves no half-started authority.
- Changing source invalidates the correct compile-cache entry; unchanged source/dependencies may safely reuse the cache.

### Exit criteria

- A server owner can add/edit an ordinary C# package without rebuilding `UltraSim.Server` itself.
- Compile/load diagnostics identify the actual failing package/source/dependency.
- package lifecycle is deterministic and bounded.
- Contracts-only survivability proof passes.
- built-in versus dynamic deployment does not require unrelated public contracts.

---

## P3 — Framework adapter/runtime boundary migration

### Goal

Replace the older ambiguous “Engine Host/engine handler” concept with a clean Framework Runtime Host plus platform adapter seams.

### Tasks

- Define runtime-service identity/lifecycle contract.
- Define capability publication.
- Define platform adapter lifecycle.
- Define headless behavior.
- Define Godot adapter boundary.
- Migrate selected existing engine-facing services only where the new seam provides concrete value.
- Ensure dedicated server works without rendering/audio/input adapters.
- Ensure Game authority does not depend on Godot types.
- Separate implementation selection from runtime configuration.

### Selection/configuration distinction

Example:

```
Presentation implementation = Godot adapter       [composition/restart]
Vegetation = On                                  [possibly live]
```

Do not imply replacing the implementation is live merely because one configuration value is live.

### Exit criteria

- adapters can register/retire with clear ownership.
- adapter diagnostics/controls appear through Control Plane.
- headless composition has no irrelevant presentation/audio requirements.
- current gameplay code no longer needs arbitrary direct Godot coupling where the new seam was selected.

---

## P4 — Engine Contracts + typed definition facets

### Goal

Prove removable Engine Cores and cross-Engine definition composition without lateral Core coupling.

### Tasks

- formal Engine package structure: Contracts/Core/(optional profiles/scripts).
- typed facet registration seam.
- typed Definition Tag/Set registration so unrelated packages can contribute members to shared semantic groups without lateral Core dependencies.
- stable/dense facet and tag/set IDs as appropriate.
- typed stores.
- builder extension pattern.
- duplicate/conflict validation.
- deterministic tag/set contribution diagnostics and optional cross-package membership without hidden hard dependencies.
- definition alias/remap integration for persisted authored identities.
- dormant optional facet semantics.
- explicit required-capability semantics.
- semantic asset IDs.
- representative cross-feature item definitions.
- prove at least one representative first-party Game definition/content addition uses the same facet/tag/contract seams intended for external packages.

### Representative proof

Create/compile items spanning:

```
Items
Presentation
Inventory
Equipment
Combat
Durability
Skills
```

### Exit criteria

- Inventory.Core does not depend on Combat.Core merely because an item uses both.
- Combat.Core does not depend on Inventory.Core for the same reason.
- deleting/disabling an optional Core preserves declared-valid authored meaning.
- the named Contracts-only survivability test from P2 passes for at least one real Engine package/facet composition.
- hot runtime lookup is typed/bounded.

---

## P5 — User/Admin/Developer control surfaces

### Goal

Turn the control census into discoverable surfaces without exposing test harness plumbing as product UX.

### Classification pass

For every discovered control/parameter, classify:

```
User
Admin
Developer
Test
Cold Configuration
```

### Minimum generic viewer/widget vocabulary

The Control Plane viewer must earn enough generic presentation vocabulary to render real providers without one-off subsystem widgets. At minimum support:

- Boolean/toggle controls;
- enum/select controls;
- bounded integer/decimal input (spinner and/or slider where appropriate);
- text/string input;
- action/button;
- bounded table/collection;
- hierarchical category/tree/tab organization;
- disabled/read-only state with reason;
- requested/effective/pending and restart-required presentation;
- confirmation for destructive/authoritative actions.

Do **not** port every archived V2 UI widget merely because it existed. Add the generic vocabulary actually earned by current User/Admin/Developer consumers.

### Server-composed UI visual vocabulary

Before representative PF6 UI is treated as architecturally complete, extend the neutral UI
definition/presentation contract with the UO-inspired visual primitives documented in
[Docs/UI.md](../UI.md):

- single-image backgrounds/panels;
- tiled images;
- resizeable NineSlice and/or NinePiece frame backgrounds with explicit tile/stretch policy;
- stateful button visual roles with Normal/Hover/Pressed/optional Released/optional Active and
  deterministic fallback;
- image-backed Toggle/checkbox visuals;
- exclusive/radio-choice semantics when a real first-party surface uses them;
- dynamic admitted semantic presentation references `DefinitionDomainId + StableDefinitionId + PresentationRoleId` so Game-level item/portrait/icon helpers do not leak domain types into the UI kernel;
- 3D-derived portrait realization as the normal fallback when a definition has World presentation but no dedicated 2D portrait; dedicated icons remain optional overrides;
- bounded derived portrait caching and compatibility with the UI/Icon atlas path;
- bounded optional Live Portrait / Inspection presentation vocabulary without one live 3D scene per visible UI item;
- bounded image tint/opacity/clipping semantics;
- per-control TextInput maximum length and text overflow policy where earned;
- renderer-neutral per-element tooltip/help binding where earned.

Keep server/Game authoring compact and gump-like. Do not expose Godot `TextureButton`,
`NinePatchRect`, theme resources, filesystem paths, or raw HTML as the semantic contract.

The existing Canvas/Vertical/Horizontal/Overlay/Scroll nesting model remains canonical; do not
replace it with a flat legacy command stream.

### ECS Control Panel proof

Use the P1 ECS provider as a concrete proof of the inverted V1.5/ACC model. The viewer should be able to show, without knowing concrete ECS system types:

- registered systems;
- stable system key/identity;
- stage;
- enabled/effective schedule;
- period/offset;
- execution policy/parallel policy where relevant;
- bounded timing/work/row diagnostics where measured;
- dependencies/access metadata as read-only diagnostics;
- safe writable controls only where the scheduler/runtime explicitly supports them.

### User Settings

- F10 becomes a Control Plane User viewer.
- preserve existing display/camera/control settings.
- vegetation normal On/Off setting.
- interactive binding editor.
- inventory viewer preference placeholder/contract ready for P6.
- resource-pack manager if included in scope.
- apply/restart indicators.
- requested/effective/pending where relevant.
- disabled reason/tooltips where useful.

### Developer viewer

- renderer diagnostics.
- actor tier diagnostics/overrides where safe.
- motion/interpolation diagnostics.
- vegetation/environment diagnostics.
- network/publication diagnostics.
- test-only flags remain hidden from ordinary product UI.

### Admin viewer

- F4 Server Monitor migrates to Control Plane diagnostics.
- authorized write surfaces appear only with server-granted capability.
- ordinary users remain read-only where allowed.

### Exit criteria

- ordinary intended features no longer require undocumented CLI flags.
- generic viewers can render the minimum control vocabulary without subsystem-specific panel surgery.
- representative server-composed UI can use authored image panels, tiled/nine-slice backgrounds and image-state buttons without Godot-specific Game/server code.
- ECS Control Panel proof works through publication/discovery rather than concrete UI knowledge.
- current Settings behavior regresses nowhere.
- user/admin/developer/test boundaries are explicit.

---

## P6 — Inventory Engine viewer profiles

### Goal

Make the intended D2-ish Inventory presentation real and human-testable.

### Tasks

- viewer-profile contract.
- formal List viewer.
- D2 Grid viewer.
- item cells/rows resolve `PresentationRole.InventoryPortrait` through the generic semantic presentation reference.
- prove a representative 3D-only item definition (no hand-authored icon) derives and caches a stable Inventory portrait.
- prove missing portrait-specific art falls through World presentation / primitive fallback rather than hiding a valid item.
- keep live/animated portrait realizations bounded; normal multi-item inventory display remains cached-image based.
- preferred viewer User control.
- compatibility resolver/fallback.
- live switching.
- no semantic mutation on switching.
- equivalent action journeys.
- save/load coverage.
- Remote reconnect coverage.
- switch while open.
- accessibility/large-container considerations.

### Exit criteria

- D2 Grid is credible as intended UltraSim default.
- representative inventory items can display from semantic presentation identity without requiring bespoke 2D icons.
- List remains usable alternate/fallback.
- authoritative placement/state remains exact across switches.
- semantic outcomes equivalent across supported viewers.

---

## P7 — Server Operator Product: command authority, console, GUI, save/backup/recovery

### Goal

Make exported servers operationally credible without debugger/source-only workflows. Reassemble the strong V3 backend into a real operator product rather than treating startup CLI switches as administration.

### Core command/action authority

- canonical typed server action registry/routing.
- admin capability model.
- principal/audit path.
- Save action.
- graceful Shutdown action.
- Restart / Restart-with-save behavior where runtime/host semantics support it safely.
- Kick action.
- Pause/Resume where runtime semantics support it.
- Playback control where runtime opted in.
- initial bounded Teleport/Spawn/etc. actions as actually needed.
- cold config clearly separate from live controls.

### Save, autosave, backup and recovery

V3 already has the stronger crash-durable coherent save-generation machinery. The missing work is an operator-facing policy/product around it.

Implement/define:

- operator-triggered Save Now;
- scheduled autosave policy with explicit enabled/cadence/configuration ownership;
- backup/snapshot policy and bounded retention;
- operator-triggered backup/snapshot where appropriate;
- startup diagnostics showing current durable generation/last successful save;
- tested restore/recovery workflow using existing transactional persistence semantics;
- clear handling of failed/incomplete save candidates;
- no frontend directly manipulating persistence internals.

Compressed/rotating archival format is not required to reproduce V2 exactly; the requirement is a credible bounded backup/recovery operator product over V3 persistence.

### Participant/account administration surface

Expose the identity information that actually exists, without pretending missing credential/product flows are complete:

- participants/session identity;
- account/character identity where admitted;
- enabled/disabled/account state supported by current V3 semantics;
- Kick and other genuinely implemented administrative actions;
- room for richer account administration later without making P7 depend on full account-product completion.

### Console

- exported/headless console host.
- live log/status stream.
- real persistent input line.
- command parsing into typed semantic requests.
- result output.
- startup/shutdown/failure UX.
- shared action semantics with all other frontends.

### Godot GUI server

- exported GUI host.
- status/participants/tick/log views.
- command input.
- common action buttons.
- Control Plane category rendering.
- restart-required labels.
- save/autosave/backup/recovery status/actions.
- participant/account views bounded to actually implemented authority.

### In-game admin

- same semantic actions through authorized F4/Admin viewer.
- no direct frontend-owned server behavior.

### Exit criteria

- Console, GUI, and in-game Admin invoke the same semantic actions.
- unauthorized requests reject cleanly and authoritative mutations are auditable.
- scheduled autosave and explicit Save are exercised.
- backup/restore/recovery workflow is exercised against real save generations.
- startup/shutdown/restart/reconnect paths are documented and tested.
- server can be operated without debugger/source edits.

---

## P8 — Audio Kernel + Sound Engine representative implementation

### Goal

Make the vertical slice audibly representative and prove new Framework/adapter/control boundaries.

### Tasks

- Audio Kernel contracts/core as generic as justified.
- Godot audio adapter.
- Sound Engine semantic event→cue layer.
- content/cue resolution.
- footsteps.
- jump/land.
- doors.
- pickup/drop/equip.
- primary/action cue.
- UI sounds.
- forest/wind ambience.
- useful notification cue.
- make Master Volume effective.
- add SFX/Ambient/UI categories as earned.
- publish audio controls through Control Plane.
- local/remote semantic-event consistency.

### Exit criteria

- representative play is not silent.
- controls audibly affect realization live where intended.
- sound presentation cannot change Game outcomes.

---

## P9 — Representative Cabin visual pass

### Goal

Replace the most distracting placeholders with a representative vertical slice.

### Tasks

- log cabin exterior.
- cabin interior.
- roof/window/door presentation.
- pine trees.
- irregular rock variants.
- terrain/grass material treatment.
- vegetation discoverability/default decision.
- representative props/clutter.
- intentional daylight/lighting.
- readable interior/exterior transitions.
- actor/body integration.
- missing-asset primitive fallback regression.
- content precedence remains User → Realm → world.local → Primitive.

### Exit criteria

- first-time testers can understand intended visual language.
- primitive fallback still works mechanically.
- visual replacement changes no Game semantics.

---

## P10 — UI/failure/product polish

### Goal

Remove obvious development-harness friction from real test journeys.

### Tasks

- spacing/type/layout cleanup.
- Settings organization.
- restart/apply indicators.
- inventory interaction polish.
- admin/control viewer polish.
- connection/admission/disconnect UX.
- server unavailable/content unavailable/trust failure UX.
- session transition clarity.
- destructive admin-action confirmations.
- basic accessibility/scaling pass.
- visible connection/session state.
- no critical feature only reachable through hidden launch syntax.

### Exit criteria

- core journeys are discoverable without developer instruction sheets.
- failures explain what happened and what the user/operator can do next.

---

## P11 — Focused individual/trusted dogfood burn-down

### Goal

Use the representative slice to find sequencing, feel, lifecycle, operability and edge-case defects before many-player noise is introduced.

### Exercise at minimum

- SinglePlayer export.
- Online export.
- Complete export.
- console server.
- Godot server GUI.
- admin actions.
- Settings/control changes.
- D2/List Inventory switching.
- movement/camera profiles.
- Chat/commands.
- save/load.
- reconnect.
- second-Realm/profile scoping where fixtures support it.
- W7 multi-Pack/Far Gate traversal.
- item pickup/equip/drop lifecycle.
- vegetation On/Off.
- audio category controls.
- presentation fallback/missing-resource behavior.
- session churn.
- long-running server/client soak.
- lower-spec hardware where available.

### Finding ledger

Every issue gets:

```
ID
severity
classification
reproduction
owner
status
disposition
commit/fix reference
blocks PF6?
blocks many-player live?
```

### Exit criteria

- no known architecture/mechanical blocker.
- no known high-severity product blocker.
- remaining polish/deferred work explicit.
- operator workflow credible for an external closed test.

---

## Gate 1 — Identity / character-mode decision

### Goal

Decide deliberately what participant identity and persistence promise the first PF6/many-player campaign is making. V3 has useful Account/Character/Admission foundations, but the current public Concept Demo journey remains guest-oriented and does not yet provide the full old account credential + character create/select/delete product flow.

Before PF6, choose and document exactly one initial posture:

### Mode A — Guest/disposable test identity

Use this if the test is primarily about platform/gameplay feel and participants are **not** promised durable long-lived progression.

Require:

- guest identity semantics are explicit to participants;
- reconnect/session expectations are documented;
- no UI implies durable account/character ownership that is not actually guaranteed;
- operator tools can still identify participants sufficiently for the test/audit model.

### Mode B — Persistent progression identity

Use this if participants are expected to accumulate meaningful durable character progress across sessions.

Then the campaign must promote the necessary account-backed admission and character-product flow into pre-live scope, including the minimum credible:

- account/authentication product path;
- character select;
- character creation if participants need to create their own;
- deletion/recovery policy if exposed;
- durable reconnect identity;
- operator/account diagnostics appropriate to the selected scope.

Do not build full historical V2 credentials/RBAC merely because it existed if Mode A is sufficient for the first test.

### Exit criteria

- the selected identity mode is recorded in the PF6 handoff;
- UI/server behavior matches that promise;
- any missing capability required by the selected mode is implemented and tested before PF6.

---

## PF6 — Closed external human validation

PF6 is not the mass live test, and it is not a claim that “the game is complete.”

It is a bounded closed validation of a representative real slice: enough architecture, authoring/composition, product behavior and presentation must exist together that humans can judge whether the platform is stable and usable under an intended scenario rather than merely passing isolated subsystem tests.

By PF6, the project should have exercised its own normal content-authoring seams substantially enough that, after PF6 defects are repaired, expanding UltraSim increasingly means adding definitions, profiles, scripts, packages and content rather than repeatedly inventing bespoke engine hooks.

It has enough representative product quality that humans can judge things automation cannot.

### Human evaluation focus

- movement/camera feel.
- correction/interpolation feel.
- Inventory UX.
- Settings discoverability.
- UI readability.
- crowd/actor presentation.
- content acquisition/failure UX.
- server/operator experience.
- audio feedback/mix.
- visual clarity/perceived completeness.

### PF6 rule

Do not use scarce human attention to rediscover:

- missing obvious controls we already know are missing;
- known architecture defects;
- secret CLI requirements;
- placeholder art that obviously is not representative;
- test harness plumbing.

### Exit criteria

- closed external test completed.
- feedback triaged.
- blockers fixed or explicitly accepted with rationale.
- defaults adjusted using real human/performance evidence.
- many-player entry review completed.

---

# 16. Many-player live test entry criteria

UltraSim is ready for a many-player live/dogfood test only when all relevant items below are true.

## 16.1 Architecture

- F1.12 closed.
- Control Plane stable enough that new controls do not require hard-coded panel surgery.
- Runtime Host package lifecycle proven end-to-end.
- C# server-authoring flow (Scripts tree → compile/cache → register/init) proven end-to-end.
- Contracts-only survivability acceptance proven.
- stable composition fingerprint exists.
- Framework/platform adapter boundary established for selected services.
- typed definition/facet composition proven.
- representative Engine Cores remain laterally decoupled.
- Game outcomes remain independent from viewer/presentation choices.
- architecture verifier enforces useful dependency boundaries.

## 16.2 Operability

- exported server can run without debugger.
- console server frontend exists.
- Godot server GUI exists, or campaign explicitly accepts console-only for first many-player test.
- admin actions authenticated/authorized.
- audit/principal trail exists for authoritative admin mutation.
- status/logging/failure output useful.
- graceful save/shutdown exercised.
- scheduled autosave policy exercised.
- backup/recovery/restore workflow exercised.
- cold config clearly distinguished from live controls.

## 16.3 User controls

- intended features are not hidden behind undocumented flags.
- Vegetation has discoverable normal setting.
- key bindings editable.
- display/camera/control settings remain correct.
- Inventory viewer switch implemented.
- audio controls are real.
- restart-required values say so.
- requested/effective/pending behavior is not misleading.

## 16.4 Product slice

- D2-style Inventory representative.
- List Inventory retained as alternate/fallback.
- basic SFX/ambience exists.
- Cabin/trees/rocks/terrain/grass are recognizable representative assets.
- UI no longer reads primarily as bare test harness.
- primitive/missing-asset fallback remains functional.

## 16.5 Correctness and quality

- canonical architecture verification passes.
- uninterrupted canonical Release/export verification passes on tested tree.
- long-session lifecycle/reconnect/save-load paths pass.
- no known P0/P1 data corruption/authority/lifecycle defects.
- no known blocker predictably makes external feedback low-value.
- lower-spec/performance evidence sufficient for honest defaults.
- vegetation default selected from evidence rather than assumption.
- client/server resource trends show no unexplained monotonic growth under stable load.

## 16.6 Human validation

- individual dogfood performed continuously.
- trusted/internal dogfood completed on representative slice.
- Identity / character-mode gate resolved and documented.
- PF6 closed external validation completed.
- PF6 findings triaged into blocker / fix-now / later-polish / future-feature.
- remaining limitations documented for participants.

---

# 17. Explicit non-goals before many-player testing

Unless separately promoted to blockers, the following are not required merely to begin broader testing:

- W8 live WorldPack Add/Replace/Remove;
- W9 complete terrain/world editor;
- W10 durable mutable topology;
- arbitrary hot unload/reload of every package;
- hostile/untrusted third-party plugin ABI;
- every future Inventory placement/viewer profile;
- final graphics settings matrix;
- final production art for the whole game;
- web administration portal;
- full fluid/flow/pressure/chemistry simulation beyond the narrow Liquid Bodies MVP unless independently promoted by World/game requirements;
- the broader future Engine/modding backlog merely because it has been documented;
- perfect low-end optimization for every experimental feature;
- dynamic replacement of persistent/network/component schemas at runtime;
- final GI/lighting technology;
- universal GPU actor interpolation;
- skeletal crowd batching/impostors unless independently earned by evidence.

Keep these explicit so the pre-test campaign does not expand indefinitely.

---

# 18. Anti-patterns to reject

Do not close this campaign by introducing shortcuts that erase the architecture we just established.

Reject:

- giant `Dictionary<string, object>` system/definition/config bags;
- reflection exposing arbitrary public properties as admin controls;
- UI widgets mutating subsystem internals directly;
- one Control registry owning every subsystem's persistence;
- per-frame/per-tick Control Plane lookups in hot paths;
- Inventory.Core depending on Combat.Core because one item uses both;
- Combat.Core depending on Inventory.Core for the same reason;
- Godot types in permanent engine-neutral contracts;
- dynamic package startup that can leave partial authority active;
- built-in and dynamic packages using unrelated lifecycle/capability models;
- separate Save/Kick/etc. semantics for Console/GUI/in-game admin;
- pretending restart-required controls are live because a slider exists;
- exposing smoke/evidence flags as ordinary user settings;
- claiming plug-and-play means unsafe arbitrary hot unload;
- allowing requested configuration to be shown as effective before it really applies;
- anonymous authoritative admin mutations with no principal/audit identity.

---

# 19. Architecture-verifier direction

As the monorepo is cleaned up, add enforceable dependency rules approximating:

```
Kernels/*
    must not reference:
        Engines/*
        Game/*

Framework/*
    may reference:
        Kernels/*
        Bridges/*
    must not reference:
        concrete Game implementation

Engines/<X>/Core
    may reference:
        Engines/<X>/Contracts
        lower-level Kernel/Framework/Bridge contracts
        explicitly shared stable contracts
    should not reference:
        unrelated Engine Core implementations
        UltraSim.Game implementation

Game/*
    may reference:
        Engine Contracts/Cores
        Framework
        Bridges
```

These do not need to be perfect immediately.

The independent-repository test should guide what the verifier eventually enforces.

---

# 20. Durable artifacts this campaign should leave behind

Maintain these as real artifacts, ideally machine-readable plus human-readable summaries:

1. **Control census / registry report**\
   provider/category/control/action list, provenance, requested/effective/pending state, apply mode, persistence owner and authorization.
2. **Package/capability graph report**\
   built-in + dynamic packages, versions, dependencies, provided capabilities, resolved composition fingerprint.
3. **Definition/facet registry report**\
   facet types/stable IDs, owning package and active runtime consumer.
4. **Server script/package author guide**\
   package anatomy; Contracts/Core/Scripts rules; dependencies/capabilities; compile lifecycle/cache; definition facets; control publication; registration seams; removable-package rules; and a working sample package.
5. **Server operator guide**\
   startup config, live controls, console commands, GUI operation, save/autosave/backup/recovery/shutdown, common failures.
6. **Dogfood finding ledger**\
   issue classification, severity, reproduction, owner and disposition.
7. **PF6 handoff**\
   exact build, defaults, selected identity/character mode, known limitations, scenarios and questions humans should evaluate.
8. **Many-player test handoff**\
   server/client build identities, composition fingerprint, default settings, selected identity/character mode, capacity assumptions, known limitations, logging/diagnostics collection and rollback plan.

---

# 21. Short-form dependency order

```
F1.12 green
    ↓
P1   Framework Control Plane
    ↓
P2   Framework Runtime Host + C# Server Authoring Environment
    ↓
P3   Framework adapter/runtime boundary migration
    ↓
P4   Engine Contracts + typed definition facets
    ↓
P5   User/Admin/Developer control surfaces
    ↓
P6   Inventory D2/List viewers
    ↓
P7   Server Operator Product + console + Godot GUI + save/backup/recovery
    ↓
P8   Audio Kernel + Sound Engine
    ↓
P9   Representative Cabin graphics/content
    ↓
P10  UI/failure polish
    ↓
P11  focused individual/trusted dogfood burn-down
    ↓
Gate 1  identity / character-mode decision
    ↓
PF6  closed human validation
    ↓
Many-player live/dogfood test
```

Art/audio work may overlap with engineering where it does not create architectural rework.

Control ownership / package / adapter boundaries should be stabilized before large amounts of UI are hard-wired around temporary seams.

---

# 22. Final design rules

> **KERNELS MAKE APPLICATIONS POSSIBLE.**

> **CONTRACTS PRESERVE MEANING BETWEEN INDEPENDENTLY REMOVABLE PIECES.**

> **FRAMEWORK MAKES KERNELS, CONTRACTS, MODULES, LIFETIMES, CONTENT, PERSISTENCE, AND CONTROLS COOPERATE AS A PLATFORM.**

> **ENGINE CORES MAKE INTERACTIVE/GAMEPLAY CAPABILITIES POSSIBLE.**

> **GAME IMPLEMENTATION DECIDES WHICH CAPABILITIES PARTICIPATE, HOW THEY BEHAVE, AND WHAT GAME THE PLAYER IS ACTUALLY PLAYING.**

> **CONTENT IS THE CONCRETE AUTHORED STUFF CONSUMED BY THAT GAME IMPLEMENTATION.**

> **THE VIEWER IS NOT THE OWNER OF THE CONTROL IT DISPLAYS.**

> **SUBSYSTEMS PUBLISH MANAGEMENT SEMANTICS; VIEWERS DISCOVER, AUTHORIZE, ORGANIZE, AND RENDER THEM.**

> **REQUESTED, EFFECTIVE, AND PENDING CONFIGURATION ARE NOT THE SAME THING.**

> **ADMIN MUTATION MUST BE AUTHORIZED, GENERATION-FENCED, AND ATTRIBUTABLE.**

> **ENGINE CORES MAY DEPEND DOWNWARD; AVOID LATERAL CORE-TO-CORE COUPLING.**

> **USE TYPED CONTRACTS AND DENSE/STABLE IDS; DO NOT TURN EXTENSIBILITY INTO STRING-KEYED PROPERTY BAGS.**

> **THE CONTROL PLANE IS A COLD MANAGEMENT PATH, NOT A HOT-PATH DEPENDENCY.**

> **BUILT-IN AND DYNAMIC PACKAGES SHOULD PARTICIPATE IN THE SAME CAPABILITY/LIFECYCLE MODEL WHERE PRACTICAL.**

> **FIRST-PARTY GAME CONTENT SHOULD DOGFOOD THE SAME AUTHORING CONTRACTS EXPECTED OF SERVER/COMMUNITY PACKAGES; A PRIVILEGED CONTENT-ONLY BYPASS IS AN ARCHITECTURE SMELL.**

> **MOD FRIENDLINESS IS A CONSEQUENCE AND TEST OF GOOD CONTENT AUTHORING/EXTENSIBILITY, NOT A SEPARATE MOD-API LAYER.**

> **REAL AUTHOR USE CASES MAY PROMOTE NEW ENGINE/FRAMEWORK/KERNEL SEAMS, BUT THE UNDERLYING SEMANTIC NEED MUST BE REVIEWED BEFORE ADDING BESPOKE HOOKS.**

> **PLUG-AND-PLAY INITIALLY MEANS STARTUP-TIME COMPOSITION, NOT UNSAFE HOT UNLOADING.**

> **ARCHITECTURAL ROLE IS NOT DEFINED BY DIRECTORY, REPOSITORY, OR WHETHER A PACKAGE SHIPS BY DEFAULT.**

> **THE MONOREPO SHOULD BECOME LOGICALLY EXTRACTABLE BEFORE PHYSICAL REPOSITORY SPLITS ARE ATTEMPTED.**

> **MANY-PLAYER TESTING STARTS ONLY WHEN HUMANS ARE JUDGING THE INTENDED PRODUCT SLICE RATHER THAN KNOWN DEVELOPMENT-HARNESS LIMITATIONS.**

---

# 23. Final many-player GO / NO-GO checklist

Before scheduling the first many-player live test, answer every item explicitly.

## GO only if

- F1.12 is CLOSED.
- P1–P11 closure evidence is accepted or any intentionally skipped item has an explicit non-blocking rationale.
- Gate 1 identity/character mode is explicitly resolved.
- PF6 closed validation is complete.
- exact server/client build IDs recorded.
- exact package/runtime composition fingerprint recorded.
- default user graphics/settings profile recorded.
- default server cold configuration recorded.
- capacity assumptions recorded.
- console/GUI/admin operation tested.
- server script/package author workflow tested from source edit through startup composition.
- save/autosave/backup/recovery/shutdown/restart/reconnect tested.
- content acquisition cold/warm tested.
- Inventory D2/List tested.
- movement/camera profiles tested.
- audio tested.
- representative Cabin/terrain/tree/rock/grass presentation tested.
- primitive fallback tested.
- multi-Pack/Far Gate traversal tested.
- long-running resource/lifecycle soak shows no unexplained growth.
- no known P0/P1 correctness/data/authority defect.
- no known obvious product omission that will dominate participant feedback.
- logging/diagnostic capture process documented.
- issue-triage/rollback process defined.

## NO-GO if

- a known authority/data-corruption/lifecycle blocker exists;
- normal users still require secret flags for intended test features;
- server operation still requires debugger/source edits;
- the test slice is so placeholder-heavy that visual feedback will be dominated by known scaffolding;
- Inventory feedback would still target a viewer we already know is not the intended profile;
- audio is still effectively absent while we intend to evaluate interaction feel;
- settings/admin controls misrepresent whether changes are effective/restart-required;
- current known defects are likely to consume more tester attention than genuinely unknown edge cases.

When all GO conditions are satisfied, the project has crossed the intended boundary:

> **UltraSim is no longer merely architecture-complete enough to run; it is representative, operable, inspectable and configurable enough that many real players can productively discover the edge cases we do not already know about.**

---

# Appendix A — Current baseline control/default census

This appendix records the important current values discovered in the F1.11 baseline. It is a migration/reference aid, not a requirement that every raw value become a user-facing control.

## A.1 Client built-in defaults

```
UI scale                 1.0
Master volume            0.8   (currently no real audio consumer)
Pointer sensitivity      1.0
Invert Y                 Off
Control grammar          OrbitCommand
Camera profile           Orbit
Camera distance          14 m
Camera FOV               70°
Render distance          1 chunk
Display settings mode    Simple
Display mode             Windowed
Simple aspect            16:9
Simple resolution        1920×1080
Internal 3D scale        100%
Vegetation               Off
```

### Display constraints

```
Maximum dimension        4096
Maximum pixel budget     8,294,400 (3840×2160)
Internal 3D scale        50–100%
```

### Simple resolution presets

```
16:9
    1280×720
    1600×900
    1920×1080
    2560×1440
    3840×2160

16:10
    1280×800
    1440×900
    1680×1050
    1920×1200
    2560×1600

4:3
    1024×768
    1280×960
    1600×1200
    1920×1440

5:4
    1280×1024
```

## A.2 Current default physical bindings

```
Exit Application       Alt+F4
Client Menu            Esc
Disconnect             F8
Settings               F10
Move Left              A
Move Right             D
Move Forward           W
Move Backward          S
Select                  Left Mouse
Destination             Left Double Click
Context Menu            Right Mouse
Primary                 Middle Mouse
Interact                E
Inventory               I
Chat                    Enter
Jump                    Space
Server Monitor          F4
Cycle Debug             F11
Camera Look             Middle Mouse Drag
Camera Zoom In          Wheel Up
Camera Zoom Out         Wheel Down
```

Reserved controls currently include Exit, Client Menu, Disconnect, Settings, and Debug-cycle semantics; server profile recommendations may not steal them.

## A.3 Current camera/control profile vocabulary

```
Control grammars
    OrbitCommand
    ThirdPersonDirect
    IndependentMoveLook

Camera profiles
    Orbit
    ThirdPersonFollow
    IndependentMoveLook
```

Current server Concept Demo profile names:

```
orbit
third-person
independent
```

These are server recommendations, not server authority over the user's display/accessibility/audio preferences.

## A.4 Current actor presentation policy

Production F1.10 policy:

```
Tier vocabulary
    Detailed
    BatchedPrimitive

Tiering enabled               Yes
Soft Detailed budget          64
Promotion distance            20 m
Demotion distance             28 m
Controlled actor              always Detailed
Mandatory feature actors      may exceed soft budget
```

This is currently code policy, not an ordinary user setting.

## A.5 Current controlled-motion presentation policy

```
Nominal horizontal rate       6,000 mm/s
Maximum speculative lead      900 mm
Maximum unobserved time       200 ms
Prediction history capacity   128
Correction duration           180 ms
Correction deadband           20 mm
Discontinuity snap            4,000 mm
```

These values are presentation policy and must never become Game authority simply because a developer panel can inspect or override them.

## A.6 Current remote interpolation policy

```
Maximum actors                64
Samples per actor             12
Presentation delay            100 ms
Maximum sample gap            500 ms
Teleport/discontinuity        4,000 mm
Extrapolation                 None; hold newest authority
```

Again: developer diagnostics/tuning, not normal user settings.

## A.7 Current vegetation prototype details

```
Default                       Off
Enable path                   --f1-11-vegetation
Profile version               1
Maximum patches/chunk         1,024
Patches/eligible base cell    1
Blades/patch                  8
Vertices/blade                12
World seed                    0xF1112026
Grass shadow casting          Off
Wind                          shader-driven
Cut state                     presentation/debug-only
Additional T1.1 surfaces      not populated
```

Diagnostic environment controls currently include:

```
--f1-11-time=dawn|noon|dusk|night
--f1-11-atmosphere=temperate|dry|cold
--f1-11-no-shadows
```

These launch controls should migrate into correctly owned User/Admin/Developer controls where appropriate; the CLI test/debug paths may remain for automation.

## A.8 Current renderer comparison vocabulary

```
World instance backend
    NodeMultiMesh      [production]
    DirectRid          [comparison/diagnostic]

Instance upload
    PerRow             [production]
    Bulk               [comparison/diagnostic]
```

Do not expose these as ordinary graphics options. They are implementation/diagnostic choices unless future evidence changes that classification.

## A.9 Current World renderer/cache bounds of interest

Representative existing bounds include:

```
GodotWorldRenderer default
    Maximum render chunks       256
    Maximum groups/chunk        128

Client structural fringe       1 chunk
Remote pending Pack requests    8

Client semantic cache default
    Maximum resident chunks     512
    Frozen statics/chunk        64
    Multi fragments/chunk       64
    Public records/chunk        64
```

These are resource/admission policies, not raw player-facing settings.

## A.10 Current server DirectConnect defaults

```
Gameplay bind                  127.0.0.1:7777
Content bind                   127.0.0.1:7778
Client profile                 third-person
Authority Hz                   60
Maximum input Hz               60
Recommended input Hz           30
Input burst                    10
Input burst window             100 ms
Network service delay          1 ms
Maximum participants           8
Telemetry                      Off
Unreliable aggregation         CrossLane
Current public representation  Message129WhenBeneficial
```

Participant cap accepted by the current product architecture remains bounded to 64 even though the DirectConnect default launch value is 8.

## A.11 Current server startup knobs

Implemented cold/startup controls include:

```
--bind=
--content-bind=
--identity-dir=
--official-content-root=
--client-profile=
--authority-hz=
--maximum-input-hz=
--recommended-input-hz=
--input-burst=
--input-burst-window-ms=
--network-service-delay-ms=
--maximum-participants=
--same-address-admission-burst=
--telemetry=
--unreliable-frame-cap=
--unreliable-aggregation-scope=
--public-representation=
--public-cadence=
--public-compression=
```

Automation/accounting hooks also exist but must remain Test-only rather than being promoted into normal operator UX merely because they are launch parameters.

## A.12 Current live-capable server runtime values

The server runtime contains live APIs for:

```
Playback rate
Simulation frequency
```

but only when the runtime was explicitly constructed to allow those mutations.

The future Control Plane must therefore report:

```
control exists
current value
apply mode = SafePoint/Live-like owner boundary
currently writable = true/false
reason if false
```

rather than assuming every server exposes mutable time/tick policy.

## A.13 World clock defaults

```
Simulation frequency                 60 Hz
World time rate                      1×
Playback rate                        1×
Maximum simulation steps/advance     1,000,000
Variable playback allowed            No by default
Variable simulation frequency        No by default
```

## A.14 Network Kernel default reservations

Representative defaults:

```
Maximum connections                  1,024
Packet buffers                       4,096
Packet buffer bytes                  1,472
Inbound ring                         4,096
Outbound ring                        4,096
Maximum protocol messages            256

Reliable messages                    128
Received messages                    128
Maximum message bytes                16,384
Maximum fragments/message            64
Sent-packet capacity                 2,048
Inbound packets/pump                 64
Outbound packets/pump                64
Minimum retransmission steps         2
Maximum retransmission steps         128
Initial congestion window            4 packets
Maximum congestion window            128 packets
Independent unreliable messages      128
Reliable payload arena               262,144 bytes
Received payload arena               262,144 bytes
```

These are Kernel construction/resource policies. They are not candidates for ordinary live admin sliders.

## A.15 Content HTTP host defaults

```
Maximum resources                    4,096
Maximum resource bytes               768 MiB
Concurrent responses                 4
Queued responses                     8
Response timeout                     30 seconds
```

These are cold server/content-host configuration.

## A.16 WorldPack admission defaults

```
Maximum Pack bytes                   512 MiB
Maximum chunks/Pack                  65,536
Maximum sections                     32
Maximum frozen statics               1,000,000
Maximum Multi instances              262,144
Maximum Multi fragments              2,000,000
Maximum additional-surface frags     65,536
Maximum additional frags/chunk       64
```

These are hard admission/safety bounds, not target content density or gameplay limits.

## A.17 ECS cold configuration categories

`EcsWorldOptions` currently owns cold reservations for:

- entity capacity;
- maximum archetypes;
- prepared plans;
- prepared transitions;
- structural commands;
- lifecycle-history capacity;
- scheduler reservations;
- persistence identity capacity.

`EcsSchedulerOptions` currently owns:

- maximum systems;
- dependency capacity;
- work items;
- structural segments;
- worker count;
- caller participation;
- structural commands/payload per producer;
- partitions/system;
- profiling enabled;
- phase staggering enabled;
- stagger-phase bound.

These are useful diagnostics/configuration metadata, but only values proven safely mutable should become writable Control Plane controls.

## A.18 Current Inventory reality

Authoritative behavior currently supports the operational concepts needed for:

```
Unplaced containers
Grid containers
item footprints
Grid coordinates
repositioning
stack splitting
equip/unequip
transfer
drop/pickup
```

The current viewer remains largely List/table oriented and presents Grid information/cell choices without being the intended D2-style spatial Inventory.

Therefore:

```
List viewer             present but not yet formalized as profile
D2 Grid viewer           missing
Preferred viewer setting missing
Live viewer switching    missing
Slot/Freeform viewers    not to be faked until authority supports them
```

## A.19 Current admin/command reality

Current in-game Server Monitor:

```
read-only
sanitized diagnostics
only action = Close
```

Current player slash commands:

```
/help
/where
```

Not currently present as a production capability:

```
admin principal/capability system
writable Server Monitor
server operator command registry
RunUO-style command console
Godot server control GUI
Save/Kick/Spawn/Teleport/etc. shared admin authority
```

This means P7 is a real capability build, not merely surfacing hidden buttons.

## A.20 Test/evidence launch flags stay Test-only

Representative families currently include:

```
--shell-ui-smoke
--composition-smoke
--smoke
--d0-*-smoke
--f0-*-smoke
--f1-*-smoke
--a0-8-*
--m0-0-*
--pf*-*
--w7-native-gate-trigger-file=
telemetry/accounting ready/start/stop/summary file hooks
```

The exact automation flags may continue to evolve.

Permanent rule:

> The existence of a useful test flag does not make that flag a product/admin setting.

The Control Plane classification pass decides whether the underlying capability earns a User/Admin/Developer surface while preserving test automation independently.

---

# Appendix B — Old terminology → canonical terminology migration

Use this table when updating older plans/docs/code comments so terminology converges without confusing architectural meaning.

| Older / ambiguous wording                | Canonical wording                                                                     | Notes                                                                               |
| ---------------------------------------- | ------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| Engine Host                              | **Framework Runtime Host**                                              | Infrastructure that composes/hosts packages and capabilities; not a gameplay Engine |
| Engine handler                           | **Framework adapter/runtime boundary** or **Runtime Host** depending responsibility   | Avoid implying Godot/render/audio service is a gameplay Engine                      |
| Rendering Engine (UltraSim architecture) | **Presentation/Rendering Adapter** over relevant contracts/Kernels                    | Godot may call itself an engine externally; UltraSim terminology stays distinct     |
| Audio Engine meaning device/mixer        | **Audio Kernel**                                                                      | Generic device/mix/spatial machinery                                                |
| Sound/game audio behavior                | **Sound Engine**                                                                      | Footsteps, ambience semantics, cue mapping, game-facing behavior                    |
| Core (ambiguous)                         | **Kernel** when foundational, **Engine Core** when gameplay capability implementation | Do not use importance/prestige as classifier                                        |
| Scripts (as if small callbacks)          | **Game Implementation** umbrella containing Scripts/Profiles/Definitions/Rules        | This layer decides what game is actually being made                                 |
| Plugin                                   | **Runtime Package**                                                                   | Classify by package class; “Extension” means sparse owner-attached OOP state        |
| Godot Engine integration                 | **Godot Adapter / Bridge**                                                            | External technology realization                                                     |
| Server/client glue                       | **Framework / Bridge** depending ownership                                            | Framework composes; Bridges translate contracts/boundaries                          |

---

# Appendix C — Independent-repository extraction checklist

Use this whenever a major package boundary is added or refactored.

For package `X`:

- Can `X` be described in one architectural role: Kernel, Bridge/Contract, Framework, Engine Core, Game Implementation, Content, or Adapter?
- Are its downward dependencies explicit?
- Does it reference `UltraSim.Game` unnecessarily?
- Does a Kernel reference an Engine/Game implementation?
- Does an Engine Core reference an unrelated Engine Core where a Contract would suffice?
- Does a Framework package contain concrete game design?
- Are Godot/platform types leaking across a permanent neutral contract?
- Could authored data preserve its meaning if an optional Core disappeared?
- Are required versus optional capabilities explicit?
- If extracted into a separate repo, would dependency direction still make sense?
- Would extraction primarily be packaging work, or would it require redesigning semantic ownership?

A package that fails this test does not automatically need to move repos. It needs its architectural boundary repaired first.

---

# Appendix D — Campaign completion board

This compact board is intended to sit near the top of active implementation notes.

```
[x] Gate 0  F1.12 product dogfood/readiness

[x] P1      Framework Control Plane — CLOSED
[ ] P2      Runtime Host + C# Server Authoring Environment — ACTIVE
[ ] P3      Framework adapter/runtime boundary migration
[ ] P4      Engine Contracts + typed definition facets
[ ] P5      User/Admin/Developer control surfaces
[ ] P6      Inventory D2/List viewer profiles
[ ] P7      Server Operator Product + console + Godot GUI + save/backup/recovery
[ ] P8      Audio Kernel + Sound Engine
[ ] P9      Representative Cabin visual/content pass
[ ] P10     UI/failure/product polish
[ ] P11     Focused trusted dogfood burn-down

[ ] Gate 1  Identity / character-mode decision

[ ] PF6     Closed external human validation

[ ] LIVE    Many-player live/dogfood test GO
```

For every checked item, retain:

```
starting HEAD
implementation/tested HEAD
final evidence HEAD
verification summary
known limitations
next checkpoint
clean worktree state
```

This keeps the campaign consistent with the evidence discipline used throughout the F1 and World work.
