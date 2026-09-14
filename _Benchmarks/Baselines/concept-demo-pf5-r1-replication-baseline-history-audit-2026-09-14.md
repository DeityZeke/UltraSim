# Concept Demo PF5-R1 — Replication Baseline and History-Model Audit

## Disposition

**Outcome B — reject history-derived latest-state as the fundamental steady-state model.**

This does **not** reject a complete current-state snapshot when a recipient first learns an AOI
scope, or a current-state rebase after retained history can no longer bridge a gap. Those are
necessary bounded costs. The finding is narrower:

> A snapshot should establish history's starting point. Retained mutation history should not be
> the database from which essentially identical steady-state latest truth is reconstructed once
> per recipient.

PF6 remains paused. The next checkpoint should design explicit `Introduction`/`Rebase` operations,
shared immutable current/latest source preparation, recipient-local exact applied knowledge, and
explicit removal/absence debt. PF5-R1 did not implement that design.

The complete machine-readable results, bounded range records, histograms, retention census, and
timing rows are in
[the PF5-R1 JSON](concept-demo-pf5-r1-replication-baseline-history-audit-2026-09-14.json).

## Identity and invariants

| Item | Result |
| --- | --- |
| Starting HEAD / measurement base | `61638fc70291bc117a5e933bf48bb04a19de0141` |
| Measured instrumentation commit | `ca57f1eb23695ae7017ac3b3fd58acd5a9cf3876` |
| Feature-complete correctness candidate | `af9fcb740c02eb08efa8d4985d929b50b135504e` — unchanged |
| Production semantics changed | No |
| Runtime / host | .NET 8.0.29, Linux, 16 logical processors, 1 GHz stopwatch |
| Replication | `ChunkPublication`, `Message129WhenBeneficial` |
| Compression | message 148 LZ4; try canonical 129 at 384 bytes; require 32-byte saving |
| Cadence / aggregation | Immediate/Near/Far `1/2/4`; CrossLane cap 2 |
| Protocol / product | Game manifest v29; transport revision 2; 60 Hz; participant cap 64 |

All PF5-R1 diagnostics are opt-in, owner-thread-bound, fixed-capacity, cold observations. The shadow
oracle never supplies a production record and cannot change read selection, delivery identity,
queue admission, proof, cursor advancement, or wire bytes. The large runtime census reported zero
hash/count/materialized-size consistency mismatches and its bounded range table did not overflow.
That is strong diagnostic evidence, not mathematical proof of record equality; the focused
regression separately performs direct record comparison.

## Current model found in code

`ChunkPublicationStore` owns packed current rows plus one globally bounded history pool. History is
linked per chunk, but capacity and eviction are global. For every ordinary delta request,
`TryCopyDelta`:

1. starts at the chunk history head and examines all retained nodes;
2. skips nodes at or before the recipient's proven cursor;
3. collapses newer repeated mutations by public slot;
4. rereads current positive records (removals remain explicit history entries);
5. sorts and emits the recipient's delta.

`OfflineDirectChunkRecipient` owns each recipient/chunk applied revision. A new subscription has
interest/tracking origin but `HasKnowledge=false` and no applied cursor. It copies complete current
truth for that relevant chunk at revision R0. Only packet-free local commit or an authenticated
matching remote proof advances knowledge to R0. Changes newer than R0 remain debt. Reconnect and
source-epoch replacement start fresh lifetimes.

This is correct today, but snapshot causes previously obscured the distinction between first
introduction, reconnect, source reset, explicit recovery, prior absence becoming present, invalid
cursor/key, and history-floor loss. PF5-R1 now records those causes separately without changing
behavior.

## Source-work census

The synchronized 64-recipient run is the clearest steady-state result:

| Metric | Result |
| --- | ---: |
| Delta requests | 46,080 |
| Distinct `(epoch, chunk, from, to)` ranges | 720 |
| Repeated ranges | 45,360 (98.4375%) |
| History nodes examined | 3,018,240 |
| Nodes skipped at/before cursor | 2,972,160 |
| Emitted/current-record lookups/sorted records | 46,080 / 46,080 / 46,080 |
| Mean history nodes examined per request | 65.5 |
| Mean records emitted per request | 1.0 |
| Prepare-each-distinct-range-once timing ceiling | 97.62% of measured source preparation |

Thus a one-record latest-state result commonly required walking dozens of retained mutations, and
the same canonical range was reconstructed for almost every observer. The examined-node histogram
places the synchronized median at at most 128 nodes and p95 at at most 128 nodes; every request
emitted exactly one record.

The production-shaped 128-recipient real-UDP fixture was stronger:

| Metric | Result |
| --- | ---: |
| Delta requests / distinct ranges | 284,861 / 2,160 |
| Repeated exact ranges | 99.2417% |
| Record instances / distinct-range records | 714,639 / 5,400 |
| Repeated record instances | 99.2444% |
| History nodes examined / skipped | 24,858,714 / 24,144,075 |
| Mean nodes / records per request | 87.27 / 2.51 |
| Full measured source preparation | 187.09 ms over 270 measured ticks |
| First preparation of each distinct range | 4.00 ms total |
| Shadow repeat-source ceiling | 183.10 ms total, 97.86% of source preparation |

The 183.10 ms difference is an oracle ceiling, **not** a promised production win. It is about
0.678 ms per measured tick, but recipient scheduling, message representation, queue admission,
delivery IDs, proof tickets, retries, and cursor/absence knowledge remain recipient-specific.
Detailed instrumentation raised the Network-plus-replication p95 from a 3.507 ms bracketing-control
mean to 3.755 ms (+7.07%), so source clocks are interpreted as attribution evidence rather than a
new production performance baseline. Both uninstrumented bracketing runs retained the selected
semantics and real UDP convergence.

## Attempts to destroy synchronization

| Scenario | Requests | Distinct ranges | Repeat ranges | Nodes/request | Records/request | Shadow source ceiling |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Synchronized | 46,080 | 720 | 98.44% | 65.50 | 1.00 | 97.62% |
| Staggered joins | 38,768 | 720 | 98.14% | 72.14 | 1.00 | 97.89% |
| Different cadence phases | 11,904 | 736 | 93.82% | 66.94 | 2.15 | 90.21% |
| Controlled loss | 46,115 | 1,420 | 96.92% | 65.53 | 1.06 | 96.22% |
| Controlled reorder | 46,080 | 1,432 | 96.89% | 65.50 | 1.25 | 96.50% |
| Proof delay | 46,080 | 720 | 98.44% | 65.50 | 3.93 | 98.34% |
| Temporary stalls | 42,240 | 728 | 98.28% | 65.59 | 1.03 | 97.97% |
| AOI churn | 42,464 | 1,005 | 97.63% | 65.49 | 1.00 | 97.35% |
| Reconnect | 46,080 | 720 | 98.44% | 65.50 | 1.00 | 98.22% |
| History pressure | 42,112 | 720 | 98.29% | 14.54 | 1.00 | 98.24% |
| Combined divergence | 8,205 | 1,977 | 75.90% | 30.80 | 3.92 | 76.86% |

Combined divergence deliberately mixed cadence skew, loss, reorder, proof delay, stalls, AOI churn,
reconnect, and bounded history pressure. It successfully reduced synchronization, but three quarters
of exact ranges and record instances still repeated. This is substantial sharing opportunity, yet
the global-history behavior below means a range cache alone would optimize around the deeper model
rather than resolve it.

## Introduction, rebase, and recovery

The quiet case performed no delta/history reads. Its 512 recipient/chunk first introductions copied
10,240 relevant current records (447,488 canonical application bytes). That is expected work: the
recipients did not possess those facts.

Observed snapshot reasons were explicit:

- ordinary synchronized/loss/reorder/stall scenarios: 512 first-scope introductions only;
- AOI churn: 2,048 first-scope introductions, reflecting newly entered relevant scopes;
- reconnect: 512 initial introductions plus 64 `ReconnectGenerationReset` baselines;
- history-pressure: 512 initial introductions plus 128 `HistoryFloorLost` rebases;
- combined divergence: 1,826 introductions plus 64 reconnect baselines.

No evidence says these baseline copies should be removed. They should instead become explicit
source-side `Introduction(scope, R0)` or `Rebase(scope, Rn)` artifacts, independently delivered and
proven for each recipient.

The maintained introduction-race regression captures R0, mutates and then removes the entity while
R0 is in flight, proves R0, and verifies the removal newer than R0 is still delivered. It also
proves AOI leave/re-entry cannot inherit old knowledge. The architecture-independent invariant is:

> Capturing `Introduction(C,R0)` establishes tracking origin R0 but no applied knowledge; proving
> it may establish only R0; every required mutation/removal newer than R0 remains independently
> deliverable, and old AOI/source/session lifetimes cannot satisfy the new one.

## Global history retention and interference

The pressure scenario used a 256-entry global pool, produced 1,440 history entries, observed 1,184
pressure evictions and 1,184 floor advances, then required 128 correct current-state rebases. In the
combined scenario the pool retained 32 entries for each sampled chunk, with oldest retained audited
commits around 118/119 and newest around 180/181.

A separate two-chunk probe established cross-region interference. With global history capacity 8,
one needed cold-chunk change was followed by 23 mutations in another chunk. Eighteen pressure
evictions advanced floors 16 times; the cold recipient's cursor at revision 2 fell below floor 3
and correctly received `SnapshotRequired`, although the cold chunk itself had not created the
pressure.

This is a safe recovery outcome, not state corruption. It is nevertheless evidence that a global
mutation-rate-dependent pool is an awkward fundamental basis for ordinary latest-state delivery:
hot activity can shorten unrelated scopes' bridge depth.

## Fixed-total topology

All rows held 512 authoritative mutations, dirty chunks, recipient-publication relationships,
delta requests, and emitted records constant.

| Hot chunks × recipients/chunk | Distinct ranges | Repeated ranges | Source prep | Prepare once | Reusable ceiling |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 × 512 | 1 | 511 | 90.64 µs | 0.38 µs | 90.26 µs |
| 8 × 64 | 8 | 504 | 90.48 µs | 1.71 µs | 88.77 µs |
| 64 × 8 | 64 | 448 | 92.38 µs | 12.00 µs | 80.38 µs |
| 512 × 1 | 512 | 0 | 97.94 µs | 97.94 µs | 0 |

Chunk grouping therefore exposes a real source-preparation sharing shape at high fanout, but the
current implementation reconstructs each recipient publication independently. The equal small
source times also show why this micro-fixture alone cannot predict whole-flow savings.

## Source truth versus recipient obligation

Potentially shareable immutable source truth is limited to:

- source epoch, chunk/scope key, source revision interval, current records, removals, and a content
  hash/canonical encoding fact;
- a current-state introduction/rebase captured at a specific source revision;
- a latest-state mutation materialization that does not include a recipient delivery identity.

The following remain independently recipient-owned:

- application session/generation and transport connection;
- queue admission and retry/refusal;
- delivery ID and proof ticket;
- exact applied revision/knowledge;
- negative/absence debt;
- cadence, AOI lifetime, recovery, and cursor advancement.

Shared source preparation is therefore compatible with exact recipient proof, but shared source
truth must never imply shared delivery or applied knowledge.

## V2 comparison

Archived V2 prepared and encoded an authoritative entity payload once, retained/coalesced latest
state by `(item, scope)`, and reused immutable payloads through scope fanout. Per connection it
still selected relevant latest entries. Its `ConnectionLatestStateSentTracker`, however, tracked
sent rather than exactly proven applied sequence; its scheduler, background flush, security,
encoding, and timing boundaries also differ from V3.

The defensible synthesis is therefore V2-style shared immutable source preparation plus V3-style
session/generation fencing, transactional queue admission, exact delivery/proof identity, explicit
negative knowledge, and recipient-local applied state—not restoration of V2.

## Latest state, occurrences, and absence

Current/latest-state candidates include public transform/orientation, prefab/body and public
equipment presentation, door/open and other state flags, and owner-private current state.

Occurrence/history semantics remain necessary for Chat, transactional action results, application
events, and any future fact whose intermediate occurrences must be observed. Those should retain
bounded event/history mechanisms rather than be forced into latest-state replacement.

Positive current truth can often be superseded by a later proven current version. Removal cannot be
rediscovered by scanning only present rows. A future design therefore needs exact recipient-local
absence debt or an immutable, revision-fenced source removal/tombstone publication with independent
proof. PF4B positive supersession must not silently discharge negative obligations.

## Recipient-knowledge memory

The measured high-water was 512 recipient↔chunk relationships and an estimated 10,240 active
recipient↔public-entity relationships (64 recipients × 8 subscribed chunks × 20 records). Packed
design estimates—not current allocations—are:

| Record scope | 16 bytes | 24 bytes | 32 bytes |
| --- | ---: | ---: | ---: |
| Per active recipient↔chunk | 8,192 B | 12,288 B | 16,384 B |
| Per active recipient↔entity | 163,840 B | 245,760 B | 327,680 B |

These figures exclude indexes, array/object headers, alignment, capacity slack, delivery/proof debt,
and absence storage. `WorldPublicRecord.EntityPublicVersion` is useful evidence for an entity/hybrid
design, but it does not itself define recipient knowledge or removal semantics.

## Regressions and remaining risks

Permanent regressions now prove:

- scan/collapse counters and shadow equality for repeated ranges;
- a mutation/removal during in-flight introduction cannot disappear;
- tracking origin is not applied knowledge;
- two recipients sharing canonical source truth retain different proof identities;
- history-floor loss remains explicit recovery;
- explicit recovery, source epoch reset, reconnect, prior absence, and first introduction remain
  distinguishable;
- fresh AOI/source/session lifetimes cannot inherit old proof.

The selected production wire/profile behavior remains unchanged and the dormant audit adds only a
null diagnostic branch. Unresolved design risks are the new model's bounded lifetime/eviction,
atomic introduction capture, exact removal debt, source epoch replacement, and recipient-knowledge
memory under larger real AOI relationships. Those belong to the next design checkpoint.

Verification completed on 2026-09-14:

- `--pf5-r1-only --skip-godot`: passed all 159 replication tests plus the focused semantic,
  topology, pressure, shadow-equality, and real-UDP gates;
- full canonical `./verify.sh --configuration=Release`: passed all kernel, Game, client, server,
  content, persistence, integration, Network-flavor, actual Godot export, Contentless Online,
  mixed native/bot swarm, and lifecycle/composition gates;
- worktree diff/link validation: passed.

## Answer and next checkpoint

How much of `TryCopyDelta` should exist in steady-state latest-state replication?

**Not the recurring recipient-specific retained-list walk, repeated-mutation collapse, current-row
reread, and sort when the same source revision interval is being delivered to many recipients.** A
bounded history bridge still has value for ordinary lag/recovery and exact occurrence semantics.
The source-side latest-state result should be prepared once per authoritative scope/revision fact;
recipient-local work should begin at AOI/cadence selection, queue admission, delivery/proof identity,
and exact knowledge/absence handling.

The next checkpoint should design and prove:

```text
INITIAL / AOI ENTRY / HISTORY EXHAUSTED
    explicit bounded current-state Introduction or Rebase at R0

STEADY STATE
    shared immutable current/latest source publication
    + recipient-local queue/delivery/proof knowledge

ORDINARY LAG
    bounded history bridge

REMOVAL
    explicit exact absence debt
```

PF6 may **not** resume yet. It may resume after that replication-model checkpoint re-establishes the
accepted correctness, product, impairment, export, and performance gates.
