# Inventory Kernel AGENTS

## Mission

Build `Kernels/Inventory` as an engine-agnostic inventory/container kernel with explicit bridge layers for ECS, server gameplay rules, client runtime state, UI viewers, persistence, and Godot rendering.

The inventory kernel is the neutral source of item/container identities, ownership references, placement facets, permission vocabulary, mutation intents, transaction shapes, and validation. It is not the ECS implementation, not the Godot inventory UI, not a database, and not a gameplay rules monolith.

Use [README.md](./README.md) as the current design reference. If implementation reveals the plan is wrong, update the contracts and this doc early.

## Priority Order

Do not violate this order without explicit justification:

1. Correctness
2. Safety/security
3. Simplicity
4. Maintainability
5. Performance

Inventory bugs can duplicate, delete, steal, or corrupt items. Correctness and explicit authority boundaries matter more than clever shortcuts.

## Required Layering

Use this split unless there is a strong reason not to:

- `Kernels/Inventory`
  - ids and refs
  - owner/requester identity handles
  - item instance and stack records
  - container metadata and snapshots
  - placement mode/facet contracts
  - permission vocabulary
  - mutation intent/result envelopes
  - transaction lifecycle contracts
  - validation helpers and pure rule-neutral compatibility checks
  - codec/round-trip contracts when needed
- server/ECS bridge, outside `Kernels/Inventory`
  - resolves owner/requester refs into ECS entities, accounts, characters, vendors, houses, guilds, and world objects
  - checks range, line of sight, ownership, housing, guild, vendor, bank, lock, and scripted rules
  - mutates actual ECS/game state
  - emits persistence and audit events
- client bridge, outside `Kernels/Inventory`
  - consumes container snapshots/deltas
  - tracks local open container views
  - submits UI-originated intents with client sequence/correlation ids
  - receives server-authoritative results
- UI kernel/bridge, outside `Kernels/Inventory`
  - renders list/grid/freeform viewers over neutral container state
  - maps item/container records to UI schema/model payloads
  - submits inventory intents through the inventory/client bridge
- Godot renderer, outside `Kernels/Inventory`
  - drag/drop visuals
  - list/grid/freeform controls
  - placement previews
  - hover, tooltip, and context menu presentation

The inventory kernel may define `Entity` owner/requester refs. It must not depend on `Kernels/ECS` or resolve those refs itself.

## Kernel Ownership Rules

The inventory kernel owns:

- stable inventory ids
- owner/requester ref shape
- item definition vs item instance distinction
- stack quantity/max-stack fields as data
- container id/type/lifecycle metadata
- placement mode compatibility vocabulary
- permission descriptor vocabulary
- mutation intent/result shape
- transaction/suspended item state shape
- validation of structurally invalid payloads
- correlation/audit id fields

The inventory kernel does not own:

- ECS entity lookup
- account/character persistence
- item definition catalogs
- actual stack merge policy
- actual container capacity policy
- equipment rules
- range checks
- housing/rental/guild/vendor/bank gameplay rules
- Godot rendering
- UI schema projection
- database writes
- anti-cheat/audit storage

If a feature needs gameplay knowledge, put only the neutral contract in the kernel and the behavior in a bridge/rules layer.

## Non-Negotiable Design Rules

### Container Truth Is Not The UI

Do not let the first D2 grid implementation define the inventory model.

- list viewer must be able to consume the same container state
- grid placement is optional metadata
- freeform placement is optional metadata
- item/container identity, permissions, quantity, and mutation semantics are independent from presentation

Write list-style fixtures before grid-specific helpers when possible.

### Refs Are Not Direct Dependencies

Owner and requester refs are handles.

The kernel can carry:

- entity refs
- character refs
- account refs
- world-object refs
- guild refs
- vendor refs
- system refs

The bridge resolves them. The kernel does not know whether an entity is loaded, dead, owned by a player, or in range.

### Server Authority

Every mutation is server-authoritative.

Clients and UI viewers submit intents. Bridges/rules validate and apply them. The client may preview a drag/drop placement, but the server decides the result.

### Placement Mode Compatibility

Container placement mode and item placement metadata must match structurally.

The kernel should reject obvious mismatches:

- freeform placement into grid-only container
- grid placement into list-only container
- slot placement into non-slot container

Detailed bounds, collisions, equipment eligibility, rotation permission, and capacity are rules-layer concerns.

### Stack Policy Is Rule-Owned

The kernel carries quantities and merge intents. It must not hardcode mergeability.

`MaxStackQuantity > 1` means stackable in principle. Rules decide whether two concrete item instances can merge.

### Multiple Viewers Are Normal

A container may have many concurrent viewers:

- owner
- GM/admin
- corpse looters
- account-bank sessions
- trade participants
- NPC/system observers

Do not design APIs around "one player opens one container."

### Transactions Must Prevent Duplication

Trade/escrow-like flows need explicit suspended/transactional state.

An item must not be logically both in its source backpack and in a trade offer. Commit/cancel/disconnect behavior must be representable by the contract before runtime implementation.

### Audit Fields Are Reserved Early

Mutation intents/results should carry correlation/audit ids from the start, even if initial bridges only pass them through.

Do not retrofit audit/correlation after every intent shape is already in use.

## Bridge Rules

### Server/ECS Bridge

Bridge owns:

- resolving refs
- checking authority
- applying mutations
- dirtying ECS/components
- persistence coordination
- audit/event logging
- item definition lookup
- scripted rule evaluation

Bridge must not:

- bypass kernel validation
- mutate item/container state without producing result/delta shape
- rely on client permission hints
- silently duplicate or destroy transactional items

### UI Bridge

UI bridge owns:

- mapping container snapshots to UI schema/model data
- choosing list/grid/freeform viewer
- routing drag/drop/context actions into inventory intents
- applying server deltas to open viewers

UI bridge must not:

- define item truth
- decide server permissions
- assume grid placement exists for all containers
- mutate container state locally as authority

### Godot Renderer

Godot owns visuals only:

- drag ghost
- placement highlight
- hover state
- context menu placement
- tooltip display
- grid/list/freeform controls

Godot must not define inventory rules or bypass the server-authoritative mutation path.

## Testing Rules

Add kernel tests before bridge implementation.

Minimum early coverage:

- owner/requester ref validation
- unresolved owner result shape
- container lifecycle validation
- placement mode vs placement metadata compatibility
- stack split/merge intent validation
- move/swap/equip intent validation
- public/default world container permissions
- GM/admin viewer over another owner container
- NPC/system requester intent shape
- account-owned bank with multiple containers
- multiple concurrent viewer session shape
- locked/no-trade/no-drop item facets
- trash/destructive lifecycle marker
- trade/transaction suspended item shape
- commit/cancel transaction result shape
- audit/correlation id round trips
- list and grid viewer fixtures over the same neutral container state

Regression tests are required for item duplication, item loss, stale item ids, stale container ids, and permission bypass fixes.

## Benchmark Rules

Benchmark early.

Initial benchmark targets:

- item/container snapshot validation cost
- container delta validation cost
- placement compatibility checks
- move/split/merge intent validation
- transaction commit/cancel validation shape
- viewer projection over 10, 100, and 1000 item containers
- permission descriptor evaluation hook with fake resolver
- allocation behavior for many small deltas

Inventory can become hot through loot, crafting, vendors, banks, trade, and NPC simulation. Keep contract validation measurable before bridge complexity arrives.

## Performance Guidance

Avoid in hot paths unless justified:

- reflection
- per-item heap allocations
- boxing
- string parsing loops
- dictionary rebuilds per delta
- viewer-specific projection inside kernel validation

Prefer:

- stable ids
- immutable snapshots
- small value records
- reusable compatibility contexts
- explicit arrays/lists at boundaries
- bridge-owned caches for resolved rules

## Guiding Rule

Build a small, explicit, measurable inventory kernel.

Keep item/container truth independent from UI viewers, ECS storage, and gameplay rule bridges. If D2 grid, Skyrim list, and UO freeform inventory cannot share the same container state, fix the kernel contract before building more UI.
