# UI Schema Authoring Language Spec

Purpose: define the authoring surface for the server-driven UI schema system: node types, layout primitives, layers, styles, bindings, conditional state, tabs, paging, and the conceptual AST that sits inside `PanelDefinition`.

Status: proposed authoring-language spec. This sits on top of the already-locked wire/cache model in `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`.

This document is intentionally split between:

- semantics we should design toward now
- API/AST details that remain provisional until implementation proves them

---

## 1) Scope

This document defines what goes inside a cached panel definition.

It does **not** redefine:

- `OpenPanel`
- `PanelDefinition`
- `UpdatePanel`
- `ClosePanel`
- `UiPanelAck`
- `UiPanelResponse`
- cache negotiation
- per-server cache identity

Those remain owned by:

- `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`

The package/layout and phasing for implementation remain owned by:

- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`

---

## 2) Design Principles

- One schema system and one renderer.
- Definitions are cached; payloads are runtime model data.
- The schema expresses structure, layout intent, style references, and interaction contracts.
- The schema does **not** send engine UI controls, executable code, or raw asset paths.
- Bundled panels and server-sent panels use the same schema system.
- The authoring surface should be expressive enough for real custom panels without devolving into a RunUO `AddLabel/AddButton/...` wire DSL.

---

## 3) Authoring Surface vs Runtime AST

The preferred server-side authoring model is a fluent builder.

Example:

```csharp
var schema = UiSchema.Create("char_info")
    .Position("center")
    .Size(520, 430)
    .Draggable()
    .Style("panel.ornate")
    .Build();
```

Important distinction:

- authoring API = ergonomic builder surface for server code
- runtime AST = engine-agnostic serialized definition consumed by the renderer

Locked direction:

- builder compiles to AST
- renderer consumes AST

Still provisional:

- exact builder method names/signatures
- exact AST record/class names
- exact binary/json schema codec

---

## 4) Panel Root

Every schema starts with a panel root.

### 4.1 Panel Root Capabilities

- template id
- position
- size
- root style
- draggable
- closable
- focusable
- layers
- one content root

### 4.2 Example

```csharp
var schema = UiSchema.Create("multiplayer_menu")
    .Position("center")
    .Size(720, 520)
    .Draggable()
    .Style("panel.ornate")
    .Build();
```

---

## 5) Positioning Model

Support both semantic placement and explicit offsets.

### 5.1 Semantic Positions

- `center`
- `center-top`
- `center-bottom`
- `left`
- `right`
- `cursor`

Semantic positions are the default and preferred mode.

### 5.2 Explicit Positions

- `(x, y)`
- `(x, y, anchor)`

Supported anchors:

- `top-left`
- `top-center`
- `top-right`
- `center-left`
- `center`
- `center-right`
- `bottom-left`
- `bottom-center`
- `bottom-right`

---

## 6) Layer Model

Layers are visual-only background/decorative elements rendered behind content.

They are:

- part of the definition
- cached
- non-interactive
- render-ordered back-to-front

### 6.1 Capabilities

- id
- style
- inset
- offset
- size
- optional edge tiling

### 6.2 Example

```csharp
.Layer("frame", l => l
    .Style("panel.ornate_frame")
    .Inset(0))

.Layer("surface", l => l
    .Style("panel.inner_surface")
    .Inset(8))
```

Locked direction:

- layers exist
- layers are definition-only
- layers render before content

Still provisional:

- exact builder names
- exact AST representation for inset/offset/tiling

---

## 7) Layout Primitive Set

These are the major content-structure primitives the schema system should support.

### 7.1 Core Layout Primitives

- `Content`
- `Section`
- `Row`
- `Group`
- `Columns`
- `Column`
- `Header`
- `Footer`
- `ScrollRegion`

### 7.2 Navigation/Collection Primitives

- `Repeater`
- `Pager`
- `TabSet`
- `TabPage`

### 7.3 Intent

- `Row` = horizontal arrangement
- `Group` = inline grouping inside a row
- `Columns` = side-by-side columns
- `Section` = vertical block with optional style/background
- `Header` / `Footer` = semantically distinct bands
- `ScrollRegion` = bounded scrollable area
- `Repeater` = repeated item template over collection data
- `Pager` = page navigation control
- `TabSet` = tab-strip with one selected page/content area
- `TabPage` = content for one logical tab

### 7.4 Nesting Rules

Recommended rules:

- `Row` contains `Group` children only
- `Columns` contains `Column` children only
- `Group` contains inline/content nodes, not other layout primitives
- `Section`, `Column`, and `ScrollRegion` may contain nested layout/content nodes
- `TabSet` contains `TabPage` children
- `TabPage` contains normal content/layout nodes

---

## 8) Content Node Types

Minimum useful content nodes:

- `Label` (static text)
- `Text` (bound text)
- `Value` (formatted bound value)
- `Image`
- `ActionButton`
- `InputText`
- `Checkbox`
- `Separator`
- `Spacer`

### 8.1 Notes

- `Label` is definition-time/static
- `Text` and `Value` are payload-bound
- `ActionButton` fires `UiPanelResponse`
- `InputText` contributes value on submit/action
- `Checkbox` may either fire immediately or contribute to the next action payload

### 8.2 Interactive Value Return Semantics

This is a locked semantic requirement even though the exact payload encoding remains provisional:

- `UiPanelResponse` may carry current interactive field values needed by the server to handle an action
- at minimum, this applies to:
  - `InputText`
  - `Checkbox`

Examples:

- a `TextPrompt` with an input field and a `Submit` button should send the current text value with the submit action
- a form with multiple input fields may send the current values for all relevant interactive fields when the user presses `Confirm`
- a checkbox may either:
  - fire immediately with its new value, or
  - contribute its current value to the next action response from the panel

Recommended direction:

- action responses should include the current values of relevant interactive controls in panel scope
- repeater-row actions should additionally include repeater row context (`rowIndex`, `rowKey`) when applicable

Still provisional:

- exact response payload encoding
- exact field naming in the response payload
- whether all interactive values are always included or only those relevant to the triggering action
- exact scoping rules for panel-level vs row-level interactive values

---

## 9) Binding Language

Bindings reference model fields using dot-separated paths.

Examples:

- `player.name`
- `points.stats`
- `skills.rows`
- `tabs.selected`
- `clientList.currentPage`

### 9.1 Binding Context

At panel root:

- bindings resolve against the full model

Inside repeaters/tab pages:

- bindings resolve against the current local context
- `$root.` can reference the panel root model

Example:

```csharp
.Repeater("skills", bind: "skills.rows", row => row
    .Text("skill_name", bind: "name")
    .ActionButton("skill_up", action: 300,
        enabledWhen: "$root.points.skills > 0 && base < cap"))
```

Locked direction:

- simple path-based binding
- local context + `$root` escape

Still provisional:

- whether bindings remain raw strings at runtime or compile to a small typed representation

---

## 10) Conditional State

Nodes and layout primitives should support:

- `visibleWhen`
- `enabledWhen`

Semantics:

- `visibleWhen == false` -> not rendered, takes no layout space
- `enabledWhen == false` -> rendered but non-interactive/disabled

### 10.1 Predicate Surface

Supported conceptually:

- comparisons: `>`, `<`, `>=`, `<=`, `==`, `!=`
- boolean ops: `&&`, `||`, `!`
- grouping: `(...)`
- operands:
  - field paths
  - `$root` field paths
  - numeric literals
  - string literals
  - boolean literals

Example:

```csharp
.ActionButton("str_up", action: 201,
    visibleWhen: "points.stats > 0",
    enabledWhen: "stats.str < stats.strCap")
```

Locked direction:

- simple client-evaluated predicates exist

Still provisional:

- exact authored syntax vs structured predicate AST

---

## 11) Repeater Semantics

Repeaters bind to a collection and stamp out repeated content.

### 11.1 Required Semantics

- collection bind path
- item template
- row-local binding context
- optional empty text
- optional visible row cap / scroll handoff

### 11.2 Action Context

When an action fires from within a repeater row, the response should include:

- `actionId`
- `rowIndex`
- `rowKey` when available

`rowKey` should be preferred server-side when the model exposes a stable id/key.

---

## 12) Pager / Page Navigation

Pager should not be thought of as “just list paging.”

It is the general page-navigation control for:

- large lists broken into pages
- record-by-record browsing
- page-based server-owned result sets

### 12.1 Required Semantics

- current page bind
- total pages bind
- previous action id
- next action id
- disabled state at bounds

Example:

```csharp
.Pager("server_list_pager",
    currentPage: "clientList.currentPage",
    totalPages: "clientList.totalPages",
    prevAction: 821,
    nextAction: 822)
```

Authority rule:

- page selection remains server-driven by default
- client clicks send `UiPanelResponse`
- server updates model and responds with `UpdatePanel`

---

## 13) Tabs

Tabs are a first-class authoring concept, not a workaround built from ad hoc buttons.

### 13.1 Required Semantics

- tab-strip with items
- selected tab bind
- per-tab action
- per-tab content/page
- optional badge/count
- visible/enabled state per tab

### 13.2 Example Shape

```csharp
.TabSet("server_tabs",
    items: "tabs.items",
    selected: "tabs.selected",
    tabs => tabs
        .Tab("client_list", action: 801, page => page
            .Repeater("client_rows", bind: "clientList.rows", row => row ...))
        .Tab("favorites", action: 802, page => page
            .Repeater("favorite_rows", bind: "favorites.rows", row => row ...)))
```

Authority rule:

- tab selection remains server-driven by default
- client click sends `UiPanelResponse`
- server updates selected tab and sends `UpdatePanel`

This keeps behavior deterministic and avoids local/client authority drift.

---

## 14) Style System

Style references are logical string ids.

Examples:

- `panel.ornate`
- `panel.inner_surface`
- `header.decorated`
- `section.banded`
- `row.split`
- `button.confirm`
- `button.plus`
- `pager.default`
- `tabs.default`

Styles may control:

- background
- border
- padding/margins
- text appearance
- icon references
- hover/pressed/disabled visuals
- size hints

Locked direction:

- schema chooses logical style keys
- client theme/content resolver decides actual visuals

---

## 15) Composite / Convenience Nodes

Composite helpers are valid and useful, but they are authoring sugar only.

Examples:

- `StatRow`
- `LabeledValue`
- `LevelDisplay`

Rule:

- composites compile to standard nodes/layout primitives
- renderer should not depend on custom server-side convenience helpers

---

## 16) Validation Constraints

The renderer/validator should enforce limits such as:

- max nodes per definition
- max nesting depth
- max layers
- max binding-path length
- max predicate length
- max definition payload size

Locked direction:

- these limits exist

Still provisional:

- exact numbers

---

## 17) Example Shapes This Language Must Cover

At minimum, the authoring surface should be able to express:

1. `MessageBox`
2. `SimpleListMenu`
3. `TextPrompt`
4. `CharInfo`-style layered stat/skill panel
5. `PointDistribution`-style paged allocation panel
6. tabbed multiplayer menu with paged results

If the language cannot express those without falling back to raw client scripting, it is still too weak.

---

## 18) Internal AST Direction

The internal AST should conceptually include:

- panel root node
- layer nodes
- layout nodes
- content nodes
- repeater node
- pager node
- tab-set/tab-page nodes

This document does **not** freeze the exact C# record types yet.

Locked direction:

- the AST exists and is engine-agnostic

Still provisional:

- concrete runtime type hierarchy
- concrete codec encoding shape

---

## 19) Locked vs Provisional Summary

### Locked Direction

- panel root model
- positioning model
- layer model
- layout primitive set
- repeater semantics
- tab semantics
- pager/page-navigation semantics
- binding paths with `$root`
- visible/enabled conditional behavior
- style-reference model
- composites are sugar, not renderer-level primitives

### Provisional

- exact fluent builder signatures
- exact AST record/class names
- exact predicate representation
- exact validation numbers
- exact serialization codec

---

## 20) Related Docs

- `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`
- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `Docs/SERVER_DRIVEN_UI_SHAPE_EXAMPLES_2026-03-18.md`
- `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`
