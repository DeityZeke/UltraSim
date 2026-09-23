# F1.8A-R1 — motion and camera corrective evidence (2026-09-23)

Starting HEAD: `78b47d5b9e832c454fbca36fb06a665915a25159`.
Corrected production/test commit: `21764ff3f4e6fb5fd9baf846af1e9e6dac9fd92a`.
This artifact extends, rather than rewrites, the original
[F1.8A evidence](client-f1.8a-remote-motion-presentation-2026-09-23.md).
Game manifest v32, Network transport revision 2, authority, AOI, proof, and publication
policies are unchanged. F1.9/F1.10 were not started.

## Reproductions and repairs

| Finding | Before repair | Corrected behavior and regression |
|---|---|---|
| Idle controlled owner displacement | With no prediction or pending command, authority moved X 0→600 mm; presented X stayed 0 mm indefinitely. The new Core assertion failed on the old code with `Expected '600', got '0'`. | Authority consumes only an existing *aligned* speculative lead. Otherwise the presented pose reconciles to new authority; at 200 ms the Core and native actor/camera assertions resolve to 600 mm. |
| Blocked, then resumed authority movement | After suppression, authority moved to X=600 mm while presentation remained at 0; a same-direction held command could remain suppressed. | Fresh unblocked horizontal authority movement releases suppression; the old residual cannot cancel the new owner displacement. Core tests require X=600 mm after settling and a subsequently admitted same-direction command to predict again. |
| Vertical public teleport | A 10 m Y-only relocation produced Y=5 m as `InterpolatedRemote`, with zero teleport resets. | The discontinuity check covers 3D distance. The same input resolves at Y=10 m as `DiscontinuitySnap`, with one reset. Controlled vertical relocation also resets lead and snaps without duplicating Game Physics. |
| Moving public actor picking | The pointer intersected the latest authoritative record position even when Godot displayed an interpolated actor elsewhere. | The presenter exposes its exact assigned pose by public identity. The remote picker still enumerates only disclosed semantic rows and still nominates the same ID, but intersects at the displayed pose. A native woodland-animal ray hits X=1.5 m and misses its old X=1.0 m bounds. Game retains final range/interaction authority. |
| Accepted camera settings | Orthographic stepped zoom changed distance but not native orthographic size; actor metadata overwrote the accepted profile pivot every frame. | Stepped zoom scales the orthographic lens by current/default bounded distance. The resolved camera profile exclusively owns target pivot height; actor metadata no longer overrides it. Native camera tests cover zoom in/out, a 2.3 m profile pivot, aspect/crop, and pointer rays. |

The profile-pivot precedence is deliberate: an accepted client profile describes the realized
camera. Actor presentation anchor metadata remains available, but is not an implicit override of
that profile. The separate F1.6 Local/Remote world Texture/Material-consumer gap remains deferred;
it is not a motion repair.

## Real UDP motion-quality control

The unchanged external authenticated-UDP proxy ran zero delay, 50 ms one-way delay, that delay
with ±15 ms deterministic jitter, and the same profile dropping every 53rd datagram. Values below
are one representative wall-clock run after the repair, not a latency distribution or photon test.
`Error at first owner` and `error at Stop ACK` are presented-versus-exact-owner horizontal error;
settling is from Stop acknowledgement to within 20 mm of that owner pose.

| One-way/jitter/drop | Input→model presentation | Input→owner | Error at first owner | Error at Stop ACK | Settle to 20 mm | Max correction |
|---|---:|---:|---:|---:|---:|---:|
| 0/0/none | 4.517 ms | 26.342 ms | 57 mm | 47 mm | 102.785 ms | 48 mm |
| 50/0/none | 4.265 ms | 125.237 ms | 666 mm | 1,146 mm | 178.743 ms | 1,147 mm |
| 50/15/none | 4.275 ms | 129.480 ms | 692 mm | 257 mm | 166.523 ms | 258 mm |
| 50/15/every 53rd | 4.246 ms | 165.410 ms | 800 mm | 78 mm | 133.517 ms | 79 mm |

All four finished at the exact owner X/Z pose after finite correction; the fixed-delay case
retained local-before-owner reversal and Destination response. The 1.15 m rapid-transition tail
in this run, the original 1.4–1.6 m tails, and a later 1.67 m diagnostic tail are **not** proof of
imperceptible presentation. The finite 180 ms correction and sparse public-sample holds need
direct human-feel inspection in F1.12. No claim of vertical prediction is made.

## Verification and lineage qualification

- `dotnet run --project _Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj -c Release`:
  34/34 passed, including the new convergence and vertical-teleport controls; zero warmed managed
  allocation in the bounded temporal update remains tested.
- `dotnet run --project _Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj -c Release`:
  passed, including real authenticated UDP delay/jitter/loss, exact Stop convergence, and the new
  presented-versus-owner error/settling measurements.
- Godot 4.7.1 Mono S0.8 and T0.10 headless scenes: passed, including native actor/camera
  convergence, visible pick geometry, orthographic zoom/pivot, woodland source replacement,
  and equipment/nameplate/overhead attachment.
- `./verify.sh --architecture-only`: passed.
- `./verify.sh --configuration=Release`: **passed uninterrupted on the corrected production
  commit**, including SinglePlayer/Online/Complete physical exports, Contentless Online,
  PF1/PF2 exported mixed swarms, F0.7, F1.0 guardrails, and all repository gates.

The previous evidence-only F1.8A HEAD (`78b47d5`) had a production-commit full pass but three
later full-run failures: PF2 mixed-swarm execution, PF1 all-participants readiness timeout,
and PF1 sword-workflow conservation. Focused PF1 and PF2 reruns passed on that unchanged tree.
The cause of those intermittent failures was **not established**; this report neither deletes
them from history nor claims the correction fixed them. A new uninterrupted full pass on
`21764ff3` is the current canonical closure evidence.

Host: CachyOS Linux 7.2.6, Ryzen 7 7800X3D, 30 GiB RAM, .NET SDK 8.0.131/runtime 8.0.31,
Godot 4.7.1 Mono. Runs were serialized. Corrective source adds no protocol fields, native actor
nodes, per-frame allocation, Game authority, or off-thread work.
