# UI Subsystem Package and Implementation Plan

Purpose: define the concrete package/layout shape for the server-driven UI subsystem and the phased implementation order, so this work can scale like `UltraSim/ECS` and `UltraSim/Network` instead of accreting ad hoc under client-only folders.

Status: architecture/implementation plan with the core pre-game schema flow now largely implemented. The wire/cache model is active in the codebase; this document remains the package-shape and follow-up reference rather than a purely speculative plan.

Implementation snapshot (2026-04-03):
- bundled panel path is implemented
- cold-path definition negotiation/cache flow is implemented
- schema-rendered login / character select / character create / character delete-confirm flows are implemented
- repeater support is implemented
- tracked `ServerUiPanelService` is the real server-side owner
- the next UI-subsystem work is hardening, additional gameplay/admin panels, and trust-boundary polish rather than basic subsystem scaffolding

Related tracked server runtime ownership/lifecycle planning lives in `Docs/SERVER_RUNTIME_ARCHITECTURE_AND_ROADMAP.md`.
Discovery/registry planning for multiplayer server-list consumers lives in `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE.md`.
Tracked `_tmp` break-away sequencing lives in `Docs/TMP_TO_OFFICIAL_SERVER_MIGRATION_CHECKLIST.md`.

---

## 1) Architectural Position

The server-driven UI system is large enough to be its own subsystem.

Do not model it as:

- a client-only feature under `Client.Godot/UI`
- a server-only feature under `UltraSim/Server/Menus`
- a narrowly named bucket such as `CachedMenus`

Model it as:

- a top-level `UltraSim/UI` subsystem for engine-agnostic contracts, schema, validation, and runtime support
- a server-side orchestration layer under `UltraSim/Server/UI`
- a client runtime layer under `UltraSim/Client/Runtime/UI`
- an engine-specific renderer/host layer under `Client.Godot/UI/ServerDriven`

That matches the real responsibilities:

- the schema language is not Godot-specific
- the cache/instance model is not Godot-specific
- the panel lifecycle is not server-only
- only the final renderer and theme resolution are engine-specific

---

## 2) Proposed Package Layout

### 2.1 Top-Level Layout

```text
UltraSim/
- UI/
  - Contracts/
  - Schema/
  - Authoring/
  - Validation/
  - Serialization/
  - Runtime/
- Server/
  - UI/
- Client/
  - Runtime/
    - UI/

Client.Godot/
- UI/
  - ServerDriven/
```

### 2.2 Responsibilities by Package

#### `UltraSim/UI/Contracts/`

Shared engine-agnostic contract types for the subsystem.

Examples:

- `UiPanelInstanceId`
- `UiPanelTypeId`
- `UiPanelCategory`
- `UiPanelPresentationMode`
- `UiPanelFlags`
- `UiPanelCloseReason`
- `UiAckStatus`
- `UiPanelOpenCommand`
- `UiPanelUpdateCommand`
- `UiPanelCloseCommand`
- `UiPanelDefinition`
- `UiPanelAck`
- `UiPanelResponse`
- shared bundled panel ids/constants

Rule:

- if a type participates in the protocol, instance lifecycle, or shared panel semantics, it belongs here, not in `UltraSim/Client/Runtime/ClientPresentationContracts.cs`

#### `UltraSim/UI/Schema/`

Schema AST and schema-semantic types.

Examples:

- `UiPanelSchema`
- `UiSchemaNode`
- layout node records
- content node records
- layer node records
- position/inset/size/offset structs
- binding path representation
- predicate representation

Rule:

- this package defines what a schema is, not how it is authored fluently and not how Godot renders it

#### `UltraSim/UI/Authoring/`

Server-side-friendly authoring surface that compiles to the schema AST.

Examples:

- `UiSchema`
- `UiSchemaBuilder`
- `PanelBuilder`
- `RowBuilder`
- `ColumnsBuilder`
- composite helpers like `StatRow`, `LabeledValue`, `LevelDisplay`

Rule:

- this is authoring sugar; it compiles to `UltraSim/UI/Schema`

#### `UltraSim/UI/Validation/`

Definition validation and safety checks.

Examples:

- schema validator
- predicate validator
- node-count/depth checks
- duplicate-id checks
- binding-path validation
- style-key validation hooks

Rule:

- both bundled definitions and server-delivered definitions should go through the same validator

#### `UltraSim/UI/Serialization/`

Engine-agnostic codecs for schema definitions and model envelopes.

Examples:

- `UiSchemaCodec`
- `UiPanelDefinitionCodec`
- reusable primitives for payload encoding

Rule:

- this package serializes definitions/contracts; it does not know about Godot controls

#### `UltraSim/UI/Runtime/`

Engine-agnostic UI runtime helpers and state-machine logic that are not specific to either client or server.

Examples:

- definition cache key structs
- pending-open state records
- cached-definition metadata
- optional generic predicate evaluator
- repeater action context helpers

Rule:

- this is the subsystem kernel for runtime behavior that both sides can share

#### `UltraSim/Server/UI/`

Server-side panel orchestration and authoring usage.

Examples:

- panel service / panel manager
- panel-instance registry
- server response dispatch
- open/update/close helpers
- bundled-schema catalog registration
- server-side payload codecs per panel/model
- menu/prompt workflows

Rule:

- server gameplay systems should talk to this service, not manually assemble low-level packets everywhere

#### `UltraSim/Client/Runtime/UI/`

Engine-agnostic client runtime state and negotiation flow.

Examples:

- client definition cache coordinator
- pending cold-path open tracking
- instance registry
- ack generation
- update/close handling while definition is pending

Rule:

- keep the cache/negotiation state machine out of Godot scene code as much as possible

#### `Client.Godot/UI/ServerDriven/`

Godot-specific rendering, theming, and hosting.

Examples:

- schema renderer
- panel controller base
- style/theme resolver
- concrete panel host nodes
- Godot cache persistence adapter if needed
- bridge from `UiManager` / `MenuCanvasManager`

Rule:

- this package turns engine-agnostic schema/contracts into actual Godot UI nodes

---

## 3) Concrete File-Level Direction

This does not need to be perfect on day one, but the direction should be explicit.

### 3.1 Move/Refactor Existing UI Contracts

Current UI command types living in:

- `UltraSim/Client/Runtime/ClientPresentationContracts.cs`

should migrate toward:

- `UltraSim/UI/Contracts/UiPanelContracts.cs`

`ClientPresentationContracts.cs` should keep presentation-only concepts such as:

- actor pose
- animation cues
- VFX/SFX cues
- chat bubbles
- nameplates

It should stop owning shared server-driven panel contract types once the new subsystem lands.

### 3.2 Shared Panel Ids

Use:

- `UltraSim/UI/Contracts/SharedUiPanelIds.cs`

or:

- `UltraSim/UI/Contracts/BundledUiPanelIds.cs`

Not:

- `UltraSim/Shared/UI/...`

Reason:

- these ids are part of the UI subsystem contract, not general gameplay shared state

### 3.3 Bundled Definition Catalog

Add a bundled-definition catalog under the UI subsystem, not under Godot-only code.

Suggested location:

- `UltraSim/UI/Runtime/BundledPanelDefinitionCatalog.cs`

Responsibility:

- register bundled schemas by `(UiPanelTypeId, DefinitionRevision)`
- expose lookup for warm-cache bootstrapping

The Godot client may still mirror/cache them in its own runtime, but the canonical bundled-definition source should be engine-agnostic.

---

## 4) Ownership Boundaries

### 4.1 `UltraSim/UI` Owns

- panel/open/update/close/ack/response contracts
- schema AST
- authoring API
- validation rules
- schema serialization
- bundled-definition catalog shape
- cache key semantics
- repeater/tab/pager semantics

### 4.2 `UltraSim/Server/UI` Owns

- when a panel opens
- what payload/model it carries
- how server actions map back to gameplay systems
- how bundled/custom definitions are registered and served

### 4.3 `UltraSim/Client/Runtime/UI` Owns

- whether a definition is cached
- when `NeedDefinition` is sent
- stashing open/update while waiting for definition
- discarding pending state on close
- deciding when the renderer can instantiate

### 4.4 `Client.Godot/UI/ServerDriven` Owns

- rendering the schema into Godot controls
- applying styles/themes
- mapping logical image/style ids to assets via content resolver
- focus wiring into menu systems
- concrete panel node lifecycle under `MenuCanvasManager`

### 4.5 `Client.Godot/UI/MenuCanvasManager.cs` Still Owns

- panel hosting
- z-order
- shared vs exclusive menu policy
- escape priority integration

It should not own:

- schema AST definitions
- cache negotiation policy
- payload codecs for every panel

---

## 5) Recommended Names to Avoid

Avoid these as primary subsystem names:

- `CachedMenus`
- `SchemaMenus`
- `MenuKernel`
- `Gumps`

Reasons:

- too tied to one feature slice
- too tied to old UO terminology
- too narrow once prompts, forms, HUD-like panels, and schema authoring all live here

Preferred naming:

- `UltraSim/UI`
- `UltraSim/Server/UI`
- `UltraSim/Client/Runtime/UI`
- `Client.Godot/UI/ServerDriven`

---

## 6) Phased Implementation Plan

This should land in phases. Do not try to build the full schema authoring language and full renderer in one pass.

### Phase 0: Subsystem Extraction / Contract Cleanup

Goal:

- create the package skeleton and move shared UI contracts into the new subsystem without changing behavior yet

Work:

- add the `UltraSim/UI/...` folders
- inventory `UltraSim/Client/Runtime/ClientPresentationContracts.cs`
- classify each UI-adjacent type as:
  - stays in presentation contracts
  - moves to `UltraSim/UI/Contracts`
  - needs a different permanent home
- move/refactor current panel command types out of `UltraSim/Client/Runtime/ClientPresentationContracts.cs`
- add:
  - `UiPanelCategory`
  - canonical `UiPanelPresentationMode`
  - `DefinitionRevision`
  - `UiPanelFlags`
  - `UiPanelCloseReason`
  - `UiAckStatus`
  - `UiPanelDefinition`
  - `UiPanelAck`
- switch `UiPanelTypeId` to `ushort`
- add shared bundled panel ids

Done when:

- the subsystem has a stable contract home
- the old client-only contract file no longer owns server-driven UI types

### Phase 1: Warm-Cache Bundled Panel Path

Goal:

- prove the system end-to-end with one bundled schema and no cold-path dependency yet

Work:

- add bundled definition catalog
- implement one bundled definition:
  - `MessageBox`
- add engine-agnostic client runtime registry for:
  - active panel instances
  - bundled-definition lookup
- add Godot renderer/controller for the bundled `MessageBox`
- route open/update/close through:
  - client runtime
  - `UiManager`
  - `MenuCanvasManager`
- wire `UiPanelResponse`

Done when:

1. server opens `MessageBox`
2. client resolves bundled definition locally
3. panel renders
4. click returns `UiPanelResponse`
5. server handles response and closes panel

### Phase 2: Cache Negotiation / Cold Path

Goal:

- implement the real cache-miss flow without yet tackling the full rich schema surface

Work:

- add client definition cache keyed by `(UiPanelTypeId, DefinitionRevision)`
- add pending-open tracking
- enforce exact-match revision lookup (`typeId + revision`, never `>=`)
- add `UiPanelAck` send path
- implement:
  - `Ok`
  - `NeedDefinition`
  - `Error`
- implement server serving of `PanelDefinition`
- implement cold-path replay:
  - stash open
  - stash updates
  - cancel on close
- allow late `PanelDefinition` arrival to populate cache without resurrecting a closed/cancelled instance
- if a new `OpenPanel` arrives for an existing `panelInstanceId`, replace any active or pending state for that instance
- optionally show a client-local loading placeholder/spinner; keep this non-blocking and non-protocol
- if a pending panel is effectively exclusive, the placeholder must obey the same exclusivity/input-blocking semantics as the eventual real panel

Done when:

- a panel definition absent from local cache can be requested, received, cached, and rendered cleanly

### Phase 3: Minimal Schema AST + Minimal Renderer

Goal:

- support a small but real schema language, enough for bundled panels and basic server-sent definitions

Minimum node set:

- panel root
- content root
- label/text/value
- action button
- input text
- container/section/row/group
- repeater

Work:

- add `UltraSim/UI/Schema`
- add definition validator
- add schema codec
- add Godot renderer for the minimum node set
- convert bundled `MessageBox`, `SimpleListMenu`, and `TextPrompt` to actual schemas

Done when:

- bundled panels and simple server-sent definitions use the same AST/renderer path

### Phase 4: Rich Authoring Surface

Goal:

- add the schema language features needed to cover real RunUO-style custom screens

Work:

- add authoring builder under `UltraSim/UI/Authoring`
- add:
  - semantic + explicit positioning
  - layers
  - tabs / tab-strip containers
  - columns/groups/headers/footers
  - separators/spacers
  - style refs everywhere
  - binding context with `$root`
  - `visibleWhen` / `enabledWhen`
  - pager / general page navigation
- add repeaters with row action context (`rowIndex`, `rowKey`)
- add composite helpers as authoring sugar only

Done when:

- the `CharInfo` and `PointDistribution` style examples can be authored without dropping to raw node construction

### Phase 5: Server Authoring / Panel Service

Goal:

- make the subsystem usable by real server gameplay systems instead of only proof-path code

Work:

- add `UltraSim/Server/UI` panel service
- define registration/discovery model for:
  - bundled definitions
  - server-authored dynamic definitions
- add typed server model/payload codecs per panel
- add helpers for:
  - open
  - update
  - close
  - action dispatch
- stop using `_tmp` proof glue for panel orchestration

Done when:

- gameplay systems can request a panel via a real server service without manually assembling low-level traffic

### Phase 5.5: Server-Side Custom Binding Scripts (FiveM-style resource loading)

Goal:

- allow server owners to ship custom client-side panel behavior alongside panel definitions,
  without requiring a client binary update — similar to how FiveM allows custom server resources
  to deliver Lua/JS UI scripts to connecting clients

Concept:

- a panel definition's payload currently carries schema AST bytes
- the payload can additionally carry a script block (e.g. Lua via MoonSharp) after the schema bytes
- the script is sandboxed; it receives a restricted API:
  - `panel.openPayload` — read-only bytes from the server's OpenPanel payload
  - `panel.fieldValues` — read-only packed field values the user typed
  - `panel.sendPacket(bytes)` — send opaque bytes through the panel's channel
  - `panel.suppress()` — suppress the normal UiPanelResponse
  - `panel.playSound(id)` — trigger a local sound
  - `panel.applySetting(key, value)` — write a client-local setting
  - `panel.updateLabel(id, text)` — mutate a label in the current panel
- script lives entirely in the definition payload; bump the revision → clients get the new script
  on next cache negotiation, no client binary update needed
- pre-shipped named bindings (login `0xFF02`, payment, etc.) always take priority and cannot be
  overridden by server scripts — the security floor lives in the trusted client binary, not in the
  script runtime

Priority stack (highest to lowest):

1. Pre-shipped named binding registered by typeId at client startup  ← cannot be overridden
2. Server-streamed scripted binding from definition payload           ← sandboxed, arbitrary server
3. Default UiPanelResponse path                                      ← fallback for all other panels

Recommended implementation:

- MoonSharp (Lua for .NET) — embeddable, sandboxable, explicit API surface registration
- script is parsed and cached alongside the definition; definition cache key `(typeId, revision)`
  covers the script version automatically
- script engine is optional at runtime; panels without a script block use the default path

Use cases:

- settings panel: apply graphics/sound settings locally, preview before confirming
- sound preview: play a sample when user adjusts a slider
- file/content picker: initiate a chunked local operation before responding
- any panel where the client needs to do local work before or instead of sending a UiPanelResponse
- NOT appropriate for: login credentials, payment data, or any security-sensitive flow
  (those use pre-shipped named bindings, not server scripts)

Phase gate: after Phase 5 (server authoring / panel service) is stable.

---

### Phase 6: Hardening / Tooling

Goal:

- make the subsystem safe, inspectable, and maintainable

Work:

- cache persistence and eviction policy
- diagnostics/logging for definition lookup and render failures
- dev preview tooling for schema definitions
- validation tooling/tests
- fuzz/limit tests for malformed definitions
- theme fallback diagnostics
- performance profiling for large repeaters and layered panels
- focus/input traversal hardening for dynamic controls (`InputText`, Tab traversal, Enter/Esc priority)
- large-list optimization review (`maxVisible`, paging, later virtualization only if profiling justifies it)

Done when:

- the system is stable enough for broader gameplay usage and future richer panel work

---

## 7) Recommended Near-Term Order

For this repo, the practical near-term order should be:

1. Phase 0
2. Phase 1
3. Phase 2
4. Phase 3

Then pause and validate before committing to the full rich authoring language.

Reason:

- the wire/cache model needs proving first
- a single bundled proof (`MessageBox`) reduces risk
- cold-path negotiation needs proving early
- the full authoring surface is expensive and should rest on a working transport/runtime foundation
- character/account flow should not expand the bundled panel catalog; it should consume the schema path

Do not start with:

- full `CharInfoGump` parity
- full layer/style/predicate implementation
- generic “everything node” renderer

That is too much surface area before the subsystem seam is proven.

---

## 8) Test/Validation Gates by Phase

### After Phase 0

- contracts compile cleanly from new package locations
- no accidental Godot dependency appears in `UltraSim/UI`

### After Phase 1

- bundled `MessageBox` opens/closes end-to-end
- `UiPanelResponse` round-trip works
- `Esc` priority still behaves correctly with server-driven exclusive panels

### After Phase 2

- cold-path `NeedDefinition -> PanelDefinition -> Ok` works
- updates received before definition are correctly stashed
- close-before-definition drops pending state cleanly
- duplicate `OpenPanel` on the same `panelInstanceId` cleanly replaces active or pending instance state
- late `PanelDefinition` is cached without resurrecting a closed instance
- exclusive pending placeholder blocks input the same way the eventual exclusive panel would
- reconnect/session-loss clears active and pending instance state without corrupting the per-server definition cache

### After Phase 3

- bundled schemas and server-sent schemas use one renderer path
- validator rejects malformed definitions safely

### After Phase 4

- `CharInfo`-style and `PointDistribution`-style layout examples can be expressed without runtime hacks
- repeater row actions return stable row context
- tab/page actions remain server-authoritative while still providing immediate visual press feedback

### After Phase 5

- real server systems can open/update/close panels without `_tmp` glue

---

## 9) Relationship to Existing Docs

- `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`
  - defines the wire/cache contract
- `Docs/UI_SCHEMA_AUTHORING_LANGUAGE_SPEC.md`
  - defines the schema authoring surface and semantic node/layout model
- `Docs/SERVER_DRIVEN_UI_SHAPE_EXAMPLES_2026-03-18.md`
  - defines example panel shapes and should evolve to use the richer authoring language
- `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`
  - defines the Godot host/canvas ownership and should treat this subsystem as a first-class architecture slice
- `Docs/SERVER_DRIVEN_UI_IMPLEMENTATION_CHECKLIST_2026-03-18.md`
  - should track the first concrete implementation milestones against this package plan

---

## 10) Summary

The server-driven UI work is now big enough to deserve the same treatment as ECS and Network:

- a top-level subsystem
- clear engine-agnostic core packages
- clean server/client runtime boundaries
- a phased implementation order

The recommended root is:

- `UltraSim/UI`

with server/client integration under:

- `UltraSim/Server/UI`
- `UltraSim/Client/Runtime/UI`
- `Client.Godot/UI/ServerDriven`

That is the cleanest shape for the codebase and the least likely to collapse into Godot-specific drift.
