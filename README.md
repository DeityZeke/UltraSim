# UltraSim

UltraSim is a bounded, deterministic simulation and game-runtime foundation for
very large persistent worlds. The repository contains the accepted ECS 3.4.2
kernel, authoritative Runtime/hosts, transactional persistence, neutral UI
protocol and Godot renderer, concrete game composition, Accounting, Admission,
Inventory, and the completed remote-authority Network N0 foundation.

Network N0 is now frozen as the executable/reference remote path. It includes
secure ProtocolLoopback and UDP, authenticated application binding, controlled
input/owner state, observer WorldState, retained Events, atomic baseline recovery,
generation replacement, shared-listener/native socket batching, long-lived
WorldState identity-ledger compaction, and the accepted dense/sparse performance
evidence. Dense-400 demonstrated a usable <=6 ms server-role observation; sparse
V3 remains materially behind frozen V2 scale. The final N0.11 exact-AOI experiment
removed substantial relation/record/wire work but worsened server-role p95 because
precise recipient/entity membership reconciliation doubled interest-update cost.
That result closes open-ended N0 tuning and identifies world-spatial topology as
the next architectural dependency rather than another packet/codec problem.

The active major milestone is **Terrain / World T0**. T0 builds the chunk-native
world substrate originally intended for terrain, statics, streaming, and AOI:
deterministic chunk-relative coordinates, packed terrain and UO-like statics,
logical Multi/property composition, derived dynamic ECS membership, one shared
revisioned publication page per chunk, bounded dirty history, local snapshot/delta
streaming, and efficient Godot terrain/static rendering. The acceptance vertical
is a small woods/cabin world rather than another isolated kernel demo.

**Network N1 is deliberately deferred until T0 is accepted.** N1 will then test
recipient-to-chunk subscriptions/cursors against the frozen N0 sparse relation
graph: snapshot a chunk on first interest, query its bounded dirty history at AOI
cadence thereafter, change query frequency rather than entity semantic type, and
let the client perform fine public visibility. See the forward plans below.

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
  and packet ownership, generated codecs/manifests, security, reliability,
  deterministic impairment, and connected owner-thread UDP transport.
- `Kernels/Spatial` — dependency-neutral integer spatial identity, geometry, and
  canonical math; no runtime world storage or kernel coupling.
- `Kernels/UI` — neutral definitions, models, deltas, actions, semantic port,
  and bounded transport-independent semantic wire codec.
- `Server/*` — authority lifecycle, hosts, and durable save-set coordination.
- `Client/*` — retained client projection, remote network bridge, and Godot
  presentation.
- `Game/*` — concrete game schema/content/state, authoritative workflows,
  canonical replication projection, persisted world-position authority,
  generation-owned dynamic spatial membership, and game-owned network binding.
- `_Tests`, `_Benchmarks`, `_Tools` — executable correctness, evidence, and gates.
- `_Archive/_V2` — frozen historical/reference source used only by explicit
  comparisons. Other archived generations and reference material live beside it
  under `_Archive` and are excluded from active Godot project discovery.

Terrain T0.0 accepted the tiny Spatial contracts seam and concrete game position
profile. T0.1 accepted the persisted component wrapper and first game-owned,
generation-coherent maintained spatial index after full-capacity migration and
reference-model hardening, without creating a generic world storage kernel.
T0.2 accepted the compact dense base-terrain contract using a per-chunk int32
base plus unsigned q5 offsets. T0.3 now proposes the canonical sectioned
immutable WorldPack/source and is stopped for review. Terrain queries,
publication, and Network N1 remain later gates.
The verifier continues to enforce the exact active production project inventory
and dependency directions.

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
- [Terrain / World T0 active plan](Docs/Plans/TERRAIN.md)
- [Network V3 N1 deferred plan](Docs/Plans/NETWORK.md)

Source and tests are executable truth. XML comments define public API semantics.
Current Markdown explains architecture and rationale. Plans describe only
unfinished work. Immutable JSON records accepted benchmark evidence. Git history
is the archive for completed plans and acceptance reports.
