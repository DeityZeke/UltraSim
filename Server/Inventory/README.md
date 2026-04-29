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
- first concrete move mutation rule:
  - requester resolution check
  - source take access check
  - destination put access check
  - range policy hook
  - capacity policy hook
  - whole-item move only

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

It does not persist, authorize real gameplay, split partial stacks, or own transport delivery.

## Store Prototype Rules

- The store holds snapshots and emits operation results.
- The store does not know what a trade, bank, corpse, or house is.
- Transactions are generic participant-container sets with suspended items.
- Delta fanout always uses the container's viewer registry.
- Store events are effects for higher layers to consume; they are not network packets.
- Unresolved suspended-item returns are explicit events for higher layers/rules to recover or destroy deliberately.
- `ApplyMove` evaluates injected mutation rules first, then commits snapshot changes only for accepted whole-item moves.
- Rule rejection is returned as an `InventoryMutationResult`; malformed store state remains a store failure.
- Persistence, audit logging, and database ownership stay outside this prototype.

## Move Rule Prototype

`InventoryMoveMutationRules` is the first concrete mutation rule. It intentionally supports only whole-item `MoveItem` intents. It proves that real mutation policy can be composed from:

- `IInventoryContainerAccessPolicy`
- `IInventoryInteractionRangePolicy`
- `IInventoryContainerCapacityPolicy`
- kernel snapshots/intents
- resolved requester refs

Splits, stack merges, equipment restrictions, grid collision placement, weight limits, ownership rules, and scripted container behavior remain future policy implementations.

## Boundary Rules

- The kernel owns neutral id/ref shapes.
- This bridge resolves refs against real runtime stores.
- ECS types must not move back down into `Kernels/Inventory`.
- NPC/entity-AI requesters should resolve through the same requester path as player/session requesters.
- Fake resolvers belong in tests; production bridge code should remain concrete or interface-based.
- Rules interfaces should accept kernel snapshots/intents plus resolved runtime refs, not direct ECS/session/database dependencies.
