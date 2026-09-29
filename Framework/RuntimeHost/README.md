# Framework Runtime Host — P2 manifest-planning foundation

This BCL-only project begins P2's pre-authority package composition contract. It is **not yet** a server module loader or authoring environment. No production server currently consumes it, and P2 remains active.

Selected packages have canonical stable IDs, three-component versions, a diagnostic class, and one of three compile stages: Contracts, Core, Scripts. A strict `UltraSim.ServerModule/v1` JSON manifest accepts finite package dependencies, required/optional/provided capabilities, ordering constraints, and conflicts. It rejects unknown/duplicate fields and invalid identities rather than silently normalizing them. Package IDs are not paths, process-local indices or proof of permission to load code.

`ModuleCompositionPlanner` resolves at most 128 packages before authority startup. It checks duplicate packages/capability providers, required package versions, missing capabilities, conflicts, backward stage edges and cycles, then emits a stable ordinal topological order. A missing optional capability is valid; a missing required one fails. Contracts can therefore remain selected after an optional Core is removed, while a consumer requiring that Core's capability fails explicitly. The focused test is a **planning model**, not P2's eventual real compiled Engine/facet survivability proof.

`ManifestFingerprint` hashes canonical selected metadata independent of input enumeration order. It deliberately **does not claim to be** the final `RuntimeCompositionFingerprint`: source bytes, compiled assembly/dependency identity and schema remaps are not represented yet. No compile cache may use this manifest-only value as its sole validity key.

The next P2 slices must add a server-owned `Scripts/` source tree, deterministic staged compilation and cache with exact source/dependency keys, bounded diagnostics, isolated load/lifecycle with rollback, real registration seams, Control Plane diagnostics and an end-to-end sample package. Neither an authoritative server start nor dynamic code admission is implemented here.

Run `dotnet run --project _Tests/UltraSim.Framework.RuntimeHost.Tests/UltraSim.Framework.RuntimeHost.Tests.csproj -c Release`; the canonical verifier also runs these tests.
