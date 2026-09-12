# PF3 — Cadence and authenticated server performance policy

**Status:** implemented; permanent long-window evidence is recorded in
`_Benchmarks/Baselines/concept-demo-pf3-cadence-performance-policy-2026-09-12.json`.

PF3 separates six clocks which were previously easy to mistake for one another:

```text
visible/headless render loop
physical/semantic input sampling
stock-client command submission
authoritative simulation
Hosted Network service
owner/public semantic delivery
```

The checkpoint does not change OwnerState cadence, ChunkPublication cadence, AOI, packet
aggregation, compression, the 64-participant Concept Demo bound, or the PF2/PF2.5 accounting
definitions. Those remain PF4/PF5 work.

## ServerPerformanceProfile/v1

Every current remote Concept Demo application lifetime receives one finite profile before content
authorization or guest admission. The profile contains:

- revision and canonical SHA-256 identity;
- authoritative simulation frequency;
- maximum Hosted Network service interval;
- maximum and recommended per-client input frequencies;
- finite input burst capacity and window;
- current informational OwnerState and Immediate/Near/Far public delivery expectations;
- a bounded interpolation-buffer recommendation.

It contains no executable policy, bindings, camera preference, render-FPS requirement, secret
capacity, or packet-arrival promise. The secure deployment-authenticated application session
delivers the profile. The client canonicalizes it, requires the exact hash, automatically applies
it without user consent, and retains an exact application proof. The server gates content and
admission on that proof. A reconnect gets a fresh application epoch, profile application, and
proof; the profile is immutable within one application lifetime.

Transport acknowledgement is not profile application proof. A modified client cannot raise the
hard limit by claiming a different recommendation.

## Stock input scheduler

`ClientWorldInputScheduler` is engine-neutral Client Core state used by exported Godot clients,
packet-free Local play, and NetworkBots. Physical adapters resolve the accepted stock control
grammar into semantic input. The scheduler then owns:

```text
latest continuous locomotion                 coalescible
Jump / Interact / Primary and one-shot edges retained, ordered, bounded
held -> released and moving -> stopped       retained, ordered, bounded
submission deadline and hard local cap       profile-derived
transport rejection                          retry without losing the obligation
semantic completion debt                     sequence-fenced and bounded
```

It never manufactures an authority result. Continuous sampling may run above the recommended
submission cadence. An absolute cadence phase avoids frame-quantization drift while still
submitting at most one current state per pump; missed samples are not synthesized. Discrete edges
may use the profile's small burst allowance, but all commands remain subject to the same hard
maximum and finite server-side limiter.

Local play resolves the same finite scheduling policy directly and remains packet-free.

## Authority enforcement

Each authenticated application/control lifetime owns one monotonic finite token bucket plus a
bounded rolling admission window. Its capacity is the declared burst capacity, and the window must
be large enough to contain hard-rate traffic over that interval. The rolling window has no arbitrary
server-side phase: the earlier fixed-window form could reject an otherwise compliant stream merely
because independent client/server windows straddled different boundaries. Admission occurs before
Game mutation. A syntactically valid current
command over the limit:

- does not mutate Game;
- advances the command sequence through an explicit `RateLimited` owner result;
- cannot poison a later valid command;
- increments bounded diagnostics;
- leaves no state after retirement.

The maintained bot modes are `compliant`, `hard-limit`, `over-limit`, `burst-within`,
`burst-over`, and `stale-session`. They use real ClientOnly transport and ordinary Game protocol;
they are not packet fuzzing or direct authority access.

## Server clocks

Direct Connect accepts authoritative frequencies of 20, 30, and 60 Hz. Simulation continues to
use authoritative elapsed time; ticks remain ordering identity. Hosted Network is an event-driven
pump with a bounded maximum service interval, not a fabricated semantic Hz clock. It may service
faster than authority without inventing Game truth.

Current command-line controls are:

```text
--authority-hz=20|30|60
--maximum-input-hz=1..240
--recommended-input-hz=1..maximum
--input-burst=1..32
--input-burst-window-ms=10..5000
--network-service-delay-ms=0..19
```

The default Concept Demo profile remains 60 Hz authority, 60 Hz hard input maximum, 30 Hz stock
recommendation, a 10-command/100 ms burst cap, and a configured 2 ms maximum Network service
interval. PF3 characterizes alternatives; it does not change public/owner delivery policy.

## Timing and rate evidence

Live Monitor percentiles remain bounded rolling views. Explicit PF3 windows additionally retain
fixed whole-window histograms for authority, Network, client semantic application, bot pump, input
edge delay, and native frame/presentation timings. Reports label rolling versus whole-window
scope, producer versus submission versus authority versus delivery rates, and aggregate versus
per-client rates. Across-client reports expose the p50/p95/worst client for each client's p50,
p95, and p99 plus the absolute maximum and worst client ID.

Native telemetry reports the runtime display backend, headless/windowed state, VSync mode,
`Engine.MaxFps`, reported refresh rate, main-loop/frame distributions, and bounded process/GC
facts. A headless main-loop cadence is never described as a visible presented FPS.

## Replication scope carried forward

PF2.5 Option A remains frozen: `ChunkPublication` is the sole current dynamic-public owner. PF3
only observes its scheduling attribution, including schedule attempts, no-change/cadence/no-debt
stops, capacity/transport deferral, and delivery-producing work. Exact message attribution remains
separate for Chunk Delta (120), Chunk Applied Proof (130), snapshot traffic, presentation metadata,
OwnerState, and player commands. Quiet scheduling and the large delta/proof wire cost are PF4
hypotheses, not PF3 optimizations.

## Verification and operators

The short permanent gate is:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf3-only
```

It covers canonical profile identity/fencing, stock scheduling and packet-free Local composition,
20/30/60 mismatches, compliant/hard/abusive burst behavior, stale-session replay, real UDP,
ClientOnly bots, and actual exported Online clients. Long baselines use
`Tools/NetworkBots/run-pf3-baseline.sh`; visible native experiments use `--pf3-native-only` with
the explicit render-mode and evidence-output options documented by the bot/operator guide.
