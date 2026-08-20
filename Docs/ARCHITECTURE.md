# UltraSim architecture

UltraSim is a bounded, deterministic simulation substrate for very large game
worlds. Its design target is the smallest predictable machine that can advance
that simulation efficiently—not a general framework for every possible game.

## Principles

> Identity is a value. Components are data. Storage is layout-optimized.
> Traversal is contiguous. Mutation crosses a barrier. Barrier work is
> batchable. Runtime memory is bounded. Normal use cannot corrupt invariants.

Other permanent rules are:

- design for the ecosystem; implement for the game;
- do not abstract the first instance;
- bridges translate, route, and synchronize;
- cold compilation may be sophisticated when it produces a small hot machine;
- expected rejection is transactional; an unexpected failure after trusted
  mutation begins faults the owning authority rather than pretending recovery.

## Four layers

```text
AUTHORING / MODDING
    source definitions and future tools
             |
GAME COMPOSITION
    concrete schema, content, rules, workflows
             |
RUNTIME / BRIDGES / ORCHESTRATION
    authority lifecycle, persistence, hosting, client projection
             |
KERNELS
    ECS, Network, UI, and dependency-neutral Spatial mechanisms/contracts
```

Dependencies point downward or sideways through narrow semantic contracts.
Kernels do not reference Server, Client, or Game. Server Runtime depends on ECS,
not Game. Client Core depends on the neutral UI kernel, not Game or Server. Game
Composition is the concrete place where game-owned domains meet.

The repository verifier freezes the exact active project-reference graph. A new
reference is an architecture decision, not a convenience edit.

## Authority

One `ServerRuntime` owns one active `ServerGeneration`. A generation is the
coherent authority root:

```text
ServerGeneration
    ECS world
    authoritative clock
    game-owned generation state
```

Publication prepares and validates a complete candidate before one root swap.
The previous generation is retired only after publication. Game code borrows
authority through callback-scoped capabilities; hosts and clients never receive
the ECS world as shared mutable state.

Offline play remains server-authoritative. Its local semantic link avoids fake
packetization while preserving the same authority/presentation separation proved
by the accepted Network N0 OfflineDirect/ProtocolLoopback/UDP paths.

## Data and time boundaries

ECS topology is frozen while scheduled systems execute. Structural commands
become visible only at the documented barrier. Changed-value observation and
structural lifecycle observation are independent contracts.

Authoritative cadence follows:

> Tick is identity. Delta is time. Frequency determines Delta.

Per-time behavior uses elapsed or authoritative fixed-point time and must prove
its meaning at multiple fixed cadences. Per-step behavior is deliberate and
documented. Fixed 60 Hz, 1x playback, with cadence adaptation disabled is the
uncompromised performance baseline.

## Boundedness and allocation

World, scheduler, command, lifecycle, content, persistence, UI, Network, and
future spatial-world capacities are chosen explicitly. Hot paths use reserved
unmanaged/native or contiguous numeric storage and avoid warmed managed allocation
where their contract promises it. Capacity exhaustion is a named result or
exception; it is not permission to allocate an unbounded fallback.

Managed collections remain acceptable in cold game composition and small
derived/application indexes when their ownership and scale are explicit.

## Current project groups

- `Kernels/ECS*`: ECS runtime and source generators.
- `Kernels/Network`: dependency-clean Network V3 identities, lane contracts,
  protocol/security/reliability/transport mechanisms, and deterministic evidence
  controls.
- `Kernels/UI`: neutral view/model/action protocol.
- `Kernels/Spatial`: dependency-neutral integer spatial values, explicit
  geometry/bounds, floor normalization, and checked wide-distance math. It owns
  no world storage, membership, AOI, publication, or threads.
- `Server/Runtime`, `Server/Hosting`, `Server/Persistence`: authority,
  deployment, and durable save-set mechanics.
- `Client/Core`, `Client/Network`, `Client/_Godot`: retained projection, remote
  delivery, and rendering.
- `Game/*`: concrete content, state, policy, authoritative workflows, the fixed
  game-world position profile, and replication composition.
- `_Tests`, `_Benchmarks`, `_Tools`: executable evidence and repository gates.
- `_Archive/_V2`: frozen historical/reference implementation, compiled only by
  explicit comparison projects. The rest of `_Archive` is likewise outside the
  active project/build and Godot discovery surfaces.

Network N0 is accepted as the current remote-authority reference, including
bounded long-lived WorldState recovery and the measured dense/sparse evidence.
Its final sparse experiments showed that steady-state replication scales much
better than continuously mutating precise recipient/entity interest topology.
The active major milestone is therefore [Terrain / World T0](Plans/TERRAIN.md).
T0.0 accepted the dependency-neutral Spatial seam plus the 16-byte game-world
position/profile. T0.1 accepted persisted ECS position authority paired with a
bounded, generation-owned maintained index through a generic Runtime post-ECS-step
capability. Its full-capacity migration behavior is model-checked. Packed
terrain/statics, revisioned publication, bounded dirty history, local streaming,
and Godot rendering remain later Terrain gates.
[Network N1](Plans/NETWORK.md) is deferred
until T0 proves those chunk/publication contracts, then will test recipient-to-
chunk subscription/cursor replication against the frozen N0 relation graph.
