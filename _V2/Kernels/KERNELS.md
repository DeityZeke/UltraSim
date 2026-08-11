
Kernels are engine agnostic and stand alone.

Kernel rules:

- Kernels must not depend on Godot projects or host wrappers.
- Kernels must not reach into each other for runtime behavior.
- ECS should not reference UI, UI should not reference ECS, Inventory should not resolve ECS entities directly, and Network should not own gameplay mutation.
- Cross-kernel integration belongs in bridge/glue layers under `Client`, `Server`, or another explicit runtime host.
- Kernels may share stable identity concepts only through deliberately small neutral contracts, or through bridge-owned translation.

Current kernels:

- `ECS`: entity/component/runtime scheduling kernel.
- `Network`: transport/protocol/session/replication kernel.
- `UI`: schema/model/action/tooltip protocol kernel.
- `Inventory`: planned item/container/intent kernel. It may carry entity/account/world owner refs, but ECS/account/world resolution belongs in bridges.
