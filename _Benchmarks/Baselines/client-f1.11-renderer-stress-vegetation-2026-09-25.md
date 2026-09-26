# F1.11 — Renderer stress consumers / Remote resource parity / vegetation

Campaign filename stamp: 2026-09-25. Implementation and execution span September 25–26; accepted final runs were September 26, 2026 (America/Chicago).

## Disposition and lineage

F1.11 CLOSED after the uninterrupted Release/export pass described below. F1.12 is next/unstarted. W8/W9/W10 remain unstarted. Vegetation is a **default-Off, opt-in presentation prototype**, not a selected shipping-quality feature or authoritative gameplay.

| Tree | Commit |
| --- | --- |
| Starting clean main | `3b105dfe99a5faed94eed28da5da9dd1d4aed934` |
| Renderer/resource implementation | `1ac8e61460bf0eb5fd8e04114de8583c134613c1` |
| W7 counter-unit assurance / tested implementation | `b59dd38282477ae4cf2efd631d792e2d7a3504b9` |
| Final evidence/docs | Enclosing evidence commit; `git log -1 --format=%H -- _Benchmarks/Baselines/client-f1.11-renderer-stress-vegetation-2026-09-25.md` |

There were no intervening commits and no pre-existing worktree edits. Native samples identify their exact tested commit. The evidence commit changes docs/data only, not the tested implementation.

## What changed

Local and Remote use [GodotCatalogWorldAssetResolver](../../Client/Godot/Adapter/GodotCatalogWorldAssetResolver.cs), the extracted existing Cabin mesh/palette consumer, through the same effective Texture/Material resolution and `GodotDerivedResourceStore`. Remote no longer unconditionally uses `assets:null`. This is not a general mesh importer or new atlas.

The server independently advertises the small passive `server.concept-demo.world-presentation` package, namespace `cabin`, with the existing diagnostic wall/wood PNG leaves and StandardLit materials. The optional presentation activation advances from suffix 1 to **2** because its immutable plan now includes a third package. Required structural World/UI activation, exact semantic proof and Game manifest remain unchanged. No old activation is retargeted. Online still contains no playable art/world/local authority.

User → authenticated Realm → explicitly installed world.local → Primitive remains the chain. The new typed activation-identity reopen overload reads **already authorized activation metadata**, then checks its recorded plan/result through the existing exact-key open. CAS possession never authorizes a scope. Local resource fallback reopens the known Cabin Default activation; no fake Realm or content-scope migration is involved.

Changed wall/wood content updates existing mesh surfaces and retires replaced derived leases. Unchanged geometry/instance groups and semantic state remain intact. Crossing from available to absent resources may rebuild bounded **native realization only**, never reset the semantic cache/World composition. Resolver/store/generation leases retire in owner order; persistent CAS/derived files are not expected to become physically empty.

## Reproducible commands and environment

```bash
dotnet run --project _Tests/UltraSim.Client.Content.Tests -c Release
dotnet run --project _Tests/UltraSim.Network.Application.Tests -c Release
dotnet run --project _Tests/UltraSim.Network.Application.Tests -c Release -- --w7-r1-only
dotnet build _Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj -c Debug -p:Optimize=true -p:DebugType=portable -m:1
ULTRASIM_F111_TESTED_HEAD=b59dd38282477ae4cf2efd631d792e2d7a3504b9 \
  '/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64' \
  --path _Tests/UltraSim.S0.Godot.Tests --rendering-method gl_compatibility \
  res://F111RendererStressAcceptance.tscn -- --evidence=/absolute/output.json
./verify.sh --architecture-only
./verify.sh --configuration=Release
```

Godot headless uses the same scene but the bounded 100-object lifecycle control; it does not certify GPU pixels/culling. Actual native run: Godot 4.7.1 Mono a13da4feb, Compatibility/OpenGL 3.3, NVIDIA RTX 4070 Ti SUPER (16,376 MiB), driver 615.71.09. CPU Ryzen 7 7800X3D, 8 cores/16 threads; 32,738,320,384 bytes system RAM. CachyOS Linux 7.2.6-1-cachyos x86_64; SDK 8.0.131, executing .NET 8.0.31 (installed host 10.0.12). Managed verification is Release; native tests use the repository's optimized Debug convention. No CPU affinity, isolated desktop, integrated-GPU/Deck control, or server-process CPU claim. Each native measurement ran alone, without competing benchmark jobs.

## Maintained workload and clocks

[F111RendererStressAcceptance](../../_Tests/UltraSim.S0.Godot.Tests/F111RendererStressAcceptance.cs) uses the production semantic cache, structural residency, sliding window, World renderer, actor presenter, actual Gameplay Output SubViewport, common catalog/derived consumer and finite environment provider.

121 authored sloped base chunks; 162 semantic slots and 49 render slots. Objects occupy the same central nine chunks: 50% frozen statics, 25% static Multi pieces, 25% public Chest/Sign records. 1,000 = 500/250/250; 5,000 = 2,500/1,250/1,250; 10,000 = 5,000/2,500/2,500. Radius adds terrain/grass, **not object population**. There is one T1.1 upper grass Top surface over an intentionally absent base cell.

64/128/256 disclosed actor records use the real F1.10 policy: two near Detailed X/Y animated actors, 62/126/254 Cheap actors in three groups; primitive control replaces the two Detailed visuals but keeps the policy/population. This is synthetic presentation cardinality, **not product CCU**. Moving pose submission is client presentation, not a new simulation/replication workload.

Native owner-thread wall samples: 32 warm calls, then 256 raw timestamp differences, `Stopwatch.Frequency=1,000,000,000`. World service covers `ProcessFrame` with no pending build; actor service covers presented-pose submissions plus `UpdateOriginAndLifetimes`. A separate settled cohort excludes pose submissions. These clocks exclude awaited frames, snapshots, reveal creation, initial construction and GPU diagnostics. They are not complete Godot frame time, thread CPU or server occupancy. Cold structural residency/build, public refresh, actor realization, derived preparation, three-round churn, reveal and retirement are separate enclosing intervals.

GPU/viewport-render CPU, draw calls and native primitives are eight short samples per view after settling. Percentiles use nearest-rank samples; eight samples' p95/p99 are their max. They are not sustained FPS ratings. No stage percentile is added/subtracted to manufacture another metric.

| Objects | Actors | Radius | Consumer | World Nodes/resources | Patches | Moving actor mean/p95 µs | Settled actor mean µs |
| ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: |
| 1000 | 64 | 1 | Intended | 92/64 | 0 | 152.858 / 156.368 | 117.376 |
| 1000 | 64 | 1 | Intended + grass | 101/76 | 9215 | 168.002 / 175.388 | 129.666 |
| 5000 | 128 | 1 | Intended | 92/64 | 0 | 292.886 / 339.136 | 232.510 |
| 5000 | 128 | 1 | Intended + grass | 101/76 | 9215 | 219.740 / 229.097 | 207.792 |
| 10000 | 256 | 1 | Intended | 92/64 | 0 | 447.110 / 452.614 | 418.612 |
| 10000 | 256 | 1 | Intended + grass | 101/76 | 9215 | 443.360 / 455.045 | 409.716 |
| 10000 | 256 | 2 | Intended + grass | 181/108 | 25599 | 447.284 / 455.545 | 413.984 |
| 10000 | 256 | 3 | Intended + grass | 301/156 | 50175 | 442.822 / 452.705 | 410.687 |
| 10000 | 256 | 1 | Primitive | 92/64 | 0 | 439.160 / 448.195 | 408.309 |

Raw data preserves both actual native runs, including the earlier `1ac8e614` run. Identical renderer code produced materially different cold/service timings (256 moving actors approximately 652–654 µs means earlier, 439–447 µs later). **This is run variability, not an optimization by the assurance counter change.** World idle service means were about 0.06–0.10 µs, near timestamp/bookkeeping resolution; do not generalize that number to drawing or rebuilding 10,000 objects.

## Vegetation ownership and geometry

[ClientVegetationScatter](../../Client/Core/ClientVegetationScatter.cs) hashes world seed `0xF1112026`, full spatial key, cell and profile version 1. Exactly one patch per eligible present Grass base cell (maximum 1,024/chunk), with a reproducible 100–400 mm intra-cell root. Height uses the World-owned canonical base triangle query, not chunk height or an independent bilinear surface. All eight blades in this prototype share that sampled root; their seed/yaw/width/height vary. This is an intentionally clustered patch, not eight separately grounded offset roots.

[GodotVegetation](../../Client/Godot/Adapter/GodotVegetation.cs) owns one shared mesh/material/shader and 20,480-byte staging array per opted-in renderer. One chunk owns one MultiMesh/Node; no blade/patch ECS entity, public ID, collision object or process callback. The finite triangle list has eight blades × two segments × six vertices = **96 source vertices / 32 triangles per patch**, no indices/connector strip vertices. VERTEX_ID decodes blade/segment/side; INSTANCE_CUSTOM carries seed/cut/reserved density/profile. TIME-driven wind creates no CPU transform rewrite loop.

| Radius | Grass batches | Patches | Logical blades | Nominal submitted vertices |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 9 | 9,215 | 73,720 | 884,640 |
| 2 | 25 | 25,599 | 204,792 | 2,457,504 |
| 3 | 49 | 50,175 | 401,400 | 4,816,800 |

Base void remains excluded even below the authored upper grass Top band (center count 1,023). Additional surfaces are deliberately not vegetation sources. Every root matches an independent sloped-plane height control. Finite chunk AABBs include full terrain height range, blade height, wind and width; real native convex-frustum inclusion/disjoint culling passed. Horizontal/1.25-km vertical rebase preserves local row transforms and build count.

Cut changes one custom-data row: .75 collapses six blades, does not touch semantic hash or rebuild chunks. Later measured calls were approximately 1.95–2.77 µs; the first call was 256.477 µs and is retained, not hidden. Collapsed blades still submit vertices; no compaction/regrowth/loot/authoritative harvesting is claimed.

## GPU/display/environment evidence

At fixed 10,000 objects / 256 actors, all 1920×1080/100%, 1280×720/100%, 1920×1080/50% controls retain identical World/actor populations and source presentation. Primary matched view is explicitly **diagnostic top-down**, not mislabeled stock Orbit. Real stock Orbit and ThirdPerson realizations and near-horizontal views are additional controls.

Radius-3 final run: diagnostic top-down 1920 shadow-Off/On mean GPU 1.630/2.481 ms; 1280 1.736/2.453; 1920/50% 1.882/3.900 (On max 6.389 ms). These tiny windows have visible variability and are not monotonic-resolution guarantees. Reveal mean 3.087 ms; stock Orbit/ThirdPerson 2.890/2.684; near-horizontal grass-caster Off/On 2.522/2.859. Earlier 1920 shadow-On mean 3.835 ms/max 7.681 is also retained. No overdraw hardware counter was available: view/density, draw/primitives and GPU duration are **overdraw-sensitive controls**, not a measured pixel-overdraw ratio.

The finite common provider exercises Dawn/Noon/Dusk/Night × Temperate/Dry/Cold with real native direct sun/shadows and ambient/background palette. Temperate time GPU controls are retained. This is debug presentation, not authoritative world time/weather, volumetric atmosphere or advanced GI. Existing Cabin layout/physics/art policy is unchanged; the small palette is diagnostic, not a completed painted art pass. Product opt-in:
```text
--f1-11-vegetation
--f1-11-time=dawn|noon|dusk|night
--f1-11-atmosphere=temperate|dry|cold
--f1-11-no-shadows
```
Ordinary product defaults remain unchanged, including grass casting Off. Vegetation remains default Off pending lower-spec evidence and later human evaluation.

## Allocations, resources, content and retirement

World unchanged service: zero warmed managed allocation. Settled content-backed/primitive actor cohorts: zero. Moving content-backed actor service: **6,720 bytes / 256 updates**, separately reconciled; moving primitive: zero. Reveal is cold work (256 actors: 50,800 bytes, approximately 1.15–1.20 ms); it creates bounded labels without changing Cheap body tiers. Native animation/process/render work outside these calls is not included.

At 10,000 objects semantic array reservation is 42,170,278 bytes. Intended source owns 21 meshes and 18 primitive materials (including terrain), with two derived material leases; derived store live textures/materials = 2/2. Detailed actor resolver realizes two resources and retains 4,723,368 artifact bytes. Cheap actor groups own nine resources separately. These are different owner counts, **not total process RSS or exclusive VRAM**. Renderer MultiMesh counters describe static/public groups (54); vegetation adds 9/25/49 and actor Cheap adds three. Renderer resource counts include terrain meshes, own MultiMeshes, and vegetation's three shared resources, but exclude borrowed source/derived/actor resources. Grass staging is not its total CPU/GPU footprint; native instance buffers and engine resources are additional.

Cold stress store builds three textures (Default two + User replacement), subsequent independent stores reopen four decoded artifacts across apply/restore, zero rebuilds. Local/Realm matched catalogs have identical stable resolved fingerprint, texture hash and derived key; User update/disable preserves mesh RID, semantic hash, actor count and completed-build count. Current-generation actor replacement/promotion remains covered by T010/F1.10 acceptance.

Three enter/retain/retire/re-entry rounds reproduce exact edge scatter with a **new native MultiMesh**, no Node/resource-count drift. Exact actor removals and renderer disposal return live actors/renderer/grass to zero; all generation blob/catalog pins return to zero. Persistent CAS/derived files may remain. Native sunlight/environment/resources have explicit owner cleanup.

## Verification and unexpected gate disposition

- Client.Content: 38 passed; BuiltInTestWorlds: 9; Client Core: 36; Local sessions: 23; integrated gates: 39; Game replication: 181; World: 115.
- Authenticated Network.Application complete suite plus 16 W7 structural tests passed. Seven focused W7-R1/accounting controls passed.
- Native Compatibility: nine stress rows + parity passed on tested commit; bounded headless F1.11 plus F1.9B/F1.10/T010/S08 passed canonically.
- Architecture: all four groups passed. **One uninterrupted `./verify.sh --configuration=Release` exited 0 on b59dd382.** Actual SinglePlayer/Online/Complete exports, Contentless cold/warm/profile change, Complete-hosted, PF1/PF2 and F0.7 all passed.
- Remote actual cold/warm: Realm wall hash `814d2ffdd40fca8b30ebc4b894af32d977e157fa3fcc4f7f0ae0a79c266611ef`, live textures/materials 2/2, derived disk hits 0/2. Warm Pack bytes and required content fetch bytes 0. The printed 1,958 cold required bytes are **not** total optional actor/palette traffic.
- Opt-in exported W7: two stale ordinary rejections, two missing/prepared attempts, zero missing-Begin/malformed/contradictory/capacity, guarded retirement and warm bytes 0; no Far arrival/readiness rejection.
- PF1: 8 bots +2 exported clients, conserved sword 1/1/1, clean 11 joins/leaves. PF2: reconciled UDP/retirement, 11 lifetimes. F0.7: six clean boundaries, fresh Remote epochs, exact profile reuse, Local→Local→RemoteA→RemoteB→RemoteA→Local.
- Canonical log SHA-256: `984a1f8687f77db598c0414109125992c48adc3069b8e6448f50fe5a59012f20`. [Structured evidence](client-f1.11-renderer-stress-vegetation-2026-09-25.json) preserves pass output, prior failure/repair, counts and per-run statistics; [tested raw](client-f1.11-renderer-stress-vegetation-2026-09-25.raw.json) and [earlier control raw](client-f1.11-renderer-stress-vegetation-2026-09-25.control.raw.json) retain exact samples (including integer identities).


One full Release attempt on `1ac8e614` stopped at W7 seam counter equality (RejectedDeliveries 1, missing-resident/prepared attempts 2). The existing assertion conflated ordinary rejection with segmented Snapshot Begin rejection, which intentionally belongs to RejectedSnapshots. A deterministic production-class probe establishes a valid missing-resident Begin: ordinary rejection +0, snapshot rejection +1, missing resident +1, queued negative proof +1, no snapshot candidate or semantic mutation.

The separate `b59dd382` assurance commit adds that subset diagnostic and reconciles ordinary + missing-Begin attempts **exactly**, retains the <=4 stale-attempt bound and demands zero malformed/contradictory/capacity outcomes. Exported W7 uses the same accounting. Delivery/proof decisions did not change. This is a verification/diagnostic defect, not an unresolved product false-knowledge fault. No accepted gate was skipped. Intermediate authoring/build corrections (wrong package namespace rejected by strict validation, wrong typed file kind, dummy headless geometry readback and disposed diagnostic misuse) were corrected before freezing the implementation.

## Ordered closure handoff (1–48)

1. Starting HEAD: `3b105dfe99a5faed94eed28da5da9dd1d4aed934`.
2. Tested implementation: `b59dd38282477ae4cf2efd631d792e2d7a3504b9`.
3. Final evidence HEAD: enclosing docs/evidence commit, identified by git history above and final handoff.
4. Commits: renderer/resource `1ac8e614`, independent assurance `b59dd382`, then evidence/docs.
5. Files changed: exact `filesChanged` list in the companion JSON; 19 implementation/test files, seven docs and four evidence artifacts.
6. Remote gap: closed; no unconditional null world resolver.
7. Resolver: common Local/Remote catalog/derived consumer; no new mesh importer/atlas.
8. Precedence: User/Realm/Local/Primitive tested; semantic activation independent.
9. Remote cold/warm: actual exported Realm textures/materials 2/2, derived disk hits 0/2, warm required/Pack bytes zero.
10. Stress counts: 1,000/5,000/10,000 objects; 64/128/256 actors; 121 authored chunks.
11. Vegetation source: present Grass BaseTerrain only; no new World data format.
12. Scatter: fixed seed/full key/cell/profile hash and canonical triangle height.
13. Granularity: one patch/cell, eight shared-root blades, maximum 1,024/chunk.
14. Topology: two triangle quads/blade, 96 vertices/32 triangles per patch, no indices/connectors.
15. Patch/MultiMesh: 9,215/9; 25,599/25; 50,175/49 at radius 1/2/3.
16. Logical blades/nominal vertices: 73,720/884,640; 204,792/2,457,504; 401,400/4,816,800.
17. AABB/culling: real finite-frustum inclusion/disjoint checks passed.
18. Void: excluded despite an upper grass surface; center 1,023 patches.
19. Height: every root matches the independent sloped-plane control.
20. Wind: shader TIME; no per-blade CPU/Node/process update.
21. Cut: one custom row, .75 collapses six blades; no semantic mutation/rebuild; cold and later timings retained.
22. Shadows/overdraw: direct shadow/dense/near-horizontal GPU controls; no hardware overdraw ratio claimed.
23. Cabin: common ordinary content/derived consumer and opt-in prototype; final painted art/GI not claimed.
24. Environment: 12 finite time/atmosphere combinations, native sun/ambient/shadow state; presentation-only.
25. Static/public counts: 50% statics, 25% Multi pieces, 25% public; fixed across radius/output.
26. Actors: Detailed/Cheap = 2/62, 2/126, 2/254 under actual stock policy.
27. Names: reveal all actors without promotion; cold label work and retirement retained.
28. Distance: radius 1/2/3, same 10,000-object/256-actor population.
29. Pixels: 1920/100%, 1280/100%, 1920/50%, no population expansion.
30. Derived: first store three texture builds including User; later stores four disk hits/no rebuild.
31. Replacements: matched Local/Realm result/key, User exact restore and live-world mesh/semantic/build identity conservation.
32. CPU: raw 256 owner-wall samples per service family, separate cold/churn/reveal/retirement; both variable runs retained.
33. Allocation: unchanged world/actors zero; moving intended 6,720 bytes/256 calls, primitive zero; reveal cold 50,800 bytes at 256.
34. Native ownership: World Nodes/resources and separate source/derived/actor/grass counts above; not global RSS/VRAM.
35. GPU: actual Compatibility viewport samples; eight per view, not sustained FPS.
36. Churn: three rounds, exact scatter, new native identity after retirement, no count drift.
37. Rebase: horizontal and 1.25-km vertical origin controls preserve local rows/build count.
38. Far Gate: actual exported opt-in vegetation 127→128→127/retirement/warm reprepare passed.
39. W7-R1: seam start, third Pack, four-Pack corner and deterministic Snapshot Begin accounting passed.
40. F1.9B: NodeMultiMesh/PerRow unchanged.
41. F1.10: conservative hybrid/mandatory features unchanged.
42. Formats: settings5, Game33, transport2, WorldPack1, manifest1, worldview2, participant64 unchanged.
43. Architecture: all four groups passed, including final docs/artifact links.
44. Release: uninterrupted exit 0 on tested implementation; earlier failure and independent assurance repair retained.
45. Evidence: this MD, companion JSON, tested raw and earlier control raw at the same basename.
46. W8/W9/W10: not started.
47. F1.12: next, not started; PF6 still waits for it.
48. Closure worktree: clean after the final evidence/docs commit; implementation unchanged since tested tree.

## Remaining risks / next checkpoint

F1.12 only next. Human motion feel/correction tails remain a dogfooding issue, not addressed here. Prototype base-only clustered grass, unselected shadow/culling/coverage tradeoffs, absent integrated/Deck evidence and short non-isolated GPU windows remain limitations. Advanced GI, terrain editing/live W8 transactions, general vegetation gameplay, GPU interpolation, skeletal crowds/impostors and atlas promotion are not implemented. No performance result is credited to synthetic server R300/R350 or used to raise product limits.
