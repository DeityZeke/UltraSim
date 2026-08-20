# Server Gameplay

`Server/Gameplay` owns engine-agnostic gameplay component shapes and small
runtime descriptors. It should stay below networking, UI, and shell wrappers.

## Spawner Primitives

The first spawner slice is intentionally only a foundation:

- `ServerSpawnerComponent` is the ECS component for a spawner entity.
- `ServerSpawnerRuntimeState` carries active-count and tick bookkeeping.
- `ServerSpawnerRegistry` maps stable spawn-set keys to admin-readable metadata.
- `ServerSpawnerKey` computes the stable `ulong` key hash stored in ECS.

ECS components stay unmanaged, so strings such as `orc_camp` or display names do
not live inside the component. Components store the stable hash; admin panels and
commands resolve that hash through the registry.

Deferred content work:

- actual spawn execution systems,
- spawn set package loading/unloading,
- smart activation by player proximity,
- admin show/hide/edit panels,
- persistence for authored spawner instances.
