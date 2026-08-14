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
    ECS and neutral UI mechanisms
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
packetization but preserves the same authority/presentation separation expected
of the future remote path.

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

World, scheduler, command, lifecycle, content, persistence, and UI capacities are
chosen explicitly. Hot paths use reserved unmanaged/native storage and avoid
warmed managed allocation where their contract promises it. Capacity exhaustion
is a named result or exception; it is not permission to allocate an unbounded
fallback.

Managed collections remain acceptable in cold game composition and small
derived/application indexes when their ownership and scale are explicit.

## Current project groups

- `Kernels/ECS*`: ECS runtime and source generators.
- `Kernels/Network`: dependency-clean Network V3 identities, lane contracts,
  deterministic evidence controls, and eventually protocol/transport mechanisms.
- `Kernels/UI`: neutral view/model/action protocol.
- `Server/Runtime`, `Server/Hosting`, `Server/Persistence`: authority,
  deployment, and durable save-set mechanics.
- `Client/Core`, `Client/_Godot`: retained projection and rendering.
- `Game/*`: concrete content, state, policy, and authoritative workflows.
- `_Tests`, `_Benchmarks`, `_Tools`: executable evidence and repository gates.
- `_V2`: frozen historical/reference implementation, compiled only by explicit
  comparison projects.

Network V3 is the active major layer. N0.0 evidence, N0.1 game-owned canonical
projection/OfflineDirect, N0.2 bounded byte foundations, and N0.3 secure reliable
ProtocolLoopback are implemented. Remote Admission/UI begins at N0.4. The remaining checkpoints are in
[`Plans/NETWORK.md`](Plans/NETWORK.md).
