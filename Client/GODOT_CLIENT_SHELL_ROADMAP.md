# Godot Client Shell Roadmap

Purpose: rebuild the Godot client shell without losing the modular shape of the old client.

This is the active checklist for the rebuilt `Client/_Godot` shell. The old v2 client under `_OLD` remains the primary behavioral and scene-composition reference, but the current implementation should target the rebuilt `Client/Core`, `Client/Network`, `Kernels/Network`, `Server/Network`, and `Server/Core` boundaries.

The older v1 tree under `_OLD/_OLD` is reference-only. Pull from it only when it has a specific idea worth rescuing, such as the old hybrid client render/culling experiment or the old admin control panel concepts. Do not copy its runtime shape forward.

## Current Read

The current rebuilt client has useful engine-agnostic pieces:

- `Client/Core`
  - movement input/state contracts
  - input sequencing
  - prediction store
  - interpolation buffer
- `Client/Network`
  - live movement session host
  - movement input send path
  - authoritative movement receive path
  - observer latest-state/event consume path
  - chat event bridge

The current Godot client shell now has a real thin-host spine:

- `Client/_Godot/project.godot`
- `Client/_Godot/UltraSim.Client.Godot.csproj`
- `Scenes/ClientRoot.tscn`
- `GodotClientRuntimeRoot`
- persistent world containers:
  - `World/Map`
  - `World/DynamicEntities`
  - `World/Effects`
  - `World/CameraRig`
- persistent UI layers:
  - `UILayers/World`
  - `UILayers/HUD`
  - `UILayers/Menus`
  - `UILayers/Debug`
- preview actor flow:
  - `CharacterRoot`
  - `MobileDefinition`
  - `MobileDefinitionCatalog`
  - `ClientCharacterWorld`
  - `LocalPlayerController`
  - `OrbitFollowCameraRig`
- local app shell flow:
  - start menu
  - manual server browser row
  - direct connect
  - settings placeholder
  - in-session options
  - shared chat panel
- current network shell bridge:
  - direct UDP session driver over `ClientMovementSessionHost`
  - movement input send path
  - owner movement receive path
  - observer latest-state receive path
  - chat submit/event display path
  - ping/status HUD summary

This means Phase G0, G1, G2, G3, and the pre-schema portion of G5 are implemented and validated on the current Linux/Godot Mono setup. The remaining client bottleneck is no longer "create a shell"; it is reintroducing the old v2 feature set on top of the rebuilt runtime shape: discovery polish, server-driven schema UI, login/character flow, reconnect/persistence validation, content-backed gameplay panels, and admin/debug panels.

Latest pre-G6 validation on Linux:

- `UltraSim.Client.Tests`: passed
- `UltraSim.Server.Tests`: passed
- `dotnet build Client/_Godot/UltraSim.Client.Godot.csproj -c Release`: passed
- `dotnet build Server/_Godot/UltraSim.Server.Godot.csproj -c Release`: passed
- Godot client content-cache/preview smoke: passed
- Godot server + client live direct-connect smoke: passed
  - connected over UDP
  - owner movement acknowledged
  - chat echoed
  - client disconnect cleared session world
  - server empty-session smoke satisfied

The active Godot executable used for these smokes is:

```text
/mnt/data/Programs/Godot v4.6.2 Mono/Godot_v4.6.2-stable_mono_linux.x86_64
```

## Testing Posture

Tests should land early and often.

The first implementation slice should avoid putting substantial behavior directly into Godot scripts if that behavior can reasonably live in `Client/Core`, `Client/Network`, or a shared content/UI library. Godot-specific scene glue still needs build/smoke validation, but most policy and state-machine behavior should be testable without launching Godot.

Expected coverage pattern:

- each `Client/Core` behavior gets focused unit tests
- each `Client/Network` behavior gets packet/session/bridge tests
- shared UI/content/cache behavior gets codec/validation/cache tests
- Godot scene work gets compile/build validation and a small manual smoke checklist until there is a practical automation harness

Do not defer testability until the shell is "done." The old ECS/network approach paid off because each slice stayed measurable and regression-tested.

Current Godot smoke hooks:

- client shell:
  - `--us-connect=host:port`
  - `--us-connect-host=host`
  - `--us-connect-port=port`
  - `--us-connect-max-handshake-attempts=n`
  - `--us-connect-handshake-retry-seconds=seconds`
  - `--us-connect-ready-keepalive-timeout-seconds=seconds`
  - `--us-content-cache-root=path`
  - `--us-smoke-quit-after=seconds`
  - `--us-smoke-require-connected`
  - `--us-smoke-require-connected-count=n`
  - `--us-smoke-disconnect-after=seconds`
  - `--us-smoke-final-disconnect-after=seconds`
  - `--us-smoke-reconnect-after=seconds`
  - `--us-smoke-require-disconnected`
  - `--us-smoke-require-session-failed`
  - `--us-smoke-session-failed-contains=text`
  - `--us-smoke-require-content-cache`
  - `--us-smoke-chat-after=seconds`
  - `--us-smoke-chat-text=text`
  - `--us-smoke-require-chat-received`
  - `--us-smoke-move-after=seconds`
  - `--us-smoke-move-duration=seconds`
  - `--us-smoke-move-x=-100..100`
  - `--us-smoke-move-y=-100..100`
  - `--us-smoke-require-owner-movement`
  - direct-connect failure policy: 5 handshake attempts, 0.5s apart, then an error screen/status
  - graceful server shutdown policy: explicit disconnect notice clears session-owned world state immediately
  - crash/loss fallback policy: 5s without server pongs clears session-owned world state and reports connection loss
- server shell:
  - `--us-server-start`
  - `--us-server-port=port`
  - `--us-server-profile=name`
  - `--us-server-tick-ms=n`
  - `--us-server-max-catchup=n`
  - `--us-server-session-auth-timeout-seconds=seconds`
  - `--us-server-session-keepalive-timeout-seconds=seconds`
  - `--us-server-no-movement-world`
  - `--us-smoke-quit-after=seconds`
  - `--us-smoke-require-empty-sessions`
  - `--us-smoke-empty-session-grace=seconds`

These are smoke/test hooks only. Normal interactive flow still goes through the local app shell and server operator shell.

## Old Client Pieces Inspected

Primary old code references:

- `_OLD/Client.Godot/_Scenes/client.tscn`
- `_OLD/Client.Godot/GodotClientRuntimeRoot.cs`
- `_OLD/Client.Godot/GodotClientSession.cs`
- `_OLD/Client.Godot/NetworkClientSessionDriver.cs`
- `_OLD/Client.Godot/PreviewClientSessionDriver.cs`
- `_OLD/Client.Godot/GodotClientNetworkRuntime.cs`
- `_OLD/Client.Godot/World/ClientCharacterWorld.cs`
- `_OLD/Client.Godot/Controllers/LocalPlayerController.cs`
- `_OLD/Client.Godot/_Prefabs/Camera/OrbitFollowCameraRig.cs`
- `_OLD/Client.Godot/_Prefabs/Mobiles/CharacterRoot.cs`
- `_OLD/Client.Godot/_Prefabs/Mobiles/*Definition*`
- `_OLD/Client.Godot/UI/UiManager.cs`
- `_OLD/Client.Godot/UI/WorldCanvasManager.cs`
- `_OLD/Client.Godot/UI/HudCanvasManager.cs`
- `_OLD/Client.Godot/UI/MenuCanvasManager.cs`
- `_OLD/Client.Godot/UI/ClientAppShell.cs`
- `_OLD/Client.Godot/UI/ClientChatMenu.cs`
- `_OLD/Client.Godot/UI/ServerDriven/*`
- `_OLD/UltraSim/Client/Runtime/Discovery/*`
- `_OLD/UltraSim/UI/*`

Additional v1 references to consult only when useful:

- `_OLD/_OLD/Client/ECS/ControlPanel`
  - old admin/debug control panel concepts:
    - per-system timings
    - per-system live settings/tuning
    - FPS/entity/archetype counters
    - admin-relevant runtime diagnostics
- `_OLD/_OLD`
  - old hybrid client render/culling concepts
  - useful conceptually, but the implementation was brittle and should not be ported directly

Primary old docs:

- `_OLD/Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`
- `_OLD/Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`
- `_OLD/Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`
- `_OLD/Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `_OLD/Docs/SERVER_DRIVEN_UI_IMPLEMENTATION_CHECKLIST_2026-03-18.md`
- `_OLD/Docs/ACCOUNT_CHARACTER_PERSISTENCE_DESIGN.md`
- `_OLD/Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE_V2.md`
- `_OLD/Docs/AUTHORITATIVE_PHYSICS_ARCHITECTURE.md`

Current planning docs:

- `Client/UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md`
  - comprehensive rebuilt UI/schema/admin-panel plan
  - folds in old v2 schema UI, old v1 ECS settings/control panel, ACC-style system pages, and current ECS `SystemSettings` / `ControlDescriptors`

## Decisions To Preserve

### One Persistent Shell, Inject Current Things

The client should keep one long-lived shell/root scene. Current content is injected into stable containers instead of replacing the whole app shape.

Preserve this structure:

```text
ClientRoot
- Controller
- ContentCatalogs
- World
  - Map
    - TerrainChunks
    - StaticChunks
    - MapEnvironment
  - DynamicEntities
  - Effects
  - CameraRig
- UILayers
  - World
  - HUD
  - Menus
  - Debug
```

The important rule is not the exact node names. The important rule is ownership:

- current map/world is injected under `World/Map`
- current avatar/mobile definitions are resolved through catalogs
- dynamic replicated actors are owned by `World/DynamicEntities`
- effects are not actor-owned by default
- camera follows an actor anchor but is not owned by the actor prefab
- UI layer categories persist for the life of the shell
- menus/HUD/debug/world overlays are dynamic children inside their persistent layers

### Keep Godot Thin

Godot owns:

- scene composition
- input collection
- camera feel
- actor rendering
- animation/audio/vfx presentation
- UI rendering and focus
- local settings persistence

Godot should not own:

- protocol logic
- packet codecs
- prediction/reconciliation/interpolation algorithms
- session admission/auth semantics
- server-driven UI state-machine semantics
- discovery registry service behavior

### Preserve Modularity For Future Content Systems

The client is expected to support three different extension classes:

- user-selected texture/resource packs
- server-specific streamed content patches
- explicit user-installed client mods

These are not the same thing and must not collapse into one generic "mod" bucket.

Texture/resource packs are local visual remaps. They can override textures, UI skinning, icons, and local presentation mappings through the content resolver. They should not execute code or change protocol behavior.

Server content patches are server-specific data/assets cached under that server's namespace. They can provide models, textures, materials, sounds, UI schemas, item visuals, map/static chunks, and visual remaps. They must not provide executable code or runtime injection.

Client mods live in an explicit `Mods` folder and may eventually use versioned client API hooks and runtime/script injection. They are user-installed, opt-in, and "use at your own risk." They are not silently installed by servers.

### Preserve Local App Shell vs Server-Driven UI Split

`ClientAppShell` is for client-local app flow:

- start menu
- server browser/direct connect
- settings
- in-session options/pause flow
- disconnect/error screen

Server-driven panels are not `ClientAppShell` screens. They are sibling windows under the menu layer, owned by `MenuCanvasManager`.

This split matters because login, character select/create/delete, admin panels, vendors, crafting, inventory, etc. should use the server-driven schema path, while local menus remain client-owned.

### Preserve UI Schema Cold/Hot Paths

Server-driven UI is RunUO-gump-like in concept: the server builds a UI/menu in code, sends an engine-neutral schema/model to the client, and the client renders it.

There are two client paths:

- cold path:
  - server opens a panel with type/revision/hash and payload
  - client does not have the schema cached
  - server sends or responds with the full schema definition
  - client validates the schema, renders it with the payload, and caches the schema in the server-specific appdata cache
- hot path:
  - server opens a panel with type/revision/hash and payload or delta
  - client has the exact matching schema cached
  - client renders the cached schema with only payload/delta data

Rules:

- schema definition and payload data stay separate
- exact identity matching only; do not accept a "close enough" schema
- cache identity includes server identity, panel type, revision, and schema hash/version
- server sends schema/data, never executable UI code
- schema validation is mandatory before render/cache
- cache quota/eviction hooks should exist even if initially simple

### Preserve Persistent UI Categories

Do not swap entire UI layers in and out. Keep:

- `WorldCanvasManager`
  - projected nameplates
  - overhead chat
  - future damage numbers, prompts, markers
- `HudCanvasManager`
  - connection/ping widget
  - player HUD
  - future hotbar/minimap/status bars
- `MenuCanvasManager`
  - local app shell screens
  - shared chat panel
  - server-driven panels
  - focus/z-order/modal rules
- `DebugCanvasManager`
  - diagnostics and overlays

### Preserve Current Actor / Current Map Concepts

The shell should know where current content plugs in, but should not hardcode one final game map or one final player prefab.

Current actor shape:

- `CharacterRoot`
  - `VisualRoot`
  - `CollisionRoot`
  - `Anchors`
    - `CameraPivot`
    - `NameplatePivot`
    - `EffectsRoot`
- `MobileDefinition`
  - visual scene
  - collision scene
  - visual offsets/scales
  - anchor offsets
- `MobileDefinitionCatalog`
  - logical actor definition id -> mobile definition

Current map shape:

- `MapRoot`
  - terrain chunk renderer
  - static chunk renderer
  - map environment/lighting

Map swaps should not clear replicated actor state unless the app/session state explicitly requires it.

### Preserve Animation, Sound, And VFX Hooks

The shell should keep semantic presentation hooks for:

- animation cues
- sound cues
- VFX cues
- actor definition changes
- material/texture remaps
- UI/icon/theme remaps

The first shell does not need final content for these, but the adapter shape should make them normal presentation commands, not one-off scene calls.

### Preserve Terrain/Static Streaming Hooks

Terrain and static content should support a UO/Minecraft-style hybrid:

- dynamic gameplay remains server-authoritative
- frozen/static chunks can be rendered client-side from cached content
- server can stream an updated chunk/static patch
- housing or map edits can produce updated static content
- clients hot-patch cached map/static content without treating it as replicated actors

This is a content/bulk/static pipeline, not the same as realtime actor replication.

### Remember Server Script Compiler Boundary

Server-side RunUO-style scripting belongs to the server startup/plugin system, not the client patch stream.

The old server startup flow had the intended shape:

- `Core: Initializing... - done`
- `Core: Starting... - done`
- `Scripts: Compiling C# scripts... - done (0 warnings, 0 errors)`
- `World: Loading...`
- discovery/listening startup

The client should expose stable protocol/UI/content/presentation hooks that server scripts can target. It should not accept executable scripts from streamed server content.

Future live content builders, such as an in-game item/prefab designer, should save validated data-authored prefab descriptors through server registries rather than generating C# or using script cache output. The client UI can provide the authoring surface, but scripts/plugins/core code still define new executable behavior and new component types.

Implementation gate: complete. The minimal server-driven UI path reached the
builder-authored live smoke, and the server-side C# script compiler/runtime setup
now exists before login, character select/create, inventory, crafting, vendors,
and admin menus expand further.

Canonical plan: [Server Script Compiler And Runtime Layout Plan](../Server/SCRIPT_COMPILER_AND_RUNTIME_LAYOUT_PLAN.md).

Target exported server/app shape:

- `scripts/Engines` for reusable server-side systems such as inventory rules, crafting, vendors, UI panel providers, and admin pages
- `scripts/Items`
- `scripts/Mobiles`
- `scripts/UI`
- `scripts/Commands`
- `scripts/Regions`
- `scripts/Customs`
- generated compiler artifacts under `cache/scripts`, not editable script source
- compile cache keyed by source hashes, referenced assemblies, compiler options, and API/protocol versions
- startup diagnostics that clearly report compile warnings/errors and cache hits/misses

This makes server-side modding fall out naturally while preserving the client security rule: server content patches can provide data/assets/UI schemas, but not executable client code.

## Target App Flow

Minimum platform loop:

1. Start client.
2. Show local app shell.
3. Browse official/manual server list.
4. Connect.
5. Server opens login schema panel.
6. Server opens character select/create schema panel.
7. Enter game.
8. Show local avatar, remote avatars, names, chat, HUD, and camera.
9. Disconnect/reconnect.
10. Same account/character state returns, including position once persistence is verified.

## Roadmap

### Phase G0 - Shell Scaffold

Goal: create the stable Godot shell skeleton.

Deliver:

- `Client/_Godot/UltraSim.Client.Godot.csproj`
- root scene, likely `Scenes/ClientRoot.tscn`
- `GodotClientRuntimeRoot`
- `UiManager`
- empty persistent `World/HUD/Menus/Debug` canvas managers
- `World/Map`, `World/DynamicEntities`, `World/Effects`, `World/CameraRig`
- preview mode that boots without a server

Done when:

- Godot project builds
- root scene opens/runs
- shell can enter preview state
- UI layers exist and can be toggled without replacing the root

Validation:

- project build succeeds
- root scene load smoke test/manual run succeeds
- non-Godot app-state helper logic has unit tests if any is introduced

### Phase G1 - Actor Presentation Spine

Goal: display and control a current avatar without involving networking yet.

Deliver:

- `CharacterRoot`
- `MobileDefinition`
- `MobileDefinitionCatalog`
- `ClientCharacterWorld`
- `LocalPlayerController`
- `OrbitFollowCameraRig`
- preview actor spawn/despawn
- camera binding to `CameraPivot`
- nameplate anchor present even if labels are not final

Done when:

- preview actor appears
- WASD/camera-relative movement works in preview
- camera follows the actor
- actor definition can be swapped by logical id

Validation:

- actor definition catalog resolution is unit-tested outside Godot where practical
- controller input mapping helpers are unit-tested if kept engine-neutral
- Godot preview run verifies actor spawn, movement, and camera follow

### Phase G2 - UI Layer Spine

Goal: restore the local app shell shape before adding server-driven schema.

Deliver:

- `ClientAppShell`
- `MenuScreenHost`
- start menu
- multiplayer/direct-connect menu
- settings menu
- session status screen
- in-session options/pause screen
- `HudCanvasManager` with minimal status/ping text
- `WorldCanvasManager` with v2-style nameplate/overhead-chat projection
- shared chat panel shell under menu layer
- persisted local presentation settings for nameplates, overhead chat, chat panel visibility, visible world-UI budget, and world-UI scale

Done when:

- app starts into menu
- preview mode can enter a visible world/HUD state
- Esc opens/closes in-session options without destroying the shell
- chat panel focus blocks movement input
- nameplates/overhead labels project from actor anchors, support reveal/debug actions, and keep overhead chat stacked/pruned by TTL

Validation:

- menu navigation state/back-stack logic is unit-tested if extracted
- world UI visibility/cap/prioritization policy is unit-tested outside Godot where practical
- Godot manual smoke verifies Esc, focus, HUD/menu/chat layering

### Phase G3 - Current Network Session Driver

Goal: connect the Godot shell to current rebuilt `Client/Network`, not old TCP runtime code.

Deliver:

- Godot-side session driver over current UDP/control-plane session path
- local input source -> `ClientMovementSessionHost`
- movement state receive -> `ClientCharacterWorld`
- observer state/event receive -> remote actor presentation
- chat submit -> `ClientMovementSessionHost.TrySendChat`
- chat events -> chat panel and overhead bubbles
- ping/status surface into HUD

Done when:

- Godot client can direct-connect to current console/headless server
- local avatar is owned and controlled
- remote actors appear and interpolate
- chat sends/receives
- ping/status updates in HUD
- no-server direct connect retries briefly, then shows a clear error
- graceful server stop is reported immediately by control-plane disconnect notice
- server crash/loss is detected by client keepalive timeout and clears session-owned world state
- silent UDP clients are eventually unbound and removed from the server movement shell
- local client disconnect sends a close notice so the server unbinds/despawns immediately
- disconnect clears session-owned world state without killing the shell

Validation:

- `Client/Network` bridge tests cover send/receive paths
- local integration test covers one client and loopback/UDP session where practical
- Godot manual smoke covers direct connect, move, observe, chat, disconnect

### Phase G4 - Discovery Contracts And Browser

Goal: restore server selection without rebuilding the whole registry/portal.

Deliver:

- lift shared discovery contracts from old `UltraSim/Discovery`
- current server status endpoint adapter in `Server/Discovery`
- official registry query client
- in-memory/manual providers
- provider-backed `ClientServerBrowserService`
- server browser menu backed by provider snapshots
- direct connect remains available when registry is down

Done when:

- manual/direct connect works
- official registry rows can be queried when service is available
- registry failure shows friendly UI, not raw socket errors
- selected server row resolves to a connect target

Validation:

- discovery row/query formatting tests
- direct-connect parse/validation tests
- provider snapshot/controller tests
- registry unavailable behavior test with fake query provider

Current status:

- engine-agnostic discovery contracts exist in `Discovery`
- `ClientServerBrowserService` supports provider-backed snapshots, refresh, provider selection, and friendly provider failure state
- Godot app shell has a provider-backed server browser surface
- official registry provider is wired in the Godot client shell, defaults to
  `http://127.0.0.1:5000`, and can be overridden with `--us-registry-url=...`
  or `ULTRASIM_REGISTRY_URL`
- manual/local-dev provider is wired and direct-connect remains available
- saved-server favorites/history provider adapters and JSON storage exist in
  `Client/Core`; Godot wires the provider shape, loads/saves
  `user://saved_servers.json`, and records ready direct-connect sessions into
  History
- selected server rows can be added/removed as Favorites, favorite state is
  overlaid onto official/manual/history rows, and the History tab can be cleared
- server browser filters are active for search, region, language, trust,
  password, and whitelist state
- server browser sort modes are active for name, users, ping, region, and last
  seen time
- LAN browser provider uses the shared UDP LAN discovery query
- direct-connect row resolution, official provider query construction, and
  provider failure behavior are covered by `UltraSim.Client.Tests`
- the v2 official registry service has been moved into `Services/Registry`
- the v2 official discovery DTOs/query/publisher contracts have been restored under `Discovery`
- server-side discovery publisher/status adapters under `Server/Discovery` are
  wired into console, headless, and Godot server wrappers
- optional embedded discovery status endpoint host is available for
  `server.discovery.status_endpoint_url` and is wired into console, headless,
  and Godot server wrappers
- `_Tests/UltraSim.Registry.Tests` covers the publisher -> registry-contract
  handler -> official client query loop, including startup visibility and
  unpublish-on-shutdown
- `_Tests/UltraSim.Registry.LaunchSmoke` starts the real registry service over
  HTTP, publishes a server row, queries it through the official client provider,
  resolves the row into a direct-connect endpoint, handshakes against the
  advertised non-default UDP port, and verifies unpublish-on-shutdown
- Godot server shell has a read-only Discovery tab for current discovery
  configuration, publisher state, last publish time, status endpoint state, and
  latest discovery message

Remaining deferred G4 work:

- LAN server-shell responder lifecycle wiring is deferred
- mDNS/zeroconf provider is explicitly deferred
- richer server row presentation can wait until the schema/menu work gives us better reusable UI primitives

### Phase G5 - Content Resolver And Cache Spine

Goal: add the minimal content/cache shape before schema caching and real content patching.

This phase intentionally comes before server-driven UI because UI schema caching depends on the same server identity, cache namespace, path safety, and hash validation rules as later streamed content.

Deliver:

- content resolver interfaces for logical ids
- resource-pack override order
- server-specific cache namespace/path policy
- patch manifest model
- schema cache path model
- asset hash/size validation primitives
- no executable content in server patch manifests
- explicit future `Mods` root shape kept separate from streamed content

Done when:

- texture/resource pack remap can be represented even if only one default pack exists
- server cache path can be resolved safely
- schema definitions can be associated with server identity
- path traversal/hash validation behavior is tested

Validation:

- cache path tests
- manifest validation tests
- resolver precedence tests
- server patch cannot contain executable/script entries test
- metadata/index round-trip tests
- cache eviction safety tests
- schema/manifest/asset cache store tests

Current status:

- first engine-agnostic `Client/Core/Content` slice exists:
  - server identity -> deterministic path-safe cache namespace
  - server asset cache path policy with traversal rejection
  - UI schema cache key/path model by server, panel type, revision, and SHA-256 hash
  - server patch manifest validation that rejects executable/script content
  - asset size/SHA-256 validation primitive
  - content resolver trust-class contracts
  - layered resolver stack with explicit priority ordering:
    - built-in defaults
    - user resource-pack overrides
    - server patch mappings
    - future explicit user mods
  - resource-pack manifest -> resolver layer conversion
  - cache metadata/quota planner that proposes oldest unpinned entries for eviction
  - persisted cache metadata index with validated JSON load/save
  - metadata eviction execution that removes planned candidates from the index
  - cache eviction executor that deletes files only under the cache root and then removes metadata
  - opaque UI schema byte cache store with SHA-256 validation and metadata updates
  - server patch manifest cache store with validation, exact patch identity checks, and metadata updates
  - server patch asset byte cache store with manifest size/SHA-256 validation and metadata updates
  - first Godot-side content/cache service adapter:
    - resolves default `user://UltraSimCache`
    - loads/recreates cache index metadata
    - exposes schema, server patch manifest, and server-asset cache stores
    - saves cache metadata on shell exit
- remaining G5 work:
  - G5 is complete enough for the pre-schema gate
  - stop here before schema codec/validation, typed schema cache entries, or server-driven menu work; those belong to G6/UI-1

### Phase G6 - Server-Driven UI Schema Path

Goal: bring back the schema/cache UI path once there is a shell to render it.

Design reference:

- `Client/UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md`

Deliver:

- shared UI contracts/schema/codec ported into current tree
- client UI runtime/cache negotiation
- Godot `UiSchemaRenderer`
- `MenuCanvasManager` server-driven panel host
- bundled `MessageBox`
- login panel packet binding
- character select/create/delete panels
- panel response/ack send path

Done when:

- server opens login panel
- invalid login updates challenge/retry path
- successful login closes panel
- server opens character select/create panel
- character selection enters game
- session loss clears active/pending server-driven panels

Validation:

- schema codec round-trip tests
- schema validation limit tests
- cache hit/miss tests
- panel ack/response tests
- login challenge packet-binding tests

### Phase G7 - Persistence/Reconnect Validation

Goal: prove the current rebuilt stack has the same user-facing persistence loop the old docs described.

Deliver:

- connect/login/character select/create through Godot client
- disconnect and reconnect same process
- server restart and reconnect
- verify account and character survive
- verify character position/yaw survive according to policy
- focused tests for:
  - disconnect during character select
  - quit to character select
  - autosave around disconnect

Done when:

- user can enter game, move, disconnect, reconnect, and return to the expected character state
- current persistence gaps are documented with tests or explicit defers

Validation:

- focused server/client integration tests for reconnect and character position policy
- manual Godot smoke for visible reconnect flow
- regression test for disconnect during character select

### Phase G8 - Presentation Polish Hooks

Goal: fill the shell hooks needed before authoritative physics and richer gameplay.

Deliver:

- animation command sink wired to model presenters
- SFX command sink
- VFX command sink
- connection-quality HUD widget
- debug overlay for entity id, ping, interpolation/correction state
- local client settings persistence for nameplates/chat/HUD/debug preferences

Done when:

- server/client presentation commands can be visibly tested in Godot
- physics work can validate camera/controller/correction feel in-client

Validation:

- semantic command dispatch tests outside Godot where practical
- Godot manual smoke for animation/SFX/VFX hook visibility

### Phase G9 - Local Single-Player Loopback And Pre-Physics Gate

Goal: prove the client can run the same server-authoritative game path in local single-player before major physics work starts.

Single-player should not become a separate gameplay architecture. It should launch or attach an embedded/local server, connect the normal client over loopback, and use the same login/session/world/persistence path as remote play with only the hosting/bootstrap policy changed.

Deliver:

- local embedded/headless server bootstrap option from the Godot shell or launcher
- loopback connect flow that reuses the normal network session driver
- local-world/server-profile selection hook
- clean shutdown/restart behavior for the embedded server
- save/cache namespace separation between local worlds and remote servers
- diagnostic UI that makes local-server state visible enough to debug startup failures

Done when:

- client can start a local server and connect to it over loopback
- local single-player reaches the same in-game shell as remote play
- local disconnect/reconnect uses the same persistence rules as remote play
- local server shutdown does not leave the client shell in a broken state
- physics can rely on a running authoritative server for world bounds and entity truth

Validation:

- server bootstrap option parsing tests
- cache/save namespace tests for local world identity
- loopback session integration test where practical
- manual Godot smoke for start local game, enter world, move, disconnect, restart

Do not start major authoritative physics work until:

- Godot client shell runs
- direct connect works
- local single-player loopback works enough to validate the same authoritative path
- discovery/menu path is usable enough
- login/character flow is usable enough
- avatar/world presentation exists
- chat panel and overhead chat are usable
- reconnect/persistence behavior has been validated or explicitly scoped

Physics can still have small prep work, but not the main implementation push.

## Explicit Defers

These are not first-pass blockers:

- final visual design for every menu
- full Knives-style chat tab customization
- LAN/mDNS discovery
- favorites/history persistence
- portal DB migration
- full inventory/vendor/crafting schemas
- full mod asset patch/signing pipeline
- terrain chunk streaming
- final character customization
- full audio/VFX content library

## Open Design Hooks To Preserve

Keep these seams available even if they are empty in the first pass:

- current map/dimension injection
- current environment/lighting injection
- current avatar/mobile definition injection
- content resolver/pack stack
- texture/resource pack remaps
- per-server content patch/cache namespace
- explicit opt-in client mod root
- server-driven UI cache by server identity
- UI schema cold/hot cache path
- animation/SFX/VFX semantic presentation commands
- static chunk/map patch rendering path
- projected world UI cap/prioritization
- shared vs exclusive menu/window policy
- command-input chat path
- local single-player as embedded server + loopback client
- debug layer overlays for future physics/chunk/AOI tools

## Current G6/UI Status

The pre-schema client shell gate is validated, and the server-driven UI path is now past the pure-kernel stage. The implemented shape currently includes:

- `Kernels/UI/UltraSim.UI.csproj`
- schema/catalog identity records
- surface/focus/open/back/tooltip policy enums
- capability flags and renderer capability record
- panel metadata
- action/context descriptor shape
- explicit `ActionId` binding on action-capable schema nodes such as buttons
- action availability, request, and result contracts:
  - client sequence echo
  - server-authored unavailable/stale/out-of-range state
  - machine-readable rejection reasons
- flat tooltip/rich-tooltip descriptors:
  - title/icon/quality role
  - body/stat/requirement/warning/error/flavor/advanced lines
  - payload/state-slot-backed dynamic values
  - optional detail action id
- runtime state binding descriptor and fallback shape
- early node id reservations
- schema node/tree descriptors
- schema document validation
- model payload slot descriptors
- model payload document validation against schema slots
- model update/delta descriptors:
  - full model update
  - slot patch
  - scoped page/tab/region patch
  - repeater row add/update/remove/replace
  - schema/catalog superseded update envelopes
- schema compatibility context for exact identity checks plus reusable hot-path payload compatibility
- simple JSON contract codec for round-trip tests
- `_Tests/UltraSim.UI.Tests`
- `_Benchmarks/UltraSim.UI.Benchmarks`

Current UI-1 validation:

- `dotnet build Kernels/UI/UltraSim.UI.csproj -c Release`: passed
- `dotnet run --project _Tests/UltraSim.UI.Tests/UltraSim.UI.Tests.csproj -c Release`: passed
- `dotnet run --project _Benchmarks/UltraSim.UI.Benchmarks/UltraSim.UI.Benchmarks.csproj -c Release`: passed

First UI benchmark report:

- `_Benchmarks/results/ui-benchmarks.json`
- current schema validation baseline includes `SchemaDocument_Validate_1000Nodes`
- current model/schema compatibility baseline includes `ModelDocument_ValidateAgainstSchema_1000Slots`
- current update validation baseline includes `ModelUpdate_ValidateSlotPatch_1000Slots`
- current hot-path update baseline includes `ModelUpdate_ValidateSlotPatchAgainstContext_1000Slots`
- current action validation baselines include `ActionRequest_ValidateAgainstContext_10000` and `ActionResult_ValidateAgainstContext_10000`
- current tooltip validation baseline includes `TooltipDescriptor_ValidateAgainstContext_10000`

Current client/Godot bridge status:

- `Client/UI/ClientUiPanelRegistry` opens panels through the in-memory definition cache, retains validated schema and compatibility context per instance, validates action requests, and applies full/patch/repeater model updates with sequence tracking.
- `Client/_Godot/Scripts/UI/ServerDrivenPanelRenderer.cs` now renders the normal menu/settings vocabulary: `Panel`, `Row`, `Column`, `Group`, `Section`, `ScrollRegion`, `Tabs`, `Pager`, static `Text`, model-backed `DynamicText`, `Image`, `Button`, `IconButton`, `Separator`, `Spacer`, `TextInput`, trusted password-style `TextInput`, `NumberInput`, `Toggle`, `Slider`, `Stepper`, `Select`, `RadioGroup`, `CheckboxGroup`, `Repeater`, and `Table`; unsupported node kinds degrade to inert placeholders.
- `MenuCanvasManager` can mount, replace, close, and clear server-driven panels on separate menu/modal/exclusive lanes.
- `WorldCanvasManager` now mirrors the old v2 presentation behavior closely enough for the current shell: a persistent world-UI root, distance-prioritized labels, two-line overhead chat with TTL pruning, nameplate reveal/debug actions, and configurable text templates.
- Local presentation settings now persist under `user://client_settings.cfg` for overhead chat, nameplates, chat panel visibility, visible world-UI budget, and world-UI scale.
- Godot buttons bound with `ActionId` produce validated client action route results through `ClientUiPanelRegistry.ValidateActionRequest`.
- The local Godot smoke flag `--us-smoke-server-ui-message-box` cold-opens a server-driven message-box schema, mounts it into the menu lane, verifies model-backed dynamic text, and presses the rendered OK button through validated action routing.
- The live loopback path now proves server UI open, client receipt/render, client action, and server receipt over the real reliable event path.

Next G6/UI steps:

1. Continue admission panel polish for character select/create/delete on top of the server-authored package.
2. Add richer tooltip presentation after the login/character path is human-usable.
3. Keep discovery/portal integration and official registry adapter separate from local/manual discovery polish.

The only meaningful pre-G6 caveat is the official registry adapter. The manual/provider-backed discovery path is in place and tested; the official query adapter can be added when the portal/registry endpoint exists and should not block UI/schema kernel work.
