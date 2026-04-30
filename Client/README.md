# Client

The client subsystem is being rebuilt with the same layering rules as the server:

- `Client/Core` for engine-agnostic client runtime state
- `Client/Network` for the client-side bridge over `Kernels/Network`
- `Client/_Godot` as the wrapper/front end

Current focus:

- Godot client shell validation on the rebuilt network/runtime shape
- direct-connect movement/chat/observer smoke against the current server
- restoring old v2 client features cleanly on top of the new runtime boundaries
- preparing the content/cache and server-driven UI schema path for login and character flow

Schema/admin UI planning:

- `UI_SCHEMA_AND_ADMIN_PANEL_PLAN.md` is the current reference for the rebuilt RunUO-style server-driven UI path, future ECS control panel, typed settings controls, cached schema model, and ACC-style pluggable admin pages.

What now exists:

- `Client/Core`
  - input sequencing
  - prediction storage
  - interpolation buffering
  - minimal movement input/state contracts
  - app-shell navigation and direct-connect validation helpers
  - server-browser service snapshot model
  - first content/cache spine primitives:
    - server-specific cache namespace and path policy
    - UI schema cache key/path model
    - server patch manifest validation
    - asset size/SHA-256 validation
    - content resolver trust-class contracts
    - layered content resolver priority stack for built-ins, resource packs, server patches, and future mods
    - first cache metadata/quota eviction planning hook
    - persisted cache metadata index store with safe JSON round-trip validation
    - cache eviction executor that deletes files only under the cache root
    - UI schema cache byte store with hash validation and metadata updates
    - server patch manifest cache store with validation, identity checks, and metadata updates
    - server patch asset cache byte store with manifest size/hash validation
  - first server-driven UI runtime bridge primitives:
    - panel registry over the engine-agnostic UI kernel cache
    - cold-open schema validation/cache insert path
    - hot-open schema lookup and model validation path
    - explicit missing/superseded schema and catalog statuses
    - open panel instances retain their validation compatibility context for action/update routing
    - action request validation against open panel instances before network send
    - state-binding resolver registry stub for future entity/inventory/party/session cache adapters
- `Client/Network`
  - movement input send path over `Kernels/Network`
  - authoritative movement-state receive path
  - prediction acknowledgement hook
  - correction-magnitude tracking against predicted owner state
  - loopback-testable movement bridge
  - `ClientMovementSessionHost` for shared control-plane + movement polling over one live transport path
  - observer latest-state/event consume path
  - chat event bridge
- `Client/_Godot`
  - root scene and runtime root
  - first Godot content/cache service adapter over `Client/Core/Content`
  - persistent world/map/dynamic/effects/camera containers
  - persistent world/HUD/menu/debug UI layers
  - preview actor, local controller, orbit camera, nameplate/overhead-chat projection
  - local app shell, manual server browser row, direct connect, in-session menu, chat panel
  - initial UDP session driver wiring for movement, observer state, chat, ping/status, and disconnect

Current benchmark note:

- the first client loopback movement scenario now exists in `_Benchmarks/results/network-benchmarks.json`
- current snapshot:
  - `ClientMovementLoopback_1024Inputs`: `0.913 ms`, `1.22 MB`
  - `ServerMovementLoopback_1024Inputs`: `2.176 ms`, `3.43 MB`
  - `UdpMovementRoundTrip_1024Inputs`: `15.431 ms`, `4.32 MB`
  - `UdpMovementMiniRamp_32Clients_240Ticks`: `108.954 ms`, `14.46 MB`

The current client/network checkpoint is now beyond narrow loopback only:

- real UDP socket transport exists
- the live session path keeps handshake and movement on one shared poll loop
- the first small multi-client UDP ramp signal exists without putting protocol/runtime logic into `Client/_Godot`
- the widened live harness now also exposes owner-correction sample counts and correction magnitudes for the first real owner path

The old v2 client under `_OLD` remains the main behavioral reference for these features, but not the architecture template. The older v1 tree under `_OLD/_OLD` is reference-only; consult it only for specific concepts worth rescuing, such as the hybrid render/culling experiment or the old admin control panel shape.

## Godot Smoke Hooks

The Godot shells have small command-line hooks for repeatable G3 smoke validation:

```bash
# Terminal 1: start the Godot movement server shell and quit after 15s.
Godot_v4.6.2-stable_mono_linux.x86_64 --headless --path Server/_Godot \
  res://Scenes/ServerShellRoot.tscn -- \
  --us-server-start --us-server-port=42042 --us-smoke-quit-after=15

# Terminal 2: direct-connect the client shell and quit after 8s.
Godot_v4.6.2-stable_mono_linux.x86_64 --headless --path Client/_Godot \
  res://Scenes/ClientRoot.tscn -- \
  --us-connect=127.0.0.1:42042 --us-smoke-quit-after=8 --us-smoke-require-connected
```

A passing client smoke prints `Connected.` before shutdown. With `--us-smoke-require-connected`, the client exits nonzero if it never reaches the ready session state; `--us-smoke-require-connected-count=n` verifies reconnect loops. Add `--us-smoke-disconnect-after=seconds --us-smoke-require-disconnected` to prove the local disconnect path runs before quit. Add `--us-smoke-reconnect-after=seconds` and `--us-smoke-final-disconnect-after=seconds` to validate same-process disconnect/reconnect/clean-final-disconnect. Add `--us-smoke-require-session-failed` to assert expected failure cases like no server listening; `--us-smoke-session-failed-contains=text` can pin the reported failure text. Add `--us-smoke-require-content-cache` to prove the Godot content/cache adapter initialized; `--us-content-cache-root=path` overrides its default `user://UltraSimCache` root for disposable smokes. Add `--us-smoke-chat-after=seconds --us-smoke-require-chat-received` to send a chat line through the live session and fail if the echoed chat event does not return to the Godot shell; use `--us-smoke-chat-text=text` to override the default message. Add `--us-smoke-move-after=seconds --us-smoke-require-owner-movement` to drive deterministic movement input and fail if no acknowledged owner movement state returns; `--us-smoke-move-duration=seconds`, `--us-smoke-move-x=n`, and `--us-smoke-move-y=n` tune the movement sample. The client direct-connect driver retries the UDP handshake 5 times at 0.5s intervals, then reports a connection error instead of hanging. Smokes can tune this with `--us-connect-max-handshake-attempts=n` and `--us-connect-handshake-retry-seconds=seconds`. After a ready session, explicit server shutdown notices show immediately; missing server pongs for 5s are treated as crash/loss fallback, tunable with `--us-connect-ready-keepalive-timeout-seconds=seconds`. In both cases the shell clears session-owned world state, returns preview controls, and shows the error screen. Local client disconnects now send an explicit close notice so the movement server can unbind and despawn the session immediately instead of waiting for keepalive timeout. The Godot movement server shell also uses session auth/keepalive timeouts so silent UDP clients are unbound and removed server-side. Add `--us-smoke-require-empty-sessions` to server smokes to fail if a test exits while movement sessions are still bound; `--us-smoke-empty-session-grace=seconds` lets the smoke wait through the disconnect cleanup window before failing. These hooks are for validation only; user-facing connection flow remains the local app shell.
