# Persistence Extension

Persistence is now in early Phase 2 startup rather than pure placeholder status.

Current pieces in place:

- `PersistenceIdAttribute`
- `PersistenceKind`
- `PersistenceTypeRecord`
- `PersistenceTypeRegistry`
- `PersistenceComponentDescriptor`
- `PersistenceSignature`
- `PersistenceSignatureBuilder`
- `EntityPersistenceSnapshot`
- `IEcsPersistenceAdapter`
- `ISerializableComponent<T>`
- world/system save-load hooks
- `WorldPersistenceView`
- `PersistenceArchetypeView`
- `World.CapturePersistenceSnapshot(...)`
- `World.RestorePersistenceSnapshot(...)`

Current intentional limits:

- persistence is currently an in-memory snapshot/restore layer, not a file format
- `IDataReader` / `IDataWriter` remain minimal adapter seams
- there is no binary save-set writer/reader yet
- system persistence still uses only the existing `SystemBase.Serialize/Deserialize` hooks
- component versioned bit-level serialization is still deferred beyond the current in-memory snapshot slice

Expected future work:

- binary or other persistence adapters
- system-owned state persistence
- richer type/version databases and manifest work
- file-backed save/load pipelines
