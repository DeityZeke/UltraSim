# ECS full-world persistence

ECS persistence captures and restores one sealed world's durable logical state.
It does not define save-set filesystem transactions or game participant
composition; those layers are documented in
[`../../Docs/PERSISTENCE.md`](../../Docs/PERSISTENCE.md).

## Durable identity

`PersistentWorldId` identifies the durable world. `PersistentEntityId` is unique
within that world. `PersistentEntityKey` combines both when identity leaves the
owning world. Runtime `Entity`, slot/generation, row, chunk, archetype ID, and
world-local component/prefab handles are never durable identity.

Allocator high-water state is persisted so restored/new identities do not reuse
previous durable IDs incorrectly.

## Schema and codecs

Persistent components use stable canonical keys, schema versions, and generated
or explicitly registered codecs. The saved version is supplied to the current
reader. Aliases, drops, and migrations are explicit and validated; contradictory
or chained declarations reject.

Saved runtime shape is separate from persisted payload. This preserves tags,
transient/derived membership policy, enable planes, and the distinction between
"component exists" and "component has serialized bytes."

Entity references encode save ordinals plus the required identity validation.
Restore first creates every durable identity, then hydrates columns and resolves
references. Cross-participant authoritative references resolve only against the
staged candidate provider set.

Prefab provenance uses stable content keys. Persistent system state uses stable
system persistence IDs and framed schema versions.

## Snapshot container

The container has bounded counts/offsets/lengths, stable shape/component tables,
fixed-width column sections where applicable, enable/provenance/system sections,
schema/catalog fingerprints, sequence, and integrity digest. Parsing validates
canonical ordering, unique IDs/ordinals, section containment/nonoverlap, padding,
boolean forms, trailing data, configured ceilings, and digest before publishing
state.

Artifact and temporary-memory limits are enforced before proportional copies or
allocations. After schema and codec resolution, restore admission includes the
largest `rowCount * current runtime component size` whole-column decode buffer,
including historical direct readers whose encoded rows may be much smaller.
The reported estimate covers kernel-owned managed buffers known to the restore
plan; it is not an exact CLR/process working-set bound or a bound on allocations
inside application-owned migrators/reconstructors. Current artifacts are
byte-array bounded and therefore cannot claim sizes beyond `int.MaxValue` even
if a host filesystem supports larger files.

## Capture

Capture occurs at an owner-controlled frozen logical boundary. One exclusive
capture operation scope begins before persistent-system size/write callbacks and
continues through logical ECS collection. Retained facade access—including
tick, mutation, disposal, or nested capture—rejects during that scope; callback
failure releases the scope in `finally`. Generated column codecs avoid
reflection, boxing, and per-value objects. Ordinal maps provide bounded O(1)
reference translation. Capture produces immutable owned snapshot state that can
be encoded after authority is released.

Sparse-capacity worlds may pay entity-capacity-sized ordinal maps; benchmark
profiles explicitly disclose capacity, live population, reference density,
temporary memory, allocation, and throughput before representation changes are
accepted.

## Restore

Restore is staged:

```text
parse and authenticate
bind schema/content/system catalogs
plan capacity and shapes
allocate every entity identity
hydrate columns/enable/provenance/system state
resolve references
validate canonical logical state
publish the prepared world once
```

The staging factory must return a newly created, unsealed world. Restore adopts
cleanup ownership only after that side-effect-free admission check. Rejecting a
running, disposed, or otherwise invalid factory result leaves it caller-owned;
an admitted new candidate is still disposed if later construction, hydration,
or validation fails.

Failure before publication disposes an adopted candidate and leaves active
authority unchanged. Prepared publication is single-use; callbacks cannot be
retried after they might have partially published. Restored worlds begin with
clean change and lifecycle observation unless an explicit future contract says
otherwise.

## Compatibility and failure

Expected format/schema/capacity/corruption failures are classified results or
exceptions before mutation. Unexpected post-proof invariant failure faults the
candidate/owner. Decoders treat artifact bytes as untrusted even when loaded from
local storage.

Native and pinned storage backends share logical snapshot semantics. Exact byte
layout and version compatibility are frozen by format tests and immutable
artifacts, not by runtime local IDs.
