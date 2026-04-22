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

Current intentional limits:

- prefab spawning is immediate-only for now
- there is no static global prefab registry
- file-backed/data-driven loading is still deferred
- resolved-column application exists, but resolved-column caching/other deeper spawn optimizations are still deferred

Expected later work:

- deferred prefab spawn queue integration
- data-driven prefab loading
- richer prefab catalogs and import pipelines
- additional spawn-path optimization once real workloads justify it
