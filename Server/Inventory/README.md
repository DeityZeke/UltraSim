# Server Inventory Bridge

`Server/Inventory` is the server-side bridge between runtime concepts and `Kernels/Inventory`.

The inventory kernel stays engine-agnostic. This layer is allowed to know about ECS entities, server sessions, accounts, characters, world objects, and future gameplay rules.

## Current Status

Implemented first boundary slice:

- `IInventoryRuntimeRefResolver`
- ECS-backed `EcsInventoryRuntimeRefResolver`
- packed ECS entity ref codec using `entity:{packedUlong}`
- runtime ref resolution result/failure vocabulary
- rules foundation interfaces:
  - `IInventoryMutationRules`
  - `IInventoryContainerAccessPolicy`
  - `IInventoryStackMergePolicy`
- `IInventoryOwnerLifetimePolicy`
- default owner lifetime policy for unresolved/despawned owners
- in-memory store prototype:
  - container snapshot registry
  - viewer registry/session tracking
  - scoped delta dispatch recipient selection
  - transaction begin/suspend/cancel
  - owner-lifetime refresh handling
  - whole-item move application through injected mutation rules
  - split-stack application through injected mutation rules
- first concrete move mutation rule:
  - requester resolution check
  - source take access check
  - destination put access check
  - destination placement compatibility check
  - range policy hook
  - capacity policy hook
  - whole-item move only
- first concrete split-stack mutation rule:
  - requester resolution check
  - source take access check
  - destination put access check
  - destination/new item id uniqueness check
  - destination placement compatibility check
  - range policy hook
  - capacity policy hook
  - all-or-nothing source decrement plus new-stack creation
- first concrete merge-stack mutation rule:
  - requester resolution check
  - source take access check
  - destination put access check
  - merge policy hook
  - range policy hook
  - all-or-nothing source decrement/removal plus destination increment
- first concrete equip-item mutation rule:
  - requester resolution check
  - source take access check
  - destination put access check
  - slot placement requirement
  - equipment slot policy hook
  - range policy hook
  - capacity policy hook
  - all-or-nothing source removal plus equipment-slot insertion
- benchmark harness in `_Benchmarks/UltraSim.Server.Inventory.Benchmarks`

Entity refs intentionally preserve the full packed ECS handle, including version bits. Using only the entity index would be smaller, but it would also allow stale refs to accidentally resolve after slot reuse. If inventory transport later needs a compact binary form, encode the same packed value as an 8-byte integer rather than changing the logical ref shape.

## Owner Lifetime Signal

The resolver detects stale entity refs, but it does not decide what happens to open containers. `IInventoryOwnerLifetimePolicy` owns that bridge-level decision.

The default policy currently maps:

- resolved owner: keep container open
- persistent/world-persistent entity container with despawned owner: close viewers, keep container
- transactional entity container with despawned owner: close viewers and release transactions
- runtime/ephemeral/destructive entity container with despawned owner: close viewers and destroy container
- non-entity unresolved owner: close viewers

Transport session closure, persistence, recovery policy, and real gameplay-specific rollback remain future bridge responsibilities.

The in-memory store prototype now exercises the first version of this signal path. It closes viewers, cancels affected transactions, and returns suspended whole items to their source containers when a transaction is cancelled or owner invalidation requires release. If the source container no longer exists, the store emits `SuspendedItemReturnUnresolved` instead of guessing whether to destroy, recover, or reroute the item.

It does not persist, authorize real gameplay, or own transport delivery.

## Store Prototype Rules

- The store holds snapshots and emits operation results.
- The store does not know what a trade, bank, corpse, or house is.
- Transactions are generic participant-container sets with suspended items.
- Delta fanout always uses the container's viewer registry.
- Store events are effects for higher layers to consume; they are not network packets.
- Unresolved suspended-item returns are explicit events for higher layers/rules to recover or destroy deliberately.
- `ApplyMove` evaluates injected mutation rules first, then commits snapshot changes only for accepted whole-item moves.
- `ApplySplit` evaluates injected mutation rules first, then commits source quantity and destination item changes only after both updated snapshots validate.
- `ApplyMerge` evaluates injected mutation rules first, then commits source decrement/removal and destination quantity changes only after both updated snapshots validate.
- `ApplyEquip` evaluates injected mutation rules first, then commits source removal and equipment-slot insertion only after both updated snapshots validate.
- Rule rejection is returned as an `InventoryMutationResult`; malformed store state remains a store failure.
- Persistence, audit logging, and database ownership stay outside this prototype.

## Move Rule Prototype

`InventoryMoveMutationRules` is the first concrete mutation rule. It intentionally supports only whole-item `MoveItem` intents. It proves that real mutation policy can be composed from:

- `IInventoryContainerAccessPolicy`
- `IInventoryInteractionRangePolicy`
- `IInventoryContainerCapacityPolicy`
- kernel snapshots/intents
- resolved requester refs

The rule validates the effective destination placement, not the item's old source placement. That keeps cross-viewer moves legal when the caller supplies a destination-compatible placement, such as moving a list-backed loot row into a grid-backed backpack.

Splits, stack merges, equipment restrictions, grid collision placement, weight limits, ownership rules, and scripted container behavior remain future policy implementations.

## Split Rule Prototype

`InventorySplitStackMutationRules` is the first concrete split rule. It supports creating a new stack item from part of a source stack. A split intent must provide:

- source container id
- destination container id
- source item id
- destination/new item id
- split quantity
- proposed destination placement

The store applies split as an all-or-nothing operation. It builds and validates the reduced source stack and the new destination stack before committing either snapshot. A rejected split leaves both containers unchanged.

Stack merge, partial stack transfer into an existing destination stack, and split-specific definition rules remain future policy implementations.

## Merge Rule Prototype

`InventoryMergeStackMutationRules` is the first concrete merge rule. It supports transferring quantity from one existing stack into another existing stack. A merge intent must provide:

- source container id
- destination container id
- source item id
- destination item id
- merge quantity

The first concrete rule requires the merge policy's approved transfer quantity to exactly match the intent quantity. If a policy wants to cap to available stack space, the caller should submit the capped quantity explicitly. This keeps the result deterministic and prevents the store from applying a quantity different from the requested action.

The store applies merge as an all-or-nothing operation. Partial merges decrement the source stack and increment the destination stack. Full merges remove the source item and increment the destination stack. A rejected merge leaves both containers unchanged.

## Equip Rule Prototype

`InventoryEquipItemMutationRules` is the first concrete equip rule. `EquipItem` intents must carry a slot placement. The kernel validates that the intent is slot-shaped; this bridge layer decides whether the item is allowed in that slot through `IInventoryEquipmentSlotPolicy`.

The first store implementation treats equip as a whole-item transfer into a slot-backed equipment container. It removes the item from the source snapshot and inserts the same item into the equipment container with the requested slot placement only after both updated snapshots validate. Occupancy, item category eligibility, class/race restrictions, two-handed conflicts, cosmetic overlays, and similar gameplay constraints belong in equipment slot policy implementations, not in the inventory kernel.

The benchmark slot policy intentionally uses a simple linear occupancy scan and the prototype store still copies immutable snapshots on each accepted mutation. Large synthetic equip runs into a growing equipment container therefore measure prototype stress, not the intended production cost of an indexed equipment-slot policy.

## Benchmark Harness

Current server bridge benchmark:

```bash
dotnet run --project _Benchmarks/UltraSim.Server.Inventory.Benchmarks/UltraSim.Server.Inventory.Benchmarks.csproj -c Release -- --output _Benchmarks/results/server-inventory-benchmarks.json
```

Initial scenarios cover:

- accepted same-container reposition moves with no viewers
- accepted same-container reposition moves with one viewer
- rejected out-of-range moves
- accepted cross-container split-stack moves
- accepted cross-container partial merge-stack moves
- accepted cross-container equip-item moves
- raw `DispatchDelta` fanout across 1000 viewers

These are intentionally narrow baselines for store/rules overhead before persistence, transport delivery, richer equipment rules, and scripted containers are layered on. The current decomposition points at accepted-move snapshot/result/delta construction as the small-container cost, not one-viewer fanout. Large shared containers still have real linear recipient fanout cost, so high-viewer containers such as vendors, auction views, trade observers, or admin views should remain measured as features land.

## Boundary Rules

- The kernel owns neutral id/ref shapes.
- This bridge resolves refs against real runtime stores.
- ECS types must not move back down into `Kernels/Inventory`.
- NPC/entity-AI requesters should resolve through the same requester path as player/session requesters.
- Fake resolvers belong in tests; production bridge code should remain concrete or interface-based.
- Rules interfaces should accept kernel snapshots/intents plus resolved runtime refs, not direct ECS/session/database dependencies.
