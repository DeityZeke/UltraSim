# UltraSim documentation

This is the map for current UltraSim documentation. Source and tests are the
executable truth; XML comments define public API contracts; these documents
explain the current architecture and rationale. Completed plans and acceptance
reports live in git history. Accepted measurements live in immutable JSON under
[`_Benchmarks/Baselines`](../_Benchmarks/Baselines/README.md).

## Engine

- [Architecture](ARCHITECTURE.md) — layers, dependency direction, authority,
  boundedness, and the source-of-truth hierarchy.
- [Runtime](RUNTIME.md) — `ServerRuntime`, generations, hosts, lifecycle,
  publication, and authoritative cadence.
- [Persistence](PERSISTENCE.md) — ECS snapshots, save-set transactions, game
  participant composition, recovery, and generation publication.
- [UI](UI.md) — neutral UI protocol, authoritative workflows, client state, and
  Godot presentation.
- [Game](GAME.md) — concrete game composition, schema/content ownership,
  Accounting, Admission, and Inventory.
- [Game replication](../Game/Replication/README.md) — N0.1 canonical projection,
  capture/history, authoritative ingress, and OfflineDirect oracle.

## ECS

- [ECS overview](../Kernels/ECS/README.md)
  - [execution and API selection](../Kernels/ECS/EXECUTION.md)
  - [scheduler](../Kernels/ECS/SCHEDULER.md)
  - [structural mutation](../Kernels/ECS/STRUCTURAL.md)
  - [compiled content](../Kernels/ECS/CONTENT.md)
  - [lifecycle publication](../Kernels/ECS/LIFECYCLE.md)
  - [persistence](../Kernels/ECS/PERSISTENCE.md)

## Network

- [Network kernel foundation](../Kernels/Network/README.md) — N0.0 contracts,
  N0.2 bounded bytes, and N0.3 secure reliable ProtocolLoopback.
- [Network V3 active plan](Plans/NETWORK.md) — accepted N0.4 application binding
  and N0.5 through the complete remote
  authority/client vertical.

## Evidence and tools

- [Benchmark methodology](../_Benchmarks/README.md)
- [Accepted baseline policy](../_Benchmarks/Baselines/README.md)
- [Local AI tooling](../_Tools/LocalAI/README.md)

## Active plan

- [Network V3](Plans/NETWORK.md) is the only active major implementation plan.
  N0.0 through N0.4 are accepted; N0.5 carries the exact N0.4 application
  vertical onto real UDP without reopening ECS, Runtime, or game authority.

## Source-of-truth hierarchy

```text
source + tests                 executable invariants
XML / IntelliSense            public API semantics
current Markdown              architecture, rationale, usage
Docs/Plans                    work not yet implemented
benchmark JSON                accepted measured evidence
git history                   completed plans and archaeology
```

New acceptance reports do not belong at the repository root. Promote lasting
contracts into the appropriate current document, preserve accepted measurements
as machine-readable artifacts, and let git retain the historical narrative.
