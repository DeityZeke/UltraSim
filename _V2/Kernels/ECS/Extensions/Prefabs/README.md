# Prefab Extension

Prefabs are now in Phase 2 startup rather than pure placeholder status.

Current pieces in place:

- `IPrefab<TSelf>`
- `ITemplatePrefab<TSelf>`
- `IResolvedPrefab<TSelf>`
- `PrefabDefinition`
- `PrefabCatalog`
- immediate `World.CreatePrefab(...)` helpers
- resolver-backed `World.CreatePrefab(string key)` / `TryCreatePrefab(...)`
- deferred `EnqueueCreatePrefab(...)` structural queue integration for definitions, typed prefabs, and resolver-backed keys

Current intentional limits:

- there is no static global prefab registry
- file-backed/data-driven loading is still deferred
- resolved-column application exists, but resolved-column caching/other deeper spawn optimizations are still deferred

Expected later work:

- data-driven prefab loading
- richer prefab catalogs and import pipelines
- additional spawn-path optimization once real workloads justify it
