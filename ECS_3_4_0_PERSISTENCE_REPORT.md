# ECS 3.4.0 Persistence Acceptance Report

Date: 2026-08-12  
Profile: `ecs-3.4.0-full-world-transactional-persistence`  
Decision: accepted as ECS 3.4.0

## Executive result

ECS 3.4.0 is a coherent whole-world persistence milestone rather than a binary
serializer bolted onto live storage. It establishes durable entity identity,
canonical generated column encoding, complete runtime-shape reconstruction,
reference remapping, explicit migration/reconstruction, clean staging restore,
persistent system-state framing, and one crash-durable multi-participant
generation transaction.

The milestone passes its principal scale gates:

| Scale | Capture mean / p95 | Preflight mean / p95 | Restore mean / p95 | Sum of phase means | Authoritative pause mean / p95 | Logical encode | Artifact |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 100K | 69.00 / 95.24 ms | 28.58 / 29.63 ms | 48.08 / 100.31 ms | 145.67 ms | 36.05 / 61.21 ms | 1.333 GiB/s | 11,613,472 B |
| 1M | 463.40 / 469.10 ms | 296.55 / 341.39 ms | 280.73 / 288.90 ms | 1,040.69 ms | 167.35 / 169.35 ms | 2.144 GiB/s | 116,125,968 B |

Both scales produce byte-identical canonical recaptures. The 1M workload is
116.126 bytes/entity, finishes well below the 2 s round-trip gate, keeps the
measured authoritative pause below 250 ms, and exceeds the 1 GiB/s fixed-width
logical encode target.

## Accepted architecture

### Identity and reference model

- 64-bit `PersistentWorldId` plus 64-bit monotonic `PersistentEntityId` form a
  globally meaningful 16-byte key.
- Durable identity lives in an optional fixed-capacity world side table; it does
  not widen query rows or alter hot `Entity` traversal.
- Runtime slot reuse cannot reuse a durable ID; restore reinstates allocator
  high-water state.
- A snapshot sorts entities by durable ID and uses a dense 32-bit save ordinal
  for all internal `Entity` references.
- Every destination entity is allocated before any reference-bearing column is
  decoded.

This deliberately supersedes the early idea of persisting only runtime
`Entity.Index`. Index remapping is compact for one load, but it cannot serve
offline/cross-participant identity or prove that a reused slot denotes the same
logical object. Dense save ordinals retain the compact remap advantage without
making the runtime allocator an external identity contract.

### Shape, payload, enable state, and provenance

- Complete saved runtime membership is independent of persisted value columns.
- `Value`, `MembershipOnly`, `Derived`, and `None` policies make reconstruction
  explicit.
- Storage-free tags participate in saved shape without fake payload.
- Selected enableable components persist compact bitplanes.
- Stable `PrefabKey` text/hash is optional provenance. Runtime prefab handles are
  never durable, and saved mutable state is never replaced by current defaults.

### Generated codecs and evolution

- The schema generator emits reflection-free canonical little-endian fixed-width
  codecs and 128-bit component fingerprints.
- Entity-reference members require an explicit attribute and ordinal remapping.
- Custom codecs remain generated-bound/cold-registered; there is no reflection
  or per-value object dispatch in capture/restore.
- Historical versions use direct old-version-to-current readers, not migration
  chains.
- Stable-ID aliases and explicit drops are distinct policies.
- Complete saved-shape migrations support explicit N:M split/merge.
- Derived reconstructors run after authoritative hydration.
- Fingerprints are a diagnostic/fast-reject hint, not sole compatibility proof.

### Transactional restore

Preflight clones and treats the artifact as hostile bytes. It validates the
header, directory, checked ranges, overlap, counts, identities, UTF-8, ordinals,
section completeness, per-section CRC32, and whole SHA-256 under configured
limits before a staging world is created.

Restore registers resolved current shapes, seals/initializes the staging world,
allocates identities, hydrates columns/references/enables, applies explicit shape
migrations and derived reconstruction, restores framed system state/tick, and
validates population/identity/clean observation state before `BeginRun`.
Failures dispose staging state; the active world is never mutated.

Restore intentionally emits no Created/Added/Removed/Destroyed history and starts
with no component-change version. Loading a checkpoint is generation
publication, not a gameplay event storm.

### System state

Opt-in system runtimes declare stable 64-bit persistence identity and a nonzero
state schema version and implement `IPersistentSystemState`. State is sorted and
framed. Scheduler tick is durable; graph topology, workers, timing/profiling,
prepared handles, and change/lifecycle cursors are reconstructible and omitted.

### Save-set durability and publication

The active `Server/Persistence` project contains:

- generic save/load participant contracts;
- `EcsWorldSaveSetParticipant` and an ownership-transferring prepared ECS world;
- a caller-owned common capture boundary;
- safe participant artifact names;
- SHA-256 manifest integrity;
- write-through files plus `Flush(true)`;
- platform directory fsync;
- pending-directory to complete-generation rename;
- durable atomic `CURRENT` pointer replacement;
- strict-current and newest-complete-compatible recovery;
- Prepare → Resolve → Validate across every participant, followed by exactly one
  host publication callback.

Fault injection covers every named publication boundary. Recovery exposes a
previous or new complete generation, never a mixed participant set. A complete
generation remains recoverable when a crash occurs before the first `CURRENT`
pointer is published.

## Correctness evidence

Focused 3.4 tests cover:

- canonical full-world byte round trip;
- canonical bytes independent of packed-row history;
- byte-exact Native/Pinned capture-core parity;
- identical serial/C8 scheduled final-state artifacts;
- owned raw capture remaining immutable after the live world changes, with
  participant encoding/durability proven outside the common epoch;
- tags and membership-only runtime shape;
- component values and persisted enable state;
- internal reference ordinal remapping;
- cross-participant `PersistentEntityKey` payloads remaining durable keys rather
  than ECS-local ordinals;
- unresolved/missing current prefab content retaining advisory saved provenance
  without overriding the saved shape or value;
- persistent ID high-water and runtime-slot reuse;
- durable-identity lookup churn beyond total table slots without tombstone
  saturation;
- clean value/lifecycle observation after restore;
- explicit two-column-to-one-column shape migration;
- converging historical shapes with capacity aggregated for the one current
  destination archetype;
- direct version-1-to-version-2 component reader migration;
- explicit enable-plane transfer through a shape migration;
- derived reconstruction after authoritative hydration;
- persistent scheduler state and scheduler tick, including stable-ID order
  independent of registration order, optional unknown-state omission, and
  required missing-state rejection;
- corruption, limits, and sampled whole-artifact byte mutation;
- generator diagnostics for inaccessible/unsupported payload members and
  unmarked `Entity` references;
- save-set fault injection at every durability boundary;
- participant artifact bounds enforced during the write, before an oversized
  file can be materialized;
- failed participant preparation with no mixed publication;
- compatible recovery without `CURRENT`;
- real ECS participant staging and host-owned generation publication.

Existing generator/runtime/scheduler/lifecycle/content suites remain the
regression gates. The implementation does not change the 24-byte structural
command record, 40-byte command slot, packed query storage, scheduler worker
model, or warmed simulation allocation contract.

The permanent 141-row ECS kernel benchmark suite was also rerun under the 3.4
profile. Every timed row reported zero managed allocation; its capture is
`_Benchmarks/results/ecs-v3-3.4.0-full-regression-2026-08-12.json`.

## Allocation, GC, and memory

Persistence is a cold checkpoint operation and is not zero allocation in 3.4.0.
The accepted run reports rather than hides its proportional ownership:

| Scale | Capture allocation/iteration | Restore allocation/iteration | Gen0/1/2 during measured loop | Final process working set | Final managed heap |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 100K | 52.90 MB | 16.76 MB | 8 / 8 / 8 | 315.61 MB | 116.28 MB |
| 1M | 552.01 MB | 165.51 MB | 5 / 5 / 5 | 2.238 GB | 683.66 MB |

The artifact and bounded-plan accounting is also reported directly rather than
being inferred only from process memory:

| Scale | Schema | Shape + row map | Entity identities | Component columns | Enable plane | Ordinal/generation scratch | Durable-ID map | Restore-plan temporary | Staging known payload |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 100K | 267 B | 400,040 B | 2,000,004 B | 9,200,080 B | 12,516 B | 1,200,000 B | 3,048,576 B | 2,800,064 B | 15,253,524 B |
| 1M | 267 B | 4,000,040 B | 20,000,004 B | 92,000,080 B | 125,016 B | 12,000,000 B | 28,388,608 B | 28,000,064 B | 150,367,561 B |

System-state and prefab sections are 12 B and 4 B in this data-only fixture;
container header/directory/checksum/alignment overhead is 549 B at 100K and
545 B at 1M. `Staging known payload` is the ECS-owned post-seal accounting
contract, not CLR/process overhead. Capture/restore allocation and process memory
above remain necessary because temporary arrays and object headers are not all
part of that storage reservation snapshot.

There are no boxed component values or per-entity managed snapshot objects, but
the current synchronous canonical model owns multiple proportional arrays and
the staging world coexists with source/artifact/parse state. This is acceptable
for the initial time gates, not an endpoint. Peak memory and full-GC pressure are
the clearest future optimization targets. Direct chunk-run capture, pooled/owned
large buffers, section streaming, and dropping parse bytes as soon as consumed
should be evaluated without weakening the immutable staging transaction.

## V2 versus V3 evidence

The complete existing semantic adapter was rerun in both child-process orders.
It reports 103 comparable rows, zero canonical-state mismatches, nine explicit
unsupported rows, and zero mismatch/catalog drift when the reverse run is
compared with the accepted 3.4 artifact. Relative to 3.3, exactly two unsupported
descriptor changes are intentional: persistence is no longer “missing in V3”;
it is unsupported because the V2 and V3 durable/transactional contracts do not
match. Every measured V3 row reports zero timed caller allocation; scheduler
rows retain their separate worker-allocation instrumentation.

There is no honest accepted current-V2 full persistence benchmark. The permanent
V2 `PersistenceView` row measures world seeding plus view construction and a
metadata checksum; it does not encode or restore. It is therefore excluded from
the performance ratio.

| Capability | V2 current | ECS 3.4.0 | Comparison class |
| --- | --- | --- | --- |
| Stable component IDs/version hook | Yes | Generated IDs/codecs/direct readers | Semantic reuse |
| Complete runtime shape | No; only serializable payload can define restore topology | Yes; shape and payload are separate | V3 capability |
| Durable entity identity | Writes runtime packed handle; current restore ignores it | 64+64-bit durable key/high-water | V3 capability |
| Internal entity remap | No in current V2 | Dense save ordinals | V3 capability |
| Cross-participant transaction | Sequential live mutation | Prepare/Resolve/Validate/single Commit | V3 capability |
| Restore observation state | Marks everything dirty | Clean change/lifecycle state | Different contract |
| Input bounds/integrity | Unbounded counts/payload; no strong checksum | Limits, CRC32 sections, SHA-256 artifact | V3 capability |
| Crash durability | Staging/manifest intent, but no fsync and promotion gaps | durable generation and atomic pointer protocol | Different contract |
| Capture representation | reflection, boxing, lists/object arrays | generated fixed-width columns/proportional arrays | Directional |
| Full equivalent benchmark | None | 100K/1M accepted | Unsupported V2 ratio |

The archived predecessor's dictionary-based old-packed-ID remap confirms the
usefulness of “allocate identities, then hydrate/fix references,” but it is a
historical design control, not the current V2 baseline. Its old small fixture
(1K players + 10K items) reported roughly 15.5 ms save, 5.2 ms load, 9.61 MB save
allocation, and 5.64 MB load allocation after a bulk-read pass. Those values use
a different system, workload, format, and durability contract and are not
presented as a V2/V3 speed ratio.

An honest future comparison adapter may add a narrowly constrained provisional
case with only 1–3 value components and no tags, enable planes, references,
migrations, provenance, system state, or durability. It must label the semantic
and integrity work each implementation performs.

## Accepted limitations

- whole-world replacement only; no merge or append;
- no incremental/change-version snapshot;
- no compression or rich retention/operator backup UI;
- no V2 production-format reader/importer;
- no real account/inventory/relationship participant adapters yet;
- no cross-participant component reference codec beyond durable keys interpreted
  by the owning participant;
- synchronous ECS participant capture; encoding is separable from the measured
  authoritative collection phase but the convenience API returns the final
  artifact in one call;
- current capture/restore memory is substantial;
- fault injection simulates every protocol boundary, but a process-kill/power-cut
  filesystem harness remains desirable per supported platform;
- the public facade currently targets the native world; the generic capture core
  is byte-exact across Native/Pinned backends, but there is no separate public
  pinned-world product facade.

These are explicit boundaries, not hidden claims of completion.

## Decision and next direction

Accept 3.4.0. It is a coherent durable-state generation boundary and meets the
task's correctness/time/throughput gates. The version profile and authoritative
docs advance to 3.4.0; the accepted benchmark artifact is immutable.

The next natural work should be integration-shaped rather than another isolated
serializer feature:

1. wire the ECS participant into the real engine-agnostic server host and define
   ownership-swapping publication;
2. add account/inventory durable-reference participants and cross-participant
   resolve/validate tests;
3. measure peak memory and prototype direct/pooled section streaming only if the
   host's checkpoint budget requires it;
4. define the storage-level checkpoint bridge needed by Network without allowing
   Network or Godot wrappers to own ECS persistence semantics;
5. defer incremental snapshots until real checkpoint cadence and dirty-density
   data demonstrate their value.

Tests and production-shaped evidence remain allowed to change that order.
