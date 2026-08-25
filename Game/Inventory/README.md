# Inventory

Inventory is authoritative Game state. ECS components retain container ownership,
item location, quantity, and equipment relationships; `InventoryIndex` is a
generation-local derived accelerator rebuilt from ECS truth after restore.

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

