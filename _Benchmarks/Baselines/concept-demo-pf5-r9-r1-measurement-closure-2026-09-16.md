# PF5-R9-R1 — packet-producer measurement closure

PF5-R9-R1 independently confirms and repairs both benchmark defects reported after PF5-R9. It also
measures the real concurrent background handoff and runs lower-overhead, one-family attribution
controls. No production runtime or replication semantic code changes in this follow-up.

The disposition is **Outcome C for the optional producer-attribution investigation**:

> Epoch propagation and primitive precision close. Concurrent handoff and packet-boundary carry
> now have direct measurements. A material residual remains inseparable from Network timing
> observer tax, so the producer is not declared globally minimal and no additional recipient gain
> is claimed. This optional evidence gap is not a PF6 blocker.

## Lineage and policy

- starting HEAD: `f9395027083abe4d674acae853bd8b7cb598e2cd`
- measurement implementation: `0e96535cabc9750620c85d373128c7497bfd294f`
- replication semantic candidate: `14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9`
- integrated production candidate: `f9b0a44d2e583b75c8511dbebe74bcbfbf653101`
- production semantics changed: **no**

The retained product policy is ChunkPublication, compatible immutable body/canonical-byte sharing,
CurrentBundled, selective multi-entry message-148 LZ4, singleton compression off, 1/2/4 cadence,
CrossLane cap two, BackgroundPlatformBatch, manifest v29, transport revision 2, 60 Hz authority,
and participant cap 64. Synthetic populations above 64 remain architecture probes.

Measurements used Release, .NET 8.0.29, CachyOS Linux x64, and an AMD Ryzen 7 7800X3D. `perf` and
`dotnet-trace` were not available, so the maintained bounded counters/timers remain the source of
record.

## Confirmed benchmark repairs

### Independent application epochs

The report-level `--unique-epochs` flag previously reached only diagnostic attribution. Timing and
fixed-delivery rows silently used a shared application epoch.

Production-shaped rows now default to one epoch per recipient. Shared epochs require the explicit
`--shared-epoch-control` option. Every timing, topology, sparse, and diagnostic row records:

```text
recipient count
distinct epoch count
minimum epoch
maximum epoch
```

The focused gate requires `DistinctEpochCount == Recipients` for ordinary rows and exactly one for
the explicit shared control. The actual values passed to both server and client replication
adapters are the values reported.

### Primitive timestamp precision

The old primitive used `Stopwatch.GetElapsedTime(...).TotalNanoseconds`, which first rounded the
duration through 100 ns `TimeSpan` ticks. The repaired primitive retains raw timestamp differences
and converts only after capture:

```text
nanoseconds = raw ticks × 1,000,000,000 / Stopwatch.Frequency
```

A permanent conversion probe requires 450 ticks at a 1 GHz counter to remain exactly 450 ns.

## Corrected rated curve

All rows below use independent recipient epochs, background detailed timing Off, three alternating
matched repetitions, 60 warmup ticks, and 270 measured ticks. No tails were discarded.

| Workload | Mean | Average p95 | Individual run p95 |
| --- | ---: | ---: | --- |
| R128/high | 0.947 ms | 1.068 ms | 1.089 / 1.060 / 1.053 ms |
| R300/low | 2.435 | 2.554 | 2.718 / 2.435 / 2.510 |
| R300/high | 3.357 | 3.631 | 3.852 / 3.520 / 3.522 |
| R350/high | 4.953 | 5.433 | 5.576 / 5.376 / 5.346 |

The corrected tested crossing remains between R300/high and R350/high. This is not product CCU.
R350 also performs materially more semantic work than R300, as the PF5-R9 report already records.

### Same-code epoch control

The same measurement implementation was rerun with explicit shared epochs:

| Workload | Independent mean / p95 | Shared mean / p95 |
| --- | ---: | ---: |
| R128/high | 0.947 / 1.068 ms | 0.941 / 1.054 ms |
| R300/low | 2.435 / 2.554 | 2.461 / 2.637 |
| R300/high | 3.357 / 3.631 | 3.346 / 3.628 |
| R350/high | 4.953 / 5.433 | 4.973 / 5.455 |

At R300/high the independent-epoch delta is +0.34% mean and +0.09% p95. Other rows move in both
directions. The change from the historical PF5-R9 curve is therefore not attributed to epoch shape.

Independent epochs do slightly change representation bytes because the complete canonical
messages contain application lineage. R300/high increased from 311,684,599 to 311,718,396 wire
bytes across the complete measured run, about 0.011%.

## Detailed background timing remains opt-in

| Workload | Timing-On mean tax | Timing-On p95 tax |
| --- | ---: | ---: |
| R128/high | +0.024 ms | +0.023 ms |
| R300/low | +0.075 | +0.156 |
| R300/high | +0.170 | +0.183 |
| R350/high | +0.156 | +0.185 |

The PF5-R9 default change remains justified. Physical datagram/byte accounting, admissions,
refusals, failures, queue occupancy, and lifecycle correctness remain active while detailed timing
is Off.

## Corrected primitives

Raw timestamp conversion produces the following warmed results. Allocations are zero on the
measured producer thread.

| Payload | AES-GCM mean / p95 | Queue + select + frame + AES + accepting endpoint mean / p95 |
| ---: | ---: | ---: |
| 64 B | 608 / 860 ns | 802 / 840 ns |
| 128 B | 576 / 590 | 780 / 810 |
| 256 B | 570 / 570 | 805 / 830 |
| 512 B | 618 / 620 | 842 / 870 |
| 1,024 B | 663 / 670 | 904 / 930 |
| maximum | 696 / 700 | 931 / 970 |

The previous 100 ns-quantized values are superseded. The actual operation still has fixed and
byte-dependent components; one aggregate ns/byte value is not valid.

## Actual concurrent background handoff

The new primitive uses the real path:

```text
SharedUdpServerEndpoint
    -> SharedUdpServerListener
    -> bounded SharedUdpBackgroundEgress copy/address/accounting/synchronization
    -> real concurrent native sender
    -> loopback UDP socket
```

The producer interval stops at admission. Worker service/syscall time remains asynchronous and is
not added to producer latency. Every cohort reconciles accepted work to physical sends, has zero
refusals, and allocates zero warmed bytes on the producer thread.

| Datagram | Empty→nonempty mean / p95 | Sustained nonempty mean / p95 |
| ---: | ---: | ---: |
| 64 B | 1,119 / 2,070 ns | 526 / 1,230 ns |
| 512 B | 1,420 / 2,290 | 485 / 1,330 |
| 1,472 B | 1,353 / 2,250 | 137 / 90 |

The sustained 1,472-byte mean exceeds p95 because a small number of long concurrent-contention
tails are retained; its p50 is 60 ns and maximum 58,979 ns. This is why both distributions and
tails are retained rather than quoting only a mean.

Ordinary R300/high runs accepted 294,217 datagrams and woke the worker about 270 times—roughly one
wake per authority tick, not one per datagram. Empty-queue wake cost is therefore real but not a
dominant per-datagram charge. Most production-shaped admissions occur while the queue remains
nonempty.

## Sparse attribution and remaining residual

The sparse Network pass keeps Game telemetry Off and enables Network stage timing only. The Game
pass separately compares Off with Detailed while keeping Network and egress timing Off.

| Network-only mean per tick | R128/high | R300/high |
| --- | ---: | ---: |
| complete server-owned remote budget | 1.182 ms | 3.942 ms |
| Network/remote parent | 1.170 | 3.909 |
| schedule/codec/queue | 0.345 | 1.095 |
| proof application | 0.074 | 0.259 |
| server outbound | 0.322 | 1.548 |
| server inbound | 0.164 | 0.393 |
| signed residual | 0.265 | 0.615 |

The matched all-detailed-timing-Off budget means were 0.967 and 3.276 ms. Enabling Network stage
timing alone therefore added about 0.215 and 0.666 ms/tick. The residual is of the same order as
that observer tax. It is still recorded, signed, and unexplained; it is not labeled encryption,
synchronization, or removable production work.

### Packet-boundary carry

With Network timing Off, the isolated Game Detailed pass measured:

| Workload | Calls | Bytes copied | Total | Mean/call | Mean/tick |
| --- | ---: | ---: | ---: | ---: | ---: |
| R128/high | 2,700 | 2,261,886 | 0.677 ms | 251 ns | 0.0226 ms |
| R300/high | 25,862 | 23,283,898 | 6.998 ms | 271 ns | 0.2333 ms |

The corresponding complete Game-Detailed budget inflation was 0.627 and 3.538 ms/tick. Carry copy
is therefore concrete but only a minority of Detailed observer inflation. It remains a possible
local experiment, not a promised whole-path saving; the current carry preserves a prepared
candidate across packet boundaries and cannot simply be deleted.

## Fixed-delivery topology

Every row has 512 independent epochs and exactly 30,720 publications, messages, proofs, 5,406,720
Game bytes, and 61,440 datagrams:

| Shape | Mean | p95 |
| --- | ---: | ---: |
| 1×512 | 2.283 ms | 2.344 ms |
| 8×64 | 2.297 | 2.364 |
| 64×8 | 2.356 | 2.544 |
| 512×1 | 2.873 | 3.335 |

Source/topology overhead remains measurable with delivered work held exact.

## Compression compatibility

The corrected independent-epoch diagnostic retains zero complete-input reuse:

| Workload | Attempts | Distinct | Exact repeats |
| --- | ---: | ---: | ---: |
| R128/high | 2,444 | 2,444 | 0 |
| R300/high | 2,863 | 2,863 | 0 |

The explicit shared control still produces 2,345 and 2,588 repeats. This proves that the census is
sensitive to the epoch configuration and preserves the PF5-R9 decision not to cache complete
compression results across real recipient lifetimes.

## Disposition

- The epoch-propagation and primitive-precision defects are closed with permanent regressions.
- The real concurrent handoff is no longer omitted from primitive evidence.
- Detailed background-egress timing remains opt-in.
- No new production optimization is adopted.
- The residual remains an optional profiling question. A profiler or a still-sparser Network
  aggregate-timing mode would be required before assigning it to production operations.
- Packet-boundary carry is the only new concrete local CPU lead, bounded at about 0.233 ms/tick in
  the observer-heavy R300/high diagnostic—not a promised p95 reduction.
- No additional 50–100 recipients are promised.
- PF6 may resume because no production correctness defect or failed product gate remains.

## Verification and evidence

Focused verification:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf5-r9-only
```

Corrected production-shaped campaign:

```bash
dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj \
  -c Release -- --pf5-r9-packet-producer \
  --output _Benchmarks/Baselines/concept-demo-pf5-r9-r1-packet-producer-audit-2026-09-16.raw.json
```

Explicit shared-epoch control:

```bash
dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj \
  -c Release -- --pf5-r9-packet-producer --shared-epoch-control \
  --output _Benchmarks/Baselines/concept-demo-pf5-r9-r1-shared-epoch-control-2026-09-16.raw.json
```

Raw SHA-256:

- independent epochs: `9e97ec5fc9aca394b5c29591dd9559225f778970f955fcd0cdf8b625a6aac9b6`
- explicit shared control: `eb322a4603875eba0808c5af7f7004ea0e0c7f6666a9d3857e63ca3275506f18`

The Network suite, 177 replication tests, and PF5-R9 focused verifier pass. Canonical Release
verification also passes on the final evidence tree, including exports, Contentless Online, mixed
exported-client/bot swarms, lifecycle, and final composition parity.
