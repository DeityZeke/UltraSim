# P0.3G Dirty Chunk shared-page and recipient-bundle scale matrix

- Commit: `1bc631c7136c2962fcd9b8d6b0e91f82747a4175`
- Runtime: .NET 8.0.29, Release, tiered compilation and ReadyToRun disabled
- Host: Unix 7.1.5.1, 16 logical processors
- Scope: `DirtyChunkSpatialFanout` Game replication core, canonical Game-codec
  bytes, payload-derived synthesized client, and immediate per-chunk application proof
- Excluded: socket I/O, transport framing, security, packetization, and client
  engine/world storage
- Sparse trace: one entity and recipient per CCU, 35% matching entity/recipient
  motion/tick, 320 ticks, 10 warmup ticks
- Dense trace: 1,000 public entities, 2% spatial dirt/tick, 120 ticks, 30 warmup ticks
- Stop rule: finish the first run with p95 greater than 16 ms, or finish 4,096 CCU
  and stop even if still below 16 ms
- Validation: 42 clean artifacts, exact Git SHA, semantic convergence and chunk-family
  equivalence, zero warmed managed allocation, and exact bundled-entry/proof accounting

## Verified diagnosis and implementation

The P0.3F Dense-400 counters confirmed the proposed diagnosis. Dirty Chunk delivered
only 2.6% more records than Dirty Entity, but emitted 6.6 times as many Game messages.
Its source mutations were chunk-coalesced, while materialization and delivery remained
one recipient by one chunk at a time.

P0.3G changes only the experimental Dirty Chunk profile:

- a bounded shared page cache materializes a `(source commit, chunk key, applied base
  cursor)` snapshot/delta/absence result once and reuses it for recipients on the same
  lineage;
- a bounded recipient envelope packs as many as 32 unrelated chunk entries under one
  Game-message header and byte budget;
- every entry retains its own delivery ID, cursor lineage, and application proof;
- source commit replacement invalidates shared pages; leave/re-entry, timeout, stale
  proof, history fallback, and source-epoch fencing retain their prior semantics;
- the profile still performs dirty-time recipient discovery and still owns no retained
  global `Chunk -> Recipient` subscriber graph.

The canonical protocol now includes message 129, `ultrasim.game.dirty-chunk-bundle`,
with transactional hostile-input decoding. The current hosted Dirty Chunk adapter remains
on the accepted single-chunk path; this matrix uses the concrete profile bundle API and
canonical codec size, not real Network transport. An eventual hosted adoption must use the
actual nonfragmenting `WorldState` payload ceiling (or separately earn another lane/segmentation
contract) rather than assuming the 16 KiB semantic benchmark budget is one UDP datagram.

## Dense-400 causal comparison

| Measure | P0.3F single-chunk delivery | P0.3G shared pages + bundles | Change |
| --- | ---: | ---: | ---: |
| p95 | 4.906 ms | 2.737 ms | -44.2% |
| Materialized records | 1,360,000 | 1,360,000 | unchanged |
| Chunk application proofs | 397,554 | 397,554 | unchanged |
| Game messages | 397,554 | 54,983 | -86.2% |
| Canonical Game payload | 83.73 MB | 75.96 MB | -9.3% |
| Profile mechanism reserve | 76.16 MB | 78.28 MB | +2.11 MB |
| Shared page materializations | n/a | 2,056 | — |
| Shared page hits | n/a | 395,498 | — |

The new path averages 7.23 independently proved chunk entries per recipient message,
and 99.48% of entry builds reuse an already materialized equal-lineage page. The unchanged
record and proof counts prove that the improvement came from semantic header/message and
source-read amortization, not from weakened disclosure, omitted payload, or bundle-level
false knowledge.

Against the unchanged P0.3F Dense-400 controls, Dirty Chunk is now close to Maintained
Entity (2.737 versus 2.634 ms), but it remains slower than entity/chunk batch (2.082 ms),
Dirty Entity (1.850 ms), and the selected Chunk Publication profile (1.680 ms). This
therefore validates the delivery-multiplicity diagnosis without making Dirty Chunk the
selected profile.

## Sparse scale matrix

Times are milliseconds. Every row has 310 measured ticks and zero measured managed
allocation.

| CCU | p50 | p95 | p99 | max | Messages | Chunk proofs | Canonical bytes | Known reserve MiB |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 512 | 3.406 | 3.465 | 3.511 | 3.608 | 117,739 | 553,062 | 82,212,082 | 33.04 |
| 640 | 5.144 | 5.224 | 5.278 | 5.353 | 142,171 | 814,664 | 113,083,198 | 45.21 |
| 768 | 7.078 | 7.567 | 8.734 | 9.168 | 165,146 | 1,082,670 | 144,677,652 | 58.40 |
| 896 | 8.719 | 8.903 | 9.449 | 9.603 | 179,534 | 1,348,824 | 176,234,406 | 73.18 |
| 1,024 | 9.024 | 9.103 | 9.192 | 9.239 | 181,608 | 1,388,779 | 193,528,329 | 89.55 |
| 1,152 | 9.756 | 10.280 | 11.061 | 11.498 | 188,304 | 1,504,163 | 215,785,816 | 108.60 |
| 1,280 | 11.456 | 11.719 | 11.776 | 11.912 | 204,582 | 1,776,856 | 247,856,735 | 127.95 |
| 1,408 | 13.950 | 14.174 | 14.370 | 14.451 | 230,747 | 2,050,311 | 280,096,603 | 149.06 |
| 1,536 | 16.165 | **16.656** | 16.914 | 17.388 | 250,551 | 2,308,300 | 310,916,279 | 171.54 |

P0.3F stopped at 896 CCU / 16.894 ms. P0.3G reaches 1,536 CCU / 16.656 ms,
an increase of 640 CCU (71.4%) under the same mechanical stop rule. At the old
896-CCU stop point, p95 falls 47.3% to 8.903 ms.

## Dense scale matrix

Times are milliseconds. Every row has 90 measured ticks and zero measured managed
allocation. The separate 400-recipient causal row is included between sweep points.

| CCU | p50 | p95 | p99 | max | Messages | Chunk proofs | Known reserve MiB |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 128 | 0.780 | 0.881 | 0.951 | 0.951 | 17,595 | 127,184 | 35.58 |
| 192 | 1.175 | 1.351 | 1.455 | 1.455 | 26,396 | 190,780 | 52.11 |
| 256 | 1.552 | 1.775 | 1.958 | 1.958 | 35,197 | 254,396 | 67.61 |
| 320 | 1.991 | 2.258 | 2.404 | 2.404 | 43,991 | 318,071 | 85.17 |
| 384 | 2.365 | 2.687 | 2.832 | 2.832 | 52,784 | 381,647 | 100.70 |
| 400 | 2.442 | 2.737 | 2.984 | 2.984 | 54,983 | 397,554 | 104.57 |
| 448 | 2.953 | 3.275 | 3.553 | 3.553 | 61,582 | 445,272 | 116.18 |
| 512 | 3.224 | 3.645 | 3.878 | 3.878 | 70,379 | 508,940 | 131.72 |
| 576 | 3.564 | 4.053 | 4.414 | 4.414 | 79,172 | 572,466 | 151.28 |
| 640 | 3.975 | 4.440 | 4.829 | 4.829 | 87,973 | 636,159 | 166.75 |
| 704 | 4.331 | 4.886 | 5.255 | 5.255 | 96,772 | 699,741 | 182.32 |
| 768 | 4.795 | 5.466 | 6.005 | 6.005 | 105,564 | 763,400 | 197.80 |
| 832 | 5.181 | 5.941 | 6.671 | 6.671 | 114,363 | 827,005 | 213.39 |
| 896 | 5.405 | 6.193 | 6.854 | 6.854 | 123,158 | 890,664 | 228.86 |
| 960 | 6.002 | 6.842 | 7.449 | 7.449 | 131,957 | 954,257 | 244.47 |
| 1,024 | 6.351 | 7.162 | 7.851 | 7.851 | 140,753 | 1,017,806 | 259.95 |
| 1,088 | 6.662 | 7.561 | 8.316 | 8.316 | 149,549 | 1,081,519 | 283.42 |
| 1,152 | 7.179 | 8.382 | 9.215 | 9.215 | 158,350 | 1,145,095 | 299.07 |
| 1,216 | 7.773 | 11.156 | 14.084 | 14.084 | 167,140 | 1,208,706 | 314.54 |
| 1,280 | 7.882 | 9.011 | 9.849 | 9.849 | 175,933 | 1,272,356 | 330.01 |
| 1,344 | 8.441 | 9.644 | 10.393 | 10.393 | 184,744 | 1,335,966 | 345.49 |
| 1,408 | 8.810 | 9.841 | 10.724 | 10.724 | 193,532 | 1,399,520 | 361.16 |
| 1,472 | 9.254 | 10.480 | 11.341 | 11.341 | 202,325 | 1,463,163 | 376.64 |
| 1,536 | 9.746 | 10.891 | 11.695 | 11.695 | 211,122 | 1,526,805 | 392.11 |
| 1,600 | 9.858 | 11.116 | 11.871 | 11.871 | 219,925 | 1,590,431 | 407.59 |
| 1,664 | 10.487 | 11.840 | 12.728 | 12.728 | 228,721 | 1,654,030 | 423.30 |
| 1,728 | 10.949 | 12.326 | 13.290 | 13.290 | 237,521 | 1,717,662 | 438.78 |
| 1,792 | 12.686 | 14.176 | 15.330 | 15.330 | 246,319 | 1,781,254 | 454.25 |
| 1,856 | 13.119 | 14.871 | 15.986 | 15.986 | 255,113 | 1,844,880 | 469.73 |
| 1,920 | 12.426 | 14.097 | 15.084 | 15.084 | 263,910 | 1,908,526 | 485.20 |
| 1,984 | 13.592 | 15.196 | 16.360 | 16.360 | 272,704 | 1,972,176 | 500.96 |
| 2,048 | 13.881 | 15.535 | 16.998 | 16.998 | 281,505 | 2,035,738 | 516.44 |
| 2,112 | 14.616 | **16.415** | 17.929 | 17.929 | 290,300 | 2,099,398 | 547.91 |

P0.3F stopped at 1,344 CCU / 16.319 ms. P0.3G reaches 2,112 CCU /
16.415 ms, an increase of 768 CCU (57.1%). At the old 1,344-CCU stop point,
p95 falls 40.9% to 9.644 ms. The isolated 1,216-CCU tail did not trigger the
mechanical stop and later points returned to trend; no retry was substituted.

## Verdict

The proposed finding was correct: Dirty Chunk had implemented source batching but not
delivery amortization. Shared equal-lineage pages and recipient multi-chunk messages remove
most message construction without changing recipient knowledge or proof meaning. The remaining
gap is not proof that chunk batching is intrinsically slower, but neither is P0.3G evidence that
Dirty Chunk should replace `ChunkPublication`: recipient discovery remains on demand, application
proof remains per chunk, the profile reserves about 2.1 MB more at Dense-400, and the hosted path
has not adopted this envelope or proved MTU-safe transport behavior.

P0.3G is an experimental profile-floor checkpoint. It does not alter the selected current-game
profile, authorize N1.5 for `ChunkPublication`, or make production/network CCU claims.
