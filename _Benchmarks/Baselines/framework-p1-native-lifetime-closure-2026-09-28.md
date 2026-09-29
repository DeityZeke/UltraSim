# P1 closure — native lifetime repair acceptance

**Disposition (2026-09-28): P1 Framework Control Plane CLOSED.** The native shutdown failure that held its canonical Release gate is repaired for the known UltraSim failure modes and accepted by the focused native and product verification below. P2 is the next active checkpoint; Gate 1 and PF6 remain ahead. This companion does not rewrite the [original P1 failure evidence](framework-p1-control-plane-2026-09-27.md) or the [repair-candidate report](client-native-lifetime-repair-2026-09-28.md); both accurately describe their earlier states.

## Lineage and mechanism

- Tested clean `main`: `c106a7b6ec9d9b4b825c0a8b507147371f3d4488`, the merge containing production repair `0cd15e6b71a5c90a4c4363d6239fe02110a68ee9`. No tracked source changed during this acceptance run.
- Original failure: intermittent exported Single Player warm Official-content SIGSEGV on the `.NET Finalizer` thread, after Godot C# language/native teardown. Earlier P1 evidence includes both current and unchanged pre-P1 reproductions; representative warm batches passed only 6/10, 9/10, 7/10 and 6/10, depending on control placement. The defect was not caused solely by P1 Control Plane code.
- Causal repair: native-backed presentation wrappers now have explicit strong owners and retirement ordering. The application shutdown coordinator retires owners before Godot teardown and waits across an actual SceneTree deletion-queue boundary, without forced GC, a fixed delay, or exception suppression. Native tests retain strong references and assert the wrappers' `NativeInstance` becomes zero after owner retirement. This addresses the diagnosed finalizer-after-language-teardown mechanism; finite stress cannot establish impossibility of every future native race.
- Runtime: Linux x86-64, AMD Ryzen 7 7800X3D, .NET SDK 8.0.131 / runtime 8.0.31, Godot 4.7.1 Mono. Physical client exports and the canonical verifier ran as separate processes. No affinity was imposed.

## Acceptance results

| Control | Result |
| --- | --- |
| Native ownership/actor-tier and renderer-stress gates | Both passed against real Godot 4.7.1 Mono. |
| Physical exports | Single Player, Online and Complete passed. |
| Immediate startup close | 30/30 passed: 10 per edition, exit 0, ready and shell-pass markers present. The composition-smoke exit requests shutdown during root startup without an artificial delay. |
| Warm Official-content export | One cold cache fill and **50/50** subsequent warm Single Player launches passed: exit 0 and exact F1.4 installed-content pass marker; no native crash. |
| Real window close | 3/3 exported Single Player X11 `WM_DELETE_WINDOW` controls passed, exit 0 after client-ready. An earlier timeout-forced probe was excluded from this result. |
| Architecture verifier | `./verify.sh --architecture-only` passed. |
| First canonical Release attempt | Stopped at Server Hosting because this machine lacked the `Microsoft.AspNetCore.App` shared runtime; this was an environment prerequisite failure, not a product-test result. Its raw log is retained. |
| Focused Server Hosting after environment repair | All 18 tests passed, including the 1,024-cycle Kestrel shutdown stress. |
| Canonical Release rerun | `./verify.sh --configuration=Release` **passed uninterrupted** with a task-local ASP.NET Core 8.0.31 runtime. It included Godot native/export gates, Official cold/warm/absent content, F1.12 product dogfooding, Direct Connect/content acquisition, mixed lifecycle, native Online clients, bot impairment and final composition parity. |
| Repository | `git status --short --branch` returned only `## main...origin/main`; no tracked or untracked worktree changes. |

The task-local runtime was installed under `/tmp/ultrasim-p1-aspnet-runtime`; it was not installed system-wide. Its root uses the host SDK via symlinks so the same private host could both build and run the missing ASP.NET runtime. The failed and passing canonical logs are separate and neither is represented as the other.

The reproducible scripts, per-run ledgers and logs are preserved in [raw acceptance evidence](framework-p1-native-lifetime-closure-2026-09-28.raw.tar.gz) (SHA-256 `764d97f7234db3c0611d637b5b004174f9c255047252badf455273f12c43f4f0`). The [machine-readable companion](framework-p1-native-lifetime-closure-2026-09-28.json) records exact run counts and log hashes.

## Disposition

P1's previously accepted focused, architecture and integrated Control Plane results remain valid. Its missing canonical Release condition is now satisfied on the merged repair tree. **P1 CLOSED; native-lifetime repair ACCEPTED; P2 ACTIVE/NEXT.** P2–P11 and Gate 1 are not complete. PF6 and many-player live testing remain **NOT READY / NOT STARTED**. W8/W9/W10 remain unstarted.
