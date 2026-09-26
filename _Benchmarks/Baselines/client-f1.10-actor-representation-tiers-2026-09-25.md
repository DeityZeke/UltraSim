# F1.10 — actor representation tiers and temporal presentation seam

Date: 2026-09-25. **F1.10 CLOSED** after an uninterrupted canonical Release pass on
`cbf9175c63a7f7ab843363782a2c80c018b88cbd`.
Selected outcome: **C — conservative hybrid**, not a universal moving-update CPU win.

## Lineage, scope, and exact controls

Starting clean main: `ef3376226a59a5b22e7e8ce22991e6ea230d0264`.
Tested implementation and measurements: `cbf9175c63a7f7ab843363782a2c80c018b88cbd`.
The raw export's `sourceHead=cbf9175c` identifies that exact commit.
Final evidence commit: the commit containing this report; see git history.
There were no intervening commits or unrelated changes at task start.

Only Client/Core, Godot realization, tests, and verifier integration changed.
Settings v5; Game manifest v33; transport revision 2; WorldPack/v1;
WorldStructuralManifest/v1; worldview/v2; product admission cap 64 are unchanged.
Game truth, source/AOI/session fencing, semantic proof, content precedence, cadence,
replication, and F1.9B NodeMultiMesh/PerRow selection remain unchanged.
W8 and F1.11 are unstarted. PF6 still waits for F1.12.

Hardware/runtime: Ryzen 7 7800X3D (8 cores/16 threads), 30 GiB host RAM,
RTX 4070 Ti SUPER (16,376 MiB), NVIDIA 615.71.09, CachyOS Linux 7.2.6-1,
.NET SDK 8.0.131 / target runtime 8.0.31, Godot 4.7.1 Mono a13da4feb.
Native geometry/measurements use real OpenGL Compatibility 3.3, not dummy headless rendering.
One Godot process; desktop host, no fixed affinity/clocks or isolated-host guarantee.
No concurrent benchmark campaign was launched.

The actor-only fixture uses 16/64/128/256 exact records: alternating X/Y/animal primitives,
one controlled actor, two near actors total, the rest at 32 m + 0.1 m × actor index,
with an eight-row 0.8 m Z spacing. Both modes use identical definitions, semantic records,
positions, motion inputs, timing, and display inputs. All-Detailed is a constructor control
through the **same current owner**, not a timing of an isolated older commit.
The comparison contains primitive detailed hierarchies, not 256 animated GLBs or remote clients.
The current 64-buffer remote interpolation capacity is not increased by this native 256-actor fixture.

## Policy and ownership

`ClientActorRepresentationPolicy.ConceptDemo`: enabled, soft Detailed budget **64**,
promotion distance **20,000 mm**, demotion distance **28,000 mm**, measured in world-space 3D.
Distance then complete generational WorldPublicEntityId determines discretionary priority;
the dead band retains the current tier. Mandatory features may exceed the soft budget within
the existing **256 semantic actor** hard capacity; none are dropped to force a quality budget.

Controlled actors are always Detailed, including missing-content primitive fallback.
Equipped main hands, active Primary/Interact/landing cues, and active overhead speech pin Detailed.
Nameplate reveal alone never promotes a body. Cheap bodies are deliberately simplified primitives,
not a claim of skeletal or equipment batching. Policy-cheap actors are counted separately from
Detailed missing-content fallback actors. Two near actors remain Detailed in all measured hybrid rows.

`GodotPrimitiveActorBatches` owns up to three shape/color groups (HumanX/HumanY/Animal).
Each owns a MultiMeshInstance3D, MultiMesh, mesh and material. Per-row uploads preserve F1.9B policy.
Bounded owner-slot→family/row and dense row→full entity ID/owner maps support swap-back removal.
All group native capacities are finite (three × owner capacity); only live rows are visible.
Moving/rebased AABBs merge the actual scaled/transformed primitive geometry.

Actor slots retain semantic record, CPU-resolved pose, action sequence, cue expiry and motion state
independently of native representation. A Detailed promotion prepares a hidden candidate with the
**current** resolver, then commits mapping/visibility and retires the old row in one owner-thread call.
Failure frees the candidate and leaves the cheap body available. There is no queued async transition:
leave/dispose cannot race a later stale commit. Demotion retires hierarchy while preserving identity,
pose and sequence clocks. Cheap labels are separate bounded overlay Nodes only while needed.
Native rows/RIDs never become semantic or picking identities.

`TryGetActorRoot` is explicitly Detailed-only. Pose/tier/record queries work for both tiers.
Local semantic visibility counting no longer requires a root; Remote animal pointer nomination
already uses the CPU presented pose, then the existing disclosed ID/range and Game acceptance.
Remaining root consumers are controlled animation/equipment or explicitly Detailed native tests.

## Temporal seam and functional assurance

`ClientActorTemporalPresentation` exposes the **actual selected receipt-time bracket**:
server generation, full ID, previous/current public versions and poses, receipt timestamps,
target receipt time, and the CPU-resolved pose/provenance. Held and discontinuity samples have
equal endpoints. It is not a pair of fabricated current poses or invented server timestamps.

Remote uses `TrySampleTemporal` and still submits its CPU-resolved pose. The old `TrySample`
API delegates once without doubling counters. Delay, gaps, holds, vertical teleport and generation
policy are unchanged. Controlled prediction/reconciliation is untouched. No GPU interpolation,
readback-based semantics, extrapolation, impostors, or shader animation was implemented.

Focused managed/native controls prove deterministic tie-breaking, capacity, hysteresis,
mandatory expansion, zero selector allocation, actual bracket selection, legacy sampling parity,
and teleport/stale-generation reset. Native assurance includes:

- real transform readback and frustum/AABB inclusion/exclusion;
- one committed body at frame boundaries, pose retention across promotion/demotion;
- all actor names revealed without crowd-body promotion; release removes cheap labels;
- overhead survives threshold crossing and retires; equipment forces Detailed;
- accepted Primary/Interact/landing cues survive transitions and are not replayed on promotion;
- same presented animal pick nomination across tiers, without per-actor physics Nodes;
- injected preparation exception leaves no hidden candidate and preserves the old body;
- family/scale replacement, floating-origin rebasing, interest exit/reentry;
- removal and slot-generation replacement, swap-back mapping, control transfer;
- repeated mixed-tier disposal leaves the host free of actor bodies, groups, labels and roots.

T010 additionally exercises **real** CAS/catalog/presentation-generation ownership:
a distant cheap actor spans a User hot replacement, promotes using the new User FileHash,
demotes, then promotes with current Realm art after disabling User. Existing animated X/Y
65-bone Idle/Walk/Jump, scale-free hand socket, equipment and primitive→content state retention
checks stay green. Resolver stubs in the focused fault test do not substitute for this content gate.

## Native matched measurements

Three repetitions per count/mode, alternating pair order. Each row has 32 moving warmups,
128 idle samples and 256 moving samples (6,144 moving samples total). Raw timestamp differences
use Stopwatch.Frequency = 1 GHz; percentile = nearest-rank. Tables show the **median of three
per-run statistics**, not a pooled p95. Every run/tail remains in the JSON/raw data, including
the first 16-actor Detailed moving mean of 14.763 µs (later repetitions 7.426/7.555 µs).

CPU columns measure elapsed wall time on the Godot owner thread, not hardware thread CPU.
Moving includes setting each non-controlled presented pose, bounded policy selection, Detailed
service and cheap per-row/bounds service. Idle includes that owner's unchanged-frame service.
No clocks wrap individual field writes. GPU/render-thread timing is outside these intervals.

| Actors | Mode | Body Nodes | All presentation Nodes | Initial ms | Moving mean µs | Moving p95 µs | Idle mean µs |
|---:|---|---:|---:|---:|---:|---:|---:|
| 16 | Detailed | 160 | 193 | 3.941 | 7.6 | 7.8 | 7.2 |
| 16 | Hybrid | 23 | 28 | 1.071 | 30.4 | 34.8 | 28.4 |
| 64 | Detailed | 640 | 769 | 15.776 | 32.1 | 34.5 | 30.2 |
| 64 | Hybrid | 23 | 28 | 1.118 | 101.9 | 108.8 | 96.5 |
| 128 | Detailed | 1280 | 1537 | 30.418 | 74.9 | 78.9 | 67.4 |
| 128 | Hybrid | 23 | 28 | 1.393 | 205.5 | 212.3 | 194.7 |
| 256 | Detailed | 2560 | 3073 | 58.081 | 193.0 | 201.5 | 170.6 |
| 256 | Hybrid | 23 | 28 | 1.884 | 400.8 | 433.1 | 369.5 |

| Actors | Mode | Round-trip ms | Reveal ms | Retirement ms | Initial allocated B | GPU mean ms | Viewport render CPU mean ms | Global draw calls (3 runs) |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---|
| 16 | Detailed | 0.015 | 0.006 | 0.785 | 152888 | 0.074 | 0.127 | 18/18/18 |
| 16 | Hybrid | 0.383 | 0.080 | 0.225 | 25144 | 0.085 | 0.124 | 7/7/7 |
| 64 | Detailed | 0.061 | 0.023 | 2.870 | 988992 | 0.090 | 0.188 | 66/66/66 |
| 64 | Hybrid | 0.574 | 0.378 | 0.250 | 33784 | 0.088 | 0.116 | 7/7/7 |
| 128 | Detailed | 0.139 | 0.055 | 5.866 | 1218616 | 0.221 | 0.255 | 130/130/130 |
| 128 | Hybrid | 0.798 | 0.798 | 0.291 | 45304 | 0.206 | 0.110 | 7/7/7 |
| 256 | Detailed | 0.332 | 0.140 | 11.656 | 2436920 | 0.465 | 0.394 | 258/258/258 |
| 256 | Hybrid | 1.163 | 1.690 | 0.321 | 68344 | 0.441 | 0.112 | 7/7/7 |

Round-trip is one subject's promotion/demotion plus the enclosing two cohort updates;
the Detailed control only changes that subject's pose. Reveal includes bounded cold label creation
on the cheap path, not a steady-state hot-frame operation. Retirement is synchronous owner work;
a later native boundary separately proves the host is clean.

**Both paths allocate zero warmed managed bytes** in the measured idle and moving loops.
Initial allocation excludes constructor reservations, input construction, diagnostics/export and
engine/render threads; it is not a whole-process allocation claim.

GPU diagnostics are eight stationary frames after four warmups per run, using actual measured
Gameplay Output time at 1920×1080 output / 50% internal scale (960×540 internal).
Viewport render CPU is engine-reported render service, not actor-update CPU or total process CPU.
Global draw counts are isolated-fixture frame observations, **not actor-exclusive counts**.
Do not add these distributions to owner-thread p95 or derive sustained FPS from these short samples.
Display assurance separately traverses Simple 1920×1080/100%, Advanced 1280×720,
and 1920×1080/50%; actor identity, counts and tiers remain unchanged.

### Resources, labels and native operations

Detailed body counts include the existing root/visual/equipment/cue scaffold (10 Nodes/primitive);
total adds its two labels and the common presenter root. Hybrid has 2 Detailed actors plus
3 batch Nodes: **23 body Nodes / 28 total Nodes**, independent of the far-cohort population.
Full reveal totals are 42/90/154/282 Nodes (all 16/64/128/256 names visible).
An overlay Node is not a body Node. The cheap tier has no per-actor `_Process`.

Cheap groups own **9 resources** (3 MultiMeshes, 3 meshes, 3 materials) and 3 visual Nodes.
Detailed resource ownership remains in existing visual/resolver lifetimes; no comprehensive
Detailed GPU-byte reduction is claimed. Initial native counters record 14/62/126/254 transform
uploads and 3 bounds writes for hybrid, zero cheap uploads for Detailed. These snapshots are taken
**before warmup**, not a fabricated census of all later calls. Transform setters skip unchanged rows;
bounds still traverse live rows each frame. Full moving-stage timing includes that work.

Managed cheap-array payload reservations are 3,008/12,032/24,064/48,128 B in the hybrid fixture;
selector/cohort payload reservations are 1,040/4,160/8,320/16,640 B in **both** comparison modes.
This excludes array/object headers, the base ActorSlot slab and native allocations, and does not
assert lower total RSS. Three native groups reserve three × actor-capacity instance slots.
Persistent CAS/resources can outlive a session; live presenter ownership is what must retire.

## Decision and limitations

**C — conservative hybrid.** At 256 actors the far fixture cuts body Nodes 2,560→23,
initial realization 58.081→1.884 ms and synchronous retirement 11.656→0.321 ms.
It also cuts draw observations 258→7; short GPU time is not a large/general GPU-speedup claim.

Moving owner cost **increases** 193.017→400.758 µs at 256; near 64 it is 32.106→101.915 µs.
Explicit per-row interop and live bounds service are real costs. Existing bounded linear actor
lookup and policy ordering also remain; no sampled attribution establishes an unavoidable minimum.
Near actors (up to the soft 64 budget) and feature-bearing actors remain Detailed.
The measured node/construction/lifecycle value supports distant batching, not aggressive player
demotion, a universal skeletal renderer, higher product CCU, or a server R300/R350 improvement.

F1.8A correction/feel quality risks remain as previously recorded: rapid reversal/Stop tails
historically about 1.4–1.67 m and sparse-update holds. This work does not reset their historical
maximum or claim human validation. Remote catalog world Texture/Material integration remains a
separate known gap; this actor-tier change does not close it. GPU interpolation/actor animation,
impostors, regional actor partitioning, and tuning live-bounds cost remain future experiments.
F1.11 and W8 have not begun.

## Verification and reproducibility

Verification status: **PASS**, one uninterrupted canonical Release run on the implementation commit,
exit 0, final marker `PASS repository verification`. No failed full run was hidden or rerated.
Focused managed tests: 36/36; optimized Debug native build: zero warnings/errors.
Focused headless lifecycle and full real-renderer 24-row matrix: PASS.
T010 real content transitions and existing native actor parity: PASS.
Architecture-only gate: PASS (4 checks).

Canonical managed results include 36 Client Core, 181 Game Replication, 115 World, 38 Content,
9 BuiltInTestWorlds, 23 Local Client and 39 integrated acceptance gates. The existing complete
Network/Application, UI, Inventory, Chat, hosting and lifecycle suites passed. Expected invalid
Network-flavor builds failed as designed; those are positive rejection gates, not verifier failures.
Native S08/T08/T010, F1.9A/F1.9B and the new F1.10 headless gate passed. Actual SinglePlayer,
Contentless Online and Complete exports passed, including installed/absent/restored Official art,
Remote cold/warm acquisition, different profiles through the same Online executable, W7 exported
gate/seam/return (`stalePreparedWindowDeliveries=0`), PF1/PF2 exported-client/bot impairment and
accounting, and F0.8 parity. F0.7/F1.0 retained all six clean boundaries and fresh remote lifetimes:
Local→menu→Local→menu→Remote A→menu→Remote B→menu→Remote A reconnect→menu→Local→menu.
The F1.0 coordinated gate recorded Local 49 resident / 25 rendered chunks, cold Remote 1,958 bytes,
warm reuse 1/3 packages/blobs, profile reuse False/False/True and clean retirement.

Full local verifier log `/tmp/f110-canonical-release.log` SHA-256:
`7d7f4e1da7b05b8889d30c684512d41c7b162784e7fe7e6931834bb0c5dd3b22`.
Its permanent selected gate markers are retained in the companion JSON. Native accepted-run log
SHA-256: `13ae5ac11dc1f4141f11e932fa353626c5a2ba42127df2f7afe9810fc6160a4b`.

Development provenance retained: the first new native-fixture build failed on 13 incorrect
display/native API references; those test-code references were repaired, then the temporary
`Position` name-hiding warning was removed. Neither was a runtime semantic defect or a weakened gate.
Two development full-native matrices preceded the accepted commit-bound raw run; they were controls,
not discarded rated tails. Earlier F1.8A/F1.9B verifier failures and their dispositions remain in
their immutable reports; they are not rewritten as passes by this checkpoint.

Reproduce (from repository root, actual executable on this host):

~~~bash
dotnet run --project _Tests/UltraSim.Client.Tests -c Release
dotnet build _Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj \
  -c Debug -p:Optimize=true -p:DebugType=portable -m:1
'/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64' \
  --headless --path _Tests/UltraSim.S0.Godot.Tests res://F110ActorTierAcceptance.tscn
ULTRASIM_F110_TESTED_HEAD=cbf9175c \
'/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64' \
  --path _Tests/UltraSim.S0.Godot.Tests --rendering-method gl_compatibility \
  res://F110ActorTierAcceptance.tscn -- --evidence=/absolute/new-output.json
'/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64' \
  --headless --path _Tests/UltraSim.S0.Godot.Tests res://T010PlayableWorldAcceptance.tscn
./verify.sh --architecture-only
./verify.sh --configuration=Release
~~~

Export new repetitions to a new path; do not overwrite the accepted raw baseline.
The canonical verifier includes the new bounded headless scene and retains all existing native,
edition/export, structural, impairment and mixed-lifecycle gates.

## Ordered closure handoff

1. Starting HEAD: `ef3376226a59a5b22e7e8ce22991e6ea230d0264` (clean).
2. Tested implementation HEAD: `cbf9175c63a7f7ab843363782a2c80c018b88cbd`.
3. Final evidence HEAD: commit containing this report; exact SHA in the final handoff/git history.
4. Commits: actor implementation/tests/verifier, then documentation/immutable evidence only.
5. Files: 14 implementation/test files listed by `git show --stat cbf9175c`; five requested
   current docs/READMEs and this MD/JSON/raw triplet. No unrelated edits included.
6. Tiers: Detailed and BatchedPrimitive are real production implementations.
7. Policy: enabled conservative hybrid; world-space distance, deterministic full-ID priority.
8. Capacity: soft 64 Detailed, mandatory expansion within hard 256 semantic presentation actors.
9. Thresholds: promote at/below 20 m; Detailed demotes beyond 28 m or discretionary budget loss.
10. Hysteresis: cheap retains its tier between 20/28 m; nearest/full-ID priority under budget pressure.
11. Controlled actor: always Detailed, including primitive fallback without art.
12. Required features: equipment, active Primary/Interact/landing and overhead speech pin Detailed.
13. Cheap bodies: compatible primitive MultiMeshInstance3D groups, PerRow transforms, no body-per-actor Node.
14. Three families, bounded dense generational row mappings and swap-back repair; exact moving bounds.
15. Temporal seam: real receipt-time/version endpoints plus CPU-resolved pose; no fabricated server clock.
16. F1.8A: existing sampling semantics/policy/counters and controlled predictor preserved.
17. Detailed X/Y: T010 65-bone Idle/Walk/Jump, hand attachment and content upgrade parity passed.
18. Reveal: all 16/64/128/256 names visible without body promotion; temporary overlays retire.
19. Overhead Chat: pins Detailed through valid expiry across distance changes, then allows demotion.
20. Equipment: forces Detailed and remains correctly realized across changes/transition.
21. Actions: Primary, Interact and landing retained; completed sequence not replayed by promotion.
22. Picking: same CPU presented animal pose/ID across tiers; no cheap physics-body requirement.
23. Transitions: exact records/IDs/poses retained, one committed native body, failed candidate cleaned.
24. Content: real User hot replace → current User promotion → disable → current Realm promotion passed.
25. Fixture: 16/64/128/256, three repetitions per mode, identical inputs, not product clients/CCU.
26. Detailed body Nodes: 160/640/1,280/2,560.
27. Hybrid body Nodes: 23 throughout (two Detailed + three batch Nodes).
28. Cheap owned resources: 3 MultiMeshes + 3 meshes + 3 materials; no total GPU-memory claim.
29. Moving CPU: at 256, median run means 193.017→400.758 µs; all runs/percentiles retained above/in JSON.
30. Transition CPU: at 256, 0.332 ms Detailed pose/cohort control vs 1.163 ms hybrid actual round-trip.
31. Allocation: zero warmed owner-thread idle/moving bytes; cold allocation/reservation scope disclosed.
32. GPU: actual viewport diagnostics available; eight stationary samples/run, separate from owner CPU.
33. Local: SinglePlayer and Complete local exports, Official installed/absent/restored controls passed.
34. Remote: Contentless Online cold/warm, profiles, shared authority and impairment controls passed.
35. W7 seam: managed seam/corner/third-Pack and actual exported journey passed; stale prepared deliveries zero.
36. Display: new three-case tier invariance and existing F1.9A native/export display gates passed.
37. F1.9B remains NodeMultiMesh/PerRow; no direct-RID or bulk policy change.
38. Versions: settings5/Game33/transport2/WorldPack1/structural-manifest1/worldview2 unchanged.
39. Architecture-only: four checks passed before canonical and again on final docs/evidence.
40. Canonical Release: one uninterrupted pass, exit 0 on the exact implementation SHA, no gate weakened.
41. Evidence: this report, matching `.json`, and `.raw.json`; commands and provenance above.
42. W8 not started.
43. F1.11 next, not started; PF6 still waits for F1.12.
44. Clean-worktree closure: final evidence commit contains only the documented files; final handoff
    records the exact final SHA and post-commit `git status --short` result.
