# UltraSim Client — Thin Shell, Capability Composition, and Playable Client Plan

**Status:** frozen architectural direction with Physics P0.1b (`6bfef1e2`), F0.0a executable
composition/shipped-dependency separation, F0.0b permanent Network-free local-session extraction,
F0.1 persistent shell/settings/focus, and content A0.0 contracts/resolution accepted.
Direct-connect trust, content activation, and later content acquisition remain separately gated
before their consuming checkpoints.

**Purpose:** replace the acceptance-oriented Godot playable harness with a real persistent client shell while preserving UltraSim ownership boundaries and proving that local play, remote play, bundled content, streamed content, server-composed UI, presentation, and optional client capabilities can be composed independently.

Companion content-distribution plan: [`ASSET_STREAMING.md`](ASSET_STREAMING.md).

The milestone is not generic "front-facing cleanup." It is:

> **Reconstruct a V2-quality real Godot client shell on V3 architecture, promote Cabin in the Woods to an optional built-in Test World, and prove the same thin client can be compiled as Single Player-only, Online-only, Complete, or Contentless Online without retaining absent capabilities as hidden runtime code.**

A second client goal is now explicit:

> **The same stock client architecture should be able to feel materially different from one server to another — UO-like, third-person survival-like, or another supported interaction style — through declarative content, UI, controls, and camera profiles rather than server-supplied executable code.**

---

# 1. Executive decision

UltraSim's client is deliberately thin.

It owns:

- physical input capture;
- client-local application flow;
- local settings and accessibility/preferences;
- retained client semantic state;
- rendering/presentation;
- UI realization/focus;
- content acquisition/cache/resolution;
- camera implementation/feel;
- local diagnostics.

It does **not** become authoritative Game logic because the server happens to be embedded in the same process.

The client should exist in several concrete compositions.

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
    content acquisition/cache

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

    NO bundled playable world
    NO bundled authoritative Game implementation
    NO Single Player capability
```

The distinction is real at project/package/assembly level. A Single Player build does not merely hide Multiplayer while still shipping Network. An Online build does not merely hide Single Player while still shipping local Game/server code.

The shell exposes only features supplied by its composition.

---

# 2. Current V3 position

The current repository already has many correct independent pieces.

## Client Core

`Client/Core` is engine-neutral and Network-free. It owns retained client semantics such as:

- `ClientWorldSemanticCache`;
- bounded semantic chunk residency;
- retained public entity knowledge;
- presentation-window coordination;
- input sequencing/focus-aware semantics;
- concrete control-profile canaries;
- client chat state;
- UI runtime/cache state;
- other session-local knowledge.

This is the correct center of the real client.

## Client Network

`Client/Network` is a separate project that introduces remote protocol/Network dependencies.

That separation should become an explicit executable-composition boundary.

## Godot adapter

`Client/_Godot` is Network-free and translates Client Core semantics into native:

- rendering;
- UI controls;
- raw input capture;
- render windows/resources;
- camera/presentation;
- fallback assets.

The Godot adapter must not become the place where local-vs-remote Game semantics diverge.

## Current playable harness

`Client/Playable.Godot` currently composes Client Core, Client Network, Game projects, Server Runtime/Persistence, and Network into one acceptance executable. That was appropriate for vertical proof.

It is not the final client ownership model.

Keep it until the real shell reaches equivalent automated/manual coverage, then shrink/retire it.

The current harness already proves the intended authoritative local path: Godot forms a
`GamePlayerCommand`, Game/Physics owns the result, and Client Core owns the resulting presentation
state. It does not use Network for local movement. Before F0.0b its remaining shortcuts were
composition and lifetime shortcuts, not evidence for a different architecture:

```text
Godot thread
    directly invokes active Game generation
    directly ticks ServerRuntime
    directly scans/copies the complete Cabin publication store
    uses protocol loopback for the accepted Chat consumer
```

F0.0b replaced those shortcuts with one authority owner thread, bounded local command ingress,
bounded domain-specific semantic egress, and explicit applied/rejected local semantic proof. It
does not redesign OfflineDirect or route the host player through loopback Network.

F0.0a resolved the former shared assembly boundary. `Game/Replication` now owns packet-free
publication, disclosure, knowledge/debt, profile, and proof meaning without referencing Game
Protocol or `Kernels/Network`. `Game/Replication.Remote` contains the Network-dependent remote
codec/protocol-peer adapters. Local Play can consume Game-owned semantics without shipping either
the remote adapter or Network kernel.

## Existing UI foundation

V3 already has substantial engine-neutral UI machinery under `Kernels/UI`, including authoring, compiled definitions, models, protocol/types, and semantic codecs. `Game/UI` already has a real Inventory consumer.

The real client should prove that path rather than invent a second Godot-only game UI framework.

---

# 3. V2 archaeology to preserve

Archived V2 established useful concepts:

- one persistent client root;
- persistent World/HUD/Menus/Debug UI categories;
- client-local app-shell UI separate from server-driven UI;
- nameplates and overhead chat;
- actor/mobile definitions/catalogs;
- X/Y human presentation assets;
- RunUO-gump-like server-composed UI;
- cold/hot schema caching;
- local settings flow;
- content resolver/cache/pack stack;
- resource packs versus server patches versus executable mods;
- asset/content download from server or declared external source;
- future diagnostics/admin concepts.

Recover behaviors/boundaries, not old ownership mistakes.

Historical V2 X/Y definitions are useful calibration evidence:

```text
X Bot -> Human Female
Y Bot -> Human Male
VisualScale        1,1,1
CameraPivotOffset  y=1.6m
NameplateOffset    y=2.0m
EffectsOffset      y=0.9m
```

Those values are starting calibration evidence, not frozen universal character dimensions.

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

SERVER-COMPOSED UI IS DATA/SCHEMA, NEVER ARBITRARY EXECUTABLE CLIENT CODE.

STANDARD PREINSTALLED UI DEFINITIONS USE THE SAME SEMANTIC IDENTITY AS CACHED CUSTOM DEFINITIONS.

PREINSTALLED UI MUST NOT BECOME A SECOND HARDCODED GAME-UI PROTOCOL.

WORLD/HUD/MENU/DEBUG UI CATEGORIES ARE PERSISTENT LAYERS AND INPUT/FOCUS DOMAINS.

PRESENTATION DOES NOT POLL THE SERVER FOR FACTS THE CLIENT ALREADY KNOWS.

CTRL-REVEAL NAMEPLATES DOES NOT CHANGE AOI, SUBSCRIPTION, OR SERVER DISCLOSURE.

CLIENT SEMANTIC CACHE KNOWLEDGE IS PRESENTATION INPUT, NOT AUTHORITY.

SERVER DISCLOSURE DETERMINES WHAT THE CLIENT MAY KNOW; CLIENT POLICY DETERMINES HOW KNOWN STATE IS PRESENTED.

SERVER-RECOMMENDED CONTROLS/CAMERA/UI BINDINGS ARE DECLARATIVE CLIENT PROFILES, NOT EXECUTABLE INPUT CODE.

THE USER OWNS FINAL PHYSICAL BINDINGS AND CLIENT-LOCAL SETTINGS.

SERVER DEFAULTS NEVER DESTRUCTIVELY REWRITE USER SETTINGS.

LOCAL CONTENT, PASSIVE SERVER CONTENT, USER RESOURCE PACKS, AND EXECUTABLE CLIENT EXTENSIONS ARE DIFFERENT TRUST CLASSES.

NORMAL SERVER CONTENT IS PASSIVE/ALLOWLISTED DATA AND ASSETS.

A SERVER MAY NOT SILENTLY INSTALL OR EXECUTE CLIENT CODE THROUGH THE NORMAL CONTENT PATH.

FUTURE EXECUTABLE CLIENT EXTENSIONS, IF SUPPORTED, REQUIRE A SEPARATE PACKAGE/LOADER/CONSENT PATH.

FUTURE EXECUTABLE EXTENSIONS USE PRECOMPILED WASM OR PRECOMPILED .NET DLL PAYLOADS; THE STOCK CLIENT DOES NOT COMPILE SERVER MOD SOURCE.

CONTENT AUTHORITY AND CONTENT BYTE ORIGIN ARE SEPARATE.

THE GAME SERVER AUTHORIZES EXACT CONTENT IDENTITY; LOCAL FILES, SERVER STATIC HOSTING, OR EXTERNAL HTTPS/CDN MAY SUPPLY THE VERIFIED BYTES.

A CONTENTLESS CLIENT RETAINS A SMALL BUILT-IN BOOTSTRAP/FALLBACK PRESENTATION SET.

SINGLE PLAYER MAY USE AN EMBEDDED AUTHORITATIVE SERVER WITHOUT USING NETWORK TRANSPORT.

LOCAL PLAY MAY BYPASS PACKETIZATION; IT MAY NOT BYPASS GAME AUTHORITY OR CLIENT SEMANTIC BOUNDARIES.

THE PERMANENT LOCAL AUTHORITY RUNS ON AN OWNER THREAD INDEPENDENT FROM GODOT PRESENTATION.

LOCAL COMMAND INGRESS AND SEMANTIC EGRESS ARE BOUNDED; QUEUED DOES NOT MEAN APPLIED.

LOCAL APPLICATION KNOWLEDGE ADVANCES ONLY AFTER CLIENT CORE APPLIES AND REPORTS THE SEMANTIC RESULT.

ENABLING LAN/INTERNET HOSTING ADDS REMOTE NETWORK RECIPIENTS TO THE EXISTING AUTHORITATIVE WORLD;
IT DOES NOT RESTART THE WORLD OR MOVE THE HOST PLAYER ONTO LOOPBACK NETWORK.

DIRECT CONNECT IS THE FIRST REMOTE ENTRY POINT.

HOSTED NETWORK REMAINS PINNED-KEY-ONLY; HUMAN TOFU/EXPLICIT-FINGERPRINT ONBOARDING BELONGS TO THE
CLIENT SHELL ABOVE NETWORK.

SERVER DISCOVERY / PUBLIC SERVER LISTS ARE DEFERRED UNTIL SEPARATELY AUTHORIZED.

THE REAL CLIENT IS A PRE-EXTRACTION COMPOSITION ORACLE FOR THE FUTURE REPOSITORY SPLIT.
```

---

# 5. Ownership map

## Client shell

Owns:

- application lifetime;
- Main Menu;
- local Settings;
- Exit;
- loading/transition/error surfaces;
- Direct Connect when Remote Play exists;
- Single Player entry when Local Play exists;
- in-session local options/disconnect;
- capability-driven menu composition;
- persistent UI roots;
- orchestration of one active client session/world presentation;
- user consent surfaces for server control profiles and future executable extensions.

Does not own:

- authoritative world state;
- combat/inventory/physics permissions;
- Network protocol semantics;
- server discovery service behavior;
- server-authored gameplay menus.

## Client Core

Owns engine-neutral client state/policy:

- semantic world cache;
- public entity knowledge;
- presentation coordination;
- input sequencing/focus suppression;
- control grammar/profile interpretation;
- bounded chat state;
- UI semantic/cache state;
- layered/effective client profile state where appropriate;
- session-local knowledge lifetimes.

## Godot adapter

Owns:

- scene composition;
- native controls;
- renderer resources;
- camera implementation/feel;
- animation/audio/VFX presentation;
- raw physical input capture;
- UI focus realization;
- platform/local settings persistence;
- fallback primitives/assets.

Does not own Game semantics, protocol codecs, AOI, replication policy, or server UI state machines.

## Local Play capability

Owns composition of an authoritative in-process/local Game/server runtime for Single Player.

Bridges committed Game semantic outputs into Client Core through bounded domain-specific local
delivery/proof adapters without Network/UDP. Presentation code never directly mutates or reads
mutable authoritative Game/ECS state.

## Remote Play capability

Owns:

- client protocol/session machinery;
- Network dependency;
- remote transport;
- submission/proof paths;
- mapping remote Game semantics into the same Client Core state used by Local Play.

## Content capability

Owns acquisition/cache/resolution of passive content through the rules in `ASSET_STREAMING.md`.

It does not make the byte host semantic authority.

## Game/server

Remains authoritative in local and remote compositions.

Single Player means "the authoritative server is local," not "the client is authoritative."

---

# 6. Capability composition model

Compose the real client from explicit capabilities supplied by the executable composition root.

Do not start with process-global service locators or arbitrary reflection plugin discovery.

```text
ClientShell
    always present

LocalPlay
    optional

RemotePlay
    optional

ContentAcquisition
    present in Online/Complete/Contentless as needed

BuiltInWorldCatalog / TestWorld
    optional

Future ServerDiscovery
    optional, deferred

Future ClientExtensionHost
    optional, deferred and separately trusted
```

Menu entries follow installed capabilities.

```text
LocalPlay present
    -> Single Player

RemotePlay present
    -> Multiplayer / Direct Connect

ClientExtensionHost absent
    -> executable extension servers can be rejected cleanly rather than silently loading code
```

This is composition, not runtime flags around linked code.

## Single Player

Must compile without:

- `Client/Network`;
- `Kernels/Network`;
- remote Game protocol transport;
- Hosted Network.

Menu:

```text
Single Player
Settings
Exit
```

## Online

Must compile without local authoritative implementation dependencies such as Server Runtime/Game composition merely for play.

Menu:

```text
Multiplayer
    Direct Connect
Settings
Exit
```

No server discovery required in this campaign.

## Complete

```text
Single Player
Multiplayer
Settings
Exit
```

## Contentless Online

Remote Play + content/cache + bootstrap/fallback only. No bundled playable world or local authority implementation.

## Future installable capabilities

Architecture should leave room for installing official capabilities later, but dynamic capability installation is not required now. First prove clean project/package boundaries.

---

# 7. Local play versus remote play

The invariant is **semantic parity, not packet parity**.

## Local

```text
Godot input
    -> Client Core input/control semantics
    -> bounded local command ingress
    -> authority owner thread
    -> authoritative Game/server runtime
    -> committed Game/world/chat/UI semantics and recipient debt
    -> bounded domain-specific local semantic egress
    -> Client Core apply
    -> applied/rejected local proof
    -> authority owner thread retires or retains debt
    -> Godot presentation
```

No loopback UDP is required just to pretend local/remote are identical.

The local bridge preserves the same meaning as the remote application boundary:

```text
latest-state work may coalesce under its accepted semantic rules
Events remain independent occurrences
capacity/backpressure is explicit
queued != applied
authority cadence does not depend on Godot render cadence
```

Use concrete local public-world, Chat, UI, owner/private, and command adapters. Do not begin with a
generic `ITransport<T>` or a universal application-message bus merely because several domains cross
the same process boundary.

## Remote

```text
Godot input
    -> Client Core input/control semantics
    -> Client Network / protocol
    -> Network
    -> hosted authoritative server
    -> Game
    -> semantic publication
    -> protocol / Network
    -> Client Network
    -> Client Core semantic state
    -> Godot presentation
```

Transport differs; Game meaning and client semantic destination should not.

## Hosting from an existing local world

In a Complete build, enabling LAN/Internet hosting adds the Hosted Network listener and remote
application sessions to the already-running authoritative world:

```text
                         authoritative Game
                         /                \
             local application       remote application
                  adapter              protocol adapters
                     |                       |
              host Client Core            Network
                                             |
                                      remote Client Core
```

The host player remains on the local application adapter before and after hosting is enabled. The
world is not destroyed/recreated as a separate multiplayer authority, and the host does not begin
paying packetization, encryption, retransmission, or loopback latency. A Single Player-only build
cannot expose the listener because the Network capability is physically absent.

Forbidden:

```text
if singlePlayer:
    Godot UI -> directly mutate GameWorldState / Inventory / ECS
```

---

# 8. Cabin in the Woods becomes the built-in Test World

Promote Cabin from an acceptance fixture into the first optional built-in Test World/content package.

It is not hardcoded client logic.

```text
BuiltInServerContent/
    CabinInTheWoods/
        WorldPack
        content definitions
        server/game configuration
        UI definitions/models
        presentation assets/references
```

Exact package path/format remains unfrozen.

## Local

```text
Main Menu
    -> Single Player
    -> Cabin in the Woods
    -> start local authoritative runtime
    -> enter through Local Play semantic path
```

## Remote

The same Test World is externally hostable/reachable through Direct Connect.

## Parity objectives

- movement;
- swept collision;
- chunk crossings;
- calibrated camera/input;
- human actor scale;
- door interaction;
- inventory/container;
- chat/commands;
- nameplates/Ctrl reveal;
- overhead speech;
- server-composed Inventory UI;
- content Default/Replace/Add;
- local/server-hosted/external content-source parity;
- disconnect/session teardown;
- world/client-cache rebuild.

Cabin is the common controlled fixture for client composition, scale, physics, UI, and asset streaming.

---

# 9. Human presentation and scale calibration

A real human model is a required early client calibration tool, not cosmetic polish.

Recover X/Y Bot where licensing/source permits continued use.

Use at least one human body in the first real Cabin client; support both early enough to prove presentation definitions are not hardcoded to one mesh.

Calibrate:

```text
authoritative playable body
    currently human-scale Physics body

against

visual human model
camera pivot
camera distance/pitch/FOV
nameplate anchor
effects anchor
door/chest/wall dimensions
0.5 m terrain cells
movement-speed feel
```

Historical `1.6m` camera pivot / `2.0m` nameplate anchor are starting evidence only.

Presentation anchors belong to the presentation definition. They are not Physics constants and are not inferred from one mesh forever.

---

# 10. Persistent real-client shell

Recover the V2 persistent-shell concept.

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

Exact node names are not frozen. Ownership is.

Disconnect/world replacement retires session-owned presentation beneath the persistent shell and returns to local app flow.

---

# 11. UI layers are input/focus layers

## World

- nameplates;
- overhead chat;
- future damage numbers;
- prompts/markers.

## HUD

- chat feed;
- connection/ping;
- player HUD;
- future hotbar/status/minimap.

## Menus

- local app shell;
- local Settings;
- server-composed windows;
- focus/z-order/modal policy.

## Debug

- FPS/performance;
- Network diagnostics when present;
- cache/chunk/render/UI diagnostics;
- optional admin tooling.

Focus semantics are explicit:

```text
World focus
    gameplay input active

Chat entry
    text keys suppressed from movement

Modal Menu
    world interaction suppressed

Debug
    captures only what its active controls require
```

Do not accumulate independent Godot `_Input()` exceptions.

---

# 12. Three UI ownership classes

## 12.1 Client-local application UI

Works without server/session:

- Main Menu;
- Single Player entry if available;
- Multiplayer/Direct Connect if available;
- local Settings;
- Exit;
- loading/error/connection states;
- in-session local options/disconnect;
- server-control-profile consent;
- future executable-extension consent.

## 12.2 Standard client presentation UI

Client-owned primitives driven by already-disclosed semantic state:

- nameplates;
- overhead speech;
- chat feed;
- targeting/pointer;
- connection state;
- fallback/missing-content indicators.

## 12.3 Server-composed game UI

Game/server owns semantic windows and sends engine-neutral definitions/models/actions:

- Inventory/container;
- character sheet;
- vendor/crafting;
- dialog/quest;
- property/admin pages;
- later admission/character flows where appropriate.

Godot renders them; it does not own gameplay truth.

---

# 13. Server-composed UI and standard compiled definitions

Retain definition/model separation:

```text
UiCompiledDefinition
    layout/schema/control semantics

UiModel
    current state/value payload
```

Use exact identity such as existing/future equivalent of:

```text
UiDefinitionId / panel type
revision
hash/version
```

No "close enough" schema.

## Standard preinstalled library

Common layouts may ship precompiled in the client but remain ordinary exact UI definitions.

Good:

```text
server opens InventoryStandard hash X
client already has X
render model immediately
```

Bad:

```text
if UiId == Inventory:
    use hidden hardcoded special screen
```

## Cold/hot

Cold: obtain validated definition, cache by verified application-realm/content identity, render
model.

Hot: exact definition already present, render model/delta.

Disconnect/session replacement retires server-owned panels/actions but not local app UI.

---

# 14. Nameplates: cached presentation, never server polling

Ctrl reveal is entirely client-side over already-authorized retained knowledge.

It must not:

- send server query;
- expand AOI;
- alter subscription;
- request hidden entities;
- request names the client was not entitled to know.

```text
Game/server publication
    -> Client semantic/presentation metadata cache
    -> resident/renderable entity presentation
    -> nameplate renderer
```

Holding Ctrl:

```text
show all currently resident/renderable entities
that are already known and nameplate-eligible
```

While held:

- newly resident eligible entity appears automatically;
- retiring entity disappears;
- moving entity label follows anchor;
- metadata/name updates refresh automatically;
- no presentation-triggered polling.

Eligibility is presentation semantics, not `EntityType == Player`.

Examples:

```text
player                    yes
NPC/mob                    yes
named quest object         maybe
generic dropped item       usually no
important/named item       maybe
hidden/private entity      never disclosed, therefore impossible to reveal
```

Relatively cold public presentation metadata should carry authoritative custom display names/categories where they cannot be derived from content definition. Do not put unchanged name strings in every movement row.

---

# 15. Chat presentation

Use the accepted Chat C0.1a semantics directly:

- accepted speech is a fact/occurrence;
- authoritative display label/public anchor travels with it;
- bounded ClientChatState retains feed history;
- incompatible/missing actor suppresses overhead presentation only, not the feed fact.

Overhead belongs to World layer. Feed/input belongs to HUD/Menu. Text focus suppresses movement via shared focus policy.

No second Godot-only chat protocol.

---

# 16. Actor presentation parity

Recover useful V2 concepts:

- stable actor presentation root;
- Visual/Effects/Camera/Nameplate anchors;
- logical mobile definition/catalog;
- X/Y human variants;
- public prefab/definition id -> client presentation definition;
- animation/audio/VFX hooks;
- fallback representation.

Client never infers gameplay truth from loaded model/scene.

---

# 17. Client settings must be readable and layered

Settings should be easy to inspect, configure, diff, and diagnose. Exact serialization format (JSON/TOML/etc.) remains unfrozen, but the data must be human-readable where practical.

Conceptual roots:

```text
settings/
    client.*
    controls.*
    servers/
        <verified application realm encoding>/
            preferences.*
            bindings.*

cache/
    <verified application realm encoding>/
        client-profile.*
```

Server-provided defaults live in realm-scoped cache, **not** inside the user's settings file.

## Resolution layers

Effective settings are composed rather than destructively patched:

```text
Built-in client defaults
        ↓
User global settings
        ↓
optional accepted server recommendations
        ↓
User per-server overrides
        ↓
Resolved session settings
```

The resolved object is transient/session state.

`Use Server Defaults` does not overwrite global settings. `Keep My Controls` ignores the server's recommended physical mappings. `Customize` creates/edits per-server overrides.

User-owned values remain inspectable after leaving the server.

## Client-reserved controls

Some local controls are never taken away by a server profile, including at minimum:

- Exit application;
- open local client menu;
- Disconnect;
- open local Settings;
- platform/window essentials.

Accessibility/user-sensitive settings such as UI scale, volume, sensitivity, invert-Y, etc. remain user-owned. A server may suggest behavior-compatible defaults where appropriate but does not silently force hostile values.

---

# 18. Server-recommended control / camera / UI profiles

A server can describe how its game is intended to be controlled without shipping input code.

The current Client Core already has concrete evidence for multiple grammars:

- `OrbitCommandProfile`;
- camera-relative `ThirdPersonDirectProfile`;
- `IndependentMoveLookProfile`.

Godot currently maps a small physical InputMap vocabulary. The missing layer is declarative profile/binding composition.

Conceptually:

```text
Physical input
    W / MouseRight / MouseMiddle / GamepadRT
        ↓
Client binding profile
        ↓
Client semantic action
        ↓
Supported control grammar
        ↓
Game intent / client-local UI action
```

## Server profile may recommend/describe

```text
control grammar
recommended physical bindings
camera profile
camera defaults/limits
pointer-lock behavior
supported semantic actions
UI hotkeys
context-menu binding
interaction binding
```

Example UO-like profile:

```text
ControlGrammar = OrbitCommand
MouseLeft click       -> Select
MouseLeft hold        -> MoveTowardPointer
MouseLeft doubleClick -> Destination
MouseRight            -> Cancel/Context
Camera                 -> IsometricOrbit
```

Example third-person profile:

```text
ControlGrammar = ThirdPersonDirect
WASD                   -> Move
MouseRight             -> ObjectContextMenu
MouseMiddle            -> PrimaryFire
E                       -> Interact
Camera                  -> ThirdPersonFollow
```

Same client architecture; materially different feel.

## Action vocabulary versus key recommendation

Keep them separate.

```text
Action catalog
    Fire
    ContextMenu
    Interact
    OpenInventory

Recommended bindings
    Fire        -> MiddleMouse
    ContextMenu -> RightMouse
```

The player may rebind actions without changing Game meaning.

Do not design an unbounded universal action protocol before a real server needs more vocabulary than current Game actions can express. First prove the layered profile mechanism with earned actions/control grammars.

## Camera profiles

Server selects/configures supported client camera capabilities; it does not upload camera scripts.

Possible supported families over time:

- Orbit;
- ThirdPersonFollow;
- IndependentMoveLook;
- TopDown;
- FirstPerson later;
- RTS later.

Declarative parameters may include distance, pitch range, FOV default, yaw behavior, shoulder offset, zoom behavior, pointer lock.

A new unsupported camera grammar requires a new stock-client capability or, much later, an explicitly trusted executable extension.

---

# 19. Server profile consent and persistence

When a connected server advertises a materially different client profile, present a clear local prompt equivalent to:

```text
This server recommends custom controls.

Right Mouse    Object Context Menu
Middle Mouse   Primary Fire
Camera         Third-Person Follow

[Use Server Defaults]
[Keep My Controls]
[Customize]
```

Remember the decision by durable server/profile identity, conceptually:

```text
ApplicationRealmId
ControlProfileRevision
ControlProfileHash
```

Do not prompt every connection when exact profile identity is already accepted/overridden.

Materially changed profile hash/revision may prompt again.

Server defaults remain recommendations layered below user per-server overrides.

---

# 20. Content source model

Detailed content semantics live in `ASSET_STREAMING.md`. The client recognizes distinct classes.

## Built-in/local server content

Single Player/Complete optional content such as Cabin WorldPack, presentation definitions/assets, standard UI definitions.

## Passive server-authorized content

Validated passive data/assets required or offered by remote server:

- models;
- textures/material descriptors;
- sounds/icons;
- UI definitions;
- item/mobile presentation data;
- terrain/static/world content;
- control profile data.

This is cached by verified application-realm/content identity.

The **authoritative server names the exact package/hash**, but bytes may come from:

- local/preseeded source;
- server-hosted static HTTP;
- external HTTPS/CDN/object storage;
- bounded approved mirror list.

The byte host does not become semantic authority.

## Local resource packs

User-local visual remaps. No protocol/Game authority.

## Executable client extensions — future

Separate trust class and optional client capability.

Not normal server content.

If ever supported, a server may advertise an optional/required extension, but execution requires explicit install/consent and the client may omit extension-host capability entirely.

---

# 21. Future executable client extensions

No current feature has earned client-extension distribution. Server Game code + server-composed UI + declarative controls/camera + passive Add/Replace content should cover nearly everything ordinary server authors need, similar in spirit to RunUO's ability to create radically different server experiences on one client.

Therefore this remains deferred.

If later authorized:

```text
WASM
    preferred constrained/sandboxable runtime

precompiled .NET DLL
    advanced/full-trust runtime
```

No source compiler is included in the stock client for server extension source.

Use an explicit mod/extension package foundation with fields equivalent to:

```text
Id
Name
Description
Version
PackageHash / FileHashes
Runtime
EntryPoint
ClientApi compatibility
Dependencies
Deterministic load ordering where needed
Declared capabilities for sandboxed runtime
Publisher/signature later
Required or Optional
```

Loader responsibilities:

- validate hashes;
- resolve dependencies deterministically;
- reject cycles/missing deps/incompatible API ranges;
- compute active extension-set fingerprint;
- never execute unapproved candidate just to inspect it.

Manifests/hashes/sorting make extensions more auditable/reproducible; they do not make arbitrary full-trust code safe.

A future required extension prompt should clearly state that executable code is being installed/run and allow refusal. Refusing a required extension means refusing to join.

Signatures prove provenance, not safety.

---

# 22. Contentless Online Client

Desired final composition:

- shell;
- client core;
- Godot adapter;
- Remote Play;
- passive content acquisition/cache;
- bootstrap/fallback assets;
- no bundled playable world;
- no local authoritative Game/server implementation.

It launches, configures settings, Direct Connects, obtains validated server-authorized content from approved source(s), and renders generic semantic world/UI state.

This does not make the protocol/client impossible to reverse engineer. The meaningful property is absence of unneeded authoritative Game implementation and proprietary bundled world content.

Executable extension support is separately optional; a Contentless build can omit it.

---

# 23. Fallback assets are permanent infrastructure

Every composition retains tiny fallback presentation:

```text
missing mesh/object
    -> box/capsule/category primitive

missing texture
    -> high-visibility rainbow/checker in debug mode

texture debug disabled
    -> neutral grey/default fallback

missing animation
    -> stable pose/bounded timer
```

Fallback never changes authoritative collision/Game truth.

---

# 24. Direct Connect first; discovery later

Initial Remote flow:

```text
Main Menu
    -> Multiplayer
    -> Direct Connect
```

Future optional `ServerDiscovery` may add favorites, curated/public lists, LAN, registry, entitlement lists.

Do not build discovery merely to finish real-client reconstruction.

## Human trust onboarding above Hosted Network

Hosted Network continues to require an explicitly pinned deployment public key. It does not gain
silent self-trust or TOFU policy. The Client Shell supplies the missing human/application flow.

First connection may use either:

- an address plus an explicitly supplied fingerprint/invite; or
- TOFU after displaying the untrusted candidate deployment-key fingerprint and obtaining explicit
  local consent.

After consent, persist a trust record and perform/restart the real Hosted handshake with that exact
key pinned. A later mismatch blocks; it never silently replaces the trusted key.

```text
endpoint
    mutable location hint

deployment-key fingerprint
    cryptographic transport/deployment trust anchor

ApplicationRealmId
    durable server/shard namespace authenticated by that deployment
```

The verified application realm initially scopes per-server settings, accepted client profiles, UI
cache ownership, and content activation ownership. Do not manufacture separate UI/content/control
realm identity spaces until a concrete deployment needs them. A future rotation may preserve trust
when the old trusted key signs the new deployment key plus realm; otherwise key replacement requires
explicit user approval.

Current string-shaped UI cache fields may receive a canonical encoded verified realm identity. This
checkpoint does not require rewriting the UI kernel merely to change those fields to a binary type.

---

# 25. Remote controlled-actor parity is required

The real Online/Complete client must prove the actual retained Game locomotion path.

```text
Godot raw input
    -> Client Core control/input semantics
    -> remote protocol/Network
    -> authenticated application/control session
    -> GamePlayerCommand / GameWorldActions
    -> Physics
    -> canonical WorldPosition
    -> owner/public semantic result
    -> Client Core
    -> Godot
```

No second remote-only movement authority path.

---

# 26. Debug/admin quality-of-life

Possible Debug-layer surfaces after basic shell works:

- FPS/frame timings;
- client cache high-water;
- render/chunk diagnostics;
- Network metrics when present;
- UI definition/cache diagnostics;
- content source/cache diagnostics;
- active server profile/binding resolution diagnostics;
- optional server/admin pages;
- local/admin-authorized ECS/system diagnostics.

Useful, but not a giant mandatory admin framework.

---

# 27. Proposed package/composition direction

Exact names remain unfrozen, but use separate thin executable composition roots sharing the shell,
Core, and Godot adapter. Do not rely on one executable project with conditional references to prove
that an absent capability is physically absent. Desired dependency shape is equivalent to:

```text
Client.Core
    engine-neutral client semantics/settings/control profiles

Client.GodotAdapter
    Godot presentation/input

Client.Shell.Godot
    persistent app shell/local menus/consent/settings

Client.LocalPlay
    optional local authority bridge
    depends on Game/server
    NO Network requirement

Client.RemotePlay / Client.Network
    optional remote protocol/network

Client.Content
    passive content resolver/cache/acquisition substrate

Client.BuiltInTestWorlds
    optional Cabin package

Client.ExtensionHost          future/deferred
    optional precompiled WASM/DLL extension loader
```

No shared shell/Core/adapter package should require both Local and Remote implementations.

---

# 28. Build/profile verification

## Single Player

Required:

- compiles/launches;
- Single Player / Settings / Exit only;
- Cabin works;
- X/Y human presentation/camera calibration available as configured;
- no `Client.Network`;
- no `Kernels.Network`;
- no remote protocol transport;
- local settings/control-profile machinery still works.

## Online

Required:

- compiles/launches;
- Multiplayer/Direct Connect / Settings / Exit;
- no Single Player;
- no local Game/server runtime implementation;
- external Cabin works;
- real remote controlled actor path;
- server profile prompt/override works;
- passive content can come from server-hosted or external approved source.

## Complete

Both capabilities; local and remote Cabin converge semantically/presentationally.

## Contentless Online

- no bundled playable world;
- no local authority implementation;
- bootstrap/fallback launch;
- obtains/validates required passive content;
- normal passive content cannot silently execute code;
- extension support may be absent.

Dependency scans are permanent verifier gates.

Inspect final shipped artifacts rather than only source project references. Gates include the
resolved project graph, generated `.deps.json`, copied managed/native assemblies, and Godot export
contents. An assembly hidden from a menu but still shipped is not an absent capability.

---

# 29. Implementation roadmap

## F0.0a — Real client executable-composition foundation

**Status: CLOSED.** `Client/SinglePlayer.Godot`, `Client/Online.Godot`, and
`Client/Complete.Godot` are separate fixed composition roots over one shared
`Client/Bootstrap.Godot` menu/bootstrap. Their built `.deps.json` and copied output assemblies are
permanently verified: Single Player excludes Client Network/Game Protocol/the Network kernel,
Online excludes local Game composition/replication/Server Runtime, and Complete contains both
capability families. `Playable.Godot` served as the acceptance/reference executable for F0.0b.

Implement:

- minimal launchable shell composition roots distinct from `Playable.Godot` (persistent UI/layer
  behavior follows in F0.1);
- explicit Local/Remote capabilities;
- capability-driven menu entries;
- Single Player/Online/Complete build matrix;
- separate thin executable roots rather than conditional hidden references;
- separate the current Network-facing replication integration from the Game-owned
  publication/debt dependencies enough for the Local graph to remain Network-free;
- shipped-artifact dependency checks proving absent capabilities are physically absent.

No discovery, dynamic plugin loading, or full streaming implementation yet.

**Gate:** all compositions compile with forbidden dependencies absent.

---

## F0.0b — Permanent OfflineDirect local-session extraction

**Status: CLOSED.** `Client/Local` owns the concrete Network-free Cabin/local application bridge.
It runs mutable `ServerRuntime`/Game authority on a private owner thread and exposes only bounded
control-session-fenced command/admin/Chat ingress, one coalesced immutable owner presentation view,
and retained public-world/Chat/UI semantic channels. Client application returns applied/rejected
proof; rejected public/Chat debt is retried, rejected UI projection follows the existing terminal
view-desynchronization rule, and presentation stalls do not stop authoritative ticks.

`Client/Playable.Godot` now consumes this boundary. It no longer ticks or enters active Game
generations, scans/copies the complete publication store, or constructs a local protocol loopback.
The local public recipient uses the accepted Game chunk-interest profile and cadence; dynamic-public
AOI leave clears only public knowledge while structural Client residency remains intact.

Single Player and Complete install `UltraSim.Client.Local`; Online excludes it. The shipped-output
gate still proves that Single Player contains no Client Network, Game Protocol, Remote Replication,
Network kernel, or Hosted implementation.

Extract the behavior already proven by the playable Cabin slice into its permanent ownership shape:

- one local authoritative Game/server owner thread independent from Godot;
- bounded local command ingress using normal Game commands/control-session fencing;
- bounded local public-world, owner/private, Chat, and UI semantic egress;
- the same Game-owned disclosure/AOI/cadence policy for the local recipient;
- Client Core applied/rejected results returned as local semantic proof;
- latest-state coalescing and Event retention remain domain-specific;
- no direct Godot access to mutable ECS/Game authority;
- no complete-publication-store scan/copy every render frame;
- no protocol loopback merely to deliver local Chat/UI/application semantics;
- Network physically absent from the Single Player graph.

Do not build a universal transport/message abstraction. Extract concrete local domain adapters and
only generalize a primitive after multiple accepted consumers genuinely duplicate it.

**Gate:** the authority continues ticking when Godot presentation is deliberately stalled; bounded
local debt recovers through application proof; disclosure/final Client Core semantics match the
accepted remote path; ordinary local play ships no Network assembly.

---

## F0.1 — Persistent shell + local settings/control foundation

**Status: CLOSED.** `Client/Core` owns the engine-neutral shell lifecycle, focus policy, finite
physical-binding/action vocabulary, built-in defaults, scoped user-override document, declarative
control/camera recommendation model, and deterministic layered resolver. Recommendations are
transient and opt-in; they cannot replace reserved client controls or carry local audio/accessibility
settings. The current scope key is intentionally opaque fixture/storage vocabulary rather than a
hostname, endpoint, or claim of verified `ApplicationRealmId`; F0.3a establishes that identity.

`Client/Bootstrap.Godot` now supplies one persistent `ClientRoot` with Controller,
ContentCatalogs, World, and World/HUD/Menu/Debug layer roots. It owns readable versioned
`user://settings/client.json` persistence with fail-closed invalid-file fallback, main/settings,
loading/error/in-session-options surfaces, and reserved-control dispatch. All three fixed
compositions launch this same shell while their capability entries remain tied to physically
installed dependencies.

Concept Demo D0.1 extends the accepted settings document with the client-owned bounded
`RenderDistanceChunks` value. The supported range is currently 1–3 chunks and the default is 1.
It is deliberately absent from server control/camera recommendations: changing it rebuilds only
the client semantic/presentation working set and does not expand Game AOI, disclosure, or authority.
Settings format v2 writes the value while still admitting v1 documents as default-radius input.

Implement:

- persistent ClientRoot;
- World/HUD/Menu/Debug layers;
- Main Menu;
- readable local Settings persistence;
- physical binding representation;
- built-in client defaults;
- per-server user override store shape;
- control/camera profile selection;
- effective layered settings resolution;
- client-reserved controls;
- Exit/loading/error/in-session local options;
- focus/input ownership.

Do not need a live server to use/test these.

**Gate:** each build composition launches correct local app shell; profile/settings resolution is engine-neutral testable.

---

## F0.2 — Single Player Local Play + Cabin + human calibration

**Status: CLOSED.** `Client/Local.Godot` is the separately composed permanent Cabin consumer used by
Single Player and Complete. It binds `LocalPlayableSession` into the F0.1 World/HUD/session
lifecycle, resolves the exact A0.1 Default catalog for normal play, and realizes the frozen
Default/Replace/Add catalogs in headless acceptance through the same Godot consumer. Online has no
dependency on the local capability or built-in Test World.

The permanent path now covers normal command input, public world presentation, door/chest
interaction, Chat, server-composed Inventory UI, clean leave/re-entry with fresh application/control
lineage, the F0.1 resolved control/camera settings and bindings, and the bundled X/Y Bot human
calibration bodies. Initial presentation anchors are 1.6 m camera pivot, 2.0 m nameplate, and 0.9 m
effects; they are presentation evidence rather than Physics constants. `Playable.Godot` remains
only a thin acceptance wrapper over this consumer.

F0.2 hardening also freezes the administrative relocation ordering used by acceptance and future
teleport consumers: after authority reports success, the local client continues pumping until its
controlled-player semantic projection equals the committed authoritative position before deriving
a following absolute facing/movement point. Authority completion alone is not presentation
convergence. This closes the reproduced stale-origin facing race without changing command meaning.

Implement:

- consume the existing `Client.Local` capability;
- bind `LocalPlayableSession` lifecycle into the persistent shell and Cabin presentation;
- optional Cabin Test World package;
- normal Game input/session semantics;
- real X/Y human presentation path;
- camera/nameplate/effects calibration;
- world/session enter/leave;
- renderer/camera/input through real shell;
- inventory/chat/current public consumers.

Hard rule: no Client.Network/Kernels.Network.

This checkpoint consumes the accepted A0.0/A0.1 local content contracts/oracle from
`ASSET_STREAMING.md`; it does not invent a second Cabin package/catalog format. X/Y meshes may remain
bundled/build-time content during the first passive-content campaign.

**Gate:** launch -> Cabin -> move/interact/chat/inventory -> leave to Main Menu, with coherent human scale and no Network assembly.

---

## F0.3a — Online Direct Connect trust and session lifecycle

Implement:

- Remote Play capability;
- Direct Connect;
- candidate deployment-key discovery/display without treating it as trusted;
- TOFU and optional explicit fingerprint/invite onboarding in Client Shell;
- persistent deployment-key pin plus verified `ApplicationRealmId`;
- mismatch blocking and explicit replacement policy;
- authenticated application session;
- clean disconnect/failure;
- Online build with no local authority implementation.

Hard rules:

- Hosted Network remains pinned-key-only;
- no silent pin replacement/self-trust;
- no discovery/list;
- no executable code in trust/profile data.

**Gate:** first trust, reconnect, mismatch, explicit fingerprint, and clean teardown all preserve the
Network trust boundary and realm-scoped client state.

---

## F0.3b — Remote controlled actor and semantic parity

Implement:

- real retained `GamePlayerCommand` control;
- external Cabin parity target;
- the same Game authority/Physics path as Local Play;
- remote semantic application/proof into the same Client Core destinations;
- no remote-only movement authority or presentation state.

**Gate:** external Cabin controlled-actor, public world, Chat, owner/private, and UI consumers match
the accepted local semantic result under relevant loss/reconnect cases.

---

## F0.3c — Server client-profile negotiation and consent

Implement:

- server-advertised declarative client profile identity/hash;
- `Use Server Defaults / Keep My Controls / Customize` flow;
- remembered per-server/profile decision;
- no destructive settings rewrite.

Hard rules:

- profile identity is scoped by the verified application realm;
- only stock supported control/camera grammars and validated parameters are accepted;
- client-reserved controls remain available;
- no executable code in client profile.

**Gate:** materially different supported control profiles can be selected without changing the
client executable, while user settings and reserved controls remain intact.

---

## F0.4 — World presentation parity

Implement:

- actor/mobile definition/catalog;
- X/Y variants;
- nameplates;
- Ctrl reveal all already-known resident eligible entities;
- automatic cache-driven add/remove/update while held;
- no Ctrl server request/AOI change;
- cold public presentation metadata for custom names as needed;
- overhead chat/feed;
- focus isolation.

**Gate:** multi-actor cached nameplate/overhead behavior with no presentation polling.

---

## F0.5 — Server-composed UI + standard compiled library

Implement:

- Godot rendering of engine-neutral compiled UI;
- exact definition identity;
- preinstalled standard definition cache;
- cold/hot paths;
- session lifetime fencing;
- real Inventory/container panel;
- local/remote same semantics.

Main Menu/Settings/Exit stay client-owned.

**Gate:** Inventory works locally/remotely through normal server-composed UI; cached/preinstalled/custom definitions behave correctly.

---

## F0.6 — Content-source separation + Contentless Online integration

Asset streaming implementation is governed by `ASSET_STREAMING.md`; do not hide its full campaign in one client commit.

Client integration must prove:

- Test World/content optional, not Client Core;
- Online may ship without Cabin;
- passive server content cached by verified application-realm/content identity;
- normal content source may be local/preseeded, server-hosted static HTTP, or external HTTPS/CDN;
- byte source does not become semantic authority;
- fallback is diagnosable;
- user resource packs remain separate;
- normal passive content cannot silently install executable extensions.

Executable extension host remains deferred/separate.

**Gate:** Contentless Online connects to compatible server and renders acquired world without bundled playable world/local authority implementation.

---

## F0.7 — Settings/debug/lifecycle hardening

Implement:

- settings UX/persistence polish;
- resolved-binding/profile diagnostics;
- in-session options/disconnect;
- content/cache/source diagnostics;
- connection/loading/error UX;
- bounded debug surfaces;
- lifecycle hardening across local->menu, remote->menu, repeated runs, generation/world replacement.

**Gate:** no stale world, panels, nameplates, overhead chat, render chunks, control profile, or input focus crosses session boundaries.

---

## F0.8 — Final composition/parity matrix

### Single Player

```text
launch
-> Main Menu
-> Single Player
-> Cabin
-> human-scale camera calibration
-> move/collide/cross chunks
-> door/container
-> nameplates / Ctrl reveal
-> chat feed + overhead
-> Inventory server-composed UI
-> leave
-> Main Menu
```

### Online

```text
launch
-> Direct Connect
-> accept/keep/customize server profile
-> external Cabin
-> real remote controlled actor
-> same presentation/game consumers
-> passive content source/cache path
-> disconnect
-> Main Menu
```

### Complete

Run local then remote in one executable lifetime; no stale semantic/presentation/settings-session state crosses modes.

### Contentless Online

Prove no bundled playable world/local authority dependency.

### Different-feel canary

Use the same executable with at least two supported client profiles:

```text
Profile A
    UO-like OrbitCommand / pointer movement

Profile B
    third-person camera-relative Direct / different mouse bindings
```

This demonstrates that server-authored declarative profiles can materially change feel without a client fork.

### Dependency matrix

Mechanically verify forbidden dependencies absent.

**Gate:** client composition clean enough for repository extraction.

---

# 30. Permanent acceptance scenarios

## Shell/capabilities

- absent capability -> absent menu entry;
- present capability -> works;
- no Network in Single Player graph;
- no local authority implementation in Online graph;
- Single Player authority runs independently from Godot presentation;
- local semantic pressure cannot stall or silently advance authority knowledge;
- enabling hosting preserves the host local-session path and authoritative world;
- repeated enter/leave returns clean shell.

## Human scale/camera

- real human body appears at intended world scale;
- camera pivot/distance/FOV are human-usable;
- nameplate/effects anchors come from presentation definition;
- X/Y swap does not alter Game authority/Physics body.

## Settings/control profiles

- human-readable persisted user settings;
- server recommendation stored separately from user settings;
- Use Server Defaults does not overwrite global settings;
- Keep Mine ignores recommended physical bindings;
- per-server overrides win;
- exact accepted profile does not reprompt every join;
- materially changed profile can reprompt;
- client-reserved controls remain available;
- two supported control grammars yield expected Game intent.

## Local/remote parity

- same Cabin movement/collision result;
- remote uses normal Game locomotion authority;
- Local Play never mutates/reads Game authority directly from presentation;
- local queued semantics advance knowledge only after Client Core application result;
- local and remote recipients use the same Game-owned disclosure/AOI policy;
- same Client Core semantic shape consumed by Godot.

## Direct-connect trust

- first unpinned endpoint requires explicit TOFU or supplied fingerprint/invite;
- accepted deployment key is used as the actual Hosted pin;
- later mismatch blocks without silent replacement;
- verified application realm scopes settings/profile/UI/content ownership;
- explicit teardown does not leave candidate trust/session state active.

## Nameplates

- normal eligible plate;
- Ctrl all resident eligible known entities;
- entity enters while held -> appears;
- retires -> disappears;
- movement follows;
- metadata update refreshes;
- hidden/non-disclosed cannot be revealed.

## Chat

- feed accepted occurrence;
- compatible anchor -> overhead;
- missing actor -> feed only;
- chat focus suppresses movement.

## Server UI

- standard preinstalled hot path;
- cached custom hot path;
- cold validate/cache/render;
- malformed/unbounded reject;
- Inventory remains Game-authoritative;
- disconnect retires server panel/action lineage.

## Content

- missing asset visible fallback;
- fallback does not change Game truth;
- verified application-realm content ownership remains isolated;
- required content references wait for exact activation application proof;
- Contentless build has no Test World;
- same passive package from local/server-host/external source resolves identically;
- normal passive package cannot execute arbitrary client code.

## Future extension boundary

Until extension host exists:

- server advertising required executable extension is rejected clearly/cleanly;
- passive-content loader does not treat DLL/WASM/script as ordinary content;
- no compiler/runtime injection is pulled into the normal content path.

---

# 31. Performance and boundedness posture

The client need not be allocation-free in every UI frame, but scale-sensitive structures retain UltraSim posture:

- bounded semantic caches;
- bounded presentation windows;
- no node per terrain cell;
- no server polling for nameplates;
- no rebuild-everything on camera movement;
- cached UI definitions;
- model/delta updates rather than repeated schema rebuild;
- bounded server-controlled UI/content;
- explicit cache quotas/failure policy;
- content acquisition concurrency/retries/timeouts bounded;
- UI/nameplate work follows changed/resident state rather than theoretical ECS capacity.

Correct ownership first; measure whole-client behavior where useful.

---

# 32. Security/disclosure rules

```text
CLIENT INPUT IS INTENT, NOT TRUSTED GAME TRUTH.

SERVER DISCLOSURE DETERMINES WHICH ENTITY/PRESENTATION FACTS ENTER CLIENT KNOWLEDGE.

CTRL NAMEPLATES CAN ONLY PRESENT ALREADY-DISCLOSED KNOWLEDGE.

SERVER-COMPOSED UI CANNOT EXECUTE ARBITRARY CLIENT CODE.

SERVER CLIENT PROFILES ARE DECLARATIVE DATA; USER OWNS FINAL LOCAL BINDINGS/SETTINGS.

PASSIVE SERVER CONTENT IS ALLOWLISTED DATA/ASSETS, NOT SILENT MOD INSTALLATION.

CONTENT HOSTING LOCATION DOES NOT CHANGE AUTHORITY; HASHED PACKAGE IDENTITY DOES.

FUTURE EXECUTABLE EXTENSIONS ARE THIRD-PARTY SOFTWARE WITH EXPLICIT CONSENT, NOT ASSET DOWNLOADS.

SIGNATURES ESTABLISH PROVENANCE, NOT SAFETY.

LOCAL RESOURCE PACKS CANNOT CHANGE SERVER PROTOCOL OR GAME AUTHORITY.

ONLINE-ONLY CLIENT DOES NOT NEED LOCAL SERVER/GAME IMPLEMENTATION.

SINGLE-PLAYER-ONLY CLIENT DOES NOT NEED NETWORK IMPLEMENTATION.
```

Fail closed on malformed identities/profiles/UI/content while preserving local app-shell usability where possible.

---

# 33. Explicit non-goals for this campaign

Unless separately authorized, do not expand client reconstruction into:

- public discovery/registry;
- matchmaking;
- account ecosystem redesign;
- generalized service locator/plugin framework;
- executable extension loader/runtime;
- WASM sandbox implementation;
- downloading/executing DLLs through passive content;
- client-side source compiler;
- public mod marketplace;
- arbitrary server camera/input scripts;
- giant admin framework;
- Network rewrite;
- UI kernel rewrite merely to imitate V2;
- unrelated Physics/game feature campaigns;
- requiring Network in Single Player for convenience;
- requiring local Game/server implementation in Online for convenience.

Self-hosted/external passive content acquisition is handled in `ASSET_STREAMING.md` and is not an excuse to merge content serving into gameplay replication.

---

# 34. Deferred decisions intentionally open

- exact final project names;
- exact local semantic bridge API;
- exact presentation metadata type for custom names;
- exact standard UI catalog;
- exact settings serialization format;
- exact control-profile contract/action catalog generalization;
- exact content cache format/quota;
- exact content package/HTTP source implementation;
- final executable extension API;
- WASM runtime choice/capability model;
- future discovery shape;
- whether official capabilities later support dynamic install or separate binaries.

Frozen requirements are ownership/dependency/security behaviors, not spellings.

---

# 35. Definition of done

The client-shell campaign is complete when UltraSim has a real executable shell that:

1. launches independently of active world/session;
2. has persistent World/HUD/Menu/Debug layers;
3. exposes only installed capabilities;
4. compiles Single Player with Network physically absent;
5. compiles Online with local authority implementation physically absent;
6. compiles Complete with both;
7. runs local authority on an owner thread with bounded command/semantic/proof handoff;
8. promotes Cabin to optional Test World content;
9. uses same authoritative Game semantics locally/remotely;
10. proves real remote controlled-actor Game/Physics path;
11. onboards and pins Direct Connect identity above Hosted Network;
12. uses real human presentation to calibrate world/camera/nameplate scale;
13. drives nameplates/overhead from retained client knowledge;
14. supports Ctrl reveal without server polling;
15. keeps Main Menu/Settings/Exit client-owned;
16. has readable layered local settings;
17. can accept/ignore/customize declarative server controls/camera defaults without destructive rewrite;
18. demonstrates at least two materially different supported control profiles on same executable;
19. renders server-composed UI through engine-neutral UI system;
20. supports preinstalled and cached custom UI definitions through one identity model;
21. runs Inventory through that path;
22. cleans all session-owned state on leave/disconnect;
23. retains minimal fallback presentation;
24. has Contentless Online path with no bundled playable world;
25. consumes passive content from source-independent local/server-hosted/external sources under `ASSET_STREAMING.md`;
26. keeps executable extensions outside normal passive content and unimplemented unless separately authorized;
27. has permanent dependency/behavioral acceptance gates;
28. is coherent enough for repository extraction without first untangling a monolithic client.

At that point the client is no longer an acceptance harness around framework pieces. It is the permanent thin composition shell those pieces are meant to serve.

---

# 36. Useful archaeology/current anchors

Current V3:

- `Client/Core`
- `Client/Core/ClientWorldControlProfiles.cs`
- `Client/Network`
- `Client/_Godot`
- `Client/_Godot/GodotWorldInput.cs`
- `Client/Playable.Godot`
- `Kernels/UI`
- `Game/UI/InventoryUi.cs`
- `Game/World` public/semantic records
- Chat C0.1a client/overhead semantics
- Physics P0.0-P0.1b boundary
- Terrain T0 semantic cache/render window
- [`ASSET_STREAMING.md`](ASSET_STREAMING.md)

Archived V2:

- `_Archive/_V2/Client/GODOT_CLIENT_SHELL_ROADMAP.md`
- `_Archive/_V2/Client/UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md`
- `_Archive/_V2/Client/CONTENT_PACK_PIPELINE_PLAN.md`
- `_Archive/_V2/Client/CLIENT_IMPLEMENTATION_PLAN.md`
- `_Archive/_V2/Client/Core`
- `_Archive/_V2/Client/UI`
- `_Archive/_V2/Client/_Godot`
- X/Y human avatar definitions/assets.

Recover concepts where they fit current ownership; do not port old coupling because a feature once existed.

---

# 37. Roadmap summary

```text
Physics foundation                    CLOSED through P0.1b
        |
        v
F0.0a executable composition roots / shipped-dependency proof
        |
F0.0b permanent OfflineDirect local-session extraction     CLOSED
        |
F0.1  persistent shell + readable layered settings/control foundation CLOSED
        |
A0.0  content contracts + deterministic resolution (companion plan) CLOSED
        |
A0.1  local Cabin Default/Replace/Add oracle                         CLOSED
        |
F0.2  Single Player + Cabin + X/Y human scale/camera, NO Network     CLOSED
        |
F0.3a Direct Connect identity trust/session lifecycle
        |
F0.3b real remote GamePlayerCommand + semantic parity
        |
F0.3c server client-profile consent/settings
        |
F0.4  actor definitions/nameplates/Ctrl/chat presentation parity
        |
F0.5  server-composed UI + standard compiled definitions + Inventory
        |
F0.6  content-source integration + Contentless Online (A0 companion campaign)
        |
F0.7  settings/debug/lifecycle hardening
        |
F0.8  final local/remote/complete/contentless + different-feel matrix
        |
        v
repository generalization / extraction readiness
```

**Hard stop:** do not silently turn this roadmap into discovery, matchmaking, executable mod loading, account redesign, or unrelated gameplay campaigns. Review each checkpoint before advancing.
