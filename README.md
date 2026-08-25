# UltraSim

UltraSim is a bounded, deterministic simulation and game-runtime foundation for
very large persistent worlds. The repository contains the accepted ECS 3.4.2
kernel, authoritative Runtime/hosts, transactional persistence, neutral UI
protocol and Godot renderer, concrete game composition, Accounting, Admission,
Inventory, the accepted Terrain/World T0 vertical, and the completed hosted
remote-authority foundation.

The original Network N0 path is frozen and retained as the explicit
`MaintainedEntityRelation` Game replication profile. It includes
secure ProtocolLoopback and UDP, authenticated application binding, controlled
input/owner state, observer WorldState, retained Events, atomic baseline recovery,
generation replacement, shared-listener/native socket batching, long-lived
WorldState identity-ledger compaction, and the accepted dense/sparse performance
evidence. Dense-400 demonstrated a usable <=6 ms server-role observation; sparse
V3 remains materially behind frozen V2 scale. The final N0.11 exact-AOI experiment
removed substantial relation/record/wire work but worsened server-role p95 because
precise recipient/entity membership reconciliation doubled interest-update cost.
That result closed open-ended N0 tuning and identified world-spatial topology as
the next architectural dependency rather than another packet/codec problem.

Terrain / World T0 is accepted/frozen. It provides the chunk-native
world substrate originally intended for terrain, statics, streaming, and AOI:
deterministic chunk-relative coordinates, packed terrain and UO-like statics,
logical Multi structure composition, a separate future 3D region/property seam,
derived dynamic ECS membership, one shared
revisioned publication page per chunk, bounded dirty history, local snapshot/delta
streaming, and efficient Godot terrain/static rendering. The acceptance vertical
is a launchable woods/cabin world rather than another isolated kernel demo.

Network N1.0-N1.4 and Hosted H0.0-H0.5 are accepted. The dedicated-server/
independent-client gate selects `ChunkPublication` as the current UltraSim game
profile while retaining `MaintainedEntityRelation` as a supported comparison and
alternate Game policy. Both flatten after Game encoding into the same hosted
Network machinery. N1.5 shared packing and the future `DirtyEntitySpatialFanout`
profile have not started.

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
base plus unsigned q5 offsets, and T0.3 accepted the canonical sectioned
immutable WorldPack/source. T0.4 accepted exact integer base queries and the
bounded deterministic base-mesh payload. T0.5 accepted optional packed
frozen-static and static-Multi sections, stable logical identities, deterministic
render groups, and bounded exact static-bounds queries. T0.6 accepted the
explicit dynamic-public component, generation-owned packed chunk publication,
bounded dirty history, canonical snapshot/delta reads, and direct committed SoA
views. T0.7 now implements exact hysteretic chunk-interest rings and a bounded
packet-free local snapshot/delta streaming oracle and is accepted/frozen after
consumer hardening. T0.8 now implements the one bounded production client
semantic world cache plus an independently budgeted Godot terrain/grouped-
instance renderer. Its hardening pass now separates immutable structural builds
from dynamic public refresh, maps semantic handles onto independent private
render slots, validates snapshot lineage canonically, and uses integer-relative
vertical anchors before float conversion. Final sliding-window hardening makes
same-chunk X/Z movement common-root-only, coalesces rapid/reversed transitions
toward the latest desired center, and provides a bounded retire/reuse reset for
fast travel. The final review pass separates public refresh from window ownership,
adds indexed structural-key lookup and a true same-center fast path, makes
continuous Y movement common-root-only, and includes terrain surfaces in render
group admission. A post-acceptance portability pass moves desired-window
candidate/diff/reset policy into a transactional engine-neutral Client Core
planner; the Godot class is now only its native admission bridge. T0.8 is
accepted/frozen. T0.9 now implements the exact immutable
pack plus bounded complete terrain/static/Multi overlay view, per-chunk structural
revision/hash identity, and the fourth `worldview` save-set participant. Its
implementation plus client-portability/invariant hardening is accepted/frozen.
Prepared client meshes honor overlays; empty/no-op overlay
lineage is save/restore-closed; and ordinary identity validation uses cold
bound-pack indexes rather than rescanning the immutable world. T0.10 is accepted:
retained Direct/Destination authority and cadence semantics are closed, and
`Client/Playable.Godot` now provides the first real OfflineDirect woods/cabin
application. Human acceptance and the broad performance matrix closed T0. Network
N1 is now selected through the Hosted H0.5 remote-process gate; the current profile
seam and paired conformance tests live in `Game/Replication`.
The native T0.10 acceptance now also composes one real ECS woodland actor through
the selected dirty-work scheduler, canonical secure chunk protocol, production
client semantic cache, and Godot renderer. Spawn, movement, interest leave/re-entry,
destruction, reconnect/resync, and save/load generation replacement are exercised
without a benchmark-only world adapter.
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

Launch the current T0.10 client with a Godot 4.7 .NET build:

```bash
godot --path Client/Playable.Godot
```

The controls and save location are documented in
[`Client/Playable.Godot/README.md`](Client/Playable.Godot/README.md).

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
- [Terrain / World T0 accepted plan/status](Docs/Plans/TERRAIN.md)
- [Network V3 N1 plan/status](Docs/Plans/NETWORK.md)
- [Hosted Network H0](Docs/Plans/HOSTED_NETWORK.md)

Source and tests are executable truth. XML comments define public API semantics.
Current Markdown explains architecture and rationale. Plans describe only
unfinished work. Immutable JSON records accepted benchmark evidence. Git history
is the archive for completed plans and acceptance reports.
