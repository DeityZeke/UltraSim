# PF5-R7 adversarial recipient-reuse follow-up

This follow-up independently reproduced all four ASTRA observations against clean starting commit
`4be883250a733110a6a947128f09815e92f00b65`. Three warranted production changes; the proof-ledger
finding warranted a permanent census but not another index under the measured current debt bound.

## Accepted repairs

An exact body-cache hit no longer rebuilds the source record range. The encoder first establishes a
shallow immutable range identity, performs the cache lookup, validates that the current prepared
publication chain still provides complete contiguous coverage, and only then enters the existing
recipient-local pending transaction. A miss uses the existing full reconstruction path. Cache
residency cannot authorize an expired `HistoryFloor`, a different source lifetime, or another
recipient's proof.

The body store now retains canonical record bytes instead of typed records. Message 120 and 129
still write recipient-specific headers independently, then copy the immutable record section.
Public writers and all untrusted decoders retain complete validation. The Game manifest and wire
format do not change.

Turning Detailed telemetry Off now stops outstanding-delivery diagnostic slab copies and clears,
including when live debt existed during the transition. The retained arrays remain reusable and
Detailed can be enabled again safely.

## Proof-retirement disposition

The unrelated-debt scan is real. A deterministic eight-chunk drain performs 28 unrelated checks
and no same-chunk checks. The R300 Detailed run, however, measured 249,920 checks for 151,175 proof
retirements (1.653 checks/proof), zero same-chunk matches, and an outstanding high-water of five.
Adding a delivery-id index plus chunk-local links would introduce another bounded structure and
consistency surface for shallow current debt. The operation census remains permanent so this can
be revisited if delayed-proof or product evidence grows the ledger materially.

## Measured result

| Recipients | Workload | Model | schedule p95 | server-owned remote p95 |
| ---: | --- | --- | ---: | ---: |
| 64 | low | before | 0.230 ms | 0.514 ms |
| 64 | low | after | 0.220 ms | 0.514 ms |
| 64 | high | before | 0.226 ms | 0.495 ms |
| 64 | high | after | 0.188 ms | 0.460 ms |
| 300 | low | before | 1.087 ms | 2.484 ms |
| 300 | low | after | 1.017 ms | 2.479 ms |
| 300 | high | before | 1.960 ms | 4.168 ms |
| 300 | high | after | 1.180 ms | 3.519 ms |

The high-change R300 result reduces schedule/codec p95 by 39.8% and the complete server-owned
remote p95 by 15.6%. Low-change tail movement is small; its schedule mean falls 8.1%, while the
complete remote p95 is effectively flat. Disabled controls moved upward on the same host, so the
enabled high-change improvement is not explained by a generally faster run.

Detailed R300 evidence records 151,175 candidate requests but only 1,196 cache misses,
materializations, and validation/hash calls. The 149,979 hits still validate current retained
coverage, but do not collapse, sort, or materialize source records. The high-change run copies
116,519,594 canonical record bytes from immutable bodies.

The byte-only store reserves 2,887,168 bytes versus the prior typed store's 3,214,848 bytes. An
intermediate typed-plus-byte prototype reserved 6,032,896 bytes and regressed low-change tails; it
was rejected.

Semantic digest, publications, proofs, Game-message count, bundle entries, and logical application
bytes reconcile exactly. One R300/high physical window ended with one fewer 17-byte proof frame in
flight, accounting for one datagram and 84 physical bytes; it is asynchronous window-boundary
noise, not a representation change.

Permanent regression coverage includes byte-for-byte 120/129 parity, mixed masks/removals,
near-MTU bodies, history-floor eviction despite a resident body, independent recipient proofs,
queue refusal, source replacement, warmed allocation, Detailed-to-Off with live debt, and proof
scan operation counts. The focused Game replication suite passes all 176 tests.

The production implementation is
`14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9`. PF5-R2 semantic lineage remains
`992fc541460f18534da9036c73cb711277de11ce`; selected cadence, AOI, compression, aggregation,
transport, and participant policy remain unchanged.
