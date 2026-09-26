# F1.12 — Product dogfooding / PF6 readiness, partial evidence

**Verdict: NOT READY FOR PF6. F1.12 remains ACTIVE.** Four observed product mechanical defects are repaired, one assurance defect is corrected, and one uninterrupted canonical Release verifier passed. This is not closure of the complete dogfood matrix or an external-human evaluation.

## Lineage and retained evidence

Starting clean `main`: `e96af793046afc21dcd0a4f164a9eaec7e8ad2bc`.
Frozen implementation tested canonically and exported for the native control:
`ac6a81bf1f777380cdf87b62719a6204bded4a8f`.
The final evidence-only commit is identified by `git log -1 --format=%H -- _Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.json`; it does not claim to be the earlier tested implementation SHA.

Companions:

- [Machine-readable summary](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.json).
- [Verbatim structured raw snapshots and samples](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.raw.json). Original 64-bit identity literals are retained without conversion through floating-point numbers.
- [Uninterrupted canonical verifier log](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.release.log).
- [Native, failed capture and mutation-control logs](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.controls.log).

No unrelated changes existed at entry or were included. Implementation commits, in order:

| Commit | Change |
| --- | --- |
| `1cd00dd0666c35454ffbe895ebeb6c656e539df7` | Animated hand-local grip assurance |
| `efe2d0fdb531f6289574b7d98469d53132f7688f` | Durable Local save sequence |
| `1a74c162b82cad7675608548b0fddb3c740052eb` | Explicit exact Local guest restore / Chat |
| `2e210e268ef0da6806cae8415f658745feb88cdb` | Exported product journeys / bounded ownership census |
| `92a79c275db2aec2332c5bf462838334944c310e` | Local metadata generation-cohort fence |
| `68b7377033ed6e4426a8a8039c8e893a21affef4` | Native Cabin probe / focused exported mixed control |
| `3bc00dced2eb02cea6c8547b0d25ebbb583172a1` | Preserve active Remote Settings/options |
| `67faccc89ad6d84129b5bd7f8a8bc011cff2dc39` | Mixed provenance / native monitor observations |
| `ac6a81bf1f777380cdf87b62719a6204bded4a8f` | Explicit harness-radius-2 stable windows (not the shipping radius) |

## Finding ledger

Five newly observed findings are Category 1: **four product defects and one test-assurance defect**. Seven carried-forward questions/deferred groups are recorded separately. Ledger totals are 12: Category 1 = 5; Category 2 = 0 confirmed missing product behaviors; Category 3 = 3; Category 4 = 4. Coverage gaps below are not invented missing product behaviors or confirmed runtime defects.

| ID / class | Reproduction, mechanism and disposition |
| --- | --- |
| F112-01 / mechanical | Save, retire, re-enter the same save root, then Save: sequence restarted at 1 and durable high-water rejected it. `SaveSetCoordinator.TryGetNextSequence` reads complete generations, including abandoned complete publications; pending/malformed directory names do not authorize reuse. `LocalPlayableSession` initializes from it; exhaustion fails closed without wrap. Fixed `efe2d0f`. The original placement mutation fails the new second-lifetime regression. |
| F112-02 / mechanical | Load replaced the generation and correctly expired accountless Admission, but Local control continued without restoring trusted Chat admission. Spatial speech returned PrivateError, participant not entered. `Admission.TryResumeLocalGuestAfterRestore` explicitly resumes only the exact expired trusted-Local guest/persistent world/entity/original prefab in a newer generation, unbound elsewhere. No remote request invokes it; normal remote/accountless expiry remains. Fixed `1a74c162`. Rejection controls cover live/default/wrong-generation/missing bindings; post-load spatial speech passes. |
| F112-03 / mechanical | Complete exported fourth-lifetime Load reproduced `Cold public metadata was rejected: Rejected`. Authority view and a subsequent independent metadata copy could span different generations. The new expected-generation copy under the existing view lock returns no rows on mismatch; native consumer defers until its next coherent projection pass. Fixed `92a79c27`. Removing the fence fails the focused test; final Complete journey passes. |
| F112-04 / mechanical | Active Remote A, open scoped Settings across progress frames: progress attempted `EnterSession` again because the shell was not InSession. Core correctly rejected entry outside Loading. `ClientHost.ProcessRemote` now commits entry only from Loading, preserving active Settings/options. Fixed `3bc00dce`. Original exported mixed control failed; repaired and final canonical eight-lifetime controls pass. |
| F112-05 / assurance | Animated hand phase invalidated an old global-downward sword assertion. Fixed `1cd00dd0`: exact hand-local grip basis, attachment/grip centre and 0.75 × 0.055 × 0.018 m dimensions; full walking hand/root basis verified. Global directional assertion remains for primitive hand only. No new runtime sword-pose defect is claimed. |
| F112-06 / polish | Historical reversal/Stop correction tails approximately **1.4–1.67 m**, sparse-update interpolation holds. Preserve the whole range for human evaluation; no tuning or human-feel pass is inferred. |
| F112-07 / polish | Equipped grip and primitive/clay visual quality still need actual human evaluation. Mechanical attachment is not a visual-quality grade. |
| F112-08 / polish | Cabin material/lighting look remains ungraded. Final art is not required; its mechanical visual controls are still required and open. |
| F112-09 / deferred | W8/W9/W10 authored editing and durable runtime topology: unstarted. |
| F112-10 / deferred | Advanced GI, universal atlas, full Cabin remodel/universal occlusion: unstarted. |
| F112-11 / deferred | Vegetation gameplay/regrowth/final art: unstarted. Prototype remains opt-in/default Off; no lower-spec claim. |
| F112-12 / deferred | GPU actor simulation, universal skeletal batching and other engine adapters: unstarted. |

The four product defects were closure blockers before repair. No unresolved **confirmed** product defect is asserted on the tested journeys. Unexercised save-body changes or hosted load with live guests are not evidence-backed defects and are not presented as such. Harness composition corrections (releasing Interact, returning to a central radius-test anchor, and restoring physics after D0.3's deliberate pause) are not product repairs.

## Actual exported product journeys

Canonical verifier PID 2477095 ran the frozen tree without implementation edits. Every Local row uses the actual exported edition, capability button and guest form, fresh epochs, current multi-Pack World, product Settings controls, authoritative central/Far save/load, door/chest transfer, sword pickup/equip/drop/repickup, Far Gate, render-radius 1/2/3 churn, Chat focus, UI/context/tooltip probes and exact menu retirement. Each has three measured lifetimes plus a fourth fresh-session Load from the earlier save.

| Export / presentation | Stable window | Samples | Stable semantic/render chunks | Result |
| --- | --- | ---: | ---: | --- |
| SinglePlayer / Official installed | 3 × 8 s, headless | 24 | 49 / 25 | PASS |
| SinglePlayer / optional art absent | 3 × 4 s, headless | 12 | 49 / 25 | PASS; primitive actor fallback |
| SinglePlayer / primitive-only | 3 × 4 s, headless | 12 | 49 / 25 | PASS |
| Complete / Official + opt-in vegetation | 3 × 4 s, headless | 12 | 49 / 25 | PASS; not a vegetation performance rating |
| Complete / Official installed, native X11, radius 2 | 3 × 40 s | 120 | 49 / 25 | Logical/resource journey PASS; rendering rating unproven |
| Complete / Official installed, native X11, built-in radius 1 | 3 × 40 s | 120 | 25 / 9 | Logical/resource journey PASS; rendering rating unproven |

Two actors were active during stable windows. The shipping built-in radius is **1** (`ClientSettingsResolver.BuiltIn`); the harness defaults its separately normalized steady window to **2**. Canonical filenames containing `default` distinguish vegetation Off, not shipping render radius. The fourth Load restores central position, open door, bread in chest and equipped sword. Presentation lease/generation swaps use maintained derived/resource probes; primitive-only intentionally does not run an art-backed wall-override test. These are scripted mechanics, including administrative relocations to choose probe starts, **not a claim of uninterrupted human navigation**.

Settings use actual named controls and Save callbacks: Advanced application 1920×1080, frame/output 1280×720, internal 50%=640×360, UI scale 1.25 and render radius 3; Simple relocks 4:3/output/internal policy. All three native display-mode selections preserve roots/session. Exact original settings are restored in `finally`. Successful mode selection does not by itself prove pointer nomination under every realized mode.

Complete mixed control: `Local → menu → Local → menu → Remote A cold → menu → Remote B → menu → Remote A warm → menu → Local → menu → Remote A warm2 → menu → Local → menu`. Four fresh Local epochs; four fresh Remote applications; remembered A profile on both reconnects; independently scoped B. All eight lifetimes return to exact persistent-root baseline, including Chat/focus, gump ownership, context, Debug Off and cleared server profile. Pre-retirement `Residue` snapshots intentionally contain live canaries; they are not post-retirement zero-debt snapshots.

| Mixed Remote row | Required bytes | Optional presentation bytes | Pack bytes | Required ready / guest active ms |
| --- | ---: | ---: | ---: | ---: |
| A cold | 1,958 | 6,936,776 | 531,138 | 267.901 / 490.141 |
| B | 1,958 | 6,936,776 | 531,138 | 234.779 / 435.903 |
| A warm1 | 0 | 0 | 0 | 226.927 / 297.406 |
| A warm2 | 0 | 0 | 0 | 163.732 / 242.797 |

These one-shot wall clocks begin at Remote consumer construction **after authenticated profile negotiation**, not connect initiation. They are not latency distributions. Realm B authorizes its own metadata even when immutable blobs are shared; scope count grows 2→3 without duplicating its content bytes. Warm A fetches neither required, optional nor structural bytes. Exact application/session identities and complete snapshots are retained in raw JSON.

## Native resource observations and failed visual control

Host: CachyOS Linux 7.2.6-1-cachyos; Ryzen 7 7800X3D, 8 cores/16 threads, 32,738,320,384 RAM bytes; RTX 4070 Ti SUPER, 16,376 MiB VRAM, driver 615.71.09. Godot 4.7.1 Mono Compatibility/OpenGL; SDK 8.0.131, managed runtime 8.0.31 (launcher host 10.0.12 is distinct). KDE Wayland desktop; main native product launch X11. No affinity control or lower-spec host. Measurement jobs were serialized; ordinary desktop activity was uncontrolled. Two idle servers left by the earlier failed mixed development run were stopped before native measurements.

Native Complete Local radius-2 run: 171.623 s process elapsed, user CPU 28.541 s, system CPU 1.614 s. This includes client, packet-free local authority, setup and scripted probes; **not server-only CPU or a rated steady-state frame cost**. Thirty UI churn cycles, four Debug transitions per cycle, three complete retirements plus fourth-load retirement.

| Lifetime | Stable s | RSS first / last / maximum MiB | Heap min / max / last MiB | Gen0 collections |
| ---: | ---: | ---: | ---: | ---: |
| 0 | 40.260 | 527.0 / 582.1 / 582.1 | 46.0 / 105.1 / 98.7 | 4 |
| 1 | 40.186 | 524.9 / 562.7 / 562.7 | 59.8 / 121.1 / 96.5 | 3 |
| 2 | 40.199 | 582.8 / 616.2 / 620.4 | 45.8 / 104.9 / 55.2 | 4 |

Radius-2 owned counts remained stable: 49 semantic chunks, 25 render chunks, 180 renderer Nodes, two actors, two live derived textures/two materials, zero open views outside probes; structural ownership is reconciled separately from legitimate arriving public truth. Cache data may persist; session leases/owners must retire. Heap is sawtooth with collections, but **RSS growth has not been explained as a plateau or ruled out as a leak** by this short control. Exact root-boundary retirement does not prove all native/process memory is leak-free.

The separate built-in-radius-1 control explicitly passed `--f1-12-stable-radius=1`, starting and
restoring fresh default Settings radius 1. It also passed three 40-second windows, 30 UI cycles and
fourth-lifetime Load/retirement: elapsed 170.639 s, user CPU 25.325 s, system CPU 1.636 s. Stable
ownership was 25 semantic chunks / 9 render chunks / 84 renderer Nodes / two actors / two textures
and two materials. Its resource observations were:

| Lifetime | Stable s | RSS first / last / maximum MiB | Heap min / max / last MiB | Gen0 collections |
| ---: | ---: | ---: | ---: | ---: |
| 0 | 40.174 | 466.8 / 487.6 / 487.6 | 43.8 / 88.5 / 65.8 | 2 |
| 1 | 40.203 | 464.5 / 494.0 / 494.0 | 69.6 / 126.2 / 126.2 | 1 |
| 2 | 40.210 | 469.2 / 502.1 / 502.1 | 53.4 / 109.8 / 109.8 | 1 |

These are two independent 120-sample resource controls (240 samples total), not one uninterrupted
240-second steady window. The default-radius control likewise does not resolve the RSS trend.

The native draw-call monitor was zero in every sample of both controls; radius-2 per-second engine FPS was 130–145 but is **not accepted rendered FPS**. `TimeProcess` observations contain synchronous acceptance/UI probe work and are neither all-frame samples nor whole-frame p95. Consequently no GPU rating, lower-spec rating or monotonic-resource-stability conclusion is made.

The new opt-in Classic Cabin preview would capture installed/primitive views at Dawn/Noon/Dusk/Night × Temperate/Dry/Cold after actual `FramePostDraw`, preserving authority identity. **It did not produce an image**: X11 was cancelled after failing to progress at the draw wait; a separate Wayland control timed out at 90 s, exit 124. Primitive preview was not reached. Forced termination produced engine unreferenced-string/RID/allocator errors; these are retained, not evidence of normal clean retirement. Root cause of missing post-draw progress is unresolved. It would also need explicit doorway/interior/window/blocker views beyond the current Orbit capture. No bright/balanced/oppressive profile or final GI capability is invented.

## Verification and motion attribution

`bash ./verify.sh --architecture-only`: PASS, four gates.
`bash ./verify.sh --configuration=Release`: **PASS, one uninterrupted invocation on ac6a81bf**. Includes Core/Content/Local/Network Application/Replication/World, S0 native, F1.7/8/8A/9A/B/10/11, W7/W7-R1, three actual exports, Contentless Online cold/warm, eight-lifetime mixed, Complete hosting, and PF1/PF2 native/headless bot impairment/action/physical-accounting gates. Expected stripped-package/unknown-flavor/wrong-edition failure probes remain expected failures, not suppressed verifier failures.

Selected managed counts: World 115, Core 36, Content 38, BuiltIn 9, Persistence 13, Admission 18, Chat 36, Inventory 34, Local 24, Replication 181; Network Application reports 72 plus its preserved integration families, including 16 W7 tests. No gate was weakened. Development focused Local/Admission/Chat/save-set/native acceptance runs passed; save-sequence and metadata-fence mutations failed as intended and were reverted. The original focused export campaign failed on metadata; the original mixed campaign failed on Settings. Final canonical covers their repaired paths rather than relabeling those earlier failures as passes.

PF1 seed 104729: 8 bots + 2 actual exported Online clients, mixed chaos 14 s, clean joins/leaves 11/11, sword 1/1/1. PF2 seed 130363: 20 s after 5 s warmup, UDP accounting reconciled and retired. Exported native adapters in these canonical rows were **headless**, not a rendered crowd benchmark or product capacity rating.

Existing real-UDP motion model controls retained first-presented 4,220–4,451 µs, first-owner 14,804–137,664 µs; Stop error 29–1,136 mm and settling to 20 mm 55,475–178,412 µs. These are deterministic harness response clocks, **not perceived native response/human feel**. Historical 1.4–1.67 m correction tails remain in the handoff; a smaller current sample is not a new maximum. Controlled authoritative Y, facing/jump/block/stop mechanics remain under maintained tests, not new presentation tuning.

## Reproduce and continue

Run the focused exported controls or the complete canonical path:

```bash
bash ./verify.sh --f1-12-only --configuration=Release
bash ./verify.sh --f1-12-mixed-only --configuration=Release
bash ./verify.sh --architecture-only
bash ./verify.sh --configuration=Release
dotnet run --project _Tests/UltraSim.Client.Local.Tests -c Release -- --test ConceptDemoSaveSequenceSurvivesSessionReentry
dotnet run --project _Tests/UltraSim.Game.Admission.Tests -c Release
dotnet run --project _Tests/UltraSim.Game.Chat.Tests -c Release
```

The final full invocation, not a standalone final `--f1-12-only` rerun, produced the retained passing focused rows. Focused verifier PID outputs live in ignored `.output/f1.12-verification/<pid>/`; the mixed-only control retains its Complete export in `.output/f1.12-products/<pid>/`.

The native export/control used **absolute output and argument paths**; a relative export path first failed Godot's path preparation and is not a product failure:

```bash
bash Client/Godot/export-client.sh Complete '/mnt/data/mega/Godot Projects/UltraSim/.output/f1.12-products/ac6a81bf/UltraSimComplete.x86_64'
env XDG_DATA_HOME='/mnt/data/mega/Godot Projects/UltraSim/.output/f1.12-native/ac6a81bf/user-installed' \
  '/mnt/data/mega/Godot Projects/UltraSim/.output/f1.12-products/ac6a81bf/UltraSimComplete.x86_64' -- \
  --f1-12-dogfood --expect-edition=Complete --f1-12-art=installed --f1-12-seconds=40 \
  --f1-12-head=ac6a81bf1f777380cdf87b62719a6204bded4a8f \
  '--f1-12-save-root=/mnt/data/mega/Godot Projects/UltraSim/.output/f1.12-native/ac6a81bf/installed-saves' \
  '--content-cache=/mnt/data/mega/Godot Projects/UltraSim/.output/f1.12-native/ac6a81bf/installed-content' \
  '--f1-12-evidence=/mnt/data/mega/Godot Projects/UltraSim/.output/f1.12-native/ac6a81bf/installed.json'
```

Choose new isolated directories for a cold reproduction. `--f1-12-stable-radius=1|2|3` defaults to 2 **in this diagnostic harness**; pass `=1` for the shipping built-in radius. Earlier development evidence at radius 3 is not substituted for either control. `--f1-12-art=absent|primitive` selects the maintained missing-art/fallback controls. `--f1-11-vegetation` is separate opt-in. Native Cabin: `timeout 90s <export> -- --f1-12-cabin-preview --f1-12-art=installed --f1-12-cabin-evidence=<absolute-directory> --f1-12-save-root=<absolute-isolated-root> --content-cache=<absolute-isolated-root> --f1-12-head=<tested-sha>`; Wayland control additionally used `--display-driver wayland --disable-vsync` before `--`. Do not call it passed until all images, identity preservation and clean retirement exist.

## Required final-report checklist / provisional PF6 handoff

1. Starting HEAD: e96af793046afc21dcd0a4f164a9eaec7e8ad2bc.
2. Tested implementation HEAD: ac6a81bf1f777380cdf87b62719a6204bded4a8f.
3. Final evidence HEAD: resolve by the path-specific command above; evidence-only, separately attributed.
4. Commits: nine implementation commits listed above, then evidence/docs.
5. Files: Server/Persistence coordinator; Game/Admission + README; Client.Local session; Godot renderer/root/Local/Remote partials and snapshots; Local/Chat/native tests; repository verifier; current Client/completion/pre-freeze docs; these evidence companions. Exact list: `git diff --name-only e96af793 HEAD`.
6. Total ledger entries: 12, including seven carried-forward groups.
7. Category 1: five (four product, one assurance).
8. Category 2: zero confirmed; unrun controls do not establish missing product behavior.
9. Category 3: three carried-forward questions.
10. Category 4: four deferred groups.
11. Repaired defects: durable save sequence, Local post-load Admission/Chat, metadata generation race, active Remote Settings; plus phase-correct grip assurance.
12. Blockers: complete native rendering/visual controls and resource/performance coverage below.
13. SinglePlayer dogfood: exported mechanical journeys PASS; not manual feel evaluation.
14. Official/primitive: installed, absent and primitive exports PASS mechanically.
15. Save/load: central/Far, repeated lifetimes and fresh re-entry Load PASS.
16. Item lifecycle: pickup/equip/drop/repickup, chest transfer and grounded paths PASS.
17. Online cold: actual exported Contentless Online and mixed A/B PASS.
18. Online warm: zero required/optional/Pack fetch in both mixed A reconnects.
19. Reconnect: two repeated A reconnects, fresh epochs/profile reuse PASS.
20. Multi-Realm: independently authorized B transition PASS.
21. Complete lifecycle: eight mixed lifetimes and exported hosting parity PASS.
22. Session/resource soak: two separate 120-sample native logical controls + shorter headless windows; ownership stable, RSS trend open.
23. Settings: actual Save controls, scoped Remote preservation and restoration PASS.
24. Display modes: logical configuration/session preservation PASS; visual/pointer matrix incomplete.
25. UI/gump churn: 60 native logical cycles across two controls, drag/front/context/independent close and exact retirement PASS.
26. Chat/focus: Enter/Escape/click-away and retirement canary PASS; post-load Chat repaired.
27. Context/tooltip: maintained native-layout and action probes PASS; full display-variant pointer journey open.
28. Camera/profile: three stock profiles preserve authoritative identity; human feel ungraded.
29. Movement: maintained authoritative mechanics/temporal tests PASS; no tuned prediction policy.
30. Impairment: maintained multi-seed PF1/PF2 and latency/jitter/loss motion controls PASS.
31. Actor-tier product: synthetic native assurance PASS; actual-product promotion/demotion/crowd/nameplate visual journey incomplete.
32. Presentation generation: User wall enable/disable and semantic preservation PASS; no authority reset introduced.
33. Derived-cache recovery: maintained corrupt/version/delete-rebuild acceptance PASS; warm disk reuse retained.
34. Render distance: 1/2/3 churn PASS; separate steady harness-radius-2 and shipping built-in-radius-1 logical resource controls PASS.
35. Far Gate/Pack seam: Local reciprocal gate and exported W7 127→128→127 PASS.
36. W7-R1: preserved three-plus-Pack/corner/retirement-fence controls PASS in canonical suites.
37. Primitive usability: core mechanical navigation/interaction fallback PASS; visual feel not graded.
38. Vegetation opt-in: Complete journey PASS; default stays Off, no performance/default change.
39. Cabin: Classic mechanical F0.2 initialized; native post-draw image/doorway/interior/window/blocker matrix OPEN.
40. Performance/resources: no rendered FPS/whole-frame p95 rating; stable owner counts do not resolve rising RSS.
41. Default proposed PF6 configuration: current NodeMultiMesh/PerRow, Hybrid soft64/20m/28m, built-in radius1, vegetation Off, normal Official optional art/current display policy; not yet a readiness approval.
42. Optional later PF6 comparisons: primitive-only/missing art, passive User override, radius2/3, display internal scale and independently opt-in vegetation; label each, no capacity promises.
43. Human-feel questions: full historical correction tails, sparse holds, grip/animation/camera and interaction feel.
44. Polish: primitive/clay style, Cabin lighting/materials, equipped visual grade.
45. Deferred: W8/W9/W10, final art/GI/atlas, vegetation gameplay, GPU actors/other adapters.
46. Versions: settings5, Game33, transport2, WorldPack1, structural manifest1, worldview2, 60Hz/cap64 unchanged.
47. Architecture verifier: PASS on implementation; evidence/docs links checked separately afterward.
48. Canonical Release: PASS uninterrupted on ac6a81bf; no final-evidence-SHA recursion claim.
49. Evidence: report/summary/raw/Release/control logs linked above.
50. W8/W9/W10 and PF6: not started.
51. Exact verdict: **NOT READY FOR PF6**.
52. Worktree: clean at frozen implementation verification; final evidence/docs committed separately and final status checked in handoff.

Concrete remaining closure work, not a protocol/renderer redesign:

- Diagnose the missing native post-draw progress, then produce actual installed/primitive Cabin doorway/interior/window/blocker and time/atmosphere evidence. Preserve the failed controls.
- Complete actual-product actor-tier/nameplate and pointer/display visual interaction controls. Passing synthetic native tests is not a substitute.
- Extend actual Online and representative rendered field/interior/dense/radius resource observations, and establish whether current RSS growth plateaus. Keep process/authority/fixture clocks separate; do not claim all-frame p95 from latest-frame samples.

No lower-spec test, final-art feature or disabled vegetation cost is added as a blocker. PF6 remains waiting on F1.12, not on a new optimization campaign.
