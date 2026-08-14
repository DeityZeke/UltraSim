# Lifecycle publication

Lifecycle history reports successful structural facts:

- entity created;
- entity destroyed;
- component added;
- component removed.

It is independent of value-change tracking. Lifecycle does not carry arbitrary
component values; changed-row cursors do not infer missing entities or topology.

## Publication and ordering

Records publish only after authoritative structural mutation succeeds. Failed or
rejected commands publish nothing. Immediate owner-thread operations and deferred
barrier playback enter one world-local logical sequence. Scheduler participant
completion order cannot change record order; deterministic merged command order
does.

Create/destroy/membership facts use stable public identity appropriate to the
record contract, never chunk row or local archetype storage as durable meaning.

## Ring and cursor

Lifecycle storage is a fixed-capacity world-owned ring. Each independent
`LifecycleCursor` tracks its next logical sequence. Reading copies bounded
records into caller storage; cursor acknowledgement advances only after a
successful read/consumer operation.

If a consumer falls behind retained history, the API reports resync required. It
does not silently skip records or allocate an unbounded backlog. Consumers then
rebuild their projection from authoritative state and capture a new cursor.

Enabling lifecycle capacity is explicit in world options. Disabled worlds pay no
ring reservation. Snapshot diagnostics expose capacity/sequence/retention facts.

## Usage

Lifecycle is appropriate for incremental presentation, replication, indexes, or
audit consumers that need topology history. A newly connected or lagged consumer
still requires a complete baseline/resnapshot; lifecycle is not a complete world
serialization format.

Consumers needing component values pair lifecycle with a baseline or prepared
lookup/query under the relevant authority boundary.
