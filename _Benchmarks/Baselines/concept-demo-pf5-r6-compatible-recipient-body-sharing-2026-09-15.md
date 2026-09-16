# PF5-R6 — compatible immutable recipient-body sharing

PF5-R6 started from clean `main` at
`5f416167bcc89e6b68cc1705c92c13460ba3dbd5`. There were no intervening commits.
The selected production policy was, and remains, `BackgroundPlatformBatch`, `CurrentBundled`,
singleton compression disabled, cadence 1/2/4, CrossLane aggregation cap two, Game manifest v29,
and transport revision 2.

## Disposition

**Outcome A — bounded compatible-body sharing is selected for production.** Identical covered
authoritative Delta truth is now materialized and validated once per exact source range, while
application session, delivery id, queue admission, retry, cursor, outstanding debt, and applied
proof remain recipient-local. The production implementation is
`eca422171c6a1c27c6a0e022a9ff493ece5da597`.

The separate dormant singleton experiment cleanup is
`ddf454b36e387f84ebfeae70a665acf765d76402`: selected message 148 no longer performs an unused
standalone-120 serialization. This cleanup does not contribute to the body-sharing result and does
not enable singleton compression.

- PF5-R2 semantic lineage: `992fc541460f18534da9036c73cb711277de11ce`.
- Starting integrated tree: `5f416167bcc89e6b68cc1705c92c13460ba3dbd5`.
- New production implementation candidate: `eca422171c6a1c27c6a0e022a9ff493ece5da597`.
- Measurement tree including the dormant-path cleanup: `ddf454b36e387f84ebfeae70a665acf765d76402`.

No Game message, wire field, manifest, transport identity, cadence, AOI, compression, aggregation,
or proof rule changed.

## Identity, ownership, and bounds

The authoritative lookup key is:

```text
physical ChunkPublicationStore lifetime
+ SourceEpoch
+ SpatialChunkKey
+ BaseRevision
+ CurrentRevision
+ HistoryFloor
```

`RecordCount` is checked as an integrity property; content hash is cached data, never identity.
The store is owned by Game replication alongside one physical publication source and shared by all
remote recipients of that source. Source replacement requires a new source-fenced store. A
reconnect may reuse source truth but always creates a new application epoch, delivery id, and proof
obligation.

The production store has 1,024 body slots, 65,536 fixed record slots, at most 64 records/body, and
four-way set associativity. Lookup is at most four probes. Replacement is bounded round-robin
within the selected set. A fill copies and hashes the complete prepared body before publishing its
explicit valid bit. No body pins source history or is pinned by a recipient.

The fixed enabled reservation is 3,214,848 bytes (3.066 MiB):

| Part | Bytes |
| --- | ---: |
| metadata/index | 60,928 |
| record slab | 3,145,728 |
| bounded diagnostics | 8,192 |

Disabled mode reserves no body arrays. Capacity pressure degrades to eviction and recomputation.
The collision stress used four slots/two ways, forced eight evictions, retained exact latest hits,
rejected an identity/body mismatch, fenced source replacement, and allocated zero managed bytes
over 1,024 warmed hits.

## Correctness boundary

Lookup happens only after the existing recipient `TryBeginRead` transaction has authorized a
Delta. Therefore a resident old body cannot bypass current source retention. The permanent test
advances 132 revisions beyond the stalled recipient's cursor and proves that scheduling returns
Snapshot/Rebase without issuing another body request. Queue refusal after a hit changes no delivery
id, cursor, outstanding debt, or proof obligation; retry may reuse the immutable body.

Three recipient lifetimes in the focused test reuse one body, receive three independent tickets,
and prove independently. Disabled and enabled paths produce identical records, content hash, and
standalone message-120 bytes. Existing canonical bundle, removal, mixed-mask, slot-reuse,
source-epoch, snapshot, loss/reorder, and packet-boundary gates continue through the same writers
and decoders. Valid hash zero remains explicitly valid.

## Measurement method

- Linux x86-64, .NET 8.0.129, AMD Ryzen 7 7800X3D, 16 logical processors.
- Release, deterministic seed `84111361`.
- E=1,000, low dirty=20; high dirty=round(35% of R).
- 60 warmup + 270 measured ticks for rated real secure-UDP rows.
- `BackgroundPlatformBatch`, real client decode/apply/proof, singleton compression OFF.
- Detailed attribution: separate 60-warmup / 90-measured-tick R300 run.
- Rated heavy clocks are disabled; the detailed rows quantify stage costs separately.
- The server and benchmark clients share one process. Process CPU/RSS are fixture context, not
  server-only product resource claims.

Raw report hashes:

| Report | SHA-256 |
| --- | --- |
| final rated Off/On | `ec2b4545ec20c23cb729529045a8df442f0f01876ab310d4dfb1cb9fcd9e74a9` |
| final detailed R300 | `3b83ff1874aff45ca1b130c836fad3fe3be611b31aaeae14ce83f3c440980ce0` |
| census/divergence | `9986d60c37ad6e6b53f2179f18b71d6f4eafb70834a9f3b852d1ecc8cb912833` |
| post-R6 singleton check | `d81cd01352f94c5718901e75bf93d291893fed1b00d4b1c8a557914fb0a39f59` |
| R64 confirmation | `0a178f2d19eb8f54a55bdb93b9e34d06e1b3ef7c6e8c2ab19d6161aebeef30c0` |

## Rated real-UDP body-sharing matrix

Every Off/On pair retained the semantic digest, publication/proof counts, message/bundle choices,
compression selections, and canonical application bytes. Small physical byte/count differences
where present are the already accepted PF5-R3 background-drain timing effect.

| R | Dirty | Sharing | Requests | Fills | Hits | Hit rate | Server p95 ms | Schedule p95 ms | Body MiB |
| --: | --: | --- | --: | --: | --: | --: | --: | --: | --: |
| 128 | 20 | Off | 0 | 0 | 0 | — | 1.533 | 0.954 | 0 |
| 128 | 20 | On | 80,772 | 1,282 | 79,490 | 98.41% | 1.073 | 0.465 | 3.066 |
| 128 | 45 | Off | 0 | 0 | 0 | — | 2.389 | 1.777 | 0 |
| 128 | 45 | On | 80,772 | 1,281 | 79,491 | 98.41% | 1.257 | 0.651 | 3.066 |
| 192 | 20 | Off | 0 | 0 | 0 | — | 2.424 | 1.480 | 0 |
| 192 | 20 | On | 219,603 | 2,629 | 216,974 | 98.80% | 1.821 | 0.855 | 3.066 |
| 192 | 67 | Off | 0 | 0 | 0 | — | 5.193 | 3.803 | 0 |
| 192 | 67 | On | 219,603 | 2,629 | 216,974 | 98.80% | 2.604 | 1.425 | 3.066 |
| 256 | 20 | Off | 0 | 0 | 0 | — | 3.515 | 2.033 | 0 |
| 256 | 20 | On | 294,707 | 2,696 | 292,011 | 99.09% | 2.374 | 1.066 | 3.066 |
| 256 | 90 | Off | 0 | 0 | 0 | — | 8.107 | 5.963 | 0 |
| 256 | 90 | On | 294,707 | 2,696 | 292,011 | 99.09% | 4.231 | 2.120 | 3.066 |
| 300 | 20 | Off | 0 | 0 | 0 | — | 3.931 | 2.266 | 0 |
| 300 | 20 | On | 339,560 | 2,696 | 336,864 | 99.21% | 3.014 | 1.334 | 3.066 |
| 300 | 105 | Off | 0 | 0 | 0 | — | 9.997 | 7.195 | 0 |
| 300 | 105 | On | 339,560 | 2,696 | 336,864 | 99.21% | 5.121 | 2.316 | 3.066 |
| 350 | 20 | Off | 0 | 0 | 0 | — | 5.180 | 3.025 | 0 |
| 350 | 20 | On | 476,981 | 3,707 | 473,274 | 99.22% | 3.931 | 1.824 | 3.066 |
| 350 | 122 | Off | 0 | 0 | 0 | — | 14.255 | 10.612 | 0 |
| 350 | 122 | On | 528,509 | 4,179 | 524,330 | 99.21% | 7.527 | 4.019 | 3.066 |

Across R128–R350, low-change p95 improves 23.3–32.5%; high-change improves 47.2–49.9%.
The 64-recipient confirmation remains sub-millisecond and noisy: low is 0.769/0.885 ms Off/On,
while high is 0.776/0.499 ms. Its matched CPU is 1.255/1.266 cores low and 1.023/1.032 high.
The selected result is based on the stable 128–350 scaling and stage attribution, not that single
sub-millisecond low row.

At R300/high, both policies emit 376,641 UDP datagrams and 312,110,470 UDP payload bytes in the
final rated run. CPU is 1.039/1.048 fixture cores Off/On, queue-residence p99 is 2.097/2.097 ms,
and outstanding proof high-water is five. No capacity drop, socket failure, `WouldBlock`, sender
failure, or convergence failure occurred.

## R300/high detailed attribution

| Metric | Off | On |
| --- | ---: | ---: |
| body requests | 0 | 113,570 |
| distinct bodies / fills | 0 | 896 |
| body hits | 0 | 112,674 (99.21%) |
| requests/body p50 / p95 / p99 / max | — | 139 / 182 / 182 / 300 |
| records requested | 0 | 2,050,452 |
| records built | 0 | 15,529 |
| records served from bodies | 0 | 2,034,923 |
| materialization calls | 113,570 | 896 |
| validation/hash calls | 113,570 | 896 |
| materialization ticks | 11,873,439 | 101,319 |
| validation/hash ticks | 443,218,130 | 3,388,057 |
| body lookup / copy / fill ticks | 0 / 0 / 0 | 4,078,106 / 4,283,191 / 373,816 |
| complete source-read ticks | 104,470,750 | 106,724,971 |
| complete schedule ticks | 807,570,725 | 375,883,664 |
| schedule p95 ms | 9.705 | 4.801 |
| server-owned remote p95 ms | 13.523 | 8.631 |

The store avoids 112,674 materializations and hash calls in this window. Source read is unchanged,
as required by the safe post-`TryBeginRead` seam. Lookup/copy/fill costs total 8.74 ms across the
entire 90-tick detailed window, while validation/hash falls by 439.83 ms and complete scheduling
falls by 53.5%. The remaining dominant high-change cost is recipient source-read/collapse plus
encoding, queue/proof bookkeeping, and transport admission—not compatible-body construction.

## Reuse under recipient divergence

| Scenario | Requests | Fills | Hits | Hit rate | Requested records | Built records |
| --- | --: | --: | --: | --: | --: | --: |
| synchronized | 46,080 | 720 | 45,360 | 98.44% | 46,080 | 720 |
| staggered joins | 38,768 | 720 | 38,048 | 98.14% | 38,768 | 720 |
| cadence phases | 11,904 | 736 | 11,168 | 93.82% | 25,600 | 1,520 |
| controlled loss | 46,115 | 1,420 | 44,695 | 96.92% | 48,786 | 2,129 |
| controlled reorder | 46,080 | 1,432 | 44,648 | 96.89% | 57,472 | 2,144 |
| proof delay | 46,080 | 720 | 45,360 | 98.44% | 181,248 | 2,832 |
| temporary stalls | 42,240 | 728 | 41,512 | 98.28% | 43,392 | 800 |
| AOI churn | 42,464 | 1,005 | 41,459 | 97.63% | 42,464 | 1,005 |
| reconnect | 46,080 | 720 | 45,360 | 98.44% | 46,080 | 720 |
| history pressure | 42,112 | 720 | 41,392 | 98.29% | 42,112 | 720 |
| combined divergence | 8,205 | 1,977 | 6,228 | 75.90% | 32,169 | 7,175 |

No identity collision occurred. Exact proof/cursor divergence fragments reuse but does not destroy
it, even in the deliberately combined adversarial case.

## Dormant singleton reevaluation

The lazy fallback repair proves selected 148 writes zero standalone 120s; an actual fallback writes
exactly one. The post-R6 high-change real-UDP rerun remains unfavorable on the authority thread:

| R | Canonical p95 ms | Singleton p95 ms | Regression | Selected 148 | Fallback writes |
| --: | --: | --: | --: | --: | --: |
| 128 | 1.208 | 1.329 | +10.1% | 37,004 | 0 |
| 192 | 2.395 | 2.716 | +13.4% | 54,656 | 133 |
| 256 | 3.669 | 4.897 | +33.5% | 173,035 | 114 |
| 300 | 4.493 | 5.575 | +24.1% | 287,862 | 0 |

The bandwidth reduction remains large, but body sharing does not make recipient-local singleton
LZ4 free. Singleton compression remains experimental and OFF in `CurrentBundled`.

## Verification

`--pf5-r6-only` passes all 175 replication tests and its real secure-UDP Off/Census/On parity plus
11-scenario divergence gate. The canonical Release verifier passes every kernel/Game/client suite,
actual Single Player/Online/Complete exports, Contentless Online cold/warm, Complete-hosted native,
mixed exported-client/bot, accounting, lifecycle, and final composition gate. An initial full run
observed one non-reproducing 6,192-byte warmed WorldPack allocation assertion; the World suite
passed immediately alone and the complete canonical rerun passed. No World production/test source
was changed. Packet-free local composition remains physically free of the remote body-store source
file; the store is linked only by `Game/Replication.Remote`.

PF6 remains free to proceed after canonical closure. A possible later performance investigation is
the still-recipient-local source-read/collapse and encoding/bookkeeping work. It is not part of
PF5-R6, and no early source-read bypass or canonical-byte-body B2 experiment was implemented.
