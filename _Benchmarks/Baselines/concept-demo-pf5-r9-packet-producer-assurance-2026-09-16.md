# PF5-R9 — packet-producer assurance and cost audit

PF5-R9 closes one security/correctness defect, two measurement/performance defects, and one
deployment-policy question. It does not claim that the remaining producer is globally minimal or
that another population increment is available.

The closure is **Outcome A**:

> Correctness and measurement repairs close. Detailed background-egress timing becomes opt-in
> after a matched whole-path cost measurement. No packet, proof, cadence, AOI, compression,
> aggregation, participant-cap, or replication semantic policy changes.

## Lineage and scope

- task starting HEAD: `1cf51fc833b7c12b9b7ea971a8201ba2fd0663d3`
- prior PF5-R8-R1 final: `e57865941222c0aa85c659a02236f4dac1bba59c`
- replication semantic candidate: `14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9`
- AES-GCM nonce repair: `c95c77bf`
- complete background-enqueue measurement: `f5e5bf64`
- dormant Detailed-slab clearing repair: `e9997ed5`
- primary measurement implementation: `de2f84f3`
- asynchronous-cohort correction / measurement HEAD: `d6abeb93629b65bcd53bb056c726ae1c9a341e61`
- background timing policy: `4a58e58ad08c5dc8ea77eab6983414d4d0f64b49`
- exact compression-input census: `f9b0a44d2e583b75c8511dbebe74bcbfbf653101`
- integrated production candidate entering final verification: `f9b0a44d2e583b75c8511dbebe74bcbfbf653101`

The pre-existing dungeon-plan edit was already committed as the doc-only starting commit. PF5-R9
did not modify that file or any unrelated work.

The retained policy is ChunkPublication, PF5-R6 compatible immutable body/canonical-byte sharing,
CurrentBundled, selective multi-entry message-148 LZ4, singleton compression off, 1/2/4 cadence,
CrossLane cap two, BackgroundPlatformBatch, manifest v29, transport revision 2, 60 Hz authority,
and product participant cap 64. Synthetic populations above 64 remain architecture probes.

The primary campaign used Release, .NET 8.0.29, CachyOS Linux x64, an AMD Ryzen 7 7800X3D,
16 logical processors, and a 1 GHz Stopwatch counter. The fixture is one process containing the
authority and secure-loopback protocol-adapter clients, plus the bounded background sender thread.
Process CPU/RSS therefore are not server-only metrics.

## Phase A — assurance repairs

### A1. Refused encryption reused an AES-GCM nonce

**Confirmed defect; high severity; high confidence.**

`NetworkProtocolPeer.TryEncryptAndSend` previously selected `_nextPacketNumber`, encrypted, called
the endpoint, and advanced the number only after `Accepted` or `DroppedByImpairment`. A refused
submission therefore allowed another encryption invocation under the same directional key and
epoch to reuse the same nonce. Changed latest-state plaintext, ACK, KeepAlive, or other associated
data could occupy that second invocation. The probe establishes the in-process invariant failure;
it does not claim that both ciphertexts were remotely exposed.

The repair allocates and irrevocably consumes the packet number before header construction and
encryption. Admission remains independently transactional:

```text
allocate nonce identity
    -> encrypt exactly once
    -> attempt endpoint admission

refusal:
    nonce remains consumed
    application/reliable work remains pending
    sent counters do not advance
    delivery/proof state does not advance
```

`EncryptionInvocations` and attempted plaintext bytes now account for actual crypto work;
successful-send counters continue to account only for admitted transmissions. Packet-number gaps
are valid. Exhaustion fails without wraparound.

Permanent regressions observe the actual attempted secure header and cover:

- refused Data, an interleaved accepted KeepAlive, then changed latest-state Data;
- refused ACK and retry;
- 70 refused reliable attempts, a gap larger than the ACK bitmap, and eventual reliable
  completion/proof;
- exhaustion at `ulong.MaxValue` without reuse or wrap;
- admission refusal retaining protocol/application state.

Reverting the allocation to its old post-admission location makes the unique-packet-identity
regression fail.

### A2. The old enqueue timer was narrow

**Confirmed measurement omission; medium severity for attribution; high confidence.**

`SharedUdpBackgroundEgress.ProducerEnqueue` intentionally remains the accepted queue-mutation core.
It begins after `ThrowIfFaulted`, has no refusal sample, and ends before its own distribution update
and `_wake.Set`.

`SharedUdpServerListener` now owns a separate enclosing `ProducerEnqueueComplete` timer. It covers:

```text
fault inspection
refusal or accepted queue mutation
accepted-core diagnostic update
empty-to-nonempty worker wake
```

It excludes only its own later outer distribution recording. Accepted, refused, and faulted
outcomes are separate. Physical accounting remains active when timing is disabled.

Across the accepted ordinary runs, the weighted accepted-core mean was 0.136–0.149 microseconds;
the complete-call mean was 0.194–0.204 microseconds. The old timer omitted about 0.052–0.060
microseconds per accepted call. That is a material fraction of this tiny operation, but it is not a
large unexplained millisecond-scale server cost.

### A3. Off/Basic still cleared a Detailed-only typed slab

**Confirmed performance defect; low severity; high confidence.**

`RemoteChunkWorldStateServer.ClearBundleStaging` cleared `_bundleDeltaRecords` even when the slab
was neither populated nor read outside Detailed telemetry. Off and Basic now skip only that typed
diagnostic slab. Functional encoded-record staging and required functional/security clearing remain
unchanged.

The regression seeds and directly inspects the slab across Off, Basic, Detailed, and
Detailed-to-Off transitions. It does not depend on counters that themselves turn off. Removing the
guard makes the slab mutate and the regression fail.

## Timing and cohort boundaries

The authoritative main-thread budget is the paired same-tick sum:

```text
RemotePublicationPreparation
+ RecipientCommitObservation
+ NetworkAndRemoteReplication
```

The measured window freezes profile, protocol, transport, listener, process, and raw per-tick
samples before convergence drain. Drain deltas and final debt remain separate.

Within `NetworkAndRemoteReplication`, the Detailed-only diagnostic split is:

```text
Game schedule / codec / queue
+ server Game proof application
+ server outbound protocol parent
+ server inbound protocol parent
+ signed residual
```

The residual is parent minus those paired children for each tick. It is not clamped. Outbound and
inbound protocol parents are direct enclosing intervals. Their deeper stage clocks overlap their
parents and are reported only as child attribution. Sender-thread service and syscall wall time are
off-thread and are never added to the authority-thread percentile.

Asynchronous egress uses an inventory/cohort contract:

```text
opening queued inventory + accepted admissions
    = physical completions + explicit drops/failures + ending queued inventory
```

It is intentionally not forced into an invalid same-window `accepted == physically sent`
assertion. Ordinary retained rows happened to end balanced with zero refusals; pressure regressions
exercise partial native send, WouldBlock, no-buffer, rebind, retirement, physical loss/retry, and
worker fault independently.

## Background timing policy

All four matched rows used identical repaired code, semantic digests, publications, Game messages,
and proof counts. Three repetitions alternated timing-on/off order; no tail was discarded.

| Workload | Off mean | On mean | Δ mean | Off p95 | On p95 | Δ p95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| R128/high | 0.907 ms | 0.922 ms | +0.015 ms | 1.021 ms | 1.031 ms | +0.009 ms |
| R300/low | 2.297 ms | 2.330 ms | +0.034 ms | 2.443 ms | 2.513 ms | +0.071 ms |
| R300/high | 3.096 ms | 3.197 ms | +0.101 ms | 3.325 ms | 3.426 ms | +0.101 ms |
| R350/high | 4.467 ms | 4.624 ms | +0.157 ms | 4.860 ms | 5.074 ms | +0.214 ms |

The production default is therefore now timing **off**. Operators and benchmarks can opt in
independently of Game telemetry and Network stage timing. Always-on physical bytes, datagrams,
admissions, refusals, errors, queue occupancy/high-water, and cleanup correctness remain. What is
lost while off is detailed enqueue/queue-residence/worker-service timing distributions.

At R300/high with timing on, the worker accepted and physically sent about 294,217 datagrams per
270-tick run, used about 9,477 native send batches, and recorded approximately 747 ms worker-service
wall time, of which about 723 ms was physical syscall time. Queue-residence p95 occupied the
bounded histogram bucket ending at about 2.10 ms. These are off-thread wall measurements and are
not authority CPU.

## Main-thread decomposition

Detailed mode is a diagnostic pass, not the production rating. It measurably inflates scheduling
and proof work, so its absolute stage weights are not divided by a global correction factor.

| 30-tick diagnostic | R128/high | R300/high |
| --- | ---: | ---: |
| complete server-owned mean | 1.925 ms/tick | 6.754 ms/tick |
| Game schedule/codec/queue | 0.891 | 3.366 |
| Game proof application | 0.234 | 0.868 |
| server outbound protocol | 0.340 | 1.443 |
| server inbound protocol | 0.147 | 0.371 |
| signed residual | 0.301 | 0.670 |
| residual per-tick range | 0.275–0.320 | 0.653–0.726 |

The small remainder between the complete server-owned mean and this parent split is source
publication preparation plus recipient commit observation. The residual remains explicitly
unattributed; it includes harness/protocol orchestration not enclosed by the new server outbound and
inbound parents. It is not labeled encryption, synchronization, or unavoidable work.

The following child totals are for 30 Detailed ticks:

| Child responsibility | R128/high total | R300/high total |
| --- | ---: | ---: |
| shared encoded-body copy | 0.275 ms | 1.290 ms |
| bundle-record copy | 0.378 | 1.727 |
| packet-boundary carry copy | 0.838 | 6.871 |
| functional/diagnostic staging clear | 0.445 | 2.403 |
| bundle encode | 0.249 | 0.477 |
| Game→Network queue call | 0.512 | 2.826 |
| compression | 4.030 | 4.762 |
| Network selection | 0.663 | 2.869 |
| frame encode | 0.379 | 2.162 |
| secure send (header + AES-GCM) | 3.317 | 20.806 |
| endpoint/background handoff | 1.417 | 4.711 |
| endpoint receive | 0.691 | 1.684 |
| secure receive | 2.328 | 5.978 |
| frame admission/dispatch | 0.470 | 1.293 |

These children are not all siblings and must not be summed into a new parent. For example,
compression and copy/queue timings live inside schedule/codec/queue, while Network selection,
secure send, and endpoint handoff live inside the server outbound parent.

At R128/high, 8,843 of 8,984 compatible-body requests hit; only 141 bodies were built. Hits copied
5.94 MB of canonical record bytes and avoided 8,843 materializations/hashes. At R300/high, 37,430
of 37,725 requests hit; 295 bodies were built, eight evictions occurred, and 29.07 MB were copied.
The old reconstruction waste remains largely eliminated. Required recipient envelopes, admission,
delivery debt, proof, secure framing, and final byte movement still scale with actual obligations.

Proof debt remained shallow in this workload: R128 and R300 outstanding high-water were three and
five. The Detailed census records delivery-ID lookup comparisons separately from obsolete-debt
checks. No new proof index is selected; the realistic debt does not pay for its complexity.

## Exact compression-input compatibility

Source-body compatibility is not sufficient evidence for compression-result reuse. PF5-R9 adds a
bounded 4,096-entry Detailed-only oracle over the complete canonical message-129 bytes immediately
before LZ4. Hashes select candidates; equality is confirmed by length and exact bytes. The oracle
never supplies production output and freezes at the pre-drain boundary.

| Epoch model | Workload | Attempts | Distinct | Exact repeats |
| --- | --- | ---: | ---: | ---: |
| old synthetic shared epoch | R128/high | 2,444 | 99 | 2,345 (95.95%) |
| old synthetic shared epoch | R300/high | 2,863 | 275 | 2,588 (90.39%) |
| independent recipient epochs | R128/high | 2,444 | 2,444 | 0 |
| independent recipient epochs | R300/high | 2,863 | 2,863 | 0 |

There were no hash collisions or capacity saturations. The independent-epoch control matches the
ordinary product invariant that application lifetimes are recipient-specific. Canonical 129
contains the application session epoch, bundle/delivery identity, and entry delivery IDs before
compression. Thus the complete inputs are not byte-compatible even though their record bodies are.

Compression-result reuse is rejected for production. Ciphertext sharing is categorically invalid
across independent security contexts and was never considered.

## Primitive controls

The actual retained directional AES-GCM implementation and actual
`NetworkProtocolPeer.TryQueue+Emit` path were measured after warmup with a fresh nonce for every
invocation and zero warmed managed allocation:

| Payload | AES-GCM mean / p95 | queue+selection+frame+AES+endpoint mean / p95 |
| ---: | ---: | ---: |
| 64 B | 413 / 400 ns | 608 / 600 ns |
| 128 B | 405 / 400 | 617 / 700 |
| 256 B | 405 / 400 | 703 / 700 |
| 512 B | 504 / 500 | 710 / 700 |
| 1,024 B | 509 / 500 | 749 / 800 |
| maximum (1,428 B plaintext / 1,405 B app payload) | 595 / 600 | 815 / 800 |

The curve shows both fixed invocation cost and byte-dependent cost. It does not justify one global
AES ns/byte constant. Integrated cost is higher because it includes recipient count, idle pumps,
cache state, real listener handoff, receive/proof traffic, and contention. No integrated p95 is
reconstructed by summing primitive percentiles.

## Fixed delivered work and rated boundary

The corrected fixed-delivery control holds exactly 30,720 publications, Game messages, proofs,
5,406,720 Game bytes, and 61,440 wire datagrams in every row:

| Source topology | server-owned mean | p95 |
| --- | ---: | ---: |
| 1×512 | 2.190 ms | 2.410 ms |
| 8×64 | 2.232 | 2.421 |
| 64×8 | 2.204 | 2.388 |
| 512×1 | 2.631 | 2.885 |

The 512-source extreme adds about 0.44 ms mean while delivered records/messages/proofs/bytes stay
fixed. This is measured topology/source overhead, not recipient-delivery amplification.

Instrumentation-Off rating averages:

| Workload | mean | p95 |
| --- | ---: | ---: |
| R128/high | 0.907 ms | 1.021 ms |
| R300/low | 2.297 | 2.443 |
| R300/high | 3.096 | 3.325 |
| R350/high | 4.467 | 4.860 |

R300/high retained all three runs, including a 3.596 ms first-run tail. R350/high is the first
tested point clearly above the approximate 4 ms authority-thread budget, so the revised tested
crossing is between R300/high and R350/high. This is an evidence correction on the current host,
not a product-cap change or CCU claim. R300→R350 also increases prepared source publications by
50% and recipient publications by roughly 55.6% for 16.7% more recipients; it is not a pure
recipient-count cliff.

The all-client fixture used roughly 1.0–1.06 process CPU cores in the retained runs and reached a
process peak working set up to about 950 MB across the sequential campaign. Those values include
the in-process client copies and must not be quoted as server-only resource use.

## Findings and next experiments

1. **Confirmed defect, repaired:** nonce allocation occurred after admission. The direct packet-
   identity regressions prove unique allocation under refusals, interleaving, large gaps,
   retransmission, epoch lifetime, and exhaustion.
2. **Confirmed avoidable diagnostics, repaired:** dormant typed-slab clearing and default detailed
   background timing imposed work without changing semantics.
3. **Measured required responsibility under current policy:** recipient header/delivery identity,
   exact admission/debt/proof, secure packet construction, and final bounded handoff remain
   recipient-specific. This classifies responsibility, not theoretical minimum cost.
4. **Unattributed:** 0.301 ms/tick at Detailed R128 and 0.670 ms/tick at Detailed R300 remain in
   the signed main-thread residual. Detailed observer tax prevents treating them as production
   weights.
5. **Small measured concern:** proof retirement scans are real but debt high-water three/five does
   not justify an index here.
6. **Rejected experiment:** compatible compression-output caching. Exact complete inputs repeat
   only when the fixture incorrectly shares an application epoch; production-shaped epochs remove
   every repeat.

At most three future experiments are earned, none a PF6 prerequisite:

1. Sparse one-family diagnostic passes around packet-boundary carry copying and the remaining
   signed residual. Acceptance requires a matched Off whole-path win without greater queue age,
   worker CPU, memory, or semantic change.
2. A production-process profiler run separating AES implementation CPU from cache/dispatch and
   thread scheduling. The primitive ceiling suggests crypto is material but not the entire
   producer.
3. Proof indexing only if a real impairment/product run raises outstanding depth enough that exact
   lookup plus obsolete scans become material.

Another 50–100 synthetic recipients is an upper-bound possibility, not a defensible promise. The
current evidence shows both more required work and nonzero cost per operation, but no tested local
change demonstrates that population gain.

## Verification and reproduction

Focused verification:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf5-r9-only
```

Full primary campaign:

```bash
dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj \
  -c Release -- --pf5-r9-packet-producer \
  --output _Benchmarks/Baselines/concept-demo-pf5-r9-packet-producer-audit-2026-09-16.raw.json
```

Compression-input controls:

```bash
dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj \
  -c Release -- --pf5-r9-packet-producer --diagnostic-only \
  --warmup 60 --diagnostic-ticks 30 \
  --output _Benchmarks/Baselines/concept-demo-pf5-r9-compression-input-census-2026-09-16.raw.json

dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj \
  -c Release -- --pf5-r9-packet-producer --diagnostic-only --unique-epochs \
  --warmup 60 --diagnostic-ticks 30 \
  --output _Benchmarks/Baselines/concept-demo-pf5-r9-compression-input-census-unique-epochs-2026-09-16.raw.json
```

Raw SHA-256:

- primary: `2533853f1180968c660f1d7e056c273d15299b473923f105fac093496039f36f`
- shared-epoch census: `470115af97c1e443a5fbceca9a7230851467cb445d79d1b4d118e7ef5f2a5410`
- independent-epoch census: `492e16f9dcc2239b5bae620beef03869394dddcda764a4bcd84abac631c9769e`

The Network suite, 177 replication tests, PF5-R9 focused verifier, and the complete canonical
Release/export verifier pass on the final implementation and evidence tree. The canonical run also
passed Contentless Online, the mixed exported-client/headless-bot swarm, edition boundaries, and
lifecycle/parity gates.

PF6 may resume. Optional producer profiling does not block human validation; an unresolved nonce
defect would have blocked it, but that defect is repaired and permanently regressed.
