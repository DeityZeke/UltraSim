# Inventory

Inventory is authoritative Game state. ECS components retain container ownership,
item location, quantity, and equipment relationships; `InventoryIndex` is a
generation-local derived accelerator rebuilt from ECS truth after restore.

Container storage semantics are independent from their viewer. A list UI does
not imply authoritative list positions, and Grid/freeform viewers do not change
item identity or owner-private authorization.

Mutations are transactional Game operations. `Move`, `Split`, `Equip`, and
`Unequip` validate complete source/destination topology and capacity before
publishing ECS plus index state. Accepted mutations are projected as granular UI
row changes; a client command is intent, never authoritative truth.

## Owner-private UI

`InventoryUiController.OpenOwned` is the private-container route. Trusted Game
composition supplies the actor already authorized by Admission. The controller
proves that the source and every explicit transfer destination belong to that
actor, retains their complete persistent keys, and revalidates ownership for
every action and generation replacement. Loss of ownership closes the view.

`Open` remains the trusted unrestricted route for ownerless public-world
containers such as chests. It must not be used as a remote participant
authorization shortcut.

Inventory remains owner/private state. It is transported through the generic UI
semantic protocol and is not published through dynamic-public AOI profiles such
as `ChunkPublication`.

## I0.8 remote vertical

The native Godot acceptance composes the production path end to end:

```text
authenticated connection
    -> Accounting / Admission / CharacterSelect
    -> owner-authorized InventoryUiController
    -> RemoteUiSemanticPort
    -> secure Network protocol
    -> RemoteUiClientBridge / UiClientRuntime
    -> GodotUiRenderer
```

It exercises native row/button interaction for cross-container move, split,
equip, and unequip; rejects foreign containers and stale/duplicate/unknown-view
actions; resynchronizes an open view after save/load generation replacement; and
reconnects through a fresh application session without disclosing another
actor's inventory.

## I0.9 container-policy foundation

`InventoryRuleCatalog` is immutable compiled Game policy. A server selects either
content-authored per-container profiles or one explicit uniform profile. The
current content set assigns separate stable profiles to ordinary backpacks and
world storage; both intentionally use `Unplaced`, so existing list projection
retains no fake authoritative list/grid coordinates. `GridBackpack` selects the
operational Grid profile described below.

Effective rules currently compose:

```text
server scope selection
    -> content-authored profile
    -> persistent instance item-count and Grid dimensions
    -> generation InventoryLimits
```

Item count, grid dimensions, weight, and nesting are distinct capacity fields.
`InventoryPlacement` likewise defines tagged `Unplaced`, `Grid`, `Slot`, and
`Freeform` values without viewer or Godot vocabulary. I0.9 closed on the
unplaced/item-count floor. Weight, nesting, Slot, and Freeform profiles still fail
closed until their authoritative facts and mutations are earned.

## I0.10 authoritative Grid placement

One concrete `GridBackpack` profile now operates as a 5×5 Grid with a separately
enforced item-count capacity. Immutable item footprint is prefab policy: absence
of `InventoryGridFootprint` canonically means 1×1, while the sword carries a
persisted 1×3 content footprint. Stack quantity never multiplies footprint.

Instance location is optional authoritative state:

```text
Unplaced/equipped item
    InventoryItem only

Grid-contained item
    InventoryItem
    + InventoryGridPlacement(X,Y)
```

Cold composition derives placement-bearing archetypes from each complete item
shape and prepares generation-fenced add/remove routes before world seal. Grid
creation, reposition, Grid↔Unplaced movement, equip/unequip, split with explicit
placement, persistence, and rebuild all use the same transactional authority.
Bounds, overlap, item count, and ECS structural capacity reject before publishing
domain state; automatic packing and rotation are intentionally absent.

The owner-private UI exposes the 5×5 cells through the generic UI semantic
protocol. The server derives the selected item's footprint from compiled content;
the client submits only a bounded cell choice. The remote Godot acceptance proves
native selection/reposition, hostile cell rejection, save/load resynchronization,
and exact private disclosure without routing Inventory through public AOI.

This checkpoint deliberately does not add automatic packing, weight propagation,
nested-container topology, physical shelves, or viewer selection.

## I0.11 dynamic instance capacity

The compiled Grid Backpack profile now owns a 10×10 maximum and a 5×5 initial
size. Each persisted `InventoryContainer` retains its current item-count limit and
current Grid width/height as one atomic capacity vector; Unplaced containers retain
canonical zero Grid dimensions.

`SetEffectiveCapacity` can grow or shrink item count and Grid dimensions
independently within the selected profile maximum. Shrink is deliberately
conservative: it is rejected if the current item count exceeds the requested limit
or any retained footprint would fall outside the requested Grid. There is no
automatic packing, deletion, dropping, or grandfathered overflow.

An explicit server-owned UI refresh republishes changed dimensions through the
existing owner-private semantic path. The native remote acceptance proves a
5×5→6×5 upgrade, 30-cell Godot presentation, and exact save/load restoration.
Inventory does not decide why an upgrade occurs; achievements, effects, or admin
policy remain future Game consumers of this bounded authority operation.
