# Godot / .NET Native Shutdown Lifetime Assurance

Status: CANONICAL evidence history; ACTIVE product blocker / assurance reopened

Primary references:
- [P1 native-lifetime closure evidence](../../_Benchmarks/Baselines/framework-p1-native-lifetime-closure-2026-09-28.md)
- [Original native-lifetime repair candidate](../../_Benchmarks/Baselines/client-native-lifetime-repair-2026-09-28.md)
- [P4 evidence / reproduced warm-exit finding](../../_Benchmarks/Baselines/framework-p4-typed-definitions-2026-09-29.md)

Related:
- [Godot adapter / headless boundary](../TOPICS.md)
- [Framework runtime / ownership vocabulary](framework-runtime-packages-services-extensions.md)

## Historical repair

The original exported Single Player failure was an intermittent SIGSEGV on the .NET Finalizer thread after Godot C# language/native teardown. Full dumps identified GodotObject finalization of native-backed wrappers including ArrayMesh and SurfaceTool and the native path through the Godot C# language singleton.

The P1 repair did not use sleep, exception suppression, or a forced-GC/finalizer drain as production behavior. It introduced explicit strong ownership and deterministic retirement for known native presentation resources, scoped temporary native helpers, and a Godot application shutdown coordinator that retires owners before engine teardown and observes two SceneTree process-frame boundaries so an intervening deletion-queue flush occurs.

Acceptance on that tree included 30/30 rapid startup-close, 50/50 warm Official-content launches, 3/3 real window closes, native lifetime regression tests, and an uninterrupted canonical Release verifier. That evidence justified closure for the known failure modes, but the closure record explicitly noted that finite stress cannot prove every future native race impossible.

## Reopened 2026-09-29 finding

During P4 verification, the same native failure family reappeared independently of P4 source:

- one warm Official-content SIGSEGV in 10 launches on the exact pre-P4 starting export;
- one in 10 on a P4 export;
- an additional canonical P4 attempt failed in the same warm stage;
- dumps again show .NET Finalizer, pthread_mutex_lock, and Godot executable offset +0xb55d75, matching the historically observed native offset family.

This proves the current product still has a native shutdown-lifetime defect. It does **not** yet prove which finalizable wrapper is escaping retirement or whether the cause is:

1. a residual engine/finalizer race present even on the original P1 closure tree;
2. a later native-wrapper ownership regression introduced after P1 closure;
3. another wrapper/path that converges on the same Godot teardown failure.

P4 remains closed because the pre-P4 control reproduces the defect. PF6 native-lifetime assurance is reopened.

## Investigation discipline

Do not repair this by:

- sleeping before Quit;
- swallowing native/managed disposal failures;
- weakening warm Official-content verification;
- forced GC/finalizer draining as the production mechanism;
- simply raising retry counts until a batch happens to pass.

A diagnostic finalizer drain may be used only as a causal control, as before.

The next investigation should compare the original accepted P1 closure tree against later P2/P3/pre-P4 checkpoints under the same runtime/export conditions. If the original closure tree now reproduces, treat the old 50/50 campaign as finite evidence that missed a residual race. If it remains clean while a later checkpoint reproduces, bisect the ownership regression.

Resolve the current crash to the exact managed finalizer/native wrapper and native source path where possible. A production fix should restore deterministic owner retirement or establish a proven engine-level shutdown barrier for wrappers the application cannot own safely.

## Acceptance expectation

Closure should require causal evidence, not only a lucky stress batch:

- identify the escaped wrapper/finalizer family or equivalent engine lifetime defect;
- add a targeted regression that proves deterministic native retirement/fencing;
- preserve strong references where useful and assert NativeInstance retirement for application-owned wrappers;
- prove cleanup ordering on all exit routes;
- rerun warm Official-content stress substantially beyond the previous small reproduction batch;
- finish with one uninterrupted canonical Release verifier on the exact fixed tree.

Keep the historical failed and successful campaigns intact.
