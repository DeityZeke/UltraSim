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

Entity refs intentionally preserve the full packed ECS handle, including version bits. Using only the entity index would be smaller, but it would also allow stale refs to accidentally resolve after slot reuse. If inventory transport later needs a compact binary form, encode the same packed value as an 8-byte integer rather than changing the logical ref shape.

## Owner Lifetime Signal

The resolver detects stale entity refs, but it does not decide what happens to open containers. `IInventoryOwnerLifetimePolicy` owns that bridge-level decision.

The default policy currently maps:

- resolved owner: keep container open
- persistent/world-persistent entity container with despawned owner: close viewers, keep container
- transactional entity container with despawned owner: close viewers and release transactions
- runtime/ephemeral/destructive entity container with despawned owner: close viewers and destroy container
- non-entity unresolved owner: close viewers

Actual session closure, delta emission, item rollback, and persistence are still future store/session responsibilities.

## Boundary Rules

- The kernel owns neutral id/ref shapes.
- This bridge resolves refs against real runtime stores.
- ECS types must not move back down into `Kernels/Inventory`.
- NPC/entity-AI requesters should resolve through the same requester path as player/session requesters.
- Fake resolvers belong in tests; production bridge code should remain concrete or interface-based.
- Rules interfaces should accept kernel snapshots/intents plus resolved runtime refs, not direct ECS/session/database dependencies.
