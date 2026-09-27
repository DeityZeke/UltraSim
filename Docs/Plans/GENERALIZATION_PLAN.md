# UltraSim Post–Proof-of-Concept Architecture & Ecosystem Roadmap

**Status:** Future roadmap. Begins only after the playable Concept Demo is accepted.

**Purpose:** Turn the architecture proven by UltraSim into a set of independently reusable kernels, Engines, host infrastructure, packages, profiles, tooling, and web-facing capabilities without destabilizing the working game that proved those seams.

The Concept Demo remains the behavioral reference implementation throughout extraction.

---

# 1. Core Principle

Do not stop building the game in order to prematurely build a framework.

The framework is extracted **from working UltraSim subsystems after their boundaries have been proven by real use**.

```text
working game requirement
        ↓
proven implementation
        ↓
stable seam
        ↓
extract reusable core
        ↓
standalone tests/examples
        ↓
UltraSim becomes one consumer
```

Avoid:

```text
invent generic framework
        ↓
hope UltraSim still fits it
```

The post-demo campaign therefore begins by freezing a known-good UltraSim baseline and then pulling the implementation apart one seam at a time.

---

# 2. Architectural Vocabulary

The following vocabulary should be treated as the intended long-term model.

## 2.1 Kernel

Low-level reusable machinery with no UltraSim gameplay knowledge.

Examples:

* ECS
* Network
* Persistence
* low-level serialization/storage utilities
* other similarly fundamental infrastructure if earned

A Kernel should be usable independently of UltraSim, Godot, and specific game rules.

---

## 2.2 Entity

Stable simulation identity used by the ECS and optionally by higher-level associations.

Entity generation must remain part of identity so reused ECS slots cannot inherit stale state.

---

## 2.3 Component

Dense ECS data stored through the archetype/component model.

Best suited for:

* numerous objects;
* homogeneous data;
* hot simulation state;
* bulk processing.

Examples:

* Position
* Velocity
* Health
* PublicState
* movement state

---

## 2.4 System

ECS-oriented data processing.

A System primarily works over Entities and Components, usually in bulk.

Examples:

```text
VelocitySystem
MovementSystem
ReplicationProjectionSystem
GrowthSystem
```

The defining question is:

> Is this primarily processing potentially large amounts of homogeneous ECS data?

If yes, it is probably a System.

---

## 2.5 Module

A sparse managed sidecar attached to an Entity outside archetype storage.

A Module is useful when an Entity needs richer or unusual state without changing its ECS archetype merely to support one sparse behavior.

Conceptually:

```text
Entity
├─ Components
│  ├─ Position
│  └─ Velocity
│
└─ optional Modules
   ├─ VendorModule
   └─ SpecialBehaviorModule
```

Hard rules:

* Modules are managed objects and therefore create GC pressure.
* They are intended to remain sparse.
* Large homogeneous populations should use Components instead.
* Module association must use complete Entity identity, including generation.
* Destroying an Entity must not leave a Module attached to a future reused slot.
* Do not store managed Module references inside dense Components merely to bridge the two models.

A universal Module registry/API should not be invented until real consumers establish the required contract.

---

## 2.6 Engine

A reusable domain capability/core.

An Engine performs a job but does not necessarily operate over large ECS datasets and does not need to use ECS at all.

Examples:

```text
InventoryEngine
ChatEngine
HousingEngine
FarmingEngine
WebEngine
```

Typical Engine work may look like:

```text
receive request
    ↓
validate
    ↓
perform domain operation
    ↓
produce result/event/projection
```

rather than:

```text
iterate 100,000 Components
```

An Engine may use:

* ECS;
* Modules;
* World;
* Persistence;
* Network adapters;
* other Engines;

but only through explicit dependencies.

It may also maintain its own specialized internal representation.

---

## 2.7 EngineHost

The ACC/VitaNex-like server composition and lifecycle manager.

The EngineHost is **server/runtime infrastructure**, not a gameplay Engine and probably not a Kernel.

Conceptually:

```text
Server Host
├─ Kernels
│  ├─ ECS
│  ├─ Network
│  └─ Persistence
│
├─ EngineHost
│  ├─ registration
│  ├─ dependency/version checks
│  ├─ lifecycle
│  ├─ configuration
│  ├─ diagnostics
│  └─ Engine API exposure
│
└─ Engines
   ├─ Inventory
   ├─ Chat
   ├─ Web
   └─ ...
```

The EngineHost manages Engines; it does not become their implementation.

---

## 2.8 Profile / Script / Package

Concrete product/game behavior built on reusable Engines and Kernels.

Examples:

```text
Inventory.Core
    ↓
UltraSim D2-style Inventory profile

Chat.Core
    ↓
UltraSim Local / Global / System Chat

Housing.Core
    ↓
specific shard/property rules
```

Packages may contain:

* profiles;
* scripts;
* content;
* configuration;
* optional Engine implementations;
* dependencies on existing Engines.

A package that uses an existing Engine does not need to ship another Engine DLL.

---

# 3. Supported Programming Styles

The framework should not become ideologically ECS-only.

It should support several valid composition styles.

## 3.1 ECS-heavy

```text
Entity
Components
Systems
```

Best for large-scale data-oriented simulation.

---

## 3.2 Object/Engine-heavy

```text
Entity
Modules
Engines
```

This could support a design surprisingly similar to RunUO:

```text
RunUO Serial
    ↓
UltraSim Entity

RunUO attached object/module state
    ↓
UltraSim managed Modules

RunUO service/system logic
    ↓
UltraSim Engines
```

This approach may sacrifice some ECS performance benefits, but the framework should permit it.

---

## 3.3 Hybrid — expected UltraSim model

```text
dense/hot state
    → Components + Systems

sparse rich Entity state
    → Modules

domain capability
    → Engines
```

Use the representation appropriate to the workload.

---

# 4. Phase 0 — Freeze the Accepted Concept Demo

Before extraction begins:

1. Complete and accept the playable Concept Demo.
2. Establish canonical Debug/Release verification.
3. Preserve behavioral fixtures, protocol baselines, benchmarks, and deterministic test-world evidence.
4. Record the exact accepted architecture at the transition point.
5. Tag a known-good pre-extraction baseline.

The purpose is to make UltraSim itself the oracle against which extraction is checked.

No reusable-core extraction should require guessing whether game behavior changed.

---

# 5. Phase 1 — Formalize the Reusable Architecture

Before moving repositories around, freeze the conceptual contracts.

Document:

* Kernel;
* Entity;
* Component;
* System;
* Module;
* Engine;
* EngineHost;
* Package;
* Profile;
* Script;
* adapter/integration boundaries.

Also document dependency direction.

The most important rule is:

> **Reusable cores may not reference the UltraSim EngineHost or UltraSim Game.**

Instead:

```text
Inventory.Core
        ↑
Inventory.UltraSim
        ↓
UltraSim EngineHost contracts
```

Never:

```text
Inventory.Core
        ↓
UltraSim EngineHost
```

A reusable core must remain instantiable and testable if its UltraSim adapter is deleted.

---

# 6. Phase 2 — Implement the EngineHost

Build the ACC/VitaNex-inspired management layer.

Initial responsibilities:

* explicit Engine registration;
* stable Engine IDs;
* Engine version metadata;
* dependency/version validation;
* deterministic initialization ordering;
* configuration lifecycle;
* start/stop lifecycle;
* save/load hooks where appropriate;
* diagnostics;
* failure reporting;
* explicit enabled/disabled state where safe.

Possible lifecycle:

```text
Registered
    ↓
Configured
    ↓
Loaded
    ↓
Started
    ↓
Running
    ↓
Stopping
    ↓
Saved / Disposed
```

Runtime enable/disable should **not** automatically be promised for every Engine.

For example:

```text
ChatEngine
    may be reasonably disable-able

InventoryEngine
    may not be safely removable while live inventory state exists
```

Each Engine should explicitly declare or implement supported lifecycle transitions.

Avoid reflection-heavy magic based on specially named methods.

Prefer explicit interfaces/manifests or generated registration.

---

# 7. Phase 3 — Begin Monorepo Seam Extraction

Extract one subsystem at a time.

Do not perform a giant repository split.

Each extraction follows:

```text
1. Freeze current subsystem behavior.
2. Identify generic core.
3. Move UltraSim-specific policy outward.
4. Give the core standalone tests.
5. Give the core standalone examples/profiles where useful.
6. Add tiny UltraSim adapter.
7. Reintegrate.
8. Run complete UltraSim regression suite.
9. Review the extracted public API.
10. Only then close the extraction.
```

Candidate repository shape:

```text
inventory/
├─ src/
│  └─ Inventory.Core/
│
├─ integrations/
│  └─ UltraSim/
│
├─ profiles/
│  └─ examples/
│
├─ tests/
└─ benchmarks/
```

---

# 8. Phase 4 — Convert Existing Domain Cores into Engines

Use real UltraSim capabilities as the first EngineHost consumers.

## 8.1 Inventory

Target:

```text
Inventory.Core
    generic ownership/container/transaction capability

Inventory.UltraSim
    EngineHost registration + UltraSim integration

UltraSim package/profile
    D2-style inventory rules/presentation
```

Inventory should not require ECS as its primary storage model merely because UltraSim has an ECS.

Use an ECS bridge only where it is genuinely useful.

---

## 8.2 Chat

Target:

```text
Chat.Core
    generic message validation/routing/recipient capability

Chat.UltraSim
    EngineHost adapter

UltraSim profile
    Local
    Global
    System
    overhead presentation integration
```

Chat is an especially strong Engine example because its work naturally resembles:

```text
Take Message
    ↓
Verify
    ↓
Resolve Recipients
    ↓
Broadcast
```

and does not need to masquerade as bulk ECS processing.

---

## 8.3 Other Engines

Potential future Engines include:

* Web
* Housing
* Farming
* Navigation
* UI semantic core
* economy-related engines
* quest engines
* other reusable domain capabilities

Do **not** create Engines merely to populate the architecture.

A healthy official Engine list should remain relatively small.

If there are dozens of tiny “gameplay Engines,” that likely means scripts/profiles/packages were incorrectly promoted into the Engine layer.

---

# 9. Phase 5 — Profiles, Examples, and Package Composition

Each reusable Engine should provide enough examples to demonstrate how it is intended to be consumed without making UltraSim the only documentation.

Possible examples:

```text
Inventory
├─ SimpleSlots
├─ Equipment
└─ Grid-style example

Chat
├─ Global
├─ Proximity
└─ Team/channel example
```

UltraSim-specific rules remain outside the generic repository where appropriate.

A package may then declare:

```text
requires:
    Inventory >= 1
    Chat >= 1
```

and provide only:

```text
Profiles/
Scripts/
Content/
```

if no new Engine implementation is required.

---

# 10. Inbound HTTP Framework Endpoint / Kestrel Bridge

The old proposal that WebEngine/Web.Core own generic listener/routing/lifecycle machinery is
superseded by F1.12-R3's narrow correctness-driven extraction:

```text
Static Content / future Discovery / future richer Web capability
                    ↓
Framework.Http.Endpoint
                    ↑
Bridges.Http.Kestrel
```

Framework owns backend-neutral, instance-based inbound contracts, frozen service-owned route
definitions, independent service budgets and lifecycle. Kestrel is an external technology Bridge,
not the architectural contract. Server.Hosting explicitly composes both. A richer optional Web
capability may consume this endpoint; it must not become listener owner simply because its API
is sophisticated. Outbound HTTP, generic authorization, MVC, Runtime/Module Host and dynamic
providers are not implemented by R3. This does not begin the general P1/P2/P3 campaign.

# 11. Static Content as the first provider

Static Content owns copied immutable resource bytes and exact routes, not a listener or global
web singleton. One endpoint can host multiple independently bounded services. The R3 test-only
status-like service models the known registry poller's tiny status/proof workload and remains
available while Content's active/queue budget is saturated. It does not implement a registry.

Sharing listener machinery must not collapse semantic/security domains. Future public, registry,
account and operator surfaces retain provider-owned authorization and limits. External content
hosting (nginx/CDN/object store) remains possible without changing content identity.

Discovery should remain an open transport-neutral application contract usable by native client,
website and third-party consumers. Website/client are consumers; official registry is a provider;
HTTP is a transport. Neither a website database schema nor client-private DTO defines Discovery.
Future projections should be committed/bounded snapshots, not direct access to mutable authority.

**Sequencing after successful R3 closure:** F1.12/Gate 0 closed; P1 Framework Control Plane next;
P2–P11 and Gate 1 planned. PF6 remains planned/not ready/not started until the broader
productization and identity/character-mode gate. Many-player live testing is not ready.
No Control Plane, Discovery, Accounting federation, outbound integrations or W8/W9/W10 work is
authorized by this early extraction.

---

# 12. Phase 8 — Web/API Surfaces

WebEngine can expose several separately authorized surfaces.

## 12.1 Public

Examples:

* server status;
* public character profile;
* paperdoll/avatar;
* skill leaderboards;
* PvP leaderboards;
* guild/public organization information;
* public world/map projections where appropriate.

---

## 12.2 Account-authenticated

Examples:

* account characters;
* own paperdoll;
* bank/inventory views;
* owned property;
* account preferences.

Begin read-only.

Any future mutation must become an authoritative command routed through the appropriate Engine/Game owner.

Never directly edit persistence/database state from the website.

---

## 12.3 Operator

Examples:

* diagnostics;
* Engine state;
* server health;
* administrative actions;
* operational metrics.

---

## 12.4 Authoring

Examples:

* live world editing;
* map tooling;
* structural candidate submission;
* content publication workflow.

Authoring tools remain clients of authority.

For World editing:

```text
external editor
    ↓
authenticated API
    ↓
candidate operation/artifact
    ↓
server validates privately
    ↓
valid?
    ├─ no → publish nothing
    └─ yes
        ↓
authoritative commit
        ↓
new structural revision
        ↓
clients acquire/apply
```

A browser/editor must never become World authority.

---

# 13. Web Read Models

HTTP request threads should not casually read/mutate the live ECS World.

Prefer:

```text
Game / Engines
    authoritative state
        ↓
committed projections
        ↓
immutable/versioned web read models
        ↓
WebEngine
```

Suitable projected data includes:

* paperdolls;
* profiles;
* leaderboards;
* guild information;
* server status;
* map snapshots.

Authoritative web actions use:

```text
authenticated request
    ↓
bounded command
    ↓
authority owner
    ↓
appropriate Engine/Game operation
    ↓
result
```

The same ownership rules used by the Godot client should apply to web clients.

---

# 14. Deployment Discovery

Do **not** make fundamental server discovery dependent on WebEngine.

WebEngine is optional.

Core deployment/application discovery must remain available independently and may advertise optional services such as:

```text
Application Realm
Deployment Fingerprint
Gameplay Endpoint
Content Endpoint
Web/API Endpoint
Public Website
Capabilities
```

Conceptually:

```text
UltraSim deployment
├─ gameplay endpoint
├─ optional content endpoint
├─ optional Web/API endpoint
└─ optional website
```

WebEngine may additionally expose richer HTTP discovery such as a conventional well-known endpoint, but Direct Connect must continue functioning if WebEngine is absent.

---

# 15. No Monolithic “Mod API”

Do not create one giant traditional `UltraSim.ModApi`.

The stable extension surface should emerge from the architecture itself:

```text
EngineHost contracts
Engine public APIs
ECS contracts
World contracts
Content contracts
high-level Game scripting contracts
Package/Profile conventions
```

A package should be able to consume:

```text
InventoryEngine
```

directly rather than requiring:

```text
UltraSimModApi.DoInventoryThing(...)
```

that merely wraps the same operation.

A smaller high-level scripting/package surface may eventually be designated as the **strongly compatibility-supported** layer.

For example:

```text
IInventoryOperations
IChatOperations
IUiOperations
IWorldCommands
IEntityLookup
```

This is primarily a compatibility promise, not a second implementation.

Advanced authors may use deeper Engine/ECS contracts with correspondingly weaker compatibility guarantees.

---

# 16. Public API Evolution

Do not freeze huge public APIs prematurely.

Prefer independently versioned contracts:

```text
ECS API vN
Inventory API vN
Chat API vN
EngineHost API vN
Web API vN
Content format vN
Game scripting API vN
```

rather than one global:

```text
ModApiVersion = N
```

This allows Chat to evolve without forcing an unrelated Inventory API version transition.

---

# 17. Third-Party Engines

The EngineHost should eventually allow trusted server operators to install third-party Engines.

Example:

```text
Weather/
├─ Weather.Core
├─ Weather.UltraSim
├─ Profiles/
├─ Scripts/
└─ Content/
```

A third-party Engine does not imply downloadable executable client code.

Client-visible behavior should preferably use existing semantic surfaces:

* public World state;
* UI;
* Chat;
* Content;
* commands;
* declarative profiles.

Executable client extensions, if ever supported, remain a separate explicit trust/install/consent class.

---

# 18. Fresh Independent Architecture Review During Extraction

Each extracted core should receive a fresh architecture review from a strong reviewer that has not accumulated all of the historical context behind the design.

The reviewer should treat the core as an independent third-party library.

Questions include:

* Is the public API coherent?
* Is anything secretly UltraSim-specific?
* Can the UltraSim adapter be deleted?
* Are ownership and lifetime rules explicit?
* Are failure semantics bounded and understandable?
* Are unnecessary abstractions present?
* Are important extension seams missing?
* Is allocation/concurrency behavior appropriate?
* Could another game realistically consume this repository?

If a future model such as GPT-6 ASTRA is available and suitable, it may serve as one such independent reviewer.

The newer reviewer is not treated as an oracle.

Architectural changes still require concrete justification.

---

# 19. Full Recomposition Review

After the major kernels and Engines have been extracted and then composed back into UltraSim, perform another independent top-down review.

The purpose is different from per-core review.

Ask:

* Are dependency directions still clean when everything is assembled?
* Are there circular service relationships?
* Do individually clean cores compose awkwardly?
* Are lifecycle assumptions incompatible?
* Is state duplicated across Engines?
* Are there scalability bottlenecks at integration seams?
* Have adapters become larger than the generic cores they connect?
* Has any Engine become effectively mandatory without being classified appropriately?
* Are package and Engine responsibilities still distinct?

Conceptually:

```text
bottom-up review
    “Are the pieces clean?”

top-down review
    “Is the machine clean?”
```

Both are required.

---

# 20. Open-Source / Public Ecosystem Rollout

The intended public story should be:

```text
UltraSim
    working reference product

+
reusable infrastructure
    extracted from it
```

not:

```text
speculative framework
    looking for a game
```

Public repositories may eventually include:

```text
UltraSim ECS
UltraSim Network
UltraSim Persistence
UltraSim EngineHost
Inventory
Chat
Web
other proven reusable Engines
UltraSim adapters/profiles
```

`UltraSim Network` already establishes the intended packaging direction. Its
normal public package should be the canonical Full `UltraSim.Network.dll`, with
shared Core plus complete Hosted Client and Hosted Server capability. Optional
same-identity ClientOnly/ServerOnly artifacts may remain available for consumers
that want physical role minimization, but they are not separate architectures,
functionality tiers, or a secrecy/security model. Most users should install the
Full package; no future ecosystem extraction should force existing light-build
consumers to change namespaces or assembly references merely to adopt Full.

The exact names of the other eventual extracted packages are not frozen. The
existing `UltraSim.Network` assembly/package identity is already canonical.

A user should be able to choose:

```text
ECS only
Inventory only
Web + Inventory
whole server stack
UltraSim itself
```

without unnecessarily inheriting unrelated pieces.

---

# 21. Forums / Community Ecosystem

The eventual community forums have several important roles beyond normal conversation.

## 21.1 Package sharing

Authors can publish/discuss:

```text
[Package] Advanced Farming
[Profile] Classic Inventory
[Engine] Auction House
[WorldPack] Example Dungeon
[Tool] Browser Map Editor
```

with tutorials, screenshots, compatibility notes, and support.

---

## 21.2 Architectural requests

A major purpose is to let authors say:

> “I can build this, but the only available route is hacky, slow, allocation-heavy, unsafe, or coupled to internals.”

That request is then classified as:

```text
incorrect usage?
documentation gap?
missing example/profile?
missing Engine operation?
missing generic mechanism?
real architectural flaw?
```

Possible outcomes:

* point the author toward the intended seam;
* improve documentation/examples;
* add a legitimate public operation;
* generalize a mechanism;
* accept/design an architectural change.

GitHub issues/PRs remain the durable engineering workflow.

Forums provide the higher-context design discussion that often needs to happen before a good PR can exist.

---

# 22. Framework Evolution Rule

Do not promote every requested feature into the framework.

A useful rule:

```text
one package needs it
    → probably package-local

several unrelated packages reinvent it
    → investigate

many Engines need the same missing primitive
    → likely framework/core candidate
```

Examples:

```text
“Add fishing”
    → probably a package/profile/Engine

“There is no bounded persisted scheduler,
so every Engine implements its own timer wheel”
    → likely architectural gap
```

The ecosystem should evolve from real usage pressure.

---

# 23. Long-Term Result

The desired result is not merely:

> UltraSim, the game.

It is:

```text
Reusable Kernels
        ↓
EngineHost
        ↓
Reusable Engines
        ↓
Profiles / Scripts / Packages
        ↓
World / Content / Tools / Web
        ↓
UltraSim
```

with UltraSim acting as the primary reference implementation proving that the pieces work together under a real persistent multiplayer simulation.

The framework should be usable for games that are nothing like UltraSim.

UltraSim itself remains approximately:

> **Kenshi-scale world simulation, Sims-like people, in a UO-like persistent sandbox/environment.**

The broader architecture should remain genre-agnostic wherever the underlying problem genuinely is.

---

# 24. Immediate Post-Demo Sequence

The currently preferred order is:

```text
1. Freeze accepted playable Concept Demo baseline.

2. Formalize Kernel / System / Component /
   Module / Engine / EngineHost terminology.

3. Implement EngineHost.

4. Begin monorepo extraction one proven seam at a time.

5. Convert Inventory into:
       Inventory.Core
       Inventory.UltraSim
       UltraSim profile/package.

6. Convert Chat into:
       Chat.Core
       Chat.UltraSim
       UltraSim profile/package.

7. Extract other proven reusable cores as earned.

8. Add standalone profiles/examples/benchmarks.

9. Build Web.Core + hosting adapters.

10. Integrate WebEngine with EngineHost.

11. Migrate static content hosting onto shared Web
    machinery only if its deliberately dumb semantics
    can be preserved.

12. Add public/account/operator/authoring APIs.

13. Expand deployment discovery to advertise optional
    content/Web/tooling services without depending on WebEngine.

14. Build external tools such as web profiles,
    leaderboards, admin tooling, and live map editing.

15. Perform independent review on every extracted core.

16. Recompose complete framework + UltraSim.

17. Perform full independent architecture review.

18. Prepare staged public/open-source ecosystem release.

19. Launch/support community package sharing and
    architectural feedback workflow.
```

---

# 25. Non-Goals

This roadmap does **not** mean:

* rewrite the Concept Demo immediately after completing it;
* convert every subsystem into an Engine;
* put every object into ECS;
* abandon ECS for OOP;
* create hundreds of managed Entity Modules;
* create one giant Mod API;
* require WebEngine for a server to function;
* require UltraSim clients to know server Engine implementation details;
* automatically download executable client extensions;
* expose live ECS internals directly over HTTP;
* make the static package host Game-aware;
* create arbitrary abstraction layers without proven consumers;
* promise API stability before boundaries have earned it.

The objective is controlled extraction of already-proven seams.

---

# 26. Success Criteria

The post-demo architecture campaign is successful when all of the following become true:

```text
UltraSim still works as the reference game.

Major reusable cores build and test without UltraSim.

Deleting an UltraSim adapter does not break its reusable core.

EngineHost can compose independently developed Engines.

Inventory and Chat prove the Engine model with real workloads.

ECS Systems remain clearly distinct from domain Engines.

Sparse managed Modules are possible without contaminating dense ECS storage.

Packages can extend existing Engines without new executable cores.

Web/API functionality is optional and host-adapter agnostic.

Static content delivery remains replaceable by external infrastructure.

Discovery advertises optional services without requiring them.

Third-party authors can build substantial server functionality
without a monolithic Mod API.

Real package authors can identify missing seams and drive
architectural evolution through concrete use cases.

The extracted framework can be recomposed into UltraSim without
losing the behavior/performance/invariants established by the
pre-extraction Concept Demo.
```

The game proves the framework.

The framework makes the game easier to extend.

The ecosystem grows only from capabilities that real workloads have earned.
