# UltraSim

UltraSim is a bounded, deterministic simulation and game-runtime foundation for
very large persistent worlds. The current repository contains the accepted ECS
3.4.2 kernel, authoritative Runtime/hosts, transactional persistence, neutral UI
protocol and Godot renderer, concrete game composition, Accounting, Admission,
Inventory, and the completed S0 end-to-end integration slice.

Network V3 is the active major implementation milestone. N0.0's contracts,
N0.1's canonical game projection/OfflineDirect oracle, N0.2's bounded byte/kernel
foundation, N0.3's secure reliable ProtocolLoopback, N0.4's remote
Admission/CharacterSelect/Inventory application vertical, N0.5's connected-UDP
transport/session path, N0.6's bounded controlled-input/owner-state vertical,
N0.7's maintained AOI/pending/application-knowledge vertical, and N0.8's atomic
baseline/recovery/generation-replacement vertical are implemented. N0.9 now
accepts the hardened integrated OfflineDirect/secure-loopback/UDP/listen-host
vertical, including multi-consumer retained application Events, reliable
baseline cancellation, exact WorldState parity, and durable generation replacement.
N0.10 now includes the isolated V2/V3 owner-movement comparison, shared-listener
and reliable-only-ACK CPU corrections, bounded retained-payload arenas,
cadence/stall acceptance, and V3-only integrated `spread_mix`/`dense_mix`
evidence. Four hundred observer-heavy clients remain bounded and converge with
zero capacity loss. Participant-local Event indexing removed the first proven
cross-participant ledger scan and reduced the probes-off dense/spread p95s to
about 21.2/19.5 ms in the combined same-process harness. Opt-in role attribution
puts dense server work at about 10.1 ms p95—inside feasibility but above the ≤6 ms
usable target. The next WorldState census added an updates-only causal control
and found repeated pending-list head scans. Per-tier bundle cursors removed
80.1% of pending examinations and lowered probe-enabled dense Full server-role
p95 from 10.568 to 9.971 ms without changing semantics, wire, cadence, or
capacity. Dense remains feasible rather than usable; measured application-ACK/
protocol and canonical relation-maintenance work shape the next isolated step.
The following application-proof census rejected reliable batching, then proved
WorldState proof reconstructible under loss and moved only that proof to bounded
independent-unreliable delivery in protocol v9. Probe-enabled Full server-role
p95 fell 9.753→7.743 ms; the probes-off combined Full tick is now 13.396 ms with
zero fixed-60 deadline misses. The server role remains above the ≤6 ms usable
target. A subsequent stable `Far`-horizon fast path collapsed three maintained-
graph traversals into one for all 2,400 Dense-400 dirty updates, lowering
authority-update p95 1.087→0.703 ms and probe-enabled Full server-role p95
7.743→7.557 ms. The probes-off combined Full tick is now 13.068 ms with zero
deadline misses. A following expiration census found 97,912 bundle-build calls
rescanning 3,200 slots at only 123 semantic times: 313.3 million diagnostic slot
checks. Caching one completed global expiration sweep per exact semantic time
reduces that to 393,600 checks. Probe-enabled Full server-role p95 is now
5.876 ms and the probes-off combined Full tick is 10.598 ms, with identical
bundles, records, packets, wire bytes, convergence, and capacity behavior.
Dense-400 is now inside the ≤6 ms server-role usable target in this observation;
known-deferred pending debt was then measured separately. Although 44,430 of
44,712 `NoData` builds examined only deferred debt, a bounded ready/deferred
wheel enlarged and churned the hot relation representation enough to regress the
probes-off Full tick 10.598→11.711 ms. That representation was rejected and is
not in production; only dormant attribution and immutable negative evidence are
retained. Comfortable p99 headroom remains open.

## Design

> Build the smallest predictable machine that can move a very large simulation
> forward efficiently.

```text
AUTHORING / MODDING
GAME COMPOSITION
RUNTIME / BRIDGES / ORCHESTRATION
KERNELS
```

Identity is a value. Components are data. Storage is layout-optimized. Traversal
is contiguous. Mutation crosses a barrier. Barrier work is batchable. Runtime
memory is bounded. Normal use cannot corrupt invariants.

One `ServerRuntime` owns one coherent active `ServerGeneration`: ECS world,
authoritative clock, and game state. Clients and hosts do not share mutable world
authority. Bridges translate, route, and synchronize.

Authoritative time follows:

> Tick is identity. Delta is time. Frequency determines Delta.

Fixed 60 Hz, 1x playback, with cadence adaptation disabled is the performance
baseline. Per-time behavior proves its meaning at multiple fixed cadences;
per-step behavior is an explicit documented choice.

## Current project shape

- `Kernels/ECS*` — generated schema/query APIs, packed ECS, scheduler,
  structural/lifecycle/content/persistence mechanisms.
- `Kernels/Network*` — dependency-clean identity/lane contracts, fixed connection
  and packet ownership, generated codecs/manifests, canonical framing, and
  deterministic datagrams plus secure reliable ProtocolLoopback and connected
  owner-thread UDP transport. Game-owned adapters above it now carry bounded
  redundant input and revisioned owner state without transferring authority.
- `Kernels/UI` — neutral definitions, models, deltas, actions, semantic port,
  and bounded transport-independent semantic wire codec.
- `Server/*` — authority lifecycle, hosts, and durable save-set coordination.
- `Client/*` — retained client projection, remote application bridge, and Godot presentation.
- `Game/*` — concrete game schema/content/state, authoritative workflows,
  canonical replication projection, and game-owned network application binding.
- `_Tests`, `_Benchmarks`, `_Tools` — executable correctness, evidence, and gates.
- `_V2` — frozen historical/reference source used only by explicit comparisons.

The verifier enforces the exact active production project inventory and
dependency directions.

## Verify

Requirements are .NET 8 and, for the Godot gate, Godot 4.7.1 Mono. Set
`ULTRASIM_GODOT` or pass `--godot=/path/to/executable` when it is not in the
known local location.

Run the complete canonical gate:

```bash
./verify.sh --all-configurations
```

Useful focused variants:

```bash
./verify.sh --architecture-only
./verify.sh --configuration=Release --skip-godot
./verify.sh --all-configurations --no-restore
```

The architecture-only gate also validates canonical documentation, relative
links, root-document policy, XML-document generation policy, and stale claims.

## Documentation

Start with the [documentation map](Docs/README.md).

- [Whole-engine architecture](Docs/ARCHITECTURE.md)
- [Runtime, authority, and cadence](Docs/RUNTIME.md)
- [Persistence](Docs/PERSISTENCE.md)
- [UI and client projection](Docs/UI.md)
- [Game composition](Docs/GAME.md)
- [ECS](Kernels/ECS/README.md)
- [Network kernel foundation](Kernels/Network/README.md)
- [Game replication](Game/Replication/README.md)
- [Benchmark methodology](_Benchmarks/README.md)
- [Network V3 active plan](Docs/Plans/NETWORK.md)

Source and tests are executable truth. XML comments define public API semantics.
Current Markdown explains architecture and rationale. Plans describe only
unfinished work. Immutable JSON records accepted benchmark evidence. Git history
is the archive for completed plans and acceptance reports.
