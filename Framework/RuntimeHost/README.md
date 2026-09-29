# Framework Runtime Host — P2 package lifecycle

This BCL-only project plans bounded package compositions and owns optional runtime lifecycle entrypoints. It has no Roslyn, filesystem, ECS, Control Plane, Game or Network dependency. The server-owned compiler adapter is in [Server/Authoring](../../Server/Authoring/README.md). P2 remains **ACTIVE**; this is an unshipped authoring API.

## Canonical vocabulary

| Term | Meaning |
| --- | --- |
| Runtime Package | Authored/deployed compilation and composition unit. ID, version, stage, class, dependency relations and optional entrypoint belong here. |
| Package capability | Declarative vocabulary made available by a selected package. `Provides`/`Requires`/`Optional` influence planning and do **not** demand a live object. |
| Runtime Service | One live object declared by `Services`, registered with `RuntimePackageContext.Provide`, and reached through a generation-fenced `RuntimeServiceHandle<T>`. Service relationships are separate from package capabilities. |
| Object Extension (Extension) | Sparse managed OOP state/behavior attached to an owner or global identity, outside ECS component storage. `ExtensionRegistry` binds a stable type ID to `ExtensionFamily<T>` once; an owner may hold multiple named instances. |
| Contribution | Bounded multi-contributor proof, distinct from exclusive services. `RuntimeContributionId` is **not** the final universal hook/facet/event system. |

Package ≠ Engine Core ≠ Runtime Service ≠ Extension ≠ ECS component. An Engine Core package may implement a domain Engine, expose a service and manage Extensions. The ACC archive called its owner-attached objects “Modules”; that historical name translates to UltraSim **Extensions**, not Runtime Packages.

`UltraSim.RuntimePackage/v3` is the strict unshipped manifest format. Its `Provides` entries are declarative package capabilities; `services` entries are exclusive live objects. A Contracts-only package can compile and remain selected with no entrypoint, no runtime object and `RuntimePackageState.Available`. `RuntimePackageClass` uses `Runtime`, `Definition`, `System`, `EngineCore`, `Kernel`, `Schema`, `Framework`; these classify packages and do not mean an attached Extension. `ContentTrustClass.ExecutableExtension` is a separately qualified client-content security term.

The host starts optional lifecycle owners in plan order through Register → Configure → Initialize, then publishes Running only after all owners succeed. A package-owned Control Plane publication may be staged in Initialize; it becomes visible after initialization and retires before Stop. Failures roll entered owners back in reverse order and aggregate cleanup faults. No host tick, reflection dispatch, compiler/cache polling or per-frame work exists. Generations fence retained service handles and contexts.

`ExtensionRegistry` is cold, bounded sparse state, not another ECS. High-cardinality homogeneous state stays in ECS. `Framework.Persistence` coordinates durable save sets; owning Engine/Extension families retain their own schema and serialize a bounded artifact. Individual Extension instances do not become save-set participants.

The manifest-only `ManifestFingerprint`, exact `CompileKey`, resolved `RuntimeCompositionFingerprint`, and process-local host `Generation` are different identities. See the authoring README for source/cache/load policy.

Focused gate: `dotnet run --project _Tests/UltraSim.Framework.RuntimeHost.Tests/UltraSim.Framework.RuntimeHost.Tests.csproj -c Release`.
