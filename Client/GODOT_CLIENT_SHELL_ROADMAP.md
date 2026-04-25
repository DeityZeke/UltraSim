# Godot Client Shell Roadmap

Purpose: rebuild the Godot client shell without losing the modular shape of the old client.

This is the active checklist for the rebuilt `Client/_Godot` shell. The old client remains the behavioral and scene-composition reference, but the current implementation should target the rebuilt `Client/Core`, `Client/Network`, `Kernels/Network`, `Server/Network`, and `Server/Core` boundaries.

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

The current Godot client shell is essentially empty:

- `Client/_Godot/project.godot`
- icon/import/editor metadata
- no main scene
- no runtime root
- no UI layers
- no actor presenter
- no shell app/menu flow

That makes `Client/_Godot` the next platform bottleneck. Discovery UI, UI schema rendering, avatar presentation, chat UX, camera feel, map/environment swapping, and persistence/reconnect validation all need a real client shell.

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

Primary old docs:

- `_OLD/Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`
- `_OLD/Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`
- `_OLD/Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`
- `_OLD/Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `_OLD/Docs/SERVER_DRIVEN_UI_IMPLEMENTATION_CHECKLIST_2026-03-18.md`
- `_OLD/Docs/ACCOUNT_CHARACTER_PERSISTENCE_DESIGN.md`
- `_OLD/Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE_V2.md`
- `_OLD/Docs/AUTHORITATIVE_PHYSICS_ARCHITECTURE.md`

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

### Preserve Local App Shell vs Server-Driven UI Split

`ClientAppShell` is for client-local app flow:

- start menu
- server browser/direct connect
- settings
- in-session options/pause flow
- disconnect/error screen

Server-driven panels are not `ClientAppShell` screens. They are sibling windows under the menu layer, owned by `MenuCanvasManager`.

This split matters because login, character select/create/delete, admin panels, vendors, crafting, inventory, etc. should use the server-driven schema path, while local menus remain client-owned.

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
- `WorldCanvasManager` with nameplate/overhead-chat projection
- shared chat panel shell under menu layer

Done when:

- app starts into menu
- preview mode can enter a visible world/HUD state
- Esc opens/closes in-session options without destroying the shell
- chat panel focus blocks movement input
- nameplates/overhead labels project from actor anchors

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
- disconnect clears session-owned world state without killing the shell

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

### Phase G5 - Server-Driven UI Schema Path

Goal: bring back the schema/cache UI path once there is a shell to render it.

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

### Phase G6 - Persistence/Reconnect Validation

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

### Phase G7 - Presentation Polish Hooks

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

### Phase G8 - Pre-Physics Gate

Do not start major authoritative physics work until:

- Godot client shell runs
- direct connect works
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
- mod asset patch/signing pipeline
- terrain chunk streaming
- final character customization
- full audio/VFX content library

## Open Design Hooks To Preserve

Keep these seams available even if they are empty in the first pass:

- current map/dimension injection
- current environment/lighting injection
- current avatar/mobile definition injection
- content resolver/pack stack
- server-driven UI cache by server identity
- projected world UI cap/prioritization
- shared vs exclusive menu/window policy
- command-input chat path
- local single-player as embedded server + loopback client
- debug layer overlays for future physics/chunk/AOI tools

## Next Implementation Slice

Start with Phase G0 + G1 together:

1. Add the Godot client C# project.
2. Add root scene and runtime root.
3. Add world/map/dynamic/effects/camera/UI layer containers.
4. Add preview actor presentation with `CharacterRoot`, `MobileDefinition`, local controller, and orbit camera.
5. Validate the shell runs before any network or schema work.

This creates the surface we need for every following slice.
