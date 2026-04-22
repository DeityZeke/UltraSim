
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

Current default host tick rate:

- `16 ms` fixed-step by default across `headless`, `console`, and `Godot`
- the ECS world remains host-driven, so this is the current default server/world tick cadence
- network cadence is not locked to a separate default yet; the new network kernel is still pump-driven by higher layers until the fuller server bridge/runtime policy lands

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

See [SERVER_WRAPPER_IMPLEMENTATION_PLAN.md](/d:/MEGA/Godot%20Projects/UltraSim/Server/SERVER_WRAPPER_IMPLEMENTATION_PLAN.md) for the concrete implementation plan and [AGENTS.md](/d:/MEGA/Godot%20Projects/UltraSim/Server/AGENTS.md) for subsystem-specific engineering rules.
