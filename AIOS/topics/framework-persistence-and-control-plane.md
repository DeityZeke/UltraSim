# Framework Persistence and Control Plane Publication

Status: CANONICAL current architecture

Primary references:
- [Framework Persistence](../../Framework/Persistence/README.md)
- [Persistence architecture](../../Docs/PERSISTENCE.md)
- [Framework Control Plane](../../Framework/ControlPlane/README.md)
- [Framework Runtime Host](../../Framework/RuntimeHost/README.md)
- [P2 closure evidence](../../_Benchmarks/Baselines/framework-p2-closure-2026-09-29.md)

Related topics:
- [Framework Runtime Packages / Services / Extensions / ECS](framework-runtime-packages-services-extensions.md)

## Persistence ownership

UltraSim persistence is participant-owned durable meaning inside a Framework-owned transaction.

A participant/Engine/Extension family owns:

- its durable schema and semantic meaning;
- schema/version compatibility readers or explicit migrations when supported;
- capture of its authoritative state;
- staged decode/reconstruction;
- validation of its own invariants.

Framework Persistence owns:

- one coordinated save epoch/sequence;
- participant admission and artifact bounds;
- hashing/integrity and crash-durable publication;
- complete-generation manifests and CURRENT publication;
- staged restore;
- cross-participant reference resolution;
- validation before authority publication;
- recovery selection;
- exactly-once generation commit semantics.

This is intentionally stronger than the archived ACC pattern where each system directly writes arbitrary files.

Individual Object Extension instances are normally **not** separate save participants. The owning Engine/Extension family captures all relevant instances into one bounded participant artifact.

ECS Components continue through ECS persistence. Do not duplicate the same authoritative state in an Engine artifact merely because an Engine operates on it.

## Persistence is not ECS-dependent

Framework.Persistence is BCL-only. Its neutral durable reference is independent of Entity.

ECS maps PersistentEntityKey into the neutral reference domain through the Server persistence bridge. A Framework-only/OOP application can save and restore Engines/Extensions without importing ECS.

That preserves the intended application spectrum:

~~~text
Framework-only OOP game
    save participants work

Framework + ECS simulation
    ECS is one participant/domain among others
~~~

## Durable schema packages fail closed

A Runtime Package that extends durable save schema declares its participant ownership explicitly.

The save-set schema ledger records the exact schema-owner package identity/version/service/artifact relationship. Removal, incompatible version change, renamed identity, or changed artifact ownership fails load before publication unless an explicit migration/drop operation has transformed the saved meaning.

A package alias changes identity admission; it does **not** magically migrate participant bytes.

P2 deliberately did not invent a generic saved-data transformer. Definition/facet migrations belong to P4 after the typed Definition system exists.

## Staged restore rule

The model remains:

~~~text
Capture
    one logical epoch
        ↓
encode/write after authority lease

Restore
    Prepare
        ↓
    Resolve candidate references
        ↓
    Validate
        ↓
    Commit complete generation once
~~~

Prepared authoritative references resolve against staged candidate providers, not against the active generation being replaced. A staged owner that owns a namespace prevents fallback to unrelated/old authority.

Malformed or unsupported participant state rejects before publication and leaves the active generation unchanged.

## Control Plane direction

The Control Plane is an independent cold management substrate.

The permanent direction is:

~~~text
owner/subsystem
    explicitly publishes descriptors/provider
        ↓
Control Plane
    discovers, authorizes, routes, renders
~~~

Not:

~~~text
Control Plane
    scans/reflection-discovers owner internals
~~~

Runtime Host and Engine/package owners may publish management semantics explicitly through adapters/publications. The Control Plane does not become:

- the Runtime Host;
- package dependency resolution;
- a Runtime Service locator;
- Engine capability discovery;
- persistence ownership;
- arbitrary cross-provider gameplay messaging.

Runtime dependencies use typed Framework/Engine contracts. Operator observation/control uses the Control Plane.

## Lifetime coupling

Provider lifetime is explicitly fenced.

For a package-owned management surface, retirement occurs before the publishing owner stops/disposes. Retained requests carry provider generation and stale generations reject rather than being silently routed to a replacement owner.

Conceptually:

~~~text
stop admitting owner work
    ↓
retire Control Plane publication
    ↓
old provider generation fenced
    ↓
Stop owner/package
    ↓
dispose/release owner
~~~

This complements Runtime Host generation fencing for Runtime Service handles; the two generations solve different lifetime problems.

## Separation of registries

Keep these concepts distinct:

- Runtime Host: who is selected/running and who owns lifecycle;
- package/runtime capability/service registry: what runtime capability/service exists;
- Control Plane: what owners choose to expose for management;
- Framework Persistence: what owners choose to persist and how save generations are coordinated.

Their APIs may cooperate, but none should collapse into a universal global registry.
