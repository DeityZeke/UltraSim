# P3 Framework adapter/runtime boundary — closure evidence

**Disposition:** P3 **CLOSED** on tested implementation `9134f6d9bafd5708e7ceeaa70005e7bf60f9d95d`. P4 is next; PF6 is **NOT READY / NOT STARTED**. This is a selective boundary checkpoint, not a new platform framework or a claim that audio and later Engine-definition work are complete.

## Lineage and scope

- Clean starting `main`: `4bbd26a24bc3b9c55c3a0d0e90a3f848eb9d83d4` (P2 closure).
- Production/architecture/test implementation: `cd2aebb273c55998e9158be69846cbb80f4a9896`.
- Test-only diagnostic context for an intermittent pre-existing motion assertion: `9134f6d9bafd5708e7ceeaa70005e7bf60f9d95d`. It does not relax the assertion or change product motion.
- Canonical Release passed on that exact latter commit. This later evidence/docs commit does not change production or tests; its SHA is available from this file's Git history.
- Linux 7.2.6-1-cachyos x86-64; Ryzen 7 7800X3D; local .NET SDK 8.0.125 via `/home/nathan/.dotnet` and Godot 4.7.1 Mono; Release. No CPU affinity or isolation was asserted.

## Boundary audit and implementation

The authoritative Game, Framework, Kernels, Server and Bridges projects, and Client Core/Local/Network, had no direct Godot source or project dependency. Existing Godot input capture, world realization and native application lifecycle are already in the Godot client adapter; migrating them behind a single `IPlatformAdapter` would add indirection without removing an actual coupling. P3 therefore retained their ownership and added no generic platform interface.

The Godot client now registers a cold, read-only `client.godot.adapters` Control Plane provider after the persistent shell roots exist and retires its exact generation before UI composition retirement. It publishes the compiled presentation/input/runtime selection as `godot`, audio as `none` (P8 remains unimplemented), and bounded owner-thread diagnostics for edition, lifecycle phase, session presence and native root/layer counts. Implementation selection is not a live setting; ordinary preferences such as vegetation remain under their settings owner. No Game authority or session behavior is routed through this provider.

The architecture verifier now rejects Godot SDK/package/reference use outside the Godot project and direct Godot imports in the headless/engine-neutral source roots. Exact export gates admit the intentionally added `UltraSim.Framework.ControlPlane` contract assembly in Single Player, Online and Complete while still rejecting opt-in management proof assemblies. A real `ServerOnly` Direct Connect publish succeeded with RuntimeHost/ControlPlane present and Godot/client/render/input/audio assemblies absent from its dependency graph. This proves the current dedicated composition is headless without inventing a required audio adapter.

## Verification and adverse attempts

| Control | Result |
| --- | --- |
| Godot native test build, Release and optimized Debug | Pass; 0 errors/warnings. |
| Native `F110ActorTierAcceptance.tscn` with adapter provider/lifetime assertions | Pass. Read authorization, fixed selections, absent audio, revision changes, retirement and stale-generation rejection covered. |
| Godot client Release/Debug builds and actual Complete `--composition-smoke` | Pass; 0 errors/warnings. |
| Framework Control Plane Release tests | 19/19 pass. |
| `dotnet publish Server/DirectConnect/UltraSim.Server.DirectConnect.csproj -c Release -p:UltraSimNetworkFlavor=ServerOnly --output .output/p3-headless-probe` | Pass; `.deps.json` excludes `GodotSharp`, Godot client, Client.Local and Client.Network. |
| `env PATH="/home/nathan/.dotnet:$PATH" bash ./verify.sh --architecture-only` | Pass. |
| First and second canonical Release attempts on `cd2aebb2` | Failed at existing `F18aPresentedMotionPrecedesImpairedOwnerState` timing assertion in `F03bRemotePlayableTests.cs:398`. The same focused case passed twice in separate processes on the unchanged tree. Cause was not established. |
| Third canonical Release attempt, production unchanged, with diagnostic-only assertion context not yet committed | Passed the motion gate, then failed the existing `WorldPackBoundsRejectBeforeProportionalAllocation` warmed-allocation assertion with 12,336 bytes. The complete World suite passed 115/115 standalone unchanged; the full Network Application suite passed three independent runs. The diagnostic patch was then committed separately. These observations do not establish the source of either intermittent failure. |
| Fourth `env PATH="/home/nathan/.dotnet:$PATH" bash ./verify.sh --configuration=Release`, on committed `9134f6d9` | **PASS uninterrupted, exit 0**. This included architecture/integrated suites, native Godot tests, ServerOnly Direct Connect, all three physical exports and exact assembly gates, Official installed/absent/primitive content, F1.12 exported dogfood, Online/contentless and native-bot impairment, mixed lifecycle, final composition parity and `git diff --check`. Scratch: `/tmp/ultrasim-repository-verification/459091`. |

The earlier failed attempts remain part of the P3 verification record. They were not made green by weakening acceptance criteria. The final pass supports P3 closure, not a claim that unrelated timing-sensitive test behavior has been causally repaired. Console output was not retained as a separate raw log; the command, exact tested SHA, failures and final result are recorded here.

## Deliberate limits

Audio implementation remains P8. Typed definitions, facets and first-party Game-definition dogfooding remain P4. Polished user/admin/developer controls remain P5, and the Server Operator Product remains P7. No hot-swap, live adapter selection, Game/Network protocol change, new rendering backend, or PF6 work occurred. Headless composition is demonstrated by the product publish and guarded by source/project checks; the Godot client deliberately adds only its own adapter and cold diagnostics.
