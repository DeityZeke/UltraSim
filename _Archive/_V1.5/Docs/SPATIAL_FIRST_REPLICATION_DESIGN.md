# Spatial-First Replication Design

## Goal

Reduce replication build cost by changing the observer path from:

- global replay-window traversal
- then per-delta recipient selection

to:

- spatial chunk selection first
- then replay traversal only for the chunks a connection is subscribed to

This design targets the next ceiling after the current best configuration:

- `--repl-build-workers 2`
- `--observer-stream-cadence 2`

With that configuration, the server stays near the target through roughly `2048` CCU, but bends upward at `2304+`.


## What the current data says

At `2048` with the current best config:

- `tick_avg_ms ~9.59`
- `build_avg_ms ~6.84`
- `replay_avg_ms ~4.49`
- `host_flush_avg_ms ~7.38`

At `2304`:

- `tick_avg_ms ~10.89`
- `build_avg_ms ~7.72`
- `replay_avg_ms ~5.18`
- `host_flush_avg_ms ~8.15`

At `2560`:

- `tick_avg_ms ~12.23`
- `build_avg_ms ~8.77`
- `replay_avg_ms ~5.92`
- `host_flush_avg_ms ~9.17`

So the next useful reduction is still on the observer build/replay side. The current pipeline still pays too much global replay traversal cost before it knows which connections are actually spatially relevant.


## Non-goals

- Do not change owner correction semantics.
- Do not remove replay from owner traffic.
- Do not change reliable despawn/spawn behavior.
- Do not parallelize flush in this pass.
- Do not add another cache-heavy recipient selection layer.


## Current shape

Today the observer path is:

1. publish candidate into one global replay window
2. build traverses replay buckets over a tick range
3. for each delta, recipient selection maps entity -> chunk -> subscribed connections
4. packet/batch assembly runs per selected connection

That means spatial locality exists in the data model, but it is applied inside the replay visit loop rather than before it.


## Desired shape

Observer path becomes:

1. publish observer delta into a replay partition keyed by chunk
2. build connection `C`
   - read `C`'s subscribed chunk set
   - visit only those chunk replay partitions
   - walk replay history only inside those partitions
3. emit observer payloads to `C`

Owner path remains:

1. publish into owner/global path
2. build owner correction exactly as today


## Core idea

Split replay storage by stream semantics:

- owner stream:
  - keep existing global replay semantics
  - keep per-connection ack/start tick behavior
- observer stream:
  - partition replay by `ChunkCoord`
  - build only from subscribed chunk partitions

This keeps correctness where it matters and removes unnecessary global traversal from the dominant fanout path.


## Required correctness boundaries

### 1. Owner stream stays unchanged

Owner packets still need:

- ack patching
- full replay semantics
- per-owner delivery

They should not be forced through chunk replay partitions.


### 2. Observer replay remains tick-ordered

Within a chunk partition, replay still preserves:

- tick order
- per-tick bucket ordering
- existing payload semantics

We are changing *which buckets a connection visits*, not the ordering within a bucket.


### 3. AOI transitions still produce correct visibility behavior

Spatial-first build does not replace:

- `RefreshChunkVisibility`
- pending despawn grace
- explicit despawn sends

Those behaviors remain as they are.


### 4. Cross-chunk entity movement must be handled explicitly

When an entity changes chunk:

- future observer deltas publish into the new chunk partition
- AOI logic already handles visibility churn
- no special replay stitching is required beyond correct publish target selection

We should not attempt to duplicate observer deltas into both old and new chunk partitions. That would add cost back.


### 5. Duplicate observer sends must be impossible

A connection subscribes to multiple chunks. Each observer delta belongs to exactly one chunk partition. That means:

- one delta
- one partition
- one visit per subscribed connection

No de-dup pass should be needed if publish-to-chunk is single-valued.


## Proposed data structures

### `ObserverChunkReplayIndex`

New server-side helper, likely under `UltraSim/Server/Gameplay` or `UltraSim/Network/Replication`.

Responsibilities:

- map `ChunkCoord -> ReplicationReplayWindow`
- create partition on demand
- prune all active partitions once per build tick
- publish observer candidates into the correct chunk partition
- expose active partition count / total bytes / total deltas for diagnostics

Suggested shape:

```csharp
private sealed class ObserverChunkReplayIndex
{
    Dictionary<ChunkCoord, ReplicationReplayWindow> _windowByChunk;
    Dictionary<ulong, ChunkCoord> _lastPublishedChunkByEntityId;

    void Publish(in ReplicationCandidate candidate, ReadOnlySpan<byte> payload, ChunkCoord chunk);
    void PruneAll(long currentTick);
    bool TryGetWindow(in ChunkCoord chunk, out ReplicationReplayWindow window);
}
```

Notes:

- `ReplicationReplayWindow` itself does not need to know about chunks.
- Keep chunk partitioning outside the generic pipeline first. Lower risk.


### `ChunkSubscriberSnapshot`

For build, we need a stable dense view of a connection's subscribed chunks.

Current source of truth already exists:

- `_subscribedChunksByConnection`

We should expose a stable per-connection array/span view for build:

- avoid enumerating `HashSet<ChunkCoord>` repeatedly on the hot path
- snapshot only when AOI changes, not every tick

This is one of the few cache-like structures that is justified, because:

- it reflects actual topology changes
- it is not recomputed every delta


## Integration strategy

### Phase 1: sidecar observer replay path

Do not rewrite `NetworkReplicationPipeline` first.

Instead:

1. keep existing bridge/pipeline for owner/global path
2. add an observer-specific sidecar build path in `ServerPlayerReplicationService`
3. publish observer candidates into `ObserverChunkReplayIndex`
4. per connection, build observer packets by:
   - walking subscribed chunk partitions
   - visiting replay buckets over `[startTick..currentTick]`
   - directly appending packets/batches for that connection

Why:

- lower risk than trying to generalize the whole pipeline at once
- avoids fighting existing grouped/selector code paths
- lets us test whether spatial-first traversal is actually the lever before deeper refactors


### Phase 2: merge if the sidecar path wins

If Phase 1 produces a real cost drop:

- either keep the observer sidecar path as a permanent specialization
- or fold the concept back into a generalized chunk-partitioned replay layer later

There is no requirement to force owner and observer through one abstraction if that abstraction costs performance.


## Build algorithm for Phase 1

For each connection:

1. get owner/global packets from existing build path as today
2. if connection has no subscribed chunks, done
3. compute observer replay range from connection cursor / start tick
4. for each subscribed chunk:
   - fetch `ReplicationReplayWindow` for chunk
   - if none, continue
   - visit `[minStartTick..currentTick]`
   - emit observer packets for this connection only

This is spatial-first because:

- the outer loop is subscribed chunks
- not global replay buckets


## Why this should help

At high CCU, each connection only cares about a small AOI subset. With global replay:

- every observer delta participates in global traversal
- then gets filtered spatially inside the loop

With chunk-first traversal:

- a connection only visits chunks it subscribes to
- global non-local observer deltas are never touched for that connection

That is actual work elimination, not cache substitution.


## Expected gains

The target is not a guess at `2x` or `10x`. The realistic expected shape is:

- `replay_avg_ms` should drop materially first
- `build_avg_ms` should follow
- `tick_avg_ms` should improve by a smaller but still meaningful amount

If this path is working, the success signature is:

- lower `replay_avg_ms`
- lower `build_avg_ms`
- flat or lower `host_flush_avg_ms`
- no replication correctness regressions


## Metrics to add for the prototype

Keep this light.

### New observer spatial metrics

- `observer_chunk_windows`
- `observer_chunk_replay_deltas`
- `observer_chunk_replay_bytes`
- `observer_chunk_build_avg_ms`
- `observer_chunk_build_max_ms`
- `observer_chunk_visits`
- `observer_chunk_visit_avg`

### Per-build counters

- `observer_chunk_candidates_sent`
- `observer_chunk_candidates_skipped_no_window`
- `observer_chunk_candidates_skipped_no_subscription`

Do not add per-delta micro-timers.


## Risks

### 1. Double-build complexity

Running owner/global and observer/chunk paths side-by-side increases code complexity. That is acceptable if the observer sidecar is clearly isolated.

### 2. Partition explosion

If chunks churn heavily, chunk replay windows could proliferate. Need pruning and empty-window reclamation.

### 3. Cursor semantics mismatch

Owner/global path already has per-connection replay semantics. Observer sidecar must use the same replay range calculation, or observer history will drift.

### 4. Batch flush interaction

If observer sidecar greatly increases per-connection locality but still emits too many batches, flush may become the next limiter faster. That is acceptable; it means the bottleneck moved.


## Recommended implementation order

1. Add `ObserverChunkReplayIndex`
2. Publish observer candidates into chunk replay partitions in parallel with the existing path
3. Add a disabled-by-default flag:
   - `EnableObserverSpatialFirstBuild = false`
4. Build one connection from chunk partitions only, compare against existing observer output
5. Gate full observer path behind the flag
6. Benchmark against:
   - `--repl-build-workers 2`
   - `--observer-stream-cadence 2`


## Stop conditions

Abort or revert if:

- observer packets disappear or collapse like the bad replay split experiment
- `replay_avg_ms` drops but `build_avg_ms` rises materially
- AOI visibility/despawn correctness regresses

Proceed if:

- `replay_avg_ms` drops materially
- `tick_avg_ms` improves at `2304+`
- p95 improves without pushing flush into a worse wall


## Recommendation

The next implementation should be:

- observer-only
- chunk-partitioned
- sidecar to the current owner/global pipeline

Do not start by rewriting the generic replication pipeline. The generic path has already shown that abstraction-preserving changes tend to regress. The observer path is where the scale problem is, and it should be specialized accordingly.
