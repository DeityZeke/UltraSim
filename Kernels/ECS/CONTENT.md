# Compiled ECS content

Content authoring is a cold convenience layer that disappears into the ordinary
ECS runtime machine.

```text
authoring definitions
    inheritance + composition + defaults
        -> deterministic compilation
            -> immutable prefab catalog
                -> ordinary archetype + default payload instantiation
```

## Concepts

- A component is runtime data/topology.
- An archetype is a physical component signature.
- A prefab is a stable content identity plus one compiled archetype/default set.
- Inheritance/composition are authoring relationships, not runtime entity object
  hierarchies.

Multiple prefabs may share one archetype while retaining different provenance
and defaults. Prefab identity is not inferred from archetype identity.

## Authoring and compilation

Definitions use canonical stable keys. Parents and included traits/fragments are
resolved cold. Defaults are type-safe component values; child/local values
override inherited values according to the documented deterministic order.

Compilation rejects missing references, duplicate/ambiguous contributions,
cycles, schema mismatch, invalid storage/tag values, unknown components, and
capacity overflow. Output order and diagnostics are deterministic independent of
discovery order.

The closed catalog binds keys to compact world-local `PrefabId` values, prepared
archetype handles, immutable encoded defaults, and provenance. Runtime creation
does not reflect, parse strings, traverse inheritance, or allocate managed
objects.

## Instantiation

Immediate and deferred prefab creation use the normal ECS creation/structural
pipelines. Retained-empty rows and barrier-local reused rows are fully reset
before defaults are published. Dense/exact changes, enable state, and lifecycle
follow the same contracts as ordinary creation.

Deferred creation returns a segment-local `DeferredEntity`; typed replacement
commands may initialize additional values within that recording contract.

## Persistence

Snapshot provenance stores stable prefab keys, not local `PrefabId` values.
Restore resolves keys against the loading catalog and explicit compatibility
policy. Persisted runtime shape remains authoritative; provenance does not imply
that current prefab defaults silently rewrite saved entity data.

## Boundary

The current mechanism compiles constant component defaults, inheritance, and
composition. Live text/XML tooling, scripting, hot reload, spawn tables, and a
general mod package system are future authoring layers. They must compile to this
bounded catalog rather than add a second runtime prefab representation.
