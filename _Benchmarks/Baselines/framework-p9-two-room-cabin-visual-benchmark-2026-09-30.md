# P9 two-room Cabin Visual Benchmark — implementation evidence

Status: **benchmark implemented; P9 remains ACTIVE.** This is an authored visual laboratory, not a replacement for Classic Cabin or a human lighting/feel rating. Starting HEAD was `080a72ae63ec4101021c4290dec670315e69e278`; the implementation and all results below were tested on `d434a5b34a0917ca5dcc4bfc339b5f55f102f6c8`.

## Authored truth and presentation boundary

`benchmark-world:cabin-visual/v1` is a separate deterministic 25-chunk WorldPack, fingerprint `B0CE72EEE73C3E014733499A9816A608280DFEF6757BD40FB599DB0318E81BDC`. It contains a two-room 8 × 12 m structural shell with a continuous 100 mm supported floor, an exterior doorway in windowless Room A, a 2 m open interior doorway, one east-facing Room B window aperture with separate structural jamb/sill/lintel collision, two roofs, a Room B bookshelf blocker, and small exterior tree/rock controls. The opening is authored structure, not a hole invented by the renderer. There is no new interactive interior door or window mechanic.

Classic Cabin and its oracle were not edited. Its frozen WorldPack fingerprint remains `80DCF456AD43FD3876D6E5BB6936FD3747E0ECDA23179407753DD393DC981B57`. The new pack runs through normal Local authority/WorldPack binding only when `--p9-two-room-benchmark` is supplied; normal Concept Demo composition is unchanged. The existing `official.world-local.cabin-p9` package (manifest `8f1a92abaf6628a254321b0c95465beeba7d540f944d33636760d8bf5c0c6951`) supplies optional art. Primitive mode keeps the same authoritative pack and renders the four structural window pieces instead of its painted skin.

The integration gate verifies exact fingerprints, deterministic reopen, room topology, real window collision gap, continuous floor, solid wall/bookshelf collision, and an ordinary authority movement command walking through the interior doorway. No Game, Network, inventory, or content-authorization protocol changed. A bounded vegetation-preparation halo now checks neighboring chunk-owned roof/floor fragments, preventing grass from piercing the authored floor at a chunk seam; vegetation remains opt-in/default Off.

## Runs and observations

Host: Linux `7.2.6-1-cachyos`, AMD Ryzen 7 7800X3D (16 logical processors), NVIDIA GeForce RTX 4070 Ti SUPER driver `615.71.09`, Godot `4.7.1` Mono/OpenGL, local .NET SDK `8.0.125` and runtime `8.0.25`. Exported Complete captures used an X11 display and the normal Official package output. No human subjective rating was performed.

| Control | Result |
| --- | --- |
| `./verify.sh --architecture-only` | Pass; four architecture checks. |
| S0 integrated acceptance, Release | 40/40; new two-room gate passed. |
| `./verify.sh --configuration=Release` | One uninterrupted pass on the implementation commit, including physical exports, product dogfood, Online WorldPack, Complete-hosted and final composition parity. The exact [canonical log](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/raw/canonical-release.log) is retained. |
| Separate Complete export | Passed; retained [export log](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/raw/complete-export.log). |
| Exported Complete, cold cache, vegetation On | 20/20 native captures; clean session retirement. Actual vegetation-patch census found no patches inside the 8 × 12 m floor footprint. |
| Exported Complete, warm same cache | 20/20 native captures; clean retirement, zero actor-package source bytes fetched in the warm acquisition snapshot. |

In the exported cold capture, both installed and same-session Primitive had 25 rendered chunks and the same semantic world hash (`Low=11998795687333834974`, `High=14004041867362825532`). Optional world-mesh selections changed `11 → 0`; static instances changed `22 → 25` because one painted window skin was replaced with four structural aperture pieces. The capture asserts this exact delta. The warm capture reproduced these counts and truth. The clean final menu boundary reports no active session. [Raw cold](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/raw/exported-complete-cold-vegetation-on.json), [raw warm](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/raw/exported-complete-warm.json), and [integrated acceptance](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/raw/integrated-acceptance.json) retain the full observations; all 20 exported cold [PNGs](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/captures/) are retained.

Representative views: [Room A noon](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/captures/installed-RoomA-Noon-Temperate.png), [interior doorway](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/captures/installed-InteriorDoorway-Noon-Temperate.png), [Room B noon](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/captures/installed-RoomB-Noon-Temperate.png), [Room B night](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/captures/installed-RoomB-Night-Temperate.png), [installed window exterior](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/captures/installed-WindowSide-Noon-Temperate.png), and [Primitive Room B](framework-p9-two-room-cabin-visual-benchmark-2026-09-30/captures/primitive-same-session-RoomB-Noon-Temperate.png).

Reproduction from a built Godot project or Complete export: run with `-- --p9-two-room-benchmark` for interactive Local play. For bounded image evidence, add `--f1-12-cabin-preview --f1-12-cabin-evidence=<directory>`; `--f1-11-vegetation` opts into the vegetation control. Use a fresh cache for cold acquisition and reuse it for warm acquisition. The exported control used this command shape (with an absolute writable evidence directory):

```bash
bash Client/Godot/export-client.sh Complete /tmp/p9-export/UltraSimComplete.x86_64
/tmp/p9-export/UltraSimComplete.x86_64 -- \
  --f1-12-cabin-preview --p9-two-room-benchmark --f1-11-vegetation \
  --f1-12-cabin-evidence=/tmp/p9-evidence \
  --f1-12-save-root=/tmp/p9-saves --content-cache=/tmp/p9-cache \
  --official-content-root=/tmp/p9-export/Content/Official
```

## Remaining P9 decisions

The authored geometry and native realization now make Room A versus Room B, directional time-of-day, aperture, blocker, and Primitive comparisons possible without changing semantic identity. These captures do **not** prove that sun transmission through the current painted/glazed material, interior brightness, composition, or human feel meet the final P9 visual target. Any art/lighting tuning should be evaluated against this fixed WorldPack and separately versioned if authoritative structure changes. P10/P11, Gate 1, and PF6 were not begun.
