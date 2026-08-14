# Authoritative Cadence Independence Hardening

Status: Accepted

Date: 2026-08-14

Integrated workload: `ultrasim-s0.8-integrated-acceptance-v3`

ECS kernel baseline: 3.4.1 (unchanged)

## Result

UltraSim can run the same authoritative game at fixed 20, 30, 40, 50, or
60 Hz. A save can move between those configurations without changing durable
tick, exact world time, ECS state, or game state. Runtime cadence changes are an
explicit owner policy, disabled by default, and no automatic adaptive controller
was added.

The frozen contract is:

```text
Tick       = authoritative step/order identity
Delta      = elapsed simulation time represented by a step
Frequency  = host execution policy that determines Delta
```

`SystemSchedule.PeriodTicks` remains step-relative. Time-relative game behavior
uses `DeltaSeconds`, authoritative world time, or a game-owned accumulator.

## Clock representation and persistence

Authoritative world time remains a signed 64-bit integer microsecond count plus
one bounded fraction. The fraction now uses a format-wide denominator of
3,600,000 quanta per microsecond instead of the current simulation frequency.
The supported cadence domain is a positive frequency no greater than 1,000 Hz
that exactly divides the 3,600,000,000,000-quanta-per-second time base. This
includes the required 20/30/40/50/60 Hz set and many common additional rates.
Unsupported rates reject explicitly; no floating-point authoritative time or
silent rounding is used.

Clock artifact format v2 is 38 bytes and persists:

```text
world-time rate
simulation tick
integer world-time microseconds
fixed fractional quanta
```

It deliberately excludes simulation frequency, playback, and host elapsed-time
residue. Format v1's 42-byte frequency-relative state remains readable when its
fraction converts exactly to the fixed denominator. An inexact legacy fraction
raises a compatibility failure instead of being reinterpreted.

## Runtime policy

`ServerRuntimeOptions.AllowVariableSimulationFrequency` defaults to false.
`ServerRuntime.SetSimulationFrequency(...)` is legal only when that option was
enabled and Runtime is idle on its owner thread in Initialized or Running state.
A successful change preserves generation identity, ECS/game authority,
simulation tick, exact world time/fraction, and playback. The next scheduler
step receives `1 / frequency` as `DeltaSeconds`.

Host elapsed residue is non-authoritative and is discarded on a real cadence
change. Fixed-policy, invalid-frequency, foreign-thread, game-callback,
simulation, publication, and publication-reconciliation attempts reject before
clock mutation. Rejection does not fault or dispose a healthy active generation
when the caller handles the expected control error within its boundary.

Whole-generation replacement and durable restore retain the active/loading
host's cadence policy. Durable time never imports the saving host's frequency.

## Adversarial coverage

The permanent focused gates prove:

- fixed 20/30/40/50/60 Hz configurations over 120 simulated seconds;
- differing step counts and period-10 scheduler invocation counts at each rate;
- matching time-relative quarter-second behavior and accumulated delta;
- exact 7x world-time advancement across the full frequency matrix;
- a live 0.5 seconds at 60 Hz followed by 0.5 seconds at 30 Hz;
- live `60 -> 50 -> 40 -> 30 -> 20 -> 60` changes without publication;
- deliberate host-residue reset at a switch;
- fixed-policy and unsupported-frequency transactional rejection;
- owner-thread, game-callback, simulation, publication, and reconciliation
  rejection boundaries;
- 60 Hz save -> 20 Hz load and 20 Hz save -> 60 Hz load through the complete
  `world + accounting + clock` transaction;
- exact preservation of a nonzero two-thirds-microsecond fraction;
- ECS scheduler tick / Runtime tick coherence after restore;
- exact legacy-v1 conversion and inexact-v1 compatibility rejection;
- zero warmed managed allocation on the ordinary fixed-60 Runtime tick path.

## Representative Release evidence

The focused 2026-08-14 run produced:

| Gate | Result |
| --- | ---: |
| 20/30/40/50/60 elapsed-time matrix | identical 120 s semantic state |
| Matrix world time at 7x | exactly 840,000,000 us, fraction 0 |
| Mixed live-cadence sequence | 94 monotonic steps, exactly 15,715,000 us |
| 60 -> 20 restored fraction | 116,666 us + 2,400,000/3,600,000 us |
| 20 -> 60 restored fraction | 466,666 us + 2,400,000/3,600,000 us |
| S0 account-backed artifact | 1,807 B |
| S0 detached save | 50.101 ms |
| S0 load | 44.571 ms |
| S0 owner capture pause | 11.065 ms |
| Fixed-60 direct ECS tick, 4,096 rows | 20.101 us mean |
| Fixed-60 InternalHost tick, 4,096 rows | 20.553 us mean |
| Paired-median host/direct ratio | 1.022x |
| Warmed timed Runtime tick allocation | 0 B |

Performance rows explicitly used fixed 60 Hz, 1x playback, Runtime cadence
changes off, and adaptive cadence off. Repeated focused and full-verifier runs
passed the existing paired-median 5% wrapper gate. This remains a
regression/architecture sample, not a cross-machine performance claim.

Machine-readable evidence:

- `_Benchmarks/Baselines/s0.8-integrated-acceptance-cadence-independent-2026-08-14.json`

## Changed surface

Runtime/hosting:

- `Server/Runtime/WorldClock.cs`
- `Server/Runtime/ServerRuntimeOptions.cs`
- `Server/Runtime/ServerRuntime.cs`
- `Server/Runtime/ServerGameFacet.cs`
- `Server/Hosting/HostedRuntime.cs`
- `Server/Hosting/InternalHost.cs`
- `Server/Hosting/HeadlessHost.cs`

Persistence:

- `Game/Persistence/WorldClockSaveSetParticipant.cs`

Contract-only ECS documentation:

- `Kernels/ECS/Systems/SystemSchedule.cs`
- `Kernels/ECS/Systems/SystemContexts.cs`
- `ECS_SYSTEM_SCHEDULER.md`

Focused tests and documentation:

- `_Tests/UltraSim.Server.Runtime.Tests/Program.cs`
- `_Tests/UltraSim.Game.Persistence.Tests/Program.cs`
- `_Tests/UltraSim.S0.Integration.Tests/Program.cs`
- `S0 Integration Design.md`
- `_Benchmarks/README.md`
- `_Benchmarks/Baselines/README.md`
- `_Benchmarks/Baselines/s0.8-integrated-acceptance-cadence-independent-2026-08-14.json`
- this report and the root/S0 acceptance summaries

No ECS runtime/storage/query/scheduler API changed. No cadence controller,
network, render, physics, AI-fidelity, or generalized time framework was added.

## Performance policy

Normal tuning remains fixed 60 Hz with Runtime cadence changes and automatic
adaptation off. A future adaptive-degradation test is a separate stress class.
Adaptive cadence may trade temporal resolution for overload survival only when
explicitly enabled by deployment policy; it may never conceal a fixed-rate
regression.

## Verdict

Cadence independence is now a proven engine capability. Adaptive cadence remains
future optional policy above Runtime.
