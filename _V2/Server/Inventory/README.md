# Server Inventory Bridge

`Server/Inventory` is the server-side bridge between runtime concepts and `Kernels/Inventory`.

The inventory kernel owns neutral contracts. This bridge is allowed to know about ECS, sessions, accounts, characters, world objects, gameplay policy, owner lifetime, and the server-side store prototype.

The core rule is:

```text
kernel carries shape; bridge resolves authority and effects
```

## Implemented Surface

The bridge currently provides:

- runtime ref resolution:
  - `IInventoryRuntimeRefResolver`
  - `EcsInventoryRuntimeRefResolver`
  - packed ECS entity ref codec using `entity:{packedUlong}`
  - runtime resolution result/failure vocabulary
- policy interfaces:
  - `IInventoryContainerAccessPolicy`
  - `IInventoryContainerCapacityPolicy`
  - `IInventoryEquipmentSlotPolicy`
  - `IInventoryInteractionRangePolicy`
  - `IInventoryItemLockPolicy`
  - `IInventoryMutationRules`
  - `IInventoryOwnerLifetimePolicy`
  - `IInventoryStackMergePolicy`
  - `IInventoryTransactionCommitPolicy`
- default owner-lifetime policy for unresolved/despawned owners
- in-memory store prototype:
  - container snapshot registry
  - viewer registry/session tracking
  - scoped delta recipient selection
  - owner-lifetime refresh handling
  - transaction begin/suspend/cancel/commit
  - unresolved suspended-item return events
  - rule-gated move/split/merge/equip/lock application
- concrete starter rules:
  - whole-item move
  - split stack
  - merge stack
  - equip item
  - lock/unlock item

## Runtime Refs

Entity refs intentionally preserve the full packed ECS handle, including version bits. Using only the entity index would be smaller, but it would also allow stale refs to accidentally resolve after slot reuse.

If inventory transport later needs a compact binary form, encode the same packed value as an 8-byte integer rather than changing the logical ref shape.

Non-ECS owner/requester refs stay external and opaque. Account, character, guild, vendor, session, script, and system refs are resolved by the runtime layer that owns them, not by ECS.

## Store Discipline

The in-memory store is a correctness prototype, not a persistence layer.

It guarantees:

- mutations validate against current snapshots
- accepted mutations apply all-or-nothing
- rejected and `NoOp` mutations do not mutate snapshots
- container deltas are scoped through the viewer registry
- store events are effects for higher layers, not network packets
- malformed store state is a store failure, while rule rejection is an explicit mutation result
- persistence, audit logging, database ownership, and transport delivery stay outside the store

The store does not know what a trade, bank, corpse, house, vendor, rental, or trash bin is. Those are policy and gameplay concepts above the store.

## Mutation Rules

### Move

`InventoryMoveMutationRules` supports whole-item `MoveItem` intents.

It composes:

- requester resolution
- source `Take` access
- destination `Put` access
- destination placement compatibility
- range policy
- capacity policy

The rule validates the effective destination placement, not the item's old source placement. That keeps cross-viewer moves legal when the caller supplies destination-compatible placement, such as moving a list-backed loot row into a grid-backed backpack.

### Split

`InventorySplitStackMutationRules` creates a new stack item from part of a source stack.

A split intent must provide source container, destination container, source item, new destination item id, split quantity, and proposed destination placement. The store validates the reduced source stack and new destination stack before committing either snapshot.

Split always creates a new stack. Consolidating into an existing destination stack is merge, not split.

### Merge

`InventoryMergeStackMutationRules` transfers quantity from one existing stack into another existing stack.

The starter rule requires the merge policy's approved transfer quantity to exactly match the intent quantity. If a policy wants to cap to available stack space, the caller should submit the capped quantity explicitly. The store must never silently apply a different quantity than requested.

Partial merge decrements the source and increments the destination. Full merge removes the source item and increments the destination, atomically.

### Equip

`InventoryEquipItemMutationRules` treats equip as a whole-item transfer into a slot-backed equipment container.

The kernel validates that `EquipItem` carries slot-shaped placement. This bridge decides whether the item is allowed in that slot through `IInventoryEquipmentSlotPolicy`.

Occupancy, item category eligibility, class/race restrictions, two-handed conflicts, cosmetic overlays, and similar gameplay constraints belong in equipment slot policies, not in the inventory kernel.

### Lock/Unlock

`InventoryLockItemMutationRules` supports `LockItem` and `UnlockItem` as in-place metadata mutations over `InventoryItemFlags.Locked`.

The starter rule requires:

- requester resolution
- source `Mutate` access
- range approval
- item lock policy approval
- quantity `1` as an action-shape convention

If the item is already in the requested state, the rule returns `NoOp` and the store emits no delta. Accepted lock-state changes emit `ItemUpdated` with the updated item snapshot.

## Transactions

Transactions are generic participant-container sets with suspended items. They are not trade-specific objects.

Current transaction behavior:

- `BeginTransaction` registers participant containers.
- `SuspendWholeItem` removes an item from its source container and stores the item snapshot in transaction-reserved state.
- `CancelTransaction` resolves suspended items backward to their source containers.
- `CommitTransaction` resolves suspended items forward to their destination containers.

Cancel behavior:

- If the source container still exists, the item is returned.
- If the source container no longer exists, the store emits `SuspendedItemReturnUnresolved`.
- Higher layers decide whether unresolved items are recovered, destroyed, or rerouted.

Commit behavior:

- Commit uses `IInventoryTransactionCommitPolicy`.
- Policy sees the current destination snapshot, including earlier incoming items in the same commit.
- Destination state at commit time wins over destination state at transaction begin time.
- The store prepares and validates every updated destination snapshot before mutating live state.
- A rejected commit leaves the transaction open and items suspended.
- An accepted commit emits destination `ItemAdded` deltas, clears suspended state, removes the transaction, and emits `TransactionCommitted`.

This is the foundation needed for player trade, auction escrow, crafting escrow, loot reservation, and mail attachment escrow.

## Owner Lifetime

The resolver detects stale refs. `IInventoryOwnerLifetimePolicy` decides what happens to open containers when an owner becomes unresolved.

The default policy maps:

- resolved owner: keep container open
- persistent/world-persistent entity container with despawned owner: close viewers, keep container
- transactional entity container with despawned owner: close viewers and release transactions
- runtime/ephemeral/destructive entity container with despawned owner: close viewers and destroy container
- non-entity unresolved owner: close viewers

The store currently closes viewers, cancels affected transactions, returns suspended items when possible, and emits unresolved-return events when it cannot safely return an item.

## Benchmark Harness

Run:

```bash
dotnet run --project _Benchmarks/UltraSim.Server.Inventory.Benchmarks/UltraSim.Server.Inventory.Benchmarks.csproj -c Release -- --output _Benchmarks/results/server-inventory-benchmarks.json
```

Current scenarios cover:

- accepted same-container reposition moves with no viewers
- accepted same-container reposition moves with one viewer
- rejected out-of-range moves
- accepted cross-container split-stack moves
- accepted cross-container partial merge-stack moves
- accepted cross-container equip-item moves
- accepted in-place lock/unlock item updates
- accepted transaction commits with suspended whole items
- raw `DispatchDelta` fanout across 1000 viewers

Interpretation notes:

- accepted move cost in the prototype is mostly snapshot/result/delta construction, not one-viewer fanout
- equip stress uses a simple linear slot policy over a growing synthetic equipment container
- transaction commit stress includes setup, suspending 1000 items, and committing into a growing immutable destination snapshot
- high-viewer containers such as vendors, auction views, trade observers, and admin views should stay benchmarked as features land

## Validation

Current standard validation:

```bash
dotnet build Server/Inventory/UltraSim.Server.Inventory.csproj -c Release /p:UseSharedCompilation=false
dotnet run --project _Tests/UltraSim.Inventory.Tests/UltraSim.Inventory.Tests.csproj -c Release
dotnet run --project _Tests/UltraSim.Server.Inventory.Tests/UltraSim.Server.Inventory.Tests.csproj -c Release
dotnet build _Benchmarks/UltraSim.Server.Inventory.Benchmarks/UltraSim.Server.Inventory.Benchmarks.csproj -c Release /p:UseSharedCompilation=false
dotnet run --project _Benchmarks/UltraSim.Server.Inventory.Benchmarks/UltraSim.Server.Inventory.Benchmarks.csproj -c Release -- --output _Benchmarks/results/server-inventory-benchmarks.json
```

## Boundary Rules

- The kernel owns neutral id/ref shapes.
- This bridge resolves refs against real runtime stores.
- ECS types must not move back down into `Kernels/Inventory`.
- NPC/entity-AI requesters should resolve through the same requester path as player/session requesters.
- Fake resolvers belong in tests; production bridge code should remain concrete or interface-based.
- Rules interfaces should accept kernel snapshots/intents plus resolved runtime refs, not direct ECS/session/database dependencies.
- Store operations should never silently duplicate, destroy, or reroute suspended items.
