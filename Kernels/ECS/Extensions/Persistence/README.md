# Persistence Extension Placeholder

Persistence is a second-wave import for the new ECS kernel.

The core kernel intentionally does not carry the old save/load implementation during Phase 1.

Current persistence-facing seams already preserved:

- `IDataReader`
- `IDataWriter`
- `IEcsPersistenceAdapter`
- `ISerializableComponent<T>`
- world/system save-load hooks
- `WorldPersistenceView`
- `PersistenceArchetypeView`

Expected future work:

- persistence ids and signature grouping
- binary or other persistence adapters
- system-owned state persistence
- type registry and versioned component serialization
