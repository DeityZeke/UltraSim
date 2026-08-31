# UltraSim Client — Thin Shell, Capability Composition, and Playable Client Plan

**Status:** proposed/frozen implementation plan after Physics P0.1b (`6bfef1e2`).

**Purpose:** replace the current acceptance-oriented Godot playable harness with a real, persistent, user-facing client shell while preserving UltraSim's existing ownership boundaries and proving that local play, remote play, bundled content, and optional client capabilities can be composed independently.

This plan incorporates the useful client-shell, UI-layer, server-composed UI, presentation, and content lessons from archived V2 while targeting the current V3 architecture. V2 is archaeology and behavioral reference, not code or ownership to port wholesale.

The milestone is not generic "front-facing cleanup." It is:

> **Reconstruct a V2-quality real Godot client shell on V3 architecture, promote Cabin in the Woods to an optional built-in Test World, and prove the same thin client can be compiled as single-player-only, online-only, complete, or contentless-online without retaining absent capabilities as hidden runtime code.**

---

# 1. Executive decision

UltraSim's client is deliberately thin.

It owns input capture, presentation, UI rendering, local settings, content caching, local application flow, and session-facing client state. It does **not** become the authoritative owner of gameplay because the server happens to be embedded in the same process.

The client should be able to exist in several concrete compositions:

```text
Single Player Client
    client shell
    client core
    Godot presentation
    local authoritative Game/server capability
    optional bundled Test World/content

    NO Client.Network
    NO Kernels.Network
    NO remote protocol transport
    NO Multiplayer button

Online / MMO Client
    client shell
    client core
    Godot presentation
    remote client protocol/network capability

    NO local authoritative Game/server composition
    NO required bundled world
    NO Single Player button

Complete Client
    client shell
    local play capability
    remote play capability
    optional bundled Test World/content

Contentless Online Client
    client shell
    remote play capability
    bootstrap/fallback presentation assets
    content acquisition/cache

    NO bundled game world
    NO bundled authoritative Game implementation
    NO Single Player capability
```

The distinction must be real at the project/package/assembly level. A Single Player build does not merely hide Multiplayer while still shipping Network. An Online build does not merely hide Single Player while still shipping the local Game/server implementation.

The shell exposes only features supplied by its composition.

---

# 2. Current V3 position

The current repository already contains many of the correct independent pieces.

## Client Core

`Client/Core` is engine-neutral and currently does not depend on Network. It owns client semantic state and presentation coordination such as:

- `ClientWorldSemanticCache`;
- bounded semantic chunk residency;
- retained public entity knowledge;
- presentation-window coordination;
- input sequencing and focus-aware client semantics;
- client UI state/contracts;
- other client-side retained semantic state.

This is the correct center of the real client.

## Client Network

`Client/Network` is already a separate project. It introduces the remote protocol and Network dependencies.

That separation is valuable and must become an explicit executable-composition boundary.

## Godot adapter

`Client/_Godot` is also already Network-free. It translates Client Core semantics into Godot-native rendering, UI controls, input capture, render windows, and fallback presentation resources.

The Godot adapter remains a presentation/input adapter. It must not become the place where local-vs-remote Game semantics diverge.

## Current playable harness

`Client/Playable.Godot` currently composes Client Core, Client Network, Game projects, Server Runtime/Persistence, and the Network kernel into one acceptance executable. Its large `PlayableWorldClient` has been useful for proving whole verticals.

That executable is an acceptance harness, not the final client ownership model.

It should remain available while the real shell is being built, then either shrink into a dedicated acceptance launcher or retire once the real client provides equivalent automated and manual coverage.

## Existing UI foundation

V3 already contains substantial engine-neutral UI machinery under `Kernels/UI`, including authoring, compiled definitions, models, protocol/types, and semantic wire codecs. `Game/UI` already has a real Inventory UI consumer.

The real client should consume and prove these capabilities rather than inventing a parallel Godot-specific game UI framework.

---

# 3. V2 archaeology to preserve

Archived V2 established several client concepts that remain useful:

- one persistent client root rather than replacing the entire scene/app per screen;
- persistent UI categories/layers;
- local application-shell UI separate from server-driven UI;
- world overlays such as nameplates and overhead chat;
- menu/HUD/debug separation;
- actor definition/catalog concepts rather than hardcoding one final model;
- a RunUO-gump-like server-composed UI path;
- cold/hot UI schema caching;
- a client-local main menu and settings flow;
- explicit content/resource distinctions;
- future diagnostics/admin tooling as optional client quality-of-life rather than gameplay authority.

Recover these **behaviors and boundaries**.

Do not recover obsolete V2 ownership where networking, world state, or client presentation improperly owned server/game semantics.

---

# 4. Frozen client principles

```text
THE CLIENT IS A THIN INPUT / PRESENTATION / UI / CONTENT-CACHE SHELL.

THE CLIENT DOES NOT BECOME GAME AUTHORITY IN SINGLE PLAYER.

LOCAL PLAY AND REMOTE PLAY CONVERGE ON THE SAME CLIENT SEMANTIC STATE.

NETWORK IS AN OPTIONAL CLIENT CAPABILITY, NOT A REQUIRED CLIENT FOUNDATION.

A SINGLE-PLAYER BUILD MUST COMPILE AND RUN WITHOUT CLIENT.NETWORK OR KERNELS.NETWORK.

AN ONLINE-ONLY BUILD MUST COMPILE AND RUN WITHOUT LOCAL GAME/SERVER COMPOSITION.

OPTIONAL CAPABILITIES ARE REMOVABLE AT BUILD/PACKAGE LEVEL, NOT MERELY HIDDEN AT RUNTIME.

A MISSING CAPABILITY PRODUCES NO UI ENTRY POINT FOR THAT CAPABILITY.

CLIENT-LOCAL APPLICATION UI AND SERVER-COMPOSED GAME UI ARE DIFFERENT OWNERSHIP DOMAINS.

SERVER-COMPOSED UI IS DATA/SCHEMA, NEVER EXECUTABLE CLIENT CODE.

STANDARD PREINSTALLED UI DEFINITIONS USE THE SAME SEMANTIC IDENTITY AS CACHED CUSTOM DEFINITIONS.

PREINSTALLED UI MUST NOT BECOME A SECOND HARDCODED GAME-UI PROTOCOL.

WORLD/HUD/MENU/DEBUG UI CATEGORIES ARE PERSISTENT LAYERS AND INPUT/FOCUS DOMAINS.

PRESENTATION DOES NOT POLL THE SERVER FOR FACTS THE CLIENT ALREADY KNOWS.

CTRL-REVEAL NAMEPLATES DOES NOT CHANGE AOI, SUBSCRIPTION, OR SERVER DISCLOSURE.

CLIENT SEMANTIC CACHE KNOWLEDGE IS PRESENTATION INPUT, NOT AUTHORITY.

SERVER DISCLOSURE DETERMINES WHAT THE CLIENT MAY KNOW; CLIENT POLICY DETERMINES HOW KNOWN STATE IS PRESENTED.

LOCAL CONTENT, SERVER CONTENT PATCHES, RESOURCE PACKS, AND EXECUTABLE CLIENT MODS ARE DIFFERENT CONCEPTS.

A SERVER MAY STREAM DATA/ASSETS; IT MAY NOT SILENTLY INSTALL EXECUTABLE CLIENT CODE.

A CONTENTLESS CLIENT MUST RETAIN A SMALL BUILT-IN FALLBACK PRESENTATION SET.

SINGLE PLAYER MAY USE AN EMBEDDED AUTHORITATIVE SERVER WITHOUT USING NETWORK TRANSPORT.

LOCAL PLAY MAY BYPASS PACKETIZATION; IT MAY NOT BYPASS GAME AUTHORITY OR CLIENT SEMANTIC BOUNDARIES.

DIRECT CONNECT IS THE FIRST REMOTE ENTRY POINT.

SERVER DISCOVERY / PUBLIC SERVER LISTS ARE DEFERRED UNTIL SEPARATELY AUTHORIZED.

THE REAL CLIENT MUST BECOME A PRE-EXTRACTION COMPOSITION ORACLE FOR THE FUTURE REPOSITORY SPLIT.
```

---

# 5. Ownership map

## Client shell

The application shell owns:

- application lifetime;
- main menu;
- local Settings;
- Exit;
- loading/transition/error surfaces;
- Direct Connect when remote play is installed;
- Single Player entry when local play is installed;
- in-session local options/disconnect flow;
- local capability-to-menu composition;
- persistent UI layer roots;
- orchestration of one active client session/world presentation.

The shell does not own:

- authoritative world state;
- combat, inventory, physics, character permission, or movement authority;
- Network protocol semantics;
- server discovery service behavior;
- server-authored game menus.

## Client Core

Client Core owns engine-neutral client state and policy:

- semantic world cache;
- public entity knowledge;
- client presentation coordination;
- input sequencing/focus suppression policy;
- bounded client chat state;
- UI semantic/cache state where appropriate;
- presentation eligibility/state that is not engine-specific;
- session-local knowledge lifetimes.

## Godot adapter

Godot owns:

- scene composition;
- native controls;
- renderer resources;
- camera feel;
- animation/audio/VFX presentation;
- raw physical input capture;
- UI focus realization;
- local settings persistence where platform-specific;
- presentation fallback primitives/assets.

Godot does not own Game semantics, protocol codecs, AOI, replication policy, or server UI state machines.

## Local play capability

The Local Play capability owns composition of an authoritative in-process/local Game/server runtime for Single Player.

It may directly bridge committed Game semantic outputs into Client Core **without Network/UDP packetization**, provided it uses the same semantic boundaries and does not let presentation code mutate authoritative Game state directly.

## Remote play capability

The Remote Play capability owns:

- client protocol/session machinery;
- Network kernel dependency;
- remote transport;
- remote submission/proof paths;
- mapping remote Game semantics into the same Client Core state used by Local Play.

## Game/server

Game/server remains authoritative in both local and remote compositions.

Single Player means "the authoritative server is local," not "the client is authoritative."

---

# 6. Capability composition model

The real client should be composed from explicit capabilities supplied by the executable composition root.

Do not begin with a process-global service locator or reflection-based arbitrary plugin discovery.

Conceptually:

```text
ClientShell
    always present

LocalPlay capability
    optional

RemotePlay capability
    optional

BuiltInWorldCatalog / TestWorld capability
    optional

Future ServerDiscovery capability
    optional, deferred

Future ClientModHost capability
    optional, deferred
```

The shell uses the capabilities it was actually given.

```text
if LocalPlay exists:
    show Single Player

if RemotePlay exists:
    show Multiplayer / Direct Connect

if neither exists:
    shell can still launch Settings / diagnostics / Exit for test compositions
```

This is composition, not a runtime feature flag around code that is still linked into the executable.

## Required initial build profiles

### Single Player

Must compile without references to:

- `Client/Network`;
- `Kernels/Network`;
- remote Game protocol transport;
- Hosted Network.

Expected main menu:

```text
Single Player
Settings
Exit
```

### Online

Must compile without references to local authoritative composition such as:

- `Game/Composition` as an executable/server implementation dependency;
- `Server/Runtime`;
- server persistence;
- built-in Test World package unless deliberately added.

Initial main menu:

```text
Multiplayer
    Direct Connect
Settings
Exit
```

No server discovery/list is required in this campaign.

### Complete

Contains both capabilities:

```text
Single Player
Multiplayer
Settings
Exit
```

### Online Contentless

Contains Remote Play but no bundled playable world/content pack beyond bootstrap/fallback assets.

This is a stronger packaging profile and may require the content-acquisition checkpoint before it can enter a world.

## Future installable capability

The architecture should not prevent a later experience where an official multiplayer capability can be installed into an existing Single Player distribution.

However, **dynamic assembly/plugin installation is not required now**.

First prove clean projects/packages and explicit composition roots. A future real need can earn a versioned dynamic capability loader.

---

# 7. Local play versus remote play

The key invariant is semantic parity, not packet parity.

## Local play

```text
Godot input
    -> Client Core input semantics
    -> Local Play semantic bridge
    -> authoritative Game/server runtime
    -> committed Game/world/chat/UI semantics
    -> Local Play semantic projection/bridge
    -> Client Core semantic state
    -> Godot presentation
```

Local Play should not encode a packet, encrypt it, send it over loopback UDP, decrypt it, and decode it merely to pretend that local and remote execution are identical.

That work is unnecessary when the server is in-process.

What must remain identical is:

- authority ownership;
- command/session semantics;
- accepted Game transitions;
- semantic publication identity;
- client-side knowledge/application semantics;
- UI definitions/models;
- public/private disclosure rules;
- presentation inputs.

## Remote play

```text
Godot input
    -> Client Core input semantics
    -> Client Network / protocol
    -> Network
    -> hosted authoritative server
    -> Game
    -> Game semantic publication
    -> protocol / Network
    -> Client Network
    -> Client Core semantic state
    -> Godot presentation
```

Local and remote play should converge on the same Client Core types/stores wherever the meaning is actually the same.

## No local authority bypass

Forbidden pattern:

```text
if singlePlayer:
    Godot UI -> directly mutate GameWorldState / Inventory / ECS
```

Local UI may invoke a local semantic bridge for a normal Game request. The request still goes through Game authority.

---

# 8. Cabin in the Woods becomes a built-in Test World

The current Cabin in the Woods playable slice should be promoted from "acceptance fixture that happens to be playable" into the first optional built-in Test World/content package.

It should not become hardcoded client logic.

Conceptually:

```text
BuiltInServerContent/
    CabinInTheWoods/
        WorldPack
        content definitions
        server/game configuration
        UI definitions/models where applicable
        presentation assets or references
```

The exact package path/format is not frozen by this plan.

## Single Player use

```text
Main Menu
    -> Single Player
    -> Cabin in the Woods
    -> start local authoritative server/runtime with Cabin package
    -> enter through Local Play semantic path
```

## Remote use

The same Test World should be hostable by an external server and reachable through Direct Connect.

The client must not contain a special "remote Cabin" implementation.

## Parity objective

The same user-facing behaviors should work locally and remotely:

- movement;
- swept collision;
- chunk crossings;
- camera/input;
- opening/closing cabin door;
- inventory/container interaction;
- chat and commands;
- nameplates;
- overhead speech;
- server-composed Inventory UI;
- disconnect/session teardown;
- world/client-cache rebuild.

This becomes a powerful composition oracle before repository extraction.

---

# 9. Persistent real-client shell

Recover the V2 persistent-shell concept.

Conceptually:

```text
ClientRoot
├── Controller / SessionCoordinator
├── Content/Catalog roots
├── World
│   ├── Map
│   │   ├── Terrain
│   │   ├── FrozenStatics / Multi presentation
│   │   └── Environment
│   ├── DynamicEntities
│   ├── Effects
│   └── CameraRig
└── UILayers
    ├── World
    ├── HUD
    ├── Menus
    └── Debug
```

Exact Godot node names are not frozen. Ownership is.

The shell persists while current session/world content is injected and retired beneath it.

A disconnect or world/session replacement should retire session-owned presentation/state and return to the application shell without reconstructing the whole executable.

---

# 10. UI layers are also input/focus layers

The V2 menu categories should return as persistent lifetime categories.

## World layer

Examples:

- actor/nameplate projections;
- overhead chat;
- future damage numbers;
- interaction prompts;
- world markers.

These are presentation overlays tied to already-known world semantics.

## HUD layer

Examples:

- chat feed;
- connection/ping status;
- player HUD;
- future hotbar/status bars/minimap.

## Menu layer

Contains both:

- client-local application menus;
- server-composed game/application panels while connected.

It owns z-order, focus, modal behavior, and panel lifetime realization.

## Debug layer

Examples:

- FPS/performance;
- network diagnostics when Remote Play exists;
- ECS/server diagnostics when allowed by a local/admin semantic source;
- chunk/render/cache diagnostics;
- UI diagnostics;
- optional admin tooling.

Debug/admin surfaces are quality-of-life features, not required gameplay authority.

## Focus semantics

UI categories should participate in input suppression explicitly.

Examples:

```text
World focus
    movement/world input active

Chat entry focused
    text keys do not become movement

Modal Menu
    world interaction suppressed

Debug overlay
    captures input only when its active controls require it
```

Do not accumulate ad-hoc Godot `_Input()` exceptions that independently decide whether gameplay input is allowed.

---

# 11. Three UI ownership classes

Not all UI should be server-driven.

## 11.1 Client-local application UI

Must work with no server/session.

Initial required set:

- Main Menu;
- Single Player entry when Local Play exists;
- Multiplayer / Direct Connect entry when Remote Play exists;
- local Settings;
- Exit;
- loading/connection state;
- failure/error state;
- in-session local options/disconnect menu.

The server does not decide whether the user can open Settings or Exit the application.

## 11.2 Standard client presentation UI

These are client-owned presentation primitives driven by disclosed semantic state, not server-opened generic windows.

Examples:

- nameplates;
- overhead speech;
- chat feed presentation;
- targeting/pointer presentation;
- connection status;
- fallback/missing-content indicators.

The server provides facts/identity. The client decides the visual presentation.

## 11.3 Server-composed game UI

The server/Game owns the semantic menu/window and sends engine-neutral definitions/models/actions.

Examples:

- Inventory/container;
- character sheet;
- vendor;
- crafting;
- dialog/quest surfaces;
- character/account game flows when later earned;
- property management;
- admin/game-control pages;
- other RunUO-gump-like panels.

Godot renders them. It does not own their gameplay truth.

---

# 12. Server-composed UI: compiled definitions and a standard set

The existing UI kernel should be the basis of the real server-composed path.

## Definition versus model

Keep the fundamental split:

```text
UiCompiledDefinition
    layout/schema/control semantics

UiModel / payload
    current values/state
```

Do not repeatedly send a full layout when only model data changes.

## Exact identity

A compiled definition should have exact semantic identity sufficient to determine whether client and server agree on the definition, for example existing/future values equivalent to:

```text
UiDefinitionId / panel type
revision
hash/version
```

Do not accept "close enough" definitions.

## Standard preinstalled set

The client may ship with a standard library of commonly used **compiled UI definitions**.

These are normal UI definitions whose exact bytes/identity happen to be preinstalled.

They are not hardcoded semantic switch cases.

Good:

```text
server opens InventoryStandard revision/hash X
client already has exact X
client renders with model payload
```

Bad:

```text
if UiId == Inventory:
    call special Godot inventory screen outside UI kernel
```

Candidate standard definitions may include Inventory, basic dialogs/confirmations, simple selection/list forms, and other layouts proven common by real consumers. Do not freeze a large catalog before consumers earn it.

## Cold path

```text
server opens definition identity + model
client lacks exact definition
    -> obtain full compiled definition
    -> validate bounds/schema
    -> cache under server/content namespace
    -> render model
```

## Hot path

```text
server opens definition identity + model/delta
client already has exact definition
    -> render immediately
```

## Server-owned panel lifetime

Connected server-composed panels are session/server-owned presentation.

On disconnect or application-session replacement:

- retire server-owned panels;
- clear their model/action debt;
- do not clear local Main Menu/Settings infrastructure;
- never allow stale panel actions to target a new session.

---

# 13. Nameplates: cached presentation, never server polling

Holding Ctrl to reveal nameplates is entirely a client presentation operation over already-authorized client knowledge.

It must not:

- send a server query;
- expand AOI;
- alter chunk/public subscription;
- request hidden entities;
- request names for entities the client was not already entitled to know.

Conceptually:

```text
Game/server publication
    -> ClientWorldSemanticCache / presentation metadata cache
    -> resident/renderable actor presentation
    -> Nameplate renderer
```

When Ctrl is pressed:

```text
show nameplates for all currently resident/renderable entities
that are already known and nameplate-eligible
```

While Ctrl remains held:

- a newly resident eligible entity automatically gains a nameplate;
- a retiring entity loses it;
- a moving actor's plate follows its presentation anchor;
- changed public presentation metadata updates the plate;
- no polling loop asks the server to restate known information.

This follows the same retained-cache model already used for public world state.

## Nameplate eligibility

Do not hardcode "all ECS entities" or "players only."

Eligibility is client presentation semantics over disclosed information.

Possible examples:

```text
player                    yes
NPC/mob                    yes
named quest object         maybe
generic dropped item       usually no
important/named item       maybe
hidden/GM-private entity   not disclosed, therefore impossible to reveal
```

Exact policy belongs to Game/public presentation semantics and client presentation definitions, not Network.

## Public display metadata

Current minimal world public rows are intentionally small and do not carry a display label on every movement update.

The real client should not solve nameplates by querying the server when Ctrl is pressed.

Instead, earn a relatively cold presentation-identity/metadata path when needed, conceptually:

```text
WorldPublicEntityId
    -> authoritative disclosed display label when dynamic/custom
    -> presentation/nameplate category/style identity as needed
```

For content-defined mobs/items, the client may derive the ordinary display label from a known presentation/content definition keyed by prefab/definition identity.

For custom-named actors/players, Game supplies the authoritative disclosed label once and whenever it changes.

Movement/latest-state publication should remain focused on changing spatial/public state rather than repeatedly carrying unchanged strings.

The exact metadata type/wire/cache spelling is deliberately unfrozen until implementation archaeology chooses the smallest correct seam.

---

# 14. Overhead chat and chat feed

Chat C0.1a already establishes the correct semantic split:

- accepted speech is an occurrence;
- it carries authoritative display label and optional public presentation anchor;
- the client applies it to bounded `ClientChatState`;
- feed history survives world generation replacement;
- an incompatible/missing public actor suppresses overhead presentation but not the feed fact.

The real client should consume that path directly.

Overhead bubbles belong to the World UI layer.

Chat feed/input belongs to HUD/Menu presentation as appropriate.

Chat text focus must suppress movement input through shared client focus policy.

Do not rebuild a second Godot-only chat protocol.

---

# 15. Actor presentation parity

The real client should recover useful V2 actor-presentation concepts without coupling authority to Godot scenes.

Useful concepts include:

- a stable actor presentation root;
- visual/collision/effect/camera/nameplate anchors;
- actor/mobile definition catalog;
- multiple body/presentation definitions such as male/female body variants where still useful;
- world public prefab/definition identity -> client visual definition;
- animation/audio/VFX presentation hooks;
- fallback representation when content is missing.

The client does not infer gameplay truth from which scene/model is currently loaded.

---

# 16. Content source model

The client must distinguish at least four content classes.

## 16.1 Built-in/local server content

Used by a Single Player distribution or Complete distribution.

Examples:

- Cabin Test World;
- preinstalled WorldPack;
- game definitions;
- presentation assets;
- standard server UI definitions.

Conceptually, this content belongs to the local authoritative game/server package even when it is physically shipped beside the client executable.

## 16.2 Server-provided content patches

Remote servers may supply validated data/assets required to present their world.

Examples:

- models;
- textures/materials;
- sounds;
- UI definitions;
- item/prefab presentation data;
- terrain/static/world content;
- server-specific remaps.

Server content is cached under the server/content identity namespace.

It is **not executable client code**.

## 16.3 Local resource/texture packs

These are user-local visual substitutions/remaps.

They may affect presentation such as textures, icons, UI skinning, audio, or model mappings where allowed.

They do not alter protocol/Game authority.

## 16.4 Client mods

Future explicit user-installed executable/script extensions.

They are not silently delivered by servers.

A client mod API/loader is deferred until separately earned.

---

# 17. Contentless Online Client

One desired final composition contains no bundled playable world and no bundled authoritative Game implementation.

It includes only enough to:

- launch the shell;
- configure settings;
- Direct Connect;
- negotiate required content;
- validate/cache server-provided data/assets;
- render generic semantic UI/world state;
- use fallback assets while content is unavailable/missing.

This distribution should contain no Cabin world pack or other game-world content merely because the Complete build does.

This is useful both as a deployment profile and as an architectural proof that the client is not secretly relying on local server/Game content to render a remote world.

It does not make the executable impossible to reverse engineer. The client protocol, renderer, and semantic machinery remain inspectable. The meaningful property is that the distribution does not contain the authoritative Game implementation or proprietary bundled world/content that it does not need.

---

# 18. Fallback assets are permanent client infrastructure

Every client composition should retain a tiny hardcoded fallback presentation set so missing content remains diagnosable.

Examples:

```text
missing mesh/object
    -> simple box/capsule according to known semantic bounds/category

missing texture
    -> intentionally ugly high-visibility checker/rainbow diagnostic texture

texture-debug disabled
    -> neutral/simple fallback if desired

missing animation
    -> bounded presentation timer/static pose rather than breaking Game semantics
```

Fallback presentation is client-local.

It must not change authoritative gameplay state or collision.

A contentless client can therefore join and remain semantically functional even when specific presentation assets have not yet arrived, subject to whatever content admission policy the server/client chooses.

---

# 19. Direct Connect first; discovery later

This campaign does not require server discovery or a public server list.

The initial Remote Play application flow is:

```text
Main Menu
    -> Multiplayer
    -> Direct Connect
```

A future `ServerDiscovery` capability may add:

- saved favorites;
- curated endpoints;
- LAN discovery;
- public registry/server browser;
- entitlement/access lists.

The shell composition model must leave room for this, but no discovery service or registry architecture should be introduced merely to finish the real-client rebuild.

---

# 20. Remote controlled-actor parity is a required client gate

The current public-actor/replication evidence proves authoritative remote public state, but the historical Physics archaeology noted that the real retained `GamePlayerCommand` locomotion path was not yet proven as the remote player's actual live control route.

The real Online/Complete client must close that gap before claiming local/remote playable parity.

Required shape:

```text
Godot raw input
    -> Client Core quantized/sequenced Game locomotion intent
    -> remote protocol/Network
    -> authenticated application/control session
    -> real GamePlayerCommand / GameWorldActions authority
    -> Physics
    -> canonical WorldPosition
    -> public/owner semantic result
    -> Client Core
    -> Godot
```

Do not create a second remote-only movement authority path merely to make the client move.

Single Player and Remote Play may differ in transport, not in Game locomotion meaning.

---

# 21. Settings

Local Settings are always client-owned and available without a server.

Examples may include:

- display/window settings;
- graphics/presentation options;
- audio;
- local input bindings;
- camera preferences;
- UI scale;
- nameplate presentation preferences;
- local debug options.

Server/game policy settings are separate and may later be exposed through server-composed UI.

Do not let a server replace or suppress the user's basic local application Settings/Exit capabilities.

---

# 22. Debug/admin quality-of-life

Archived V1/V2 had useful diagnostics/admin concepts. Some are worth recovering after the basic shell is real.

Possible client Debug-layer surfaces:

- FPS/frame timings;
- client semantic cache counts/high-water;
- render-window/chunk diagnostics;
- Network connection metrics when Remote Play is present;
- UI cache/definition diagnostics;
- physics/world debug visualization when data is legitimately available;
- server/admin pages supplied through server-composed UI;
- ECS/system diagnostics for local/admin-authorized scenarios.

These are valuable but not parity blockers unless a checkpoint explicitly names them.

Avoid making a giant universal admin framework part of the client-shell foundation.

---

# 23. Proposed package/composition direction

Exact names are deliberately not frozen, but the dependency shape should move toward something equivalent to:

```text
Client.Core
    engine-neutral client semantics

Client.GodotAdapter
    Godot presentation/input only

Client.Shell.Godot
    persistent application shell
    client-local menus
    installed-capability composition

Client.LocalPlay
    optional local-authority bridge/composition
    depends on Game/server implementation
    does NOT require Network

Client.RemotePlay / Client.Network
    optional remote session/protocol/Network path

Client.BuiltInTestWorlds
    optional bundled server/content package
    Cabin in the Woods initially
```

There may be better final names after implementation archaeology.

The important rule is that no shared shell/Core/adapter package requires both Local Play and Remote Play implementations.

---

# 24. Build/profile verification rules

The composition claim must be mechanically tested.

For each profile, verify both successful behavior and **absence of forbidden dependencies**.

## Single Player profile

Required:

- compiles;
- launches Main Menu;
- shows Single Player / Settings / Exit;
- no Multiplayer/Direct Connect;
- plays Cabin Test World;
- no project/assembly reference to `Client.Network`;
- no project/assembly reference to `Kernels.Network`;
- no remote protocol transport required at runtime.

## Online profile

Required:

- compiles;
- launches Main Menu;
- shows Multiplayer/Direct Connect / Settings / Exit;
- no Single Player entry;
- no local Game/server runtime implementation dependency;
- connects to external Test World server;
- controlled actor uses real Game command authority remotely.

## Complete profile

Required:

- compiles;
- both Local Play and Remote Play available;
- local and remote Cabin scenarios converge on the same user-visible semantics.

## Contentless Online profile

Final stronger gate:

- no bundled playable WorldPack/Test World;
- no local authoritative Game/server implementation;
- bootstrap/fallback client still launches;
- obtains or validates server-required content before/while entering remote world;
- server content cannot install executable client code.

Dependency scans/build graph checks should be permanent tests or verifier gates, not a one-time manual observation.

---

# 25. Implementation roadmap

The checkpoint names below are working names. Preserve the scope and gates even if implementation chooses slightly different labels.

## F0.0 — Real client composition foundation

**Goal:** establish the permanent client-shell dependency shape before rebuilding presentation features.

Implement:

- a real persistent client-shell project/composition root distinct from `Playable.Godot` acceptance code;
- explicit optional Local Play and Remote Play capabilities;
- local app-shell capability enumeration sufficient to add/remove menu entries based on actual installed composition;
- initial build matrix projects/configurations for Single Player, Online, and Complete;
- dependency checks proving Single Player omits Network and Online omits local Game/server implementation;
- preserve current `Playable.Godot` acceptance harness until replacement evidence exists.

Do not implement discovery, dynamic plugin loading, or content streaming yet.

**Gate:** all three compositions compile with their forbidden dependencies genuinely absent.

---

## F0.1 — Persistent Godot shell + local application UI

**Goal:** make a user-facing executable that can exist even with no active session/world.

Implement:

- persistent `ClientRoot`;
- persistent World/HUD/Menu/Debug layers;
- Main Menu;
- local Settings surface;
- Exit;
- loading/transition/error surface;
- in-session local options/disconnect shell;
- capability-driven menu entries;
- focus/input ownership between UI layers and world input;
- no server required to launch/use local application UI.

Expected menus:

```text
Single Player build:
    Single Player
    Settings
    Exit

Online build:
    Multiplayer -> Direct Connect
    Settings
    Exit

Complete build:
    Single Player
    Multiplayer -> Direct Connect
    Settings
    Exit
```

**Gate:** shell/headless Godot smokes run for each composition with no fake hidden capability buttons.

---

## F0.2 — Single Player Local Play + Cabin Test World

**Goal:** promote Cabin in the Woods into the first optional built-in server/Test World and run it through the real shell without Network.

Implement:

- Local Play capability;
- local authoritative Game/server/runtime composition;
- optional Cabin Test World/content package;
- local semantic bridge from Game outputs to the same Client Core semantic stores used by remote play;
- local input bridge into normal Game command/session semantics;
- world/session enter and leave under the persistent shell;
- current world renderer/camera/input behavior through real shell;
- inventory/chat/current public-world consumers as available.

Hard rule:

- `Client.Network` and `Kernels.Network` are absent from the Single Player build.

**Gate:** launch -> Single Player -> Cabin -> move/interact/chat/inventory -> return cleanly to Main Menu, with no Network assembly required.

---

## F0.3 — Online Direct Connect + real controlled-actor parity

**Goal:** make the Online build a real remote client rather than a public-state viewer/test driver.

Implement:

- Remote Play capability under the same shell;
- Direct Connect UI;
- authenticated remote session/application flow;
- real retained `GamePlayerCommand` controlled-actor path over remote protocol;
- owner/public state convergence through Client Core;
- clean disconnect/failure return to shell;
- externally hosted Cabin Test World as parity target.

Hard rules:

- Online build does not require local Game/server implementation;
- no server discovery/list yet;
- no alternate remote-only movement semantics.

**Gate:** an external server hosts Cabin and the Online client performs the same movement/physics/interactions/chat/inventory semantics as Local Play within the limits of currently supported remote consumers.

---

## F0.4 — World presentation parity: actor definitions, nameplates, Ctrl reveal, overhead chat

**Goal:** recover the useful V2 front-facing world presentation on top of retained client semantics.

Implement:

- actor/mobile presentation definition/catalog path as needed;
- useful body/presentation variants where current assets support them;
- nameplate anchors and renderer;
- ordinary nameplate visibility policy;
- **hold Ctrl -> reveal all currently resident/renderable nameplate-eligible known entities**;
- automatic nameplate add/remove/update as Client Core knowledge changes while Ctrl is held;
- no Ctrl-triggered server request or AOI change;
- minimal public presentation metadata path for authoritative custom display labels when not derivable from content definition;
- overhead chat through existing Chat occurrence/public-anchor semantics;
- chat feed/input through persistent HUD/Menu layers;
- focus isolation so chat/menu typing does not become world movement.

**Gate:** two or more actors/entities demonstrate cached nameplate updates, Ctrl reveal, movement-following labels, retirement, and overhead/feed fallback without presentation-triggered Network requests.

---

## F0.5 — Server-composed UI + standard compiled definition library

**Goal:** make the real client prove the intended RunUO-gump-like UI architecture.

Implement:

- Godot rendering of the existing engine-neutral compiled UI path under Menu layer;
- exact definition identity validation;
- preinstalled standard compiled definition cache/library;
- cold definition transfer/cache path;
- hot cached/preinstalled path;
- server-owned panel/session lifetime fencing;
- first real Inventory/container panel through this path;
- local and remote operation through the same UI semantics;
- standard definitions remain normal definitions, not hardcoded special screens.

Do not move Main Menu/Settings/Exit into server-composed UI.

**Gate:** Inventory opens and operates through server-composed UI in Single Player and Remote Play; exact cached/preinstalled definition avoids unnecessary full-definition transfer; a custom/cold definition validates and renders; disconnect retires server-owned panels only.

---

## F0.6 — Content-source separation + contentless Online proof

**Goal:** prove the client does not require bundled game-world content.

This checkpoint may require additional content/asset-streaming archaeology because current Client World construction expects prepared structural world content.

Implement only the smallest path needed to prove:

- Built-in Test World/content is an optional package, not Client Core;
- Online build may ship without Cabin/another world pack;
- server/content identity negotiates required presentation/world content;
- validated data/assets are cached under server/content identity;
- standard fallback assets allow visibly degraded but semantically diagnosable presentation during missing-content conditions where policy permits;
- servers do not deliver executable client code;
- local resource packs remain a separate client-local remap layer.

Dynamic executable client mod loading remains deferred.

**Gate:** Contentless Online client launches with no playable world bundled, Direct Connects to a compatible server, obtains/uses the required content path, and enters/renders the server world without local authoritative Game implementation.

If the required content-transfer work is materially larger than the rest of the client campaign, stop after archaeology and split this into separately reviewed sub-checkpoints rather than hiding a new Asset Streaming campaign inside one client commit.

---

## F0.7 — Settings/debug/hardening + V2-quality shell closure

**Goal:** make the shell comfortable and diagnosable enough to become the permanent client composition oracle.

Implement/restore only useful proven features:

- coherent local Settings persistence;
- in-session options/disconnect UX;
- connection/loading/error UX;
- basic HUD polish;
- bounded Debug-layer diagnostics;
- client cache/render/UI/network diagnostics where capabilities exist;
- useful V1/V2 admin/debug ideas only where current architecture provides a real semantic owner;
- lifecycle hardening for session replacement, local->menu, remote->menu, generation replacement, world replacement, and repeated local/remote runs.

No requirement for server discovery/public browser.

**Gate:** repeated user-flow and headless smoke cycles leave no stale session world, server panels, nameplates, chat overhead, render chunks, or input focus behind.

---

## F0.8 — Final composition/parity matrix and split-readiness gate

**Goal:** prove the real client before repository generalization/extraction.

Run the complete matrix:

### Single Player

```text
launch
-> Main Menu
-> Single Player
-> Cabin in the Woods
-> move/collide/cross chunks
-> open/close door
-> see nameplates
-> hold Ctrl for all eligible resident nameplates
-> chat feed + overhead
-> open/use Inventory server-composed UI
-> Esc/in-session options
-> leave session
-> Main Menu
```

### Online

```text
launch
-> Main Menu
-> Multiplayer / Direct Connect
-> external Cabin server
-> real remote controlled actor
-> same world presentation and gameplay consumers
-> disconnect
-> Main Menu
```

### Complete

Run Local Play, return to shell, then Remote Play in one executable lifetime and prove no stale semantic/presentation/session state crosses modes.

### Contentless Online

When F0.6 is complete, prove no bundled playable world/local authority dependency is required.

### Dependency matrix

Mechanically verify forbidden assembly/project dependencies are absent for each profile.

**Gate:** ordinary gameplay/client feature work no longer requires editing kernels merely to make the shell function, and the client composition is clean enough to survive planned repository extraction.

---

# 26. Permanent acceptance scenarios

At minimum retain automated or repeatable smoke coverage for:

## Shell/capabilities

- capability absent -> menu entry absent;
- capability present -> entry works;
- no Network assembly in Single Player dependency graph;
- no local Game/server implementation in Online dependency graph;
- repeated session enter/leave returns to same shell cleanly.

## Local/remote parity

- same Cabin movement destination/collision result locally and remotely;
- remote controlled actor uses normal Game locomotion authority;
- Local Play never mutates Game directly from presentation;
- same Client Core public semantic state shape is consumed by Godot in both modes.

## Nameplates

- normal eligible actor nameplate;
- Ctrl reveal all resident eligible known entities;
- Ctrl held while entity becomes resident -> appears automatically;
- Ctrl held while entity retires -> disappears automatically;
- movement updates position without server nameplate query;
- metadata/name change updates cached display;
- hidden/non-disclosed entity cannot be revealed by Ctrl.

## Chat

- feed always receives accepted occurrence;
- compatible public anchor -> overhead bubble;
- missing/incompatible actor -> feed-only;
- chat focus suppresses movement input.

## Server UI

- preinstalled standard definition hot path;
- cached custom definition hot path;
- cold definition validate/cache/render;
- malformed/unbounded definition rejects;
- Inventory actions remain Game-authoritative;
- disconnect/session replacement retires server-owned panel/action lineage.

## Content

- missing asset falls back visibly;
- fallback does not alter semantic/gameplay truth;
- server content cache identity prevents cross-server schema/asset confusion;
- contentless build contains no bundled Test World;
- server-provided content never executes arbitrary client code.

---

# 27. Performance and boundedness posture

The real client is not required to be allocation-free in every UI frame merely for stylistic purity, but hot/scale-sensitive structures retain the existing UltraSim posture:

- bounded semantic world caches;
- bounded presentation windows;
- no object/node per terrain cell;
- no server polling for nameplate state;
- no rebuild-everything-on-camera movement;
- cached compiled UI definitions;
- model/delta updates instead of repeated schema rebuild;
- no unbounded server-controlled UI/content allocation;
- explicit cache quotas/eviction/failure policy;
- no giant per-frame scans when a retained index/change path already exists;
- UI/nameplate rendering work should follow changed/resident presentation state rather than theoretical server/ECS capacity.

Performance gates should measure whole-client behavior where useful, but correctness/ownership comes first during shell reconstruction.

---

# 28. Security/disclosure rules

The thin client model does not weaken authority.

```text
CLIENT INPUT IS INTENT, NOT TRUSTED GAME TRUTH.

SERVER DISCLOSURE DETERMINES WHICH ENTITY/PRESENTATION FACTS ENTER CLIENT KNOWLEDGE.

CTRL NAMEPLATES CAN ONLY PRESENT ALREADY-DISCLOSED CLIENT KNOWLEDGE.

SERVER-COMPOSED UI CANNOT EXECUTE ARBITRARY CLIENT CODE.

SERVER-PROVIDED CONTENT IS DATA/ASSETS, NOT SILENT CLIENT MOD INSTALLATION.

LOCAL RESOURCE PACKS CANNOT CHANGE SERVER PROTOCOL OR GAME AUTHORITY.

ONLINE-ONLY CLIENT DOES NOT NEED LOCAL SERVER/GAME IMPLEMENTATION TO PLAY.

SINGLE-PLAYER-ONLY CLIENT DOES NOT NEED NETWORK IMPLEMENTATION TO PLAY.
```

The client should fail closed on malformed UI definitions/content identities/protocol state while preserving local app-shell usability where possible.

---

# 29. Explicit non-goals for this campaign

Unless a later checkpoint is separately authorized, do not expand the client rebuild into:

- public server discovery/registry;
- matchmaking;
- account/character ecosystem redesign;
- generalized plugin/service locator framework;
- dynamic executable capability installation;
- user client mod loader/API;
- downloading/executing server code;
- giant admin/control-panel framework;
- rewriting Network;
- rewriting the UI kernel merely to imitate V2 code;
- new Physics P0.1c support/step work without a real client/gameplay need;
- new game feature campaigns unrelated to proving client composition;
- requiring the Single Player build to carry Network for convenience;
- requiring the Online build to carry local Game/server code for convenience.

---

# 30. Deferred decisions intentionally left open

These should be chosen from implementation evidence rather than frozen prematurely:

- exact final project/assembly names for Shell, LocalPlay, RemotePlay, TestWorlds;
- whether capability composition is one project per profile, MSBuild feature composition, or another compile-time structure;
- exact local semantic bridge API;
- exact presentation-metadata type for custom nameplate labels/categories;
- exact standard UI definition catalog;
- exact schema cache on-disk format/quota;
- exact server content-transfer/cache protocol and asset packaging;
- final client mod/plugin API;
- future server discovery capability shape;
- whether official capabilities later support dynamic install into an existing binary or are distributed as separately built executables.

The frozen requirement is the ownership/dependency behavior, not these spellings.

---

# 31. Definition of done

The client-shell campaign is complete when UltraSim has a real executable shell that:

1. launches independently of any active world/session;
2. has persistent World/HUD/Menu/Debug presentation layers;
3. exposes menu entries only for installed capabilities;
4. compiles as Single Player with Network physically absent;
5. compiles as Online with local authoritative Game/server implementation physically absent;
6. compiles as Complete with both capabilities;
7. promotes Cabin in the Woods to an optional built-in Test World rather than client-special-case code;
8. uses the same authoritative Game semantics locally and remotely;
9. proves the real remote controlled-actor command/Physics path;
10. drives public world/nameplate/overhead presentation from retained client knowledge, not presentation-triggered server polls;
11. supports Ctrl reveal of all already-known resident eligible nameplates;
12. keeps local Main Menu/Settings/Exit client-owned;
13. renders real server-composed UI through the existing engine-neutral UI system;
14. supports preinstalled standard compiled UI definitions and cached custom definitions through one identity model;
15. runs Inventory through that real UI path;
16. cleans all session-owned world/UI/presentation state on leave/disconnect;
17. retains a minimal fallback presentation set;
18. has an explicit path to a Contentless Online build with no bundled playable world;
19. has permanent build/dependency and behavioral acceptance gates;
20. is coherent enough that subsequent repository extraction does not require first untangling a monolithic client executable.

At that point, the client is no longer an acceptance harness around framework pieces. It is the permanent thin composition shell those pieces are meant to serve.

---

# 32. Useful archaeology/current anchors

Current V3 anchors:

- `Client/Core`
- `Client/Network`
- `Client/_Godot`
- `Client/Playable.Godot`
- `Kernels/UI`
- `Game/UI/InventoryUi.cs`
- `Game/World` public/semantic world records
- Chat C0.1a client state + overhead semantics
- Physics P0.0-P0.1b Game/Physics boundary
- Terrain T0 client semantic cache + Godot render window

Archived V2 references worth consulting for behavior/UX only:

- `_Archive/_V2/Client/GODOT_CLIENT_SHELL_ROADMAP.md`
- `_Archive/_V2/Client/UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md`
- `_Archive/_V2/Client/CLIENT_IMPLEMENTATION_PLAN.md`
- `_Archive/_V2/Client/Core`
- `_Archive/_V2/Client/UI`
- `_Archive/_V2/Client/_Godot`

Recover concepts where they fit current ownership. Do not port V2 coupling merely because a feature once existed there.

---

# 33. Roadmap summary

```text
Physics foundation                    CLOSED through P0.1b
        |
        v
F0.0  real client composition foundation
        |
F0.1  persistent shell + local app UI
        |
F0.2  Single Player + Cabin Test World, NO Network
        |
F0.3  Online Direct Connect + real remote GamePlayerCommand control
        |
F0.4  world presentation parity: actor definitions/nameplates/Ctrl/chat
        |
F0.5  server-composed UI + standard compiled definitions + Inventory
        |
F0.6  content-source separation + Contentless Online proof
        |
F0.7  settings/debug/lifecycle hardening
        |
F0.8  final local/remote/complete/contentless matrix
        |
        v
repository generalization / extraction readiness
```

**Hard stop:** do not silently turn this roadmap into server discovery, matchmaking, mod loading, account redesign, or unrelated gameplay campaigns. Review each checkpoint before advancing.
