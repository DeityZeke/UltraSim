# ECS compiled content authoring

Status: authoritative ECS 3.3.0 contract, accepted 2026-08-12.

## Mental model

```text
authoring (cold, before Seal)                 runtime (hot)

BaseClass inheritance                        PrefabHandle (12 B)
Object Script defaults         compile        direct catalog entry (32 B)
direct-DOD definitions       ---------->      bound defaults (12 B each)
stable PrefabKey metadata                     ordinary Archetype + ECS Entity
```

Authoring inheritance is a definition-building convenience, not a runtime type
system. No `BaseClass` or Object Script object is retained in an entity, queried
by systems, or called during spawn/tick. Behavior remains components plus systems.

## Choosing the concepts

| Concept | Meaning | Lifetime |
| --- | --- | --- |
| `BaseClass` | Abstract, additive authoring shape/default definition | cold compiler input |
| Object Script | Concrete spawnable class marked by `ObjectScriptAttribute` | constructed once during explicit registration |
| `PrefabKey` | Stable lowercase path identity for saves/content references | durable cold identity |
| `PrefabHandle` | Dense world-bound direct runtime route | current world/catalog only |
| compiled prefab | Target archetype plus bound constant default copies | immutable world catalog |
| archetype | Actual component membership/storage layout | ECS runtime |

A prefab is not an archetype. One hundred Human scripts with identical component
membership and different names, jobs, wealth, or appearance defaults compile to
one archetype and one hundred prefab entries.

Sharing an archetype does not share component values. Defaults are copied into
each new row; the spawned entity then owns ordinary independent ECS state. A
system may mutate that state normally, but doing so cannot mutate the compiled
template or another instance. Systems that need semantic categories must model
them as components/tags/data, not infer them from cold display metadata.

## Minimal authoring

```csharp
public abstract class ItemBase : BaseClass
{
    protected override void Define(BaseClassBuilder shape)
    {
        base.Define(shape);
        shape.With<Position>();
        shape.With<ItemState>();
        shape.Tag<ItemTag>();
    }
}

[ObjectScript("items/iron-sword")]
public sealed class IronSword : ItemBase
{
    protected override void Configure(PrefabBuilder prefab)
    {
        prefab.With(new Damage { Min = 8, Max = 14 });
        prefab.With(new Durability { Current = 50, Max = 50 });
        prefab.DisplayName("Iron Sword");
    }
}

var content = new ContentCatalogBuilder(world.Schema);
content.Register<IronSword>();                 // explicit; no assembly scan
CompiledPrefabCatalog catalog = content.Compile(
    world,
    new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 32));

PrefabHandle sword = catalog.GetHandle(new PrefabKey("items/iron-sword"));
Entity entity = world.Instantiate(sword);      // after Initialize/BeginRun
```

`With<T>()` adds membership with deterministic `default(T)`. `With<T>(value)`
adds membership and an explicit default. `Tag<T>()` adds storage-free membership.
`Set<T>(value)` changes an already inherited/added member's default. Duplicate
membership, assigning a value to a tag, setting a missing member, missing schema
components, invalid/duplicate keys, and late mutation reject cold.

Derived `Define` overrides must call `base.Define(builder)` to include their base
shape. This is ordinary explicit C# inheritance semantics in 3.3. A source
generator/analyzer may make that mistake more diagnosable later; runtime does not
walk CLR base types or infer omitted calls.

Direct data-oriented definitions use the same builder and compile to the same
entry/default format:

```csharp
PrefabBuilder sword = content.Define(new PrefabKey("items/iron-sword"));
sword.With<Position>();
sword.With<ItemState>();
sword.Tag<ItemTag>();
sword.With(new Damage { Min = 8, Max = 14 });
```

## Compilation and determinism

Compilation occurs once while the world is `Created`:

```text
explicit definitions
  -> stable-key ordinal sort
  -> resolved component/default map
  -> canonical ComponentId signature
  -> full-signature deduplication
  -> reuse/register ordinary archetype
  -> bind physical default columns
  -> compact entry/descriptor/default arrays
  -> bind exactly one immutable catalog to the world
```

Catalog IDs follow sorted stable-key order. Signature equality, not hash alone,
decides archetype sharing. Registration order therefore does not change keys,
numeric prefab ordering, signature groups, defaults, or diagnostic hash.

The same stable key may intentionally identify corresponding content in several
separately compiled worlds/catalogs. It resolves independently in each catalog.
A `PrefabHandle` includes `WorldId` and is rejected by every other world; never
persist or transfer a handle as if it were the stable key.

Cold reflection is limited to reading one attribute from an explicitly selected
generic Object Script. There is no AppDomain scan. The compiler may allocate;
the timed spawn path may not. Authoring instances, per-definition dictionaries,
signature buckets, resolved arrays, and authoring `Type` objects are discarded.

## Runtime representation and cost

- `PrefabHandle`: 12 bytes (`WorldId`, dense prefab ID, target `ArchetypeId`).
- catalog entry: 32 bytes.
- explicit-default descriptor: 12 bytes (`ComponentId`, physical column,
  payload offset/size).
- one contiguous immutable default byte blob.
- stable keys and optional display names remain cold diagnostics/persistence
  mapping metadata.

`PrefabHandle` execution is direct indexed lookup. Stable-key lookup is a cold
binary search over sorted keys and should normally be resolved once at startup.
Spawn cost depends on ordinary allocation plus actual row/default bytes, not CLR
inheritance depth or the number of authoring types.

## Instantiation, changes, and lifecycle

Immediate owner-thread use:

```csharp
world.TryInstantiate(prefab, out Entity entity);
```

Scheduled/deferred use:

```csharp
DeferredEntity entity = context.Commands.Instantiate(prefab);
context.Commands.TryAdd(entity, perInstanceHealth); // optional replacement
```

The deferred prefab itself is one normal structural command. It uses the
existing per-worker lane, merge order, create phase, `DeferredEntity`, result,
capacity, fault, and hole-reuse machinery. It does not encode one Add command per
compiled component.

Initialization is:

```text
allocate/reuse row
  -> restore deterministic zero/enabled/history state
  -> copy explicit compiled defaults
  -> bind live entity location
  -> publish exactly one Created fact
```

Initial defaults are initialization, not changed-row writes and not
`ComponentAdded` events. A later typed replacement, write, add/remove, destroy,
or transition uses the ordinary ECS semantics. Failed entity/row/command capacity
creates no live partial entity and no false lifecycle record.

## Memory accounting

`CompiledContentSnapshot` reports entry, descriptor, default, stable-key, and
display-character payload independently. `WorldReservedPayloadSnapshot` includes
that total as `CompiledContentBytes`, never as chunk payload. CLR object/array
headers, allocator overhead, shared schema/archetype storage, and discarded cold
compiler temporaries are explicitly excluded.

## Deliberate 3.3 boundaries

Not implemented here: traits/fragments, nested prefab graphs, per-spawn callbacks,
random/default generators, true contiguous bulk prefab fill, source-generated
definition discovery, runtime script compilation, hot reload/content generations,
content-pack ownership, prefab removal, automatic live-entity migration,
persistence codecs/provenance components, or networking negotiation.

`PrefabKey` is ready to be stored by an application that needs provenance, but
3.3 deliberately does not add a provenance component to every entity. Runtime
entity identity, durable character/item identity, prefab identity, and network
identity remain distinct domains.
