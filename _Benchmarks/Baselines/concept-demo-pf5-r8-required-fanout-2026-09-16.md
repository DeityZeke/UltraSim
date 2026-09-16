# PF5-R8 — required dense fanout versus avoidable processing

PF5-R8 closes as a measurement checkpoint. It changes no replication semantics, wire format, AOI,
cadence, compression, aggregation, egress, or participant policy, and accepts no production
optimization. The principal result is:

> Current dense cost is predominantly the cost of more required recipient obligations and delivered
> records. Shared source and compatible-body preparation are already effective. The remaining
> confirmed repetition is small; the larger residual is not attributed well enough to optimize.

## Lineage and environment

The clean starting HEAD was `13c1afedae7e18ad736a93e28573bcd96cdb5a9e`; the last independent
reference was `8e60c4a1b8496a79f98ba58411875bc43b7b41be`. The only intervening work was
the already accepted telemetry-regression hardening/evidence wording; no production replication
policy changed. Measurement commits are:

- `a014c54bf304f3595b29eec5e7706e7bd1464112` — bounded operation census, raw paired
  samples, proof-delay control, tests, and focused verifier;
- `228bd1287bcc0e8c0cba27436ca2aeb2bf594d36` — matched proof-debt and bracketed
  Off/Basic/Detailed controls;
- `d1b0ac270dcf845aecf9a30399679100d7337c6d` — final-debt capture for exact conservation.

The production semantic candidate remains `14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9`.
The accepted evidence run used Release, .NET 8.0.29, CachyOS, and an AMD Ryzen 7 7800X3D
(16 logical processors). It composed one authority and real secure loopback UDP client sessions in
one process, with one bounded `BackgroundPlatformBatch` sender owner. Process CPU/RSS therefore
include clients and are not server-only metrics.

The selected policy remained ChunkPublication, shared prepared latest state, compatible canonical
body sharing, CurrentBundled, singleton compression disabled, selected message-148 compression,
1/2/4 cadence, CrossLane cap two, BackgroundPlatformBatch, manifest v29, transport revision 2,
60 Hz authority, and a product participant cap of 64. Synthetic populations above 64 are not CCU.

## Counted units and timing boundary

The census keeps these units distinct:

```text
source mutation
  -> dirty source chunk
  -> immutable prepared source interval/body identity
  -> interested recipient wakeup/due obligation
  -> scheduling attempt
  -> admitted delivery identity
  -> logical publication / bundle entry
  -> Game message
  -> Network frame
  -> UDP datagram
  -> applied proof / knowledge advancement / debt retirement
```

A retry is not new truth. A queue refusal is not admitted debt. A message is not a publication, and
a datagram is not a proof. Positive latest state may supersede older positive debt; absence remains
exact. Every controlled row validates final recipient truth through the existing independent client
cache/oracle and exact semantic digest; the focused regressions additionally compare records and
canonical bytes rather than relying only on hashes.

The rated server-owned remote boundary is the paired, non-overlapping same-tick sum of
`RemotePublicationPreparation + RecipientCommitObservation + NetworkAndRemoteReplication`.
Whole-fixture time retains in-process client work and is not described as server occupancy.
Background sender residence/service is separate. Detailed child clocks are explicitly diagnostic:
their parent reconciles as exclusive children plus a published residual, but their observer tax
makes them unsuitable for a capacity rating.

## Fixed source work, increasing fanout

Every row below performs exactly 480 prepared source publications containing 600 source records.
The 30-tick Detailed pass supplies counts; the separate 60-tick Off pass supplies rated timing.

| Recipients | Deliveries | Body hit | Detailed schedule μs/delivery | Rated remote mean | Rated remote p95 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 102 | 0.00% | 11.013 | 0.057 ms | 0.074 ms |
| 8 | 874 | 82.61% | 5.775 | 0.237 ms | 0.283 ms |
| 32 | 4,239 | 93.09% | 1.586 | 0.720 ms | 0.949 ms |
| 64 | 9,778 | 94.91% | 1.674 | 0.544 ms | 0.593 ms |
| 128 | 19,803 | 96.27% | 1.465 | 1.051 ms | 1.118 ms |
| 256 | 40,895 | 97.97% | 1.640 | 2.293 ms | 2.530 ms |

Small-population ratios are dominated by fixed per-session/pump work, batching, runtime tiering,
and the higher miss fraction. From 64 through 256, both Detailed scheduling and rated complete
remote mean are approximately flat per admitted obligation (about 1.5–1.7 μs). There is no
observed superlinear recipient algorithm in this range. Total time grows because required
recipient obligations grow.

## Fixed-total topology and activity controls

The topology shapes hold source record count at 5,370 but deliberately change source-body
cardinality, AOI fanout, message shape, and the recipient records that current policy actually
requires. They are not mislabeled as a pure topology experiment:

| Shape | Source preparations | Deliveries | Delivered record instances | Body hits | Remote p95 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1×512 | 270 | 44,644 | 886,029 | 44,179 | 5.670 ms |
| 8×64 | 330 | 16,308 | 274,536 | 15,819 | 2.041 ms |
| 64×8 | 690 | 5,520 | 42,960 | 4,830 | 1.282 ms |
| 512×1 | 5,370 | 5,370 | 5,370 | 0 | 1.373 ms |

The dense row prepares the fewest distinct source intervals but delivers 165× the sparse row's
record instances. Its cost is not evidence that compatible preparation failed; it is principally
more semantic fanout.

At fixed R128 topology, spreading 20 mutations over chunks produces 19,803 publications, 24,622
delivered records, and 1.092 ms p95. Placing those same 20 changed records in one chunk produces
1,420 publications, 28,400 delivered records, and 0.437 ms p95. At fixed publication count,
increasing spread-body records from 56,146 to 159,646 raises p95 from 1.233 to 1.556 ms. Publication
count, records/publication, and message shape are separate cost dimensions.

## Compatible versus divergent knowledge

Compatible identity is exact source epoch, chunk key, from/to revision, history floor, count, and
canonical content. Reuse remains high when recipient synchronization is attacked:

| Scenario | Body requests | Distinct identities | Compatible reuse |
| --- | ---: | ---: | ---: |
| synchronized | 46,080 | 720 | 98.44% |
| staggered joins | 38,768 | 720 | 98.14% |
| cadence phases | 11,904 | 736 | 93.82% |
| controlled loss | 46,115 | 1,420 | 96.92% |
| controlled reorder | 46,080 | 1,432 | 96.89% |
| AOI churn | 42,464 | 1,005 | 97.63% |
| combined divergence | 8,205 | 1,977 | 75.90% |

History pressure evicted 1,184 retained intervals and caused 128 safe Rebases. A resident body did
not bypass missing contiguous coverage or `HistoryFloor`. Selected steady-state history-derived
reconstruction remained zero. This supports the current shared preparation model, not another
global semantic cache.

## Work conservation

The R128/high Detailed control gives one exact end-to-end ledger:

```text
1,350 source mutations
  -> 540 dirty chunks / prepared intervals
  -> 42,330 dirty recipient fanout references
  -> 22,019 due work items and schedule attempts
  -> 22,019 admitted delivery identities
  -> 21,896 bundle entries + 123 standalone publications
  -> 4,128 Game messages
  -> 8,256 bidirectional Network frames / UDP datagrams
  -> 22,019 positive proofs
  -> 22,019 knowledge advancements
  -> 22,019 exact debt retirements
  -> 0 ending debt
```

There were no transport/capacity refusals, invalidations, rejected/contradictory proofs, stale
advancements, source gaps, or capacity drops. Refusal/retry, supersession, removal, source
replacement, reconnect, and final drain remain covered by focused permanent regressions.

## Exclusive attribution and observer tax

For the same R128/high shape, the Detailed parent averaged 2.426 ms/tick and reconciled as follows:

| Exclusive or parent stage | ms/tick | Interpretation |
| --- | ---: | --- |
| source-read parent | 0.278 | includes the next five source children |
| identity + contiguous coverage | 0.063 | 22,019 identities; 40,870 coverage links |
| body lookup + cached-body copy | 0.047 | 21,203 hits / 816 misses |
| full miss preparation + validation/hash | 0.031 | paid only on the 816 unique misses |
| remaining source-read work | 0.156 | recipient eligibility/read plumbing; not further assigned |
| bundle representation/write/clear | 0.096 | excludes compression |
| compression | 0.209 | selected policy, 3,966 attempts/selections |
| queue admission + outstanding bookkeeping | 0.123 | recipient-local transaction/debt |
| Detailed census observer work | 0.182 | not production work |
| scheduling residual | 0.241 | unattributed; not called security or proof |
| scheduler selection | 0.089 | outside schedule parent |
| proof parent | 0.433 | lookup, validation, retirement, cursor work |
| Network protocol | 0.486 | frame/crypto/packet processing measured by Network |
| broader remote residual | 0.269 | unattributed parent remainder |

The Detailed mode is deliberately expensive: across three bracketed repetitions, high-change
remote mean is 1.165 ms Off, 1.177 ms Basic, and 2.357 ms Detailed. Low-change is 1.027/1.078/2.144
ms. Detailed therefore doubles the parent; absolute child values include clock/census tax and are
not production ratings. Basic had visible low-change overhead and is also not rated. Off retains
the existing warmed zero-allocation semantic-path regressions; the full in-process fixture itself
allocated about 20.7 KB per 60-tick Off window and about 65.4 KB per Detailed window, which is
reported rather than hidden.

Subscription lookup ran twice per delivery (44,038 calls, 178,814 comparisons, 0.070 ms/tick in
Detailed): once during scheduling and once when an applied proof must revalidate current
subscription ownership. The proof subset is reported separately. Body-store→encoder copied
2.325 MB of canonical bytes, and bundle staging copied the canonical section again. Detailed also
copies typed records for its census. These copies are not automatically removable: recipient
headers, delivery state, final contiguous encoding, and packet-boundary carries remain independent.

Server reservations for this R128 diagnostic row were 126.5 MB for Game replication plus 54.1 MB
for server Network. The in-process benchmark additionally reserved 139.4 MB of client Game state
and 41.9 MB of client Network state. The shared body store was 2.887 MB. These are not combined and
reported as server memory.

## Proof debt

The normal R128/high row reached outstanding high-water eight, 1.95 delivery lookup comparisons
and 2.35 obsolete checks per proof, with no same-chunk supersession matches. Controlled proof delay
confirmed the present algorithms scale with debt: at depth 64, both multi-chunk and same-chunk
drains require about 16.3 delivery lookup comparisons and 31 obsolete checks per proof. Yet measured
proof service remained 0.89–0.99 μs/proof in that synthetic control. This is a real linear scan, but
realistic current depth does not justify a second delivery-id/chunk index and its consistency risk.

## Rated secure-UDP boundary

Three alternating/bracketed R300 repetitions retained every tail:

| Workload | schedule p95 range | server-owned remote p95 range |
| --- | ---: | ---: |
| R300 low (20 changes/tick) | 1.047–1.119 ms | 2.503–2.641 ms |
| R300 high (105 changes/tick) | 1.277–1.354 ms | 3.662–3.862 ms |

The high-change first-tested boundary is R256 3.150 ms (pass), R300 all repetitions below 4 ms,
R350 5.726 ms (fail), and R400 6.814 ms (fail). The previous accepted R300/high anchors were
1.180/3.519 ms. The current run is slower but reproduces the architectural conclusion without
discarding tails. New Off branches allocate nothing; the historical single run cannot isolate a
4–6% environment drift from branch cost, so no correction is subtracted from current evidence.

Background egress remained bounded and drained cleanly. R300/high background queue high-water was
below 1,000 datagrams in all repetitions, with no refusal, stale path drop, physical send failure,
or retained bytes. Queue residence and sender service remain separate from authority occupancy.

## Classification and disposition

Measured required responsibility under current policy:

- recipient-specific application epoch/header, delivery id, queue transaction, exact debt/proof,
  cursor, absence, and lifetime fencing;
- per-recipient secure Network frame/crypto/packet output;
- at least one final materialization of each independently selected recipient output.

Confirmed avoidable repetition, currently small:

- [`ClearBundleStaging`](../../Game/Replication/RemoteChunkWorldStateServer.cs) clears the typed
  diagnostic record slab in Off even though [`StagePreparedCandidate`](../../Game/Replication/RemoteChunkWorldStateServer.cs)
  populates it only in Detailed. The R128/high Detailed census clears 2.695 MB of such typed slots
  over 30 ticks; the complete clear stage is only 0.010 ms/tick, so this does not justify mixing a
  production repair into PF5-R8.

Likely experiments, not accepted optimizations:

1. A retention-generation-fenced O(1) coverage certificate could replace repeated compatible-chain
   walks. It must fail closed on eviction, source/AOI/session replacement, and `HistoryFloor`.
   The entire measured identity+coverage stage is only a 0.063 ms/tick observer-inflated upper bound.
2. A borrowed immutable canonical-body view through bundle staging could remove the
   body-store→encoder intermediate copy. It must preserve packet-boundary carry lifetime, exact
   120/129 bytes, compression selection, queue refusal, and independent proof. Measured body-copy
   time is only 0.017 ms/tick; bundle copy still has to produce final output.
3. A bounded delivery-id plus chunk-local proof index should be reconsidered only if product
   impairment drives realistic debt above roughly 16. At current depth it is not justified.

The 0.241 ms scheduling residual and 0.269 ms broader residual are **unattributed**, not a promised
saving. A future investigation must split them before proposing an optimization.

Another 50–100 synthetic recipients is not a defensible optimization target. The tested boundary
already crosses between R300 and R350, while the non-overlapping measured local opportunities are
far smaller than the roughly 2 ms p95 increase to R350. More recipients remain an upper-bound
possibility only after an independently measured larger opportunity—not a checkpoint promise.

PF5-R8 does not block PF6. It found no correctness defect, production policy change, or failed
product gate.

## Verification

The focused PF5-R8 verifier passed all 176 Game replication tests and its real-secure-UDP matrix.
The complete canonical Release verifier also passed, including physical product publishes/exports,
Contentless Online, the mixed exported-client swarm, lifecycle/parity checks, and `git diff --check`.
The closing tree is clean.

## Evidence and commands

Machine-readable summary:
`_Benchmarks/Baselines/concept-demo-pf5-r8-required-fanout-2026-09-16.json`

Raw fixture configuration, all rows, paired per-tick samples, seeds, distributions, counters, and
reservations:
`_Benchmarks/Baselines/concept-demo-pf5-r8-required-fanout-2026-09-16.raw.json`

```bash
dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj \
  -c Release -- --pf5-r8-required-fanout \
  --output _Benchmarks/Baselines/concept-demo-pf5-r8-required-fanout-2026-09-16.raw.json

dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf5-r8-only

./verify.sh --configuration=Release
```
