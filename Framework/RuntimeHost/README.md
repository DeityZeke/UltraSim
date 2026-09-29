# Framework Runtime Host — P2 package lifecycle

This BCL-only project plans bounded package compositions and owns optional runtime lifecycle entrypoints. It has no Roslyn, filesystem, ECS, Control Plane, Game or Network dependency. The server-owned compiler adapter is in [Server/Authoring](../../Server/Authoring/README.md). P2 is **CLOSED** after its closure verification; this remains an unshipped authoring API.

## Canonical vocabulary

| Term | Meaning |
| --- | --- |
| Runtime Package | Authored/deployed compilation and composition unit. ID, version, stage, class, dependency relations and optional entrypoint belong here. |
| Package capability | Declarative vocabulary made available by a selected package. `Provides`/`Requires`/`Optional` influence planning and do **not** demand a live object. |
| Runtime Service | One live object declared by `Services`, registered with `RuntimePackageContext.Provide`, and reached through a generation-fenced `RuntimeServiceHandle<T>`. Service relationships are separate from package capabilities. |
| Object Extension (Extension) | Sparse managed OOP state/behavior attached to an owner or global identity, outside ECS component storage. `ExtensionRegistry` binds a stable type ID to `ExtensionFamily<T>` once; an owner may hold multiple named instances. |
| Contribution | Bounded multi-contributor proof, distinct from exclusive services. `RuntimeContributionId` is **not** the final universal hook/facet/event system. |

Package ≠ Engine Core ≠ Runtime Service ≠ Extension ≠ ECS component. An Engine Core package may implement a domain Engine, expose a service and manage Extensions. The ACC archive called its owner-attached objects “Modules”; that historical name translates to UltraSim **Extensions**, not Runtime Packages.

`UltraSim.RuntimePackage/v4` is the strict unshipped manifest format (v3 is rejected, not reinterpreted). Its `Provides` entries are declarative package capabilities; `services` entries are exclusive live objects. A Contracts-only package can compile and remain selected with no entrypoint, no runtime object and `RuntimePackageState.Available`. `RuntimePackageClass` uses `Runtime`, `Definition`, `System`, `EngineCore`, `Kernel`, `Schema`, `Framework`; these classify packages and do not mean an attached Extension. `ContentTrustClass.ExecutableExtension` is a separately qualified client-content security term.

`aliases` declares bounded retired package IDs that map directly to one selected current package. Aliases are not separately selected packages. Duplicate owners, an alias also selected as an independent package, backward stage dependencies, and cycles fail planning. There are no alias chains: every alias terminates at one selected package. Dependency and ordering references resolve through that map before compilation. `RuntimeCompositionPlan.TryPlanSavedPackages` produces a bounded deterministic package-remap order using the selected plan order; it rejects duplicate/ambiguous saved identities, missing packages and version changes. It does not mutate saved data or claim to implement P4 DefinitionId/facet remaps. Process-local Generation is never a durable compatibility key.

`durableSchema` declares one exact `ISaveSetParticipant` service and canonical artifact key for a schema-extending package. The server authoring adapter validates that service after host initialization and exposes the complete participant list including an exact schema ledger. The ledger records schema-owner package IDs, versions, service IDs and artifact keys; Framework.Persistence's existing exact artifact-set validation rejects missing/extra owners. Removal, rename (even with a package alias), or version change of a durable schema fails load until an explicit migration/drop tool safely rewrites the save set. P2 does not implement such a tool or silently discard durable state. A package may not claim durable state while omitting its participant declaration. Actual DefinitionId/facet schema migrations belong to P4.

The host starts optional lifecycle owners in plan order through Register → Configure → Initialize, then publishes Running only after all owners succeed. A package-owned Control Plane publication may be staged in Initialize; it becomes visible after initialization and retires before Stop. Failures roll entered owners back in reverse order and aggregate cleanup faults. No host tick, reflection dispatch, compiler/cache polling or per-frame work exists. Generations fence retained service handles and contexts.

`ExtensionRegistry` is cold, bounded sparse state, not another ECS. High-cardinality homogeneous state stays in ECS. `Framework.Persistence` coordinates durable save sets; owning Engine/Extension families retain their own schema and serialize a bounded artifact. Individual Extension instances do not become save-set participants.

The manifest-only `ManifestFingerprint`, exact `CompileKey`, resolved `RuntimeCompositionFingerprint`, and process-local host `Generation` are different identities. See the authoring README for source/cache/load policy.

Focused gate: `dotnet run --project _Tests/UltraSim.Framework.RuntimeHost.Tests/UltraSim.Framework.RuntimeHost.Tests.csproj -c Release`.
