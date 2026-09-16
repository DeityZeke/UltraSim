# UltraSim Client — Thin Shell, Capability Composition, and Playable Client Plan

**Status:** frozen architectural direction with F0.0a-F0.5 accepted, including executable
composition, the permanent Network-free local path, persistent shell/settings/focus, Direct
Connect trust, remote controlled play, declarative client-profile consent, and generalized actor
presentation and standard/content-backed compiled-UI convergence. A0.0-A0.8 are also accepted,
including the actual exported Contentless Online product. F0.6 is already closed/superseded by
A0.2-A0.8. M0.0 now closes the first shared multi-participant authority in both dedicated and
Complete-hosted deployment shapes. I0.12 human-play hardening prevents visible equipped gear from
remaining a pointer-nominated world pickup, and I0.13 replaces its temporary cold property text
with bounded typed Inspection Properties plus content-sized world-hover/Inventory presentation.
C0.2 now closes the first routed Global Chat mode plus explicit authority-originated System
broadcast/feed/banner convergence. D0.6 now closes direct ping indication plus the first sanitized,
read-only, content-backed Server Monitor and trusted test-marker producer. D0.7a now closes shared
actor motion/equipment/action presentation semantics and a safe bundled X/Y Idle/Walk/Jump canary.
D0.7b now closes bounded passive actor-GLB admission, applied-catalog resolution, lazy native
resource realization, and real server Add/Replace X/Y presentation in the exported Contentless
Online product. F0.7 now closes the explicit same-process local/remote/multi-realm lifecycle
campaign, including exact retirement of UI, actors, Chat, chunks, native content, server profile,
and input focus. F0.8 now closes the final physical product and semantic parity matrix: actual
Single Player, Online, and Complete Godot exports are inspected and run, exported Complete hosts
exported Online guests on its existing packet-free local authority, and the same exported Online
executable proves materially different Third Person and Orbit recommendations without content or
code replacement. A post-F0.8 defensive UI correction now closes bounded publication/shutdown,
apply-proof retry, cleanup-identity, definition-limit, malformed-delta allocation,
renderer-callback, and native adapter state/range failures without changing UI authority or
protocol identity. Pre-freeze PF0/PF1 now record the exact feature-complete candidate and close a
real ClientOnly headless bot harness plus an 8-bot/2-exported-Online shared-authority gate. PF2 now
closes first-class production UDP accounting, separate HTTP body accounting, and long-window
quiet/moving/crowd characterization. PF2.5 now reconciles the selected real-game replication owner
and completes opt-in PF3 timing/queue/debt observability without changing policy; PF3 cadence and
performance-profile implementation now closes authenticated policy/proof, the shared bounded input
scheduler, authority rate enforcement, visible/headless render-clock truth, and mismatched-cadence
proof. PF4A now closes exact headless-action conservation and the bounded clean-recipient
`ChunkPublication` readiness path. Quiet recipients observe source commits in O(1) without
entering chunk scheduling; all dirty, interest, debt, retry, recovery, cadence, and source-lineage
wake conditions remain explicit. PF4B closes its bounded Delta 120 / Applied Proof 130 knowledge
census as outcome C: no client-local proof/delta optimization was worth weakening immediate exact
knowledge. PF4C now closes selected-profile message-129 bundling with exact per-entry proof and
recovery while retaining the production 1/2/4 public-tier cadence; 1/3/6 remains an
impairment-backed future candidate. PF4C-R1 additionally closes the exact optional-bundle singleton
MTU boundary: if canonical standalone 120/121 bytes fit, considering the larger message-129
envelope cannot reject them.
PF4D now closes the later physical representation: the same exported Online ClientOnly path receives
bounded cross-lane cap-two unreliable aggregates with no added wait, while reliable traffic,
per-frame semantics, Game replication ownership, AOI, and the accepted 1/2/4 cadence remain intact.
PF4E now closes dense/distributed fanout, per-recipient fairness, exact self-public, payload-shape,
and end-to-end cost attribution on that unchanged client path. Its result justified the later
bounded selective-compression experiment now accepted by PF4F below.
PF4E-R1 additionally reconciles the selected-profile rating against current and historical
Dirty/Maintained controls. It confirms outcome B while correcting the earlier fixture explanation:
the prior PF4E dense fixture already used message-129 bundling, and its cost follows
ChunkPublication's semantic unit cost multiplied by useful chunk-publication count. The three
Chunk workloads prove there is no context-free `<128` profile boundary.
PF4E-R2 now closes the measured server CPU follow-up as outcome A: bundled Chunk publications use
one trusted bounded semantic materialization and direct message-129 staging instead of an internal
standalone encode/reparse cycle. The exported client's decoder, canonical standalone fallback,
per-entry proof semantics, wire bytes, 1/2/4 cadence, and all Network policy remain unchanged.
PF4F now closes bounded selective message-129 compression as outcome B. The ClientOnly Online path
uses fixed retained scratch to recover independently compressed Game message 148 into exact
canonical message-129 bytes, then invokes the existing canonical reader. The selected five-byte
LZ4-block wrapper is attempted only at 384 canonical bytes and selected only for at least 32 bytes
of net saving. Malformed or oversized input cannot allocate peer-sized storage or mutate Client
Core. Game manifest v29 changes; transport revision 2 and all client capability boundaries remain
unchanged. PF5 now closes product swarm/failure characterization on that same physical client:
16/32/48/64 real ClientOnly sessions, realized dense/grouped/migration topology, functional Chat,
Inventory, vertical and reconnect/churn swarms, deterministic external impairment, and mixed
actual exported Online processes. A 30-minute 64-client run retains bounded proof debt, zero source
gaps/deadline misses/catch-up ticks, action conservation, and clean retirement. No PF5 measurement
changes client protocol, cadence, AOI, capability packaging, or the 64-participant product cap;
external human validation is next.

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

Network itself now has one canonical Full assembly plus optional physical
capability stripping. Product composition currently selects:

```text
Single Player    no UltraSim.Network
Online           UltraSim.Network ClientOnly
Complete         UltraSim.Network Full
Dedicated server UltraSim.Network ServerOnly
```

Every Network-capable consumer still references the same
`UltraSim.Network.dll` identity. Full is the default developer build, canonical
package, and exact API superset; ClientOnly/ServerOnly are specialist deployment
outputs, not different protocols or permanent client/server architectures.
Product export selects its intended set explicitly and verifies the physical DLL.
Stripping is not a security mechanism, and products may switch to Full later
without source changes.

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

## Godot adapter/application

`Client/Godot` is the one production Godot script assembly. Its common adapter/shell source is
Network-free and translates Client Core semantics into native:

- rendering;
- UI controls;
- raw input capture;
- render windows/resources;
- camera/presentation;
- fallback assets.

Edition-fenced project references add either `Client.Local`, `Client.Network`, or both without
adding another Godot script assembly. The Godot adapter must not become the place where
local-vs-remote Game semantics diverge.

## Current playable application

`Client/Godot` is the maintained playable application. The former `Playable.Godot` acceptance
wrapper was retired after the permanent shell/local consumer reached equivalent automated coverage.

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

A CONTENTLESS CLIENT RETAINS A TINY RENDERER-OWNED PRIMITIVE/BOOTSTRAP SUBSTRATE BELOW THE CONTENT
SYSTEM; IT NEED NOT BUNDLE AN OPTIONAL LOCAL/DEFAULT PRESENTATION PACK.

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

## M0.0 shared multi-participant authority — CLOSED

M0.0 makes the hosting contract executable rather than aspirational. One bounded
`SharedPlayableAuthority` owns one Game generation, one prepared World, one ECS, one Physics
composition, one shared scene, and several independent participant/control/application lineages.
It has two consumers:

```text
dedicated Direct Connect
    one shared authority
        -> remote A through Network
        -> remote B through Network
        -> remote C through Network

Complete client
    existing packet-free local authority + host A
        -> enable hosted adapter on that same owner thread
        -> remote B through Network
        -> remote C through Network
```

The host process contains the server which owns the world. The local player entity does not own
the world. Stopping the hosted adapter retires remote application sessions and its listener/content
host while the existing internal server generation and local participant remain alive. Exiting the
host process still ends that embedded server; host migration is not part of this checkpoint.

Each admitted guest receives distinct participant, control-session, player, backpack, and starter
item identity. The immutable Concept Demo scene is populated once. Public replication is sourced
from the shared Game world per recipient; owner-private state and UI handlers remain attached only
to their owning application. Spatial Chat fans out across local packet-free and remote Network
recipients without sending the host player's own input or semantic delivery through loopback
packets. A validated application detach retires only that participant; a new participant may reuse
the bounded slot only with fresh generational public identity and delivery lineage.

Deployment content/profile composition is split deliberately:

```text
Server.ConceptDemo
    shared deployment content and client-profile definitions
    no Client.Local dependency

Server.ConceptDemo.LocalHost
    Complete-only adapter which borrows Client.Local authority
    adds Hosted Network/content endpoints
```

Therefore the dedicated server does not acquire the local-client authority adapter, Single Player
and Online remain physically free of the Complete-only hosted adapter, and the Complete edition
alone can add Network recipients to a running local world.

The permanent engine-neutral gate covers three simultaneous participants, independent movement,
three-recipient spatial Chat, private Inventory UI, leave/reconnect, ABA-safe public identity, and
unchanged Game generation. The native product gate launches Complete with a packet-free host,
enables hosting, joins two separately running inspected exported Online clients, requires both to
realize all three humans through the normal F0.4 path, then observes clean remote detach and stops
hosting without retiring the local world.

This is a functional three-participant topology/correctness gate, not a production population,
WAN, NAT, public-bind UX, or capacity claim. Per-recipient distant-region AOI and larger
crowd/distributed/churn evidence remain consumers of the pre-freeze campaign.

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

Overhead belongs to World layer. Feed/input belongs to HUD/Menu. Text focus suppresses movement via
shared focus policy, but the focused Chat owner must continue receiving its own submit/cancel/click-
away events even after gameplay input is suppressed. Enter submits once and releases focus only on
accepted ingress; Escape or click-away releases focus without discarding the draft; the next shell
focus evaluation restores gameplay input. Escape consumed by Chat must not also open the shell menu.

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

This primitive/bootstrap substrate sits below the content system. It is not the built-in/default
content catalog and does not participate in package activation. Local/default presentation is an
optional ordinary verified content layer; server-authorized content may Add/Replace it, and permitted
user resource packs may remap presentation leaves afterward. Failure walks back down that chain to
the primitive substrate without manufacturing content authorization or application proof.

## D0.4 local fallback canary — CLOSED

The permanent Godot renderer now consumes an explicit presentation-only mesh resolution result:

```text
Available
ModelMissing
TextureMissing
ModelAndTextureMissing
```

Model absence selects the renderer-owned category primitive. Texture absence preserves an available
model and applies the configured diagnostic or neutral fallback material. The outcome is named and
counted in bounded native chunk diagnostics. It never alters the World render key, static/Multi
identity, collision bounds, support policy, public identity, or Game interaction semantics.

The local Concept Demo D0.4 fixture proves all four combinations and an authoritative explanatory
sign. It is not evidence that CAS, HTTP acquisition, package activation, or hot resource replacement
already exists.

## D0.5 local guest/demo admission — CLOSED

The permanent Single Player shell now stops on an `Enter Demo` Admission surface before creating
local authority. The user supplies only an untrusted display-name candidate and the finite X/Human
Female or Y/Human Male body choice. Invalid input remains on the modal Admission surface and no
`LocalPlayableSession` exists yet.

Client validation provides immediate bounded feedback, but it is not authority. `AdmissionService`
revalidates the request, trims the accepted name, rejects empty/control/malformed/over-limit text,
and retains one canonical session-scoped `GameGuestIdentity`. The accepted limits are 24 Unicode
scalar values and 96 UTF-8 bytes. Body selection resolves to a concrete compiled Game/public
character prefab. The local Client consumes the accepted identity for its controlled nameplate and
Chat sender presentation; it does not reuse the originally typed value as truth.

D0.5 creates no Accounting record, durable Character ownership, uniqueness policy, profanity
service, or saved guest identity. It adds no remote protocol. The later F0.3 path may transport the
same Game-owned intent and accepted result after authentication, while keeping participant/session
identity distinct from client-supplied name/body data.

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

After consent, retain the candidate in memory and perform/restart the real Hosted handshake with
that exact key pinned. Persist the trust record only after the authenticated application realm is
received. A later mismatch blocks; it never silently replaces the trusted key.

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

The maintained application uses one Godot project and a fixed host assembly compiled as three
explicit MSBuild products. Conditional project references are accepted only because the permanent
verifier publishes every edition into an isolated output and proves the forbidden dependency closure
is physically absent. Runtime flags and hidden menu entries are not capability boundaries. Desired
dependency shape is equivalent to:

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
resolved project graph, isolated published `.deps.json`, copied managed/native assemblies, and Godot
export contents where the matching templates are installed. An assembly hidden from a menu but still
shipped is not an absent capability. `UltraSimEdition` selects the physical MSBuild graph;
Godot export custom features only detect preset/product mismatches.

---

# 29. Implementation roadmap

## F0.0a — Real client executable-composition foundation

**Status: CLOSED.** F0.0a originally established three fixed thin composition roots over one shared
`Client/Bootstrap.Godot` menu/bootstrap. Their output assemblies proved that Single Player excludes
Client Network/Game Protocol/the Network kernel,
Online excludes local Game composition/replication/Server Runtime, and Complete contains both
capability families. `Playable.Godot` served as the acceptance/reference executable for F0.0b.

Implement:

- minimal launchable shell composition roots distinct from `Playable.Godot` (persistent UI/layer
  behavior follows in F0.1);
- explicit Local/Remote capabilities;
- capability-driven menu entries;
- Single Player/Online/Complete build matrix;
- fixed explicit executable products rather than runtime-hidden references;
- separate the current Network-facing replication integration from the Game-owned
  publication/debt dependencies enough for the Local graph to remain Network-free;
- shipped-artifact dependency checks proving absent capabilities are physically absent.

No discovery, dynamic plugin loading, or full streaming implementation yet.

**Gate:** all compositions compile with forbidden dependencies absent.

## F0.0c — Single maintained Godot project / distinct physical products

**Status: CLOSED.** `Client/Godot` is now the one maintained application project, scene, project
settings, import database, and export-preset surface. Its fixed
`UltraSim.Client.Application.Godot` host compiles under one explicit `UltraSimEdition`:

```text
SinglePlayer -> common Godot source + Client.Local/local content
Online       -> common Godot source + Client.Network
Complete     -> common Godot source + both capability families
```

The default editor/development product is Complete. Canonical exports use the edition-specific
wrapper and one of three named presets. Each preset contributes exactly one custom feature so the
running product can reject a mismatched compiled edition, but that feature is not the security
boundary. Conditional MSBuild references establish the dependency closure.

The verifier independently publishes all three products, inspects each `.deps.json` and copied
managed/native output, and runs the same Godot scene against all three compiled editions. Online's
resource preset also excludes the local presentation `Assets/**`. The superseded
`SinglePlayer.Godot`, `Online.Godot`, and `Complete.Godot` source projects were retired. Subsequent
manual testing exposed duplicate C# script registration when the maintained project referenced the
additional Godot SDK assemblies for `_Godot`, `Bootstrap.Godot`, and `Local.Godot`. Those assemblies
and the obsolete `Playable.Godot` wrapper are now retired too: native adapter, shell, and local
presentation source compile into the single application assembly. The shipped-output gate requires
exactly one `UltraSim.Client.*.Godot` assembly in every edition.

**Gate:** one Godot application project launches every product; capability menus match the compiled
edition; Single Player and Online published closures still physically exclude their forbidden
families; preset and compiled edition mismatches fail closed.

---

## F0.0b — Permanent OfflineDirect local-session extraction

**Status: CLOSED.** `Client/Local` owns the concrete Network-free Cabin/local application bridge.
It runs mutable `ServerRuntime`/Game authority on a private owner thread and exposes only bounded
control-session-fenced command/admin/Chat ingress, one coalesced immutable owner presentation view,
and retained public-world/Chat/UI semantic channels. Client application returns applied/rejected
proof; rejected public/Chat debt is retried, rejected UI projection follows the existing terminal
view-desynchronization rule, and presentation stalls do not stop authoritative ticks.

The maintained `Client/Godot` local capability consumes this boundary. It does not tick or enter
active Game generations, scan/copy the complete publication store, or construct a local protocol
loopback.
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

`Client/Godot/Shell` now supplies one persistent `ClientRoot` with Controller,
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

Concept Demo D0.2 removes the controller's remaining rectangular-source assumption. Structural
residency now derives valid presentation centers from the sorted authored WorldPack key set, skips
unauthored coordinates in its bounded fringe, and uses an explicit empty reset between disjoint
windows so distant clusters do not require simultaneous old+new capacity. Missing authored keys
remain outside authored world; the controller never turns them into intentional void. Public-state
interest and delivery lifetime remain separate from this client-owned structural policy.

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

**Status: CLOSED.** The edition-fenced `Client/Godot/Local` source is the permanent local consumer
used by Single Player and Complete. It binds `LocalPlayableSession` into the F0.1 World/HUD/session
lifecycle, resolves the exact A0.1 Default catalog for normal play, and realizes the frozen
Default/Replace/Add catalogs in headless acceptance through the same Godot consumer. Online has no
dependency on the local capability or built-in Test World.

The permanent path now covers normal command input, public world presentation, door/chest
interaction, Chat, server-composed Inventory UI, clean leave/re-entry with fresh application/control
lineage, the F0.1 resolved control/camera settings and bindings, and the bundled X/Y Bot human
calibration bodies. Initial presentation anchors are 1.6 m camera pivot, 2.0 m nameplate, and 0.9 m
effects; they are presentation evidence rather than Physics constants. The maintained client is now
the sole human and acceptance executable.

F0.2 hardening also freezes the administrative relocation ordering used by acceptance and future
teleport consumers: after authority reports success, the local client continues pumping until its
controlled-player semantic projection equals the committed authoritative position before deriving
a following absolute facing/movement point. Authority completion alone is not presentation
convergence. This closes the reproduced stale-origin facing race without changing command meaning.
Later interactive hardening also makes the input sequencer application/control-session-owned rather
than projection-owned. A saved Render Distance change preserves both that monotonic command sequence
and the current-generation semantic application cache while rebuilding only structural residency and
native presentation. Already-proven dynamic-public rows therefore remain present without requiring
unrelated movement to create new publication work. A replacement Game generation still creates a
fresh semantic cache, but retains the application/control-session sequencer; authority no longer
rejects post-Settings or post-load movement as stale.

The consolidated Godot composition also preserves the C# script generator's class/file identity:
native-derived `ClientRoot` resides in `ClientRoot.cs`, while generated internal-source resources are
kept out of Godot's filesystem scan and only one Debug/Release application script assembly remains
in the live editor output. This closes the reproduced editor-Play duplicate `ClientRoot` registration
without multiplying Godot projects or script assemblies.

Programmatic batched world instances also own exact transformed custom AABBs. A zero Godot
`MultiMesh` bound is not accepted as presentation merely because semantic records, selection, and
collision exist: it permits camera-dependent false culling from the chunk origin. Native acceptance
now crosses render frames and requires nonzero render bounds for the initial public-object groups.

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

**Status: CLOSED.** `Client/Network/ClientDirectConnectSession` owns one bounded owner-thread
onboarding/session state machine. A fixed-size, rate-limited, non-amplifying UDP probe obtains a
self-signed deployment-key candidate. That signature proves possession of the advertised key only;
the candidate remains untrusted until an explicit fingerprint/invite matches or the human accepts
the displayed TOFU fingerprint. Only then does the client construct the accepted Hosted transport
with that exact key pinned.

Trust publication is transactional. Candidate acceptance remains memory-only until the pinned
handshake succeeds and `ApplicationRealmId` plus a fresh application-session epoch arrive as the
first Game Control message inside the authenticated connection. Only that complete result enters
the bounded strict `trusted-servers.json` store. The verified realm forms the durable settings
scope; address/port remains a mutable last-authenticated location hint.

A known endpoint with the same candidate key reconnects without another prompt. A different key is
blocked before handshake and cannot replace the pin. Replacement is two-step: explicitly forget
the old record, return to the main menu, then begin a fresh trust ceremony. Malformed trust files
fail closed to an empty registry. Disconnect drains Hosted transport; candidate, realm, and session
lineage do not survive teardown.

At F0.3a, `Server/DirectConnect` was the external identity-only gateway. It persists an independent
P-256 deployment key and 128-bit realm. F0.3b now attaches the separately owned remote playable
application after that accepted trust/session boundary; the F0.3a trust semantics remain unchanged.
Online continues to exclude local Game/server authority.

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
- no server list, LAN registry, or matchmaking discovery;
- no executable code in trust/profile data.

**Gate:** first trust, reconnect, mismatch, explicit fingerprint, and clean teardown all preserve the
Network trust boundary and realm-scoped client state.

---

## F0.3b — Remote controlled actor and semantic parity

**Status: CLOSED for the engine-neutral authenticated semantic vertical.** Protocol v16 adds a
bounded guest request/result/application-proof exchange, retained `GamePlayerCommand` Event ingress,
and complete latest owner state. The guest request contains only application-session epoch, attempt,
body choice, and bounded display name. Game derives participant, canonical name, actor, control
session, position, public identity, range, recipients, and authority.

`RemoteGuestPlayableApplication` is the external Concept Demo composition on the existing Hosted
application-owner thread. F0.3b originally admitted one external participant without claiming
shared multiplayer; M0.0 now composes several instances against one `SharedPlayableAuthority`
instead of constructing one isolated runtime per player. Commands are exact next-sequence,
application/control-lineage fenced, resolve optional interaction nominations through Game's
public-entity table, and execute through the same `GameWorldActions`/Physics path as Local Play. UI
capture occurs after the authoritative callback, avoiding nested Game-generation access.

`RemotePlayableSession` composes the client owner-state, `RemoteChunkWorldStateClient`,
`RemoteChatClient`, and `RemoteUiClientBridge` into the existing bounded Client Core destinations.
The F0.3b external test binds the exact deterministic Concept Demo WorldPack as verified/preinstalled
structural input, then proves movement, public player/door/chest state, spatial and private-command
Chat, owner-private Inventory UI, duplicate command fencing, future-sequence rejection, and fresh
reconnect lineage over real localhost UDP.

This checkpoint does **not** acquire World/content bytes. A contentless Online product cannot claim
native world realization until A0.2+ supplies and verifies the required structural/content artifact.
Supplying a semantic cache to `RemotePlayableSession` is not content authorization or application
proof. F0.4 subsequently generalized actor/nameplate/overhead presentation without changing this
F0.3b transport boundary.

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

**Status: CLOSED.** Game protocol v19 now places one bounded, canonical declarative client-profile
advertisement immediately after authenticated application-realm identity and before required
content or guest admission. The exact revision/hash covers only finite stock control grammar,
camera profile, bounded camera parameters, and canonical physical bindings. The decoder rejects
unknown vocabulary, malformed values, duplicate or noncanonical binding order, trailing bytes, and
oversized input. No assembly, script, resource path, or executable payload can enter the profile.

The client maps that wire vocabulary into the existing `ClientProfileRecommendation` and stops at a
local consent surface. `Use Server Defaults`, `Keep My Controls`, and `Customize for This Server`
record only the selected mode plus exact `(ApplicationRealmId, profile revision, profile hash)` in
a separate atomic settings document. The recommendation remains transient and never rewrites the
global user settings file. Exact reconnect reuses the remembered decision; either a changed hash or
a changed revision requires a new choice. A decision is durable before the client queues the exact
profile-handled proof, and the server refuses content/admission progress until that proof matches
the current application-session lineage and profile identity.

The accepted store retains **one durable exact current profile decision per realm**, not an
unbounded history keyed by every prior profile. Accepting profile B replaces that realm's decision
for profile A; if the deployment later returns to A, the client asks again. This conservative
one-per-realm policy is the concrete F0.3c contract.

The Godot Online path consumes the resulting layered settings rather than retaining the old
hard-coded third-person controller. OrbitCommand, ThirdPersonDirect, and IndependentMoveLook now
select their corresponding stock control/camera behavior in the same exported executable. Scoped
user overrides remain above the accepted recommendation, and reserved application controls remain
client-owned. The development Direct Connect deployment selects its finite profile with
`--client-profile=orbit|third-person|independent`.

Accepted scope:

- server-advertised declarative client profile identity/hash;
- `Use Server Defaults / Keep My Controls / Customize` flow;
- remembered per-server/profile decision;
- no destructive settings rewrite.

Hard rules:

- profile identity is scoped by the verified application realm;
- only stock supported control/camera grammars and validated parameters are accepted;
- client-reserved controls remain available;
- no executable code in client profile.

**Gate:** focused Client Core and external Network application tests prove exact realm/profile
decision persistence, changed-profile prompting, stale-proof rejection, pre-profile admission
blocking, layered precedence, and reserved-control retention. The actual exported Contentless
Online product proves cold decision, exact remembered reconnect, and same-realm/same-revision
changed-hash re-consent while switching from ThirdPersonDirect/ThirdPersonFollow to
OrbitCommand/Orbit without changing the executable or refetching warm content.

F0.3c does not advertise executable controls, add arbitrary action vocabularies, or implement
general remote actor/selection/nameplate presentation; those remain later consumers.

---

## F0.4 — World presentation parity

**Status: CLOSED.** Hot `WorldPublicRecord` movement remains unchanged. Client Core now combines
already-resident/disclosed hot public records with a bounded renderer-neutral actor-definition
catalog and relatively cold `WorldPublicEntityId`-fenced presentation metadata. X/Y humans and the
current woodland animal resolve through that catalog; per-instance display labels and eligibility
are not retransmitted in every movement record.

Cold metadata is server-generation, full-public-identity, revision, and application-session
fenced. Removed identities retain a bounded generation floor so delayed labels cannot attach to a
reused public slot. Protocol v20 carries the retained metadata occurrence and exact client apply
result on Control without changing chunk/AOI semantics. Local Play derives the same fact from its
Game-owned admission view; remote play applies it through `RemotePlayableSession` before the shared
client actor state reaches Godot.

`GodotActorPresenter` owns one root only for actors that earn independent moving, nameplate,
effects, and overhead-speech anchors. Ordinary public objects remain chunk-batched. Ctrl changes
only local visibility over already-presented eligible actors; it has no Network, subscription, or
AOI path. Spatial Chat uses its existing full public anchor and becomes overhead only while that
exact actor is presented; the bounded feed remains independent. Local and Contentless Online use
the same Client Core state and Godot presenter, while bundled X/Y scenes and primitive Online
fallbacks remain presentation-source choices above that seam.

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

The permanent gate includes a Godot-free X/Y add/update/remove and ABA test with zero warmed
allocation, real UDP cold-metadata/proof and Chat-anchor coverage, the existing Single Player Y-body
native admission journey, and the exported Online cold/warm journey with controlled human plus
another resident actor, Ctrl reveal, primitive fallback, and anchored overhead speech. This does
not claim a shared many-participant authority; the current external deployment still owns one
participant runtime per accepted connection.

Human-play hardening after F0.4 makes local and remote Chat focus an explicit input owner above the
gameplay gate. The Single Player native admission journey now types/submits through the real Enter
event, proves Escape remains local to Chat, proves click-away preserves the draft, and verifies that
gameplay input resumes after focus is released.

Controlled-actor presentation also distinguishes the fresh owner-private pose from the separately
delivered hot public record. The world render origin and controlled root consume the same latest
owner pose; the controlled root is therefore stable at that player-relative origin even when its
valid public record trails by a tick. Other actors continue to use disclosed public records. Mixing
the fresh origin with the older controlled public pose creates a visible backward/forward sawtooth
and is a permanent native regression case for both Local and Online composition.

The controlled display label has a short 2.5-second local join grace period rather than permanent
visibility. After that grace expires it follows the same client-local Ctrl reveal policy as every
other already-known eligible actor. Expiry and reveal do not request disclosure or change AOI.

---

## F0.5 — Server-composed UI + standard compiled library

**Status: CLOSED.** Most of the original vertical was earned before its numbered
checkpoint. `UiClientRuntime` already owns exact bounded definition caching plus cold/hot opens;
local and remote Game UI use the same model/action semantics; `GodotUiRenderer` realizes the
engine-neutral compiled tree; Inventory v7 is a real interactive compiled definition; and A0.6
proves exact content-backed `.usui` resolution through an applied realm catalog. Human-play
hardening additionally supplies centered opaque views and validated close behavior.

F0.5 closed the remaining source-convergence gap:

- define one small explicitly selected stock/preinstalled compiled-UI library at the client
  composition boundary;
- preseed the exact Inventory definition into the normal `UiClientRuntime` cache;
- prove preinstalled standard, reopened cached exact, and streamed/content-backed exact definitions
  converge on the same `UiDefinitionIdentity`, `UiModel`, `UiActionRequest`, Game handler, and Godot
  renderer result;
- prove a fresh session owns a fresh bounded runtime cache and no view/definition lifetime leaks
  through teardown;
- retain the already-accepted local/remote semantic result and cold/hot failure behavior.

`Client.StandardUi` is explicit composition data and references only the UI kernel. Its embedded
1,158-byte canonical Inventory v7 artifact decodes to the exact
`game.inventory.gump/v7/b0b3e87ced073c8a40778127ba583993` identity. It is not a reason for generic
Client Core to depend on every Game UI definition. In particular, the Online product preseeds passive
definition bytes/data without acquiring the Game UI/Inventory authority assemblies. Main
Menu/Settings/Exit stay client-owned. Required content authorization/application proof also remains
independent: possessing the same preinstalled definition cannot claim that a required realm catalog
was applied.

**Accepted gate:** the same Inventory definition and authoritative actions work through preinstalled
standard, cached exact, and content-backed exact sources in local/remote composition; identity,
application proof, session teardown, and Online's no-local-authority dependency gate remain exact.

The local standard path emits a real `HotOpen`; the remote standard path resolves the passive stock
artifact with zero content-definition resolutions; and both reach the same authoritative Inventory
Select result. Independently decoded exact definitions may rebind dynamic model state by complete
compiled handle, never by object reference or a partial key. A different key/version/hash rejects.
The exported Online product contains `Client.StandardUi` but still contains neither Game UI nor
Game Inventory authority assemblies. Possession of the stock bytes does not bypass the already-
accepted authenticated content-plan or `ContentSetApplied` gates.

Human-play hardening centers the fixed logical Canvas within the current viewport. Informational
and Inventory definitions provide an opaque surface and a compiled, validated Close action;
Inventory `I` toggles its existing view instead of stacking another transparent view. Window
dragging/placement persistence remains later UI-shell polish, not a new semantic requirement.

Pointer nomination now tests bounded full-height stock presentation volumes before falling back to
terrain projection. Clicking the upper portion of a door or sign therefore nominates that already
disclosed object rather than the ground behind it. This remains client presentation: Game still
validates exact target identity, range, and interaction legality.

---

## F0.6 — Content-source separation + Contentless Online integration

**Status: CLOSED / SUPERSEDED BY A0.2-A0.8.** Do not schedule a second F0.6 implementation.
The companion Asset Streaming campaign established persistent CAS/install state, authenticated
requirement and application proof, source parity, repair/delta/mirrors, typed UI and WorldPack
consumers, and finally an inspected exported Online product with no bundled playable WorldPack or
local authority closure. A0.8 exceeds the original F0.6 gate.

The accepted closure proves:

- Test World/content optional, not Client Core;
- Online may ship without Cabin;
- passive server content cached by verified application-realm/content identity;
- normal content source may be local/preseeded, server-hosted static HTTP, or external HTTPS/CDN;
- byte source does not become semantic authority;
- fallback is diagnosable;
- user resource packs remain separate;
- normal passive content cannot silently install executable extensions.

Executable extension host remains deferred/separate.

**Gate: PASSED by A0.8.** Contentless Online connects to a compatible server and renders the
acquired world without a bundled playable world or local authority implementation.

---

## D0.6 — Read-only Server Monitor, ping, and trusted test markers

**Status: CLOSED.** This is a bounded diagnostic consumer checkpoint, not the full F0.7 lifecycle
or diagnostic-capture campaign.

Game/authority owns one sanitized `ServerMonitorSnapshot`: generation and uptime, simulation
tick/frequency, recent and p95 tick duration, entered/capacity/entity/join/leave counts, last trusted
test-marker transition, and at most 64 entered participant rows. Rows carry public display name,
Local/Remote kind, sanitized remote RTT, and coarse Spatial chunk. They carry no endpoint/IP,
credential/key, account/machine identity, exact position, owner-private state, or administrative
action.

`F4` is the stock client binding for opening/toggling the monitor. Local play emits the normal cold
compiled `game.server-monitor.gump/v1` definition through its packet-free semantic bridge. Remote
play authorizes the exact passive `.usui` in the applied realm catalog and converges through the
existing content-backed `HotOpen`, `UiClientRuntime`, and generic Godot renderer. The server UI
controller does not choose transport hot/cold behavior, and its sole action is Close.

Online HUD ping is projected directly from the authenticated protocol peer's sanitized smoothed
RTT. Packet-free local play displays `Local`; it does not construct a loopback peer. Monitor models
refresh approximately once per second. Authoritative tick sampling uses a fixed 128-sample ring;
temporary bounded UI backpressure leaves the prior view valid and retries later.

Trusted marker START/COMPLETE transitions publish the already-accepted C0.2 System occurrence, so
the same fact reaches retained Chat feed and transient banner. The last transition also appears in
the monitor. No player marker command, second banner message, or general operator/RBAC system was
added.

Permanent gates cover dedicated A/B/C, Complete packet-free Host A plus remote B/C,
disconnect/reconnect row retirement, local single-player parity, marker fanout, absence of private
endpoint material, and the exported Contentless Online monitor `.usui`/native participant-list path.
Opening the monitor does not mutate simulation or owner state.

Broad diagnostic capture, tabbed subsystem telemetry, process/network-rate detail, and admin
controls remain open. The separate F0.7 mixed-session lifecycle torture pass is now closed.

---

## D0.7a — Actor motion/equipment/action presentation foundation

**Status: CLOSED.** This is the first half of the Concept Demo animation closure. It proves shared
presentation semantics and safe local/default animation assets without claiming that runtime models
are already an A0 content kind.

The existing F0.4 cold actor row now carries only three additional public facts:

```text
MainHand
    equipped public prefab only

Vertical phase
    Grounded | Rising | Falling

Latest accepted presentation action
    Interact | Primary
    + actor-local control sequence
```

Game derives Main Hand from the authoritative Inventory index and records an action only after its
command succeeds. Exact sword identity/rolls, Inventory topology, and action legality remain private
or Game-owned. Hot position records remain unchanged. Dedicated shared authority, remote guests of
a Complete host, and the host's packet-free adapter converge on the same bounded actor read model.
A client which first sees an actor after an action records the sequence without replaying the cue.

Godot derives horizontal `Idle`/`Walk` from successive committed authoritative poses. It selects
`Rising`/`Falling` from Game's finite vertical phase, and produces one landing cue only when that
phase transitions from airborne to `Grounded`. A jump apex or ceiling bonk therefore cannot be
misread as landing. Godot never publishes animation state upstream. A semantic
`MainHandSocket` attaches public equipment to a matching right-hand bone or to a primitive actor's
fixed fallback anchor. Equipped gear is excluded from independent world batching/picking; the same
entity returns to ordinary GroundItem presentation after authoritative Drop.

The historical V1.5 X/Y FBXs kept model and action skeletons as separate Godot imports. D0.7a moves
those inputs out of the runtime resource tree and uses one reproducible Blender conversion per body.
Each generated GLB contains one body, one ordered 65-bone-name profile, and sampled `Idle`, `Walk`, and
`Jump` actions. Native acceptance loads both real generated scenes, verifies their clips/socket, and
drives committed movement through the Walk phase. This avoids runtime cross-scene skeleton
attachment/retargeting and its prior distortion/misalignment failure mode.

The archive contains no Attack or Interact clip. Accepted actions therefore retain short bounded
primitive cues; missing animation never changes Game success. Online excludes bundled `Assets/**`
and exercises the same semantic presenter through the humanoid/equipment primitive substrate.

---

## D0.7b — Runtime content-backed actor model/animation realization

**Status: CLOSED.** Content now admits one deliberately narrow `ActorPresentation` definition
backed by a self-contained binary glTF 2.0 artifact. Admission checks the container and JSON before
catalog publication and bounds nodes, meshes, primitives, vertices/indices, materials, accessors,
buffer views, bones, animations, tracks, keys, strings, and total bytes. The first profile permits
embedded binary data, triangle meshes, one skeleton, and only the reviewed material extension. It
rejects external references, images/textures, cameras, unsupported extensions, Godot scenes,
scripts, shaders, and generic executable resources. The Concept Demo then additionally requires its
exact ordered 65-bone-name fingerprint, semantic right-hand bone, and exactly one normalized
`Idle`/`Walk`/`Jump` clip. Unknown animation semantics and duplicate semantic aliases fail passive
admission. The fingerprint does not claim parent-topology, rest-transform, or inverse-bind identity;
arbitrary skeleton retargeting remains outside this profile.

Actor semantics request stable logical definitions `actors:human/x` and `actors:human/y`, never a
Godot path. The Direct Connect deployment authorizes a server actor package which Adds both bodies,
then an unmistakably recolored package which Replaces X. Required content/application proof still
precedes admission. Possessing the same bytes outside the live applied realm catalog cannot resolve
the actor definition or satisfy that proof.

The Godot adapter realizes an actor lazily when the existing presenter first needs its logical
definition. It imports only the validated bytes into an UltraSim-owned inert scene structure,
rechecks the skeleton/socket/clips, retains the exact catalog/blob lease with the native template,
and duplicates that template for actor instances. It does not instantiate a downloaded `.tscn` or
script. Failure remains a finite diagnostic and falls down to the humanoid primitive where fallback
is permitted.

Refreshing a newly available visual replaces only the presentation child. The existing actor root,
`WorldPublicEntityId`, controlled/remote role, nameplate, overhead Chat, action-sequence fence, and
Main Hand state survive. Content realization therefore cannot create a new actor lifetime. Native
acceptance proves primitive-to-content replacement plus lazy X/Y realization through the same
`GodotActorPresenter` used by bundled/local bodies.

The inspected exported Online product still contains no bundled X/Y GLB. Its cold run acquires the
World/UI/X/Y/Replace payloads, publishes the exact catalog, realizes an animated human beside the
intentional primitive woodland actor, and enters gameplay. A fresh warm process transfers zero
content bytes, produces a fresh application-session proof, and realizes the same actor content from
CAS. Dedicated and Complete-hosted shared-authority journeys use the same remote path.

This checkpoint does not add arbitrary GLB compatibility, textures/audio, user overrides, runtime
retargeting, downloadable scenes, hot activation protocol, or Attack/Interact clips. The existing
bounded primitive action cues remain the correct fallback for the absent clips.

---

## F0.7 — Settings/debug/lifecycle hardening

**Status: CLOSED.** Earlier settings focus, Chat focus, Render Distance projection replacement,
export cleanup, client-profile persistence, content-cache restart, controlled-actor lifetime, and
repeated local entry fixes are now joined by an explicit native same-process lifecycle gate.

Implement:

- settings UX/persistence polish;
- resolved-binding/profile diagnostics;
- in-session options/disconnect;
- content/cache/source diagnostics;
- connection/loading/error UX;
- bounded debug surfaces;
- lifecycle hardening across local->menu, remote->menu, repeated runs, generation/world replacement.

**Gate:** a deliberate same-process sequence such as local -> menu -> local -> menu -> remote ->
disconnect -> another remote realm -> menu -> local leaves no stale world, panels, actors,
nameplates, overhead Chat, render chunks, control profile, trust candidate, content application
session, or input focus across boundaries.

The permanent Complete-client gate runs this exact sequence:

```text
local X
    -> retire
local Y
    -> retire
remote realm A
    -> retire
remote realm B
    -> retire
remote realm A reconnect
    -> retire
local X
    -> retire
```

Before each retirement it deliberately leaves a real UI view open, multiple actors/nameplates,
overhead Chat, rendered chunks, and focused Chat input. Remote lifetimes additionally retain lazy
native actor resources. Every return to the main menu must reproduce the exact persistent Godot
root shape captured before the first session, clear the active server profile, restore global
control grammar, camera profile/distance/FOV, physical bindings, and render distance, release the
Direct Connect transport and presentation consumers, and contain no local or remote session node.

The two remote deployments have distinct deployment keys, `ApplicationRealmId` values, and
realm-bound catalog fingerprints, but serve the same World/actor/profile payload contract. First
entry into A and B each creates an independent decision; returning to A reuses its one durable exact
current profile decision while establishing a fresh
application-session epoch. The same-process test caught and repaired an automatic-consent path that
wrote the decision file without updating the live decision document. Durable trust, content bytes,
and exact profile consent are intentionally retained; transient session/application/presentation
state is not.

---

## F0.8 — Final composition/parity matrix

**Status: CLOSED.** The final gate inspects and launches the actual exported Godot products rather
than treating managed publish graphs or editor runs as product evidence. Single Player has an exact
22-assembly UltraSim closure with no Network assembly; Online has an exact 13-assembly closure with
the `ClientOnly` Network capability set, no local authority, no bundled WorldPack/package, and no
bundled actor GLB; Complete has an exact 29-assembly closure with Full Network plus both local and
remote capabilities.

The exported Single Player and Complete binaries each run their native composition/local-world
canary. The exported Complete binary then starts Single Player as host A, adds its listener to that
same authoritative generation while A remains packet-free, and admits two separately running
exported Online binaries. This proves physical product shape and local/remote semantic parity in
one acceptance journey.

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

The permanent canary uses one exported Online executable and one stable deployment/realm/content
identity. Its Third Person recommendation resolves to `ThirdPersonDirect`, `ThirdPersonFollow`,
8.5 m, 76 degrees, middle-click Select, and left-click Primary. The changed Orbit profile resolves
to `OrbitCommand`, `Orbit`, 14 m, 70 degrees, left-click Select, and middle-click Primary. The
profile hash change requires a fresh local decision, but the warm content path transfers zero
bytes. No executable, WorldPack, catalog, realm, or deployment-key change is involved.

### Dependency matrix

Mechanically verify forbidden dependencies absent.

**Gate: CLOSED.** Client composition is clean enough to enter pre-freeze product characterization.
Permanent evidence lives in
[`client-f0.8-final-composition-parity-2026-09-09.json`](../../_Benchmarks/Baselines/client-f0.8-final-composition-parity-2026-09-09.json).

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

## Shared authority

- one dedicated Game generation admits at least three independent remote participants;
- one Complete local world enables hosting without replacing its generation;
- the Complete host remains on the packet-free local semantic path;
- two exported Online clients join that existing world through Network;
- every client realizes all three human actors through the shared F0.4 path;
- spatial Chat reaches local and remote recipients while owner-private UI stays isolated;
- one remote leave/reconnect does not retire or reset the remaining participants;
- stopping the hosted adapter disconnects remotes but leaves the internal server/local player alive;
- Single Player and Online remain physically free of the Complete-only local-host adapter;
- dedicated server composition remains physically free of `Client.Local`.

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

CANONICAL ULTRASIM.NETWORK IS FULL; STRIPPED BUILDS ARE OPTIONAL PRODUCT ARTIFACTS.

NETWORK ASSEMBLY IDENTITY DOES NOT PROVE WHICH PHYSICAL ROLE SET IS PRESENT.

NETWORK CAPABILITY STRIPPING DOES NOT CHANGE SHARED PROTOCOL BEHAVIOR.
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
- `Client/Godot`
- `Client/Godot/Adapter/GodotWorldInput.cs`
- `Kernels/UI`
- `Game/UI/InventoryUi.cs`
- `Game/World` public/semantic records
- Chat C0.1a client/overhead semantics
- Physics P0.0-P0.1d boundary plus accepted Concept Demo playability hardening
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
Physics foundation                    CLOSED through P0.1d + demo playability hardening
        |
        v
F0.0a executable composition roots / shipped-dependency proof            CLOSED
        |
F0.0b permanent OfflineDirect local-session extraction                  CLOSED
        |
F0.0c one maintained Godot project / three physical products            CLOSED
        |
F0.1  persistent shell + readable layered settings/control foundation CLOSED
        |
A0.0  content contracts + deterministic resolution (companion plan) CLOSED
        |
A0.1  local Cabin Default/Replace/Add oracle                         CLOSED
        |
F0.2  Single Player + Cabin + X/Y human scale/camera, NO Network     CLOSED
        |
F0.3a Direct Connect identity trust/session lifecycle                    CLOSED
        |
F0.3b real remote guest/GamePlayerCommand + semantic parity              CLOSED
        |
A0.2  persistent verified CAS/install substrate                         CLOSED
        |
A0.3  authenticated requirement + source-independent acquisition        CLOSED
        |
A0.4  remote Default/Replace/Add + physical source parity                CLOSED
        |
A0.5  cache/delta/corruption/mirror hardening                            CLOSED
        |
A0.6  cached/streamed UI schema convergence                              CLOSED
        |
A0.7  complete canonical WorldPack streaming                              CLOSED
        |
A0.8  native Contentless Online world entry                              CLOSED
        |
F0.6  content-source integration + Contentless Online                     CLOSED
      superseded/satisfied by A0.2-A0.8
        |
F0.3c server client-profile consent/settings                              CLOSED
        |
F0.4  actor definitions/nameplates/Ctrl/chat presentation parity          CLOSED
        |
F0.5  standard/preinstalled + cached/content-backed compiled UI closure   CLOSED
        |
M0.0  shared multi-participant authority
      dedicated A/B/C + Complete packet-free host A / remote B/C           CLOSED
        |
I0.12 persistent rolled world item / Inventory / equip / Primary / drop     CLOSED
      + bounded property tooltip / equipped-item pointer hardening
        |
Network physical capability packaging
      canonical Full + optional ClientOnly/ServerOnly same-identity outputs  CLOSED
        |
I0.13 typed Inspection Properties + content-sized shared tooltip              CLOSED
        |
C0.2  routed Global + authority System Chat / transient banner              CLOSED
        |
D0.6  read-only Server Monitor / ping / trusted test markers                  CLOSED
        |
D0.7a shared actor motion/equipment/action + bundled X/Y animation             CLOSED
        |
D0.7b runtime content-backed model/animation realization                       CLOSED
        |
F0.7  explicit settings/debug/mixed-session lifecycle closure             CLOSED
        |
F0.8  final local/remote/complete/contentless + different-feel matrix      CLOSED
        |
UI defensive assurance: publication/lifetime/parser/native boundaries      CLOSED
        |
PF0/PF1 candidate + real headless/mixed exported-client swarm           CLOSED
PF2 first-class wire accounting + explanatory baselines                 CLOSED
PF2.5 replication ownership reconciliation + PF3 observability          CLOSED
PF3 cadence/performance policy                                           CLOSED
PF4A action evidence + quiet publication work suppression                CLOSED
PF4B Delta/proof knowledge census                                           CLOSED (outcome C)
PF4C selected Game bundling / public-tier cadence                           CLOSED (bundling; cadence retained)
PF4C-R1 singleton MTU boundary repair                                        CLOSED
PF4D unreliable Network-frame aggregation                                   CLOSED (cross-lane cap two)
PF4E dense/distributed fanout + remaining-cost attribution                  CLOSED (outcome B)
PF4E-R1 rated replication scaling reconciliation                            CLOSED (outcome B)
PF4E-R2 ChunkPublication CPU efficiency                                     CLOSED (outcome A)
PF4F bounded selective message-129 compression                              CLOSED (outcome B)
PF5 product swarm/scaling/failure characterization                           CLOSED
PF5-R1 replication baseline/history-model audit                             CLOSED (outcome B)
PF5-R2 shared latest-state publication                                       CLOSED (outcome A)
PF5-R2 independent review + combined timing closure                          CLOSED (PF5-R2-R1 timing accepted; outcome D)
PF5-R2-R2 HTTP shutdown + normalized scaling attribution                     CLOSED
PF5-R3 bounded background UDP egress                                         CLOSED (outcome A)
PF5-R4 recipient preparation / encoding flattening                           CLOSED (outcome A)
PF5-R5 singleton compression / prepared-hash hardening                       CLOSED (outcome B)
PF5-R6 compatible immutable recipient-body sharing                           CLOSED (outcome A)
PF5-R7 spatial topology / whole-server attribution                           CLOSED (characterization)
PF5-R8 required fanout / avoidable-processing assurance                      CLOSED (measurement; R1 controls corrected)
PF6 external human validation                                                READY TO RESUME
        |
        v
repository generalization / extraction readiness
```

**Hard stop:** do not silently turn this roadmap into discovery, matchmaking, executable mod loading, account redesign, or unrelated gameplay campaigns. Review each checkpoint before advancing.
