# PF5-R8-R1 — measurement-window and matched-control closure

PF5-R8-R1 closes the three gaps identified in the independent review without changing production
replication behavior. It supports this narrower conclusion:

> The earlier recipient reconstruction waste is largely gone. Current dense cost is strongly
> driven by required recipient deliveries, but the remaining processing has not been proven
> minimal or unavoidable.

The original PF5-R8 parent per-tick timings remain historical evidence. Its child-stage totals,
normalized child costs, and exclusive residual included post-window convergence work and are
superseded by this closure evidence.

## Lineage and scope

- task starting HEAD: `32e0a8214354e89250a3c87c6a417e93a075b6ad`
- independently reviewed PF5-R8 HEAD: `96f154958d7238004fff44a0a3aa6f6b05e1fb59`
- measurement-window/control implementation: `6da70709f649b22dd1dd84c9107350cc21f79c01`
- exact drain-delta implementation: `3d339edbb526052b00b7f640ca8cd519628d3464`
- bounded exact-equality evidence and measurement HEAD:
  `4a1e01934ca87dbc63b471f5ca4f2edce58ad647`
- production semantic candidate: `14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9`

The only intervening commit before this task added archived VitaNex reference material; it did not
change replication, Network, the benchmark boundary, or product policy. PF5-R8-R1 changes only the
benchmark adapter and focused repository verification. No production project, protocol identity,
wire format, replication semantic, cadence, AOI, compression, aggregation, egress policy, or
participant bound changed.

The run used Release, .NET 8.0.29, CachyOS, x64, an AMD Ryzen 7 7800X3D, and 16 logical processors.
It used one process containing the authority and real secure loopback UDP clients, plus the bounded
`BackgroundPlatformBatch` sender owner. Synthetic populations above 64 are architecture probes,
not product CCU.

## 1. Measurement windows are now exact

Every cumulative child snapshot is frozen immediately after the final measured tick and before
`DrainToConvergence`:

```text
measured ticks end
  -> freeze process/transport/listener/profile/protocol snapshots
  -> build all measured-window timing and normalized results from those snapshots
  -> drain to convergence
  -> record drain-only exact deltas and final debt separately
```

The drain record intentionally contains exact counter/byte/call/total-time deltas. It does not
publish post-drain cumulative percentiles as though they were drain-window distributions, because
percentiles cannot be subtracted.

The deterministic regression ran the same 32-recipient workload with one and eight extra drain
passes. Measured publications, deliveries, proofs, messages, application bytes, protocol call
counts, and normalized work were identical. Drain-only server emit calls changed from 128 to 1,024;
both runs ended at zero debt. Thus extra convergence passes are visible only in the drain section.

The authoritative remote budget remains the paired same-tick sum:

```text
RemotePublicationPreparation
+ RecipientCommitObservation
+ NetworkAndRemoteReplication
```

The lower-overhead Off-mode coarse split is also paired per tick:

```text
NetworkAndRemoteReplication
  = Game schedule/codec/queue
  + server Game proof apply
  + Network protocol and other unsplit harness work
```

The third term is a measured residual, not a claim that all of it is crypto, security, or
unavoidable Network work.

## 2. Production-capacity body sharing under divergence

The old `CensusOnly` rows are retained solely as potential-identity evidence. New matched runs use
the production `Enabled` mode and its 1,024-body capacity.

| Scenario | Requests | Hits | Misses | Encoded copies | Materializations avoided | Evictions | Hit rate |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| synchronized | 46,080 | 45,360 | 720 | 45,360 | 45,360 | 60 | 98.44% |
| staggered joins | 38,768 | 38,048 | 720 | 38,048 | 38,048 | 70 | 98.14% |
| cadence phases | 11,904 | 11,168 | 736 | 11,168 | 11,168 | 73 | 93.82% |
| controlled loss | 46,115 | 44,695 | 1,420 | 44,695 | 44,695 | 478 | 96.92% |
| controlled reorder | 46,080 | 44,648 | 1,432 | 44,648 | 44,648 | 496 | 96.89% |
| proof delay | 46,080 | 45,360 | 720 | 45,360 | 45,360 | 78 | 98.44% |
| temporary stalls | 42,240 | 41,512 | 728 | 41,512 | 41,512 | 81 | 98.28% |
| AOI churn | 42,464 | 41,459 | 1,005 | 41,459 | 41,459 | 182 | 97.63% |
| reconnect | 46,080 | 45,360 | 720 | 45,360 | 45,360 | 69 | 98.44% |
| history pressure | 42,112 | 41,392 | 720 | 41,392 | 41,392 | 68 | 98.29% |
| combined divergence | 8,205 | 6,228 | 1,977 | 6,228 | 6,228 | 982 | 75.90% |

There were zero identity conflicts. Every hit performed a real encoded-body copy and avoided a
record materialization. Each Enabled result matched its CensusOnly control in exact normalized
recipient knowledge, logical publications, canonical application bytes, proof outcomes,
snapshots, and reconnects. Each run also independently verified that populated recipient scopes
ended at the current source epoch/key/revision.

History pressure forced 128 explicit `HistoryFloorLost` Rebases while the body store was active;
reconnect created eight fresh lifetimes and 64 `ReconnectGenerationReset` introductions. The
focused retained regressions additionally prove that a resident body cannot bypass missing
contiguous coverage, source replacement, removal/slot reuse, queue refusal, or independent
recipient delivery/proof identity.

## 3. Fixed delivered work, varied source topology

The new control holds the following measured-window semantic and representation work exactly
constant in every row:

```text
15,360 admitted recipient deliveries
15,360 delivered records
15,360 Game messages
15,360 proofs
1,920,000 application bytes
512 deliveries per measured tick
```

Only the number of distinct source preparations/topology groups is intentionally varied.

| Shape | Unique source preparations | Off schedule mean | Off proof mean | Off protocol/other mean | Server-owned remote mean | p95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1×512 | 30 | 0.296 ms | 0.192 ms | 1.756 ms | 2.243 ms | 2.477 ms |
| 8×64 | 240 | 0.325 ms | 0.195 ms | 1.808 ms | 2.329 ms | 2.482 ms |
| 64×8 | 1,920 | 0.349 ms | 0.187 ms | 1.726 ms | 2.263 ms | 2.336 ms |
| 512×1 | 15,360 | 0.664 ms | 0.191 ms | 1.778 ms | 2.633 ms | 2.795 ms |

Proof and protocol/other work remain in the same broad range. Scheduling increases at the
512-unique-source extreme, from 0.58 to 1.30 microseconds per delivery versus the 1×512 row. One
run is not a causal timing fit, but the operation control now
cleanly distinguishes required recipient work from changing source-body cardinality.

This result does not show that topology is free. It shows that, after delivered records/messages/
proofs/bytes are fixed, a large required per-recipient output floor remains and unique source
preparation adds a smaller measurable scheduling cost.

## 4. Lower-overhead attribution

For the R128/high (45 changed entities/tick) Off row, the 1.136 ms/tick mean server-owned remote
budget is directly partitioned as:

| Paired Off component | Mean | Share |
| --- | ---: | ---: |
| source preparation + recipient commit observation | 0.018 ms | 1.6% |
| Game schedule / codec / queue | 0.519 ms | 45.7% |
| server Game proof application | 0.126 ms | 11.1% |
| Network protocol + other unsplit remote work | 0.474 ms | 41.7% |

The three children of `NetworkAndRemoteReplication` reconcile exactly with its 1.118 ms mean.
This is deliberately coarser than Detailed mode, but it avoids Detailed's uneven observer tax.
Across the bracketed telemetry controls, high-change Detailed scheduling is roughly 2.2× Off and
Detailed proof processing is roughly 3.5× Off. Detailed operation counts remain useful; its child
timings are diagnostic upper-bound evidence, not production weights.

The corrected R128/high Detailed window counted 21,332 deliveries/proofs/retirements, 20,523 body
hits, 809 misses, 39,750 coverage links, 41,915 delivery-ID lookup comparisons, 50,982 obsolete
checks, 2.254 MB of body bytes copied, and 4.958 MB of bundle-record bytes copied. It ended with
zero debt, refusal, contradiction, invalidation, or source gap. These counts are no longer polluted
by drain work.

## 5. Required, repeated, and unresolved work

Measured required responsibility under the current policy:

- independent application epoch/header, delivery ID, admission transaction, outstanding debt,
  proof, cursor, absence, and lifetime fences per recipient;
- a final independently selected output plus secure Network/frame/datagram processing;
- work proportional to the semantic recipient obligations the selected AOI/cadence actually
  creates.

Confirmed compatible reuse already operating in production:

- prepared source intervals are shared;
- the 1,024-body store serves exact compatible record bodies under synchronization, loss,
  reordering, stalls, AOI churn, eviction, and reconnect;
- a body hit skips collapse, sorting, record materialization, validation hashing, and record
  serialization, while still validating retained coverage.

Unresolved or not proven minimal:

- the 0.519 ms Off schedule/codec/queue bucket at R128/high;
- the 0.474 ms Off Network/other bucket, which requires a lower-overhead internal split before
  assigning cost to framing, crypto, packet assembly, sender transfer, or harness overhead;
- per-recipient coverage validation and final byte movement, despite compatible source/body
  identity;
- linear proof lookup/obsolete scans at artificially deep debt. Current product-shaped debt is too
  shallow to justify an index from this evidence.

At fixed source work, cost per delivered obligation falls as fixed costs amortize and is broadly
stable at the larger points: server-owned mean is about 1.97, 1.72, and 1.58 microseconds per
delivery at R64/R128/R256. No increasing per-obligation operation was found there. The R300→R350
high-change step is instead accompanied by a 50% increase in prepared source publications and a
55.6% increase in recipient publications for only 16.7% more recipients. That supports a changed-
work explanation, not a sudden algorithmic collapse. A consistent operation can still be more
expensive than necessary, so this is not proof of optimality.

## 6. Proof debt and rated boundary

Controlled proof-delay rows retain separate delivery lookup and obsolete-scan counts. At depth 64,
draining unrelated-chunk debt uses about 16.4 delivery lookup comparisons and 31.3 obsolete checks
per proof; same-chunk debt has a similar lookup count and expected same-chunk matches. The current
product-shaped R128/high row reaches outstanding high-water eight and about 2.39 obsolete checks
per proof. No production index is selected.

The fresh Off ratings retained every repetition:

| Workload | schedule p95 | server-owned remote p95 |
| --- | ---: | ---: |
| R256/high | 1.178 ms | 2.885 ms |
| R300/low, three runs | 0.988–1.028 ms | 2.363–2.444 ms |
| R300/high, three runs | 1.179–1.199 ms | 3.360–3.411 ms |
| R350/high | 2.423 ms | 5.090 ms |
| R400/high | 2.544 ms | 5.919 ms |

All three final R300/high repetitions pass the approximate 4 ms budget. R350 is the first clearly
failing tested population, so the tested boundary remains between R300 and R350. This remains a
synthetic architecture curve, not CCU or a promise about an intermediate population.

## 7. Disposition and next experiments

No production optimization is accepted. Another 50–100 synthetic recipients is an unsupported
promise: the observed R300→R350 work itself changes materially, while the remaining separately
measured local opportunities are much smaller than the roughly 1.6–1.9 ms p95 increase.

Ranked future experiments, none prerequisites for PF6:

1. Split the Off schedule/codec/queue bucket with batch-level, low-frequency counters around
   eligibility, coverage/body acquisition, representation, compression, admission, and debt. A
   win requires identical semantic/wire work and a matched Off A/B; current Detailed timings are
   too observer-heavy to select an optimization.
2. Prototype a retention-generation-fenced compatible-body lease that combines coverage validation
   with borrowing immutable canonical bytes through final representation staging. It must fail
   closed on eviction, `HistoryFloor`, source/AOI/session replacement, packet-boundary carry, and
   queue refusal. Current copy/coverage timings are small upper bounds, so added lifetime/memory
   complexity may not pay.
3. Reconsider a bounded delivery-ID/chunk proof index only if real impairment drives outstanding
   depth materially above the current high-water. Acceptance requires matched debt-depth curves,
   randomized lifecycle/proof ordering, exact absence semantics, and a demonstrated Off-mode win.

PF5-R8-R1 found measurement and coverage defects, not a product-runtime correctness defect.
Production policy and the semantic candidate remain unchanged. PF6 remains ready to resume.

## Verification and reproduction

The focused PF5-R8 verifier passed all 176 Game replication tests and the real-secure-UDP focused
matrix. The complete canonical Release verifier passed, including physical Single Player/Online/
Complete publishes and exports, Contentless Online cold/warm paths, mixed exported-client swarms,
lifecycle/parity gates, and `git diff --check`.

Evidence:

- `concept-demo-pf5-r8-r1-measurement-closure-2026-09-16.json`
- `concept-demo-pf5-r8-r1-measurement-closure-2026-09-16.raw.json`

```bash
dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj \
  -c Release -- --pf5-r8-required-fanout \
  --output _Benchmarks/Baselines/concept-demo-pf5-r8-r1-measurement-closure-2026-09-16.raw.json

dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf5-r8-only

./verify.sh --configuration=Release
```
