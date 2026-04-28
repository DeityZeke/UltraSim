
The UltraSim "Server" is Engine Agnostic and should have clean boundaries
it can be started in a 'headless' mode via command prompt
for a GUI/UI use the "Console" or "Godot" versions - they should be WRAPPERS ONLY
that display output and limited input functionality

Current wrapper/runtime plan:

- `Server/Core` is the real reusable application runtime
- `Server/Network` is the higher-layer server/network bridge over `Kernels/Network`
- `Server/Hosting` owns shared host-loop plumbing
- `Server/_Headless` is the canonical dedicated-server shell
- `Server/_Console` and `Server/_Godot` are thin wrappers over the same control surface
- all three wrappers now start the shared movement-world server by default, with `--no-movement-world`/equivalent escape hatches for skeleton operator runs

Current default host tick rate:

- `16 ms` fixed-step by default across `headless`, `console`, and `Godot`
- the ECS world remains host-driven, so this is the current default server/world tick cadence
- network cadence is not locked to a separate default yet; the new network kernel is still pump-driven by higher layers until the fuller server bridge/runtime policy lands
- movement-world session defaults are `5s` auth timeout and `10s` ready keepalive timeout; silent UDP clients are unbound and removed by the shared session host

Current `Server/Network` state:

- runtime/operator snapshots and events from `Server/Core` publish into scoped network streams
- the first ECS-facing bridge now also exists there:
  - ECS world/runtime snapshots
  - dirty component publication into scoped latest-state streams
- typed ECS publish rules now exist there too:
  - latest-state rules can classify into `Realtime` or `Bulk`
  - ordered-event rules can classify into `ReliableEvent` or `Bulk`
- actual chunk/ring-scoped AOI publication now also exists there:
  - AOI latest-state publication over player-centered ring scopes
  - AOI ordered-event publication over the same ring scopes
  - this is the first real gameplay-facing scope path, not just the earlier operator or synthetic scope slices
- the first authoritative movement round-trip slice now also exists there:
  - server-side polling of `MovementInputPacket`
  - ECS-owned authoritative movement application
  - owner-state projection back into `MovementStatePacket`
  - the same narrow slice now also works over the real UDP transport after control-plane handshake
  - `ServerMovementSessionHost` now keeps control-plane handshake and movement on one shared live transport poll path
- this is still the correct join point; `Kernels/ECS` and `Kernels/Network` remain separate

Current recommended gameplay publish policy:

- `Realtime` latest-state:
  - position
  - velocity
  - facing / movement posture
  - other small continuously changing entity-observer state
- `ReliableEvent`:
  - combat hits/results
  - interaction/use events
  - inventory/equipment deltas
  - spawn/despawn/state-transition style events
- `Bulk`:
  - terrain/chunk edits
  - housing/building payloads
  - large world snapshots/manifests
  - other compression-friendly payloads that must not contend with combat/movement

Current scope guidance:

- operator/runtime surfaces stay on operator scopes
- moment-to-moment gameplay replication should prefer player-centered AOI ring scopes
- chunk overlap remains an indexing/maintenance mechanism, not the gameplay definition of visibility

Current network-runtime composition status:

- movement still has its own narrow `ServerMovementSessionHost`
- the first generic stream/runtime composition layer now also exists in `Server/Network`:
  - `ServerNetworkSessionRuntime`
- `ServerMovementSessionHost` now owns:
  - `ConnectionSubscriptionRegistry`
  - `ServerEcsWorldNetworkPublisher`
  - `ServerNetworkSessionRuntime`
- that runtime currently composes:
  - filtered baseline bootstrap
  - baseline ack processing
  - bounded retransmit execution
  - ordered-event delivery
  - latest-state delta delivery
- gameplay/AOI policy still stays outside the runtime:
  - the host (or a higher gameplay layer) decides which subscriptions exist
  - the runtime only consumes that subscription state
- it is not yet the primary live host path for movement/control traffic

See [SERVER_WRAPPER_IMPLEMENTATION_PLAN.md](/d:/MEGA/Godot%20Projects/UltraSim/Server/SERVER_WRAPPER_IMPLEMENTATION_PLAN.md) for the concrete implementation plan and [AGENTS.md](/d:/MEGA/Godot%20Projects/UltraSim/Server/AGENTS.md) for subsystem-specific engineering rules.
