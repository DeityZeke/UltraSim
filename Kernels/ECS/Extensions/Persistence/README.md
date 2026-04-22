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
- `BinaryDataWriter`
- `BinaryDataReader`
- `BinaryEntityPersistenceSerializer`
- `BinaryWorldPersistenceAdapter`
- `IEcsPersistenceAdapter`
- `ISerializableComponent<T>`
- world/system save-load hooks
- `WorldPersistenceView`
- `PersistenceArchetypeView`
- `World.CapturePersistenceSnapshot(...)`
- `World.RestorePersistenceSnapshot(...)`

Current intentional limits:

- persistence now has both:
  - an in-memory snapshot/restore layer,
  - a narrow single-stream binary adapter on top of that model
- `IDataReader` / `IDataWriter` remain minimal adapter seams
- system persistence still uses only the existing `SystemBase.Serialize/Deserialize` hooks
- broader multi-file save-set/manifest work is still deferred
- component versioned bit-level serialization now has a working `BitWriter` / `BitReader` base, but broader schema/version database work is still deferred

Expected future work:

- system-owned state persistence
- richer type/version databases and manifest work
- broader file-backed save/load pipelines
