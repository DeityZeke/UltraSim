# Client AGENTS

## Mission

Build the client as a portable runtime plus thin engine shell.

The Godot client is the first host, not the client architecture itself. Preserve the old client's useful shape:

- one persistent shell/root scene
- injectable current map/world/avatar/menu/environment content
- engine-agnostic client runtime and network state
- Godot-owned presentation, input, camera, and UI rendering
- explicit content/mod/patch trust boundaries

## Priority Order

Do not violate this order without explicit justification:

1. Correctness
2. Safety
3. Simplicity
4. Maintainability
5. Performance

Client code is performance-sensitive in per-frame presentation and packet-consume paths, but not at the cost of unsafe patching, hidden coupling, or untestable UI/runtime behavior.

## Required Layering

Use this split unless there is a clear reason not to:

- `Client/Core`
  - engine-agnostic client runtime state
  - input samples/sequencing
  - prediction state
  - interpolation state
  - connection quality classification
  - client-side content/cache metadata that does not require engine APIs
- `Client/Network`
  - client bridge over `Kernels/Network`
  - packet send/receive integration
  - control-plane/session-facing client flow
  - observer/latest-state/event application into engine-neutral callbacks
  - chat event bridge
- `Client/_Godot`
  - root scene and shell orchestration
  - input collection
  - actor/camera/render presentation
  - UI rendering and focus
  - Godot resource loading
  - local settings storage
- future shared content packages
  - content ids
  - patch manifests
  - cache metadata
  - validation contracts

Do not:

- put protocol logic in `Client/_Godot`
- reference Godot from `Client/Core` or `Client/Network`
- reference server runtime/host internals from client runtime layers
- make UI schema rendering depend on server implementation classes
- let streamed server content execute code

## Shell Shape Rules

The Godot client shell should keep stable long-lived containers:

- `World/Map`
- `World/DynamicEntities`
- `World/Effects`
- `World/CameraRig`
- `UILayers/World`
- `UILayers/HUD`
- `UILayers/Menus`
- `UILayers/Debug`

Current things are injected into those containers:

- current map/dimension
- current terrain/static chunk renderer
- current environment/lighting
- current avatar/mobile definition catalog
- current local app menus
- current server-driven panels
- current patch/content pack resolver

Do not replace the whole root scene to change maps, avatars, UI menus, or environments. Replace or update the specific owned content inside the stable container.

## Presentation Rules

Engine-agnostic client/runtime code emits semantic presentation intent:

- set actor definition
- set actor pose
- play animation
- spawn VFX
- play SFX
- show chat bubble
- update nameplate
- despawn actor
- open/update/close UI panel

Godot decides how that looks:

- which scene/resource to instantiate
- which AnimationTree/AnimationPlayer state to drive
- which AudioStreamPlayer to use
- which shader/material/style/theme to apply
- where projected labels and overlays appear

## UI Rules

Keep two UI paths distinct:

- local app shell UI
- server-driven schema UI

Local app shell owns:

- start menu
- server browser/direct connect
- local settings
- in-session options/pause
- disconnect/error screen

Server-driven schema UI owns:

- login
- character select/create/delete
- gameplay gumps/windows
- admin/game-master panels
- vendors/crafting/inventory/journal-style panels

Server-driven panels must be hosted under `MenuCanvasManager`, not registered as `ClientAppShell` screens.

## UI Schema Cache Rules

The UI schema path is a two-path cache model:

- cold path:
  - server opens panel with type/revision/hash/payload
  - client does not have schema
  - client requests or acknowledges `need_definition`
  - server sends full schema definition plus payload/update
  - client validates, renders, and caches the schema under the server-specific cache namespace
- hot path:
  - server opens panel with type/revision/hash/payload or delta
  - client has matching schema
  - client renders cached schema with payload/delta

Rules:

- cache identity must include server identity, panel type, revision, and schema hash/version
- exact cache matching only; do not accept "newer enough"
- payload/model data is separate from schema definition
- server sends schema/data, never executable UI code
- schema must be validated before rendering
- cache must have quota/eviction hooks even if the first implementation keeps them simple

## Content, Patch, And Mod Boundaries

There are three distinct trust classes:

### Texture/Resource Packs

User-selected texture packs are client-local visual remaps.

They may:

- remap textures/materials
- remap icons/UI skins
- override local presentation resources through the content resolver

They should not:

- change protocol behavior
- execute code
- mutate server-authoritative state

### Server Content Patches

Server-specific streamed patches are data/assets accepted from a server into that server's cache namespace.

They may include:

- models
- textures
- materials
- sounds
- icons
- item visuals
- map/static chunk content
- UI schema definitions
- server-specific remaps/overrides

They must not include:

- executable code
- scripts
- native plugins
- arbitrary runtime injection

Store server-specific cache data under a per-server folder in the user application data area. The root should allow:

- global runtime cache
- per-server cache/config folders
- patch manifests
- asset blobs
- schema cache
- map/static chunk cache
- local server-specific settings

### Client Mods

The literal `Mods` folder is different from server streamed content.

Mods may eventually use client-side API hooks and scripts/runtime injection. They are explicitly user-installed and "use at your own risk."

Rules:

- support modding as a platform capability
- do not support individual mods as core product behavior
- keep mods opt-in and visible to the user
- do not silently install executable mod code from a server
- keep mod API hooks explicit and versioned

## Terrain And Static Content Rules

The client should preserve hooks for UO/Minecraft-style static world content:

- server-authoritative gameplay remains server-side
- frozen/static chunks can move out of hot ECS-style replication
- clients can receive static chunk updates/patches
- housing or world edits can produce updated static content
- updated static content is cached client-side and rendered as map/static content
- dynamic entities remain separate from static map chunks

The shape should allow:

- stream updated chunk
- stream house placement/static patch
- freeze region/chunk into client-rendered statics
- hot-patch visible clients
- keep dynamic collisions/physics authoritative on the server when needed

Do not design terrain/static streaming as actor replication. It is bulk/content patching plus server-authoritative gameplay state where required.

## Local Single-Player Rule

Single-player should use the same server-authoritative path as remote play.

The intended shape is:

- launch or attach an embedded/headless local server
- connect the normal client session driver over loopback
- use the same login/session/world/persistence flow
- separate local-world save/cache namespaces from remote server namespaces
- keep local-server startup/shutdown visible enough to diagnose failures

Do not build a separate offline gameplay runtime unless explicitly approved. That would duplicate authority, persistence, physics, and protocol behavior.

## Server Script Compiler Boundary

The server is expected to have a RunUO-style script compiler/registration phase.

Old server startup logged this shape:

- `Core: Initializing... - done`
- `Core: Starting... - done`
- `Scripts: Compiling C# scripts... - done (0 warnings, 0 errors)`
- `World: Loading...`
- discovery/listening startup

That is a server-side startup/plugin concern. The client should expose stable content/UI/presentation hooks that server scripts can target through protocol and schema data, but client streamed patches must not become a covert code delivery system.

## Testing Rules

Tests are expected early and often.

For each non-trivial slice, add the narrowest useful automated coverage before or alongside implementation.

Minimum test expectations by layer:

- `Client/Core`
  - input sequencing
  - prediction/reconciliation behavior
  - interpolation sampling
  - connection-quality tier/hysteresis
  - cache-key/path policy that does not require Godot
- `Client/Network`
  - packet send/receive round trips
  - session lifecycle behavior
  - observer state/event application
  - chat bridge behavior
  - UI ack/response paths once UI contracts are ported
- shared UI/content code
  - schema codec round trips
  - schema validation limits
  - cache hit/miss behavior
  - manifest/hash validation
  - server-specific cache path resolution
- `Client/_Godot`
  - keep direct unit coverage light
  - prefer adapter logic that can be tested outside Godot
  - add smoke/build validation for scene/project compile
  - add focused integration/manual checklists where Godot automation is costly

Do not use "Godot is hard to test" as a reason to leave all logic inside Godot scripts. Move testable logic downward.

## Benchmark Rules

Benchmark hot paths when they become real:

- packet consume/apply
- observer state/event application
- interpolation buffers
- UI schema decode/render planning
- content manifest validation
- asset-cache lookup
- projected world UI cap/prioritization

Do not benchmark Godot visual rendering before the shell is functionally correct unless the issue is visibly rendering-bound.

## Performance Rules

Treat these as hot paths by default:

- per-frame actor presentation
- per-frame camera follow
- per-frame projected world UI
- inbound packet consume
- observer state apply
- chat/event fan-in
- content/cache lookup during active gameplay

Avoid in these paths unless justified:

- per-entity heap allocations
- reflection
- string parsing
- dynamic resource loads every frame
- unbounded dictionaries/lists without pruning
- broad scene tree searches in `_Process`

## Security Rules

All server-provided data is untrusted.

Validate:

- UI schemas
- patch manifests
- asset paths
- file sizes
- decompressed sizes
- hashes
- server cache identity
- protocol payload lengths

Never:

- execute server-provided code
- write outside the approved cache roots
- allow path traversal
- load native plugins from server content
- trust a server to select a local mod silently

## Implementation Rules

- Use old client code as behavioral reference, not a direct architecture port.
- Preserve the modular root/container shape.
- Prefer small explicit services over a giant Godot runtime coordinator.
- Keep startup/bootstrap thin.
- Keep app state explicit.
- Keep current map/avatar/menu/environment injectable.
- Keep UI category layers persistent.
- Put reusable logic below `Client/_Godot` whenever practical.
- Add tests with each slice where the behavior can be tested without Godot.
- Document manual validation gaps when automation is not practical yet.

## First Slice Direction

Start with the smallest visible shell:

1. Godot client C# project.
2. Root scene and runtime root.
3. Stable world/UI containers.
4. Preview actor.
5. Mobile definition/catalog.
6. Local controller.
7. Orbit camera.
8. Basic build/smoke validation.

Do not start discovery, schema UI, content patching, or physics before the shell can visibly run and be tested.

## Guiding Rule

Keep the client light, portable, and replaceable.

The server decides truth. The client presents truth, caches safe data, and exposes explicit hooks for user-installed mods.
