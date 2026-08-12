# ECS 3.3.0 compiled content authoring — engineering report

Date: 2026-08-12

## 1. Verdict

**Accepted ECS 3.3.0.** The RunUO-shaped authoring layer compiles away before
`Seal`; warmed lookup/spawn is bounded and allocation-free; direct, class-shaped,
immediate, deferred, Native, Pinned, lifecycle, change, row-reuse, and Slice 0
evidence all pass.

## 2. Exact semantic profile

```text
version: 3.3.0
profile: ecs-3.3.0-compiled-content-authoring
```

## 3. Public authoring model

| Concept | Meaning |
| --- | --- |
| `BaseClass` | Cold additive authoring shape/default definition |
| Object Script | Concrete explicitly registered spawnable class with an `ObjectScriptAttribute` |
| `PrefabKey` | Stable lowercase durable content identity |
| `PrefabHandle` | 12-byte world/catalog-bound direct runtime route |
| compiled prefab | Immutable archetype route plus bound constant default copies |
| archetype | Actual ECS component membership and SoA storage |

A prefab is not an archetype, an Object Script is not an entity object, and a
runtime handle is not a persistent key.

## 4. Authoring inheritance semantics

Each derived `Define` explicitly calls `base.Define(builder)` and adds membership
monotonically. Concrete scripts normally override `Configure`. CLR inheritance is
ordinary cold C# execution; it is not traversed or retained by the runtime.

## 5. Composition semantics

`With<T>()`, `With<T>(value)`, and `Tag<T>()` establish the final canonical
component signature. Actual membership alone chooses an archetype. Values,
stable keys, display metadata, and class names do not fragment storage.

## 6. Default override semantics

Definitions resolve base-to-derived. `Set<T>(value)` replaces an existing
default without changing membership. `Set` on a missing component, duplicate
membership, or a value assigned to a storage-free tag fails cold. Omitted data
defaults are deterministic `default(T)` supplied by initialized row storage.

## 7. Stable key semantics

`PrefabKey` is a validated 1–256 character lowercase path and has a deterministic
64-bit hash. Catalog order is ordinal key order. The same key may represent
corresponding content in independent worlds; resolution occurs per catalog.

## 8. Runtime handle semantics

`PrefabHandle` contains `WorldId`, dense prefab ID, and target `ArchetypeId`.
Direct lookup is indexed and independent of catalog size or inheritance depth.
Handles are rejected across worlds and must not be saved or networked.

## 9. Discovery/compiler architecture

3.3 uses explicit `Register<TObjectScript>()`, one cold attribute lookup, and one
cold authoring-object construction. It deliberately does not scan AppDomains.
A new source generator was not justified: the measured cold compiler is already
small, deterministic, and off the hot path; generator complexity can wait until
real package/discovery pressure or analyzer diagnostics justify it.

## 10. Compilation pipeline

```text
authoring type/direct builder
→ resolved membership/default map
→ stable-key ordinal sort
→ canonical ComponentId signature
→ full-equality signature/archetype dedupe
→ physical default-column binding
→ compact descriptor/default arrays
→ immutable world catalog
```

All failure-prone definition/layout/capacity checks occur before ordinary runtime
simulation. Exactly one catalog binds to a world before `Seal`.

## 11. Final catalog representation

- `PrefabHandle`: 12 bytes.
- `CompiledPrefabEntry`: 32 bytes (ID, archetype, descriptor range, default-byte
  count, key index, signature hash).
- `PrefabDefaultDescriptor`: 12 bytes (component, physical column, byte offset,
  byte count).
- one contiguous default byte array;
- sorted stable-key and optional display-name arrays for cold resolution and
  diagnostics.

## 12. Catalog memory

The Slice catalog is 10 entries, 10 signatures, zero newly registered
archetypes, 320 entry bytes, 12 descriptor bytes, 4 default bytes, 582 stable-key
character bytes, and 306 display-character bytes: **1,224 known payload bytes**.

The 10,000-prefab synthetic catalog retains 980,000 known payload bytes:
320,000 entries, 120,000 descriptors, 160,000 defaults, and 380,000 key
characters. Cold compile allocates roughly 9.3–9.9 MiB depending on signature
count; those compiler dictionaries/arrays/authoring objects are discarded.
Reported payload excludes CLR headers/alignment and allocator overhead.

## 13. Archetype de-duplication evidence

`Villager`, `Merchant`, `Guard`, and `Blacksmith` have different keys, names,
health/activity/wealth defaults, and handles, but identical final membership.
All four resolve to one archetype. `Wolf`, `Bread`, and `IronSword` demonstrate
that real tag/data membership differences produce different signatures.

## 14. Authoring diagnostics

Tests cover invalid/duplicate keys, duplicate membership, tag-value misuse,
data-as-tag misuse, `Set` without membership, unregistered components, missing
Object Script attributes, failed-registration transactionality, mutation after
compile, second compilation, default/foreign/forged handles, and bounded world,
row, and command capacity.

## 15. Direct DOD authoring path

`ContentCatalogBuilder.Define(key)` exposes the same `PrefabBuilder` without a
class. A direct Villager and class-authored Villager compile to the same
signature/archetype/default representation and instantiate equivalently.
Manual initialized creation and compiled instantiation also reach the same
current component/archetype/enable state. Their publication differs by design:
manual tracked initialization is a value write; compiled defaults are creation
initialization and do not create false changed rows.

## 16. Immediate instantiation

`TryInstantiate` validates the world-bound handle, allocates/reuses one ordinary
row, restores deterministic row state, copies compiled defaults, publishes the
entity location, and emits one `Created`. Failure returns no partial entity or
false event. `Instantiate` is the throwing bounded-capacity form.

## 17. Deferred instantiation

`StructuralCommandBuffer.EnqueueInstantiate` and
`SystemStructuralWriter.Instantiate` return ordinary same-epoch/segment
`DeferredEntity` tokens. Same-segment typed replacements work. A compiled prefab
is one 24-byte command in one 40-byte slot regardless of signature width.

## 18. Structural integration

`InstantiatePrefab` is one command kind in the existing phase chains, worker
lanes, deterministic merge, result slots, create phase, temporary-hole reuse,
rollback, and fault policy. There is no prefab queue, callback interpreter, or
second structural executor.

## 19. Lifecycle integration

Successful prefab creation emits exactly one ordinary `Created`. It emits no
initial `ComponentAdded` storm. Later true Add/Remove/Destroy operations publish
the same normal facts as every other entity. Rejected/capacity-failed creation
publishes nothing.

## 20. Change-history behavior

Compiled defaults are initial state, not writes. Dense/exact history is reset on
row publication, so a new prefab does not appear to a from-now change cursor.
A later tracked write or typed replacement publishes ordinary exact change state.

## 21. Default initialization safety

Defaults are copied from immutable bytes into each entity row. Unspecified
members remain zero. Component sizes and physical columns come from the sealed
schema/layout; no arbitrary packed authoring layout is assumed. Spawned entities
cannot mutate template bytes or alias one another through unmanaged component
state.

## 22. Row-reuse safety

A wide-row destroy/instantiate barrier test overwrites and disables the old row,
then proves temporary-hole reuse restores compiled bytes and enabled/history
state without stale leakage, compaction, or donor relocation.

## 23. Native/Pinned parity

Native and Pinned catalogs compiled in opposite registration order have the same
diagnostic hash and defaults. Both instantiate the same values through their
shared runtime primitives. Existing backend storage/migration/lifecycle tests
remain green.

## 24. Determinism evidence

Forward/reverse registration produces identical sorted keys, dense prefab IDs,
signature hashes/groups, target archetype IDs, default counts, and catalog hash.
Hash buckets are candidates only; full signatures decide equality.

## 25. Compile benchmark

Tiered compilation and ReadyToRun were disabled. Representative means/p95:

| Prefabs / signatures | Mean | p95 | Cold allocation | Catalog |
| --- | ---: | ---: | ---: | ---: |
| 10 / 1 | 0.012 ms | 0.018 ms | 11.2 KiB | 1.0 KiB |
| 1,000 / 1 | 0.627 ms | 0.742 ms | 954.8 KiB | 95.7 KiB |
| 1,000 / 128 | 1.003 ms | 1.128 ms | 1,134.0 KiB | 95.7 KiB |
| 10,000 / 1 | 3.421 ms | 3.509 ms | 9,532.9 KiB | 957.0 KiB |
| 10,000 / 128 | 4.282 ms | 4.556 ms | 9,888.0 KiB | 957.0 KiB |

Cold compilation can collect; nine total collections occurred across the full
13-row compile matrix. This is intentionally separate from runtime allocation.

## 26. Archetype/signature scaling benchmark

For 10,000 prefabs, 1/8/32/128 signatures measured 3.421/4.588/7.559/4.282 ms.
The non-monotonic small distribution is host/cold-allocation noise, not a claim
that 32 signatures intrinsically cost more than 128. Catalog bytes stay 980,000
because entry/key/default counts are held constant; only archetype startup work
changes. All requested unique-signature counts were produced exactly.

## 27. Spawn benchmark

| Profile | Mean ns/entity | p95 | Entities/s |
| --- | ---: | ---: | ---: |
| one prefab / one archetype, 100K | 78.43 | 80.41 | 12.75M |
| 128 prefabs / one archetype, 100K | 80.16 | 86.16 | 12.47M |
| 128 prefabs / 128 archetypes, 100K | 87.56 | 91.70 | 11.42M |
| one prefab / one archetype, 1M | 76.99 | 77.71 | 12.99M |

The ordinary zero-default create control is 46.24 ns/entity. The prefab cost is
the explicit handle/default/lifecycle work; semantic prefab count by itself adds
about 2% when all 128 handles share one signature.

Deferred 16-byte spawn is 13.64 ns record, 73.29 ns playback, and 86.93 ns
end-to-end. Each spawn is one command and one lifecycle event.

## 28. Default-size ladder

| Default bytes | Mean / p95 ns/entity | Effective default GiB/s |
| ---: | ---: | ---: |
| 16 | 79.53 / 85.79 | 0.19 |
| 64 | 82.60 / 85.36 | 0.72 |
| 256 | 85.92 / 94.33 | 2.77 |
| 1,024 | 134.41 / 145.55 | 7.10 |

The fixed per-entity publication/allocation cost dominates narrow defaults; the
wide case increasingly reflects actual copied bytes.

## 29. Wide-prefab result

The 1 KiB immediate profile is 134.41 ns/entity; deferred playback/end-to-end is
125.04/137.20 ns/entity. It remains one command and one Created fact rather than
N Adds, with zero donor/hole persistence and no authoring interpretation.

## 30. Allocation/GC

Every warmed immediate, deferred record/playback/end-to-end, direct lookup, and
stable-key lookup row reports **0 managed bytes and 0 Gen0/1/2 collections**.
Direct dense entry access is 0.71 ns/op; cold stable-key binary resolution is
43.07 ns/op, both allocation-free. Slice caller and all scheduler-worker counters
also remain zero, with zero collections.

## 31. Runtime memory

Catalog payload is accounted separately by `CompiledContentBytes`. Slice 100K
adds only 1,224 known bytes to 23,918,905 serial / 25,637,314 parallel known ECS
bytes. Observed working set is about 89.4 MiB serial and 108.7 MiB parallel; those
process figures include runtime/JIT/benchmark state and are not catalog payload.

## 32. Simulation Slice 0 integration

Slice 0 compiles four alive actor tiers, four dead tiers, a world item, and a
temporary effect: 10 prefabs mapped onto its existing 10 archetypes. Bootstrap
uses immediate handles. Effect spawn uses one prefab command plus three true
instance replacements; 3.2 used default create plus four replacements. The 10K
100K run therefore records 1,708,476 commands instead of 2,028,476—320,000 fewer.

First-world compile/bootstrap numbers include cold JIT (roughly 16 ms and
187–196 ms); the second-world catalog compile is 0.109–0.115 ms and bootstrap
38–39 ms at 100K. These are reported but not substituted for the dedicated cold
compile benchmark.

## 33. Slice 100K result

Tiered runtime defaults, 256 warmup and 10,000 measured ticks:

| Mode | Mean | p95 | p99 | Allocation |
| --- | ---: | ---: | ---: | ---: |
| serial | 186.21 us | 327.19 us | 812.63 us | 0 B |
| C8 | 165.69 us | 265.44 us | 400.53 us | 0 B caller/worker |

Oracle/ECS/serial/C8 hash is `0x61C8B1872C75BC67`; ordered lifecycle hash is
`0xF02667DE0F1BC4E7` (decimal 17304632821651588327).

## 34. Slice 1M result

With 256 warmup and 1,000 measured ticks, serial is 1.452 ms mean / 3.024 ms p95;
C8 is 0.477 ms / 0.992 ms. Both allocate zero and match the 3.2 canonical hash
`0x8E9978581C27A90A` and lifecycle hash.

## 35. Comparison to 3.2 Slice

| Profile | 3.2 serial → 3.3 | 3.2 C8 → 3.3 | Interpretation |
| --- | ---: | ---: | --- |
| 100K tiered | 188.72 → 186.21 us (−1.3%) | 165.71 → 165.69 us (tie) | unchanged |
| 1M tiered | 1.488 → 1.452 ms (−2.4%) | 0.484 → 0.477 ms (−1.4%) | unchanged/no regression |
| 100K all-active | 747.91 → 737.56 us (−1.4%) | 415.90 → 416.71 us (+0.2%) | unchanged |

Startup compilation and bootstrap are separate from tick timing. The state and
lifecycle hashes are exact, not checksum-only comparisons.

The new cross-generation provisional prefab row is also exact-state: V2-first
V2/V3 means are 68.70/77.44 ns/entity, reverse order 69.51/77.17. V2 allocates
3,073,280 bytes per 10K spawn iteration; V3 allocates zero. The full joined suite
has 103 comparable rows, zero state mismatches, and nine unsupported rows.

## 36. Fresh-consumer review

The local fresh-context reviewer correctly predicted one archetype for 100 Human
scripts with identical membership and identified membership as the split. It
correctly distinguished stable key and world-local handle, but initially treated
shared storage as a possible shared-default/value risk and asked whether keys
could correspond across worlds. Documentation was clarified: defaults copy into
independent rows; systems must model semantic categories as components/tags; keys
may correspond across catalogs; handles cannot cross worlds.

## 37. Adversarial reviewer findings

| Hypothesis | Disposition |
| --- | --- |
| missing internal null guards | rejected: internal compiler construction proves non-null arrays |
| forged/foreign prefab IDs may pass | rejected by public validation and focused tests |
| command recording should verify catalog presence | rejected as redundant for public unforgeable world handles; playback still validates |
| schema-local component IDs may become stale | rejected: schema/world topology seals before runtime |
| cold default blob is unbounded | documented limitation; capacity/arithmetic are checked, but 3.3 has no independent catalog-byte quota |
| shared archetype might imply shared values | documentation-only clarification; tests already prove instance/template independence |

The only production change prompted during review was making throwing
`Instantiate` accurately report either entity-slot or target-row capacity rather
than naming only entity capacity.

## 38. Complexity review

Runtime adds one optional immutable catalog, one 12-byte handle, one entry/default
descriptor representation, and one command kind routed through the existing
barrier. Cold authoring adds BaseClass/Object Script/builders/compiler state.
After compile, authoring objects, inheritance, dictionaries, reflection results,
and discovery order disappear. No object database, virtual entity object,
runtime `IsA`, prefab scheduler, or second structural queue survives.

## 39. Trait/composition pressure

BaseClass plus explicit additions was sufficient for the Human/Creature/Item/
Equipment/Effect set and Slice 0. Repeated fragments could eventually improve
authoring reuse, but no concrete workload required traits strongly enough to add
another composition language in 3.3.

## 40. Persistence requirements discovered

Persistence needs stable `PrefabKey` identity where provenance matters, schema/
content compatibility policy, optional provenance capture, and entity-reference
remapping during load. Runtime entity IDs and `PrefabHandle` values are not
durable. Static defaults containing arbitrary runtime `Entity` references require
future self/parent/named-reference resolution rather than raw saved handles.

## 41. Deliberate deferrals

Save/load, provenance components, reference remapping, traits/fragments, spawn
parameters/factories/random rolls, nested prefab graphs, child instantiation,
source-generated discovery, Roslyn/runtime scripting, hot reload/generations,
mod packages, editor integration, networking negotiation, behavior trees, and
per-entity virtual behavior remain out of scope.

## 42. Complete test totals

Final gates comprise 218 core runtime/primitive tests, 43 scheduler tests, 9
lifecycle/filter tests, 15 content-authoring tests, 25 generator tests, 12
comparison tests, and 18 bitmap-experiment tests: **340 named executable test
groups**, plus canonical Slice replay and benchmark-specific validation. Debug
and Release runtime suites, warnings-free builds, and allocation checks pass.

## 43. Complete benchmark artifact list

- `_Benchmarks/Baselines/ecs-v3-3.3.0-compiled-content-authoring-2026-08-12.json`
- `_Benchmarks/Baselines/ecs-v2-v3-3.3.0-2026-08-12.json`
- `_Benchmarks/results/ecs-v2-v3-3.3.0-v2-first/`
- `_Benchmarks/results/ecs-v2-v3-3.3.0-v3-first/`
- `_Benchmarks/results/simulation-slice-0/ecs-3.3.0-content-100k-10000-2026-08-12.json`
- `_Benchmarks/results/simulation-slice-0/ecs-3.3.0-content-1m-1000-2026-08-12.json`
- `_Benchmarks/results/simulation-slice-0/ecs-3.3.0-content-100k-all-active-10000-2026-08-12.json`
- `_Benchmarks/results/ecs-v3-3.3.0-full-regression-2026-08-12.json`
- `_Benchmarks/results/ecs-v3-3.3.0-public-jobs-regression-2026-08-12.json`
- `_Benchmarks/results/ecs-v3-3.3.0-scheduler-regression-2026-08-12.json`
- `_Benchmarks/results/ecs-v3-3.3.0-lifecycle-regression-2026-08-12.json`
- `_Benchmarks/results/ecs-v3-3.3.0-change-decomposition-regression-2026-08-12.json`

Immutable 3.2 artifacts remain untouched and are the longitudinal controls.

## Final architectural questions

### Question 1

**Yes.** Authors get broad BaseClasses and concrete Object Scripts; runtime sees
only catalog entries, handles, archetypes, components, systems, and entities.

### Question 2

**Yes.** Authoring inheritance disappears completely after cold compilation.

### Question 3

**Yes.** Different keys/defaults/metadata with identical membership share one
archetype; the representative four Human scripts prove it.

### Question 4

**Yes.** Wide initialization is bound byte copying and remains one command/
Created fact; 1 KiB immediate spawn is 134.41 ns/entity.

### Question 5

**Yes.** Direct-DOD and class-shaped definitions enter the same builder/compiler
and produce the same runtime form.

### Question 6

**Yes.** After creation the entity has no prefab-specific simulation behavior;
ordinary writes, changes, transitions, Add/Remove, and destroy semantics apply.

### Question 7

**Yes.** Slice canonical/lifecycle state is exact and all three steady-state
profiles remain within roughly 2.4% of 3.2, with zero allocation.

### Question 8

**No concrete requirement yet.** Traits, parameters, graphs, and dynamic
authoring can remain deferred until a real content/persistence slice demands them.

### Question 9

**Yes.** The next major integration milestone should be persistence: stable
provenance policy, schema/content compatibility, serialization, and reference
remapping—not more speculative ECS-kernel work.
