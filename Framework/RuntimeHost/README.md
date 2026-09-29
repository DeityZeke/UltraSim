# Framework Runtime Host — P2 built-in lifecycle foundation

This BCL-only project supplies the cold, deterministic package runtime used by the second P2 pass. P2 remains **ACTIVE**: no production server consumes dynamically authored packages yet, and source compilation/cache, isolated assembly loading, aliases/migrations, real Engine facets, and the compiled Contracts-only survivability proof remain later P2 work.

Package manifests use strict `UltraSim.ServerModule/v2` JSON: canonical IDs, three-part versions, half-open required dependency ranges (`minimumVersion` inclusive, optional `maximumVersionExclusive`), Contracts/Core/Scripts stages, bounded relations, exclusive `Provides`, required/optional capabilities, order and conflicts. `EngineCore` and `FrameworkExtension` are diagnostic classifications, not privileges. The v1 manifest was an unshipped P2 foundation; v2 deliberately rejects v1 rather than silently reinterpreting it. The manifest fingerprint changes with version bounds but is **not** a source/compiled-composition fingerprint.

`RuntimePackageHost` starts built-in `IRuntimePackage` implementations in stable plan order through Register → Configure → Initialize, then publishes all selected packages as Running together. A failure rolls back entered packages in reverse order and preserves/aggregates cleanup faults. Stop is owner-thread-bound, deterministic and idempotent after success. Retained capability handles and package contexts carry a process-local generation fence. Register explicitly publishes one object per declared exclusive capability; `ModuleContributionId` is a separate bounded multi-contributor seam. There is no tick hook, static registration, reflection scan or per-frame polling.

`RuntimeSidecars` is bounded sparse OOP state keyed by owner, stable sidecar type and instance name; `default` is a convenient singleton instance. It permits two same-type instances on one owner and a global owner. It does **not** replace ECS for bulk data. A Framework-only test, referencing no ECS assembly, saves and reloads two sidecars using the BCL-only save-set coordinator; the participant owns its exact artifact schema.

The optional `Bridges/ControlPlane.RuntimeHost` adapter explicitly publishes lifecycle diagnostics and package rows. RuntimeHost retires attached publications before calling package Stop, so the registry cannot dispatch into a retired owner. Control Plane does not discover capabilities, resolve dependencies or own runtime objects.

The ACC archive informed explicit lifecycle and sparse owned state, but its CLR-Type durable identity, one-sidecar-per-Type map, arbitrary per-system save files and reflective hot-path construction were not copied.

Run `dotnet run --project _Tests/UltraSim.Framework.RuntimeHost.Tests/UltraSim.Framework.RuntimeHost.Tests.csproj -c Release`; the canonical verifier includes it.
