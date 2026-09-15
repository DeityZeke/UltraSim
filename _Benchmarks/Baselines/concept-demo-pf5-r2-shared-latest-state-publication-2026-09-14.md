# Concept Demo PF5-R2 — Shared Latest-State Publication

## Disposition

**Outcome A — replacement accepted.**

One authoritative latest-state publication is now prepared once per dirty chunk interval and may
serve many independently lagging recipients. Delivery identity, queue admission, application
session/generation, proof, applied cursor, retry, cadence, and recovery remain recipient-local.
Ordinary selected steady state made **zero** calls to the retained-history reconstruction control.

PF6 may be reconsidered only after independent review of the new correctness candidate. This
checkpoint does not itself perform that review or begin human validation.

The complete raw rows and all bounded divergence records are retained in the
[raw JSON](concept-demo-pf5-r2-shared-latest-state-publication-2026-09-14.raw.json). The compact
[accepted JSON](concept-demo-pf5-r2-shared-latest-state-publication-2026-09-14.json) preserves the
closure facts and links the raw artifact by SHA-256.

## Identity and policy

| Item | Result |
| --- | --- |
| Starting HEAD | `02fb6a3d3b15c89273467ceed613215f869ed8b5` |
| PF5-R1 wording correction | `7f5209f153861690d4a90b1f0595f78e5a1a4333` |
| Production implementation | `992fc541460f18534da9036c73cb711277de11ce` |
| New correctness candidate | `992fc541460f18534da9036c73cb711277de11ce` |
| Measurement head | `5e41673a6967e54c951a2a5b2f8ddbedef003fa3` |
| Strengthened removal gate | `d1b4b5e74271ae487d67778526947e7fa9319f82` |
| Prior correctness candidate | `af9fcb740c02eb08efa8d4985d929b50b135504e` |
| Runtime / host | .NET 8.0.29, Linux, Ryzen 7 7800X3D, 16 logical processors |

The internal authoritative preparation model changed, so candidate lineage advances. External
Game semantics and wire representation did not change. The selected policy remains:

```text
ChunkPublication
Message129WhenBeneficial
message 148 LZ4 at canonical >=384 bytes and net saving >=32 bytes
Immediate/Near/Far 1/2/4
CrossLane unreliable aggregation cap 2
60 Hz authority / participant cap 64
Game manifest v29 / transport revision 2
```

PF5-R1 evidence wording was corrected before implementation. Its large runtime oracle established
hash/count/materialized-size consistency, not mathematical record equality. PF5-R2's deterministic
equivalence regression directly compares every logical record and the canonical Delta-120 bytes.

## Exact prepared-publication model

Each authoritative commit already knows the affected chunk and its prior/current revisions. For
each dirty chunk interval, `ChunkPublicationStore` now freezes one bounded source artifact:

```text
PublicationEpoch
SpatialChunkKey
FromRevisionExclusive
ToRevisionInclusive
fixed WorldPublicDeltaRecord sequence
    Present = complete current public row for that mutation
    Remove  = explicit revision-fenced tombstone
```

The artifact contains no recipient identity or mutable recipient state. A fixed open-addressed
lookup resolves `(chunk, from revision)` to the next prepared interval. A recipient read follows
that contiguous chain to current source revision and materializes its bounded cumulative result.
It never walks raw mutation history or rereads current entity rows.

The authoritative mutation path still owns one globally bounded source-storage reservation. The
old history-derived implementation is accessible only to friend tests/benchmarks; every public
store constructor selects prepared latest state.

### Coverage and supersession

The recipient's applied cursor remains the only start of known coverage. A prepared interval is
usable only if its `FromRevisionExclusive` exactly matches the current coverage frontier. Missing
any interval causes Rebase; a higher revision alone is never accepted as complete coverage.

A cumulative delivery may collapse repeated positive mutations for the same public identity to
the latest complete current row, retaining the union of changed fields. It may not skip an
unrelated earlier entity fact. A later positive for another slot does not discharge an earlier
removal; both remain in the cumulative result.

A complete Present for a newer generation of the **same reused public slot** is different: it
replaces the removed prior occupant in the cumulative current truth. Generation ordering keeps old
traffic stale. This rule does not apply across unrelated slots.

### Recipient coverage and proof

Prepared source truth does not advance a recipient. `OfflineDirectChunkRecipient` and
`RemoteChunkWorldStateServer` retain their bounded per-recipient subscription, exact applied
cursor, delivery ID, outstanding ledger, application session/generation, proof ticket, and retry
state. Network refusal remains transactional: no delivery, outstanding debt, proof obligation, or
cursor advancement exists before successful queue admission.

Later cumulative exact proof may safely advance over an older in-flight delivery only because that
later delivery was constructed from the same proven start and contains every required contiguous
fact. Out-of-order/duplicate/stale/wrong-lifetime proofs remain harmless.

### Introduction, Rebase, and removal

First knowledge still copies complete relevant current truth at R0. Capturing it establishes
tracking origin, not applied knowledge. Only its matching application proof establishes R0; any
prepared R1+ facts remain outstanding while the introduction is in flight.

If prepared coverage has been evicted, a complete current Rebase is sent. Snapshot application
atomically replaces the client chunk and removes omitted stale rows. Slow recipients cannot pin
source storage.

Removal lives in two distinct places:

```text
source:     immutable prepared Remove record, bounded by source retention
recipient:  independent delivery/proof obligation in the existing ledger
```

If the source tombstone is no longer retained before recipient proof, a complete Rebase restores
exact current presence/absence instead of inventing a gap.

## Correctness results

All 164 five-profile Game replication tests passed. New focused gates prove:

- direct field-for-field old/new logical record equality and bit-for-bit canonical delta bytes;
- one prepared source publication serving eight recipients with eight independent delivery/proof
  identities; A's proof cannot satisfy B;
- lag across several publications, unrelated positive state plus retained removal, exact public
  slot generation reuse, and out-of-order proof;
- a three-record retention bound evicting at least six intervals and safely rebasing the slow
  recipient to current truth;
- fixed-total topology source scaling of 1/8/64/512 while recipient references remain 512;
- the existing Introduction R0 mutation/removal race, AOI leave/re-entry, source epoch reset,
  reconnect fencing, queue refusal, malformed/stale proof, atomic snapshot omission, impairment,
  and warmed zero-allocation contracts on the now-selected path.

The removal regression observes tombstone high-water at least one and a five-publication cumulative
coverage path. No selected steady-state test or measured run calls the old history reconstruction.

## Sharing and divergence census

The production-shaped 128-recipient fixture produced 5,400 source record mutations across 2,160
dirty chunk intervals. Those became exactly 2,160 prepared publications / 5,400 prepared records,
then served 284,861 recipient reads referencing 284,861 publications / 714,639 records—about
131.88 publication references per source preparation and 132.34 record references per frozen
source record. It delivered 276,480 semantic publications and proofs. Old history calls: zero.

The divergence attacks retained shared source truth even when recipient coverage differed:

| Scenario | Prepared | References | References/prepared | Coverage-chain high-water | Intro/reconnect | Rebase | Eviction |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| synchronized | 1,456 | 46,080 | 31.65 | 1 | 512/0 | 0 | 0 |
| staggered joins | 1,456 | 38,768 | 26.63 | 1 | 512/0 | 0 | 0 |
| cadence phases | 1,456 | 46,080 | 31.65 | 90 | 512/0 | 0 | 0 |
| loss | 1,456 | 48,786 | 33.51 | 3 | 512/0 | 0 | 0 |
| reorder | 1,456 | 57,472 | 39.47 | 2 | 512/0 | 0 | 0 |
| proof delay | 1,456 | 181,248 | 124.48 | 4 | 512/0 | 0 | 0 |
| temporary stalls | 1,456 | 46,080 | 31.65 | 31 | 512/0 | 0 | 0 |
| AOI churn | 1,456 | 42,464 | 29.16 | 1 | 2,048/0 | 0 | 0 |
| reconnect | 1,456 | 46,080 | 31.65 | 1 | 512/64 | 0 | 0 |
| history pressure | 1,440 | 42,112 | 29.24 | 1 | 512/0 | 128 | 1,184 |
| combined divergence | 1,456 | 32,192 | 22.11 | 12 | 1,826/64 | 0 | 944 |

Ordinary loss, reorder, delayed proof, stalls, cadence skew, and AOI/reconnect divergence therefore
stay on retained prepared publications. Only explicit retention exhaustion rebases. No recipient
reconstructs raw history.

## Fixed-total topology

Each row retained 512 recipient-publication references and equivalent one-record semantic results:

| Chunks × recipients/chunk | Source preparations | Recipient references | Old history calls |
| --- | ---: | ---: | ---: |
| 1 × 512 | 1 | 512 | 0 |
| 8 × 64 | 8 | 512 | 0 |
| 64 × 8 | 64 | 512 | 0 |
| 512 × 1 | 512 | 512 | 0 |

Source work now follows unique dirty source/chunk publications. Recipient delivery/proof work still
correctly follows recipient obligations.

## Bounds and memory

The production-shaped fixture reserves fixed capacity for 65,536 publications and 65,536 records.
Observed high-water was 2,450 retained publications and 7,000 records, with no tombstones in this
positive-only workload. Prepared storage reserves 13,766,560 bytes. At 128 recipients, server
replication reservation increased from 51,138,624 to 64,905,184 bytes by exactly that amount;
Network server reservation remained 53,712,939 bytes.

The pressure fixture used only 256 publication/record slots, retained 256/256, evicted 1,184, and
caused 128 explicit current-state Rebases. Its prepared reservation was 55,040 bytes. The smallest
focused slow-recipient gate uses capacity three and proves eviction plus exact recovery.

This R2 implementation deliberately retains the old bounded `HistoryEntry`/link pool as source
linkage and comparison scaffolding, so prepared record storage is additive. Removing that duplicate
storage is a future bounded-memory refinement, not required for semantic or CPU closure.

## Network-and-remote-replication performance

Measurements used fresh harnesses, unrecorded priming for both implementations, 30 warmup ticks,
270 measured ticks, alternating A/B order, three samples through 128, and detailed stage clocks
off. Values through 128 are the median of the three sample percentiles.

| Recipients | Model | Schedule p50/p95/p99/max ms | Network+replication p50/p95/p99/max ms |
| ---: | --- | --- | --- |
| 64 | history | 0.916 / 1.199 / 1.287 / 1.448 | 1.338 / 1.650 / 1.747 / 1.936 |
| 64 | prepared | 0.733 / 0.923 / 0.989 / 1.022 | 1.170 / 1.397 / 1.464 / 1.509 |
| 96 | history | 1.372 / 1.720 / 1.875 / 1.896 | 2.014 / 2.390 / 2.565 / 2.608 |
| 96 | prepared | 1.065 / 1.318 / 1.370 / 1.448 | 1.703 / 1.995 / 2.042 / 2.187 |
| 128 | history | 1.836 / 2.274 / 2.486 / 2.529 | 2.723 / 3.220 / 3.377 / 3.435 |
| 128 | prepared | 1.447 / 1.729 / 1.832 / 1.939 | 2.336 / 2.658 / 2.806 / 2.855 |

At 128, schedule p95 falls **23.96%** and the retained `NetworkAndRemoteReplication` p95 falls
**17.46%**, leaving 1.342 ms of headroom under that metric's 4.000 ms engineering budget. This
historical metric begins after authoritative mutation/source publication preparation; it must not
be described as the total replacement cost. PF5-R2-R1 subsequently added a direct same-tick
`AuthorityThroughNetworkAndRemoteReplication` distribution and measured 12.302 ms old versus
11.961 ms prepared at 128 (**2.77% lower p95; 2.40% lower mean**). The broader interval includes
the interleaved client transport/application/proof work required to reach the same endpoint. See the
[timing-boundary closure](concept-demo-pf5-r2-r1-timing-boundary-closure-2026-09-14.md).

Dense single-sample scaling was:

| Recipients | History inclusive p95 | Prepared inclusive p95 |
| ---: | ---: | ---: |
| 160 | 4.119 ms | 3.336 ms |
| 192 | 4.566 ms | 4.066 ms |
| 256 | 6.575 ms | 5.408 ms |
| 300 | 7.519 ms | 6.362 ms |
| 350 | 9.046 ms | 7.711 ms |

The old control first fails the 4 ms budget at tested 160; prepared first fails at tested 192.
These are synthetic rated recipients, not product CCU.

### Wire and freshness guardrail

At 128, both modes produced semantic digest `03AAAD54B49C08E1`, 276,480 publications/proofs,
43,154 Game messages, and 69,546 datagrams. Median UDP payload was 40,850,532 history versus
40,852,075 prepared bytes: +1,543 bytes (+0.0038%). Fresh harnesses choose different publication
epochs, which slightly changes independently compressed bytes. The same-lineage deterministic gate
proves canonical delta bytes bit-identical. Cadence, message composition, proof count, and semantic
work are exact, so no freshness or wire policy was traded for CPU.

## Product and export controls

The complete canonical Release verifier passed at the implementation candidate, including
SinglePlayer packet-free composition, ServerOnly/ClientOnly/Full assembly gates, actual Godot
exports, cold/warm Contentless Online, Complete packet-free local + remote composition, and F0.7
mixed-session lifecycle.

The subsequent PF5 product verifier passed on the measurement lineage:

- 16 real ClientOnly bots under each light, moderate, and heavy-plausible external UDP impairment;
- one reconnect in each, exact action conservation, zero stale replay, and 17/17 clean leaves;
- 8 bots + 2 actual exported Online clients through external loss/reorder, 11 connection lifetimes;
- 16 bots + 2 actual exported Online clients, 19/19 joins/leaves, one authority generation;
- both X/Y native clients retained public actors, nameplates, Chat, presentation, fresh content
  application proof, and clean retirement;
- the 18-client control recorded authority p95 314 us, zero deadline misses/catch-up ticks, and
  12,227,974 / 5,603,556 server UDP payload bytes sent/received over the measured window;
- Online remained contentless: zero bundled packages/WorldPacks; each cold native fetched six
  authorized files / 7,669,828 HTTP body bytes separately from UDP.

## Remaining risks and next step

- Prepared reads still do bounded recipient-local chain traversal, collapse, and sort. Those are
  delivery-specific materialization costs, not raw-history reconstruction.
- Prepared storage currently duplicates some old comparison/linkage storage and adds 13.77 MB in
  the representative store. A later memory cleanup may remove that redundancy without changing
  this model.
- Retention sizing can cause routine Rebase if undersized for a deployment; the behavior is safe
  and bounded but still operationally measurable.
- Above-cap synthetic results remain architecture curves, never CCU claims.

The central PF5-R2 question is answered **yes**: source truth is now prepared once and safely serves
independently lagging/proven recipients without sharing their proof, absence, or coverage state.

The independent adversarial review accepted correctness candidate
`992fc541460f18534da9036c73cb711277de11ce`. PF5-R2-R1 then corrected its remaining timing-reporting
boundary without changing replication production code or semantics. Its canonical closure run
independently exposed a pre-existing `StaticContentHttpHost.Dispose` shutdown race, so PF6 remains
paused for that unrelated repair and a clean full Release verification.
