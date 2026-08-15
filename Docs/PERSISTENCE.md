# Persistence architecture

UltraSim persistence is four cooperating layers, not one serializer:

```text
ECS persistence            world identity, shape, columns, references, system state
Server save-set            participant transaction and crash-durable publication
Game persistence           participant composition and derived-state rebuild
Runtime publication        one coherent restored ServerGeneration
```

## ECS snapshot

ECS persistence records stable world/entity/component/content identity rather
than runtime slots, rows, chunk handles, or local archetype IDs. Saved runtime
shape is distinct from persisted component payload so tags and transient/derived
membership are not accidentally inferred from serialized value columns.

Capture assigns dense save ordinals. Restore allocates identities first, then
hydrates values and fixes entity references through the ordinal remap. Generated
codecs own component schema/version conversion. Unknown, aliased, dropped, or
migrated data follows explicit catalog policy; malformed input never becomes a
partially live world.

See [`../Kernels/ECS/PERSISTENCE.md`](../Kernels/ECS/PERSISTENCE.md) for the
container and component contract.

## Save-set transaction

The server coordinator gives all participants one sequence/epoch. Capture is a
brief owner-thread authority operation that returns immutable owned artifacts.
Encoding, hashing, file I/O, flush, rename, manifest, and `CURRENT` publication
occur after the authority lease is released and may run on a persistence worker.

Save publication is:

```text
capture every participant
write a private pending generation
flush artifact bytes and metadata
validate hashes, lengths, keys, and sequence
publish the complete generation directory
atomically publish CURRENT last
```

Only cancellation before capture begins is supported. Once the coordinator owns
a captured save set, durable publication runs to completion or reports failure;
it does not intentionally abandon a half-publication.

An exclusive coordinator lock prevents concurrent writers from moving `CURRENT`
backward or deleting another writer's pending state. Crash durability supports
Windows, Linux, and macOS; unsupported platforms reject explicitly.

## Restore and publication

Participant load is transactional:

```text
Prepare -> Resolve -> Validate -> Commit
```

All participants prepare before reference resolution. Authoritative references
resolve only against staged providers declared for the candidate; they never
fall back to the active generation. Limits cover artifacts and temporary memory
before proportional allocation. Integrity, canonicality, sequence, schema, and
capacity failures reject before publication.

Game persistence combines ECS, Accounting, and the authoritative clock. It then
rebuilds Inventory's derived index and constructs the complete
`GameGenerationState`. Runtime publishes that prepared root only after the old
generation lease has ended. Existing admission/UI sessions rebind by durable
identity or detach; generation-local `Entity` values are never silently carried
forward.

Persistent game component changes follow the same explicit version rule. N0.6
advanced `GameSimulationState` to version 2 when it added `InputVelocity`; the
catalog's direct v1 reader restores the former tick-marker payload and defaults
the new velocity to zero. The compatibility path is executable evidence, not a
same-version reinterpretation of wider bytes.

Restore starts with clean change/lifecycle observation rather than fabricating a
gameplay-wide creation storm.

## Recovery

Strict recovery requires the selected current generation to be complete and
valid. Compatible recovery may consider other complete generations only for
classified corruption/not-found failures, never arbitrary implementation,
out-of-memory, or transient I/O exceptions. Paths are contained below the save
root. Manifests and artifacts cross-check participant keys, lengths, hashes, and
sequence.

Immutable prepared surfaces do not expose mutable backing arrays/dictionaries.
Prepared ownership is single-use: publication attempts cannot be retried after a
callback may have partially committed.
