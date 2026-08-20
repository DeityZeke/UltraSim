# UI Schema and Prefab Protocol Spec (Working Draft)

Purpose: define a server-driven UI/gump model that uses one schema system with cache-based definition delivery, while remaining engine-agnostic and compatible with the modular client architecture.

Current host expectation:

- Godot keeps persistent UI canvas categories (`World`, `HUD`, `Menus`, `Debug`) under a long-lived `UILayers` root
- server-driven UI panels are not expected to own those categories directly
- server-driven menu/panel commands should route into the client `UiManager`, which then delegates to the appropriate canvas manager
- projected world UI such as overhead chat, nameplates, markers, and prompts should route into `WorldCanvasManager`
- menu-style panels should ultimately be hosted by `MenuCanvasManager`, which is responsible for:
  - shared vs exclusive menu policy
  - focus/z-order
  - separate exclusive-flow history for:
    - local app-shell screens
    - server-driven panel instances

Status: planning/spec document (not an immediate implementation task; intended to prevent design drift while chunking/AoI work proceeds).

For the concrete package split and implementation order that should sit under this protocol, see:

- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `Docs/UI_SCHEMA_AUTHORING_LANGUAGE_SPEC.md`

---

## 1) Goals

- Support server-driven UI/menu/gump flows (UO/RunUO style).
- Use a **fast schema cache-hit path** when client already has the panel definition.
- Support **cache-miss definition delivery** when client does not have it.
- Cache received panel definitions per server for future fast opens.
- Keep the protocol **engine-agnostic** (no Godot `Control` serialization on the wire).
- Allow custom servers to define UI without requiring client executable code changes.

---

## 2) Non-Goals (Initial)

- Server-provided executable UI scripts/code.
- Full mod/plugin execution sandbox for server-sent code.
- Rich desktop-style arbitrary widget scripting runtime.
- Final asset patching/signing pipeline implementation (design hooks only for now).

---

## 3) Core Model

There is one schema system and one renderer.

The only delivery difference is whether the client already has the definition cached.

1. **Cache Hit (hot path)**
   - Server sends `OpenPanel(typeId, revision, payload, ...)`
   - Client finds cached definition for `(typeId, revision)`
   - Client renders immediately

2. **Cache Miss (cold path)**
   - Server sends `OpenPanel(typeId, revision, payload, ...)`
   - Client does not find cached definition
   - Client acknowledges `need_definition`
   - Server sends `PanelDefinition(typeId, revision, schema)`
   - Client caches the definition, applies the stashed open payload, and renders

Definitions shipped with the client are simply warm cache entries on first boot.

Design rule:
- Server sends **UI schema + model data**, not engine UI controls and not executable code.

Locked implementation decisions for the first server-driven pass:

- first implementation is still the simple `MessageBox` milestone, but it sits on the same schema-cache protocol as later panels
- all server-driven panels use one schema/rendering system
- shared/runtime `UiPanelPresentationMode` is the canonical presentation-policy enum
- client-local `MenuPresentationMode` should not remain a parallel source of truth
- `UiPanelTypeId` should use a stable `ushort`/`0xNNNN` scheme
- definition cache identity should be keyed by:
  - `UiPanelTypeId`
  - `DefinitionRevision`
- `UiPanelOpenCommand` should carry `DefinitionRevision`
- boolean open-panel knobs should collapse into a flags field rather than accumulating separate booleans
- client -> server panel interaction should use `UiPanelResponse` as the canonical semantic name
- `UiPanelCloseCommand` should use a compact `UiPanelCloseReason` enum, not a free-form string
- client definition negotiation should use an ack/status path with at least:
  - `ok`
  - `need_definition`
- reserve, but do not implement yet:
  - compression flags
  - full-model vs delta model updates

---

## 4) Engine-Agnostic UI Schema (Required)

The network/shared representation must be a neutral UI AST/schema, not Godot control objects.

Each UI node should include (conceptually):
- `nodeId`
- `nodeType`
- layout data (position/size/anchors/container info)
- style/theme references (logical IDs)
- node properties (text, image ID, button action ID, etc.)
- children

Example node types (initial set):
- `Panel`
- `Label`
- `Button`
- `Image`
- `Container` (stack/absolute)
- `ScrollRegion`
- `InputText`
- `Checkbox`
- `List` / repeated-item container
- `Tabs` / tab-strip container (future)
- `Pager` / page-navigation control (future)

This list should be capability-gated and versioned.

Design note:

- tab selection and page selection should remain server-driven by default
- client interaction selects a tab or page by sending `UiPanelResponse`
- server updates the model and sends `UpdatePanel`

---

## 5) Definition vs Payload Split (Important)

Separate static structure from runtime data:

- **Definition**
  - layout tree
  - control types
  - style hooks
  - bindings/action definitions

- **Payload**
  - dynamic text
  - rows/items
  - prices/counts/states
  - enabled/disabled flags
  - selection values

Benefits:
- repeated opens become cheap
- server can update data frequently without resending layout
- definitions can be cached and versioned independently

---

## 6) Identity, Versioning, and Revisions

Each panel definition should have:
- `UiPanelTypeId` (logical/persistent panel type identifier)
- `DefinitionRevision`
- optional `schemaVersion` if the schema format itself evolves separately

### 6.1 Cache Match Rule

Client may use a local cached definition only if:
- `UiPanelTypeId` matches
- `DefinitionRevision` matches exactly

Otherwise:
- client acknowledges `need_definition`
- server sends `PanelDefinition`

---

## 7) Server-Scoped Cache (Required)

The client caches panel definitions under a per-server namespace.

## 7.1 Server Identity Keying

Do not key only by `IP:port`.

Use a stable server identity, e.g.:
- server GUID/ID
- + fingerprint/public-key identity (recommended)

Endpoint address can change; server identity should remain stable.

## 7.2 Cache Contents (Initial)

Per-server cache may include:
- panel definitions
- UI icons/theme assets (future, if permitted)
- server-provided presentation metadata (future)

## 7.3 Cache Behavior

Cache hit:
1. Client receives `OpenPanel`
2. Client finds `(UiPanelTypeId, DefinitionRevision)`
3. Client renders immediately
4. Client acknowledges `ok`

Cache miss:
1. Client receives `OpenPanel`
2. Client does not find `(UiPanelTypeId, DefinitionRevision)`
3. Client stashes the open payload for that `panelInstanceId`
4. Client acknowledges `need_definition`
5. Server sends `PanelDefinition`
6. Client validates and caches the definition
7. Client applies the stashed open payload and renders
8. Client acknowledges `ok`

While waiting on a definition:
- `UpdatePanel` messages for that instance should be stashed
- `ClosePanel` should cancel the pending open/update queue for that instance

Exact behavior rules:

- revision matching is exact tuple equality, not `>=`
- if client has `(typeId, rev: 3)` cached and server requests `(typeId, rev: 2)`, that is a cache miss
- a later `PanelDefinition(typeId, revision)` may still be cached even if the original pending instance was already closed
- caching a definition and rendering a pending instance are separate decisions
- a closed/cancelled instance must never be resurrected just because its definition arrives later
- a new `OpenPanel` for an existing `panelInstanceId` replaces any active or pending state for that instance id

## 7.4 Quotas and Eviction

Required controls:
- per-server cache size cap
- global cache size cap
- eviction policy (LRU recommended)
- user-facing “clear cache for server” and “clear all server caches”

---

## 8) Packet/Command Shapes (Conceptual)

Exact packet structs can be defined later in the network module, but the semantics should be fixed now.

### 8.1 Open UI Panel

Server -> Client:
- `panelInstanceId`
- `uiPanelTypeId`
- `category`
- `presentationMode`
- `definitionRevision`
- `flags`
- `payload`

Client behavior:
- check local definition cache for `(uiPanelTypeId, definitionRevision)`
- if found: instantiate/apply payload
- if not found: stash payload and acknowledge `need_definition`
- if the same `panelInstanceId` is already active or pending, replace that instance state with the new open

### 8.2 Panel Definition

Server -> Client:
- `uiPanelTypeId`
- `definitionRevision`
- full UI definition/schema

Client behavior:
- validate schema
- cache definition
- if an open is pending for this panel instance/type+revision, render it immediately
- if no instance is pending anymore, keep the cached definition but do not render anything

### 8.3 Update UI Panel

Server -> Client:
- `panelInstanceId`
- model update mode (`full` or reserved `delta`)
- payload

### 8.4 Close UI Panel

Server -> Client:
- `panelInstanceId`
- `closeReason`

### 8.5 UI Panel Response

Client -> Server:
- `panelInstanceId`
- `actionId`
- arguments/payload (validated)

This should flow through the normal server command/input pipeline (barrier-safe handling), same as other client requests.

Action response semantics:

- action responses may include current interactive field values needed by the server to handle the action
- at minimum this should support:
  - `InputText`
  - `Checkbox`
- repeater-row actions may additionally include:
  - `rowIndex`
  - `rowKey`

### 8.6 Panel Definition Ack / Negotiation

Client -> Server:
- `panelInstanceId`
- `uiPanelTypeId`
- `definitionRevision`
- `status`
  - `ok`
  - `need_definition`
  - reserved future statuses as needed

This replaces the older `RequestDefinition` idea with a simpler cache-negotiation ack.

---

## 8.7) Packet-Bound Actions (Built-In Panels Only)

Not all panel actions use `UiPanelResponse` as their wire path.

Built-in panels MAY declare a **packet binding** for their primary action. When the user
triggers that action, the client fires a specific named `IPacket<T>` through the normal
packet pipeline instead of (or in addition to) a `UiPanelResponse`.

### Why this exists

`UiPanelResponse` carries a generic payload blob validated only at the application level.
For security-sensitive operations — login credentials, payment confirmation, anything where
the server wants the full packet pipeline (rate limiting, typed validation, stats, replay
protection) — routing through a named packet type is strictly safer and more explicit.

A secondary benefit: a server streaming a custom panel definition cannot change what packet
fires on submit for a built-in binding. The binding is part of the **client's compiled
knowledge**, not part of the panel schema. A malicious server can stream a visually similar
panel to collect text via `UiPanelResponse`, but it cannot hijack the secure packet path
that the built-in binding uses.

### Rules

- Packet bindings are declared **only** in built-in (client-compiled) panel implementations.
  A streamed `PanelDefinition` cannot declare or override a packet binding. This is by design.
- In the intended product surface, the only client-bundled/common UI is `MainMenu`, `ServerSelect`,
  `Settings`, and generic `MessageBox`. All server-authored pre-game/gameplay panels should flow
  through streamed schema definitions.

- When a packet-bound action fires, the panel remains open until the server explicitly closes
  it. The server closes the panel after handling the typed packet (success or failure path).

- The server handles the typed packet through the normal inbound pipeline. When done, it
  signals the next step via events or callbacks — not by inspecting panel state directly.

- A panel may have BOTH a packet-bound primary action AND generic `UiPanelResponse` secondary
  actions (e.g., a login panel whose submit fires `LoginRequestPacket` but whose "forgot
  password" link sends a generic response). The two paths are independent.

### Three-tier action model

| Action tier | Submit path | When to use |
|-------------|-------------|-------------|
| Generic | `UiPanelResponse` | Character select, menus, settings, most UI interactions |
| Packet-bound (built-in) | Named `IPacket<T>` → server handler → event/callback | Login, security-sensitive operations |
| Future: scripted | Server-streamed packet type (asset-streaming phase) | Custom server behavior, hot-reload |

### Example: Login panel

The built-in `LoginPanel` implementation:
1. Reads the challenge nonce from the panel payload
2. On submit: hashes `SHA256(hash + ":" + challenge)` client-side
3. Fires `LoginRequestPacket(username, challengeResponse)` — NOT a `UiPanelResponse`
4. Panel stays open with a loading state until server closes it

The server's `LoginRequestPacket` handler:
1. Validates credentials via `ServerAccountService.TryLogin()`
2. On success: closes the login panel, fires `LoginSucceeded` event, flow continues
3. On failure: issues a new challenge, re-opens the login panel with failure reason

The flow service subscribes to `LoginSucceeded` / `LoginFailed` — it does not poll panel
state and does not inspect the packet handler directly.

---

## 9) Client Rendering Model (Engine-Specific, Schema-Driven)

The client UI renderer takes:
- neutral definition/schema
- payload
- content resolver (logical IDs -> engine assets)

and builds engine UI controls dynamically or from a precompiled cached form of that same definition.

For the first implementation:

- panel/model payload decode should happen in the panel controller/renderer path for that panel type/definition
- routing/hosting layers (`UiManager`, `MenuCanvasManager`) should not centrally decode individual panel payload formats

Design rule:
- Network/shared UI schema remains engine-neutral.
- Godot/Unity/etc. UI construction happens in the engine-specific presentation/UI layer only.

Renderer/runtime behavior rules:

- if a cold-path placeholder/spinner is shown, it must inherit the pending panel's presentation/exclusivity semantics
- optimistic visual feedback for tab/page clicks is acceptable, but content selection stays server-authoritative until `UpdatePanel` arrives
- missing styles must fall back to built-in default visuals and log a warning
- missing icons must fall back to a visible placeholder treatment and log a warning
- session loss should be treated as an implicit bulk close of all active and pending server-driven panel instances

---

## 10) Content Resolver and Visual Packs Integration

UI schemas should reference logical IDs for visuals and styling:
- icon/image IDs
- theme/style IDs
- font style IDs (logical)

The client content resolver + pack stack maps those IDs to engine assets.

This supports:
- user visual overrides
- server-scoped cached UI assets (future)
- alternate engine clients using the same logical IDs

---

## 11) Validation and Safety (Must-Have)

Dynamic server-provided UI data is untrusted input and must be validated.

Minimum validation constraints:
- max nodes per schema
- max tree depth
- max text length per field
- max total payload size
- max images/assets referenced per panel
- allowed node/control types only
- allowed action argument sizes/types

Design rules:
- Client does **not** execute server-provided UI code/scripts.
- A streamed `PanelDefinition` cannot declare, override, or intercept a packet binding.
  Packet bindings are compiled into built-in panel implementations only (see §8.7).
- Server-provided panel schemas are untrusted data. Packet-bound actions are trusted
  client code. The two must never share an execution path that server data can influence.
- Only data/schema is accepted.

---

## 12) Authoring and Tooling (Future Direction)

The old `MenuExporter` idea is still useful, but the canonical export target should become:
- neutral UI definition/schema artifact (JSON or binary)
- optional generated constants/wrappers
- optional engine-specific preview helpers

Do not make generated Godot scripts the canonical network artifact.

---

## 13) Implementation Timing (Recommended)

This is **not** a pre-chunking requirement.

Recommended sequence:
1. Minimal client modularity seam
2. Server spatial chunking + AoI
3. Shared fanout optimization
4. Client chunk streaming basics
5. UI schema/definition protocol implementation

Rationale:
- chunking/AoI/fanout are immediate scaling wins
- this spec prevents UI protocol design drift while that work proceeds

### 13.1 First Practical Implementation Slice

Recommended first milestone:

1. server opens a modal `MessageBox` panel
2. client resolves a local cached definition by `UiPanelTypeId + DefinitionRevision`
3. `UiManager` routes it to `MenuCanvasManager`
4. `MenuCanvasManager` hosts it as a server-driven exclusive panel instance
5. panel decodes its own payload and renders
6. button click returns `UiPanelResponse` to the server
7. server logs/handles the action and closes the panel

Design constraints for this slice:

- do not mix server-driven panels into `ClientAppShell`
- do not force server-driven panels into `MenuScreenHost` / `StringName` screen history
- maintain a separate server-driven exclusive panel stack keyed by `UiPanelInstanceId`

### 13.2 Follow-On Shape After First Milestone

After the first `MessageBox` milestone is proven:

1. retain the same `OpenPanel / PanelDefinition / UpdatePanel / ClosePanel` envelope
2. add cache-miss handling end-to-end
3. implement full-model updates first
4. keep delta updates reserved until profiling justifies them

---

## 14) Related Docs

- `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`
- `Docs/CHUNK_TERRAIN_STATICS_IMPLEMENTATION_GUIDE.md`
- `Docs/game_design_summary.md`
- `NETWORK_PRD.md`

