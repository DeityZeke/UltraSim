# Inventory

Inventory is authoritative Game state. ECS components retain container ownership,
item location, quantity, and equipment relationships; `InventoryIndex` is a
generation-local derived accelerator rebuilt from ECS truth after restore.

Container storage semantics are independent from their viewer. A list UI does
not imply authoritative list positions, and future grid/freeform viewers do not
change item identity or owner-private authorization.

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
current content set assigns separate stable profiles to backpacks and world
storage; both intentionally use `Unplaced`, so existing list projection retains
no fake authoritative list/grid coordinates.

Effective rules currently compose:

```text
server scope selection
    -> content-authored profile
    -> persistent instance item capacity
    -> generation InventoryLimits
```

Item count, grid dimensions, weight, and nesting are distinct capacity fields.
`InventoryPlacement` likewise defines tagged `Unplaced`, `Grid`, `Slot`, and
`Freeform` values without viewer or Godot vocabulary. Only unplaced/item-count
profiles are operational in I0.9. A selected grid, weight, or nesting profile
fails closed during content binding, creation, or rebuild until authoritative
item footprint/weight/nesting state and corresponding mutations are earned.

This checkpoint deliberately does not add grid occupancy, weight propagation,
nested-container topology, physical shelves, viewer selection, instance upgrade
stacks, or temporary modifiers. Those features must extend the concrete policy
resolution and transactional Game mutations; they must not turn the catalog into
a generic inventory-rule interface graph.
