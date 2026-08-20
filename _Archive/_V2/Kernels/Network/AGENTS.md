# AGENTS.md

## Mission

Build the new networking stack as an engine-agnostic kernel with explicit server/client bridge layers.

Use the old network as reference material, not as a direct port target.

## Priority Order

Do not violate this order without explicit justification:

1. Correctness
2. Safety
3. Simplicity
4. Maintainability
5. Performance

Networking is performance-sensitive, but not at the cost of incorrect delivery semantics, hidden coupling, or unbounded growth.

## Core Architectural Rules

- Keep `Kernels/Network` engine-agnostic and standalone.
- Keep `Kernels/Network` independent from `Kernels/ECS`.
- Put ECS/network integration in server/client bridge layers, not in either kernel.
- Keep transport, protocol, session, stream, scheduler, assembly, and metrics concerns separate.
- Prefer explicit ownership and immutable handoff points over ambient mutable state.
- Keep startup/bootstrap code thin and outside the kernel.

## Required Layering

Use this split unless there is a strong reason not to:

- `Kernels/Network/Core`
- `Kernels/Network/Serialization`
- `Kernels/Network/Protocol`
- `Kernels/Network/Transport`
- `Kernels/Network/Session`
- `Kernels/Network/Subscriptions`
- `Kernels/Network/Streams`
- `Kernels/Network/Publish`
- `Kernels/Network/Scheduler`
- `Kernels/Network/Assembly`
- `Kernels/Network/Metrics`

Bridge/integration code belongs outside `Kernels/Network`.

## Non-Negotiable Design Rules

### Immutable Outbound Buffers

- Once an outbound payload is assembled, it is immutable.
- Later stages may wrap, transform, segment, or route it, but not mutate it in place.
- Handoff boundaries must be safe for future parallel build/transform/send work.

### Packet Class Separation

Keep at least these classes distinct:

- `Control`
- `Realtime`
- `ReliableEvent`
- `Bulk`

Do not let large bulk updates share budgets or overload behavior with combat/movement traffic by accident.

### Transform Stage

The pipeline must have an explicit transform seam between assembly and send.

That seam exists for:

- compression
- segmentation
- framing/finalization
- optional encryption later

Do not bury these concerns inside transport or replication assembly code.

### Shared Payload Fanout

Support build-once, send-many behavior for shared payloads.

This is especially important for:

- terrain/chunk updates
- housing/state patches
- shared baselines/snapshots
- large replicated world updates

Do not force identical payloads to be rebuilt per connection if the payload is semantically shared.

### Bulk vs Realtime Isolation

Keep bulk and realtime traffic isolated through explicit budgets and backpressure rules.

Under load:

- `Control` and owner-critical traffic stay alive
- `Realtime` coalesces aggressively
- `ReliableEvent` remains explicit and bounded
- `Bulk` degrades first

### Parallel-Ready Handoffs

Parallelism is not the first optimization target, but the design must permit it later.

Design handoff points so future async/parallel work can happen in:

- publish classification
- assembly/build
- compression
- optional send/flush

without changing semantics.

### AOI Policy

AOI should be defined in player-centered world-space policy, not directly by chunk size.

Use chunk/cell overlap as an indexing and subscription-maintenance tool, not as the gameplay definition of visibility rings.

## What Belongs In The Kernel

- transport contracts
- packet/channel/plane/service-class contracts
- immutable payload/buffer ownership
- session/connection registry primitives
- stream and cursor models
- snapshot and recovery primitives
- scheduler/work-item/budget primitives
- transport-safe metrics and counters

## What Does Not Belong In The Kernel

- ECS world mutation logic
- server gameplay/session/account flow
- client rendering/UI code
- Godot nodes or engine-specific types
- host wrappers
- content/database/file-system policy outside explicit adapters

## Testing Rules

Write tests immediately and keep them close to the kernel.

Minimum expected early coverage:

- immutable payload ownership semantics
- transport loopback behavior
- framing/codec round trips
- handshake/session lifecycle
- latest-state stream semantics
- ordered-event retention/recovery
- cursor and baseline rules
- scheduler budget/degradation rules

## Benchmark Rules

Benchmark early, before server/client integration grows large.

Initial benchmark targets:

- loopback transport throughput/allocations
- latest-state publish cost
- latest-state drain cost
- ordered-event drain cost
- snapshot assembly cost
- client-side reconciliation/interpolation structures

Do not wait for a full end-to-end runtime before measuring the network kernel.

## Performance Rules

Treat these as hot paths by default:

- parse/decode
- packet registry dispatch
- replication publish
- stream drain
- assembly/serialization
- queueing and transport flush

Avoid in these paths unless justified:

- reflection
- boxing
- per-item heap allocations
- per-item wall-clock calls
- hidden copies
- giant monolithic coordinator classes

## Guiding Rule

Build a small, explicit, measurable networking kernel.

Preserve the old runtime's valid semantics.
Do not preserve its late-fanout, replay-heavy, or overly coupled shape.
