# UltraSim

UltraSim is a bounded, deterministic simulation and game-runtime foundation for
very large persistent worlds. The current repository contains the accepted ECS
3.4.1 kernel, authoritative Runtime/hosts, transactional persistence, neutral UI
protocol and Godot renderer, concrete game composition, Accounting, Admission,
Inventory, and the completed S0 end-to-end integration slice.

Network V3 is the active major implementation milestone. Its N0.0 dependency,
identity, lane, deterministic-evidence, and comparison foundation is implemented;
canonical game projection and OfflineDirect capture are next.

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
- `Kernels/Network` — dependency-clean N0.0 identity, lane, deterministic trace,
  impairment, and evidence contracts; protocol/transport are later checkpoints.
- `Kernels/UI` — neutral definitions, models, deltas, actions, and semantic port.
- `Server/*` — authority lifecycle, hosts, and durable save-set coordination.
- `Client/*` — retained client projection and Godot presentation.
- `Game/*` — concrete game schema/content/state and authoritative workflows.
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
- [Benchmark methodology](_Benchmarks/README.md)
- [Network V3 active plan](Docs/Plans/NETWORK.md)

Source and tests are executable truth. XML comments define public API semantics.
Current Markdown explains architecture and rationale. Plans describe only
unfinished work. Immutable JSON records accepted benchmark evidence. Git history
is the archive for completed plans and acceptance reports.
