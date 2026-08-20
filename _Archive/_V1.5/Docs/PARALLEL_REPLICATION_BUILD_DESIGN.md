# Parallel Replication Build Design

## Goal

Reduce server tick cost by parallelizing replication build work, not by adding more selector/cache bookkeeping.

Current evidence from in-process timings and `dotnet-trace` says:

- average tick cost is dominated by replication build/dispatch
- the hottest managed path is still:
  - `NetworkReplicationPipeline.TryDispatchSelectedRecipients`
  - `NetworkReplicationPipeline.VisitDeltaBucketForBuildUngroupedWhile`
  - `ServerPlayerReplicationService.WriteCorrectionPacketBatch`
  - `ServerPlayerReplicationService.TryAppendReplicationPayloadForBatch`
- raw socket write CPU time is not the primary cost

That makes parallel build the next defensible structural lever.

## Non-Goals

- Do not change protocol behavior.
- Do not change replay semantics for catch-up / ack.
- Do not parallelize publish in the first pass.
- Do not move flush off-thread in the first pass.

## Configuration

Add one runtime option:

```csharp
int MaxReplicationBuildWorkers = 0
```

Semantics:

- `0` = disabled, keep current serial path
- `-1` = auto
- `1` = explicit serial
- `N > 1` = explicit worker cap

Suggested auto resolution:

```csharp
resolved = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
```

Suggested hard clamp:

```csharp
resolved = Math.Clamp(requested, 1, 16);
```

Anything `<= 1` should stay on the existing serial path with no worker pool created.

## Integration Points

- `UltraSim/Server/ServerRuntimeOptions.cs`
  - add `MaxReplicationBuildWorkers`
- `Server.Godot/ServerRuntimeHostNode.cs`
  - expose `--replication-build-workers`
  - parse `0`, `-1`, `N`
- `UltraSim/Server/Gameplay/ServerPlayerReplicationService.cs`
  - pass the resolved worker count into bridge/pipeline construction
- `UltraSim/Server/Engines/Networking/ServerNetworkReplicationBridge.cs`
  - own worker-pool lifecycle
  - choose serial vs parallel build path
- `UltraSim/Network/Replication/NetworkReplicationPipeline.cs`
  - add worker-safe build entry points and worker-local state

## Why Bridge Owns It

`ServerNetworkReplicationBridge` already owns the serial build boundary:

- `PublishDirtyCandidates(tick)`
- `BuildAndEnqueue(tick)`
- optional batching via `BuildTickBatched(...)`

That is the correct place to switch between:

- serial build
- parallel build

without changing higher runtime flow.

## Design Choice

### First Prototype: Connection-Sharded Parallel Build

Keep publish serial.

For build:

1. Snapshot active connections on the main thread.
2. Partition connections into fixed shards.
3. Each worker runs build against its connection shard only.
4. Each worker writes only to shard-local packet buffers.
5. Main thread merges shard packet batches and hands them to the existing packet batch writer.

This is the lowest-risk first prototype because:

- each connection belongs to exactly one shard
- per-connection ordering stays local to one worker
- replay window stays read-only during build
- transport write path stays unchanged

### Known Cost of Prototype 1

Each worker will rescan replay deltas for its shard.

That duplicates:

- replay-window iteration
- delta materialization
- recipient selection work

This is not ideal, but it is an acceptable first prototype because the trace shows the real weight is still dispatch/batch work, not `CreateDelta(...)` itself.

If this prototype does not scale enough, the next design is:

### Prototype 2: Single Replay Scan, Parallel Recipient Dispatch

1. Main thread walks replay buckets once.
2. Materialize a reusable per-build delta list.
3. Precompute recipient selection once per delta.
4. Fan selected recipients into worker-local shard queues.
5. Workers perform only `TrySend` / packet assembly on shard-local connections.

This avoids duplicated selector work, but it is a significantly larger change. It should be phase 2, not phase 1.

## Worker Model

Each worker needs private build state. Do not share these across workers:

- recipient selection cache entries
- interest cache entries
- fanout reuse signatures
- batch packet buffer
- packet counters / timing accumulators

Workers may share read-only data:

- replay window
- dense connection groups
- connection groups
- connection-state-by-id lookup, if not mutated during build

Each worker should own:

- `ConnectionState[]` scratch for its shard
- `ReplicationPacket[]` batch buffer
- local sent count
- local stats/timers

## Correctness Constraints

These are mandatory for phase 1:

1. A connection is assigned to exactly one build shard per tick.
2. Only that shard may mutate its `ConnectionState`.
3. Packet order for a single connection must remain identical to serial build order.
4. Deferred/coalesced state stays shard-local by connection ownership.
5. Track/untrack/group changes must not race build workers.

That means:

- topology mutation stays on the main thread
- build runs against a stable connection snapshot
- shard assignment is rebuilt only at the start of the tick

## Merge Strategy

Do not let workers call transport directly.

Phase 1 merge should be:

1. worker produces shard-local `ReplicationPacket[]`
2. worker flushes only into its local batch sink
3. main thread concatenates shard-local batches
4. existing `_packetBatchWriter` writes to transport

This avoids transport synchronization in the prototype.

## Expected Outcome

If the current serial build is dominated by connection-local dispatch and batch write work, phase 1 should reduce:

- `build_avg_ms`
- `repl_avg_ms`
- `tick_avg_ms`

even if replay scan is still duplicated per worker.

If it does not, that means duplicated selector work is too expensive and phase 2 is required.

## Measurement Plan

Use the current realistic profile:

- `1536`
- `1664`
- `--active-ratio 0.35`
- `--chat`
- `--udp --udp-shared-socket`

Compare:

- `tick_avg_ms`
- `tick_max_ms`
- `repl_avg_ms`
- `build_avg_ms`
- `replay_avg_ms`
- `host_flush_avg_ms`
- p95

Run:

- serial baseline
- `--replication-build-workers=2`
- `--replication-build-workers=4`
- optional `8` if `4` scales cleanly

## Recommended Sequence

1. Add the option plumbing with serial default.
2. Implement phase-1 connection-sharded parallel build behind the flag.
3. Validate correctness first.
4. Benchmark `2` and `4` workers.
5. Only then decide whether phase 2 is needed.

## Why Not Background Flush First

Current trace data does not justify it as the first move:

- actual socket write CPU time is small
- trace hotspots are in build/dispatch, not write syscalls

Flush may still need work later, but it is not the highest-confidence first lever.
