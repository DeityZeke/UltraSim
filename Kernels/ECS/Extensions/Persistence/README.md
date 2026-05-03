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
- multi-file save-set/manifest coordination now lives above this extension in
  `Server/Persistence`; the ECS extension remains responsible for world
  snapshot/restore and component binary serialization only
- component versioned bit-level serialization now has a working `BitWriter` /
  `BitReader` base, but broader schema/version database work is still deferred
- a future source-generator pass should generate component serializers,
  migration content records, and admin metadata from annotated component fields;
  that generated output should feed this component persistence layer, not create
  entity classes or bypass ECS dirty tracking

Expected future work:

- system-owned state persistence
- richer component type/version databases
- source-generated component serializer and migration scaffolding
- broader file-backed save/load pipelines
