# INVENTORY.md

## UltraSim Inventory — Current Contracts, Design Direction, and Future Semantics

**Status:** living design/architecture note

**Current implementation checkpoint:** `92595d04` — I0.10 authoritative Grid placement

**Remote/private vertical checkpoint:** `0ad93d54` — I0.8 authenticated remote inventory vertical

**Earlier authoritative core:** I0.7 / S0.7 inventory foundation

This document captures the current Inventory implementation and the design direction discussed around it while the details are fresh. It intentionally distinguishes:

- **Implemented / accepted:** behavior that exists in the current repository and is already test-covered.
- **Design direction:** behavior we want the architecture to support and should avoid accidentally designing out.
- **Future / not implemented:** concepts that are intentionally deferred until a real gameplay vertical earns them.

The goal is not to port the V2 Inventory subsystem wholesale. V2 contained many useful ideas, especially viewer-neutral container semantics, but V3 has a materially stronger authoritative foundation. Future work should recover useful V2 concepts on top of V3's ECS truth, persistence, generation ownership, bounded indexes, and concrete Game semantics.

---

# 1. Core philosophy

The Inventory system should be capable of supporting very different games and server configurations without forcing every server to pay for or use every mechanic.

A server may want something close to:

- **Skyrim:** list-style inventory, primarily weight-driven.
- **Ultima Online:** freeform bag placement, nested bags, potentially no weight.
- **Diablo II:** spatial grid, item footprints, little or no weight semantics.
- **RuneScape:** single item/stack icon per slot.
- **Project Zomboid / Kenshi-like:** capacity plus weight, nested containers, effective weight reduction.
- **UltraSim hybrid:** a Diablo-like player backpack, RuneScape-like bank, list-style index/search view, physical 3D shelves, nested magical bags, and other container-specific rules in the same server.

The architecture should therefore provide reusable **authoritative inventory primitives** and a finite set of explicit policy dimensions rather than hard-coding one canonical inventory UX.

The key principle inherited from V2 and retained for V3 is:

> **Container truth is not the viewer.**

A container has authoritative contents and placement semantics. A viewer is a projection/presentation of those facts.

A Diablo grid, Skyrim list, UO freeform bag, RuneScape slot view, GM/admin table, search/index, and physical 3D shelf can be different viewers or presentations over authoritative inventory/container state.

---

# 2. Current V3 authoritative ownership

## 2.1 ECS is the authoritative mutable truth

Current V3 Inventory is not an independent in-memory database.

Inventory state is persisted in ECS components and prefab provenance. The current authoritative core includes:

- `InventoryContainer`
  - persistent effective item-count capacity;
- `InventoryContainerOwner`
  - optional persistent entity-owner relationship;
- `InventoryItem`
  - parent relationship;
  - equipment slot;
  - quantity;
- `InventoryGridPlacement`
  - optional persistent canonical X/Y for an item contained by a Grid container;
- `InventoryGridFootprint`
  - optional immutable content footprint;
  - canonical absence means 1×1;
- `InventoryStackPolicy`
  - optional content-authored maximum stack quantity;
- `InventoryEquipCapability`
  - optional content-authored allowed equipment slots.

Item definition/presentation identity comes from game content and prefab provenance rather than an Inventory-owned item-kind enum.

The current conceptual item truth is approximately:

```text
Item runtime/persistent identity
    + Prefab/content provenance
    + Parent
    + EquipSlot
    + Quantity
    + optional GridPlacement(X,Y), only while Grid-contained

Prefab/content provenance
    + optional immutable GridFootprint(Width,Height)
```

The system should continue avoiding hard-coded switches such as `if item == Sword` inside Inventory rules. New ordinary item types should be content additions, not Inventory code changes.

## 2.2 InventoryIndex is derived acceleration, not truth

`InventoryGenerationState` owns a generation-local `InventoryIndex` derived from ECS truth.

The index accelerates:

- container membership;
- item location;
- equipment lookup;
- contained-item enumeration;
- capacity checks.

Contained items use a bounded dense global child-node pool. Empty containers do **not** reserve `container count × per-container capacity` child storage. Removal swap-compacts the global child pool and repairs donor links.

The accepted rule is:

```text
ECS truth
    ↓
generation-owned derived InventoryIndex
    ↓
fast queries / mutation prevalidation / projection
```

A cold restore/repair boundary may rebuild the index from ECS truth. Rebuild prepares a replacement and only publishes it after full validation. Failed rebuild must preserve the previously published index.

## 2.3 Game owns Inventory semantics

Inventory is a concrete Game subsystem. It is not an ECS-kernel responsibility, Network responsibility, or UI responsibility.

- ECS provides storage/query/mutation mechanisms.
- Game Inventory owns item/container rules and authoritative mutations.
- Game UI owns view workflow and action validation.
- Network transports bounded semantic messages but does not decide Inventory rules.
- Client UI/presentation displays authorized semantic state but does not own authoritative truth.

---

# 3. Current implemented mutation semantics

Current authoritative mutations include:

- create container;
- create owned container;
- create item, with explicit placement when the destination is Grid;
- move contained item between Grid and Unplaced containers;
- reposition a Grid-contained item;
- split stack, with explicit destination placement when required;
- equip;
- unequip, with explicit destination placement when required;
- destroy item;
- destroy empty container.

Accepted operations validate the complete relevant topology before publishing changes.

The desired transaction discipline is:

```text
validate authoritative runtime identity
validate source topology
validate destination topology
validate content policy
validate capacity / slot constraints
validate ECS truth against derived index
    ↓
commit authoritative ECS mutation
    ↓
update derived index
```

Ordinary domain rejection returns an explicit mutation result. An unexpected failure after a mutation has already been prevalidated is an invariant/system failure, not something to disguise as normal user rejection.

## 3.1 Equip / unequip relationship

Current equipped state is represented as a relationship change, not a separate duplicate item:

```text
Contained:
    Parent = Backpack
    EquipSlot = None

Equip
    ↓

Equipped:
    Parent = Character/Owner
    EquipSlot = MainHand

Unequip
    ↓

Contained:
    Parent = DestinationContainer
    EquipSlot = None
```

Only `MainHand` currently exists as an implemented equipment slot. The architecture should allow more concrete slots later without requiring a separate equipment datastore.

---

# 4. Persistence and identity

Inventory already participates in real Game persistence.

Current tests prove:

- inventory persists across save/load generation replacement;
- equipped relationships persist and reindex;
- Grid placement and immutable content footprint persist and reindex;
- Grid occupancy is rebuilt transactionally from restored ECS truth;
- derived-index rebuild occurs from restored ECS truth;
- failed rebuild does not publish a partially valid candidate;
- retained UI/view identity uses complete durable world + persistent-entity identity to avoid cross-world numeric-ID ABA.

Important identity distinctions should remain explicit:

```text
runtime ECS Entity
    !=
PersistentEntityId / PersistentEntityKey
    !=
Prefab/content identity
    !=
Network connection identity
    !=
UI row/view identity
```

A generation swap must not carry generation-local ECS handles forward as durable identity. Persistent keys are re-resolved into the replacement generation.

---

# 5. Current private/remote Inventory vertical — I0.8

I0.8 proves a real authenticated owner-private path through the production stack:

```text
authenticated connection
    → Accounting / Admission / CharacterSelect
    → owner-authorized InventoryUiController
    → RemoteUiSemanticPort
    → secure Network protocol
    → RemoteUiClientBridge / UiClientRuntime
    → GodotUiRenderer
```

The remote vertical exercises:

- opening an owner-authorized inventory;
- current item projection;
- cross-container movement;
- split;
- equip;
- unequip;
- stale/duplicate/unknown-view fencing;
- foreign-container rejection;
- reconnect/resync;
- save/load generation replacement;
- real Godot UI presentation/input.

## 5.1 Owner-private authorization

`InventoryUiController.OpenOwned` is the current private-container route.

Trusted Game composition supplies the actor already authorized by Admission. The controller proves that:

- the source container belongs to that actor;
- each explicitly available transfer destination belongs to the same actor;
- complete persistent keys are retained;
- ownership is revalidated on every authoritative action;
- ownership is revalidated after generation replacement.

If ownership is lost, the view is closed/detached rather than silently retaining authority.

`Open` remains a trusted unrestricted composition route for ownerless world containers such as ordinary world chests. It is **not** a remote participant authorization shortcut.

## 5.2 Inventory is private state, not public AOI state

Inventory must remain separate from public world replication.

```text
Public actor/world state
    → public disclosure / AOI / selected public replication profile

Owner/private Inventory
    → authorized UI/private semantic delivery
```

Backpack contents must not be published through `ChunkPublication` or another public spatial profile merely because those Network paths exist.

---

# 6. V2 Inventory lessons

V2 had a much broader Inventory framework. It had a neutral `Kernels/Inventory` vocabulary plus a server bridge/store prototype.

V2 included concepts for:

- generic owner references:
  - entity;
  - character;
  - account;
  - world object;
  - guild;
  - vendor;
  - system;
- generic requester references:
  - player session;
  - character;
  - entity AI;
  - server system;
  - admin;
  - script;
- container lifecycle;
- placement modes:
  - none/list;
  - grid;
  - freeform;
  - slot;
- permissions/access vocabulary;
- item flags such as locked/no-trade/no-drop/quest/transaction-reserved;
- mutation intents/results for move/split/merge/swap/take/drop/equip/unequip/use/destroy/lock/unlock;
- viewer/session state;
- container deltas;
- transactions and suspended-item escrow;
- list/grid/freeform projections;
- owner lifetime policy;
- multi-viewer fanout.

V2's server bridge/store also implemented prototype move/split/merge/equip/lock and generic transaction begin/suspend/cancel/commit behavior.

However, the V2 in-memory store explicitly described itself as a correctness prototype rather than the persistence layer. V3 is narrower in feature breadth but stronger as real authoritative game state because the item/container truth is persisted ECS state with generation-owned derived indexes and real client/UI integration.

The strategy is therefore:

> **Recover useful V2 semantics selectively. Do not port the old framework wholesale.**

The most important V2 idea to preserve is the distinction between container truth and viewer/presentation.

---

# 7. I0.9 rule/profile foundation — implemented

I0.9 introduces compiled container rule profiles without changing the ECS schema or existing hot mutation layout.

Current types include:

- `InventoryRuleScope`
  - `PerContainerProfile`
  - `Uniform`
- `InventoryRuleProfileId`
- `InventoryPlacementMode`
  - `Unplaced`
  - `Grid`
  - `Slot`
  - `Freeform`
- `InventoryPlacement`
- `InventoryCapacityDimensions`
- `InventoryRuleProfile`
- `InventoryRuleSelection`
- `InventoryEffectiveRules`
- `InventoryRuleCatalog`

## 7.1 Per-container vs uniform server policy

The current implemented top-level selection is:

```text
InventoryRuleScope.PerContainerProfile
    each container's content definition selects its stable rule profile

InventoryRuleScope.Uniform
    server/composition forces every container through one selected profile
```

This supports both server philosophies:

### Uniform server

> Every Inventory follows the same system rules.

Example future server policy:

```text
All inventories:
    Grid
    footprints enabled
    weight enabled
    nesting forbidden
```

### Mixed/per-container server

> Every Inventory can have its own rules.

Example future configuration:

```text
Backpack       → Diablo-like Grid
Bank           → RuneScape-like Slot
Loot/index UI  → Skyrim-like List projection
Bookcase       → physical Grid mapped into 3D shelf transforms
Reagent pouch  → nested magical Grid bag
```

Current game content assigns stable distinct rule profiles to Backpack, World Storage, and Grid Backpack. Backpack and World Storage remain operational `Unplaced` profiles; Grid Backpack is an operational 5×5 `Grid` profile.

## 7.2 Profile maximum vs instance item capacity

A compiled rule profile declares capacity maximums. A concrete container instance retains its current persisted item-count capacity.

The current resolution shape is:

```text
server rule scope
    +
content-selected profile
    +
persistent instance item-count capacity
    ↓
InventoryEffectiveRules
```

The instance item count must be positive and no greater than the selected profile's declared maximum.

This distinction is intentional and is a clean hook for future capacity upgrades:

```text
Backpack profile maximum item count = 256
specific backpack current item count = 8
later upgraded backpack current item count = 12 / 20 / ...
```

The compiled profile should not be mutated merely because one particular bag receives an enchantment or progression upgrade.

## 7.3 I0.9 fail-closed floor and I0.10 operational floor

I0.9 closed with only `Unplaced` + item-count capacity operational. I0.10 adds authoritative `Grid` placement and independently enforces item-count capacity, grid bounds, and grid overlap.

`Slot`, `Freeform`, weight limits, and nesting depths remain declared typed contracts that fail closed because their authoritative supporting state and mutation logic do not exist yet.

This is deliberate:

> Metadata must not claim that a gameplay mechanic exists before the authoritative state needed to enforce it exists.

---

# 8. Viewer-neutral placement semantics

Placement belongs to container/item semantic state, not to one UI implementation.

The current placement vocabulary is:

```text
Unplaced
Grid
Slot
Freeform
```

The I0.9 `InventoryPlacement` value established a viewer-neutral shape containing `X/Y/Z`, `Width/Height`, optional stable `Slot`, and `Rotated`. I0.10 makes the Grid subset operational through optional persisted `InventoryGridPlacement(X,Y)`, content-authored `InventoryGridFootprint`, and a generation-owned occupancy index. Slot and Freeform remain contract vocabulary only.

`Unplaced` means authoritative inventory truth does not retain a positional placement. It can still be projected as a list, sorted view, search result, etc.

A Grid item retains canonical X/Y placement while contained by a Grid parent; its prefab supplies the immutable footprint and absence canonically means 1×1. A future Freeform item can retain freeform coordinates. Slot placement uses a stable slot identity.

## 8.1 Storage semantics and viewer semantics are separate

A Grid container does not imply it can only be viewed as a grid.

Examples:

```text
Grid backpack
    normal player viewer    → D2-style grid
    accessibility viewer    → list
    GM/admin viewer          → table/list

Slot bank
    normal player viewer    → RuneScape-style slots
    admin viewer             → searchable list/table

Freeform UO bag
    normal viewer            → UO-style freeform
    search/index viewer      → list
```

Likewise a list/index UI may not be an inventory container at all. A global item index should often be a **projection/search view over real containers**, not a fake container duplicating item ownership.

---

# 9. Capacity is multidimensional

Do not overload one `Capacity` integer with every concept.

The long-term model should distinguish at least:

```text
Item-count capacity
Grid/layout capacity
Weight capacity
Nesting capacity/policy
```

I0.9 encoded these as separate typed profile dimensions: `MaximumItemCount`, `GridWidth`, `GridHeight`, `MaximumWeightGrams`, and `MaximumNestingDepth`. I0.10 makes item-count and Grid/layout capacity operational and independently enforced. A zero weight limit currently means weight is unconstrained/disabled; zero nesting depth forbids nesting. Weight and nesting remain deferred.

These dimensions may correlate but are not semantically identical.

## 9.1 Item count vs grid cells

A 5×5 grid contains 25 cells, but that does not necessarily mean the container must support exactly 25 item identities.

A useful conventional default may be:

```text
ItemCountLimit = GridWidth × GridHeight
```

but this must remain a default/helper rather than an invariant.

Example:

```text
5×5 grid = 25 cells
ItemCountLimit = 20
WeightLimit = 35 kg
```

A sword might consume 1×3 cells while counting as exactly one item. A bag might consume 2×2 cells while still counting as one item. An insertion succeeds only when every enabled constraint succeeds.

## 9.2 Profile maximums and mutable/effective instance capacity

Long-term effective rules should conceptually resolve from layers such as:

```text
compiled base profile
    +
persistent instance upgrades
    +
temporary runtime effects
    ↓
effective container rules
```

Examples of independent upgrades:

- `+1 grid column`;
- `+1 grid row`;
- `+10 item identities`;
- `+20 kg weight capacity`;
- increased allowed nesting depth;
- reduced effective nested-content carry weight.

A spell or enchantment should modify the concrete bag's effective capacity/state, not rewrite the shared compiled Backpack rule profile.

---

# 10. Intended backpack progression

One desired gameplay direction is that the player's main backpack begins deliberately small and becomes a meaningful progression object.

Example progression concept:

```text
Cheap satchel
    5×5 grid
    modest item-count cap
    modest/no weight cap

Sturdy backpack
    larger grid
    increased item count
    increased weight

Reinforced pack
    further weight capacity
    possibly stronger nesting rules

Runic expansion
    larger interior grid without proportionally larger exterior footprint

Magically lightened bag
    large internal capacity
    reduced effective carried contents weight
```

The intended fantasy is eventually something like a special reinforced, runically enhanced, magically lightened bag earned through achievements, magic, crafting, adventures, and upgrades rather than beginning the game with the final maximum inventory.

Server owners must be free to disable or radically alter this system.

---

# 11. Nested containers / bags inside bags

Nested containers are an intended optional capability.

The key distinction is:

> **The space a container-item consumes in its parent is independent from the space available inside that container.**

Example:

```text
Small magical pouch
    outer footprint in parent grid = 2×2
    inner grid = 3×3

Upgraded pouch
    outer footprint in parent grid = 2×2
    inner grid = 5×5
```

This permits UO/Mabinogi-like organizational bags and magical space expansion.

A character might have:

```text
Main backpack
    ├── Reagent pouch
    ├── Food bag
    ├── Loot bag
    └── Tool roll
```

Players who like organization can use nested bags; other players can leave contents directly in the main bag.

## 11.1 Required safety semantics

If nesting is supported:

- containment cycles must always be forbidden;
- maximum nesting depth must be bounded/configurable;
- containers may restrict what nested container categories they accept;
- some servers/profiles may disable nesting entirely;
- operations must remain bounded and deterministic.

Never allow:

```text
Bag A contains Bag B
Bag B contains Bag A
```

The maximum nesting depth is both gameplay policy and an important bounded-work guarantee.

---

# 12. Weight semantics

Weight is an optional gameplay dimension.

A server that does not use weight should not maintain fake weight state or pay for subtree-weight bookkeeping.

If weight is enabled, distinguish several concepts rather than conflating them.

## 12.1 Three useful weight concepts

For a nested bag containing 10 kg of reagents:

```text
Main bag
    └── Magic reagent pouch
            └── 10 kg reagents
```

Useful concepts include:

### Physical contents weight

The real sum of the contained items' masses.

### Container capacity consumption

What counts against the inner container's own maximum weight capacity.

### Effective carried/parent weight

What this container subtree contributes to its parent/character after magical/structural modifiers.

These are not necessarily equal.

## 12.2 Reinforcement vs magical lightening

A reinforced bag might simply do:

```text
WeightCapacity += 20 kg
```

That allows more physical contents but does not make those contents lighter when carried.

A magically lightened bag might instead apply something like:

```text
NestedContentsCarryFactor = 50%
```

Example:

```text
bag itself            1 kg
physical contents    10 kg

parent-effective weight
    = 1 + (10 × 0.5)
    = 6 kg
```

The inner bag may still consume the full 10 kg against its own internal weight capacity even though the parent sees only 6 kg total contribution.

This Project Zomboid-like distinction is desirable, but it must remain optional/configurable.

## 12.3 Deterministic numeric representation

Authoritative weight modifiers should use deterministic fixed-point/rational representation rather than arbitrary floating-point state.

## 12.4 Derived subtree weight

If nested weight becomes operational, avoid recursively traversing every descendant item whenever a single coin moves.

A likely derived-index optimization is a cached subtree physical/effective weight with bounded ancestor propagation:

```text
child changes weight by Δ
    ↓
update direct container
    ↓
propagate bounded Δ through parent chain
```

Because nesting depth is bounded, propagation remains bounded.

---

# 13. Dynamic capacity and shrinking

Increasing capacity is straightforward. Decreasing effective capacity can invalidate existing state and therefore requires explicit semantics.

Never silently delete, destroy, duplicate, or eject authoritative items merely because a modifier expires.

## 13.1 Count/weight shrink

A useful default rule is **grandfathered over-capacity state**:

```text
old limit = 30 kg
contents  = 29 kg
buff expires
new limit = 20 kg
```

The container becomes over-capacity but retains existing contents. New operations that increase burden are rejected until contents fall within the current limit.

The same principle can apply to item-count capacity.

## 13.2 Grid shrink is harder

If grid dimensions shrink, items may occupy cells that no longer belong to the active grid.

Candidate future policies include:

1. **Permanent-only grid expansion** — temporary effects may modify count/weight but not grid dimensions.
2. **Grandfathered overflow cells / capacity debt** — existing placements outside the new active region remain temporarily legal, but no new placements may use them; moving an item out permanently releases the overflow.
3. **Transactional repack** — attempt deterministic repacking into the smaller grid; if impossible, do not complete the shrink or mark explicit unresolved overflow.

The currently favored conceptual direction is grandfathered/capacity-debt behavior because it avoids deleting items, forced world drops, or magical reshuffling. This is not implemented or frozen yet.

---

# 14. I0.10 authoritative Grid placement — implemented

I0.10 makes one concrete 5×5 Grid Backpack authoritative without changing Unplaced behavior or implementing every future Inventory mechanic.

The accepted implementation supports:

- bounded profile-authored width × height, with the current Grid Backpack at 5×5;
- item footprints independent from stack quantity;
- canonical X/Y placement;
- bounds validation;
- overlap rejection;
- deterministic reposition;
- cross-container move between compatible Grid/Unplaced semantics;
- item-count plus spatial-capacity validation;
- save/load/rebuild;
- remote owner-private UI interaction;
- hostile/stale intent fencing.

Rotation, automatic packing, weight, nesting, gravity, and 3D shelf mapping remain explicitly unsupported.

## 14.1 Item placement vs item footprint

The item instance should answer **where it is**.

The item content/prefab should answer **how much space that item type consumes**.

Conceptually:

```text
Item definition / content
    Grid footprint = 1×3

Item instance
    Grid placement = X=4, Y=1
```

For a bag-item:

```text
Bag outer footprint = 2×2
Bag interior container = 5×5
```

These facts are intentionally unrelated.

## 14.2 Optional authoritative placement state — implemented

Do not force list-only servers to carry fake grid coordinates.

`InventoryGridPlacement` exists only while an item is contained by a Grid parent. Unplaced and equipped items carry no fake grid coordinates. `InventoryGridFootprint` is immutable content policy and is not duplicated into the instance placement component.

---

# 15. Freeform and slot semantics

## 15.1 Freeform

Freeform supports UO-like bags where items have authored/canonical placement coordinates rather than occupying a rectangular cell lattice.

The same container can still be projected as a list or search view.

Freeform should remain authoritative placement semantics, not merely client-local drag position, if item position affects saved layout or multi-client consistency.

## 15.2 Slot

Slot semantics support fixed named/indexed placement such as:

- RuneScape-like bank slots;
- equipment slots;
- crafting input/output slots;
- weapon racks;
- fixed display stands.

Slot identities should be stable content-defined values rather than UI node identities.

---

# 16. Physical 3D containers — shelves, bookcases, racks

One major desired capability is mapping authoritative inventory placement directly into a 3D world model.

A bookshelf/shelf can be an actual Inventory container whose logical grid maps onto authored local transforms in the model.

Example:

```text
Bookcase logical inventory grid
    width  = N cells
    height = M cells / several shelf bands

Item A at logical cell (0,0)
Item B at logical cell (1,0)
Item C at logical cell (5,5)
    ↓
content-authored grid→local-transform mapping
    ↓
visible 3D objects on physical shelf
```

This allows:

- densely packed books taking one narrow cell each;
- sparse ingredients with intentional gaps;
- decorative organization controlled by actual inventory placement;
- same authoritative shelf contents viewed through a list/grid/admin UI if desired.

The shelf clutter is therefore not a decorative fake separate from inventory truth. The visible books/ingredients **are the items in the container**.

## 16.1 Gravity / settling

A vertical shelf grid needs deterministic support/gravity behavior.

If a coin is requested at the top of a five-cell-high shelf compartment, it should not remain visually floating.

Conceptually:

```text
requested grid placement
    ↓
validate footprint / horizontal position
    ↓
settle downward until next step would intersect
    shelf/support surface
    or an allowed supporting item
    ↓
commit canonical final placement
```

This should be authoritative placement normalization, not a client-only visual trick, so all clients agree where the item actually resides.

Gravity should generally alter the supported vertical axis while preserving deliberate horizontal gaps. It must **not** automatically horizontally pack a shelf unless that is a separate explicit policy.

Example:

```text
Book, Book, [gap], Bottle, [gap], Herb
```

The gaps remain intentional.

## 16.2 Support policy

Different physical containers may choose:

- shelf/floor surfaces only provide support;
- items may also support other items;
- arbitrary stacking forbidden;
- category-specific support rules.

Start simple. A bookshelf may initially use authored shelf surfaces only rather than supporting arbitrary towers.

## 16.3 Irregular physical layouts

A physical model may mark logical cells as unavailable/support-only because of geometry.

Example:

```text
XXXXXXXXXXXX
X..........X
X..........X
XXXXXXXXXXXX
X..........X
X..........X
XXXXXXXXXXXX
```

where `X` cells represent blocked/support structure and `.` cells are usable placement space.

## 16.4 Rendering/batching remains presentation-only

A shelf may logically contain dozens of individually interactable inventory items while the renderer batches their visuals into a MultiMesh/grouped representation when appropriate.

Inventory relationships and authority do not change because presentation batches draw calls.

---

# 17. Quantity and physical presentation

Authoritative quantity can influence visual representation without changing inventory semantics.

Example presentation-only policy:

```text
coin stack quantity
1       → one coin
2..5    → tiny pile
6..20   → medium pile
21+     → larger pile
```

Or arrows may render as a bundle; grain may render as a sack; books may simply be non-stackable.

The authoritative truth remains item identity + quantity + placement. Presentation chooses the visual representation.

---

# 18. Server configurability and policy resolution

UltraSim should support both simple and highly authored servers.

The current implementation already supports the top-level choice between Uniform and PerContainerProfile.

Longer-term, policy resolution should probably distinguish three meanings:

## Defaults

Used when nothing more specific overrides them.

## Overrides / profile choice

Container class/profile or prefab-specific choices.

## Mandatory server constraints

Rules that individual containers are never allowed to weaken.

Conceptual resolution:

```text
server mandatory constraints
    +
server defaults
    ↓
container rule profile
    ↓
container prefab/content policy
    ↓
persistent instance upgrades
    ↓
temporary runtime modifiers
    ↓
effective rules
```

Example:

```text
Server mandatory:
    NoContainmentCycles = true
    MaximumNestingDepth <= 4

Server defaults:
    Weight = disabled
    Nesting = enabled

Backpack profile:
    Grid = 6×5
    Footprints = enabled

Magic pouch profile:
    Grid = 4×4
    Weight = enabled
    NestedContentsCarryFactor = 50%
```

A server owner should also be able to say effectively:

> All bags must obey A, D, F, and X rules/settings.

Uniform mode is the simplest strong version of that. Future mandatory constraints allow globally enforced invariants while still permitting container-specific profiles for other dimensions.

---

# 19. Example game/server configurations

These are design examples, not implemented presets.

## Skyrim-like

```text
Placement        = Unplaced/List projection
ItemCount        = optional/large
Weight           = enabled
Footprints       = disabled
Nesting          = disabled or limited
```

## Ultima Online-like

```text
Placement        = Freeform
Weight           = optional/off
ItemCount        = container-specific
Nesting          = enabled
Viewer           = freeform bag
```

## Diablo II-like

```text
Placement        = Grid
Footprints       = enabled
Weight           = disabled
ItemCount        = often derived/defaulted from grid but independently bounded
Nesting          = usually disabled
Viewer           = grid
```

## RuneScape-like bank

```text
Placement        = Slot
Footprints       = disabled
Weight           = disabled
Viewer           = slot icons / tabs / search projections
```

## Zomboid/Kenshi-like

```text
Placement        = profile-dependent
Weight           = enabled
Nesting          = enabled
Nested weight    = full or reduced depending on container
Count/capacity   = enabled
```

## UltraSim hybrid

```text
Main backpack    → D2 Grid + footprints + optional weight
Reagent pouch    → nested magical Grid + reduced carried weight
Bank             → RuneScape Slot
Search/index     → Skyrim List projection over real inventories
World chest      → profile-specific storage
Bookcase         → Grid + gravity + physical 3D mapping
Equipment        → Slot/equipment relationship
```

---

# 20. Features should be optional, not fake-disabled

A server that does not use a mechanic should ideally avoid both semantic complexity and runtime cost for that mechanic.

Examples:

- list-only inventories should not maintain fake grid occupancy;
- no-weight servers should not maintain subtree weight caches;
- no-nesting servers should not perform ancestry propagation beyond what ordinary containment requires;
- freeform-only containers should not allocate grid occupancy structures;
- public world replication should not know anything about owner-private Inventory rules.

The architecture should prefer finite concrete capabilities and derived structures that exist only when an active game/profile needs them.

Avoid a giant universal interface where every mutation pays polymorphic/plugin overhead for rules that are not enabled.

---

# 21. Future ownership/requester/access semantics

Current V3 owner-private inventory uses an optional ECS entity owner and Admission-authorized actor path.

V2 had broader owner/requester vocabulary, and future gameplay may earn some of it back:

Possible owners:

- entity/player/NPC/pet/mount/follower;
- character;
- account;
- world object;
- guild;
- vendor;
- house/property;
- system.

Possible requesters/viewers:

- player session;
- character;
- NPC/AI;
- server system;
- admin/GM;
- script.

Do not introduce a giant generic ref framework merely because V2 had one. Add these identity domains when concrete gameplay systems require them, using V3's existing Accounting, Admission, persistence, and runtime identity contracts.

Multi-viewer containers remain a likely future requirement for:

- corpses/loot;
- trades;
- vendors;
- account bank;
- guild bank;
- GM inspection;
- shared world storage.

---

# 22. Transactions / trade / escrow — future

V2's generic transaction model remains useful design reference.

A future inventory transaction should avoid any state where the same item is simultaneously authoritative in both its source and trade/escrow destination.

Conceptual pattern:

```text
Begin transaction
    ↓
Suspend/reserve item from source
    ↓
transaction owns suspended item state
    ↓
Commit → atomically move to destinations
Cancel → atomically return to sources when valid
```

Potential users:

- player trade;
- auction escrow;
- crafting input escrow;
- loot reservation;
- mail attachments.

If source/destination disappears during cancel/commit, the system must emit an explicit unresolved/recovery state rather than silently duplicating, destroying, or rerouting an item.

This is not part of the current V3 core yet.

---

# 23. Future item/material weight

If/when Game materials become authoritative content, item material may influence:

- base mass;
- container reinforcement/weight capacity;
- durability;
- category-specific storage behavior.

Do not require the Inventory architecture to know about a future Materials subsystem today. Inventory should consume compiled item/container mass/capacity facts when a concrete Game content system provides them.

---

# 24. Current profile examples in Game content

As of I0.10, Game content owns stable inventory rule profile IDs for at least:

- Backpack;
- World Storage;
- Grid Backpack.

Backpack and World Storage resolve to operational `Unplaced` profiles with independent maximum item-count capacities. Grid Backpack resolves to an operational 5×5 `Grid` profile and uses an independently enforced instance item-count capacity (20 in the accepted vertical). Stable profile identity remains independent from presentation.

Current content compilation validates that selected container rules resolve and are operational before publishing Game content.

---

# 25. Design constraints for future implementation

The following should be treated as strong architecture guidance.

## 25.1 Preserve authoritative ownership

- Client intents are requests, never authoritative results.
- ECS remains authoritative mutable inventory truth unless a later explicit Game architecture changes it.
- Derived indexes/caches can be rebuilt from truth.

## 25.2 Preserve identity boundaries

- Runtime entity handles are not durable identity.
- Content/prefab identity is not instance identity.
- UI row/view identity is not Game item identity.
- Connection identity is not owner/character identity.

## 25.3 Bound work and memory

- All collections/indexes must have declared capacities/service envelopes.
- Nesting depth must be bounded.
- Grid occupancy work should be proportional to actual configured grid/item work, not global entity capacity.
- Disabled mechanics should not create persistent work loops.

## 25.4 Fail closed

- Unsupported Slot/Freeform/Weight/Nesting policy must reject before runtime publication; malformed or unsupported Grid policy must do the same.
- Capacity/invariant failure must not partially mutate authoritative state.
- Stale/foreign/duplicate remote intent must not advance authoritative state.

## 25.5 Do not mix presentation with Game truth

- Viewer layout must not silently mutate authoritative storage semantics.
- 3D shelf rendering is a mapping from semantic placement to presentation transforms.
- Renderer batching does not alter item/container relationships.

## 25.6 Do not force one game style

- Uniform global rules must be supported.
- Per-container mixed rules must be supported.
- Servers may choose no weight, no grid, no nesting, etc.

---

# 26. Suggested roadmap from the current checkpoint

The exact order can change when a real gameplay vertical earns something else, but the current natural progression is:

## I0.7 — authoritative ECS core — CLOSED

- persisted container/item relationships;
- generation-owned derived index;
- basic mutations;
- persistence/rebuild;
- content-driven items.

## I0.8 — authenticated remote/private vertical — CLOSED

- owner-authorized views;
- cross-container movement;
- split/equip/unequip;
- Network → Client Core → Godot;
- hostile/stale fencing;
- reconnect/generation replacement.

## I0.9 — policy/capacity/placement foundation — CLOSED

- Uniform vs PerContainerProfile;
- stable compiled rule profile IDs;
- independent item/grid/weight/nesting dimensions;
- viewer-neutral placement values;
- fail-closed unsupported mechanics;
- instance item-count capacity vs profile maximum.

## I0.10 — authoritative Grid placement — CLOSED

- authoritative grid placement state;
- immutable content-authored item footprints;
- bounded profile-authored width/height;
- transactional overlap/bounds validation;
- deterministic reposition and Grid ↔ Unplaced/equipment transitions;
- item-count + spatial capacity;
- save/load/rebuild;
- remote/private UI;
- Unplaced remains unchanged.

Explicitly deferred:

- weight;
- nested bags;
- gravity;
- shelf mapping;
- automatic packing;
- trade.

## I0.11 — candidate: dynamic capacity/modifier semantics

Once Grid exists, establish how persistent upgrades and temporary effects modify:

- item-count capacity;
- grid dimensions;
- later weight/nesting.

Freeze shrink/overflow semantics before temporary grid expansion becomes gameplay-visible.

## Later candidates

- Slot semantics/viewer;
- Freeform UO placement/viewer;
- weight and nested effective-weight propagation;
- nested containers;
- physical shelves/bookcases/racks;
- generic projections/search/index;
- bank/vendor/guild/account ownership;
- multi-viewer sessions;
- transaction/trade/escrow;
- lock/no-drop/no-trade/quest/reserved flags as gameplay earns them.

---

# 27. Non-goals / things not to prematurely generalize

Do not prematurely build:

- a universal Inventory plugin framework for every possible game rule;
- a generic owner/reference system merely to reproduce V2 vocabulary;
- fake Grid state for list-only servers;
- fake Weight state for servers with weight disabled;
- a giant per-container blob containing every possible boolean feature toggle;
- Network-level Inventory semantics;
- client-owned authoritative placement;
- automatic runtime policy switching based on performance;
- a complete trade/bank/vendor/crafting framework before actual gameplay needs it.

Prefer concrete finite capabilities and real gameplay-earned seams.

---

# 28. Summary model

The intended long-term shape can be summarized as:

```text
GAME CONTENT / SERVER CONFIG
    stable InventoryRuleProfiles
    server Uniform or PerContainerProfile selection
    defaults + mandatory constraints
    item definitions / footprints / mass / capabilities

                ↓

AUTHORITATIVE CONTAINER INSTANCE
    persistent identity
    content/profile identity
    owner relationship
    current item-count capacity
    future persistent upgrades/modifiers

                ↓

AUTHORITATIVE ITEM INSTANCE
    persistent identity
    prefab/content identity
    parent relationship
    quantity
    equipment relation
    optional placement state when required

                ↓

DERIVED INVENTORY INDEX
    container membership
    item locations
    equipped lookup
    optional grid occupancy for Grid-contained items
    future optional nested/subtree weight

                ↓

GAME UI / PROJECTIONS
    D2 grid
    UO freeform
    RuneScape slot
    Skyrim list/search
    admin/GM table
    physical 3D shelf mapping

                ↓

CLIENT PRESENTATION
    Godot UI / world models / batching
```

The system should let a server choose one consistent style or author a mixed world where different containers genuinely behave differently.

The core semantic commitment is:

> **Inventory is authoritative Game state with configurable container rules. Placement, capacity, weight, nesting, and viewer style are separate concerns that may be composed intentionally rather than collapsed into one mandatory inventory model.**

---

# 29. Repository landmarks at this checkpoint

Current V3 implementation areas:

- `Game/Inventory/InventoryComponents.cs`
- `Game/Inventory/InventoryContent.cs`
- `Game/Inventory/InventoryGenerationState.cs`
- `Game/Inventory/InventoryIndex.cs`
- `Game/Inventory/InventoryRules.cs`
- `Game/Inventory/README.md`
- `Game/UI/InventoryUi.cs`
- `Game/Composition/GameContent.cs`
- `_Tests/UltraSim.Game.Inventory.Tests/Program.cs`
- `_Tests/UltraSim.S0.Godot.Tests/S08RemoteInventoryAcceptance.cs`

Useful V2 archaeology/reference:

- `_Archive/_V2/Kernels/Inventory/README.md`
- `_Archive/_V2/Kernels/Inventory/*`
- `_Archive/_V2/Server/Inventory/README.md`
- `_Archive/_V2/Server/Inventory/Rules/*`
- `_Archive/_V2/Server/Inventory/Store/*`

V2 is reference material, not current authority.
