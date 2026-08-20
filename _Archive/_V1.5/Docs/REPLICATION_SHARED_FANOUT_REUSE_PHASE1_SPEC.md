# Replication Shared Fanout Reuse (Phase 1) - Working Spec

Purpose: define the first pipeline-side step toward chunk/AOI-aware shared fanout reuse without breaking existing replication behavior.

## Problem

Current replication already encodes delta payloads once, but dispatch still evaluates/send-packs per connection.

With chunk/AOI:
- many deltas share the same recipient set (subscribers of a chunk / chunk-group)
- those recipient sets overlap and **cannot** be expressed by the existing single `ConnectionState.GroupId`

This leaves fanout reuse potential on the table even after AoI filtering.

## Phase 1 Goal

Add a pipeline-supported **exact recipient selector** fast path:

- caller can provide exact recipient connection IDs for a delta
- pipeline dispatches directly to those recipients
- optional trusted mode skips `interestFilter`

This is a scaffold for later:
- shared recipient-group caching
- chunk-group serialization reuse
- packet batch reuse per recipient set

## Non-Goals (Phase 1)

- No changes to packet format
- No shared packet object reuse yet
- No pipeline-internal recipient-group cache yet
- No changes to existing behavior when selector is not configured

## API (Phase 1)

New delegate:

- `ReplicationRecipientSelector(in EncodedReplicationDelta delta, Span<int> destination) -> int`

Semantics:
- returns recipient count written
- if return value > `destination.Length`, caller grows scratch and retries
- return `0` for no recipients

New pipeline option:
- `assumeRecipientSelectorDefinesInterest`
  - if `true`, pipeline skips `interestFilter` for selector-returned recipients
  - use only when selector already represents exact interest/AoI result

## Implementation Status

Started:
- delegate added
- `NetworkReplicationPipeline` delta visitors call recipient selector fast path when configured
- unit test added for exact fanout + trusted mode

## Next Steps

1. Local island runtime (`_tmp`) wires AoI chunk subscriber sets into `ReplicationRecipientSelector`
2. Collect telemetry on selector hit rate and recipient counts
3. Add shared recipient-group caching / signature reuse
4. Add serialization reuse for repeated recipient sets (Phase 2)
