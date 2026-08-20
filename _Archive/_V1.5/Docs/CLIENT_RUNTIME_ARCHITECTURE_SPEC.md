# Client Runtime Architecture Spec (Working Draft)

Purpose: define how the client should function (core vs engine host vs presentation/UI), so we can implement the minimum modular client seam now, then proceed with server chunking/AoI/fanout work without client-side design drift.

Status: active architecture/spec document. Large parts of the `Client.Godot` scaffold are now implemented and this document should be treated as the current handoff/reference for the next client-runtime passes.

---

## Current Handoff State (2026-03)

The networking/client phase now has:

- a stable set of durable client-network pieces that should be carried forward
- a working `Client.Godot` host/session/driver scaffold
- a smaller remaining set of `_tmp` runtime glue that should still be treated as transitional

### Durable now

These are good candidates to remain the long-term client-network core:

- `UltraSim/Network/Client/ClientReconciliationQueue.cs`
- `UltraSim/Network/Client/ClientPredictionStore.cs`
- `UltraSim/Network/Client/ClientInterpolationBuffer.cs`
- `UltraSim/Network/Client/ClientInputSequencer.cs`
- `UltraSim/Network/Client/ClientCorrectionStats.cs`
- `UltraSim/Client/Engines/Networking/ClientNetworkBridge.cs`
- `UltraSim/Client/Engines/Networking/ClientInterpolationBridge.cs`
- snapshot types in `UltraSim/Client/Engines/Networking/*Snapshot.cs`

These pieces have already been load-tested, instrumented, and cleaned up enough that they should be treated as the core client runtime, not as throwaway code.

### Transitional now

These should be treated as bridge/host glue and expected to shrink or be replaced during the client skeleton phase:

- `_tmp/ECSNetworkIslandSliceNode.cs`
- `_tmp/ECSNetworkLoopbackNode.cs`
- exported scene/runtime boot flow under `_tmp`

The client skeleton should absorb the ownership of:

- application state and boot flow
- presentation node lifecycle
- map/world root ownership
- UI/HUD/menu wiring
- engine-host integration

### Implemented client scaffold now

The following client-side scaffold is now real and should be treated as the current baseline rather than as speculative design:

- `main.tscn` + `MainSceneRoot.cs`
  - export-feature bootstrap for:
    - `client_lite`
    - `client_full`
    - `server_build`
- `Client.Godot/GodotClientRuntimeRoot.cs`
  - host/orchestration root
  - owns explicit app/session state:
    - `Boot`
    - `Preview`
    - `Connecting`
    - `InGame`
    - `Disconnected`
    - `Error`
  - owns `StartSession()` / `StopSession()` / `RestartSession()` lifecycle hooks
- `Client.Godot/GodotClientSession.cs`
  - Godot-side runtime service owner
- `Client.Godot/IClientSessionDriver.cs`
  - session mode seam
- `Client.Godot/PreviewClientSessionDriver.cs`
  - preview-mode driver
- `Client.Godot/NetworkClientSessionDriver.cs`
  - network-mode driver
  - exposes driver connection state separately from presentation text
- `Client.Godot/GodotClientNetworkRuntime.cs`
  - current network gameplay/runtime composition for:
    - inbound corrections/chat/ping
    - outbound input/chat/ping
    - local prediction
    - local correction smoothing
    - remote interpolation
- `Client.Godot/World/ClientCharacterWorld.cs`
  - spawned actor registry and render-target smoothing layer
  - now owns the first mobile-definition resolution seam:
    - default fallback definition
    - logical definition selection by id
- `UltraSim/Client/Runtime/ClientActorDefinitionId`
  - shared/runtime logical actor-definition id used to decouple gameplay-side actor identity from client asset scenes
- `UltraSim/Shared/Gameplay/SharedActorDefinitionIds.cs`
  - tiny shared authoritative actor-definition ids for temporary bridge/runtime usage
- `Client.Godot/Controllers/LocalPlayerController.cs`
  - local target/controller seam
- `Client.Godot/_Prefabs/Camera/OrbitFollowCameraRig.cs`
  - camera ownership separated from the actor root
- `Client.Godot/_Prefabs/Mobiles/CharacterRoot.cs`
  - actor composition root
- `Client.Godot/_Prefabs/Mobiles/MobileDefinitionCatalog.cs`
  - client-side logical id -> `MobileDefinition` resolver resource
- `Client.Godot/_Prefabs/Mobiles/MobileCatalogEntry.cs`
  - editor-facing entry resource for stable shared actor-definition ids
- `Client.Godot/_Prefabs/Mobiles/MobileCatalogNode.cs`
  - scene-tree wrapper node so the catalog is visible/editable as a normal runtime/editor object
- `Client.Godot/_Prefabs/Mobiles/CharacterModelPresenter.cs`
  - model/presentation seam
- `Client.Godot/UI/UiManager.cs`
  - top-level UI router for persistent canvas categories
- `Client.Godot/UI/WorldCanvasManager.cs`
- `Client.Godot/UI/HudCanvasManager.cs`
- `Client.Godot/UI/MenuCanvasManager.cs`
- `Client.Godot/UI/DebugCanvasManager.cs`
  - category-specific UI ownership under `UILayers`
- `Client.Godot/UI/MenuScreenHost.cs`
  - menu screen indexing/activation under the menu canvas

### Current movement state

The active movement/runtime path has now been cut over:

- runtime movement state is float-based
- active wire movement uses quantized centimeter `X/Z` plus explicit yaw
- local prediction and temporary server movement now both use held-input movement rather than the original packet-impulse `EntityInt2` path
- local movement quality is materially improved and now good enough for the current client scaffold phase

Remaining movement-specific deferred work:

- add `Y` to the wire when gravity/elevation become active
- move world/network addressing to `chunkCoord + localOffset` when terrain/chunk streaming work becomes active
- keep the current Float movement path as the only active path; the legacy `EntityInt2` movement-only code has been removed

Movement protocol migration is no longer the main blocker. The next work should move back to broader client/runtime completion and validation.

### Current actor-definition state

- the client presentation/runtime path now supports `SetActorDefinition(...)`
- `ClientCharacterWorld` resolves logical actor definition ids through `MobileDefinitionCatalog`
- the temporary authoritative bridge now assigns `SharedActorDefinitionIds.Player` to spawned player entities
- active Float correction packets now carry the shared actor-definition id
- the client scene now resolves those ids through a catalog node + catalog-entry resources rather than hardcoded string mapping

### Practical rule for the next phase

- move durable client-core pieces forward as-is unless a real architecture issue forces change
- do not promote `_tmp` bridge/runtime code wholesale into the final client structure
- when in doubt, keep logic in `UltraSim/Network` or `UltraSim/Client`, and keep Godot-facing orchestration in the future client host/presentation layers

---

## 1) Goals

### 1.1 Immediate Goal

Create a clear client architecture boundary so the current Godot client does not accidentally absorb network/core logic that should remain engine-agnostic.

### 1.2 Long-Term Goal

Make it possible to build another client in a different engine without rewriting the majority of client networking/state logic.

This does **not** mean "no rewrite."
It means the rewrite is mostly:
- engine host/bootstrap
- input bridge
- presentation bridge (animation/audio/vfx/render/UI)

and **not**:
- protocol/runtime networking
- prediction/reconciliation/interpolation
- client state replication application

---

## 2) Core Client Principle

- **Client core decides what happened.**
- **Engine adapter decides how it looks/sounds/feels in that engine.**

Example:
- Core emits: `PlayAnimation(entityId, "Attack_1", speed=1.0)`
- Godot adapter decides how to map that to AnimationTree/AnimationPlayer/resources

---

## 3) Client Layer Split (Required Boundary)

## 3.1 Engine-Agnostic Client Core

Responsibilities:
- network packet decode / protocol handling
- input sequencing and send scheduling
- client prediction
- reconciliation
- interpolation of remote entities
- replicated state application into projected client state/world
- semantic output commands/events for presentation and UI

Must not depend on:
- `Godot.*`
- scene tree nodes
- engine resource paths
- engine timing APIs directly

## 3.2 Engine-Specific Host (Godot Client Host)

Responsibilities:
- app lifecycle
- tick/frame pump integration
- startup/CLI/config integration
- mode switching (menu/in-game/connecting)
- wiring core to input/presentation/logging/platform services

Examples:
- `Game.tscn` root controller
- boot/launcher menu state handling
- local server bootstrap for singleplayer (if done in same process)

## 3.3 Engine-Specific Input Bridge

Responsibilities:
- capture engine input
- map engine input -> engine-neutral client input commands
- enforce focus/chat/menu input capture rules

Examples:
- movement keys
- chat mode toggle (block movement while typing)
- UI clicks translated to semantic UI actions

## 3.4 Engine-Specific Presentation Bridge

Responsibilities:
- actor visuals (spawn/update/despawn)
- animation playback
- particles/VFX
- SFX/audio emit
- world-space labels / nameplates / chat bubbles
- UI panel instantiation/update/close

This layer owns scene nodes and engine assets.

## 3.5 App Flow / UI Layer

Responsibilities:
- launcher/start menu flow
- singleplayer world select/create
- multiplayer browser/favorites/history/direct connect
- settings menu
- HUD/debug overlays

This can be implemented in Godot UI/gumps, but should consume semantic commands/data rather than reaching into core internals.

## 3.6 Content Resolver / Asset Pack Stack

Responsibilities:
- resolve logical IDs (model/anim/vfx/sfx/ui skin) -> actual engine assets
- apply pack override priority (last enabled wins)
- warn on overlapping overrides
- fallback to default assets when override assets are missing
- consult server-scoped cache content (UI prefabs/schemas, optional visuals) under explicit trust/policy rules

This is the main seam that enables visual override packs and future alternate engine clients.

---

## 4) Godot Scene Tree Layout (Recommended)

This layout keeps world/static content, dynamic entities, and UI layers cleanly separated.

```text
Game.tscn (root)
- ClientHost / GameController (Node)
- WorldRoot (Node3D)
  - MapRoot (Node3D)
    - TerrainChunksRoot (Node3D)
    - StaticChunksRoot (Node3D)
    - MapEnvironmentRoot (Node3D)
  - DynamicEntitiesRoot (Node3D)
  - EffectsRoot (Node3D)
  - CameraRigRoot (Node3D)
- UILayers (Node)
  - WorldOverlayLayer (CanvasLayer)
  - MenuLayer (CanvasLayer)
  - HudLayer (CanvasLayer)
  - DebugLayer (CanvasLayer)
```

### 4.1 Why This Split Matters

- `MapRoot` can be swapped per map/dimension without entangling dynamic actor lifecycle
- `DynamicEntitiesRoot` stays focused on replicated/runtime actors
- `WorldOverlayLayer` supports projected nameplates/chat independently from menu HUD
- UI windows/gumps live in one place with consistent focus/z-order rules

### 4.2 Persistent Canvas Manager Model

`UILayers` is the long-lived UI root. The canvas categories under it are structural and stay present for the life of the scene:

- `WorldOverlayLayer`
- `HudLayer`
- `MenuLayer`
- `DebugLayer`

The client should not treat those canvas categories as disposable screens. Instead:

- `UiManager` routes UI requests by category
- each `*CanvasManager` owns dynamic child UI nodes for that category
- `WorldCanvasManager` owns projected world UI such as overhead chat, nameplates, and future markers/prompts
- category canvases remain layered and persistent
- individual UI elements are added/removed/reordered within their owning category

Practical rule:

- do **not** swap the whole `HudLayer` or `MenuLayer` in/out as the primary UI mechanism
- do dynamically instance/free HUD roots, menu shells, debug panels, and future server-driven windows inside those persistent layers

This keeps category layering stable:

- menus always appear above HUD
- HUD always appears above world overlays
- debug can remain top-most when enabled

World-UI policy:

- overhead chat and future nameplates are not actor-owned `Label3D` widgets
- actors expose anchors such as `NameplatePivot`
- `WorldCanvasManager` tracks projected world UI entries, applies client visibility preferences, and enforces caps such as `MaxVisibleWorldUi`
- client-facing world UI tuning such as `ShowOverheadChat`, `ShowNameplates`, `MaxVisibleWorldUi`, and `WorldUiScale` should be owned by `WorldCanvasManager`, surfaced through the settings flow, and persisted in local client settings
- authoritative character-name replication now exists for the current account/character flow via a lightweight reliable server nameplate update
- correction fallback text still exists for entities that have no resolved display name yet
- when persistent nameplates are disabled, `WorldCanvasManager` may still reveal them temporarily via the `Show_Nameplates` input action (with the old key export as fallback)
- debug entity ids should remain a separate reveal path controlled by `Show_Debug`
- overhead chat should support a short per-entity history stack with independent TTL per line rather than a single shared timer
- cleanup/pruning of transient world UI belongs to `WorldCanvasManager`, not individual actor prefabs

---

## 5) App State Model (Launcher + Game Flow)

Use a client app state machine, even if implemented within the same `Game.tscn`.

Recommended states:
- `Boot`
- `MainMenu`
- `Settings`
- `SinglePlayerWorldSelect`
- `MultiplayerServerBrowser`
- `Connecting`
- `InGame`
- `Disconnect/Error`

### 5.1 Main Menu / Launcher

A menu background map/scene is acceptable (and often desirable), as long as:
- it is treated as a normal app state (`MainMenu`)
- menu windows are non-closable as appropriate for that state
- world gameplay logic is not accidentally running as if the user is in-game

### 5.2 Singleplayer Flow (Recommended)

1. User selects/creates world save
2. Client host starts local/internal server using that save
3. Client connects via loopback
4. Game enters `InGame`

Design rule:
- Singleplayer should use the same network contract/path as multiplayer (no special offline simulation path).

### 5.3 Multiplayer Flow (Recommended)

Support:
- Official server list (pinned)
- Community server list
- Favorites tab
- History tab
- Direct connect by IP/endpoint

Design rule:
- Direct connect must not depend on server-list service availability.

---

## 6) UI/Gump Layering Model

## 6.1 Layer Responsibilities

- `WorldOverlayLayer`
  - nameplates
  - floating chat
  - damage numbers
  - interaction prompts (screen-projected)

- `MenuLayer`
  - gumps/windows/dialogs
  - inventory/vendor/crafting/journal/etc.
  - modal and focus/z-order management

- `HudLayer`
  - persistent player HUD
  - hotbar/status bars/target frame/minimap

- `DebugLayer`
  - developer diagnostics
  - metrics graphs
  - chunk/AoI/debug overlays

## 6.2 Focus/Z-Order Rules

- Active/focused menu window moves to top (highest z within `MenuLayer`)
- Modal windows block input to non-modal windows behind them
- Chat text input mode blocks gameplay movement/actions except explicit cancel/submit controls

### 6.2a Menu Presentation Policy

Menu UI should not be treated as one flat class of window. The menu layer should support at least two behaviors:

- **Shared** menus
  - may coexist with other shared menus
  - examples:
    - inventory
    - skills
    - crafting
    - chat window/panel
  - active click/focus brings the selected window to the top within `MenuLayer`

- **Exclusive** menus
  - own the active menu focus/state
  - examples:
    - main menu
    - pause menu
    - settings
    - connection/disconnect status
  - these should participate in a navigation stack/back-stack

Recommended practical model:

- `MenuCanvasManager` owns:
  - menu instance lifecycle
  - menu focus/z-order
  - shared vs exclusive policy
  - back-stack/history for exclusive flows only
- `UiManager` owns top-level category routing, not per-window policy details

Current intended behavior:

- chat is treated as a shared menu element under `MenuLayer`, not as HUD
- runtime/network status remains a HUD element under `HudLayer`
- the in-session local menu should be treated semantically as an **options menu**, even if the current scene/script names still use `Pause`
- in true multiplayer/online it does not pause the server; it only opens the local in-session options flow
- in future singleplayer/loopback it may additionally pause the local server if that mode explicitly supports it
- the current local in-session options flow is treated as an `ExclusiveMenuOnly` menu:
  - HUD remains visible behind it
  - shared menus such as chat are suppressed while pause is open

## 6.3 Network Quality Indicator (Future HUD Spec)

Add a connection-quality widget in `HudLayer` using a 5-bar signal icon (plus disconnected state).

Design rules:
- derive tier from **smoothed RTT** (not raw single-sample ping)
- use a small hysteresis window to avoid bar flicker
- keep mapping engine-agnostic in client core/runtime (`ConnectionQualityTier`), with engine adapter handling visuals

Tier mapping (ms):
- `<= 30`: 5 bars, gold, optional sparkle effect ("best")
- `31-45`: 5 bars green
- `46-60`: 4 bars green, 1 grey
- `61-75`: 3 bars green, 2 grey
- `76-90`: 2 bars green, 3 grey
- `91-105`: 1 bar green, 4 grey
- `> 105`: 0 bars green (all 5 grey)
- disconnected/timeout: red `X`

---

## 7) Server-Driven UI / Menu Commands

Avoid hardcoded scene-tree calls from gameplay/network code like "instantiate scene X directly."

Use semantic UI commands instead:
- `OpenUiPanel(panelTypeId, panelInstanceId, payload)`
- `UpdateUiPanel(panelInstanceId, payload)`
- `CloseUiPanel(panelInstanceId)`

For the concrete subsystem/package split and phased implementation order for this work, see:

- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `Docs/UI_SCHEMA_AUTHORING_LANGUAGE_SPEC.md`

For the Godot host, those semantic commands should route through:

- `UiManager` -> category selection
- relevant `*CanvasManager` -> lifecycle/focus policy

This is the intended insertion point for the later server-authoritative menu/schema path.

### 7.1a Local vs Server-Driven Menu Ownership

- `ClientAppShell` remains client-local app/menu flow only
- server-driven menu/panel instances must be siblings in the menu layer, owned by `MenuCanvasManager`
- server-driven panels should not be registered as `ClientAppShell` screens

### 7.1b Shared Runtime UI Command Shape (Locked)

For the first server-driven menu implementation, the shared/runtime UI contract should standardize:

- `UiPanelCategory`
- `UiPanelPresentationMode`
- `UiPanelTypeId` as a stable `ushort`
- `UiPanelInstanceId`
- `DefinitionRevision`
- `UiPanelFlags`
- `UiPanelCloseReason`

The presentation-mode enum should be canonical in shared/runtime code. Client-local menu presentation enums should not remain as a parallel source of truth.

All server-driven panels should use one schema-based system.

The client should cache panel definitions by:

- `UiPanelTypeId`
- `DefinitionRevision`

Client-shipped/common panels are simply warm cache entries in that same system.

### 7.1c Menu Stack Ownership (Locked)

`MenuCanvasManager` should own separate menu-history/state for:

- local app-shell exclusive screens
- server-driven exclusive panel instances
- shared panel registry/z-order

Do not force server-driven panel instances into the local `MenuScreenHost` / `StringName` screen stack.

### 7.1d Escape/Input Priority (Locked)

When `Esc` is pressed:

1. focused text input gets first chance to consume it if applicable
2. top-most server-driven exclusive panel gets first close/cancel opportunity
3. local in-session options/app-shell flow comes next
4. only after that should the local in-session options menu be opened as a fallback

### 7.1 UI Panel Registry Pattern

Client-side `UiPanelRegistry` maps `panelTypeId` -> engine-specific panel implementation/scene.

Benefits:
- multiple windows of same type
- easier future client ports
- cleaner server/client protocol contracts

---

## 8) Semantic Presentation Command Model (Engine-Agnostic)

The client core should emit semantic presentation commands/events, not engine calls.

Examples:
- `SetActorPose(entityId, pose)`
- `PlayAnimation(entityId, animId, speed, blend)`
- `SpawnVfx(target, vfxId, params)`
- `PlaySfx(target, sfxId, params)`
- `ShowChatBubble(entityId, text, duration)`
- `UpdateNameplate(entityId, data)`
- `OpenUiPanel(...)`
- `CloseUiPanel(...)`

### 8.1 Design Rule

Network/gameplay/client-state code must not reference raw Godot resource paths.
Use logical IDs and let the content resolver + presentation bridge map to engine assets.

---

## 9) Content Pack / Visual Override System (Client-Side)

This section captures the "Minecraft-style package handler" requirement.

## 9.1 Goals

- allow visual customization
- allow custom servers/mod packs to add/override visuals later
- support pack stacking with conflict warnings
- keep defaults intact as fallback

## 9.2 Pack Layering Model (Recommended)

Load order (highest priority last):
1. base/default pack
2. optional user packs (ordered)
3. optional server-scoped cached/streamed visual content (future, if enabled)
4. optional server-suggested visual pack(s) (future, if enabled)

Conflict behavior:
- overlapping asset override is allowed
- UI warns on overlaps/conflicts
- **last selected pack wins**

### 9.2a Server-Scoped Cache Namespace (Required)

The client should maintain per-server cached data for streamed presentation content.

Examples:
- UI panel definitions/schemas (cached for future fast opens)
- icons/theme assets (if allowed later)
- server-provided visual overrides/patches (future)

Design rule:
- Cache is namespaced by **server identity** (stable server ID + fingerprint/public-key identity), not only by `IP:port`.

### 9.2b Menu Definition Cache Behavior (Server-Driven UI)

When server opens a UI panel/menu:

1. Server sends `OpenPanel(typeId, revision, payload, ...)`
2. Client checks server-scoped cache for `(typeId, revision)`
3. If present, client renders immediately and applies payload
4. If missing, client acknowledges `need_definition`
5. Server sends `PanelDefinition(typeId, revision, schema)`
6. Client validates, caches, and renders
7. Client keeps that definition cached under the current server namespace for future opens

This avoids re-sending full menu definitions every time while preserving custom server UI flexibility.

## 9.3 Scope Guardrail (Important)

Start with **visual-only packs**:
- models
- textures/materials
- animations (presentation mappings)
- VFX
- SFX
- UI skins/themes

Do **not** mix in authoritative gameplay logic/content here initially.

## 9.4 Asset Resolution Model

Core/gameplay/network references logical IDs:
- `ModelId`
- `AnimId`
- `VfxId`
- `SfxId`
- `UiPanelTypeId`

Content resolver maps those IDs to concrete engine assets using the active pack stack.

Fallback rule:
- if override asset is missing/broken, use default pack asset and log a warning

### 9.5 Safety / Validation Constraints (Server-Provided Presentation Content)

Server-provided presentation content (especially dynamic UI schemas) must be validated before use.

Examples:
- max node count / tree depth
- max text length
- allowed node/control types
- payload size limits
- disk cache quotas (per-server + global)

Design rule:
- Client executes **no server-provided code/scripts**.
- Server may provide data/schema/assets; client runtime behavior stays client-implemented.

---

## 10) Map / Dimension Swapping (Client)

Use a dedicated `MapRoot` container so dimensions/maps can be hot-swapped cleanly.

Guidelines:
- `MapRoot` owns current map scene/chunk render controller
- `DynamicEntitiesRoot` remains independent of map static content
- environment/lighting settings are loaded with map and applied via presentation/host layer

This keeps map swaps from entangling actor replication and scene cleanup.

---

## 11) Client Runtime Execution Flow (Conceptual)

Per frame/tick (conceptual ordering):

1. Engine input captured by input bridge
2. Input bridge emits engine-neutral input commands to client core
3. Client core:
   - processes inbound packets
   - updates prediction/reconciliation/interpolation
   - applies replicated state to client projected state
   - queues semantic presentation/UI commands
4. Presentation/UI bridges consume commands and update engine scene/UI/audio/vfx
5. Host applies app-state transitions (if any) and final per-frame bookkeeping

---

## 12) Minimal Client Work Before Chunking (Do Now, Keep Scope Tight)

Goal: establish the seam, not a full client rewrite.

Minimum deliverables:
- boundary rules (core vs host vs bridges)
- initial interface contracts (spec-level or thin implementations)
- semantic presentation/UI command shape
- Godot scene-tree and UI layer conventions
- content resolver / pack stack design rules (spec-level)

Recommended minimal interfaces:
- `IClientClock`
- `IClientInputSource`
- `IPresentationSink`
- `IUiCommandSink` (or included in `IPresentationSink`)
- `IClientLogger`
- `IContentResolver` (logical asset ID -> engine asset)

### 12.1 Existing Runtime Pieces the Skeleton Should Reuse

The client skeleton should be built around the runtime pieces that already exist and have been exercised under load, not around new replacement abstractions.

Primary pieces to reuse:

- `UltraSim/Client/Engines/Networking/ClientNetworkBridge.cs`
- `UltraSim/Client/Engines/Networking/ClientInterpolationBridge.cs`
- `UltraSim/Client/Engines/Networking/ClientNetworkTickResult.cs`
- `UltraSim/Client/Engines/Networking/*Snapshot.cs`
- `UltraSim/Network/Client/ClientReconciliationQueue.cs`
- `UltraSim/Network/Client/ClientPredictionStore.cs`
- `UltraSim/Network/Client/ClientInterpolationBuffer.cs`
- `UltraSim/Network/Client/ClientInputSequencer.cs`
- `UltraSim/Network/Client/ClientCorrectionStats.cs`
- `UltraSim/Client/Runtime/ClientPresentationCommandBuffer.cs`
- `UltraSim/Client/Runtime/ClientPresentationCommandDispatcher.cs`

Practical implication:

- the future client host should wrap and compose these pieces
- it should not reimplement prediction, reconciliation, interpolation, correction queueing, or client bridge stats flow

### 12.2 Skeleton Ownership Rules

The future client skeleton should own:

- app lifecycle / boot
- app-state transitions
- scene-tree ownership
- world/map/presentation root ownership
- binding input source -> client bridge
- binding presentation/UI sinks -> engine views/widgets
- platform/config/logging/service wiring

The future client skeleton should **not** absorb:

- prediction
- reconciliation
- interpolation
- correction queueing/coalescing
- network runtime bookkeeping
- client bridge/runtime metrics

Those remain part of the durable client-network core.

What to defer until after chunking Phase A:
- full animation event pipeline design
- comprehensive VFX/SFX systems
- deep UI panel framework rewrite
- server browser service implementation details
- cross-engine packaging/distribution concerns

---

## 13) Priority Order (Current Recommended Sequence)

Execution update (2026-03-17):
- network optimization/hardening work is no longer the active focus
- the client skeleton/foundation is now materially implemented
- the float-runtime / quantized-wire movement cut is now active in the working client/server path
- terrain/statics integration remains intentionally deferred until that movement/runtime cut is validated further in broader multiplayer use

Recommended sequence:
1. **Validate the new Float movement path in broader live multiplayer use**
2. **Tighten app-state/session-state flow**
3. **Entity-definition/view resolution beyond the single mannequin default**
4. **Server spatial chunking + AOI interest filtering**
5. **Shared delta fanout optimization inside chunk/interest groups**
6. **Client render chunk sliding window + chunk-aware streaming**
7. **Terrain/statics chunk integration**
8. Expand presentation/audio/animation/content-pack systems

Rationale:
- the movement/runtime protocol cut is now in place, so the next job is validating it and then layering terrain/chunk rules on top of that better motion model
- keep the current client scaffold stable while pruning the remaining temporary movement assumptions

---

## 14) Guardrails (Quick Reference)

- No `Godot.*` types in client core/network/prediction/replication-apply paths
- No raw engine asset paths in protocol/gameplay/client core
- Core emits semantic commands; engine bridge fulfills them
- Scene nodes are replaceable views of client state, not the authoritative client state
- Direct connect must work without server-list service
- Singleplayer should use local server + loopback, not a separate offline path

---

## 15) Related Docs

- `Docs/CHUNK_TERRAIN_STATICS_IMPLEMENTATION_GUIDE.md`
- `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`
- `Docs/game_design_summary.md`
- `Docs/NETWORK_BASELINE_2026-02-22.md`
- `NETWORK_PRD.md`
- `NETWORK_IMPLEMENTATION_PLAN.md`
