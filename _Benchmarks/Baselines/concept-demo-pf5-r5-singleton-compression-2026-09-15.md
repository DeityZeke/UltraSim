# PF5-R5 — singleton compression and prepared-hash contract

PF5-R5 started from clean `main` at
`f201e1abaf0fdb06ea0750b5f32c0db0f16d6231`. PF5-R3
`BackgroundPlatformBatch` egress and PF5-R4 recipient flattening were present. No intervening
commit changed their accepted behavior.

## Disposition

**Outcome B — the bandwidth win is real, but the authority-thread cost is too high for default
selection.** `RemoteChunkPublicationPolicy.CurrentBundled` therefore keeps singleton compression
disabled. The explicit `ExperimentalSingletonCompression` policy and its permanent gates remain
available for later work. Message 129/148 schemas, Game manifest v29, transport revision 2, proof
semantics, cadence 1/2/4, AOI, CrossLane cap two, MTU, and `BackgroundPlatformBatch` are unchanged.

- PF5-R2 semantic lineage: `992fc541460f18534da9036c73cb711277de11ce`.
- PF5-R4 selected production implementation: `15aa839f865db18c1c47b72fb49ff5a6d416beb4`.
- Phase A: `c3768146f0424b62226fd53f7e20f65db646f268`.
- Experimental Phase B: `b2f3d47845e4e6c196861f0a5cbf6e158a013777`.
- Permanent gates: `06fa99837cfd3abc9a6e9b6fcd552a1df61d0b60` and
  `f775165ea222e87ad0b6cff58985d4c7a4a16e65`.

Because the experiment was not selected, no new singleton-representation production candidate is
declared. The integrated tree does contain the Phase-A trusted-seam contract hardening; it does not
change wire bytes for normally computed hashes.

## Phase A — hash contract

The hash algorithm does not reserve zero. The friend-only prepared standalone and prepared bundle
writers nevertheless treated zero as an implicit "not prepared" sentinel. Preparation is now
carried structurally by those internal-only prepared entry points. Any `ulong`, including zero, is
accepted at that trusted seam; public writers and every decoder still validate ordinary untrusted
input and reject a wire hash which does not match its records. Absence continues to use its defined
zero hash semantics. No wire field, hash pass, message layout, manifest, or transport revision was
changed.

The full 172-test replication suite proves canonical equality, zero-hash prepared encoding followed
by decoder mismatch rejection, public malformed-input rejection, removal/mixed-mask round trips,
and zero warmed allocation.

## Existing receive capability and exact selection rule

The message-129 reader has always admitted `EntryCount` from 1 through 32. Message 148 recovers an
independently decodable canonical 129 and then uses the existing 129 reader. No receive capability
or protocol identity changed.

For one prepared Delta the experimental path:

1. writes canonical message 120 as the fallback;
2. constructs the exact one-entry message 129 from the same prepared records and hash;
3. attempts the existing LZ4-block message 148 only when canonical 129 is at least 384 bytes;
4. selects 148 only when its complete size fits the peer and saves at least 32 bytes versus the
   actual message-120 fallback; and
5. performs exactly one `TryQueue` for the chosen representation.

The maximum canonical decoded 129 is 1,405 bytes. The selected physical 148 must independently fit
the peer's unfragmented application-message limit. Raw one-entry 129 is never selected. A canonical
120 which fits remains independently admissible even when its 1,374-byte 129 form does not.

Focused evidence includes:

- exact 31/32/33-byte profitability switching;
- standalone 120 = 1,372 bytes, one-entry 129 = 1,374 bytes, and a fitting 148 = 361 bytes;
- queue refusal leaving delivery id 1, cursor, proof, and outstanding debt unchanged;
- deterministic poorly-compressible valid input: 383-byte 120, 385-byte 129, 374-byte 148,
  therefore fallback to 120;
- one removal plus seven mixed-mask records round-tripping bit-exact canonical 129 bytes;
- selected singleton impairment: 33 transmissions, 24 receives, 16 duplicates, eight exact
  applied proofs, convergence, and zero debt; and
- the captured old-session 148 is recognized but rejected by a fresh application-session epoch
  without changing the client cache; and
- zero managed allocation across 32 warmed authoritative mutation / encode / compress / decode /
  proof cycles.

Existing generation, AOI re-entry, source-epoch replacement, slot reuse, Rebase, snapshot,
duplicate, and stale-proof gates remain representation-independent because message 148 feeds the
same canonical 129 decoder and proof ledger.

## Measurement environment and method

- Linux x86-64, .NET SDK/runtime 8.0.129.
- AMD Ryzen 7 7800X3D, 8 cores / 16 logical processors.
- Release configuration, workload seed `84111361` (`0x05037001`); deterministic case-order seed
  `0x5A532004`.
- Synthetic: E=1000, dirty=20 and dirty=round(35% of R), 30 warmup + 270 measured ticks.
- Real UDP: E=1000, same low/high shapes, 60 warmup + 270 measured ticks, secure loopback UDP,
  real client receive/application/proof, `BackgroundPlatformBatch`, CrossLane cap two.
- Real-UDP server and clients share one process. `ServerOwnedRemoteReplicationBudget` excludes
  client work and the background sender; process CPU includes all of them. This is architecture
  characterization, not product CCU.
- Detailed attribution used separate 30-warmup / 90-measured-tick runs at R128 and R300; its clocks
  are not substituted for the rated rows.

The start-tree raw controls and final raw reports were hashed before summarization:

| Raw report | SHA-256 |
| --- | --- |
| start synthetic OFF | `cb312bb3fbc7017d895e2c8f031be78466d093eeab8c66823772ccfaba83ce5d` |
| start ordinary real UDP | `0850461fa0ae3a1cc260df6514f313610a32376de50ac0e4231d2902ccaeeeb1` |
| final synthetic OFF | `0aaa29cd3067da5886dbf75b44f7060404120dc9dddd9465aaefbd6f9b56a004` |
| final synthetic ON | `22a129d8749f8e58d77ff155bb1507a127f8cae71f858ec178a88cb8aa29a642` |
| final real-UDP OFF/ON | `a40eb715e49eb07c16b520e54b0c11485889c671ca4a73dfbb5b4cda57e89f7a` |

The companion JSON retains every headline row needed to recompute the comparisons.

## Synthetic matched controls

All rows have identical semantic publications and exact proof obligations between OFF and ON.
Times are milliseconds.

| R | Dirty | Policy | Server p95 | Schedule p95 | Singleton candidates | Singleton 148 | App bytes |
| --: | --: | --- | --: | --: | --: | --: | --: |
| 128 | 20 | OFF | 1.952 | 0.990 | 2,576 | 0 | 12,476,409 |
| 128 | 20 | ON | 1.922 | 0.977 | 2,576 | 2,548 | 11,782,065 |
| 128 | 45 | OFF | 2.757 | 1.790 | 36,416 | 0 | 42,230,537 |
| 128 | 45 | ON | 2.912 | 1.941 | 36,416 | 36,416 | 20,784,419 |
| 192 | 20 | OFF | 3.055 | 1.550 | 117 | 0 | 22,119,773 |
| 192 | 20 | ON | 3.129 | 1.615 | 117 | 0 | 22,119,773 |
| 192 | 67 | OFF | 5.994 | 3.686 | 57,563 | 0 | 86,472,051 |
| 192 | 67 | ON | 6.395 | 4.266 | 57,563 | 57,111 | 47,910,114 |
| 256 | 20 | OFF | 4.154 | 2.025 | 165 | 0 | 29,838,551 |
| 256 | 20 | ON | 4.198 | 2.073 | 165 | 0 | 29,838,551 |
| 256 | 90 | OFF | 10.312 | 6.063 | 172,060 | 0 | 182,762,775 |
| 256 | 90 | ON | 10.285 | 7.041 | 172,060 | 172,006 | 78,487,499 |
| 300 | 20 | OFF | 4.848 | 2.347 | 182 | 0 | 34,064,744 |
| 300 | 20 | ON | 4.922 | 2.379 | 182 | 0 | 34,064,744 |
| 300 | 105 | OFF | 13.386 | 7.393 | 293,364 | 0 | 276,328,908 |
| 300 | 105 | ON | 13.449 | 9.152 | 293,364 | 293,364 | 102,533,259 |
| 350 | 20 | OFF | 6.298 | 3.096 | 186 | 0 | 41,960,578 |
| 350 | 20 | ON | 6.523 | 3.201 | 186 | 0 | 41,960,578 |
| 350 | 122 | OFF | 17.630 | 10.796 | 239,730 | 0 | 293,100,155 |
| 350 | 122 | ON | 17.401 | 11.965 | 239,730 | 239,324 | 135,460,013 |

Compression moves work into scheduling even when the wider server-owned p95 is noisy or flat. At
R300/high, schedule p95 increases 23.8% while application bytes fall 62.9%.

## Real secure-UDP controls

Low change is broadly neutral, although the R128 row has an 8.7% p95 increase. Rows with no
eligible singleton correctly do no codec work.

| R | Dirty | Policy | Remote p95 | p99 | CPU cores | App bytes | UDP bytes | Datagrams | Singleton 148 |
| --: | --: | --- | --: | --: | --: | --: | --: | --: | --: |
| 64 | 20 | OFF | 0.770 | 0.799 | 0.991 | 6,090,737 | 10,061,889 | 34,592 | 0 |
| 64 | 20 | ON | 0.766 | 0.795 | 1.010 | 5,825,586 | 9,796,738 | 34,592 | 1,088 |
| 128 | 20 | OFF | 1.533 | 1.549 | 1.029 | 12,258,099 | 20,227,135 | 69,176 | 0 |
| 128 | 20 | ON | 1.667 | 1.694 | 1.000 | 11,893,286 | 19,862,322 | 69,176 | 1,904 |
| 192 | 20 | OFF | 2.547 | 2.650 | 0.989 | 22,099,484 | 37,430,066 | 104,160 | 0 |
| 192 | 20 | ON | 2.476 | 2.553 | 1.012 | 22,096,191 | 37,426,773 | 104,160 | 24 |
| 256 | 20 | OFF | 3.372 | 3.605 | 1.009 | 29,555,398 | 50,071,838 | 139,132 | 0 |
| 256 | 20 | ON | 3.405 | 3.458 | 0.997 | 29,782,956 | 50,299,396 | 139,132 | 0 |
| 300 | 20 | OFF | 4.025 | 4.977 | 1.009 | 33,978,586 | 57,824,451 | 163,050 | 0 |
| 300 | 20 | ON | 3.925 | 4.009 | 1.008 | 33,988,859 | 57,834,724 | 163,050 | 0 |
| 350 | 20 | OFF | 5.190 | 5.448 | 1.009 | 41,622,838 | 72,216,447 | 190,560 | 0 |
| 350 | 20 | ON | 5.227 | 6.417 | 1.011 | 41,600,574 | 72,194,183 | 190,560 | 0 |

High change demonstrates the trade clearly:

| R | Policy | Remote p95 | p99 | CPU cores | App bytes | UDP bytes | Datagrams | Queue p99 ms | Singleton 148 |
| --: | --- | --: | --: | --: | --: | --: | --: | --: | --: |
| 64 | OFF | 0.789 | 0.865 | 1.034 | 6,448,863 | 10,420,015 | 34,592 | 0.262 | 0 |
| 64 | ON | 0.816 | 0.855 | 1.163 | 6,191,542 | 10,162,694 | 34,592 | 0.199 | 1,088 |
| 128 | OFF | 2.405 | 2.435 | 1.022 | 42,328,295 | 51,334,291 | 80,041 | 0.524 | 0 |
| 128 | ON | 2.565 | 2.641 | 1.012 | 20,708,804 | 29,236,740 | 69,176 | 0.524 | 37,004 |
| 192 | OFF | 5.048 | 5.195 | 1.014 | 84,439,979 | 103,358,676 | 144,640 | 0.873 | 0 |
| 192 | ON | 5.474 | 6.037 | 1.015 | 47,698,665 | 66,067,758 | 132,149 | 0.907 | 54,656 |
| 256 | OFF | 7.961 | 8.180 | 1.039 | 182,536,857 | 213,015,795 | 279,569 | 1.444 | 0 |
| 256 | ON | 8.801 | 8.935 | 1.007 | 78,151,772 | 105,202,054 | 201,645 | 0.956 | 173,035 |
| 300 | OFF | 9.844 | 9.960 | 1.044 | 273,519,967 | 312,107,564 | 376,641 | 2.097 | 0 |
| 300 | ON | 11.310 | 11.896 | 1.016 | 102,345,640 | 135,592,077 | 255,251 | 1.049 | 287,862 |
| 350 | OFF | 13.881 | 14.062 | 1.028 | 292,909,156 | 341,268,146 | 411,592 | 2.097 | 0 |
| 350 | ON | 14.890 | 15.320 | 1.008 | 133,354,605 | 176,688,575 | 297,387 | 1.049 | 239,538 |

At R128/high, app bytes fall 51.1%, UDP payload 43.0%, and datagrams 13.6%, while remote p95
increases 6.7%. At R300/high, those reductions are 62.6%, 56.6%, and 32.2%, but p95 increases
14.9%. Total same-process CPU falls 2.7% at R300 because background send work falls; that does not
erase the authority-thread regression.

Detailed R128/high measures 1.219 microseconds of LZ4 work per attempt; R300/high measures 1.309
microseconds. Exact proofs and semantic work are conserved in every OFF/ON pair. R128/high proof
p95 is 11.054 ms OFF and 9.115 ms ON; R300/high is 39.133 ms OFF and 39.796 ms ON. Outstanding
high-water is respectively 3 and 5 in both policies. No capacity drops, physical send failures,
`WouldBlock`, or sender-worker failures occurred.

The sampled 4 ms crossing is unchanged in the ordinary low-change real-UDP curve (R300 is the
first tested row over 4 ms OFF; ON is 3.925 ms there but R350 is over). The high-change curve first
exceeds 4 ms at R192 under both policies. These are synthetic recipient curves, not CCU claims.

## Why it is not selected

The experimental policy saves 0.30 MB at R64/high and 171.25 MB at R300/high over these windows.
However, compression is paid once per recipient-specific singleton. At R300/high that is 287,862
LZ4 operations, increasing schedule p95 from 7.17 to 9.00 ms in the rated real-UDP run. The current
4 ms server-owned engineering budget values main-thread headroom; shifting work from the background
sender back onto that thread is not a neutral trade.

The remaining detailed R300/high cost is dominated by recipient-local prepared validation/hash
(about 451 ms total over 90 ticks in both policies), source read/materialization, outstanding
bookkeeping, and proof retirement. Compression adds about 126 ms plus extra one-entry bundle
encoding. The next useful performance investigation is compatible immutable recipient-body
materialization/encoding sharing, while retaining independent DeliveryIds and proof state. It is
now warranted by attribution but was deliberately not implemented here. A later compression retry
would need to move bounded representation work off the authority-critical path or otherwise prove
a materially cheaper codec; threshold tuning alone does not remove the per-selected cost.

## Verification

`--pf5-r5-only` runs the complete replication suite plus a real secure-UDP OFF/ON parity gate with
`BackgroundPlatformBatch`. Existing PF5-R2 through PF5-R4, impairment/lifecycle, Release, export,
composition, and contentless gates all pass. The canonical Release verifier also passes the
packet-free Single Player, exported native client, F0.7 mixed-session lifecycle, and F0.8 final
composition/parity gates. PF6 remains free to resume under the unchanged selected production
representation.
