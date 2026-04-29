# Inventory Kernel Plan

## Purpose

`Kernels/Inventory` will define the engine-agnostic inventory/container vocabulary for UltraSim.

It is not the D2 grid viewer, not the Godot inventory UI, not an ECS component implementation, and not the persistence/database layer. It is the neutral item/container/intent contract that gameplay rules, ECS bridges, UI viewers, persistence, vendors, crafting, banks, trade, loot, followers, NPC simulation, and admin tools can all agree on.

The central rule:

```text
container truth is not the UI viewer
```

A D2 grid, Skyrim-style list, and UO freeform bag should be different viewers over the same underlying container state. If the container model cannot support a simple list viewer without grid fields leaking everywhere, the contract is wrong.

## Current Status

The first foundation slice is implemented.

Current contracts:

- `UltraSim.Inventory.csproj`
- validation helpers/results
- owner refs:
  - entity
  - character
  - account
  - world object
  - guild
  - vendor
  - system
- requester refs:
  - player session
  - character
  - entity AI
  - server system
  - admin
  - script
- container lifecycle enum
- placement modes and descriptors:
  - none
  - list
  - grid
  - freeform
  - slot
- permission descriptor vocabulary:
  - public
  - owner
  - account
  - party
  - guild
  - house access list
  - vendor managed
  - admin
  - scripted rule
- item flags:
  - locked
  - no trade
  - no drop
  - quest item
  - transaction reserved
  - non-persistent
- item snapshot validation
- container snapshot validation
- mutation intent/result contracts:
  - move
  - split stack
  - merge stack
  - swap
  - take/drop
  - equip/unequip
  - use/destroy
  - lock/unlock
  - open/close container view
  - begin/commit/cancel transaction
- correlation/audit id field on mutation intents/results
- optional client sequence field for UI-originated requests
- container viewer/session contracts:
  - view id
  - viewer/requester ref
  - granted access
  - optional visibility scope
  - authoritative recipient counting for container deltas
- container delta contracts:
  - snapshot replacement
  - item add/remove/update/move
  - quantity change
  - transaction reserve/release
  - viewer open/close
  - transaction state change
- transaction snapshot contracts:
  - transaction id/state
  - participant containers
  - suspended items with source/destination containers
- projection contracts:
  - point-in-time projection id
  - source container id/revision
  - source placement mode
  - list projection rows with sort descriptor
  - grid projection items with bounds/overlap validation
  - freeform projection rows that preserve x/y/z placement without interpreting z-layer visually
  - projections preserve item snapshots and original placement metadata
- executable tests in `_Tests/UltraSim.Inventory.Tests`
- benchmark harness in `_Benchmarks/UltraSim.Inventory.Benchmarks`

Current validation:

- `dotnet build Kernels/Inventory/UltraSim.Inventory.csproj -c Release /p:UseSharedCompilation=false`: passed
- `dotnet run --project _Tests/UltraSim.Inventory.Tests/UltraSim.Inventory.Tests.csproj -c Release`: passed
- `dotnet build Server/Inventory/UltraSim.Server.Inventory.csproj -c Release /p:UseSharedCompilation=false`: passed
- `dotnet run --project _Tests/UltraSim.Server.Inventory.Tests/UltraSim.Server.Inventory.Tests.csproj -c Release`: passed
- `dotnet run --project _Benchmarks/UltraSim.Inventory.Benchmarks/UltraSim.Inventory.Benchmarks.csproj -c Release -- --output _Benchmarks/results/inventory-benchmarks.json`: passed

Current benchmark baselines:

- `ContainerSnapshot_Validate_List_1000Items`: about `0.57 ms`
- `ContainerSnapshot_Validate_Grid_1000Items`: about `0.74 ms`
- `MutationIntent_Validate_Move_10000`: about `10.47 ms`
- `MutationResult_Validate_Accepted_10000`: about `7.60 ms`
- `ViewerRegistry_CountRecipients_1000Viewers_1000Deltas`: about `26.82 ms`
  - this is per 1000-viewer fanout pass, or roughly 26.6 ns per viewer check in that benchmark shape
- `TransactionSnapshot_Validate_1000SuspendedItems`: about `3.50 ms`
- `ListProjection_FromGridContainer_1000Items`: about `1.53 ms`
- `GridProjection_Validate_1000Items`: about `1.06 ms`
- `FreeformProjection_Validate_1000Items`: about `0.78 ms`

## Layering

Use the same kernel/bridge discipline as ECS, Network, and UI.

```text
Kernels/Inventory
  neutral ids
  owner/requester refs
  item instance records
  container state records
  placement metadata facets
  permission vocabulary
  mutation intent/result contracts
  transaction lifecycle contracts
  validation helpers

Server/Inventory or Server/Game bridge
  resolves refs into ECS entities, accounts, characters, world objects, houses, vendors
  applies gameplay rules
  validates range, permissions, locks, tags, weight, capacity, transactions
  emits persistence/audit events

Client/Inventory or Client/UI bridge
  consumes replicated/container snapshots
  applies validated deltas
  routes viewer intents
  presents list/grid/freeform viewers

Godot renderer
  drag/drop visuals
  grid/list/freeform controls
  tooltip/context menu presentation
  hover and placement feedback
```

The kernel may use identity references such as `InventoryOwnerRef(Entity, 123)` or `InventoryRequesterRef(Session, abc)`. It must not resolve those references by reaching into ECS, accounts, Godot nodes, server sessions, files, or databases.

The first bridge prototype lives in `Server/Inventory`. It resolves ECS entity owner/requester refs through `IEntityReadOnly` while keeping ECS types out of this kernel. Entity refs use `entity:{packedUlong}` so ECS generation/version bits survive across the bridge and stale slot reuse is rejected.

`Server/Inventory` also owns the first bridge foundation: mutation admission, container access, stack merging, owner-lifetime invalidation policies, and an in-memory store/session prototype. These are bridge contracts, not kernel contracts, because they need runtime resolution and gameplay policy.

## Core Concepts

### Item Identity

Separate definition identity from instance identity.

- `ItemDefinitionId`: stable item type, such as `item.iron_sword`
- `ItemInstanceId`: stable instance id for a concrete item or stack
- `StackQuantity`: current quantity
- `MaxStackQuantity`: rule input, not hardcoded kernel behavior

Rules layer decides whether two items can stack. Kernel intents should carry source/destination item ids; the rules layer decides mergeability based on definition, durability, enchantments, quality, bind state, ownership, freshness, or other server-specific metadata.

Suggested item facets:

- definition id
- instance id
- quantity
- optional display/catalog key
- optional tags
- lock state
- bind/trade restrictions
- transaction/suspended state
- persistence hint

### Owner References

Inventory owner refs are identity handles with different lifetime semantics. The kernel should carry them; bridges resolve them.

Likely owner kinds:

- `Entity`: living player, NPC, pet, mount, follower, creature, spawned world actor
- `Character`: persisted playable character
- `Account`: account-level storage such as shared bank
- `WorldObject`: chest, barrel, corpse, resource node, placed house container
- `Guild`: guild bank/storage
- `Vendor`: vendor stock or buyback pool
- `System`: internal/generated/transient owner

The owner resolver lives outside the kernel. It must answer:

- owner exists and is loaded
- owner exists but is unloaded
- owner was destroyed
- owner is unauthorized to the requester
- owner cannot be resolved due to server/system error

The kernel should not assume that all owner refs are equally durable. A character inventory may persist across sessions; an entity inventory may vanish on death; a world object may be persistent or temporary; account storage may have multiple active character sessions viewing it at once.

### Requester References

The actor submitting an inventory intent is not always a player.

Requester kinds should cover:

- player session
- character
- NPC/entity AI
- server simulation/system
- GM/admin
- scripted server process

NPCs, mounts, followers, and simulated mobs should submit through the same intent/validation shape as players when they move items, loot corpses, feed companions, restock bags, or interact with containers. They may carry different authority, but they should not bypass the rules path silently.

### Containers

A container is an authoritative item-holding state, independent from presentation.

Container metadata should include:

- `ContainerId`
- owner ref
- container kind/type id
- lifecycle
- layout/placement mode
- capacity hints
- permission policy
- item records
- optional tags/rule keys

Do not hardcode "one backpack", "one bank", or "one viewer".

Examples:

- player backpack
- equipment container
- hotbar/ability binding container, if represented as item-like slots later
- pet/mount/follower pack
- NPC inventory
- corpse loot
- world chest/barrel
- bank box
- multiple account bank boxes
- guild bank
- vendor stock
- crafting station input/output
- trade offer side
- house locked-down storage
- rental property storage
- trash bin

### Container Lifecycle

Lifecycle affects persistence, rollback, and viewer warnings.

Suggested lifecycle values:

- `Persistent`: character backpack, bank, house chest
- `WorldPersistent`: placed world chest/barrel/house container
- `Runtime`: temporary mob bag, generated encounter container
- `Ephemeral`: loot roll, crafting output preview, temporary staging area
- `Transactional`: trade offer, auction escrow, two-phase commit container
- `Destructive`: trash bin or sink where items are not guaranteed to persist

The kernel defines lifecycle vocabulary. The rules/persistence layer decides what lifecycle means operationally.

### Placement Modes

Placement metadata must be viewer-agnostic but mode-compatible.

Container declares a placement mode. Items inside the container must carry compatible placement metadata.

Suggested placement modes:

- `None`: no ordering/placement semantics
- `List`: stable list index or sort key
- `Grid`: x, y, width, height, optional rotation
- `Freeform`: x, y, z/order layer
- `Slot`: named slot id, useful for equipment or fixed station inputs

Kernel validation should reject obvious mismatches, such as freeform placement submitted to a grid container. Rules layer validates detailed constraints:

- grid bounds
- collision/overlap
- rotation allowed
- accepted item tags
- weight/count capacity
- nested-container policy
- equipment slot eligibility

This keeps the contract neutral while still preventing nonsense shapes from crossing layers.

### Stack And Merge Rules

The kernel carries stack quantities and merge intents. The rules layer decides policy.

Default interpretation:

- `MaxStackQuantity = 1`: item does not stack
- `MaxStackQuantity > 1`: item may stack if rules allow it

Merge intent should carry:

- requester ref
- source container id
- source item id
- destination container id
- destination item id
- requested quantity
- correlation/audit id

Rules layer evaluates:

- same definition
- same quality
- same durability/freshness
- same enchantments/affixes
- same bind/trade state
- stack cap
- permission/range/container constraints

Do not encode "same definition means merge" into the kernel.

## Permissions

Permissions should be generic now, not housing-specific.

World-opened containers can default to public read/take unless rules override them. Housing, rentals, guilds, vendors, and locked-down items plug into the same vocabulary later.

Suggested permission modes/facets:

- public read
- public take
- owner read/write
- account read/write
- party access
- guild access
- house access list
- vendor-managed
- GM/admin
- scripted rule
- locked item
- no-trade/no-drop/no-destroy item
- quest item

The kernel should carry permission descriptors and produce permission-related result codes. The server bridge decides whether a given requester satisfies a permission rule.

### Per-Item Locks

Some rules attach to items, not just containers.

Examples:

- UO-style house lockdown
- quest item cannot be dropped
- bound item cannot be traded
- reserved item cannot be moved during a transaction

Reserve per-item flags/facets early. Do not model every lock as a container-wide state.

## Container Sessions And Viewers

A container may have multiple concurrent viewers.

Examples:

- player has backpack open
- GM inspects the same backpack
- two players loot the same corpse
- account bank is open from multiple character sessions
- both parties watch the same trade state

Container session metadata should include:

- session/view id
- viewer/requester ref
- target container id
- granted access mode
- permission-filtered visibility if needed
- current sequence/cursor

The kernel should allow live deltas to be broadcast to all authorized viewers. A GM viewer does not change the container shape; it is an elevated viewer with different permission filtering.

Implemented contract rule:

- `InventoryContainerViewerRegistrySnapshot` is the authoritative fanout list for one container.
- `InventoryContainerDelta.RequiredAccess` says the minimum access needed to receive a delta.
- `InventoryContainerDelta.VisibilityScopeId` lets the server/rules bridge separate filtered views, such as vendor display rows vs stock-management metadata.
- Admin viewers bypass visibility scope filtering but still flow through the same registry.
- The kernel only counts/selects eligible recipients; the server bridge owns actual network delivery.

## Mutation Intents

Mutation intents are compact, auditable, server-authoritative requests.

Likely first intent kinds:

- move item
- split stack
- merge stack
- swap positions
- take from container
- drop to world/container
- equip/unequip
- use item
- destroy/discard
- lock/unlock item
- open container
- close container view
- begin transaction
- commit transaction
- cancel transaction

Intent envelope should reserve:

- requester ref
- client sequence when UI-originated
- correlation/audit id
- source container id
- destination container id when applicable
- item ids
- requested quantity
- proposed placement
- optional rule/context id

The kernel owns the shape. The bridge/rules layer owns authority and effects.

`RuleContextId` is an opaque, bridge-minted handle. Callers should not invent arbitrary payload shapes inside it. The server/session/rules layer should mint stable context ids for things like trade sessions, house rules, scripted containers, or vendor interactions; the kernel only validates that the id is stable and routes it through intents/results.

### Audit And Correlation

Reserve a correlation/audit field from the start.

It can be ignored in the first implementation, but intent/result contracts should carry it so admin tools, anti-cheat, trade dispute logs, item provenance, and server diagnostics do not require a later breaking change.

Audit data should be emitted by bridges or persistence/event systems, not stored directly in every kernel object unless needed.

## Transactions And Trade

Trade windows are the most duplication-sensitive inventory shape.

Model trade as linked transactional containers:

- each participant has an offer-side container
- items moved into an offer are suspended/reserved
- commit requires both parties to lock/confirm
- cancel or disconnect rolls items back
- commit atomically moves ownership

Avoid any state where the same item instance is both in the source backpack and in the trade offer. The kernel should include explicit suspended/transactional state so rollback/commit semantics are clear.

Implemented contract rule:

- `InventoryContainerLifecycle.Transactional` marks transactional containers.
- `InventoryTransactionSnapshot` carries the transaction state and participant containers.
- `InventorySuspendedItemSnapshot` represents item quantities reserved between source/destination containers while commit/cancel is unresolved.
- `InventoryContainerDeltaKind.ItemReserved`, `ItemReleased`, and `TransactionStateChanged` provide the live update vocabulary for viewers.

The same transaction pattern can later support:

- auction escrow
- crafting input escrow
- loot roll reservation
- player-to-player mail attachment escrow

## Weird Container Cases

### GM/Admin Inspection

No special item/container shape. A GM is another viewer/requester with elevated permission.

Important requirements:

- multiple viewers per container
- permission-filtered view model
- live deltas to all authorized viewers
- audit entries for admin moves/takes/edits

### NPCs, Mobs, Mounts, Followers

Use the same semantics as players.

An NPC with a mount/follower should use entity-owned containers, requester refs, range/permission checks, and normal intents. AI/system authority may differ from player session authority, but the shape should not fork.

### Banks And Multiple Bank Boxes

Banks are usually account-owned, not character-owned.

Allow:

- multiple containers under the same account owner
- multiple active sessions viewing the same account storage
- bank category/type tags
- rule-layer capacity/perk expansion

### UO-Style Lockdown

Treat as item/container permission state, usually per item.

The kernel carries lock state; housing rules decide who can unlock/move it.

### Rentals

Rentals need time/dynamic access policy.

Use scripted/conditional permission rules. The kernel does not need to know what rent is.

### Trash Bins

Trash bins are destructive/transient containers.

The kernel should carry lifecycle/persistence hints so viewers can warn users. The rules layer owns actual destruction timing.

### Corpses And Shared Loot

Corpses are shared containers with live deltas.

Multiple viewers may remove rows/items concurrently. The server must reject stale take intents cleanly and push row/item remove deltas to other viewers.

## UI Relationship

Inventory UI consumes inventory contracts; it does not define inventory truth.

Projection contracts are read-only, point-in-time views over a container snapshot. They do not own live state and they do not auto-update when container deltas arrive. The live delta path updates the underlying client/server container state, and a bridge may build a fresh projection for a newer container revision when the viewer needs it.

Planned viewers:

- Skyrim-style list viewer
- D2 grid viewer
- UO freeform bag viewer

Implementation discipline:

1. Define neutral inventory/container records first.
2. Write list-view contract/smoke tests before grid-specific work.
3. Add grid placement helpers only as optional placement facets.
4. Keep UO/freeform possible through `Freeform` placement metadata and `ContainerCanvas`, but do not implement the renderer until needed.

Implemented projections:

- `InventoryListProjectionSnapshot` can project a grid-backed container as a simple list while preserving each item's source placement metadata. Sort descriptors are projection/viewer inputs, not new container truth.
- `InventoryGridProjectionSnapshot` is only valid over grid-backed containers. It validates grid dimensions, item bounds, duplicate item ids, and overlapping occupied cells.
- `InventoryFreeformProjectionSnapshot` is only valid over freeform-backed containers. It preserves x/y/z placement metadata but does not define how the renderer interprets visual layering or overlap.

If the list viewer cannot consume the same container state as the grid viewer, the contract has leaked presentation assumptions.

## Bridge Metrics

The kernel should not maintain live metrics counters, but its contracts expose enough data for the server bridge/admin panel to surface useful inventory telemetry later:

- total containers
- active containers, where active means at least one current viewer
- containers by `ContainerTypeId`
- containers by `InventoryOwnerKind`
- containers by lifecycle
- total viewers
- open transactions and suspended item counts
- mutation accepted/rejected counts by result/failure reason
- delta fanout recipient counts

## Testing Plan

Early kernel tests should cover:

- owner/requester ref validation
- unresolved owner result shapes
- container placement mode vs item placement metadata compatibility
- stack split/merge intent validation without hardcoded merge policy
- public/default world container permission descriptors
- GM viewer over another owner/container
- NPC/system requester intent shape
- account-owned bank with multiple container ids
- multiple concurrent viewers over one container
- item lock/no-trade/no-drop facets
- destructive/trash lifecycle marker
- transactional/trade suspended item shape
- transaction commit/cancel result shape
- audit/correlation id round trips
- viewer-agnostic list and grid contract fixtures over the same container state

## Benchmark Plan

Benchmark before UI/server integration hides costs:

- item/container snapshot validation
- container delta validation
- placement compatibility checks
- stack split/merge intent validation
- viewer snapshot projection for list/grid/freeform facets
- many-small-item container scans
- transaction commit/cancel validation shape
- permission descriptor evaluation hook overhead with fake rule resolvers

## Implementation Phasing

Suggested first slices:

1. Inventory ids, refs, lifecycle, placement, permission, and item/container snapshot records.
2. Validation tests for refs, placement compatibility, permissions, and lifecycle.
3. Mutation intent/result envelope with correlation/audit id.
4. Split/merge/move/swap/equip intent shapes.
5. Container viewer/session snapshot and live delta contracts.
6. Transactional container/trade contract shape.
7. Minimal benchmarks.
8. UI viewer contracts over the neutral model, starting with list before grid.
9. Server/ECS bridge resolver prototype.

Stop and revise the kernel contract if any bridge starts requiring ad-hoc item/container fields that only make sense for one viewer or one gameplay feature.
