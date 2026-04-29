# UI Kernel AGENTS

## Mission

Build the UI/schema system as an engine-agnostic kernel with explicit client, server, and renderer bridge layers.

The UI kernel is a protocol, schema, validation, cache, action, and model-update engine. It is not the Godot UI implementation, not the server gameplay UI service, and not a place for gameplay-specific inventory, crafting, character, or admin behavior.

Use `Client/UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md` as the current design reference. If implementation reveals the plan is wrong, update the contracts and the plan early. Do not work around contract problems in higher layers.

## Priority Order

Do not violate this order without explicit justification:

1. Correctness
2. Safety
3. Simplicity
4. Maintainability
5. Performance

UI is user-facing and protocol-facing. Correctness, validation, deterministic behavior, and safe failure matter more than clever renderer shortcuts.

## Required Layering

Use this split unless there is a strong reason not to:

- `Kernels/UI`
  - engine-agnostic schema contracts
  - node ids and capability records
  - schema/catalog/model identity records
  - payload slot descriptors
  - model delta/update descriptors
  - action and response descriptors
  - tooltip/context/radial action descriptors
  - inventory/container viewer-agnostic contracts
  - state binding descriptors and resolver interfaces
  - validators, compatibility checks, and codec/round-trip logic
  - cache negotiation primitives
  - allocation-conscious serializers/deserializers
- client runtime bridge, outside `Kernels/UI`
  - resolver registry implementations over entity, inventory, party, local-player, and session caches
  - client-side schema/catalog cache storage
  - panel instance runtime
  - input/focus routing into the renderer
  - application of model deltas to local panel instances
- Godot renderer bridge, outside `Kernels/UI`
  - Godot controls, scenes, themes, canvas managers, and surface hosts
  - `World`, `Hud`, `Menu`, `Modal`, `Exclusive`, `Tooltip`, and `Debug` host mounting
  - visual layout, drag/drop visuals, hover timing, radial animation, tooltip placement
- server bridge, outside `Kernels/UI`
  - panel providers/builders for login, character flow, inventory, crafting, admin, ECS controls
  - permission filtering
  - action handling and server-side validation
  - server-side panel instance lifetime
  - telemetry batching discipline
- network bridge, outside `Kernels/UI`
  - message transport over `Kernels/Network`
  - routing UI open/update/close/action packets
  - service-class/budget decisions

Bridge and renderer code may depend on the UI kernel. The UI kernel must not depend on Godot, server runtime, client runtime caches, network transport implementation, ECS world mutation, database/persistence, or gameplay systems.

## Kernel Ownership Rules

The UI kernel owns:

- stable ids and versioning
- schema AST and node metadata
- schema/catalog/model cache identity
- validation limits and compatibility checks
- action/response shape
- state binding descriptor shape
- tooltip/context/radial descriptor shape
- inventory/container neutral item and placement metadata shape
- update/delta shape
- deterministic merge/update rules
- codec and round-trip behavior

The UI kernel does not own:

- actual login authentication
- character/account persistence
- inventory gameplay rules
- crafting recipes or success logic
- admin permissions
- ECS system mutation
- Godot rendering
- user input collection
- network send/receive loops
- server content patch trust policy beyond declared metadata contracts

If a feature needs both UI contracts and gameplay knowledge, put the contract in `Kernels/UI` and the behavior in a bridge/provider layer.

## Non-Negotiable Design Rules

### Schema Is Data

- Server sends schema/data, never executable UI code.
- No script blocks, expression evaluators, or arbitrary reflection execution in schemas.
- Conditional UI is represented with explicit slots, groups, variants, and server-owned model state.

### Exact Identity Matching

- Schema and catalog cache identities must match exactly.
- No "close enough" schema compatibility at runtime.
- A mismatch is a cache miss, supersession flow, or protocol error.

### Validation Before Use

Validate all external schema, catalog, model, delta, action, tooltip, and context-menu payloads before applying them.

Validate at least:

- max bytes
- max nodes
- max tree depth
- max slot count
- max input fields
- max rows/columns
- max text length
- max asset references
- valid node ids
- valid slot references
- valid action ids
- valid row/item/container keys
- valid surface/layer target
- valid capability requirements
- catalog/model compatibility

### Server Authority

- Client presentation may be optimistic, but server actions are authoritative.
- Permissions and disabled states are presentation hints on the client.
- Server revalidates every mutation, admin action, trade commit, drag/drop, context action, and world interaction.

### Central Shared Vocabulary

Semantic helpers are shared vocabulary. Do not invent local helper conventions in one panel.

Centralize conventions for:

- slot names
- action ids
- row keys
- catalog paths
- state binding source ids
- inventory/container placement facets
- tooltip payload fields
- context/radial action descriptor fields

If a helper needs a new convention, add it to the shared contract and tests first.

### Surface Target Is Not Focus Mode

Keep surface/layer target distinct from focus behavior:

- `World`
- `Hud`
- `Menu`
- `Modal`
- `Exclusive`
- `Tooltip`
- `Debug`

A tooltip can be top-layer with no focus. A world nameplate can be clickable. An exclusive panel can block lower layers. Do not collapse these concepts into one enum.

### Viewer-Agnostic Inventory

The core inventory/container contract must not assume one viewer.

- D2 grid viewer may use cell, width, height, and rotation.
- UO freeform viewer may use x/y and draw order.
- Skyrim-style list viewer may ignore placement and use sort/category metadata.

Keep item ids, container ids, quantities, permissions, and mutation action shape independent from presentation.

## Bridge Rules

### Client Bridge

Client runtime bridge owns:

- schema/catalog cache store integration
- panel instance registry
- resolver registry implementations
- local model state for open panels
- applying validated deltas
- routing actions to network/session code

Client bridge must not:

- execute server UI scripts
- bypass kernel validation
- infer gameplay permissions from presentation metadata
- let a stale schema/catalog keep receiving incompatible deltas

### Godot Renderer Bridge

Godot bridge owns:

- actual controls and scenes
- focus traversal
- z-order and host mounting
- drag/drop visuals
- tooltip placement
- radial/context menu presentation
- theme/resource resolution

Godot bridge must not:

- define protocol semantics
- mutate server-owned state directly
- know server provider classes
- make schema cache decisions
- hide validation failures by "best effort" rendering malformed data

### Server Bridge

Server bridge owns:

- builder APIs
- panel provider registry
- server-side panel instance state
- action handling
- permission filtering
- telemetry scheduling
- login/character/inventory/crafting/admin gameplay behavior

Server bridge must not:

- put provider-specific state into the UI kernel
- rely on the client to enforce permission
- emit schemas without validation in tests
- push telemetry directly from every system every tick

## Testing Rules

Write tests early and keep them close to the kernel. UI contracts are not "just UI"; they are protocol and trust boundaries.

Minimum UI-1 kernel coverage:

- schema codec round trips
- catalog identity round trips
- model payload round trips
- delta/update round trips
- action/response round trips
- capability negotiation records
- surface/layer target validation
- focus/open/back policy descriptor validation
- validator limit failures
- invalid node/slot/action references
- schema/catalog/model compatibility checks
- schema supersession and catalog supersession behavior
- slot update priority rules
- state binding descriptor validation
- resolver registry contract tests with fake resolvers
- tooltip and rich tooltip descriptor validation
- context/radial descriptor validation
- inventory/container identity and placement facet validation
- trade-window contract shape
- auction/listing live repeater contract shape

Minimum bridge/renderer smoke coverage as bridges appear:

- cold schema open
- hot schema open
- catalog miss/hit
- full model update
- scoped model delta
- repeater/table row add/update/remove
- shared container item add/update/remove
- context menu open/action/close
- radial nested category/action flow
- tooltip hover/focus/detail
- state-bound slot refresh from fake caches
- multiple panel instances using the same schema with different binding targets
- modal/exclusive/menu layer routing
- tooltip suppression by exclusive panels
- session loss panel cleanup
- stale target/range-exit action rejection

Regression tests are required when fixing UI behavior. If a bug is hard to reproduce in Godot, add an engine-agnostic kernel or bridge test that captures the shape.

## Benchmark Rules

Benchmark early, before the renderer and gameplay panels hide contract costs.

Initial benchmark targets:

- schema validation throughput and allocations
- schema codec encode/decode throughput and allocations
- model payload encode/decode throughput and allocations
- delta application cost
- repeater row add/update/remove cost
- catalog lookup/join cost
- state binding resolver dispatch cost with fake resolvers
- tooltip/context descriptor decode cost
- action/response encode/decode cost
- cache identity lookup cost
- large panel validation cost
- large inventory/grid/list model update cost

Benchmark representative shapes:

- login panel
- character select with 1, 8, and 32 rows
- stat distribution panel with conditional groups
- crafting catalog with many recipes
- inventory grid with many items
- inventory list over the same model
- admin table with dense rows
- telemetry-heavy panel with batched updates
- context/radial menu with nested actions

Track both time and allocations. The early target is not perfect performance; it is catching bad shapes before they become architecture.

## Performance Rules

Treat these as performance-sensitive:

- schema validation
- serialization/deserialization
- model delta application
- repeater/table row updates
- state binding refresh dispatch
- inventory/container update paths
- telemetry batch application
- context/radial descriptor construction and decode

Avoid in these paths unless justified:

- reflection
- dynamic invocation
- boxing in per-node/per-slot loops
- per-slot heap allocations
- unbounded string allocations
- repeated hash computation
- hidden full-model rebuilds for small deltas
- renderer-specific objects in kernel data structures

Prefer clear, measurable data structures. Use pooling/spans/value types only when tests and benchmarks justify the complexity.

## Implementation Discipline

- Build UI-1 contracts first.
- Add codec/validator tests as the contracts are introduced.
- Add benchmarks before integrating large panels.
- When UI-3/UI-4 exposes a bad contract, fix the kernel contract immediately.
- Do not patch around contract flaws in Godot/server/client bridge code.
- Keep `Client/UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md` synchronized with meaningful contract changes.

## Guiding Rule

Build a small, explicit, measurable UI kernel with strict validation and boring contracts.

Make the common UI easy through centralized helpers, not by weakening the core model.
Make the weird UI possible through capability-gated primitives and bridge layers, not by putting gameplay or renderer logic in the kernel.
