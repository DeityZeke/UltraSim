# ECS 3.4.1 Full-World Persistence

Status: authoritative persistence contract for ECS 3.4.1, accepted 2026-08-12.

## Scope

ECS persistence is an engine-neutral, whole-world snapshot participant. It owns
ECS identity, topology, authoritative component payload, selected enable state,
prefab provenance, entity-reference remapping, and opt-in scheduler-system state.
The server-level save-set coordinator owns the common capture epoch, other
participants, durable files, recovery policy, and atomic publication.

This checkpoint deliberately does not implement incremental snapshots,
merge/append restore, networking checkpoints, compression, or a V2 binary
importer. The production restore operation constructs a complete staging world
and publishes it only through a host-owned generation commit.

## Durable identity

Runtime `Entity`, `WorldId`, archetype IDs, chunk handles, rows, component IDs,
prefab handles, and scheduler handles are generation-local and never serialized
as durable identity.

- `PersistentWorldId`: 64-bit nonzero world identity.
- `PersistentEntityId`: 64-bit nonzero monotonic identity within that world.
- `PersistentEntityKey`: the 16-byte pair used outside the owning world.
- `SaveEntityOrdinal`: a dense 32-bit snapshot-local reference; `uint.MaxValue`
  is null.

The optional fixed-capacity identity side table is configured by
`EcsWorldOptions.Persistence`. IDs are assigned as part of successful entity
creation, survive runtime slot reuse, are never reused,
and restore reinstates the allocator high-water mark before publication. Capture
sorts entities by persistent ID, assigns save ordinals, and encodes internal
entity references as ordinals. Restore allocates every identity first and only
then decodes reference-bearing component columns.

## Component persistence contract

`[EcsComponent]` supplies a stable nonzero `PersistenceId` and a policy:

- `Value`: save shape membership and canonical component payload.
- `MembershipOnly`: save shape membership and reconstruct the runtime default.
- `Derived`: save shape membership and invoke a registered reconstructor after
  authoritative columns are hydrated.
- `None`: the component cannot occur in an occupied reconstructible shape.

`PersistEnableState` is explicit and valid only for enableable components. It
emits a separate packed enable bitplane; enable state is not inferred from
payload or change history.

The generator emits little-endian fixed-width current codecs and 128-bit schema
fingerprints for supported unmanaged fields/properties. `Entity` members must be
marked `[PersistenceEntityReference]`; they are ordinal-remapped rather than
copied. `PersistentEntityKey` is encoded directly. Unsupported layouts produce a
generator diagnostic and use an explicitly registered reflection-free codec.

`IPersistenceComponentReader<T>` registers one direct saved-version-to-current
reader. Migration chains are not used. Stable-ID aliases and explicit drops are
cold, mutually exclusive historical-ID declarations; aliases cannot chain through
other aliases or contradict current/drop identities. Fingerprints are compatibility diagnostics and fast
reject hints, never the only proof of compatibility.

## Runtime shape and migration

Saved runtime shape and saved value payload are separate facts. Every occupied
runtime component requires stable membership identity, including tags,
membership-only values, and derived values. This prevents the V2 failure mode in
which a shape collapsed to only the serializable payload columns.

Ordinary restore resolves each saved stable component ID into the current schema.
When a saved shape cannot map one-to-one, an explicit `IPersistenceShapeMigrator`
may map the complete sorted saved ID set to a complete current component set.
The migrator receives saved column bytes and the already-created destination
entities and may implement N:M split/merge. No heuristic shape rewrite is
performed.

After value hydration and shape migration, registered
`IPersistenceDerivedReconstructor` instances rebuild derived columns. Restore
writes use clean storage primitives: they do not advance change versions or emit
Created/Added/Removed/Destroyed lifecycle facts.

## Prefab provenance

Prefab provenance is optional metadata, not restore authority. Instantiation
records the stable `PrefabKey` hash in the identity side table. A snapshot stores
the canonical key text once, validates the stable hash on load, and records the
hash per entity. Runtime prefab handles and compiled catalog indices are never
serialized. Saved mutable components remain authoritative even when the current
prefab default has changed.

## Persistent system state

Systems opt in through a stable nonzero `SystemDefinition.PersistenceId`, a
nonzero state schema version, and `IPersistentSystemState`. State records are
sorted and framed by stable ID/version. The scheduler tick is restored, but wave
assignment, worker state, profiling, prepared handles, change cursors, and other
reconstructible scheduler machinery are not persistent state.

Missing required state, unknown saved state, or unsupported state versions reject
the staging generation. System state is applied only after the staging scheduler
has initialized and before the world begins running.

## Snapshot container

The format is explicitly versioned and little-endian. A fixed header names the
world, snapshot sequence/time, allocator high-water, schema hint, entity/shape
counts, directory location, and whole-artifact SHA-256. Directory entries are
bounds-checked, physically ordered, padding-zero checked, uniquely and contiguously
numbered where required, and carry per-section CRC32. Reserved header/directory
bytes and generated boolean payload bytes are canonical, not merely tolerated.

Sections include:

1. component schema records;
2. complete stable runtime shapes;
3. prefab stable-key table;
4. persistent entity records;
5. shape-local save ordinals;
6. canonical fixed-width component columns;
7. optional enable bitplanes;
8. framed persistent system states.

Capture order is deterministic: schema by stable component ID, shapes
lexicographically by stable membership IDs, entities by durable ID, and column
rows by save ordinal. Equal logical worlds therefore produce equal bytes when
sequence/time metadata is equal.

## Capture boundary and allocation policy

`EcsPersistence.BeginCapture` requires an idle owner thread and an empty
structural arena. It collects and returns owned canonical raw state while the
host holds the authoritative capture boundary. `CompleteCapture` performs
canonical encoding/checksums afterward without consulting the live world. The
one-call `Capture` convenience composes both for standalone use. The active
save-set participant always uses the split form, and disk I/O occurs only after
the shared epoch has been released.

The current 3.4.1 implementation uses proportional arrays and per-shape records;
it does not box component values or create per-entity managed objects. It is not a
zero-allocation operation, and the measured peak/allocation report is part of the
accepted evidence. Persistence is a cold checkpoint path; the ordinary warmed ECS
tick remains allocation-free.

Two capture maps are deliberately sized to configured entity capacity: one maps
runtime slots to save ordinals and one validates generations. This buys direct
O(1) reference encoding, but a sparse world pays approximately eight bytes per
configured slot during capture even when few slots are alive. The opt-in
`UltraSim.ECS.Persistence --sparse-capacity` benchmark records the 8M-capacity /
100K-live crossover at low, normal, and dense reference populations before any
live-sized hash or high-water-sized alternative is admitted.

Prefab provenance text is not an open-ended runtime input in 3.4.1. Keys come
from the immutable compiled content catalog or the finite restored snapshot key
table; there is no public arbitrary-key mutation API. A registered key may remain
after its last entity dies. `EcsWorld.RegisteredPrefabKeyCount` exposes that cold
retention, while the CLR dictionary/string overhead remains outside fixed-array
reserved-byte accounting. Hot/reloadable authoring must revisit this bound before
it may introduce an evolving runtime vocabulary.

## Host save-set transaction

`Server/Persistence` provides the engine-neutral coordinator contract:

```text
shared frozen epoch
  -> capture every participant
  -> durable participant files
  -> checksummed manifest
  -> fsync pending directory
  -> rename complete generation
  -> fsync generations directory
  -> durable temporary CURRENT pointer
  -> atomic pointer replace
  -> fsync root directory
```

Participant paths are restricted safe tokens and every load verifies byte count
and SHA-256. Declared participant artifact limits are enforced by the destination
stream while writing, not merely checked after materialization. Startup may
select strict `CURRENT` or the newest complete compatible generation. `.pending`
directories are never load candidates.

Save writers acquire one cross-process root lease before cleaning abandoned
pending state, selecting a forward-only sequence, encoding, and publishing an
already owned capture. Concurrent coordinators may capture independently, but
only the lease holder may touch the save-set directory; therefore one writer
cannot delete another active writer's staging directory or move `CURRENT`
backward. Ordinary in-process failures remove their
private pending generation; process termination leaves it for the next exclusive
writer to clean.

Load is one transaction across participants:

```text
Prepare all -> Resolve all -> Validate all -> host Commit(generation)
```

Participants may not mutate live state during prepare/resolve/validate. The host
commit callback is the single, exactly-once publication attempt. If host code
throws after partial publication the prepared generation becomes terminal and
cannot be retried. Fault-injection tests cover
every durability boundary and require recovery to expose either the previous
complete generation or the new complete generation—never a hybrid.

`DurableFileSystem` supplies the platform durability adapter. Linux/macOS use
directory `fsync`; Windows opens directory handles with backup semantics and
flushes them through the Win32 durability primitive. Other platforms fail closed
and must provide an adapter rather than silently claiming crash durability.

## Untrusted input and failure policy

Snapshot and manifest bytes are hostile input. The current container is
array-backed, so its public artifact ceiling is `Array.MaxLength`; defaults no
longer advertise impossible multi-gigabyte artifacts. `PersistenceLimits` bounds
the artifact, entities, shapes, components, sections, payload, strings, and
temporary work before the immutable artifact clone and section copies are
allocated. The reported temporary estimate conservatively includes proportional
parse ownership, not only small restore scratch. Parsing rejects truncation,
overflow, overlap,
duplicates, inconsistent counts, invalid ordinals, malformed UTF-8, nonzero
enable padding, invalid prefab hashes, CRC failure, and whole digest failure.

All expected incompatibility and corruption occurs before live publication.
Restore exceptions dispose the staging world. The active generation is untouched.
There is no partial live-world rollback path because live mutation never begins.

## Public usage outline

```csharp
var identity = new PersistenceIdentityOptions(new PersistentWorldId(worldKey));
var world = new EcsWorld(schema, optionsWithPersistence);
var catalog = GeneratedSchema.CreatePersistenceCatalog(builder =>
{
    // Direct historical readers, aliases, drops, shape migrations, and
    // derived reconstructors are registered here when needed.
});

using PersistenceWorldCapture owned = EcsPersistence.BeginCapture(
    world, catalog, new PersistenceCaptureOptions(sequence, unixMilliseconds));
// Release the authoritative world epoch here.
PersistenceSnapshotArtifact artifact = EcsPersistence.CompleteCapture(
    owned, out PersistenceCaptureReport capture);

PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(
    artifact.Bytes, catalog, restoreOptions);
EcsWorld staging = plan.Restore(CreateStagingWorld, out PersistenceRestoreReport restore);
```

The host owns `staging` after successful restore and may publish it only as part
of the complete participant generation.

## Tests and expected evolution

3.4.1 tests cover canonical round trip, durable slot reuse, internal references,
external durable keys, missing-current-prefab provenance, tags/membership,
enable planes, lifecycle/change cleanliness, N:1 shape
migration, derived reconstruction, persistent system state/tick, corruption and
bounds mutations, strict temporary-memory accounting, long component keys,
zero-width codec rejection, static-member generation, canonical padding/order/
boolean rejection, packed-row-order independence, byte-exact Native/Pinned capture,
serial/C8 scheduled-state parity, failed participant preparation, bounded
participant writes, malformed-pointer compatible recovery, prepared-key and
manifest/snapshot-sequence validation, exactly-once publication attempts,
cross-process writer serialization, actual child-process termination, and crash
injection at every save-set publication boundary.

Future tests remain mandatory as formats evolve: golden fixtures, direct
historical-reader matrices, optional/missing system policy, more randomized world
models, structured fuzzing, large strings/payload limits, cross-participant
reference graphs, and platform crash harnesses. Test evidence may change the
implementation direction; stable semantic IDs and transactional publication are
the contracts, not the current internal byte-builder API.

The next likely integrations are an ECS save-set participant wired into the real
server host, inventory/account durable-reference resolution, and a storage-level
network checkpoint that reuses the immutable canonical snapshot/column concepts
without coupling the ECS to the Network kernel.
