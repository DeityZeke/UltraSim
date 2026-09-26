# F1.9B — measured scalable Godot world presentation backend

Date: 2026-09-25. **F1.9B CLOSED** after an uninterrupted canonical Release pass on the corrected
implementation `be36ff502081b560bad9e0b2257bbc83a006540b`.
Production selection: **B — retain NodeMultiMesh / PerRow**.

## Lineage and scope

- Starting clean main: `e2ea4dd96951379b638a83128576625bf19825b0`.
- Backend/measurement implementation: `7b73f0d8f4896f0ba0db6bbdc8e765e1069d0f02`.
- Corrected product/debug integration: `be36ff502081b560bad9e0b2257bbc83a006540b`.
- Native measurements were taken on 7b73f0d8. The correction changes only the Local debug
  snapshot call; adapter and native-fixture sources are byte-identical between those commits.
- Final evidence commit: the commit containing this report (see git history). This avoids
  pretending that a file can embed its own commit SHA.
- No Game, authority, Core semantics, content policy, Network, or World formats changed.
  Settings v5; Game manifest v33; transport revision 2; WorldPack/v1;
  WorldStructuralManifest/v1; worldview/v2 remain unchanged. W8 and F1.10 are unstarted.

The original renderer already batched frozen statics, Multi fragments, and ordinary public
props. It did not allocate a Node per such semantic record. The permanent baseline/control
retains that native graph and per-row setter path through the new explicit group owner;
it is not an isolated rating of the old commit's managed wrapper overhead.

## Native ownership and selected policy

`GodotWorldInstanceGroup` is a concrete chunk-local realization seam below existing grouping.
A group borrows its resolved mesh/material, owns one MultiMesh, and owns either a
MultiMeshInstance3D Node or a direct RenderingServer visual RID. Direct candidates attach to
the actual F1.9A GameplayOutput World3D scenario, carry the chunk's global transform and
visibility, and rebind on scenario replacement. Disposal releases the visual RID/Node before
the MultiMesh; borrowed source resources outlive the realization.

Grouping remains WorldStaticRenderKey for statics/Multi fragments and prefab/presentation
for public props; it remains chunk-local and Pack-blind. Native slots/RIDs are not semantic
identities. Public refresh still replaces the complete public layer. This checkpoint did not
add a dynamic slot map, per-object callbacks, a new atlas, direct terrain, or actor tiers.
Terrain/additional surfaces stay mesh Nodes; detailed actors/equipment/nameplates retain
their existing earned Node-backed path.

Direct saved 54 Nodes but no group visual RIDs/MultiMeshes. Whole realization/refresh showed
no consistent material gain; direct requires explicit scenario/transform/visibility ownership
and a live-group synchronization pass. Its rebase and empty-frame service cost were higher.
The small allocation reduction does not justify changing the production default here.

Per-row remains the finite production upload policy for cold groups and current whole-layer
public refresh. **No bulk threshold is selected:** no crossover was measured through 1,024
rows on this host/API. Bulk and direct are bounded constructor-level comparison controls,
not user settings or server-profile choices. This is not a universal claim about other GPUs,
Godot backends, larger groups, or a future incremental renderer.

## Fixture, counts, and memory interpretation

One maintained fixture flows through ClientWorldSemanticCache, GodotSlidingRenderWindow /
ClientPresentationWindow, GodotWorldRenderer, and GameplayOutput. Nine adjacent chunks,
four structural families, two simple public families, exact shared terrain edges, rotated
resolved rows, scaled fallback rows, and ±1,000 m object elevations exercise the same data
through all four combinations. The nine Multi roots contribute fragments, not extra rendered
instances. This is synthetic renderer work, not product CCU or a realistic GPU-density rating.

| Realized records | Frozen statics | Multi fragments | Public props |
|---:|---:|---:|---:|
| 100 | 50 | 25 | 25 |
| 1,000 | 500 | 250 | 250 |
| 5,000 | 2,500 | 1,250 | 1,250 |
| 10,000 | 5,000 | 2,500 | 2,500 |

At every scale: 54 groups/MultiMeshes and 9 terrain meshes. Node control: 91 actual renderer
SceneTree Nodes; direct: 37 (59.3% fewer). Both have 54 group visual instance RIDs plus nine
terrain visual instances, three distinct borrowed group meshes, and one group material override.
The legacy resource counter is 63 = 54 MultiMeshes + nine terrain meshes; it is not a count of
every native mesh/material/visual instance or total GPU memory.

Semantic-cache reservation and renderer upload-scratch payload bytes are recorded per row.
Node/per-row reserves no upload scratch; bulk reserves a finite owner-local buffer derived
from semantic per-chunk limits, not one buffer per object. GC allocated bytes cover the initial
ProcessFrame cohort, not the preceding cache/renderer construction or complete process heap.
Other renderer object/array headers and native driver allocations are not presented as measured
total resident memory. Logical 3D row payload is 48 bytes/instance, not a measured GPU allocation.

## Timing boundaries and comparison

All CPU columns below are **owner-thread elapsed intervals**, including synchronous native
calls/stalls, not measured thread CPU. Three repetitions rotate comparison order; raw results,
including initial JIT/cold tails, are retained. Tables use the median of three, not a p95 rating.

- Initial: enclosing nine ProcessFrame calls plus their bounded snapshot reads.
- Build: summed same-run existing mesh-build intervals, including geometry/group creation/upload.
- Commit: summed existing root/tree/scenario commit intervals; not all native construction.
- Grouping and group preparation are non-overlapping child intervals **inside Build**.
- Initial minus Build minus Commit is a signed, same-run outside-child residual. It includes
  pre-build semantic-view/group validation and fixture/scheduling/snapshot bookkeeping; it is
  not attributed to RIDs, GPU, or a guessed security cost.
- Refresh: three complete public-layer refresh calls, separately retained for 1/few/all dirty
  rows. Rebuilt records are recorded; this is not incremental row-update timing.
- Steady: 512 warmed ProcessFrame(0,0) calls, excluding native frame drawing.
- Rebase: 256 origin changes, alternating high/low anchors with local X/Z movement.
- Retirement: one nine-chunk retirement cohort; frame-boundary assertions follow outside timing.
- GPU/viewport rendering: separate four-boundary warmup plus eight diagnostic frames per run,
  with measurements disabled during the CPU service controls. Never added to CPU percentiles.

| Records | Node initial ms | Direct initial ms | Node build ms | Direct build ms |
|---:|---:|---:|---:|---:|
| 100 | 27.757 | 27.048 | 27.439 | 25.961 |
| 1000 | 27.893 | 27.222 | 26.756 | 26.086 |
| 5000 | 50.033 | 53.050 | 29.321 | 30.610 |
| 10000 | 116.681 | 118.186 | 34.540 | 34.899 |

At 10,000 records:

| Backend/upload | Group prep ms | Refresh total ms | Retire µs | Rebase µs/call | Empty frame µs |
|---|---:|---:|---:|---:|---:|
| NodeMultiMesh/PerRow | 7.044 | 28.765 | 401.9 | 0.960 | 0.044 |
| NodeMultiMesh/Bulk | 8.091 | 28.353 | 408.5 | 1.295 | 0.044 |
| DirectRid/PerRow | 7.193 | 28.688 | 312.5 | 9.386 | 3.307 |
| DirectRid/Bulk | 8.211 | 28.516 | 296.3 | 9.293 | 3.216 |

Initial allocation at 10,000: Node/per-row 5,014,264 bytes; direct/per-row 5,003,464
(10,800 bytes less, about 0.22%). All 48 rows measured **zero warmed managed allocation**
in the renderer empty-frame loop; this does not claim zero allocation for the whole client.

The roughly 117 ms initial cohort is not nine native commits taking 117 ms: approximately
35 ms belongs to measured Build and about 0.06 ms to Commit. The substantial outside-child
remainder is preserved in JSON and remains a source/validation investigation area. The high
cold-enter/public-refresh cost is not fixed by removing group Nodes and is not claimed to be
a theoretical minimum. Frame budgeting bounds work count, not each chunk's elapsed cost.

## Bulk versus targeted submission

Actual repository transform encoder + Godot setter/buffer APIs, 1,000 operations per sample,
three repetitions, raw Stopwatch ticks converted using Frequency (1 GHz here). Sparse controls
retain unchanged rows and verify all native round trips; zero warmed managed allocations.

| Full rows | Per-row µs | Encode + bulk µs |
|---:|---:|---:|
| 1 | 0.042 | 0.502 |
| 4 | 0.139 | 0.721 |
| 16 | 0.541 | 1.848 |
| 64 | 2.140 | 6.428 |
| 256 | 9.125 | 25.034 |
| 1024 | 34.445 | 100.361 |

For a 1,024-row retained group: one-row targeted 0.034 µs versus bulk 5.917 µs;
four-row targeted 0.134 µs versus bulk 6.179 µs. Full buffer bytes are transferred by bulk;
the sparse encoder does not excuse that transfer. These are CPU submission results, not
proof that deferred GPU upload work has the same cost in all modes.

## Native assurance and presentation regressions

The real OpenGL path passed all four backends at all scales, across actual render boundaries:

- all native rows compared against the Node control; canonical structural/public transforms
  independently checked, including rotation, scale, high/low Y, and cross-chunk placement;
- combined transformed mesh AABBs enclose all rows; actual camera-frustum movement culls/reveals
  the diagnostic group; refreshed positions have updated bounds;
- disclosed prop rays nominate exact IDs/lifetimes, not native slots; empty replacement removes
  the group and fresh generation-2 IDs survive re-entry;
- source-provider geometry replacement retires the old realization without changing semantic
  hash; selected-path User/texture/material generation tests remain the canonical F1.5/F1.6 gates;
- Available/ModelMissing/TextureMissing/BothMissing groups retain their current fallback policy;
- 1920×1080/100%, 1280×720 output, 1920×1080/50% internal retain native ownership;
- actual World3D replacement rebinds direct groups; ancestor movement and ±1,250 m rebasing
  update group transforms without rewriting local rows;
- enter/retire/re-enter, queued cancellation, active/pending disposal, repeated Dispose retire
  all groups; final resource/instance/work counters are zero (root=1 until renderer Dispose).

The dummy headless renderer returns identity transform readbacks even after per-row writes.
The canonical headless F1.9B gate therefore explicitly reports nativeGeometry=False, checks the
100-record lifecycle/display/scenario cohort, and does not pretend to certify transform/culling
or GPU performance. Those checks were actually run with the real renderer.

GPU diagnostics were available: 24 nonzero samples per mode/scale; 53 observed **global**
draw calls per row (includes the output/UI, not a theoretical group count). At 10,000,
viewport GPU mean was 0.235 ms Node/per-row and 0.294 ms direct/per-row; bulk means
0.297 ms Node and 0.277 ms direct. These short diagnostics with uncontrolled GPU power/clocks
do not establish a sustained FPS or backend GPU winner.

## Verification provenance and limitations

Final canonical command: `./verify.sh --configuration=Release`, exit 0,
`PASS repository verification` on `be36ff502081b560bad9e0b2257bbc83a006540b`.
No test weakening or unrelated motion/Network repair was used to obtain that pass.
Local run-log SHA-256: `d2ca8643cb1c482fd32469a7b1c1084d8abad97b5a8d15a6d3e83c7698e393f7`.
Native measurement log SHA-256: `a85fd951934fc11ea545ff1225d80b2aa94568dca204e439cdf9a1317e0dbeb7`.
The logs are host-local, not promised permanent artifacts; the permanent raw measurements and
source hashes are the independently recomputable evidence. All 416 aggregated mean/median
values were recomputed from the raw samples before acceptance, without mismatch.

| Gate | Actual result |
|---|---|
| Client Core Release | 35 tests passed |
| Complete product Release build | Passed, zero warnings/errors |
| Real OpenGL focused comparison | Passed: 48 integrated rows, 90 submission samples, 384 GPU frames |
| Focused and canonical headless backend gate | Passed: 12 lifecycle rows; native geometry explicitly unavailable |
| Architecture | Passed on measured implementation; final documentation/link check also passed |
| Complete canonical Release | Passed uninterrupted on corrected implementation |
| Native S08/T08/T010 and derived-resource/User replacement | Passed existing gates, including source/semantic invariance |
| Local, Remote, Complete exports/composition | Passed actual exports and native journeys |
| Contentless Online | Passed cold/warm acquisition and profile change, same exported binary |
| W7 seam and generalized lifecycle | Passed exported 127→128→127; managed seam-start/three-plus-Pack/four-Pack-corner controls |
| Mixed exported client/bot impairment | PF1/PF2 passed; proofs/accounting/clean retirement retained |
| F0.7 / F1.0 lifecycle | Passed Local→Local→Remote A→Remote B→Remote A→Local; six clean boundaries |

Final lifecycle evidence: Local semantic/rendered chunks 49/25; native static/public batches 64/7;
Remote cold bytes 1,958 and warm reuse 1/3; actor resource count/bytes 1/2,213,268. Remote sessions
were fresh and exact profile reuse False/False/True. W7 reported zero missing resident/prepared
chunks and zero stale prepared-window deliveries. These are existing product workloads, separate
from the synthetic renderer count matrix. Their semantic assertions passed unchanged.

Preserved non-closure runs:

1. Full Release on 7b73f0d8 stopped at a new Local debug-page compile error: wrong snapshot
   method name. be36ff50 repairs that hookup; Complete Release then built with zero warnings/errors.
2. Full Release on be36ff50 stopped at the unchanged F1.8A loss/jitter test,
   F03bRemotePlayableTests.MeasureMotion line 394 (InterpolatedFrames > 0).
   The same Network application suite then passed on that unchanged commit, including all four
   real-UDP motion controls and 15 W7 structural tests. Cause of the intermittent failure is
   not established; no assertion, motion policy, or Network source was changed.
3. A development measurement's relative evidence path failed after the native controls because
   Godot changes working directory to the project. The saved final campaign used an absolute path.

Earlier F1.8A-R1 failure dispositions and quality tails remain historical and unchanged.
Remote still uses its accepted neutral fallback world resolver (assets:null); this checkpoint
does not claim to close its separate catalog-backed world-material integration gap.

## Changed files and next checkpoint

Implementation: `Client/Godot/Adapter/GodotWorldInstanceGroup.cs`,
`GodotWorldRenderer.cs`, `Client/Godot/Shell/ClientRoot.cs`,
`Client/Godot/Local/LocalPlayClientRoot.cs`, `Client/Godot/ClientHost.cs`;
native acceptance: `_Tests/UltraSim.S0.Godot.Tests/F19bWorldBackendAcceptance.cs` and `.tscn`;
verification: `_Tools/RepositoryVerification/Program.cs`.
Documentation: `Docs/Plans/CLIENT_COMPLETION_PLAN.md`, `CLIENT.md`,
`CLIENT_PRESENTATION_ARCHITECTURE.md`, `Client/Godot/README.md`, `Client/Core/README.md`;
evidence: this report and its sibling summary/raw JSON.

F1.10 is NEXT but unstarted. W8 is unstarted. PF6 continues to wait for F1.12; this checkpoint
does not certify human motion feel, actor tiers, dense vegetation, or a finished incremental
renderer. The cold preparation residual and Remote resource-consumer parity remain bounded,
explicit follow-up questions rather than inferred optimization gains.

## Reproduction and environment

Host: CachyOS 7.2.6-1-cachyos x86_64; Ryzen 7 7800X3D (8 cores/16 threads); 30 GiB RAM;
.NET SDK 8.0.131/runtime 8.0.31; Godot 4.7.1 Mono; OpenGL Compatibility, NVIDIA RTX 4070 Ti
SUPER, driver 615.71.09. Desktop/compositor/background applications remain present; no CPU
affinity, fixed GPU power state, or exclusive host control. Benchmark jobs were serialized.
One native fixture process owns the output viewport and renderer; this is not server-only CPU.

Godot's editor executable loads the Debug-config assembly. Native comparisons explicitly built
it with Optimize=true; raw evidence confirms jitOptimizerDisabled=false. This is an optimized
editor-host acceptance, not an exported Release player rating. The canonical Release campaign
also explicitly rebuilds current native test sources instead of loading a stale Debug DLL;
physical product exports remain separately verified.

```bash
dotnet build _Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj -c Debug -p:Optimize=true
ULTRASIM_F19B_TESTED_HEAD=<exact-source-head> "<Godot 4.7.1 Mono>" \
  --path _Tests/UltraSim.S0.Godot.Tests --scene res://F19bWorldBackendAcceptance.tscn \
  -- --evidence="<absolute-output-path>"
"<Godot 4.7.1 Mono>" --headless --path _Tests/UltraSim.S0.Godot.Tests \
  --scene res://F19bWorldBackendAcceptance.tscn
./verify.sh --architecture-only
./verify.sh --configuration=Release
```

Machine-readable summary and all individual comparison/submission/GPU samples are in the sibling
`.json` and `.raw.json`. F1.10 remains the next planned checkpoint; W8 is not started.

## Closure checklist (requested final report)

1. Starting HEAD: `e2ea4dd96951379b638a83128576625bf19825b0`, clean main.
2. Tested implementation HEAD: `be36ff502081b560bad9e0b2257bbc83a006540b`.
3. Final evidence HEAD: commit containing this report; exact SHA supplied in the handoff.
4. Commits: 7b73f0d8 backend/acceptance; be36ff50 debug integration correction; separate evidence/docs closure.
5. Files changed: eight implementation/test/verifier files, five current docs, three new evidence artifacts (listed above).
6. Baseline: existing chunk-local Node-backed MultiMeshes, not node-per-semantic-object.
7. Candidate: direct visual RID plus MultiMesh, borrowed mesh/material, explicit scenario/transform/bounds/retirement.
8. Seam: concrete `GodotWorldInstanceGroup` below retained grouping, not a generalized renderer framework.
9. Production selection: NodeMultiMesh/PerRow, outcome B.
10. Rationale: no consistent complete CPU benefit from direct/bulk; higher direct idle/rebase burden.
11. Stress counts: 100/1,000/5,000/10,000 frozen/Multi/public records, split 1/2 + 1/4 + 1/4.
12. Baseline Nodes: 91 in the nine-chunk/54-group fixture.
13. Selected Nodes: 91; direct comparison: 37.
14. Baseline resources: legacy count 63; not total GPU memory/resources.
15. Selected resources: same 63, 54 group visual RIDs plus nine terrain visual instances.
16. Instances/groups: realized instance count equals scale point; 54 groups at all scales.
17. Initial CPU: 10,000 Node/direct per-row medians 116.681/118.186 ms, build 34.540/34.899 ms.
18. Update CPU: three whole-layer public refreshes, 10,000 Node/direct per-row 28.765/28.688 ms total.
19. Retirement CPU: nine-chunk Node/direct per-row 401.9/312.5 µs medians.
20. Rebase CPU: Node/direct per-row 0.960/9.386 µs per call medians.
21. Allocation: initial 5,014,264/5,003,464 bytes; warmed renderer service zero in all modes.
22. Submission: targeted/per-row beat encode+bulk at tested full and sparse sizes; exact table above.
23. Bulk policy: no threshold selected through 1,024 rows; PerRow retained.
24. AABB/culling: real-native enclosing bounds and actual frustum movement passed.
25. Transform parity: all native rows/control plus independent structural/public expected transforms passed.
26. Picking: disclosed generation-fenced IDs retained across native realization, refresh and re-entry.
27. User replacement: existing selected-path F1.5 native hot replacement and semantic invariance passed.
28. Texture/material: selected-path F1.6 gates passed; Remote resolver gap remains explicit.
29. Fallback: all four availability states passed, unchanged selection policy.
30. Display: output size/internal scale changes retain group identity; scenario replacement separately passed.
31. Local: native playable/export and existing baseline guardrails passed.
32. Remote: cold/warm Contentless Online, profile changes and mixed sessions passed.
33. W7 seam: actual exported 127→128→127 passed, no missing/stale prepared-window deliveries.
34. W7-R1: seam-start, third-Pack retirement obligation, four-Pack corner and retirement fences passed.
35. Detailed actors: unchanged, including equipment/nameplates/action paths.
36. Versions: settings v5 / Game manifest v33 / transport 2 / WorldPack 1 / structural manifest 1 / worldview 2 unchanged.
37. GPU: 384 nonzero diagnostic samples; uncontrolled short runs, no sustained FPS/winner claim.
38. Architecture: passed implementation and final documentation/link gate.
39. Canonical Release: uninterrupted pass on corrected implementation; earlier failures preserved above.
40. Evidence: this MD, sibling summary JSON and raw JSON; exact source hashes and per-run samples retained.
41. F1.10: next, unstarted.
42. W8: unstarted.
43. Worktree: clean after the separately committed evidence/docs closure; exact final status checked in handoff.
