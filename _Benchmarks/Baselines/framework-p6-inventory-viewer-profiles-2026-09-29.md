# P6 — Inventory Engine viewer profiles (2026-09-29)

Status: **CLOSED for the current supported product Inventory shapes.** P7 is next; P8–P11, Gate 1 and PF6 have not started. This is viewer/presentation work over unchanged authoritative Inventory, not an Inventory-engine rewrite.

## Provenance

- Clean starting `main`: `25cfaf5c024d5bb05f296074a9a162051467d048`.
- Implementation and focused repair commits, in order: `d95587529e572af652629d1f6c877d5dff8da9ed`, `d94a64a2d360d08c6e3ade298dd642a4ad60ec06`, `757ce4831cfeb45b2810ddb9d8266706f49f5d88`, `d2aead903689a575b51958c57b80c91d34caed51`, `cab7876d912573ea5aa4bee0338ca71b0e44e7ca`, `4f7fc2c50a7aa5e7da3efd6cb6ef1d3092f25839`.
- Exact production tree tested by the uninterrupted canonical Release verifier, architecture verifier, graphical native acceptance, fresh export and warm launches: `4f7fc2c50a7aa5e7da3efd6cb6ef1d3092f25839`. This documentation/evidence commit is later and does not change production code.
- Host: CachyOS Linux `7.2.6-1-cachyos`, AMD Ryzen 7 7800X3D (8 cores/16 threads), NVIDIA RTX 4070 Ti SUPER; pinned Godot Mono 4.7.1 (`a13da4feb`), .NET SDK 8.0.131 and .NET/ASP.NET runtimes 8.0.31. A task-local matching SDK/runtime at `/tmp/ultrasim-p5-dotnet-8.0.131` supplied the verifier's ASP.NET dependency. Native graphical acceptance used the live display; the canonical S08 gate used Godot headless.

## Accepted implementation and authority boundary

- Settings schema v7 persists a device/User preference: `D2Grid` (default) or `List`. The effective profile is resolved against the authorized container: Unplaced/list-only containers explicitly use List without creating fake Grid coordinates. Live switching keeps the same semantic view and selected row where possible; it does not call an Inventory mutation.
- Game's owner-private UI projection carries typed numeric Grid placement, dimensions, equipment state and a bounded opaque row attachment containing a P4 `SemanticPresentationReference` plus fallback bounds. `Kernels/UI` transports the attachment but has no Inventory or Definition dependency. Game protocol manifest v34/fingerprint `11011385010817065980` includes this bounded wire change; transport revision and existing remote authentication/owner proof are unchanged.
- The Godot viewer renders one rectangle per authoritative footprint (the Iron Sword occupies one 1×3 region), an explicit non-Grid equipped area, and a formal List alternative. List can submit exact Grid-cell `PlaceAt`; D2 and List submit the same Game actions. Remote owner-private Move, Split, Equip, Unequip, placement, stale/foreign intent rejection, save/load generation replacement and reconnect are exercised in native S08. Local and Remote select the same client preference and typed presentation path.
- The first-party Iron Sword has no hand-authored Inventory icon. Its admitted World mesh feeds one reusable 3D staging scene and a bounded cache of 128×128 native images. Pending, unavailable or headless graphics use the primitive item icon. Ordinary rows consume cached images, not one live scene per item. The staging viewport, texture and World3D wrappers retire when the last Inventory view closes; cached ImageTextures retire with the UI renderer. Strong retained-reference `NativeInstance == 0` assertions cover retirement. No Game item ID, persistent Definition identity, content authorization or Inventory save state is derived from the image.
- The exact-edition composition gate now force-restores each edition before `--no-restore` publish. This prevents a shared Godot `project.assets.json` from leaking SinglePlayer references into Online `.deps.json`; the physical export path already force-restored. This is verification/composition correctness, not a product policy change.

## Acceptance and measured fixture observations

| Control | Result |
| --- | --- |
| Focused Release | UI 29/29; Game Inventory 37/37; P1 Control Plane 21/21; Local client-session 24/24; Client Core 36/36; Game replication 181/181; complete Network Application suite passed. |
| Native S08, real graphical display | `S0.8 / I0.11 GODOT ACCEPTANCE PASS`; authenticated remote Inventory, current-state action parity, owner isolation, 5×5 and upgraded 6×5 Grid, save/load and reconnect; generated sword image had over 100 visible pixels; strong native staging/texture retirement assertions passed. |
| Native S08, headless canonical | `S0.8 / I0.11 GODOT ACCEPTANCE PASS`; no GPU portrait is generated, primitive fallback remains visible and semantic/action behavior passes. |
| Graphical 5×5/2-item observation | 1 cold portrait build, 17 cache hits, cold build 34.444 ms **including render-frame wait**, warm lookup 0.320 ms **summed across 17 hits**, 65,536 reserved pixel bytes; List switch 2.986 ms, D2 switch 1.325 ms. These are native fixture observations, not controlled benchmark percentiles or a promised production budget. |
| Architecture | `./verify.sh --architecture-only` passed, including documentation architecture/link checks. |
| Canonical Release on `4f7fc2c5` | `./verify.sh --configuration=Release` passed uninterrupted; SinglePlayer, Online and Complete physical exports, contentless Online, Complete-hosted, native retirement, Direct Connect, bot/impairment and F1.12 product controls passed. Final output: `PASS F0.8_FinalCompositionParity ...` and `PASS repository verification`. |
| Fresh Official-content export/launch | Fresh SinglePlayer export, one cold installed-content launch and five separate-process warm launches: 6/6 exit 0 with the same manifest/activation fingerprints, two actor realizations (one animated, one primitive), five blobs and 9/2 live-pin counts. No SIGSEGV or managed exception in these logs. This is a short regression, not a repetition of the earlier 200-run lifetime campaign. |

Reproduce the principal gates from the repository root with the matching installed SDK/runtime in `DOTNET_ROOT`/`PATH`:

```bash
./verify.sh --architecture-only
./verify.sh --configuration=Release
dotnet run --project _Tests/UltraSim.Game.Inventory.Tests/UltraSim.Game.Inventory.Tests.csproj -c Release
dotnet run --project _Tests/UltraSim.UI.Tests/UltraSim.UI.Tests.csproj -c Release
dotnet build _Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj -c Debug
"/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64" --path _Tests/UltraSim.S0.Godot.Tests --scene res://S08HeadlessAcceptance.tscn
```

The graphical command needs a display; `--headless` validates fallback, not 3D pixel generation. The machine-readable companion records the observed metrics and gate provenance. Transient raw logs were under `/tmp/ultrasim-p6-*.log` and `/tmp/ultrasim-p6-warm.DVuan8/`; those paths are not durable evidence.

## Earlier run stops and limits

Earlier full-run stops are not erased or counted as passing: two invocations encountered the pre-existing timing-sensitive F1.8A impaired-motion assertion (sparse sample had no interpolated frame); one hit the stale v33 manifest-fingerprint test, repaired by `d94a64a2`; one encountered a W7 stale-in-flight count threshold (11 versus the test's bound of 4), with no contradictory/malformed/capacity failure, and the unchanged Network suite later passed standalone; one found the F0.2 `Element_background` gump contract, repaired by `d2aead90`; one found D0.3's stale UI host from early portrait-pump attachment, repaired by `cab7876d`; one found stale edition restore contamination, repaired by `4f7fc2c5`. The final canonical run passed. The F1.8A and W7 test variability was **not** repaired by P6 and remains gate-reliability history, not evidence of an Inventory defect.

Current configured 5×5 and upgraded 6×5 Grid capacities are proven. The core Inventory bounds permit larger theoretical grids, but neither this viewer nor the existing UI row/operation limits prove a pleasant or complete maximum-size 64×64 product; pagination/large-container UX is future work. This closure also does not implement optional live/animated portraits or Examine, final P9 artwork, P7 operator UI, or PF6 validation.
