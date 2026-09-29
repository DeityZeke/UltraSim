# Framework Runtime Packages, Runtime Services, Object Extensions, and ECS

Status: CANONICAL current architecture

Primary references:
- [Architecture](../../Docs/ARCHITECTURE.md)
- [Framework Runtime Host](../../Framework/RuntimeHost/README.md)
- [Server Authoring](../../Server/Authoring/README.md)
- [Pre-many-player roadmap](../../Docs/Plans/UltraSim_Pre_Many_Person_Live_Testing_Roadmap.md)
- [P2 closure evidence](../../_Benchmarks/Baselines/framework-p2-closure-2026-09-29.md)

Related topics:
- [Framework Persistence and Control Plane publication](framework-persistence-and-control-plane.md)
- [Definition / presentation / asset independence](definition-presentation-asset-independence.md)

## Canonical vocabulary

These terms are deliberately distinct:

| Term | Meaning |
|---|---|
| Runtime Package | Authored/deployed compilation and composition unit: ID/version, stage/class, dependencies, source/compiled identity, optional lifecycle entrypoint. |
| Package capability | Declarative composition fact used by planning. It does not imply a live object. |
| Engine Core | Reusable gameplay/domain capability composed by the Game Implementation. |
| Runtime Service | One live object explicitly provided by a lifecycle owner and reached through a generation-fenced typed handle. |
| Object Extension / Extension | Sparse managed OOP state/behavior attached to an owner or global identity outside ECS component storage. |
| Contribution | Bounded multi-contributor registration seam. It is not a universal hook/event/facet/property system. |
| ECS Component/System | High-scale data-oriented state and bulk execution. |

Permanent distinction:

> Runtime Package ≠ Engine Core ≠ Runtime Service ≠ Object Extension ≠ ECS Component.

The historical ACC term **Module** maps most closely to an UltraSim **Object Extension**, not to a Runtime Package. “EngineHost”, “Module Host”, “SideCar”, and “Sidecar” are retired as current UltraSim architecture terms. Historical documents may retain those words; translate their semantics using the current vocabulary rather than reviving the names.

## Framework and ECS are complementary substrates

UltraSim does not require every gameplay object to be an ECS Entity.

Use the Framework/OOP path when the workload is naturally:

- sparse;
- low-cardinality;
- behavior-rich;
- irregular in lifetime/state;
- easier to express as ordinary strongly typed C# objects.

Use ECS when the workload is naturally:

- numerous/high-cardinality;
- homogeneous or struct-shaped;
- processed in bulk;
- hot/update-dense;
- sensitive to cache locality, GC pressure, or per-entity overhead.

Engine Cores may use either substrate or both. A rich Extension may optionally bridge to an ECS Entity for movement, health, spatial state, or other high-volume facts. The Extension identity remains conceptually distinct from Entity identity.

This allows both extremes without architectural contortions:

~~~text
Ocarina-like game
    Framework + Runtime Packages + Services + Extensions
    little or no ECS required

Cities-like simulation
    Framework for composition/lifecycle
    ECS for nearly all gameplay state
    few or no Extensions

UltraSim
    hybrid chosen per workload
~~~

Do **not** optimize Object Extensions into a second ECS. If an Extension family becomes numerous, homogeneous, hot, and data-oriented, move the relevant state to ECS instead of rebuilding archetypes/sparse sets inside Framework.

## Object Extension semantics

An Object Extension may:

- be attached to a stable owner identity or to a global owner;
- exist without any ECS Entity;
- have multiple named instances of the same Extension family on one owner;
- carry rich managed state and behavior;
- be managed by an Engine Core or Runtime Service;
- participate in persistence through its owning family/Engine.

Cold registration binds stable Extension type identity to a typed family. Runtime code should retain typed family/service handles instead of repeatedly performing string/reflection lookup.

Illustrative semantics:

~~~text
Actor A
    QuestExtension("main")
    QuestExtension("guild")
    VendorExtension("default")
~~~

Multiple same-type instances are intentional. The older ACC Dictionary-of-Type-to-Module one-per-type restriction is not carried forward.

One authoritative owner exists for each piece of state. A convenience Extension property may route to ECS-owned state, but it must not silently maintain a second divergent authoritative copy.

## Runtime Package and service lifecycle

P2 closed with deterministic trusted server C# authoring and startup-time composition.

Key rules:

- Contracts/Core/Scripts compile stages are deliberate.
- Contracts-only packages need no dummy runtime entrypoint or service object.
- package capabilities and live Runtime Services are separate declarations.
- lifecycle is explicit Register → Configure → Initialize → Running → Stop.
- failure rolls entered owners back in reverse order; no half-started authority publishes.
- Runtime Service handles and package contexts are process-generation fenced.
- built-in and dynamically compiled packages use the same Runtime Host lifecycle vocabulary where applicable.
- package aliases/remaps are admission compatibility, not automatic saved-byte migration.
- trusted server C# is not a malicious-code sandbox and arbitrary hot unload/reload is not promised.

## Efficiency rule

“Scale through Efficiency” applies by choosing the right representation, not by forcing every abstraction into arrays.

Cold startup/composition may reasonably allocate, reflect, compile, sort, validate, and build maps. After composition freezes:

- no Runtime Host tick/poll loop;
- no compiler/cache filesystem polling;
- no reflection-driven gameplay dispatch merely because code was dynamically authored;
- no Framework tax in hot ECS loops;
- no recurring allocation solely because a package/service is hosted.

Optimize measured hot paths aggressively; keep cold flexibility when it materially improves authoring and composition.

## Next-boundary notes

P3 owns platform/runtime adapter boundaries such as Godot/headless realization.

P4 owns typed Engine Contracts, definition facets/stores/tags, DefinitionId migrations, and first-party Game-definition dogfooding. Generic Runtime Contributions must not silently become a universal hook/property system before those typed consumers earn specific contracts.
