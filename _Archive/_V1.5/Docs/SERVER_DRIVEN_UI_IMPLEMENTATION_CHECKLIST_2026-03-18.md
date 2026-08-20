# Server-Driven UI Implementation Checklist (2026-03-18)

Status: active implementation checklist. The first bundled-panel and cold-path milestones are complete; use this document to track what is done, what remains, and what still depends on `_tmp` migration.

Companion architecture/package plan:

- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `Docs/UI_SCHEMA_AUTHORING_LANGUAGE_SPEC.md`
- `Docs/SERVER_RUNTIME_ARCHITECTURE_AND_ROADMAP.md`
- `Docs/TMP_TO_OFFICIAL_SERVER_MIGRATION_CHECKLIST.md`

---

## 1) Locked Decisions

- All server-driven panels should use one schema-based system with cache negotiation.
- Client-shipped/common panels are simply warm cache entries, not a separate protocol lane.
- Client-bundled UI should stay limited to `MainMenu`, `ServerSelect`, `Settings`, and generic `MessageBox`.
- Account/character flow panels (`LoginPanel`, `CharacterSelectPanel`, `CharacterCreatePanel`, etc.) should be schema UI, not bundled prefab panels.
- `ClientAppShell` remains client-local only.
- Server-driven panels are siblings under the menu layer, owned by `MenuCanvasManager`.
- Shared/runtime `UiPanelPresentationMode` is the canonical presentation-policy enum.
- Client-local `MenuPresentationMode` should be removed or aliased temporarily, not kept as a second source of truth.
- `UiPanelTypeId` should use `ushort` and the same stable `0xNNNN`-style scheme used elsewhere.
- Definition cache identity is:
  - `UiPanelTypeId`
  - `DefinitionRevision`
- definition cache matches by exact tuple equality, never by `>=`
- `UiPanelOpenCommand` must carry `DefinitionRevision`.
- Open-panel options should collapse into a `UiPanelFlags` bitfield instead of multiple standalone booleans.
- `UiPanelCloseCommand` should use `UiPanelCloseReason : byte`, not `string? Reason`.
- Client -> server panel interaction should use the semantic name `UiPanelResponse`.
- Client definition negotiation should use an ack/status path with at least:
  - `ok`
  - `need_definition`
- Compression and delta updates should be reserved in the contract now, but not implemented in the first slice.
- Panel/model payload decoding belongs inside the panel controller/renderer path for that panel type.
- `MenuCanvasManager` must keep separate state for:
  - local app-shell exclusive screens
  - server-driven exclusive panel instances
  - shared server-driven panels
- a new `OpenPanel` for an existing `UiPanelInstanceId` replaces any active or pending state for that instance
- `Esc` priority must be:
  1. focused text input
  2. top-most server-driven exclusive panel
  3. local in-session options/app-shell flow
  4. local fallback open of the in-session options menu

---

## 1.5) Current Progress

Completed:

- shared panel contracts moved to `UltraSim/UI/Contracts`
- bundled `MessageBox` payload/codec implemented
- client bundled `MessageBox` renderer/host implemented
- client warm-cache `OpenPanel` path implemented
- client cold-path `NeedDefinition -> PanelDefinition` negotiation implemented
- client pending-open/update/close cache state-machine implemented
- transport codec support implemented for:
  - `OpenPanel`
  - `UpdatePanel`
  - `ClosePanel`
  - `PanelDefinition`
  - `UiPanelAck`
  - `UiPanelResponse`
- server-side `ServerUiPanelService` implemented
- server-side `ServerUiInboundDatagramHandler` implemented
- `ServerRuntime` now owns `ServerUiPanelService`
- `LoginPanel` (type `0xFF02`) — first schema-rendered panel in production:
  - server opens with 16-byte challenge nonce in `UiPanelOpenCommand.Payload`
  - `UiSchemaRenderer` renders fields/buttons from schema AST at runtime
  - client `GodotUiCommandConsumer` caches open payload per instance
  - packet-bound action intercepts login submit: client computes SHA-256 chain and sends
    `LoginRequestPacket` (0xE8) instead of a plain `UiPanelResponse`
  - server validates challenge-response via `ServerAccountService.TryLogin()`
  - on failure: server sends `LoginResponsePacket` (0xE9) with new nonce; client updates
    cached challenge for retry without reopening the panel
  - end-to-end tested: account creation, bad credentials, valid login all verified

Current remaining gaps:

- persisted delete-warning preference / settings integration
- broader schema widgets beyond current set (repeater is now implemented)
- more complex post-login gameplay/admin flows as needed by individual servers

---

## 2) First Milestone

Implement one end-to-end server-driven panel type:

- `MessageBox`
  - title
  - body text
  - 1-3 buttons
  - exclusive/modal
  - button click sends `UiPanelResponse` to the server

Status: completed.

Success condition:

1. server opens `MessageBox`
2. client resolves cached definition locally by `UiPanelTypeId + DefinitionRevision`
3. panel appears via `UiManager -> MenuCanvasManager`
4. button click sends action back to server
5. server handles/logs action
6. server closes panel

---

## 3) Shared Contract Work

Status: completed.

Implemented:

- `UiPanelCategory`
- canonical `UiPanelPresentationMode`
- `UiPanelTypeId : ushort`
- `DefinitionRevision`
- `UiPanelFlags`
- `UiPanelCloseReason`
- `UiAckStatus`
- `UiPanelDefinition`
- `UiPanelAck`
- `UiPanelResponse`
- `SharedUiPanelIds`
- transport codec support for open/update/close/definition/ack/response

---

## 4) Client Host / UI Routing Work

- `Client.Godot/UI/UiManager.cs`
  - route server-driven UI commands by `UiPanelCategory`

- `Client.Godot/UI/MenuCanvasManager.cs`
  - add server-driven panel registry by `UiPanelInstanceId`
  - add server-driven exclusive panel stack
  - add shared server-driven panel registry/z-order handling
  - do **not** route server-driven panels through `ClientAppShell`

- Preserve local app shell ownership:
  - `ClientAppShell` stays responsible for:
    - main menu
    - multiplayer connect
    - local in-session options menu
    - settings
    - disconnect/error shell flow

---

## 5) Client Definition Cache / Panel Interface

Status: completed for the bundled `MessageBox` / definition-cache runtime slice.

Implemented:

- client-side definition cache/registry
  - exact key: `(UiPanelTypeId, DefinitionRevision)`
  - bundled definitions can pre-populate the cache
  - late `PanelDefinition` arrivals may populate cache even if the original instance is already gone
  - closed/cancelled instances are not resurrected by late definitions
- engine-agnostic client runtime negotiation/state machine under `UltraSim/Client/Runtime/UI`

- Add server-driven panel contract
  - e.g. `IServerDrivenPanel`
    - `Initialize(UiPanelOpenCommand, IServerDrivenPanelContext)`
    - `ApplyUpdate(UiPanelUpdateCommand)`
    - `PrepareClose(string? reason)`

- Add server-driven panel context
  - e.g. `IServerDrivenPanelContext`
    - `SendResponse(UiPanelInstanceId id, uint actionId, ReadOnlyMemory<byte> payload = default)`
    - `RequestClose(UiPanelInstanceId id)`
    - optional focus helpers if needed

---

## 6) First Panel Definition

Status: completed for `MessageBox`.

Implemented:

- `MessageBox` bundled definition/renderer
- explicit bundled payload contract/codec
- explicit button/action definitions

---

## 7) Networking Work

Status: completed for the current bundled-panel/cold-path slice.

Implemented:

- inbound UI command handling on the client runtime/driver path
- outbound `UiPanelResponse` send path
- outbound panel ack/status send path
- cold-path `NeedDefinition -> PanelDefinition` handling
- session-loss bulk close for active/pending server-driven panel instances

For the first pass:

- cache hit -> instantiate and show
- cache miss -> reserved/next-slice behavior via panel ack `need_definition`
- session loss/reconnect should bulk-close all active and pending server-driven panel instances

---

## 8) Temporary Server Bridge Work

Status: partially superseded.

What changed:

- `_tmp/ECSNetworkIslandSliceNode.cs` is no longer the only proof owner for panel contracts/runtime ownership
- tracked runtime/server pieces now exist under:
  - `UltraSim/Server/`
  - `UltraSim/Server/UI/`
  - `Server.Godot/`

What remains true:

- `_tmp/ECSNetworkIslandSliceNode.cs` still owns a large amount of world/bridge/gameplay orchestration
- final break-away work should now follow `Docs/TMP_TO_OFFICIAL_SERVER_MIGRATION_CHECKLIST.md`

---

## 9) Input / Escape Rules

- If a server-driven exclusive panel is open, `Esc` should target that panel first.
- Local in-session options menu should not open on top of an active server-driven exclusive panel.
- Shared server-driven panels may optionally ignore `Esc` unless they declare a cancel action.
- Focused server-driven `InputText` should consume `Enter`/`Esc` before layer-level fallback handling.
- `Tab` navigation should remain within the focused server-driven panel rather than switching UI layers.

---

## 10) Validation Checklist

Completed validations:

- client receives and displays a server-driven `MessageBox`
- button click sends the expected action id to the server
- server closes the panel cleanly
- cache-hit `OpenPanel` path works cleanly
- duplicate `OpenPanel` with the same `UiPanelInstanceId` replaces active or pending state cleanly
- exact-match revision lookup works; a cached newer revision does not satisfy an older requested revision
- late `PanelDefinition` is cached without reopening a panel that was already closed
- reconnect/session-loss clears active and pending server-driven panel instances cleanly
- `LoginPanel` challenge-response auth flow verified end-to-end:
  - new account creation on first login
  - invalid credentials rejected with new challenge issued for retry
  - valid credentials accepted, panel closed, `EnterGame` sent

Still needs broader validation outside the bundled demo slice:

- `Esc` closes/cancels the server-driven exclusive panel before touching the local in-session options flow
- shared chat/local menu behavior remains correct with server-driven panels present
- no coupling is introduced between server-driven panels and `ClientAppShell`
- richer non-`MessageBox` panel flows work cleanly through tracked server runtime ownership

---

## 11) Explicit Defers

Do **not** implement these in the first slice:

- full dynamic schema renderer
- full cold-path definition negotiation if warm-cache-only is enough for the first proof
- delta patching
- compression
- server-provided executable UI logic
- generalized widget AST runtime
- account/identity/name systems

---

## 12) After First Milestone

Current next steps:

1. persist the delete-warning preference and expose a settings re-enable path
2. continue expanding schema widgets only where needed by real server-driven flows
3. add more complex post-login gameplay/admin panels as required
4. revisit delta updates when real panel payload churn justifies it
