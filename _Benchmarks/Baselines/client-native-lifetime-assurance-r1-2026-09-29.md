# Godot/.NET native shutdown-lifetime assurance — R1

**Disposition:** the current product blocker is CLOSED for the reproduced UltraSim failure modes. P1 and P4 remain architecturally CLOSED. P5 is safe to begin; P5 and PF6 have not started. This companion does not amend the historical [P1 acceptance](framework-p1-native-lifetime-closure-2026-09-28.md) or [P4 adverse evidence](framework-p4-typed-definitions-2026-09-29.md).

## Provenance and matched controls

- Starting clean `main`: `ac4e61938e994eddb63ee1bb17ee004e3215d117`. Repaired/tested production tree: `f46ee3bd8aff14a9516781d49d147a261dcacbb2`.
- Linux 7.2.6-1-cachyos x86-64, Ryzen 7 7800X3D, .NET 8.0.31 (SDK 8.0.131), exact Godot 4.7.1 Mono `a13da4feb`. All controls used separately exported Single Player processes, the same current warm Official-content cache, `--f1-4-official-smoke=installed`, and an 80-second per-process bound. The Official package hash was `03f77dd361bdf7c9edf564fc92c95ced31aa71378a5d14555cc670b1cda6c554` in the compared exports. No affinity was imposed. The native executable SHA-256 was `9d2552734fbd46677f76187537bd0a3833ceaf451812a9938d549cd2be453478` for every captured crash/export checked.
- Each historical row below was exported afresh from its exact detached source checkout. A row stops at its first failure; a 10/10 pass is a bounded negative sample, not evidence of absence. The original P4 evidence additionally retained pre-P4 1/10, P4 1/10, and a canonical warm-stage SIGSEGV. The earlier P1 50/50 pass was real finite acceptance, but did not cover the residual race.

| Source checkpoint | Matched warm result | Finalizer object in full dump |
| --- | ---: | --- |
| P1 accepted `c106a7b6` | 7 pass, run 8 exit 139 (PID 698107) | `Godot.Image` |
| P2 closure `4bbd26a2` | 10/10 pass | no dump |
| P3 tested `9134f6d9` | 4 pass, run 5 exit 139 (PID 702420) | `Godot.Animation` |
| pre-P4 `46dfbf16` | 3 pass, run 4 exit 139 (PID 704178) | `Godot.ViewportTexture` |
| P4 tested `137289c9` | 2 pass, run 3 exit 139 (PID 706077) | `Godot.ViewportTexture` |
| first repair candidate | 4 pass, run 5 exit 139 (PID 672540) | `Godot.StyleBoxFlat` |
| second repair candidate | 11 pass, run 12 exit 139 (PID 677032) | `Godot.Animation` |
| final repair `f46ee3bd` | **200/200 pass** | none |

A retained P1-era export also crashed on `Godot.ArrayMesh` (PID 664066); a retained P4 export crashed on `Godot.ViewportTexture` (PID 663342). These are supplementary controls; the fresh exact-SHA rows above establish lineage. P1's fresh failure proves P2–P4 did not introduce the underlying finalizer hazard. P2's 10/10 is sampling variance, not a fixing boundary.

## Native/managed failure path and repair

All eight full dumps retained for this investigation show the same `.NET Finalizer → GodotObject.Dispose(false) → native Godot +0xb55d75 → pthread_mutex_lock` family. SOS identifies the concrete managed object in each dump; GDB's complete thread stacks are in the raw archive. For a representative exact P4 crash, `rdi=0x170`, `r15=0`, and the native singleton pointer loaded at `0x4a76a38` is zero. Disassembly shows `+0xb55d75` immediately after a mutex lock formed from that pointer plus `0x170`. The official Godot revision's [managed finalizer](https://github.com/godotengine/godot/blob/a13da4feb/modules/mono/glue/GodotSharp/GodotSharp/Core/GodotObject.base.cs#L1060-L1148) calls the [associated-GC-handle interop](https://github.com/godotengine/godot/blob/a13da4feb/modules/mono/glue/runtime_interop.cpp#L2364-L2388), which consults a C# language instance binding; its [singleton and binding mutex](https://github.com/godotengine/godot/blob/a13da4feb/modules/mono/csharp_script.h#L1943-L2015) are language-lifetime state. The production binary has no DWARF symbol for this offset, so the function/source identification is **source-correlated by call path, disassembly, and registers**, not claimed as direct symbol resolution. The immediate native null-mutex crash is a Godot teardown hazard; UltraSim was allowing application-reachable wrapper classes to reach that finalizer after teardown.

SOS `finalizequeue -stat` was inspected for every captured full dump. Ready-for-finalization totals varied from 70 (fresh P1 exact-source crash) to 45,681 (retained P4 export); these totals include many Godot collection/string wrappers and are not a count of unsafe application owners. The per-dump queue statistics and exact crashing `GodotObject` fields are retained separately in the raw archive.

The repair retains and explicitly retires the `SubViewport.GetTexture()` wrapper with the UI composition, GLTF-imported mesh wrappers with their content template until visual consumers retire, and the tooltip's `StyleBoxFlat` with its panel. Temporary `Image`, `AnimationPlayer.GetAnimation()` and root-motion `NodePath` wrappers are disposed in their owning Godot-thread scopes. The gameplay-output capture now reads the already-owned texture rather than creating another wrapper. No sleep, forced GC, finalizer drain, exception suppression, Official-content disablement, or engine patch was adopted. The existing two-frame Godot deletion-queue shutdown boundary remains.

Native assertions hold strong references and require `NativeInstance == IntPtr.Zero` after owner retirement for imported mesh, gameplay texture, tooltip style, and animation lookup wrappers. Removing mesh/texture retirement made their native tests fail in development; removing tooltip/animation retirement made F1.10/T0.10 fail, respectively. The restored source passes all four focused native gates. This is causal ownership evidence plus the previously hostile whole-product stress, not a claim that no future Godot wrapper can ever escape.

## Acceptance

| Gate | Result on repaired tree |
| --- | --- |
| S0.8 native UI/composition, T0.10 native content/actor, F1.10 native actor tier, F1.11 native renderer stress | All pass; optimized Debug native build has 0 warnings/errors |
| Physical exports | SinglePlayer, Online, Complete pass |
| Immediate startup close | 30/30 pass, 10 per edition, ready and shell markers present |
| Deliberate startup edition mismatch | Expected exit 1 with controlled startup error; no native crash |
| Real X11 `WM_DELETE_WINDOW` | 3/3 pass, ready marker and exit 0 |
| Warm Official-content export | **200/200 pass**, exact marker and exit 0, no retry or discarded tail |
| `./verify.sh --architecture-only` | Pass |
| `./verify.sh --configuration=Release` | **One uninterrupted pass** on `f46ee3bd`; actual exports, installed/absent art, F1.12 dogfooding, mixed lifecycle, native Online/bot controls, and final composition parity pass |

The canonical verifier used a task-local matching ASP.NET Core 8.0.31 runtime because the system runtime was absent; no product source workaround or system-wide installation was made. Its final line is `PASS repository verification`. The [machine-readable summary](client-native-lifetime-assurance-r1-2026-09-29.json) and [raw logs, per-run ledgers, SOS and complete GDB stacks](client-native-lifetime-assurance-r1-2026-09-29.raw.tar.gz) (SHA-256 `df58bb16caced3908fb06efb9547780e2fc8277e3dc0055d068894a4d5f5312d`) are committed. Full process dumps and exact export binaries are retained outside Git under `.output/native-lifetime-assurance-2026-09-29/`; dumps contain process memory and must not be published as ordinary artifacts.

**Decision:** native-lifetime blocker CLOSED for the known reproduced paths. P5 may begin as a separate checkpoint. This does not itself start P5, Gate 1, or PF6. Any newly observed finalizer crash requires its own dump/type attribution rather than weakening these gates.
