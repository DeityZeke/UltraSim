# UI Schema And Admin Panel Plan

Purpose: define the rebuilt server-driven UI/schema system before implementation starts.

The target is a RunUO-gump-like server authoring experience with a more robust engine-neutral schema underneath. The server should be able to build login screens, character flow, vendors, crafting panels, inventories, admin tools, and future ECS control panels without shipping executable client code or hand-writing Godot UI for every feature.

This document extends the old v2 UI/schema work with lessons from:

- old v2 schema UI under `_OLD/UltraSim/UI`, `_OLD/Client.Godot/UI/ServerDriven`, and `_OLD/Server.Godot/UI`
- old v1 ECS control panel under `_OLD/_OLD/Client/ECS/ControlPanel`
- old v1 ECS settings under `_OLD/_OLD/UltraSim/ECS/Settings`
- ACC-style RunUO admin shell under `/home/nathan/Downloads/Servers/Nagashizar 2.0/Scripts/Customs/ACC`
- current ECS hooks in `Kernels/ECS/Systems/SystemSettings.cs` and `SystemControlDescriptor.cs`

## Goals

- Keep Godot thin: Godot renders schema and handles focus, but protocol and UI state-machine semantics live in shared/client runtime code.
- Keep server authoring simple: normal panels should read like compact builder code, not a raw AST.
- Keep wire payloads compact: schema is cached; repeated opens and live updates send only model payloads or deltas.
- Keep UI safe: server sends schema/data, never executable client UI code.
- Support dense admin/debug panels: tables, repeaters, collapsible groups, tabs, and live telemetry are first-class requirements.
- Support future ECS control panel work: ECS systems should expose typed descriptors, and the UI layer should render them generically.

## Non-Goals

- Do not port old fixed-position RunUO gump code directly.
- Do not let server content patches ship executable client scripts.
- Do not make login credentials flow through generic `UiPanelResponse` if a trusted packet-bound path is required.
- Do not collapse local app shell screens and server-driven panels into one system.
- Do not replace current ECS `SystemSettings` hooks with a parallel v1-only settings system.

## Ownership Boundaries

`ClientAppShell` owns client-local app flow:

- start menu
- server browser/direct connect
- local settings
- in-session options
- disconnect/error screens

Server-driven schema panels are sibling windows under `MenuCanvasManager`:

- login and character panels
- vendors, crafting, inventory, journal, trainers
- admin/game-master panels
- ECS/system/debug panels
- server-specific gameplay menus

`DebugCanvasManager` can host client-local debug overlays, but server-driven admin windows should still use the schema path when they are server-authoritative.

## Guaranteed Panel Set

UltraSim should keep a small guaranteed panel/screen vocabulary so the client and
server do not invent one-off shells for common flows.

### Client-Local Built-Ins

These must work with no server connection and should remain local Godot/client
screens rather than server-authored schema panels:

- `StartMenu`
  - based on old v2 `StartMenuScreen`
  - launch screen, preview/local mode, server browser, direct connect, settings, quit
- `ServerBrowser`
  - based on old v2 `MultiplayerMenuScreen`, but with clearer tab semantics
  - server/provider/category tabs behave like browser tabs over one stable list/detail workspace
  - tabs should not feel like unrelated category pages that rebuild the whole menu
  - direct connect can be an inline modal/pane within this screen
- `ClientSettings`
  - based on old v2 `SettingsMenuScreen`
  - local video/audio/input/UI scale/nameplate/chat/HUD/debug/content-cache settings
- `PauseOptions`
  - based on old v2 `PauseMenuScreen`
  - resume, settings, disconnect/return, and later return-to-character-select when that flow exists
- `SessionStatus`
  - based on old v2 `SessionStatusScreen`
  - connecting, retry, edit connection, disconnected, kicked, handshake failure
- `LocalMessage`
  - based on old v2 `MessageMenuScreen`
  - client-only message/error panel for offline or pre-server states
- `AssetPackManager`
  - new required local panel
  - installed packs, server-required packs, enable/disable order, cache status, and clear-cache tools
- `ChatFeedInput`
  - local persistent chat feed/input, not a server-driven schema panel for now
  - should be registered as a replaceable client surface/provider, not hardwired forever
  - core ships the fallback feed/input; a richer server package can replace the provider with a schema-backed or hybrid chat package

The current placeholder `ClientAppShell` can be evolved toward this v2 screen
set. It does not need to copy v2 scene files directly, but it should preserve
the stronger separation v2 had between start, multiplayer/server browser,
settings, pause, message, and session-status screens.

### Replaceable Core Surfaces

Some built-in UI surfaces should be default implementations, not permanent final
implementations. Chat is the first expected example:

```text
chat.surface = CoreChatFeedInput
scripts/Packages/Chat compiles
chat.surface = RichChatPackageSurface
```

This must behave like assignment or priority-based replacement, not event
subscription. A package should not `+=` another chat window and accidentally
show both the core chat panel and the rich package panel. The runtime registry
should expose one active provider per replaceable surface key, with origin and
priority/override diagnostics so admins can see which package currently owns
`chat.surface`.

Replacement should be reversible. If the rich chat package is removed, disabled,
or fails to load, the client falls back to the core chat feed/input unless the
operator explicitly disables chat UI. Chat visibility should be controlled by
settings such as:

- chat window/feed enabled
- overhead/world chat enabled
- channel tabs enabled
- rich chat package enabled

This keeps the compiled core usable by itself while allowing a future
Knives-style chat package to supersede it cleanly.

### Standard Server-Driven Helpers

These should be pre-authored helpers/builders available to server code and
scripts:

- `MessageBox(title, body, buttonText)`
- `ConfirmBox(title, body, acceptText, cancelText)`
- `ChoiceBox(title, body, choices)`
- `TextPrompt(title, body, placeholder, acceptText, cancelText)`
- `PagedListPicker(title, rows, actions)`
- `TabbedSettingsPanel(...)`

These helpers compile to normal schema/model/action documents. They are not
special renderer-only panels. They exist to keep common server scripts compact
and consistent.

### UO-Style Quality-Of-Life Targets

TazUO and the broader UO client ecosystem are useful validation for which UI
surfaces players actually value. These should be implemented as schema/HUD/world
helpers over the existing protocol, not one-off Godot widgets:

- `HealthBar(entityId)` as a peelable, state-bound helper with configurable
  color thresholds and stale-state fallback.
- `CooldownBar(actionId)` or `CooldownBar(entityId, actionId)` using server
  start/duration state and local interpolation.
- `SkillProgressBar(skillId)` bound to replicated skill/mastery progress.
- rich item comparison tooltips that bind both the hovered item and the
  currently equipped item for the matching slot.
- journal/chat filters by channel, message kind, source entity, and keyword or
  regex.
- nameplate/world-label options, including optional health percentage/health
  line display when server visibility policy allows it.
- `NearbyLootList` as a state-bound HUD/menu helper for lootable corpses or
  containers in interaction range.
- controller/gamepad focus traversal as a first-class focus mode requirement,
  not a late renderer-only patch.

## Presentation Runtime Shape

The old v2 client already used the right broad idea:

- one shared UI protocol/runtime path for server-driven panels
- Godot canvas managers for world, HUD, menu, and debug surfaces
- a `MenuCanvasManager` that hosted both local app-shell screens and server-driven panel instances
- panel metadata for category, shared/exclusive presentation, bring-to-front, session-loss persistence, and gameplay-input blocking

Keep that architecture, but make the policy less implicit this time.

Backend/runtime concepts:

- one panel protocol
- one schema/cache/action path
- panel metadata declares surface/layer target, category, focus mode, exclusive group, z-order class, input blocking, and close/back behavior

Godot presentation can then mount panels into separate child hosts without creating separate backend systems:

- shared menu panels: chat, inventory, character sheet, bank, vendor, crafting, normal admin inspectors
- modal panels: confirmation prompts, short blocking choices
- exclusive overlays: settings/options, pause menu, login, character select/create, reconnect/error screens
- HUD/world/debug surfaces as separate categories where appropriate

Opening an exclusive overlay may hide, dim, or disable the shared menu host underneath, but shared menu panel state should survive unless the server or local app shell explicitly closes it.

Schema authors should be able to declare the intended surface with an API like `.Layer("menu")`, `.Layer("hud")`, `.Layer("world")`, `.Layer("tooltip")`, or `.Layer("debug")`. The exact Godot host is renderer policy, but the authoring/runtime metadata should be explicit so a tooltip, world nameplate, health bar, and inventory window do not all compete in the same z-order pile.

Recommended surface targets:

- `World`: object-anchored UI such as name tags, interact prompts, damage numbers, click/right-clickable labels, and "hold Ctrl to reveal labels" overlays
- `Hud`: persistent gameplay HUD such as player bars, hotbar, minimap, target frame, party frames
- `Menu`: default server-driven windows such as inventory, character sheet, crafting, vendors, bank, journal, admin inspectors
- `Modal`: confirmations and short blocking choices above menus
- `Exclusive`: settings/options, login/admission, character select/create, reconnect/error, full-screen blocking flows
- `Tooltip`: transient hover/focus tooltip surface that should render above menu/modal/exclusive content unless explicitly suppressed
- `Debug`: developer/admin overlays that should stay above normal gameplay UI when enabled

Surface target and focus mode are related but separate. A tooltip can be on the `Tooltip` surface with no focus. A world name tag can be on the `World` surface but still be clickable/right-clickable. A settings menu can be on the `Exclusive` surface and block lower menu interaction. This separation keeps layering predictable without forking the schema protocol.

Exclusive and modal panels should be able to declare tooltip policy, such as allow, suppress, or suppress-external-tooltips. Login, character creation, disconnect/error, and other clean full-screen flows can suppress tooltip overlays while active instead of relying on ad-hoc renderer exceptions.

## Cold And Hot Paths

The schema path has two normal modes. Catalog-backed panels may also reference a separate catalog cache identity; schema and catalog misses are independent.

Cold path:

1. Server opens a panel with server identity, panel type, revision, schema hash, optional catalog identity, and model payload.
2. Client does not have the exact schema cached.
3. Server sends or responds with the full schema definition.
4. Client validates and caches the schema under the server-specific cache namespace.
5. If the panel references a missing catalog, client requests or receives that catalog, validates it, and caches it separately.
6. Client renders it with the model payload once required definitions are available.

Hot path:

1. Server opens a panel with the same schema identity, same catalog identity where applicable, and model payload or model delta.
2. Client has exact cached schema/catalog matches.
3. Client validates payload shape against the cached schema and catalog metadata.
4. Client renders or updates without receiving the full schema or catalog again.

Kernel contract:

- `UiPanelOpenDocument` is the open envelope for both cold and hot paths.
- Cold opens carry an inline `UiSchemaDocument` plus model payload and validate the model against that schema before render/cache use.
- Hot opens carry only schema/catalog identities plus model payload and validate against the cached `UiSchemaCompatibilityContext`.
- The open envelope carries exact optional catalog identity, but catalog bytes remain a separate cache/document concern.
- `UiDefinitionCache` is the first in-memory cache contract/prototype. It stores validated schema entries with immutable compatibility contexts, tracks catalog identities separately, and reports `Hit`, `Miss`, or `Superseded` without returning stale entries as usable hot contexts.

Schema cache identity must include:

- stable server identity
- panel type id
- schema revision
- schema hash/version
- UI protocol/schema version

The client must not accept a "close enough" schema. A mismatch is a cache miss or a protocol error, not a best-effort render.

Supersession is explicit. If the cache has the same server/panel/protocol family but a different schema revision/hash, lookup should report superseded and force the bridge into a fresh cold-open/request path. It must not hand out the old compatibility context. Catalog identity supersession follows the same rule for same server/catalog/protocol/locale families.

Identity matching and structural compatibility are separate checks:

- identity matching answers "is this model/update for the exact schema/catalog instance I have cached?"
- structural compatibility answers "do these values fit the payload slots declared by that already-validated schema?"

The runtime should validate a schema once, create a reusable compatibility context from it, and then apply hot model/delta compatibility checks against that context. Do not re-walk the full schema tree for every slot patch, telemetry batch, or small server-forced refresh.

Compatibility contexts are immutable and are created from validated schema entries. Cache entries own the reusable source context, and open panel instances must retain the exact context they validated against for their own lifetime. That means cache eviction or schema supersession cannot strand an already-open panel with no validation context. Panel instances own their instance state, current model, sequence counters, focus/navigation state, retained compatibility context, and subscriptions.

Catalog-backed opens that arrive before the required catalog identity is available should return an explicit missing-catalog state, not create a half-open panel instance. The bridge can request the catalog and retry the open when it arrives; timeout/UX handling belongs to the pending-panel bridge layer, not to a hidden registry entry.

Cold path UX must be explicit. The first open of a panel type can require schema transfer, validation, cache write, and then render. For startup-critical panels such as login and character select, prefer proactive schema push during the early session/admission flow once the server knows the client UI protocol version. If the schema is not available yet, the client should show a trusted local loading/pending panel with the same modal/input-blocking behavior as the eventual panel.

Pending exclusive panels occupy their exclusive group before the real server panel is renderable. For example, if login closes and character select has not arrived yet, the client should show a trusted local `SessionAdmission` loading panel that blocks gameplay input and reserves the `SessionAdmission` exclusive slot until the server opens login, character select, character create, or a disconnect/error panel.

## Layered Authoring Model

The implementation should have three layers.

### Low-Level Schema

The low-level schema is the compact, versioned, engine-neutral AST that goes over the wire and into the cache.

It should stay boring:

- stable node type ids
- stable slot ids
- explicit action ids on action-capable nodes such as buttons
- compact primitive values
- bounded strings and arrays
- explicit validation
- no client-executed script blocks

### Builder API

The server-facing builder should be friendly and terse. Example target style:

```csharp
Panel("Movement")
    .Toggle("Enabled", movement.Enabled)
    .Slider("Speed Multiplier", movement.Speed, min: 0, max: 10, step: 0.1)
    .Button("Reset Stats", ResetStats)
    .Table("Systems", rows);
```

The builder resolves nice API calls into the lower-level schema nodes, slot manifests, action ids, and model bindings.

Prefer nested lambda builders for containers instead of scope-closing `.End()` chains. The source shape should make hierarchy obvious and hard to misuse:

```csharp
Panel("Characters", b =>
{
    b.Column(col =>
    {
        col.DynamicText("name");
        col.Row(row =>
        {
            row.Button("play", "Play", Buttons.Play);
            row.Button("delete", "Delete", Buttons.Delete);
        });
    });
});
```

Avoid APIs where forgetting one `End()` silently compiles the wrong schema tree. If an imperative builder is still useful internally, the public authoring API should validate container balance and produce clear diagnostics.

### Context And Domain Builders

RunUO's crafting system is a useful reference, but the exact pattern is more specific than "every later add attaches to the last thing." In `CraftSystem`, a normal craft entry is registered with `AddCraft(...)`. Complex entries capture the returned item index, then call `AddRes(index, ...)`, `AddSkill(index, ...)`, `AddRecipe(index, ...)`, or `SetNeededExpansion(index, ...)` against that explicit handle. Its more stateful pattern is used for selectable material families: `SetSubRes(...)` defines the active resource selector, then `AddSubRes(...)` appends choices to that selector.

That split is worth adopting:

- use nested lambdas for actual UI layout hierarchy
- use explicit handles or scoped lambdas for domain catalogs such as crafting recipes, vendors, character creation options, skill/stat pages, and admin setting groups
- avoid hidden sticky global context where `AddMaterial(...)` silently mutates whichever recipe happened to be last
- allow short-lived context builders inside a bounded scope, with validation that rejects calls made outside the right scope

A scoped lambda parameter is the preferred explicit handle. In `cat.Recipe("Dagger", ..., r => { ... })`, `r` has the same conceptual role as RunUO's returned craft item index, but it is safer because it only exists inside that recipe's scope. You cannot accidentally attach a material to the previously-created recipe after the scope exits.

Catalog builders are intentionally flatter than layout builders. Layout builders have depth because UI has visual hierarchy: columns contain rows, rows contain buttons, tabs contain pages. A recipe, vendor entry, character option, or setting descriptor should mostly be a flat property surface on a bounded object: `Skill(...)`, `Material(...)`, `RequiresRecipe(...)`, `Price(...)`, `Permission(...)`, `Tooltip(...)`. The compiler can later render that catalog into tables, detail panels, or paged menus without making the author describe every visual node manually.

Good target style for catalog-like authoring:

```csharp
CraftingPanel("Blacksmithy", b =>
{
    b.Category("Weapons", cat =>
    {
        cat.Recipe("Dagger", typeof(Dagger), r =>
        {
            r.Skill(SkillName.Blacksmith, min: 0.0, max: 49.6);
            r.Material(typeof(IronIngot), amount: 3);
        });

        cat.Recipe("Gemmed Circlet", typeof(GemmedCirclet), r =>
        {
            r.Skill(SkillName.Blacksmith, min: 75.0, max: 125.0);
            r.Material(typeof(IronIngot), amount: 6);
            r.Material(typeof(Tourmaline), amount: 1);
            r.Material(typeof(Amethyst), amount: 1);
            r.RequiresRecipe(RecipeIds.GemmedCirclet);
        });
    });

    b.ResourceSelector(typeof(IronIngot), "Metal", res =>
    {
        res.Option(typeof(IronIngot), "Iron", requiredSkill: 0.0);
        res.Option(typeof(DullCopperIngot), "Dull Copper", requiredSkill: 65.0);
        res.Option(typeof(ValoriteIngot), "Valorite", requiredSkill: 99.0);
    });
});
```

This keeps dense RunUO-style authoring ergonomics while preserving clear ownership: layout builders build node trees, catalog builders build data-backed menus, and the lower schema compiler turns both into compact panel schemas, manifests, action ids, and payload slots.

### Catalog Data Cache

Catalog-heavy panels need a cache tier in addition to schema caching.

Examples:

- crafting recipe lists
- vendor inventories and service catalogs
- character creation options
- skill/stat progression tables
- admin page/provider registries

These datasets change much less frequently than per-open model state, but they can be too large to resend on every panel open. A blacksmithing catalog can include many categories, recipes, skill ranges, material costs, recipe ids, expansion/permission gates, and display metadata.

Use three separate concepts:

- `Schema`: panel structure, node tree, bindings, action ids
- `Catalog`: mostly-static domain data rendered by the panel
- `Model`: per-open/per-session runtime values, selections, counts, dirty flags, permission-filtered state

Catalog cache identity should include:

- stable server identity
- catalog type id
- catalog revision/hash
- UI/catalog protocol version
- locale/content variant when relevant

Opening a catalog-backed panel should normally send schema identity, catalog identity, and the small model payload. If the client lacks the catalog, it requests or receives the catalog once and caches it under the server namespace. Very large catalogs may support paged catalog fetch later, but the first implementation should prefer whole-catalog caching over streaming unless a real size problem appears.

Worked example: crafting panel.

Schema:

- declares a category list bound to catalog path `craft.categories`
- declares a recipe repeater bound to catalog path `craft.recipes`
- uses stable row keys such as `recipeId`, not transient array indexes, whenever practical
- declares model overlays for dynamic state such as known/unknown, craftable, selected, material availability, cooldown, and button enabled state
- declares actions such as `SelectCategory`, `SelectRecipe`, `CraftSelected`, `SelectResourceOption`, and `Back`

Catalog:

- categories: `categoryId`, display text/icon, sort order
- recipes: `recipeId`, `categoryId`, result item id, display text/icon, skill requirements, material requirements, required recipe id, permission/expansion gates
- resource selectors: selector id, base resource type, resource options, required skill, display metadata

Model:

- selected category id
- selected recipe id
- selected resource option ids
- visible/filter/page state
- known recipe ids or compact known bitset
- craftable recipe ids or compact craftable bitset
- available material counts
- per-selected-recipe validation/result message

The renderer joins schema, catalog, and model by stable ids. It should not need the server to resend every recipe row just because material counts changed. A model delta can update `availableMaterials`, selected ids, craftable bits, or result text while the cached catalog remains unchanged.

Worked example: stat distribution panel without catalog.

Schema:

- declares static labels and value slots for strength, dexterity, intelligence, level, experience, stat points, and skill points
- declares conditional groups for player-only skill rows, pet skill pages, and increase buttons
- declares buttons such as `IncreaseStrength`, `IncreaseDexterity`, `IncreaseSkill`, `PreviousSkillPage`, `NextSkillPage`, and `Close`
- declares stable row keys for skill rows such as `skillId`, not display position

Model:

- target entity id/name/type
- current stat values and caps
- current point pools
- current skill page and skill rows
- booleans for `canIncreaseStrength`, `canIncreaseDexterity`, `canIncreaseSkill`
- validation/status text for the last attempted action

There is no catalog in this shape. The server opens the panel with schema identity and model payload only. If points are spent or the page changes, the server sends a model delta for the affected stat fields, button enabled states, and visible skill page. Catalog identity is optional, not a requirement for every panel.

Human NPCs should be treated as the same base actor shape as player characters for UI/schema purposes. A player is the specifically controlled human mobile, not a fundamentally different stat/skill/progression model. Character sheets, stat distribution panels, need/status panels, health/experience bars, inventory/equipment views, animation tags, and admin inspectors should usually target a generic human mobile/entity binding and then apply permission/control policy for whether the viewer may edit, command, inspect, or only observe. This keeps followers, companions, vendors, citizens, guards, and player characters on one shared schema vocabulary instead of splitting "player UI" and "NPC UI" into parallel systems.

Entity-backed scripted content should also stay aligned with ECS prefabs. A script-defined `iron_dagger` should usually be a concrete prefab/default-value registration for a composed archetype chain such as `BaseItem > BaseWeapon > BaseMeleeWeapon > BaseBladedWeapon`, not a separate script-only object model. That chain is additive component composition and value overriding, not OOP inheritance. UI helpers and admin panels should be able to inspect and render the resulting prefab/archetype metadata generically: item definitions, mobile definitions, equipment slots, stats, needs, abilities, presentation tags, and rule hooks should be exposed through shared descriptors where possible.

Timed effects and other temporary states should follow the same descriptor model. A broad `WeaponState` component can hold common effect fields when systems inspect a weapon only during use. If a system needs to query all affected entities, the effect should have a distinct component/archetype boundary such as `TimedEffectState`, `PoisonState`, `BurningState`, or `AlignmentState`. UI/status panels should consume those component descriptors and state bindings generically, so an effect timer bar, poisoned-item tooltip, or admin "all timed effects" table does not require a one-off protocol.

Future live content builders should also use this same metadata path. An admin item builder should be able to select presentation assets, tags, components, attributes, recipe data, and default values, then save a versioned data prefab that registers into the normal server prefab catalog. That is different from generating C# scripts: the UI edits data-authored prefab descriptors backed by existing registered component types and archetype descriptors. A builder may compose a generated archetype signature from existing components when no current shape fits, but new component types or executable behavior still require scripts/plugins/core code.

Admin UI should show definition origin and editability. A script-authored prefab may be inspectable but require script recompilation or restart to change; a data-authored prefab can be live-added, live-disabled, revised, rolled back, or erased after server-side validation. Commands and gameplay systems should resolve both through the same prefab registry, so `/add my_new_item` does not care whether the definition came from a C# script, a built-in catalog, a content pack, or an in-game builder save.

The builder UI should reflect server-side authoring rules: duplicate prefab keys are hard errors, only registered client/server assets should be selectable, assets can declare compatible base archetype categories, and edit mode should be protected by a server-held edit lease so two staff users do not overwrite each other. Save should report validation, dangling-reference, collision, and stale-revision diagnostics directly in the panel.

Future ModernUO-style serialization generation should feed this metadata path by
annotating ECS component fields, not entity classes. Generated component
serializers, migration records, command/admin edit descriptors, and tooltip
invalidation hints can make admin inspectors and live builders richer, but the
UI still consumes descriptors and model values. It should not know about
concrete C# component types or generated serializer methods.

### Composite Helpers

Composite helpers are authoring sugar only. They should not become new protocol primitives unless they need unique renderer behavior.

Examples:

- `SettingsGroup(...)`
- `SystemTable(...)`
- `EntityInspector(...)`
- `ConfirmButton(...)`
- `Pager(...)`
- `AdminShell(...)`

### Semantic Roles And Defaults

Most gameplay panels should not be full manual construction.

Builders should offer semantic roles, tags, and defaults for common gameplay patterns, then let authors override details when needed. The common path should feel like declaring intent, not assembling every row and margin by hand.

Examples:

- `CharacterSelectList(...)`
- `StatBlock(...)`
- `SkillPage(...)`
- `InventoryGrid(...)`
- `InventoryList(...)`
- `ContainerCanvas(...)`
- `VendorList(...)`
- `CraftingCatalog(...)`
- `HealthBar(...)`
- `ResourceBar(...)`
- `ConfirmAction(...)`
- `SettingsGroup(...)`

These helpers can choose default layout, density, labels, icons, tooltips, empty states, focus behavior, common actions, and common state bindings. Authors should be able to override text, style role, permissions, action handlers, row templates, fallback behavior, or layout constraints without abandoning the helper.

Semantic helpers are shared vocabulary, not one-off local shortcuts. They should be defined and owned centrally so helper-generated schema shapes, slot names, row keys, action ids, catalog paths, and state binding conventions do not drift between panels or mods. Server-side customs and future mods can add new helpers or override helper templates through explicit registration, but the built-in helpers should remain stable reference implementations.

Use full node construction for unusual panels, dense admin/debug tools, or one-off layouts. Use semantic helpers for normal gameplay UI. This should cover most menus while preserving flexibility for RunUO-style custom panels.

### Inventory Architecture And Viewers

Design reference: `Kernels/Inventory/README.md`.

Inventory must be separated into two layers:

- inventory/container truth: server-owned item ids, container ids, quantities, equipment state, item metadata, permissions, position metadata, and mutation rules
- inventory viewers: menu shells that present and manipulate that truth in a particular style

The project should ship with the D2-style grid inventory as the primary gameplay viewer, but the schema foundation should not bake that one visual metaphor into the underlying inventory contract. The same server inventory state should be adaptable to different viewers when the data model supports it.

The shared item/container model should be viewer-agnostic. Item row keys, container ids, stack quantities, permissions, metadata, and placement records should not assume list, grid, or freeform presentation. Viewer-specific fields can be optional placement facets: a grid viewer may use cell, width, height, and rotation; a freeform viewer may use x/y and draw order; a list viewer may ignore placement and use category/sort metadata. UI-1 should define the common contract first, then let each viewer declare which facets it consumes.

Useful viewer shapes:

- Skyrim-style list inventory:
  - categories, filters, sort columns, item rows, detail pane
  - mostly a repeater/table plus selection model
  - good reference case for "simple row resolver" inventory
- D2-style grid inventory:
  - fixed grid cells, item width/height, optional rotation, equipment slots
  - drag from one slot/cell to another
  - server validates all moves and returns affected item/container deltas
  - this is the intended default product inventory
- UO-style freeform container:
  - bounded 2D container surface with item `x/y` placement and draw order
  - overlapping items may be allowed
  - drag/drop arbitrary placement, stack split/merge, nested containers, item context menus
  - useful extension/reference case for customs or alternative server rules

The viewer is not the authority. Drag/drop, equip, split, merge, move-to-bank, move-to-vendor, and take-from-container actions should submit compact intents with stable item/container ids and proposed placement metadata. The server validates permissions, capacity, collisions, range, ownership, locks, trade/vendor rules, and any anti-cheat constraints, then sends model deltas for the affected containers.

Shared containers need live deltas. A corpse, chest, bank box, trade window, or auction listing can change because another player or server process acted. Viewers must handle row/item add, update, remove, and replace deltas without assuming the local action caused the change.

Trade windows are a special shared-container shape. They are two-session shared views with two player-owned offer sides, real-time row deltas to both participants, lock/unlock state, and a two-party commit. Each player has write authority over their own offer side and read authority over both sides. The server must validate both sides at commit time and prevent unilateral completion.

Auction listings should not be treated as ordinary cached catalogs. They look catalog-like, but listing availability, prices, bids, expiration, and seller actions can change continuously. The auction panel should be modeled as a live/polled repeater or paged table with server-forced row deltas, not as a static catalog that stays valid for a long session.

This does not mean UI-1 must implement every viewer. It means the contracts should avoid coupling "inventory" to one renderer. A good staged approach is:

1. Define the underlying inventory/container slot and action conventions once.
2. Implement the D2 grid viewer for the game.
3. Keep a simple Skyrim-style list viewer as a low-cost reference/test shell.
4. Treat UO-style freeform containers as a later extension shell, adding a dedicated container-canvas renderer primitive only when needed.

Repeated patterns found while building those viewers should move down into shared helpers, descriptors, and validation hooks. Viewer-specific presentation should stay in the viewer helper.

### World Interaction Companion Panels

Some features should not be forced entirely into schema UI.

Housing/building is the clearest example. The menu side can still be schema-driven:

- building piece catalog
- categories, filters, costs, and required tools/materials
- rotate/snap/grid toggles
- undo/redo/history controls
- commit/cancel
- permission, ownership, and placement error text

After the player picks a piece, the client should enter a world placement context. That context owns the in-world ghost preview, cursor/raycast targeting, snap feedback, and local visual affordances. The server still owns validation and commits. The client submits compact placement intents; the server validates land/house permissions, collisions, support, cost, range, locks, and rule constraints, then returns success/failure and any world/container/model deltas.

This keeps the schema system responsible for menus and control surfaces while leaving spatial editing to world interaction code. A companion schema panel can stay open beside the world placement mode, but the generic schema renderer should not become a full 2D/3D building editor.

## Core Schema Nodes

Initial or early nodes:

- `Panel`
- `Text`
- `DynamicText`
- `Button`
- `IconButton`
- `Image`
- `Layer`
- `Separator`
- `Spacer`
- `Row`
- `Column`
- `Group`
- `Section`
- `ScrollRegion`
- `TextInput`
- `NumberInput`
- `Toggle`
- `CheckboxGroup`
- `RadioGroup`
- `Select`
- `Slider`
- `Stepper`
- `Repeater`
- `Table`
- `Tabs`
- `Pager`
- `ContextMenu`
- `RadialMenu`
- `InventoryGrid`
- `ContainerCanvas`

The first implementation can stage these, but the schema id space should reserve room for them. In particular, ECS/admin work needs more than text, buttons, and repeaters.

Do not make high-level concepts such as `AdminShell` a renderer-level node type. An admin shell is a composition pattern built from generic nodes such as panel, nav rail/sidebar, tabs, page host, status bar, tables, and groups. The renderer should not need to know what an ECS admin shell is.

`InventoryGrid` and `ContainerCanvas` are different from helpers such as `AdminShell`. They may need renderer-level behavior because they involve hit testing, drag/drop previews, placement rules, and dense item surfaces. Keep the underlying inventory action/model contract generic, but allow specialized renderer primitives where interaction semantics cannot be expressed cleanly as ordinary rows and buttons.

Reserving a node id does not mean implementing the renderer immediately. `InventoryGrid` is expected for the default product inventory. `ContainerCanvas` should be reserved in the schema id space so the contract can grow cleanly, but the UO-style freeform renderer can wait until a real feature or reference shell needs it.

`ContextMenu` and `RadialMenu` are interaction surfaces, not long-lived panel shells. They should use the same action/permission/validation model as panels, but their presentation lifetime is usually short and anchored to an item, entity, world position, or selected UI row.

Context/radial descriptors are not normal panel instances. They are short-lived action surfaces rendered from `UiActionMenuDescriptor`: menu key, presentation kind, context kind/id, close policy, and compact server-authored actions. The client may render them above ordinary menu content, but selecting an action still routes the declared action id and explicit context back through the server action path. They should not own model state, row deltas, or hidden panel lifetime.

## Tooltips, Context Menus, And Interaction Wheels

Tooltips should be first-class metadata on nodes, rows, cells, items, actions, and settings.

Tooltip levels:

- simple text: a short label or explanation
- rich tooltip: title, body lines, icon, rarity/quality role, stat lines, requirement lines, warning/error lines
- state-bound tooltip: tooltip fields that update from authorized item/entity/session state
- delayed/advanced tooltip: compact on hover, expanded when holding a modifier or using a controller detail action

Tooltips are presentation only. They can explain permissions, requirements, item stats, cooldowns, and errors, but the server must still validate all actions.

Rich tooltips should stay flat. They are not a nested mini-schema and should not contain buttons, child panels, arbitrary layout trees, action ids, or executable behavior. The contract is title, icon, quality/visual role, body/stat/requirement/warning/error/flavor/advanced lines, and optional slot-backed values. If a surface needs buttons, tabs, forms, context actions, or complex conditional layout, it is a context menu, popover, or panel, not a tooltip.

Dynamic tooltip values should reference existing payload slots or runtime state binding slots. A tooltip should not invent its own hidden state channel. Optional advanced/detail text belongs in passive advanced tooltip lines. Anything that needs to request more data, open details, or execute behavior should use a context action, popover, or real panel open.

Context menus cover UO-style right-click menus, item action menus, row action menus, entity interaction menus, and admin/debug action menus. They should be built from compact action menu descriptors:

```text
MenuKey
PresentationKind: context menu or radial menu
ContextKind/ContextId
CloseOnFocusLost
Actions[]
```

Each action entry stays compact:

```text
ActionId
Label/Icon
Role: normal, primary, destructive, disabled, debug
Availability/permission hint
RequiresConfirm
Optional child menu key
Optional group/category key
Optional row/item/entity context
```

The menu can be generated in three ways:

- static schema-declared actions, such as fixed row actions in a table
- model-provided actions, such as item-specific verbs for a selected inventory item
- request-on-open actions, where the client asks the server for the current context actions for an item/entity/world target

Request-on-open is important for world/entity interactions because available verbs can depend on distance, ownership, faction, combat state, permissions, cooldowns, target state, and equipped tools. The client may show cached/local likely actions for responsiveness, but executing an action always goes through the server.

Radial menus are a presentation variant of context menus. The Sims-style interaction wheel should use the same action descriptor model, but group actions by category and allow nested/depth menus:

- Talk
- Trade
- Party
- Pet/Follower
- Combat
- Admin
- Debug
- More

Nested radial menus should avoid hidden global state. Each depth level carries explicit context: target entity id, selected category/action path, world position if relevant, and client sequence. The server can return a refreshed action set when entering a nested category so the wheel does not show stale verbs.

Context/radial menu open behavior:

- item/menu-row context: anchored near the row/item/cursor
- world/entity context: anchored near cursor or target, with optional screen-space fallback
- controller mode: centered radial or selected-target action wheel
- modal behavior should be configurable; most context menus close when focus is lost, but confirmation submenus can be modal

Common edge cases:

- target disappears while menu is open: close or refresh with unavailable state
- target remains visible but the player moves out of interaction range: refresh/disable range-sensitive actions where possible; otherwise the server rejects execution with a reason and the client shows the invalid state
- permission changes while menu is open: server rejects or pushes refreshed actions
- item moves/stack changes while menu is open: update action enabled state or close
- action becomes invalid after click: server returns rejection/status; client should not assume success
- nested menu category becomes empty: show empty state or close that branch

This gives RunUO-style context menus and Sims-style radial interaction wheels the same underlying protocol: compact server-authoritative action descriptors with flexible client presentation.

Hotbar and ability slots are a useful future stress test. A single slot can be a drag/drop target, a context-menu target, a state-bound cooldown/resource display, and a quick action trigger. The architecture should support that convergence through shared action descriptors, state bindings, and drag/drop contracts rather than a special one-off hotbar protocol.

## Conditional Layout

Complex conditional layout is a first-class requirement, not an edge case.

RunUO-style panels frequently include logic such as:

- show stat buttons only when points are available and caps allow an increase
- show player skill sections for players and pet skill sections for creatures
- show entire editor regions only when a row/category/location is selected
- swap page contents based on selected tab/page/state

Simple `VisibleSlot` / `EnabledSlot` bindings are useful, but they are not enough by themselves.

The schema system should support two explicit strategies:

1. Conditional groups/subtrees:
   - a container can bind visibility/enabled state to a boolean payload slot
   - hidden subtrees do not participate in focus traversal
   - hidden subtrees may preserve client-local input state only when the server marks them as state-preserving
   - validators still count hidden subtree nodes against schema limits
2. Variant schemas:
   - when layout shape is materially different, the server may open a different schema revision/type/variant
   - variants must have explicit cache identity
   - variants should be used for major mode changes, not tiny button visibility changes

Guidance:

- use conditional groups for small to medium subtree visibility
- use tab/page model updates for content page changes
- use variant schemas when the panel's structural shape changes enough that keeping all branches in one schema would be wasteful or hard to validate

## Layout Model

Support both semantic and explicit layout.

Semantic layout:

- rows
- columns
- grouped sections
- headers
- footers
- tables
- tabs
- pagers
- scroll regions

Explicit layout:

- bounded absolute coordinates for RunUO-style dense panels
- anchored regions
- fixed-size compact windows
- controlled overlays

The default should be semantic layout. Absolute positioning is useful for dense custom server panels, but should be bounded and validated.

Fixed/absolute layouts should be capability-gated. They are most likely to look broken across aspect ratios, DPI settings, and theme changes. A server should have to opt into fixed layout explicitly, and the client/operator should be able to reject or warn on fixed-layout panels from untrusted servers.

Mechanically, fixed layout should require all of:

- a schema declaration such as `RequiresCapability.FixedLayoutV1`
- bounded panel dimensions and max coordinate extents
- renderer validation that all fixed nodes fit inside declared bounds
- a client/server capability negotiation bit before the server sends the panel
- a local trust policy that can allow, warn, or reject fixed layouts per server

Capability exchange is a connection/session-level concern, not something each panel invents independently. During admission or UI protocol negotiation, the client should advertise supported UI protocol versions, schema node families, renderer limits, and optional capabilities such as `FixedLayoutV1`. The server should only open panels that fit the negotiated capability set.

Trusted built-in panels can use fixed layout without warning. Remote server-driven fixed layouts should be opt-in, because a bad fixed layout can make controls inaccessible even when it is not malicious.

## Typed Settings Controls

Old v1 ECS settings exposed:

- bool
- int
- float
- enum
- string
- choice string
- button/action
- tooltip
- min/max/step for numeric values
- read-only/editable distinction
- value change hooks
- per-system persistence

The new schema should represent that as a generic descriptor:

```text
settingId
label
type
value
scope
readOnly
tooltip
group
min/max/step
format
options
requiresRestart
dirty
persisted
permission
```

Setting scopes:

- `ClientLocal`
- `ServerRuntime`
- `WorldPersistent`
- `Session`
- `DebugOnly`

The current ECS already has:

- `SystemBase.Settings`
- `SystemBase.ControlDescriptors`
- `SystemSettings.Apply(ref SystemSchedule)`
- `SystemControlDescriptor`
- `SystemControlKind`
- `SystemManager.ApplySettings`

Do not replace these. Grow them into the schema-facing descriptor model.

Important implementation rule: do not add a parallel UI-only system-control vocabulary because the current ECS descriptor is missing fields. Extend `SystemControlDescriptor` / related ECS-side types instead, then bridge them into schema. The schema layer should reflect ECS's own control vocabulary, not drift away from it.

## Admin Shell Model

The ACC package is a good conceptual reference. It had:

- one root command/control panel
- registered systems
- a selected system page
- system enable/disable
- save/load lifecycle
- delegated draw and response handling
- per-system page state via `ACCGumpParams`

The rebuilt version should become a typed admin shell:

```text
AdminShell
- navigation rail or tabs
- current page/system
- access checks
- common header/status
- system enable/disable
- dirty/save/revert controls
- result/toast/status feedback
- hosted page content
```

Each hosted page should come from a descriptor/provider:

```text
PanelId
Title
Category
Icon
RequiredAccess
BuildSchema
BuildModel
HandleAction
Optional Enable/Disable
Optional Save/Load
Optional TelemetrySubscription
```

This lets future systems contribute pages without hardcoding one giant admin window.

The admin shell itself should be authored as a reusable builder/helper over generic schema nodes. It should not require a special renderer mode.

## Page State

Server-owned page state is mandatory.

Examples:

- selected system
- selected entity
- selected component
- selected category/location
- active tab
- page index
- sort column
- filter text
- expanded row keys

The client can remember local presentation preferences such as window position and collapsed state, but authoritative selection and mutation state should remain server-owned for server panels.

## Panel Composition And Navigation

Server-driven panels need explicit composition rules.

There are two normal navigation shapes:

- in-shell navigation: an existing shell swaps selected page, tab, row, or page model state
- cross-panel navigation: an action opens another panel instance, such as entity inspector, item details, recipe details, bank box, or vendor buy/sell panel

In-shell navigation should stay inside the same panel instance when the surrounding shell remains stable. This is the normal path for admin pages, tabs, pagers, and selected rows.

Cross-panel navigation should create or focus a distinct panel instance when the new surface has its own lifetime, placement, focus, permissions, or close behavior. Examples: opening an entity inspector from the admin shell, opening item details from inventory, opening a vendor subpanel, or opening a confirmation dialog.

Navigation authority rule: for server-owned panels, the server decides what Back, tab changes, page changes, row selection, and close actually mean. The client may render a back button or maintain display-only focus order, but it must not locally mutate server-owned navigation state as the source of truth.

For in-shell navigation, the client sends an action intent such as `Back`, `SelectTab`, `SelectPage`, or `SelectRow`. The server recomputes the target page/region state and responds with one of:

- a scoped model delta for the affected page/tab/region
- a full model update for the same panel instance
- an open/close command when navigation crosses panel-instance boundaries
- a rejection/status update if the action is no longer valid

This keeps back/page/tab navigation fresh without forcing full close/reopen churn. It also avoids stale nested data: the destination region is refreshed explicitly by the server as part of the navigation response.

Panel open messages should be able to declare:

- `ParentPanelInstanceId`
- `NavigationOriginActionId`
- `OpenPolicy`: new, replace-current, focus-existing, or singleton-by-key
- `BackPolicy`: send-action, close-and-focus-parent, replace-with-parent-state, none
- `PlacementHint`: cascade, near-parent, centered, restore-client-position

The client presentation stack is display-only for server-owned panels. It can decide which panel appears on top or where a back affordance is drawn, but the actual navigation result must come from the server's response.

## Server-Driven Model Refresh

The server may refresh an open panel even when the user did not press a button.

Examples:

- character select list changes because a character was created, deleted, renamed, or updated elsewhere
- bank, vendor, or inventory contents change while the panel is open
- crafting availability changes because materials were consumed or gained
- admin inspector target changes due to simulation/network state
- permissions change while a panel is open

Refresh options should stay simple:

- full model update for the panel instance
- scoped model delta for a named page/tab/region
- repeater/table row add, update, remove, or replace
- open/close command if the panel is no longer valid

Use stable row keys for additive deltas. For character select, a newly-created character can be sent as a row-add delta keyed by `characterId`; a deleted character can be a row-remove delta; a major account refresh can still use a full model update. The protocol should support deltas, but builders should be allowed to choose full updates when the list is small or the simpler path is clearer.

Schema and catalog supersession during a live session must be explicit:

- if a schema revision changes while an instance using the old schema is open, the server should either keep that old instance valid until it closes or send a normal close/reopen flow for the new revision
- if a catalog revision changes while an open panel references the old catalog, the server should send a catalog-superseded/open-refresh flow, or close and reopen the panel with the new catalog identity
- clients should not keep applying deltas against old catalog row keys after the server has declared the catalog superseded
- if the server cannot safely migrate open page state, closing/reopening is preferred over silently ignoring later responses

The first implementation can use close/reopen for schema or catalog supersession. More granular live catalog replacement can be added later if content hot-reload needs it.

## Panel Lifetime

Panel instances have both client-side and server-side lifetime.

Client-side session loss:

- close or invalidate all server-owned panels
- clear pending schema requests
- clear focused server-owned input
- return input routing to local shell/preview state
- show trusted local disconnect/error UI when appropriate

Server-side session loss:

- close all open panel instances for that session
- release server-side page state and any temporary resources
- discard unsubmitted input fields
- discard dirty-but-unapplied preview state unless the panel explicitly committed it already
- leave already-applied settings/actions intact
- write audit/log entries for interrupted admin operations when useful

Admin/settings panels should distinguish:

- previewed but unapplied values
- applied runtime values
- persisted saved values

A disconnect must not leave a half-owned server-side panel object or a hidden pending mutation.

## Repeaters, Tables, And Inspectors

Repeaters need:

- stable row keys
- row action context
- row index
- selection state
- optional paging cursor
- optional sort metadata
- optional empty state

Tables need:

- compact column descriptors
- cell slot descriptors
- sortable columns where server supports it
- row selection
- row actions
- dense mode for admin panels

The table data path should remain the same row-delta path used by repeaters. A `Table` node declares a stable row-backed slot id, while schema-level table column descriptors declare ordered cell slot ids, labels, value kinds, and optional sort action ids. The model/update stream sends row keys plus per-row cell values; it does not resend column metadata. This keeps high-churn admin lists compact while preserving enough schema metadata for dense renderers and sortable headers.

Future ECS panels will need:

- systems table
- archetypes table
- entities table
- components table
- sessions/connections table
- network lanes/families table
- log/event table

Current ECS foundation:

- `World.TryInspectEntity`/`World.InspectEntity` return read-only snapshots with component ids, component type names, value previews, and public field values.
- Higher-level admin panels should consume those snapshots through `ServerEntityInspectionService`, which wraps them with server access checks and clean denied/not-found statuses.
- Editing remains deliberately out of scope for this first layer; mutation needs permissions, validation, audit logging, and typed command handlers.

## Live Updates And Telemetry

The old v1 control panel updated:

- FPS
- frame time
- ECS tick time
- memory
- entity count
- archetype count
- per-system average timing
- per-system enabled state

The new schema should support live model updates without rebuilding the full panel.

Requirements:

- full payload update
- partial slot update
- repeater row add/remove/update
- telemetry refresh interval hint
- client throttling
- server push or poll compatibility
- stale data indication

Telemetry should be declarative. The renderer should not know what an ECS system is.

Server-side push discipline is required. Do not let each telemetry-producing system push tiny updates independently on every ECS tick. Use a telemetry scheduler/service that:

- batches dirty telemetry slots across open panels
- emits at a configured interval, such as 100-250 ms for admin panels
- coalesces multiple writes to the same slot
- respects panel visibility and permissions
- can downgrade or pause telemetry when the client is overloaded
- sends one compact delta message per session/panel batch where possible

## Runtime State Bindings

Some UI surfaces should update from normal replicated/session state, not from explicit `UiPanelUpdate` messages.

Examples:

- UO-style peeled-off health/status bars
- target frame
- party frames
- selected entity inspection summary
- compact pet/follower status windows
- local player stat/resource widgets
- inventory item count badges when backed by replicated inventory state

These panels should be able to declare state bindings. The schema defines which slots can bind to state, and the model provides the binding target:

```text
slot: healthCurrent
source: EntityState
target: entityId 12345
field: Health.Current
fallback:
  whenMissing: stale
  whenUnauthorized: hidden
  whenDead: stale
  staleAfterMs: 1500
```

When the normal network/session runtime receives a state update for that entity/session/object, the client-side UI runtime invalidates and refreshes the bound slots automatically. The server does not need to send a separate menu update just to move a health bar from 78% to 72%.

The UI runtime should not reach directly into arbitrary gameplay internals. Put a small resolver interface between schema UI and gameplay/network caches:

```text
IUiStateBindingResolver
- TryResolve(bindingTarget, field, out value)
- CanBind(bindingTarget, field)
- Subscribe(bindingTarget, field, callback)
- GetStaleState(bindingTarget, field)
```

Implementations can read from client-side entity, inventory, party, session, and local-player state caches. This is an intentional dependency: state-bound panels are UI views over gameplay/session state. The dependency should be narrow and interface-based so the schema renderer remains mostly generic.

The client runtime should own a resolver registry created during client startup/session runtime composition. Resolvers such as `EntityState`, `InventoryState`, `PartyState`, `SessionState`, and `LocalPlayerState` register by source id/name. The UI runtime asks the registry for the resolver declared by a binding; it should not service-locate gameplay systems directly from individual panel code.

Engine-agnostic client UI state and intent belongs in `Client/UI/`. That project mirrors the UI kernel at the client-runtime layer: panel instance registry, state binding resolver registry, local model update application, and validated action routing. `Client/Core/` remains shell/superstructure and shared client services. `Client/_Godot/Scripts/UI/` is the renderer bridge that turns validated client UI instances into Godot `Control` trees.

State binding rules:

- bindings are declarative and permission-filtered by the server
- the server should only send bindings for targets the session is allowed to observe
- the client must still enforce binding authorization locally through `CanBind`; invalid or unauthorized bindings fail closed
- state bindings update visual slots, not authoritative game state
- binding targets are per panel instance, not per schema type; multiple health bars using the same schema can subscribe to different entities at the same time
- fallback behavior is per binding, not global to the whole panel
- if the bound target disappears, dies, goes stale, or permission is revoked, the panel follows that binding's declared fallback behavior
- bound slots should have stale indicators or timeout behavior when updates stop
- high-frequency bindings should be throttled/coalesced by the client renderer to frame cadence
- server-owned actions from the panel still go through normal action/response validation

This is distinct from telemetry. Telemetry is admin/debug data intentionally pushed for open panels. Runtime state bindings are UI views over existing gameplay/session state streams. A health bar should update because the entity status cache changed, not because the server sent a bespoke "health bar panel update."

Slot update priority should be deterministic:

1. Explicit server model updates and model deltas win.
2. Telemetry updates apply next for slots declared as telemetry-backed.
3. Runtime state bindings refresh visual values from the latest authorized client cache.

A slot should normally use one update source. If a full model update touches a state-bound slot, the model update is applied first and the binding resumes from the new value/cache state on the next binding refresh. Builders should warn when a slot is accidentally declared as both telemetry-backed and state-bound unless a specific override policy is set.

## Actions And Responses

Responses should include:

- panel instance id
- exact schema identity, and catalog identity when applicable
- action id
- row context if action came from a repeater/table
- current input field values
- selected toggle/radio/select values if needed
- client-side sequence number

Actions can be:

- normal button
- setting preview
- apply setting
- save setting
- save all
- reset/revert
- tab/page selection
- row selection
- destructive command
- context menu action
- radial menu action
- nested context/radial category selection
- tooltip detail request when tooltip data is not already present

Destructive actions should support:

- confirmation prompt
- disabled state
- permission check
- rate limit
- result message

Action descriptors should include server-authored availability, not just client-inferred state. Examples:

- available
- disabled
- stale target
- target missing
- out of range
- unauthorized
- rate limited
- busy

The client may render unavailable actions as disabled or hidden depending on presentation policy, but execution is still server-authoritative. If a radial/context menu is already open and the player moves out of range while browsing nested actions, the server may push refreshed descriptors or reject execution with `OutOfRange`; the client should show the returned invalid state instead of guessing.

Action requests should carry a monotonically increasing client sequence number for that panel/session action stream. Action results must echo the client sequence number so the client can ignore superseded responses. Rejections should carry a machine-readable failure reason such as stale panel, stale target, target missing, out of range, unauthorized, rate limited, validation failed, conflict, or server error, plus an optional display message.

## Validation And Safety

Schema data is untrusted input.

Validate:

- max schema bytes
- max payload bytes
- max nodes
- max tree depth
- max text length
- max input fields
- max repeaters
- max rows per update
- max columns
- max asset references
- allowed node types for client version
- valid slot references
- valid action ids
- valid style keys
- valid row contexts

Security rules:

- client does not execute server-provided UI scripts
- server content patches cannot contain executable UI code
- schema cannot override trusted packet bindings
- login credentials use a trusted packet-bound path if required
- server revalidates every admin action
- permissions are hints for rendering, never trust boundaries

## Trusted Login And Sensitive Inputs

Login and other sensitive credential flows need a precise rule.

If credentials are considered sensitive enough to avoid the generic panel response path, then the schema login panel must not submit username/password as ordinary `UiActionResponse` field values. Use a trusted packet-bound binding instead:

- the panel may render trusted credential fields
- those fields are owned by a client-side trusted login controller
- pressing Login invokes a dedicated login packet path
- the generic panel response may carry only non-sensitive action intent, such as cancel/back
- the server challenge/nonce and retry state stay in the trusted auth flow

Trusted credential controls should be explicit in schema metadata. A normal `TextInput` is eligible to participate in generic field submission. A password/credential input is still rendered as an input node, but it is marked as trusted/sensitive and the generic `UiActionRequestDocument.FieldValues` path must reject that slot. The login controller reads those controls and sends credentials through the dedicated admission/auth packet path instead.

The submit action should be equally explicit. A login submit button uses a trusted credential action context, so the renderer can raise it to the trusted login controller instead of sending an ordinary `UiActionRequestDocument`. Generic UI action requests must reject trusted credential actions even if they contain no field values.

If transport encryption later makes generic field submission acceptable, that should be an explicit design decision and documented as such. Until then, treat password fields as dedicated-path controls, not ordinary text inputs.

## Admission Panels

Login, character select, and character create are server admission-flow panels built on ordinary schema nodes, not UI-kernel standard panels and not bespoke renderer screens.

The UI kernel owns only the vocabulary needed to build them: exclusive surfaces, trusted credential inputs, row repeaters, select controls, and actions. The stock server-authored admission package source lives under `Server/RuntimeTemplate/scripts/Packages/Admission/`, but fresh runtime roots start with an empty `scripts/` folder so operators can install that package, another package, or fully custom admission scripts.

Admission should be event/step based rather than a monolith. The default shape is:

- `admission.client_connected`
- login panel
- `admission.login_verified`
- character select/create panel
- `admission.character_selected` or `admission.character_created`
- `admission.enter_world`

Servers may install alternate flows, such as `client_connected -> enter_world` for local tests or `client_connected -> login_verified -> enter_world` for servers without character selection. Any package can provide a replacement step only through the script admission registry; replacement is assignment, not accumulation.

`Server/Admission` owns active flow state per connection. When `admission.client_connected` fires after the live session is bound, the runtime opens the active step's panel provider, records the completion event it is waiting for, and advances only when that completion signal succeeds. `Server/Network` only forwards UI action documents and panel opens across the wire. The first login slice uses a narrow credential-validator seam that can be backed by a stub in tests and by the real accounting engine later.

`Server/Accounting` owns the real credential policy: account lookup, password
hash verification, banned/suspended/pending/locked account states, active-session
conflict policy, failed-attempt rate limiting, and login audit records. Those are
compiled server infrastructure, not script package code. Server scripts can
replace the admission flow around that policy, but they should not be required
for the server to reject invalid credentials.

The trusted login packet path must support more than one credential material
kind. Local-server authentication submits local credential material to the
server's accounting provider. UltraSim portal-backed authentication submits only
an opaque portal session/token and server instance id context; the game server
does not receive portal passwords, personal account data, or platform history.

The trusted credential packet is separate from `UiActionRequestDocument`. The
panel button still declares a `TrustedCredential` action, but the client handler
reads trusted input controls and sends a dedicated `LoginCredentialSubmitPacket`
with credential material kind, panel instance id, action id, username, and opaque
material bytes. Password/token values must not be logged, echoed, or placed in
generic UI field values.

The server-side accounting adapter is the only path that should turn a trusted
login packet into an admission success. Failed validation leaves the current
admission step open and publishes a model delta to the login panel's configured
status slot. The delta carries a machine-readable failure reason such as
`InvalidCredentials`, `RateLimited`, `Banned`, or `ExternalProviderUnavailable`;
clients map those codes to local display text and should use a generic fallback
for unknown codes.

Use the shared `SessionAdmission` exclusive group for:

- login
- character select
- character create
- delete/confirm flows that block admission
- reconnect/error panels that replace admission state

The default login panel owns presentation only. Username, remember-account, retry/status text, and the trusted password input are schema/model slots; the submit action uses the trusted credential action path.

The default character select panel is row-backed and should allow zero rows. A fresh account with no characters should not need a different schema; it can render the same panel with an empty character list and a visible create action. Character rows use stable `characterId` row keys so the server can add, replace, or remove rows if a character is created, deleted, renamed, or updated while the panel is open.

The default character create panel should use normal form controls: name text input, template/select options, start-location/select options, status text, create, and cancel. Template/start options are schema-level options for now; if they become large or content-heavy later, move them to a catalog-backed admission panel instead of turning them into per-open model payload.

## Permissions And Capabilities

Panels and controls can carry capability metadata:

- required account role
- required game-master/admin permission
- available only in debug builds
- available only on local embedded server
- read-only for current user
- hidden for current user

The server decides. The client uses this for presentation only.

## Foundational Presentation Hooks

Overhead health bars are a presentation hook, not a combat/content commitment.
The client should expose a persisted setting for showing them, but the bars only
render when server presentation state marks an entity as in combat and supplies
a health fraction. Until the real health/combat components exist, missing health
state means no bar. Pull-off health tabs, target health collectors, and richer
combat frames can build on the same entity-state binding later.

The next foundational admin/content work should stay focused on reusable
platform primitives before content packages:

- a RunUO `[props]`-style entity/component inspector;
- a dynamic capability/permission registry rather than a hard access enum;
- runtime content package/group mount and unload tracking;
- RunUO-style spawner primitives backed by ECS state and prefab groups;
- clear script-author guidance that XmlSpawner-style attachments map to ECS
  component add/remove, not a parallel attachment subsystem.

## Styling And Assets

Schema should reference logical styles and assets:

- style keys
- icon ids
- image ids
- theme slots
- density mode
- warning/destructive/primary button roles

Assets resolve through the content resolver stack:

1. built-in defaults
2. user resource packs
3. server patch mappings
4. future explicit user mods

Resource packs are cosmetic. Server patches are additive/override data/assets. Mods are explicit functionality. UI schema must preserve that separation.

## Window And Focus Policy

Panels should declare:

- surface/layer target
- focus mode
- exclusive group
- input blocking behavior
- default focus field
- close behavior
- escape behavior
- z-order class
- persistence key for client-local window placement

Multiple non-exclusive menu panels must be able to coexist. Inventory, character sheet, chat, journal, bank, vendor, crafting, and similar gameplay menus are separate server-driven or client-local menu surfaces. Opening one should not implicitly close the others unless the server says so. This is a presentation rule, not a separate backend protocol: the runtime uses panel metadata to decide which host/layer receives the panel.

Recommended focus modes:

- `Passive`: visible but does not capture keyboard/controller focus by default, such as chat history or read-only status panels
- `Normal`: focusable window that can coexist with other normal windows, such as inventory, character sheet, bank, crafting, vendors, journal, and admin inspectors
- `Modal`: top-level focused panel that blocks interaction with lower panels until answered or closed, such as confirmation dialogs
- `Exclusive`: singleton top layer for a declared group, such as settings/options, pause menu, login, character select, reconnect/error, or full-screen admin takeover

Focus mode governs default focus acquisition, input routing, controller navigation, and whether lower layers remain interactive. `Passive` does not mean "can never receive input." It means the panel does not automatically steal focus when shown. If the user explicitly clicks a text field or presses the chat key, a passive panel may temporarily own that control's input until it releases focus.

Exclusive groups prevent overlap within the same group. Opening a new panel in an exclusive group should replace, close, or focus the existing panel according to `OpenPolicy`. Example groups:

- `AppShell`: start menu, settings/options, local pause/options overlay
- `SessionAdmission`: login, character select, character create/delete
- `BlockingServerDialog`: disconnect/error/reconnect/account messages
- `AdminTakeover`: full-screen or input-blocking GM tools

Z-order classes should be explicit:

- world UI
- HUD
- normal menu windows
- floating utility windows
- modal dialogs
- exclusive app/session overlays
- tooltip overlays
- trusted error/disconnect overlays
- debug overlays

Suggested Godot menu hosts:

- `WorldUiHost`: world/object anchored UI, labels, interaction prompts
- `HudHost`: persistent gameplay HUD
- `MenuSharedHost`: passive and normal shared menu panels
- `MenuModalHost`: modal dialogs above shared menus
- `MenuExclusiveHost`: app/session overlays that can dim, hide, or disable lower menu hosts
- `TooltipHost`: transient hover/focus tooltips above normal UI
- `DebugOverlayHost`: debug/admin overlays that intentionally sit above normal gameplay UI

These hosts are renderer/runtime implementation details. They should not fork the schema protocol or create a second server-driven UI system.

Escape/back behavior should be deterministic:

- if a modal or exclusive panel is on top, route Escape to that panel's declared close/back action
- otherwise close or defocus the topmost closable normal panel by z-order, with last-focused order used to update z-order when the user clicks or focuses a panel
- if no closable server-driven panel owns focus, route Escape to the local app shell settings/options overlay
- server-owned panels receive a close/back intent; the server decides whether the panel actually closes

Session loss must close or invalidate server-owned panels. Pending exclusive panels should block input the same way the final panel would.

## Client Local Settings

Client-local settings can use the same renderer vocabulary, but they are not server-owned panels.

Examples:

- graphics
- sound
- nameplates
- chat display
- HUD preferences
- debug overlay preferences
- content/cache preferences

These can live in `ClientAppShell` or a local schema renderer path, but they should not be confused with server authority.

Current Godot shell status: the local `ClientAppShell` owns the first presentation
settings surface. `MenuCanvasManager` applies and persists overhead chat,
nameplate, chat panel visibility, visible world-UI budget, and world-UI scale
settings under `user://client_settings.cfg`. `WorldCanvasManager` keeps these as
client presentation policy only; server-authored panels and gameplay state remain
authoritative elsewhere.

## ECS Control Panel Target

The eventual ECS control panel should reproduce and improve the v1 panel:

- statistics header
- environment/hardware panel
- rendering/debug panel
- systems table
- per-system settings
- per-system live timings
- entity/archetype counters
- component/entity inspector
- chunk/AOI/network panels
- save/reload/reset controls

The schema work needed for that:

- typed setting descriptors
- collapsible groups
- tabs/sidebar navigation
- repeaters/tables
- live telemetry slots
- row selection
- dirty/apply/save-all semantics
- action result feedback
- permission-gated destructive actions

## Implementation Phases

### UI-1: Contracts And Schema Core

- port/refresh shared UI ids and contracts
- define UI capability negotiation record for protocol version, supported node families, renderer limits, and optional capabilities
- define schema node ids
- define payload slots and update messages
- define response messages
- define panel metadata: surface/layer target, category, focus mode, exclusive group, z-order class, open/back policy, input-blocking flags
- define tooltip policy metadata for modal/exclusive panels
- define catalog identity records separately from schema identity records
- define inventory/container identity, item row keys, placement metadata, and mutation action conventions separately from any one inventory viewer
- define tooltip metadata, rich tooltip payload shape, and optional detail-request flow
- define context/radial action descriptor shape for item, row, entity, world, and admin targets
- define scoped model delta format for page/tab/region refreshes
- define state binding descriptors for slots backed by replicated/session state
- define state binding resolver interface boundary
- define state binding resolver registry ownership and registration contract
- define slot update priority for explicit model updates, telemetry updates, and runtime state bindings
- define validation limits
- round-trip codec tests

### UI-2: Cache Negotiation

- implement in-memory kernel cache prototype with exact schema/catalog identity lookup
- integrate bridge with existing server-specific cache key/path model
- implement cold path schema definition handling and validated schema-cache insert
- implement hot path schema lookup through cached compatibility contexts
- implement catalog cache identity and request/response handling; first kernel step stores catalog identities until catalog documents exist
- verify schema hash/revision identity and supersession behavior
- verify catalog hash/revision identity and supersession behavior
- cache hit/miss/superseded tests for both schema and catalog data

### UI-3: Minimal Renderer

- implement client runtime bridge foundation: panel instance registry, cold/hot open routing through `UiDefinitionCache`, explicit missing/superseded statuses, and state-binding resolver registry ownership
- implement action request routing from open panel instance to validated outbound action using the panel's retained compatibility context
- implement model update routing against open panel instances: full model refresh, slot/scoped patch, repeater rows, stale sequence ignore, and schema/catalog supersession signals
- retain the validated schema document on open panel instances so renderers can build from the same schema/context that action and update routing use
- first Godot renderer slice should render only static `Panel`, `Row`, `Column`, `Text`, and `Button` nodes; unsupported node kinds should degrade to inert placeholders rather than crashing the client
- next renderer slice should add `DynamicText`, trivial `Separator`/`Spacer` nodes, and `Button.ActionId` press routing through `ClientUiPanelRegistry.ValidateActionRequest`
- dynamic text reads from the panel instance's current model slots; later update slices should refresh existing controls from model updates instead of rebuilding the schema tree
- until UI packets exist on the live network path, the Godot message-box smoke is local to the client runtime: it cold-opens a schema through the client registry, mounts the renderer output, verifies dynamic text, and presses the rendered button through action validation
- open/replace/close panel host under `MenuCanvasManager`
- split Godot presentation into world, HUD, shared menu, modal, exclusive, tooltip, and debug hosts while keeping one backend panel path; the first Godot bridge mounts separate menu, modal, and exclusive lanes under `MenuCanvasManager`
- enforce focus mode, input blocking, z-order class, and exclusive group rules
- enforce tooltip policy for modal/exclusive panels
- implement pending exclusive placeholder panels for cold server-driven opens
- implement state-bound slot refresh from client runtime caches
- implement server-forced full model, scoped region, and repeater row update application
- implement basic inventory/container model delta application using stable item/container ids
- implement basic tooltip and context menu rendering for schema-declared actions
- response/ack path
- message box smoke

### UI-3B: Server Script Compiler Gate

Status: complete. After the minimal renderer, live UI open/action packet path, and first builder-authored message-box smoke were working, the project paused and implemented the server-side C# script compiler/runtime setup before expanding login/character/menu content.

Canonical plan: [Server Script Compiler And Runtime Layout Plan](../Server/SCRIPT_COMPILER_AND_RUNTIME_LAYOUT_PLAN.md).

This compiler belongs to server startup/plugin infrastructure, not the client patch stream and not the UI kernel. The goal is a RunUO-style exported server app layout where server operators can put gameplay code under `scripts/` folders such as:

- `scripts/Engines`
- `scripts/Items`
- `scripts/Mobiles`
- `scripts/UI`
- `scripts/Commands`
- `scripts/Regions`
- `scripts/Customs`

Generated compiler output should stay outside editable script source by default, under `cache/scripts/`. The compiler should support a cache keyed by script source hashes, referenced assemblies, compiler options, and API/protocol versions so clean restarts can skip unnecessary recompilation. Startup should report a clear phase like `Scripts: Compiling C# scripts... - done (0 warnings, 0 errors)` and should fail with actionable diagnostics when scripts do not compile.

First proof should stay small:

- script-defined command or startup hook
- script-authored message/admin panel using `UiPanelBuilder`
- script registration surface that can later host engines such as inventory rules, crafting, vendors, mobs, and admin pages
- tests for compile success, compile failure diagnostics, cache hit/miss behavior, registration discovery, and no executable code arriving through server content patches

The important boundary is:

- kernels stay engine-agnostic (`UI`, `Inventory`, `Network`, `ECS`)
- server glue exposes stable APIs to scripts
- `scripts/Customs` implements gameplay/content behavior on top of those APIs
- client content patches remain data/assets only and must not carry executable scripts

### UI-4: Login And Character Flow

- login panel with trusted credential binding
- character select
- character create
- character delete confirm
- character select row add/update/remove refresh smoke
- simple state-bound status panel smoke, such as a local player health/resource widget
- session loss clears active panels
- focused smoke tests

### UI-5: Settings Controls

- toggle
- number input
- slider
- stepper
- select
- radio group
- checkbox group
- dirty/apply/revert
- descriptor-to-renderer tests

### UI-5B: Inventory Viewer Foundation

- implement the D2-style grid inventory viewer helper as the default product path
- implement or keep a simple Skyrim-style list viewer as a reference/test shell over the same inventory/container contract
- validate drag/drop, equip, split/merge, item remove, and shared-container delta flows against server authority
- validate inventory item context menu actions over the same item/container contract
- validate trade-window two-party shared view semantics at the contract/model level before treating it as a normal container
- validate auction-style volatile listing refresh as a live repeater/paged table, not a cached catalog
- reserve `ContainerCanvas` for later UO-style freeform containers; implement only when a real feature needs it

### UI-6: Admin Shell Prototype

- admin shell panel host
- plugin/page registration model
- page state model
- systems list prototype
- current ECS `ControlDescriptors` rendered generically
- permission-gated actions
- admin row/context action menus for common inspect/toggle/reset/debug actions

### UI-7: Tables And Live Telemetry

- table/repeater row context
- paging
- row selection
- partial row updates
- live slot updates
- timing/statistics panel

### UI-8: ECS Control Panel

- system settings panels
- system timings
- entity/archetype counters
- component inspector
- debug actions
- save-all/revert behavior

## Testing Plan

Engine-agnostic tests:

- schema codec round trips
- validator limit failures
- payload slot compatibility
- state binding descriptor validation
- state binding resolver authorization failure
- cache identity match/miss
- response action id validation
- row context validation
- setting descriptor serialization
- permission filtering
- update/delta application
- repeater row add/update/remove application
- shared container item add/update/remove application
- inventory viewer contract parity between list and grid viewers
- drag/drop intent validation shape for item/container actions
- tooltip metadata and rich tooltip payload validation
- context/radial action descriptor validation
- context action stale target rejection
- context/radial action range-exit rejection
- schema/catalog supersession close/reopen handling
- slot update priority between model, telemetry, and state binding sources

Godot validation:

- renderer smoke for basic nodes
- state-bound slot refresh from a fake client runtime state cache
- multiple state-bound panel instances using the same schema with different binding targets
- surface/layer routing for world, HUD, menu, modal, exclusive, tooltip, and debug targets
- tooltip hover/focus smoke
- modal/exclusive tooltip suppression smoke
- context menu open/action/close smoke
- radial menu category/action smoke
- inventory grid smoke with server-confirmed drag/drop
- inventory list smoke over the same inventory/container model
- focus traversal
- escape/close behavior
- modal/exclusive behavior
- session loss clears panels
- login/character flow smoke

Server validation:

- panel open/update/close
- panel response routing
- admin permission checks
- destructive action confirmation
- ECS descriptor generation

## Design Guidance

- Prefer semantic schema nodes over absolute pixel layouts.
- Keep absolute positioning available but bounded.
- Keep builders pleasant, but make the wire schema explicit and testable.
- Add composite helpers only when they remove real authoring pain.
- Treat permissions and disabled states as presentation hints on the client.
- Revalidate all behavior on the server.
- Keep schema and payload separate.
- Keep server-driven UI separate from local app shell screens.
- Do not send executable UI logic from server to client.
