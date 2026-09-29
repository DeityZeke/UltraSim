# Native ownership and application retirement candidate

**Status: implemented candidate; native crash closure and P1 acceptance remain pending. P2 remains held.**

Base: `edd427b1671702c4a119970b068d97a1b2edd317`. Godot source reference: `a13da4feb`. This change contains application repairs; it does not patch Godot's finalizer registry or claim an engine-wide finalizer barrier.

## Ownership changes

- Both geometry helpers scope their temporary SurfaceTool on return and exception. Their returned ArrayMesh remains caller-owned.
- Every renderer chunk retains all of its base/additional terrain mesh wrappers in an explicit owner. Node consumers retire before those wrappers are disposed. Failed mesh construction disposes unpublished meshes, and temporary native arrays are scoped.
- Detailed actor primitive meshes, weapon/cue meshes, and their exclusive materials have strong resource owners associated with their presentation subtrees. Replacement, demotion, removal, and presenter disposal retire consumers before wrappers. Reparenting does not transfer or lose those owners. Imported/shared content is not indiscriminately disposed by the actor owner.
- Primitive batches use the same ownership transfer, including partial-construction cleanup. The added-content badge retains its own mesh while borrowing its resolver-owned material.
- Resource owners dispose in reverse acquisition order, reject duplicate ownership and additions during retirement, retain partial-failure references, aggregate cleanup failures, and enforce the Godot owner thread.

## Small lifecycle coordinator

The coordinator contains one state enum and one shutdown-completion task. There is no registration-based event sink, dynamic shutdown-handler ordering, fixed sleep, forced GC, or catch that converts unexpected cleanup failure into success.

Normal phases are `Created -> Starting -> Running -> StopRequested -> Stopping -> Stopped`; shutdown during startup bypasses Running. Cleanup failure produces Faulted. Running refers to the application root; existing session/loading state remains owned by the shell/session.

The canonical ClientHost source owns native `_EnterTree`, `_Ready`, `_Notification`, `_Process`, and `_ExitTree` dispatch. Window-close, menu/key exits, startup failures, and existing application smoke exits request the same shutdown operation. Startup completion cannot overwrite a pending close. Duplicate close requests coalesce; a failure exit code is not overwritten by a successful close request.

The operation disables inherited application processing and gates deferred menu commands, then defers retirement until the current native callback unwinds. It retires the active session, remaining product services, and persistent UI in a fixed, inspectable order. All three cleanup phases are attempted and failures remain observable through ShutdownCompletion and error output. On cleanup failure, the engine is not automatically quit beneath unretired owners; an external supervisor may still terminate the failed process. This is a faulted shutdown, not a successful bounded exit.

Successful cleanup observes two SceneTree process-frame boundaries before publishing Stopped and calling Quit. The first signal could belong to the same iteration in which deferred cleanup began; the second ensures an intervening end-of-frame deletion-queue flush. This follows the inspected `scene_tree.cpp` sequence at a13da4feb: process_frame precedes idle processing, while `_flush_delete_queue` occurs near the iteration's end. **This is a native deferred-free boundary, not a .NET finalizer drain.**

External tree destruction bypassing the owned exit path performs fallback cleanup and reports premature exit through the completion task. The lifecycle state is owner-thread-bound; it is not a cross-thread event-dispatch facility. Future background startup/resource jobs must be explicitly cancelled and joined by their owner before that owner's retirement returns. Existing async diagnostic routines are not converted into a general managed task scheduler by this change.

## Regression coverage and results

Executed here with .NET 8.0.31 / SDK 8.0.425:

| Check | Result and scope |
| --- | --- |
| Lifetime and ownership controls | 7/7 passed; compile actual coordinator, retirement orchestration, and resource owner against deterministic Godot doubles |
| Actor resource controls | 3/3 passed; production factory/retirement methods extracted verbatim, including reparenting and a failure after the first weapon mesh commits; Godot doubles |
| Geometry helper controls | 10/10 passed; actual helper source, success plus four injected failure boundaries for each factory; Godot doubles |
| C# syntax | All 37 application C# files parsed in SinglePlayer, Online, and Complete configurations with no syntax errors |
| Base integrity | All 15 modified pre-existing C# files matched their exact GitHub blob at the base commit |

Native regressions were added to the existing F1.10 and F1.11 Godot gates. They hold strong references to actor weapon/cue meshes across demotion and to terrain meshes across renderer disposal, then require `NativeInstance == IntPtr.Zero`. These assertions expose the old missing disposal without relying on collection timing or renderer counters. The native gate also checks primitive resource ownership and startup-close state transitions.

**Not executed here:** a full Godot application build/source-generator run, native F1.10/F1.11 gates, exported warm launches, and canonical Release verification. The environment has no Godot executable or GodotSharp assembly. Syntax checks and engine doubles are not substitutes for those gates.

The managed controls can be repeated without package restore:

```bash
python3 _Benchmarks/Baselines/client-native-lifetime-repair-2026-09-28.raw/probes/run.py \
  --repo . --dotnet-root /path/to/dotnet-sdk-8
```

The runner writes into a new process-specific `.output` directory by default. Recorded logs are adjacent to this report under `.raw/`. Cleanup-failure stack traces in `LifetimeProbe.log` are expected injected controls; the probe exits successfully only after asserting both failures remain observable and engine Quit was withheld.

## Acceptance still required

Build all three physical products on the pinned Godot runtime, run the new native assertions and existing integration/retirement gates, then repeat the matched warm Official-content launch/exit campaign with flushed lifecycle markers and preserved process exit codes. Finish with an uninterrupted canonical Release run on the same implementation revision. Include normal interactive close and close during startup; do not add a timing delay to make rapid-close controls pass.

The engine's short-weak-reference shutdown sweep can still miss queued finalizers. The application changes reduce known sources of abandoned wrappers but do not prove complete coverage of imported, implicit, or engine-created wrappers. No crash dump was read, copied, or included. A remaining native crash requires tracing that wrapper and the engine lifetime barrier rather than marking these managed passes as production closure.
