
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
- graceful movement-world shutdown sends explicit server-disconnect control notices before closing transport
- clean movement client exits send explicit client-disconnect control notices so the shared movement host unbinds/despawns immediately
- selected character sessions snapshot their current movement position on disconnect before the entity is unbound/despawned, then restore that snapshot on the next world entry
- Godot server smokes can use `--us-smoke-require-empty-sessions` to fail if active movement sessions remain bound at smoke quit, with `--us-smoke-empty-session-grace=seconds` to wait through disconnect cleanup

ModernUO/ServUO movement lessons that still apply to the UltraSim path:

- movement remains server-authoritative; client prediction is presentation only
- the Godot client sends movement immediately on start/change/stop/jump, then
  falls back to a `60 ms` unchanged-held-input heartbeat (`~16.67 Hz`)
- the server movement bridge rejects unknown input flags and movement axes
  outside `[-100, 100]`, coalesces same-poll bursts to the newest valid intent,
  and counts the discarded packet attempts for operator visibility
- speed-hack prevention belongs at the movement-intent/application point: clamp
  and normalize movement vectors so the entity cannot exceed what its current
  movement state can physically produce for the tick window
- movement request queuing and validation order should stay deterministic so
  smoothness fixes do not become client trust

Current foundational/server-platform task list:

- access/permission rule registry for runtime-added roles, grants, denies, and later account attachments
- package-facing server settings editor widgets and apply/restart routing over the shared settings registry/facade
- separate live ECS control panel for systems, system tuning, entity discovery/modding, accounting views, items, and in-world admin tooling
- parallel persistence participant snapshot fan-out once save payload preparation becomes heavy
- region/rule framework when terrain and world regions become concrete
- health/status presentation expansion from the current overhead-bar hook into real health components and peeled-off status panels

Current server settings foundation:

- `ServerSettingsRegistry` stores ordered, key-addressable setting descriptors for bootstrap/auth/storage/discovery/operator settings
- descriptors carry a stable key, display name, value text, apply mode (`ApplyWithoutRestart` or `RestartRequired`), and editor metadata
- editor metadata identifies text, integer, decimal, boolean, path, and choice/dropdown settings; choice settings declare their allowed values up front
- `ServerSettingValueValidator` validates and normalizes edited values by editor kind before any config persistence or apply/restart routing sees them
- `ServerSettingsChangeSetValidator` validates batches of proposed edits, rejects duplicate/unknown keys, and returns normalized changes with their apply mode
- the Godot server Settings tab renders from the shared descriptor inventory and is intentionally separate from the future live ECS control panel
- editing widgets, persistence to config files, restart/apply routing, and package-fed setting registration are planned layers on top of this shared registry

Current chat command prefix policy:

- `ServerMovementWorldApplicationOptions.CommandPrefix` owns the live gameplay/chat command declaration string.
- The default is `/`, but server owners can set one alternate prefix such as `[`, `.`, `#`, or `command:`.
- The live movement host registers built-in and script commands under that one configured prefix.
- The command registry can still support extra prefixes internally, especially unprefixed operator-console commands, but normal chat should not advertise multiple command declaration symbols.

Current access/permission foundation:

- `ServerAccessRuleRegistry` stores named, runtime-replaceable rules.
- `ServerAccessRuleDescriptor` carries display name, description, grants, and denies.
- Permission keys are normalized strings such as `admin.entity.inspect`, `command.spawn`, or wildcard families such as `admin.entity.*`.
- Denies win over grants, direct subject denies win before rule denies, and legacy integer `AccessLevel` remains available as a bridge for current command paths.
- `ServerCommandRegistry` now evaluates command permission keys when constructed with a `ServerAccessRuleRegistry`.
- Commands default to `command.<name>` permission keys, can still declare a legacy minimum access level, and can be overridden with direct grants/denies on the command request.
- Accounts now persist access rule keys plus direct permission grants and denies.
- The movement host attaches selected-account access metadata to chat command requests when accounting is available.
- Command dispatch audit entries now capture handled command outcomes, sender/session context, permission key, status, and error text.
- `AccountAccessAdministrationService` is the trusted mutation path for replacing account access metadata and emits account access audit entries.
- UI editing for access rules, grants, and denies is intentionally a later layer.

Current admin inspection foundation:

- `Kernels/ECS` owns raw read-only entity/component/field snapshots through `World.TryInspectEntity`.
- `Server/Network` owns the server-facing `ServerEntityInspectionService` facade.
- The facade evaluates an optional `ServerAccessEvaluationRequest` before reading the ECS snapshot.
- `ServerAdminPermissionKeys.EntityInspect` is the first named admin permission key and maps to `admin.entity.inspect`.
- Future `[props]` commands and schema admin panels should call this facade rather than reaching directly into ECS internals.

Current spawner primitive foundation:

- `ServerSpawnerComponent` stores enabled/admin-visible/smart-activation flags, radius, max active count, respawn cadence, and a stable spawn-set key hash.
- `ServerSpawnerRuntimeState` stores active count and last/next spawn ticks.
- `ServerSpawnerRegistry` resolves stable spawn-set keys and hashes to admin-readable metadata.
- ECS components intentionally store the stable `ulong` hash instead of strings; admin panels and commands resolve display text through the registry.
- Actual spawn execution, live content group mount/unmount, smart player-proximity activation, and spawner persistence remain later slices.

Current runtime content group foundation:

- `UltraSimRuntimeContentGroupRegistry` tracks named package-like units and whether they are mounted.
- content group descriptors carry kind, display name, description, origin path, and version.
- `IUltraSimScriptContext.ContentGroups` exposes registration/mount/unmount to script packages.
- mounted groups can register unmount actions, which run in reverse order and must succeed before the group is marked unmounted.
- script commands can opt into content-group ownership through `Commands.RegisterForContentGroup`, which removes those commands from the script context when the group unmounts.
- script UI panel providers can opt into content-group ownership through `UiPanels.RegisterPanelProviderForContentGroup`, which removes those providers from the script context when the group unmounts.
- script prefab/archetype definitions can opt into content-group ownership through `Definitions.RegisterPrefabForContentGroup` and `Definitions.RegisterArchetypeForContentGroup`; archetype removal is blocked while any remaining prefab references it.
- the stock Admission package registers `packages/admission` as a mounted script package.
- spawn-set registries still need to attach their own unregister actions to this lifecycle layer once spawn execution exists.

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
  - movement input validation rejects impossible axes/unknown flags before ECS application
  - same-poll movement bursts are coalesced to the latest valid input, preserving the newest direction while dropping spam
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
