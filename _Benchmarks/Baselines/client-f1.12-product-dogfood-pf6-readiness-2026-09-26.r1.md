# F1.12-R1 — native product assurance and retirement memory

Disposition: **F1.12 ACTIVE / NOT READY FOR PF6.** R1's native/pointer/tier and bounded memory
controls pass. A new unexpected HTTP shutdown error observed in canonical verification remains
open. A later passing retry is not a repair. The historical capture stall's exact trigger was not
reproduced; that diagnostic limitation is preserved rather than replaced with an invented cause.

This companion extends the [original immutable F1.12 report](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.md).
F112-01–05 and their original failed runs/dispositions are unchanged. See the
[machine summary](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r1.json) and
[raw file/hash index](client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r1.raw.json).

## Lineage and scope

- Starting clean main: `1ebde0319875e78aba6fe9bd6e180bc7361a9c15`.
- Prior accepted tested implementation: `ac6a81bf1f777380cdf87b62719a6204bded4a8f`.
- Vegetation preference: `960b7f52fe9b253d5d108b13a0f5162eba835ed8`.
- Native assurance implementation: `056c7e336b02e00bec30938b2fcc37ee02b32cf6`.
- Final tested implementation: `7590b67f0226affdbb60f20e1db12dbf208c27f7`.
- Evidence-only commit: the commit containing this companion; exact SHA is in the task handoff.
  Resolve independently with `git log -1 --format=%H -- _Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r1.md`.
  No self-referential SHA is fabricated. The raw native JSON records the tested implementation SHA.

All eleven implementation/diagnostic commits are listed in the machine summary. Changed sources:
`Client/Core/ClientApplicationSettings.cs`; Godot `ClientHost.Remote.cs`, `Shell/ClientRoot.cs` and
`ClientRoot.F112.cs`; Local `LocalCabinClient.cs`, `LocalCabinClient.F112.cs`, new
`LocalCabinClient.F112Pointer.cs`, `LocalPlayClientRoot.cs`, `LocalPlayClientRoot.F112.cs`, new
`LocalPlayClientRoot.F112Memory.cs`; Remote `RemoteConceptDemoClient.cs` and new
`RemoteConceptDemoClient.F112.cs`; new `Adapter/GodotDiagnosticProcessMemory.cs`; Core settings/
camera tests, `T010PlayableWorldAcceptance.cs`, and `_Tools/RepositoryVerification/Program.cs`.
Current docs are updated, not the old baseline. No Game, Server, Network, world composition,
wire, cadence, authority, picking acceptance, or actor-tier production policy is changed.
W8/W9/W10 and PF6 have not started.

## Environment and reproducibility

Release, actual Complete Linux export, native X11 window on KDE Wayland (`DISPLAY=:0`), Godot
4.7.1 Mono `a13da4feb`, Compatibility/OpenGL 3.3. CachyOS Linux 7.2.6-1-cachyos; Ryzen 7800X3D
8 cores/16 threads; 32,738,320,384 bytes RAM; RTX 4070 Ti SUPER 16 GiB, NVIDIA 615.71.09.
.NET SDK 8.0.131/runtime 8.0.31 (installed launcher host 10.0.12). No controlled affinity or
dedicated machine isolation; ordinary desktop activity. Native campaigns run serially, not with
competing benchmark jobs. Local process memory includes the internal authority and client.
Rendered Remote observations are client-process memory with separate Direct Connect servers.

Commands:

```bash
bash ./verify.sh --f1-12-r1-only --configuration=Release
bash ./verify.sh --architecture-only
bash ./verify.sh --configuration=Release
dotnet run --project _Tests/UltraSim.Server.Hosting.Tests/UltraSim.Server.Hosting.Tests.csproj -c Release
```

The focused gate builds the real Complete export and uses isolated content/save roots. It invokes
`--f1-12-render-probe`, installed and primitive `--f1-12-cabin-preview`,
`--f1-12-native-product`, `--f1-12-memory --f1-12-lifetimes=8 --f1-12-seconds=15`, and
`--f0-7-mixed-smoke --f1-12-rendered-mixed`. The full commands/environment and server endpoints
are retained in `controls/f112-r1-focused7.log`; primary export run ID is `2586235`.
The independent repeated memory control came from run `2572787` at `c3d85e442263500daf0cf207104171fc977c4813`.

Selected policy is unchanged: NodeMultiMesh/PerRow; Detailed/BatchedPrimitive hybrid (soft 64,
promote 20 m/demote 28 m); vegetation default Off. Game manifest 33, transport 2, WorldPack 1,
structural manifest 1, world view 2, 60 Hz, product participant cap 64. ChunkPublication shared
source/body truth, CurrentBundled, selected multi-entry 148 LZ4, singleton compression Off,
cadence 1/2/4, CrossLane maximum two, BackgroundPlatformBatch.

## A — native capture boundary and Cabin

The old tool passively awaited global FramePostDraw without a timeout or active render request.
That is a confirmed assurance weakness, **not a proven explanation of the historical 90-second
stall**. A retained original `ac6a81bf` export completed the original twelve-capture/three-wait
matrix in today's bounded control. A Ready-time control reproducing the old F0.2 ordering also
completed its three initial passive waits. Settings read differed (old v5 export encounters v6),
and the original forced-kill environment/state is insufficient to isolate a causal engine race.
Do not relabel today's pass as reproduction or reuse forced-kill cleanup warnings as leak data.

The new cold capture tool uses **the existing Gameplay Output SubViewport**, not a screenshot
viewport: establish the session/view/environment, cross process and physics boundaries, subscribe
to pre/post-draw, call synchronous `RenderingServer.ForceDraw`, require both observations, obtain
the output texture, read its image, validate dimensions and nontrivial RGB range, and save PNG.
Godot documents ForceDraw as forcing a rendering frame in the
[RenderingServer API](https://docs.godotengine.org/en/stable/classes/class_renderingserver.html).
Signals have eight-second failure bounds; synchronous native calls additionally have the verifier's
240-second process bound. Stages log request, admission/acceptance, environment, frame crossings,
native request/pre/post observations, texture/readback and PNG completion. No sleep substitutes
for proof of draw. This is diagnostic-only, not a game frame synchronization policy.

Twenty Cabin PNGs: eight installed covering views plus four primitive views in **that same
session**, and eight fresh primitive covering views. One known-scene probe, three tier/nameplate
captures and four Remote captures bring primary R1 total to **28**. All captures are 1920×1080;
filename, art/view/time/atmosphere, dimensions, RGB extrema, pre/post counts, pixel and PNG SHA-256
are retained in their raw JSON and indexed files. This is not an aesthetic or FPS rating.

Installed covering matrix:

| Views | Time | Atmosphere |
|---|---|---|
| Exterior, doorway, main interior, shelf/blocker | Noon | Temperate |
| Exterior | Dawn, dusk, night | Temperate |
| Exterior | Noon | Cold |

The fresh primitive control covers the same eight cases; the same-session pair covers the four
Noon/Temperate geometry views. RGB/hash changes distinguish environment and art output. Visual
inspection confirms the interior furniture/blocker and Cheap-actor nameplate; it does not certify
lighting taste, optical accuracy, or Cabin attractiveness. Door opening is established by the
existing actual Door/Chest acceptance before capture. **Classic Cabin does not author a secondary
room, an interior doorway, or a window**; there is no fabricated window/light-spill acceptance.
These are explicit authored-demo limitations, not renderer failures or newly expanded W8 work.

Same-session invariant: application/control epochs `2/1`, authority generation `1`, semantic hash
`4884640456701042578 / 13764519638206834015` remain identical. Classic single-Pack base fingerprint
is `80DCF456AD43FD3876D6E5BB6936FD3747E0ECDA23179407753DD393DC981B57`; structural manifest is N/A,
not a fictitious manifest. Presentation generation alone changes `1 → 2`, wall source
`Local → Primitive`, animated/fallback actors `1/1 → 0/2`, live derived textures/materials
`2/2 → 0/0`, and live blob/catalog pins `9/2 → 0/0`. No authority/session reset occurs.
Both previews and the known-scene probe retire cleanly to the exact persistent-root boundary.

## B — exported Settings, pointer, tiers and overlays

Actual Settings controls and Save callbacks realize four display configurations, restoring the
original document, persisted store, native display and session focus in `finally`:

| Case | Application | Gameplay Frame | Output | Internal 3D |
|---|---|---|---|---|
| Simple 16:9 | 1920×1080 | Full | 1920×1080 | 1920×1080 |
| Simple 4:3 | 1600×1200 | Full | 1600×1200 | 1600×1200 |
| Advanced | 1920×1080 | 1280×720, origin 320/180 | 960×540 | 960×540 |
| 50% | 1920×1080 | Full | 1920×1080 | 960×540 |

Native camera projection supplies application coordinates; actual mouse warp, process boundary,
shipping pointer mapping and shipping world picker must agree with independently checked ID and
selection. Ground point is checked against the real support model. Exact targets in every case:

- Woodland Animal `(3,1)`, public version 1, prefab 4; selection `WoodlandAnimal`.
- Iron Sword `(9,1)`, public version 1, prefab 9; selection `IronSword`.
- Fallback Lab Sign `(8,1)`, public version 1, prefab 7; selection `FallbackLabSign`.
- Supported ground at `(17500,5000)` mm; selection `Terrain`.

Actual sign Interact is submitted through the input/authority path and returns **OutOfRange** in
all four rows. This is equal negative authority behavior, not a fabricated accepted action.
Player authority position remains `(15500,1000,7000)` mm with unchanged generation/public identity
and hand state. The display matrix proves selection/support and one actual interaction result;
it does not claim a full destination-travel action for every screen shape. Existing D0.0 and
item acceptance separately exercise real movement, pickup, equip, drop and repickup.

The real animal retains `(3,1)`, version 1, semantic record, pose and display label through
Detailed → BatchedPrimitive → Detailed. Existing administrative player relocation establishes
near/far distance; no invented crowd or new policy. The controlled actor remains Detailed.
The Cheap animal has no actor root; its same-ID ray nomination works without adding physics Nodes.
Actual Ctrl input reveals **Woodland Animal** above the Cheap body, preserves its tier/row, and
hides on release. Total visible labels `1 → 2 → 1`; no duplicate body or stale Detailed nomination.
PNG `pointer-tiers/tier-1.png` records the Cheap reveal. Equipped feature control records one
visible main hand on the controlled Detailed actor before the separate drop/repickup workflow.
No claim is made that a distant non-controlled actor equips gear in this fixture.
Exact root/session retirement removes world host, controllers, catalog consumers, gumps and profile.

## C — bounded same-process memory campaign

Two independent eight-lifetime, installed-content Local campaigns, shipping radius **1**, 15 s
stable window/lifetime, no UI churn. Each records initial shell, four live points, eight-frame +
one-second settled retirement, and a **separate** forced-GC-after-all-retirement experiment:
42 samples/campaign. No forced GC becomes production behavior. No killed run is included.

Final implementation live RSS spans 381.484–525.508 MiB. Retired census (MiB, not MB):

| Lifetime | RSS | PrivateMemorySize64 | Anonymous | Private dirty | File RSS | Managed live |
|---:|---:|---:|---:|---:|---:|---:|
| 0 | 444.941 | 495.164 | 193.742 | 236.289 | 185.059 | 51.140 |
| 1 | 508.555 | 569.820 | 252.637 | 299.711 | 185.184 | 97.557 |
| 2 | 525.762 | 586.656 | 268.375 | 316.863 | 185.246 | 64.689 |
| 3 | 502.766 | 562.094 | 244.895 | 293.789 | 185.309 | 71.148 |
| 4 | 511.543 | 574.141 | 252.965 | 302.629 | 185.309 | 61.512 |
| 5 | 507.957 | 573.398 | 249.480 | 299.441 | 185.309 | 57.310 |
| 6 | 502.563 | 566.941 | 242.805 | 293.457 | 185.500 | 66.720 |
| 7 | 508.465 | 577.781 | 248.648 | 299.359 | 185.500 | 62.075 |
| Diagnostic GC | 505.168 | 568.445 | 245.340 | 296.066 | 185.500 | 8.141 |

Initial process RSS is 230.090 MiB. Early warmup reaches 525.762 MiB, then the five remaining
retirements reuse a nonmonotonic **502.563–511.543 MiB** band. The independent C3 campaign records
446.574, 509.750, 524.746, 502.363, 509.035, 503.098, 502.672, 508.625 MiB; diagnostic GC 505.496.
All tails/peaks are retained. This supports **bounded high-water reuse in the tested workload**,
not an arbitrary per-lifetime threshold or a universal proof that longer/more varied runs cannot leak.

Forced GC reduces managed live from 62.075 to 8.141 MiB, but RSS only 508.465 to 505.168 MiB;
GC heap 36.918 MiB includes fragmentation 29.248 MiB. Thus logical retirement is not equivalent
to immediately returning process pages. Anonymous/private memory plateaus along with RSS and file
RSS changes little after warmup. Exact native allocator/graphics-driver ownership is **not resolved**;
no entire residual is labeled a harmless cache. No memory leak repair was invented.

Live owners are invariant: 25 semantic chunks, 9 render chunks, 81 renderer Nodes/51 resources,
two actors (one content-backed, one fallback), one retained actor resource/2,213,380 artifact bytes,
two textures/two materials, nine blob pins/two catalog pins, one Gameplay Output and zero open UI
views in this steady window. Retired controller/catalog/world-host/gump counts are zero with
inactive server profile/MainMenu; persistent scaffold counts intentionally remain. Global native
Nodes 41 initially/42 after first retirement then constant; orphan count 0, resource monitor 3.
Godot `MemoryStatic=0` is an unavailable/zero monitor, **not zero actual native memory**; global
resource monitor is not a count of all adapter-owned resources. Existing exact lifecycle gates
prove disposed session consumers; no post-disposal snapshot invents live pin values from null.

Memory schema records Process RSS/private, managed live/allocation/GC counts, GC heap/fragmentation/
available bytes, owned presentation snapshots and native monitors. Linux status retains VmRSS,
RssAnon, RssFile, VmSwap; rollup retains RSS/PSS/Anonymous/private clean+dirty/shared clean+dirty/
Swap. Reads are bounded cold diagnostics, optional on non-Linux, with unsupported/error fields
explicit. These overlapping/asynchronous kernel and GC figures are **not additive**. Raw bytes
allow independent recomputation; no RSS-only conclusion or extrapolated FPS/CCU is made.

## Rendered remote and mixed lifecycle

The Complete export uses its real Online capability against two separate servers: four rendered
Remote lifetimes, four one-second resource observations each, four output PNGs, with two warm A
reconnects. Fresh Remote epochs are retained exactly in raw JSON; profile decisions are
false/false/true/true. Mixed journey completes eight lifetimes and clean persistent-root boundaries.
These are real Remote consumers, not a standalone Online-only memory campaign.

Each Remote has 9 chunks, 81 renderer Nodes/51 resources, two actors and two world textures/materials.
Retained actor bytes: A 2,213,268; B 2,509,988; warm A 2,213,268. Client RSS ranges by lifetime:
459.691–460.148, 461.086–469.293, 452.680–452.695, 491.316–491.328 MiB. The brief Online
observations prove representative realization/retirement, **not a remote-memory plateau**.
Existing retirement canaries still deliberately create live UI, focus, speech/nameplates/resources
before disposal; the added rendered observation refreshes expiring real SayChat rather than
weakening that canary.

## Vegetation and findings

Advanced UI label: **Experimental Vegetation (prototype; applies next world entry)**. Settings v6,
device-owned bool, default Off. v1–v5 migrate Off, v6 omission fails closed. All With* paths retain
the choice; profile/Realm decisions cannot own it. Debug CLI force-enables, otherwise saved choice
is captured at Local/Remote entry and stays fixed through radius rebuilds. No live-toggle rewrite
or new Game/session identity. Actual Advanced Save and store reopen are tested, then restored.

| ID | Classification / reproduction | Cause and disposition | PF6 blocking? |
|---|---|---|---|
| F112-R1-01 | Assurance weakness: original capture waits unbounded | Cold explicit native request/readback + bounded signals; old stall does not reproduce today, exact historical trigger open | Traceability qualification; no current failed capture |
| F112-R1-02 | Missing exported pointer/tier/nameplate coverage | Real Settings/native nomination/tier/Ctrl controls pass on final implementation | No current failure |
| F112-R1-03 | Unexplained early RSS rise | Two clean eight-lifetime runs reuse warm RSS/anonymous/private high-water with fixed owners; no leak repair | No for tested workload; native allocator decomposition remains limited |
| F112-R1-04 | Product usability: vegetation only CLI discoverable | Device-owned Advanced preference, v6 migration, default Off, next-entry label | Repaired, nonblocking |
| F112-R1-05 | R1 diagnostic prerequisites/focus/expiry/edition failures | Correct pickup-before-drop, focus-before-new-edge, real Chat refresh and Remote conditional compilation; failed logs retained | Corrected; no Game authority workaround |
| F112-R1-06 | **Unexpected HTTP shutdown failure**, high occurrence confidence; exact interleaving unisolated | `HttpListenerResponse.FormatHeaders` NRE via framework connection/endpoint Close during unchanged hosting stress. Cleanup propagates error, canonical run exits 134. Focused retry passes 23/23, not a repair; Server code untouched | **OPEN — blocks PF6** |

F112-01–05 remain closed regressions, not renumbered. F112-08 aesthetics, historical 1.4–1.67 m
motion-feel tails, vegetation taste, advanced GI, skeletal/GPU batching and W8–W10 remain human/
future/nonblocking questions, not newly manufactured release gates.

## Verification and release disposition

One uninterrupted **canonical Release PASS** completed on frozen executable sources at `7590b67f`
(run `2591542`, exit 0, `controls/f112-r1-release3.log`). Current docs/evidence were prepared
during that run; no executable sources changed. The native focused R1 gate passes on `7590b67f`
(and independently on C3). Architecture passes all four checks on frozen implementation and again
after the current documentation/evidence update (`controls/f112-r1-architecture-docs.log`).
Failed full run 1 is retained in `controls/f112-r1-release.log`: two framework NREs during
`StaticContentHttpHostShutdownStressIsBounded`. Full run 2 is retained in
`controls/f112-r1-release2.log`: new Remote diagnostic partial lacked its edition guard in
SinglePlayer publishing; corrected in `7590b67f`. Isolated Hosting passes 23 tests including 128
stress cycles, but it does not erase run 1. No blanket exception suppression or unrelated host
repair is included. Original successful `ac6a81bf` Release log is unchanged.

Final run includes Client Core **36**, Content **38**, BuiltIn Test World **9**, Client Local **24**,
Network Application (existing enumerated groups, including **16 W7** tests), Game Replication
**181**, World **115**, Admission **18**, Chat **36**, Persistence **13**, Bot **24**, Server Hosting
**23**, and S0 integration **39** passing tests. S0 Godot (including process-memory parser),
F1.8A motion/real UDP, F1.9A/B, F1.10 tiers, F1.11 stress and retained W7-R1 regressions pass.
All three physical exports pass edition closure; Online remains Contentless. Four exported
F1.12 product controls (SinglePlayer installed/absent/primitive, Complete installed+vegetation),
eight mixed lifetimes, Contentless Online cold/warm/profile change, Complete Hosted, PF1 mixed
swarm and PF2 accounting pass. Exact case output is retained, not inferred from a partial rerun.
The new native R1 gate is separate from the canonical headless product journey; neither is
represented as the other. The companion/raw JSON and logs distinguish both full-run failures
from the successful final run.

Concrete next action: separately reproduce and repair unexpected HTTP shutdown failure, retain
all worker failures/cleanup contracts, then establish canonical Release on that repaired integrated
tree. R1 does not broaden into a host rewrite or enter PF6. Capture history remains honestly
qualified: the requested historical stall root-cause criterion has not been established, despite
successful current bounded captures. Authored missing Cabin surfaces are listed rather than invented.

**NOT READY FOR PF6.** F1.12 stays ACTIVE. Worktree at handoff is reported after the evidence-only
commit; no unrelated edits were overwritten or included.
