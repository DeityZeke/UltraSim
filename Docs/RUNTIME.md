# Server Runtime and authoritative cadence

`ServerRuntime` is a synchronous, owner-thread authority coordinator. It owns
lifecycle and publication; it is not another job scheduler. ECS owns system work
and workers. Hosts own process/platform concerns. Game composition owns game
state and rules.

## Lifecycle and ownership

Runtime moves explicitly through construction, initialization, running, stopped,
faulted, and disposed states. Mutating control calls require the owner thread and
an idle operation boundary. Reentrant lifecycle, publication, or authority use
is rejected before mutation.

`ServerGeneration` is the atomic active root containing the ECS world, clock,
and opaque game state. Replacement follows:

```text
build candidate
prepare ECS and game state
validate candidate
publish one root
notify isolated observers
retire previous root
```

An unexpected exception escaping a trusted game callback terminally faults a
running Runtime because authority may have been partially mutated. Expected game
failures remain explicit result values inside that callback.

## Game access

`ServerGameFacet` lends the current ECS world and typed game state only for the
synchronous callback that received them. Callers must not retain either value.
Publication is forbidden while an old-generation callback is active.

Generation-publication observers reconcile derived sessions/indexes. Observer
failure is isolated and reported; it cannot roll back an already published root.

## Hosts

- `InternalHost` is the in-process/offline host boundary.
- `HeadlessHost` is the engine-neutral authoritative host.
- `HostedRuntime` contains shared host control.

Hosts expose lifecycle, ticking, and permitted clock policy. They do not expose
generation replacement or raw mutable authority as public host operations.

## Clock contract

```text
SimulationTick       authoritative step/order identity
DeltaSeconds         elapsed simulation time represented by one step
SimulationFrequency  host policy determining DeltaSeconds
WorldTime            durable fixed-point authoritative time
PlaybackRate         host speed for reaching future steps
```

Persistent clock state contains tick, world-time rate, integer microseconds, and
a frequency-independent fractional quantum. It excludes simulation frequency,
playback, and host elapsed residue. A save can therefore load at another exact
supported host frequency without changing world time.

The default is fixed 60 Hz and 1x playback. Variable frequency is disabled unless
`AllowVariableSimulationFrequency` is selected at cold construction. A live
change requires the owner thread and an idle initialized/running boundary. It
preserves active authority and exact world time, then discards non-authoritative
host-time residue so one residue is never interpreted at two rates.

Supported frequencies are positive, no greater than 1,000 Hz, and exactly divide
the fixed time base. Unsupported frequencies reject; authoritative time is never
silently rounded.

Ordinary generation replacement and durable restoration retain the current
host's live cadence/playback policy. `WorldClock.InitialOptions` is explicitly
the cold configuration, not current live state.

## System-authoring rule

Continuous integration uses `DeltaSeconds`. Exact deadlines, persisted temporal
facts, and cross-cadence comparisons use authoritative fixed-point/world time.
Per-time behavior must be tested at multiple fixed cadences. Tick-relative
behavior is allowed only as an explicit design choice.

Cadence independence is an engine capability. Adaptive load control is future
deployment policy above Runtime and may not hide a fixed-60 performance
regression.
