# PF5-R4 — recipient preparation and encoding flattening

Status: **CLOSED — Outcome A.**

## Identity and policy

- Starting HEAD and exact BEFORE base: `8f6f1cba4979fe2121174d33a64d7565e8277e5b`.
- PF5-R2 replication semantic lineage: `992fc541460f18534da9036c73cb711277de11ce`.
- Prior integrated candidate: `f2940afa18d69bb83df291215dcb6903938ee3c2`.
- PF5-R4 production implementation candidate: `15aa839f865db18c1c47b72fb49ff5a6d416beb4`.
- PF5-R3 outcome: accepted Outcome A; production remains `BackgroundPlatformBatch`.
- Primary serial control: inline `PlatformBatch`, not inline scalar. Scalar remains secondary PF5-R3 evidence.

No Game semantics, delivery/proof semantics, AOI, cadence, representation selection, compression,
aggregation, message ID, Game manifest, transport revision, MTU, participant policy, or wire format
changed. The selected policy remains ChunkPublication, message 129 when beneficial, selective
message-148 LZ4 at 384/32, Immediate/Near/Far 1/2/4, CrossLane cap two, Game manifest v29, and
transport revision 2.

## Repairs

1. `de841586` sorts compact prepared indices before constructing each 72-byte
   `WorldPublicDeltaRecord` once in canonical order. `a849af9c` adds the small-count insertion
   path; 0/1 records avoid sorting.
2. `80222de6` adds an internal trusted prepared-standalone writer. Public/untrusted writers and
   all readers keep complete validation. The trusted path consumes only the internally owned,
   already validated records and existing canonical content hash.
3. `126222a1` carries at most one prepared candidate across adjacent local message splits in one
   drain. It fences source epoch, key, delivery ID, subscription index, and applied baseline. A
   queue refusal invalidates the local carry without advancing delivery, cursor, proof, or debt.
4. `15aa839f` corrects benchmark accounting after one drain became able to emit more than one
   message. It reports actual bundled publications instead of inferring that all successes were
   bundled when any bundle was emitted. This was a reporting defect only; captured bytes and
   messages were already unchanged.

The six-publication packet-split regression now records six builds, five carries, five local
reuses, six messages, and six independent proofs. The previous control flow performed eleven
preparations. The queue-refusal regression fills the bounded Network queue, verifies no delivery
ID/cursor/proof/debt advancement, then retries and proves both publications.

## Matched synthetic controls

The exact BEFORE and AFTER controls use 1,000 entities, identical positions/AOI/cadence/seed,
30 warmup ticks, 270 measured ticks, and low `dirty=20` versus high `dirty≈35% of R`.
Heavy attribution is off in these rated rows.

| R | Dirty | Server-owned p95 before | After | Change | Schedule p95 before | After | Change |
|--:|--:|--:|--:|--:|--:|--:|--:|
|128|20|1.859 ms|1.855 ms|-0.2%|0.987 ms|0.948 ms|-3.9%|
|128|45|3.747 ms|2.734 ms|-27.0%|2.835 ms|1.770 ms|-37.6%|
|192|20|2.950 ms|2.992 ms|+1.4%|1.537 ms|1.532 ms|-0.3%|
|192|67|9.407 ms|5.840 ms|-37.9%|7.243 ms|3.601 ms|-50.3%|
|256|20|3.808 ms|4.807 ms|+26.2% tail|1.931 ms|2.134 ms|+10.6%|
|256|90|16.181 ms|9.705 ms|-40.0%|12.365 ms|5.750 ms|-53.5%|
|300|20|4.818 ms|4.881 ms|+1.3%|2.391 ms|2.329 ms|-2.6%|
|300|105|23.803 ms|13.851 ms|-41.8%|17.861 ms|7.387 ms|-58.6%|
|350|20|6.299 ms|5.948 ms|-5.6%|3.148 ms|2.941 ms|-6.6%|
|350|122|28.758 ms|16.461 ms|-42.8%|21.796 ms|10.050 ms|-53.9%|

The isolated low-change R256 p95 tail is not treated as a regression signal: its server-budget
p50 changed only 3.711 to 3.777 ms, adjacent rows do not reproduce the tail, and exact semantic
work is unchanged. The high-change workload—where the duplicated paths exist—shows a consistent
27–43% server-owned p95 reduction and 38–59% schedule/codec p95 reduction.

At R300/high over the separate 90-tick Detailed window, 114,732 semantic candidates require
114,732 builds. 79,024 packet-boundary carries are reused locally with zero invalidations. The old
control flow necessarily built those 79,024 candidates again. The new path performs 114,732
validation/hash passes over 2,064,056 records and 98,064 trusted standalone writes. The old path
also revalidated/rehashed every standalone and therefore performed 291,820 total preparation or
standalone validation/hash passes versus 114,732 now.

Detailed attribution has material clock overhead and is not used as rated p95 evidence. On its
same-work R300/E300/dirty105 control, aggregate source-read ticks fall 300,258,849 to 102,639,173,
record/index sort ticks 168,353,224 to 23,669,802, materialization ticks 31,221,036 to 17,987,423,
and standalone-encode ticks 371,744,396 to 20,289,895. The E1000/dirty350 attribution control
shows the same direction: source read -67.1%, sort -87.3%, and standalone encode -94.4%.

## Compact-sort microbenchmark

Both variants allocate zero warmed managed bytes. The old side constructs and heapsorts complete
records; the new side sorts compact indices and constructs records in final order.

| Records | Old mean | Index-first mean | Change |
|--:|--:|--:|--:|
|1|6.4 ns|21.0 ns|tiny-case loss|
|4|107.3 ns|137.2 ns|-27.9%|
|8|243.8 ns|92.1 ns|+62.2%|
|16|547.3 ns|236.0 ns|+56.9%|
|30|1,257.6 ns|544.7 ns|+56.7%|

The micro result is not extrapolated into whole-server savings. It supports the implementation
choice at representative 8–30-record sizes; the rated results remain authoritative.

## Real-UDP 2×2 matrix

This is the requested primary matrix: Serial means inline `PlatformBatch`; Parallel means
`BackgroundPlatformBatch`. It uses real secure loopback UDP, the production listener/peer paths,
client decode/application and exact proof, E=1000, dirty=20, 60 warmup and 270 measured ticks.
Detailed probes are off.

| R | Implementation | Egress | Remote p95 | p99 | Process CPU | UDP datagrams | UDP bytes |
|--:|:--|:--|--:|--:|--:|--:|--:|
|128|Before|Inline PlatformBatch|2.808|2.936|2,950 ms|69,644|40,761,523|
|128|Before|Background PlatformBatch|2.543|2.640|2,950 ms|69,644|40,837,217|
|128|After|Inline PlatformBatch|2.735|2.790|2,940 ms|69,644|40,761,523|
|128|After|Background PlatformBatch|2.672|3.296|2,990 ms|69,644|40,837,217|
|192|Before|Inline PlatformBatch|4.405|4.564|4,520 ms|104,608|61,133,077|
|192|Before|Background PlatformBatch|3.883|4.082|4,530 ms|104,608|61,287,410|
|192|After|Inline PlatformBatch|4.022|4.257|4,380 ms|104,608|61,133,077|
|192|After|Background PlatformBatch|3.836|4.820|4,490 ms|104,608|61,287,410|
|256|Before|Inline PlatformBatch|6.025|6.288|6,180 ms|139,582|81,727,646|
|256|Before|Background PlatformBatch|5.536|5.718|6,220 ms|139,582|81,856,777|
|256|After|Inline PlatformBatch|5.602|5.951|6,030 ms|139,582|81,727,646|
|256|After|Background PlatformBatch|4.892|4.995|5,940 ms|139,582|81,856,777|
|300|Before|Inline PlatformBatch|7.141|7.387|7,320 ms|163,796|95,576,598|
|300|Before|Background PlatformBatch|6.495|6.828|7,300 ms|163,796|95,838,762|
|300|After|Inline PlatformBatch|6.589|8.379|7,000 ms|163,796|95,576,598|
|300|After|Background PlatformBatch|6.003|7.418|7,090 ms|163,796|95,838,762|
|350|Before|Inline PlatformBatch|8.634|8.964|8,660 ms|190,998|111,603,784|
|350|Before|Background PlatformBatch|7.644|8.125|8,650 ms|190,998|111,643,267|
|350|After|Inline PlatformBatch|7.594|7.904|8,230 ms|190,998|111,603,784|
|350|After|Background PlatformBatch|7.706|8.769|8,610 ms|190,998|111,643,267|

The final randomized R350 background row contains a host-tail outlier; an immediate isolated
same-code rerun measured 7.457/8.051 ms p95/p99. The matrix retains the original randomized row
rather than replacing it. The first sampled p95 above 4 ms remains 192 for inline batching and
256 for background batching, both before and after. Inline PF5-R4 improvement is 2.6%, 8.7%,
7.0%, 7.7%, and 12.0% at 128/192/256/300/350. Background improvement is noisy at the endpoints
but reaches 11.6% at 256 and 7.6% at 300. PF5-R3's threading benefit and PF5-R4's removed work
therefore compose, without claiming strict additivity.

Every before/after cell has exactly the same semantic digest, 276,480–756,000 publications and
proofs, Game-message count, actual bundle entries, application bytes, UDP datagrams, and UDP
payload bytes for its population/mode. The small inline/background byte difference already belongs
to ordinary asynchronous proof timing; PF5-R4 adds none.

The separate timing-enabled AFTER run reports background worker-service wall time (managed
per-thread CPU is unavailable) of 97.3/144.0/185.1/220.8/253.5 ms and queue p99 of
0.354/0.524/0.621/0.785/0.813 ms at R128/192/256/300/350. Queued-to-proof p95 is
13.160/23.564/26.892/31.644/36.130 ms. Source-to-proof p95 remains two authority steps except one
noisy R192 observation at three. All queues retire to zero; there are no capacity refusals or
worker failures. These Detailed/timestamped values are context, not the rated p95 rows.

## Representation cliff retained for the next checkpoint

PF5-R4 intentionally does not alter representation selection. At R300/high over 270 ticks,
343,032 semantic publications contain 293,364 standalone message-120 publications (85.52%) and
49,668 publications in 24,834 two-entry bundles; all 24,834 bundles select message 148. Thus
293,364 candidates remain ineligible for compression solely because the current bundle policy
requires at least two entries. That is the bounded input for a separate finding-4 experiment.

## Memory, correctness, and verification

The repaired paths remain allocation-free after warmup. No persistent recipient cache or retained
reservation is added. Candidate carry is stack scoped to one drain and at most 64
`RemoteChunkDeltaRecord` values (approximately 3,072 bytes at the current 48-byte managed layout).

All 166 replication tests pass, including introduction mutation/removal, independent proof,
out-of-order/duplicate/stale proof, coverage gap, tombstone/absence, slot reuse, eviction/Rebase,
slow recipient, AOI leave/reentry, source replacement, reconnect, custom MTU, compression,
packet split, canonical standalone bytes, warmed allocation, and the new queue-refusal test.
Focused PF5-R2, PF5-R2-R1, PF5-R2-R2, PF5-R3, and PF5-R4 gates pass. Canonical Release/export
verification also passes at evidence commit `c68454ea1e110f73b86834e95e29fbb54c885e42`, including
the physical Single Player/Online/Complete exports, Contentless Online cold/warm application,
mixed exported-client plus bot accounting, Complete hosting, and mixed-session lifecycle gates.

## Disposition

Findings 1, 2, and 3 are accepted. This is Outcome A. The cleanup removes work that had no
semantic purpose, preserves exact recipient-local admission/delivery/proof knowledge, and composes
with the selected bounded background sender.

The dominant remaining high-change costs are legitimate per-recipient canonical validation/hash,
scheduling, outstanding delivery/proof bookkeeping, and the large standalone-120 population.
Finding 4 may proceed as a separate representation experiment; it must not be mistaken for more
CPU-only cleanup.

Machine-readable evidence, exact raw-file hashes, and recomputable counters are in the
[companion JSON](concept-demo-pf5-r4-recipient-preparation-flattening-2026-09-15.json).
