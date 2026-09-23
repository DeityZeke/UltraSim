# F1.8A — remote motion presentation (2026-09-23)

Production implementation: `1a3728cf253fe50e620f876f22d24fa3d8eb7b07`, starting from
`2de63deab62c387445d5cc190af508afd37ea4e8`. The latter includes two documentation/art
commits after the request's expected `cf6160b8`; neither changed motion ownership. Source, tests,
and evidence are distinct commits. The final evidence commit is recorded in Git history. Game
manifest remains v32 and Network transport revision 2; no Game, Network, AOI, cadence, authority,
or Local-play semantics changed.

## Ownership and policy

`RemotePlayableClient.Position` and public semantic records remain exact Game-derived truth.
`ClientMotionPose` is a separate engine-neutral global-millimeter presentation value. The remote
Godot client feeds a controlled predictor only after the existing focus/profile/scheduler and
`RemotePlayableClient.TrySubmitCommand` admit a sequenced semantic command. Its fixed 128-entry
history is pruned with the existing exact `LastProcessedCommandSequence`; application, control,
server-generation, and public-actor identity fence it. The stock local presentation policy is a
6,000 mm/s horizontal approximation capped to 900 mm of speculative lead and 200 ms of unobserved
time per update. This is **not** a Game movement entitlement or authenticated server speed hint.
Game alone determines collision, jump/fall/landing, interactions, and final position. Horizontal
Direct and Destination movement, Stop, and reversal are predicted; Y and orientation remain
authority-driven. The 20 mm deadband, 180 ms finite correction, and >4 m snap/reset are explicit.
There is no shadow Game Physics simulation.

The controlled root (including attached hand/equipment, nameplate, and overhead Chat) and camera
follow the presented pose. The structural render window, disclosed semantic cache, pointer world
origin, and Game range validation continue using the authoritative anchor. The pointer camera ray
already contains the presentation offset, so adding it again to the world origin would be wrong.
Local Single Player does not instantiate the remote temporal controllers.

Non-controlled actors use exact `WorldPublicRecord.EntityPublicVersion` and public identity plus
client monotonic **receipt time**; no server simulation timestamp is inferred. Each of at most 64
actors has 12 authoritative samples, a 100 ms delay, no extrapolation, a 500 ms incoming-gap
reset, and a >4 m teleport reset. Missing data holds the newest sample. Removal, index/generation
replacement, and source-generation replacement retire old samples. The first implementation used
four samples; a real-UDP control proved that at near-60 Hz it discarded the pair needed for the
100 ms presentation timestamp. The permanent high-cadence regression and current 12-slot ring
cover that case. No per-actor Godot Tween or per-frame semantic `Updated` operation is created.

## Real authenticated UDP control

The existing `F03bRemotePlayableTests` hosted-UDP fixture was routed through a test-only bounded
external UDP proxy (512 datagrams maximum, 1,472-byte receive buffer). The proxy delays both
directions without interpreting Game or Network packets. Parameters are one-way delay, bounded
deterministic jitter, and an optional per-direction every-53rd-datagram drop. These are real wall
delays; OS scheduling makes elapsed observations variable between runs. The test samples the exact
owner snapshot and a public actor record through the real client cache. The public sample is the
controlled actor's public row treated as a *surrogate non-controlled observer* for this fixture;
the native T0.10 test separately exercises the actual woodland actor. A 20 mm displacement defines
first model-presented motion and first owner motion. Authority-first presentation would first move
at the latter time. These are model-pose times, not measured display photon latency.

One representative Release run after the implementation commit (microseconds):

| Proxy one-way / jitter / drop | Input→present | Input→owner | Owner updates | Max lead | Smooth / snap | Max correction | Public samples / interpolated / held |
|---|---:|---:|---:|---:|---:|---:|---:|
| 0 / 0 / none | 4,511 | 8,482 | 91 | 76 mm | 1 / 0 | 38 mm | 91 / 647 / 45 |
| 50 / 0 / none | 4,272 | 131,647 | 92 | 900 mm | 15 / 0 | 1,437 mm | 18 / 147 / 584 |
| 50 / 15 / none | 4,238 | 148,301 | 80 | 900 mm | 1 / 0 | 112 mm | 6 / 103 / 675 |
| 50 / 15 / every 53rd | 4,265 | 137,780 | 70 | 900 mm | 1 / 0 | 39 mm | 6 / 67 / 705 |

The final row physically dropped 12 datagrams. All four retained exact owner convergence after
Stop; the fixed-delay row additionally proved reversal and Destination presentation before the
next owner result. The fixed-delay correction high-water occurs during the rapid reversal→Stop
sequence, not the uninterrupted forward movement window (whose correction maximum was 0 mm in
that run). Its correction remained finite and smoothed, but a 1.4 m rapid-transition tail is a
human-feel risk for F1.12; this evidence does **not** prove it imperceptible. Under impaired sparse
public updates, hold frames dominate and a 500 ms gap may reset the buffer. We explicitly do not
claim interpolation can manufacture missing authoritative samples. A second run on the same
production code had a fixed-delay maximum correction of 1,576 mm; see the JSON raw rows.

## Native, semantic, resource, and verification controls

- The T0.10 native acceptance moved a predicted controlled root 600 mm and the camera/hand/
  nameplate/overhead anchors with it, then applied an owner correction without replacing the
  semantic record. It presented a non-controlled root at 1,500 mm between exact 1,000/2,000 mm
  samples. The actual replicated woodland actor also produced an intermediate pose and retired
  its temporal state on destruction and source-generation replacement.
- Core tests cover history capacity, acknowledgement pruning, stale/duplicate owners, delayed
  pre-ack packets, Stop/reversal/Destination, wall/block suppression, deadband, finite correction,
  discontinuity, identity resets, yaw wrap, one/two samples, high-cadence delayed bracketing,
  gap/teleport/ABA resets, no extrapolation, and time-step invariance.
- The bounded array payload reservations are 5,120 bytes of controlled command entries,
  36,864 bytes of public pose samples, and 1,280 bytes of actor indices: 43,264 bytes before
  managed object headers and the separate 128-record Godot adapter scratch. A warmed Core run
  sampling all 64 remote actors for 1,000 frames took 57.9 ms total (about 57.9 µs/frame on this
  host) and allocated zero managed bytes; this is a Core micro-control, not full-client CPU.
  No extra native node is created per motion sample. The canonical F1.0 census and F0.7 clean
  mixed lifecycle remained green; persistent content pins are unaffected.
- `./verify.sh --architecture-only` passed. `./verify.sh --configuration=Release` passed
  uninterrupted on `1a3728cf`, including Client Core, Network application, T0.10 native,
  SinglePlayer/Online/Complete physical exports, native/bot impairment, Contentless Online,
  F1.7 UI/camera-related native paths, F0.7 mixed lifecycle, and F1.0 guardrails. The final
  test/report-only evidence extension is reverified separately at closure.

Host: CachyOS Linux 7.2.6, AMD Ryzen 7 7800X3D (8 cores/16 logical CPUs), 30 GiB RAM,
.NET SDK 8.0.131 / runtime 8.0.31, Godot 4.7.1 Mono. Tests were separate single-process
controls without concurrent benchmark jobs. No F1.9 SubViewport/scalable renderer or F1.10
actor-tier work was started.

Open human-feel questions: rapid reversal/Stop smoothing at the 1.4–1.6 m correction tail,
intermittent holds under sparse impaired public samples, and stock-speed-hint drift if Game's
authoritative locomotion rate later changes. These are observed presentation-quality limits,
not changes to authority/proof correctness. F1.12 dogfooding should inspect them directly.
