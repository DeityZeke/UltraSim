# Network N1.4 dirty-publication scheduling implementation stop

Captured on 2026-08-22 from the Release benchmark. This is semantic server-side
work-discovery evidence, not a production AOI adoption, transport CCU, or public
WAN claim.

## What changed

- authoritative publication exposes its already-computed sorted affected chunks;
- a bounded Game/Replication reverse index turns one dirty chunk into recipient
  work without polling every current scope;
- Incoming, Dirty, and Leaving remain separate from encoding/transport/proof;
- small cumulative anchor motion uses an exact conservative threshold budget;
- remote work resolves by `SpatialChunkKey`, not retained subscription-array index.

## Selected N1 results

| Recipients | Memberships | Clean observation p95 / recipient | One dirty chunk p95 / recipient | Recipient + scheduler reserve |
|---:|---:|---:|---:|---:|
| 1 | 497 | 690.000 ns | 29,929.750 ns | 368,488 B |
| 32 | 15,904 | 71.953 ns | 751.164 ns | 11,850,808 B |
| 128 | 63,616 | 5.898 ns | 397.398 ns | 48,020,632 B |
| 256 | 127,232 | 1.025 ns | 230.633 ns | 93,661,096 B |

At 256 recipients, one changed publication chunk therefore costs about 59 us p95
in aggregate for observer-independent commit discovery, fanout, bounded history
query, and immediate packet-free semantic application. Current clean scopes issue
no publication query.

The stable-anchor fast path avoids the old full query while cumulative movement is
inside its exact budget. A real one-chunk crossing still records 62.691 us p95 per
recipient in this broad 503-subscription fixture; transition-tail scheduling is
not closed.

## Same-machine N0 control

The separate current-build N0 control uses one dirty entity and 256 recipients at
one shared anchor, so it owns one relation per recipient. It records:

- canonical relation update: 139.269 us p95;
- pending delivery + immediate application ACK: 672.185 us p95;
- all-recipient same-cell movement: 1,148.300 us p95;
- reserved bytes: 18,940,132 B.

This is a narrow causal control, not the complete N1.4 A/B matrix. N1's dirty
fanout is materially cheaper in this shape, but its first broad subscription and
reverse-index layout reserves much more memory. Neither result authorizes N0
deletion or a 4K extrapolation.

## Remaining adoption gates

- 512/768/1024 crossover and 2048/4096 attribution profiles;
- Dense-400 and dense-town entry, including the one-snapshot-candidate watch;
- boundary/corner roaming and synchronized crossing tails;
- mixed public/private entitlement;
- same-process semantic, transport replay, and sharded end-to-end evidence;
- subscription/cursor and reverse-index layout measurement/compaction;
- freshness, proof recovery, and bounded lifetime under continuous roaming.

Source artifacts:

- `network-n1.4-dirty-publication-scheduling-2026-08-22.json`
- `network-n1.4-n0-shared-one-dirty-256-control-2026-08-22.json`
