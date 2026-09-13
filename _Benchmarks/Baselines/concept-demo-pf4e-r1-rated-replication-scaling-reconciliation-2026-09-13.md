# PF4E-R1 rated replication scaling reconciliation

Date: 2026-09-13

Disposition: **CLOSED — Outcome B, selected-profile cost difference confirmed.**

PF4E-R1 changes benchmark instrumentation and repository verification only. It changes no
production code, protocol, replication owner, AOI policy, cadence, proof semantics, reliability,
compression, participant limit, or product composition.

## Provenance and reproduction

- Starting HEAD: `ee9fc2f001469bdb2b0be65feb94563beec7ed97` (`Record PF4E closure commit`).
- Feature-complete/product candidate: `af9fcb740c02eb08efa8d4985d929b50b135504e`.
- PF4E evidence implementation: `254bd60a05639f2c069cd6445032a5c2eda0fabb`.
- PF4E closure marker at start: `ee9fc2f001469bdb2b0be65feb94563beec7ed97`.
- PF4E-R1 measured code: `c7bd25ee87605bf048b82cdc36a7fde5d46069a6`.
- Historical P0.3J closure: `4383a439411c8ba0c3817c6c823008afee67eef1`.
- Historical P0.3J measured code: `6969f43836693f14718b0d266d0e14d9431b4850`.
- Historical evidence: `network-p0.3j-rated-server-role-probes-off-2026-08-24.json` and `.md`.
- Runtime: .NET runtime 8.0.29; SDK 8.0.129; Release configuration.
- OS: CachyOS Linux, reported by the runtime as `Unix 7.1.5.1`.
- Processor: AMD Ryzen 7 7800X3D, 8 cores / 16 logical processors.
- `Stopwatch.Frequency`: 1,000,000,000.
- Deterministic execution seed: 84,111,361.
- Every retained row: 30 warmup ticks, 270 measured ticks, 16 ms server tick, selected
  Immediate/Near/Far 1/2/4 cadence, real shared loopback UDP, probes off.
- Primary current-policy rows use unreliable frame cap two. A separate cap-one current-control
  artifact reproduces the historical physical policy for the strictest comparison.
- Benchmark-only capacity is sized coherently for each population. The production 64-participant
  limit is unchanged.
- Each curve receives a separate 32-recipient process-tiering prime outside retained rows.
- Rating rule: retain completed rows until the first `legacy ServerRole p95 > 4.000 ms` after a
  completed passing row. `--retain-full-ladder` remains available for explicit extensions.

Primary command:

```text
dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj -c Release --no-build -- --p03-whole-flow --pf4e-r1-rated-reconciliation --warmup=30 --ticks=270 --populations=64,96,128,192,256,384,448,512,576,640,704,768 --probes-off --output=_Benchmarks/Baselines/concept-demo-pf4e-r1-rated-replication-scaling-reconciliation-2026-09-13.json
```

Exact historical-policy control command:

```text
dotnet run --project _Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj -c Release --no-build -- --p03-whole-flow --pf4e-r1-rated-reconciliation --warmup=30 --ticks=270 --populations=384,448,512,576,640,704,768 --curves=current-dirty-entity-dense,current-maintained-entity-sparse --rated-frame-cap=1 --probes-off --output=_Benchmarks/Baselines/concept-demo-pf4e-r1-current-cap1-controls-2026-09-13.json
```

SHA-256:

- primary raw JSON: `7bef03ccbb550e3e3f00249a161d9c1d8a8aa3574f468117338fbb2aaef2272d`;
- cap-one control JSON: `ec51a4bc2b75fedbb87686cb6d574e691539de77b0cf4f9257816ab150a2104a`.

The raw primary JSON retains every row's full tick/server/inclusive timing distribution,
authority/schedule/proof/client/pump outer timings, semantic counters, transport bytes/datagrams
and batching, allocations, bounded profile/Network reservations, convergence result, cadence, and
semantic digest.

## Clock equivalence

Historical P0.3J and current `legacy ServerRole` use the same expression:

```text
authority/world mutation
+ Game replication scheduling, codec, and queue admission
+ server-side application proof processing
+ server Network stage delta
```

P0.3J disabled protocol/listener stage probes, making the final stage delta zero. PF4E-R1 does the
same. Both clocks therefore exclude the actual Network pump/socket/listener wall, client Network,
client decode/apply/proof queueing, bootstrap, warmup, final convergence drain, and validation.
The current legacy clock is sufficiently equivalent for direct comparison.

The separately retained current `NetworkAndRemoteReplication` clock is:

```text
Game schedule/codec/queue wall
+ server profile progress
+ measured server half of shared-host Network/socket/listener service
+ server-side application proof processing
```

It excludes authority mutation and all client-role work. `WholeTick` includes authority plus the
complete same-process server/client pump and application work. These three clocks are not relabeled
or added together.

## Historical-policy control

The cap-one current controls closely reproduce the historical curves. This rules out a large clock,
harness, or general implementation regression.

| Profile | Generation | Recipients | Legacy server p95 | Whole tick p95 | Publications/tick | Datagrams/tick |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Dirty Entity dense | P0.3J historical | 384 | 2.263 ms | 8.134 ms | 390.11 | 780.21 |
| Dirty Entity dense | current, cap one | 384 | 2.142 ms | 7.996 ms | 395.49 | 790.97 |
| Dirty Entity dense | P0.3J historical | 640 | 3.969 ms | 14.346 ms | 647.31 | 1,295.52 |
| Dirty Entity dense | current, cap one | 640 | 3.602 ms | 14.122 ms | 652.86 | 1,305.73 |
| Dirty Entity dense | P0.3J historical | 704 | 4.900 ms | 17.939 ms | 711.82 | 1,426.25 |
| Dirty Entity dense | current, cap one | 704 | 4.062 ms | 15.533 ms | 717.12 | 1,436.85 |
| Maintained Entity sparse | P0.3J historical | 384 | 2.080 ms | 7.364 ms | 386.79 | 773.57 |
| Maintained Entity sparse | current, cap one | 384 | 2.164 ms | 7.416 ms | 385.49 | 770.99 |
| Maintained Entity sparse | P0.3J historical | 512 | 3.032 ms | 10.262 ms | 515.57 | 1,031.15 |
| Maintained Entity sparse | current, cap one | 512 | 2.983 ms | 10.111 ms | 513.45 | 1,026.90 |
| Maintained Entity sparse | P0.3J historical | 640 | 4.081 ms | 13.153 ms | 643.08 | 1,286.16 |
| Maintained Entity sparse | current, cap one | 640 | 4.077 ms | 13.075 ms | 640.90 | 1,281.79 |

P0.3J did not run a historical rated `ChunkPublication` curve. No such value is synthesized here.

## Current cap-two curves

| Workload | Recipients | Legacy server p95 | Inclusive Network + replication p95 | Whole tick p95 | Publications/tick | Datagrams/tick |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Dirty Entity dense | 384 | 2.174 ms | 4.044 ms | 7.965 ms | 395.49 | 782.25 |
| Dirty Entity dense | 640 | 3.727 ms | 6.953 ms | 13.875 ms | 652.86 | 1,297.00 |
| Dirty Entity dense | 704 | 3.966 ms | 7.705 ms | 15.532 ms | 717.12 | 1,427.97 |
| Dirty Entity dense | 768 | **4.571 ms** | 8.583 ms | 17.021 ms | 781.32 | 1,556.77 |
| Maintained Entity sparse | 384 | 1.965 ms | 2.376 ms | 7.254 ms | 385.49 | 770.99 |
| Maintained Entity sparse | 512 | 2.826 ms | 3.269 ms | 9.991 ms | 513.45 | 1,026.90 |
| Maintained Entity sparse | 640 | 3.816 ms | 4.136 ms | 12.769 ms | 640.90 | 1,281.79 |
| Maintained Entity sparse | 704 | **4.255 ms** | 4.726 ms | 14.293 ms | 705.99 | 1,411.97 |
| Existing PF4E chunk dense | 64 | 1.746 ms | 2.132 ms | 5.091 ms | 248.69 | 134.06 |
| Existing PF4E chunk dense | 96 | 2.749 ms | 3.337 ms | 7.933 ms | 374.85 | 201.21 |
| Existing PF4E chunk dense | 128 | 3.539 ms | 4.361 ms | 10.515 ms | 501.96 | 268.59 |
| Existing PF4E chunk dense | 192 | **5.237 ms** | 6.374 ms | 15.639 ms | 762.11 | 403.33 |
| Chunk adversarial/unbatchable, one dirty chunk | 64 | 1.744 ms | 2.239 ms | 4.426 ms | 64.00 | 121.00 |
| Chunk adversarial/unbatchable, one dirty chunk | 96 | 2.282 ms | 2.970 ms | 6.013 ms | 96.00 | 182.76 |
| Chunk adversarial/unbatchable, one dirty chunk | 128 | 2.763 ms | 3.634 ms | 7.425 ms | 128.00 | 244.81 |
| Chunk adversarial/unbatchable, one dirty chunk | 192 | 3.530 ms | 4.751 ms | 10.137 ms | 192.00 | 365.59 |
| Chunk adversarial/unbatchable, one dirty chunk | 256 | **5.328 ms** | 7.093 ms | 14.905 ms | 256.00 | 487.70 |
| Chunk production-shaped localized multichunk | 64 | 2.164 ms | 2.653 ms | 7.455 ms | 512.00 | 150.30 |
| Chunk production-shaped localized multichunk | 96 | 3.145 ms | 3.890 ms | 10.945 ms | 768.00 | 220.10 |
| Chunk production-shaped localized multichunk | 128 | **4.126 ms** | 5.057 ms | 14.408 ms | 1,024.00 | 288.62 |

Under the legacy 4 ms rule, the current boundaries are:

- Dirty Entity: last pass 704, first failure 768;
- Maintained Entity: last pass 640, first failure 704;
- existing PF4E Chunk: last pass 128, first failure 192;
- explicit unbatchable Chunk: last pass 192, first failure 256;
- production-shaped multichunk: last pass 96, first failure 128.

Under the separate inclusive clock, the corresponding last-pass/first-failure pairs are Dirty
256/384, Maintained 576/640, existing Chunk 96/128, explicit unbatchable Chunk 128/192, and
production-shaped Chunk 96/128.

## Work-shape and cost decomposition

The earlier PF4E inference that its existing fixture offered no message-129 opportunity was false:
the old profile counters did not populate ChunkPublication's newer bundle facts. At 128 recipients,
the existing fixture actually emitted 35,137 message-129 envelopes containing 133,092 entries.
That is 3.788 entries per bundle and 98.20% of 135,530 semantic publications bundled. Only 2,438
publications remained standalone, producing 37,575 forward Game messages in total. It dirtied five
chunks per tick, reached a maximum of seven due publications in one recipient pass, and produced
34,797 multi-due recipient passes. It is a bundled multichunk workload, not an unbatchable fixture.

The explicit unbatchable fixture dirties all 20 rows of one chunk each tick. At 128 recipients it
delivers exactly one semantic publication and one forward Game message per recipient/tick, with
zero message-129 bundles. Its 2.763 ms p95 and 11.752 mean microseconds/publication, versus Dirty
Entity's 0.730 ms and 4.949 microseconds/publication, isolate a 2.37x mean base cost per delivered
Chunk semantic unit even before multiplying publication count.

The production-shaped fixture dirties 20 entities distributed across eight localized chunks each
tick. At 128 recipients it delivers eight semantic publications/recipient/tick. Message 129 is
active: 36,159 bundles contain 269,485 entries, averaging 7.453 entries/bundle and covering 97.47%
of all publications. This reduces forward Game messages to 1.249/recipient/tick and mean legacy
cost to 3.523 microseconds/publication, but eight useful publications still put p95 at 4.126 ms.

At 128 recipients the existing PF4E Chunk fixture versus current Dirty Entity shows:

- legacy p95: 3.539 / 0.730 = **4.85x**;
- semantic publications/recipient/tick: 3.922 / 1.042 = **3.76x**;
- mean legacy cost/publication: 5.791 / 4.949 microseconds = **1.17x**;
- application bytes/recipient/tick: 1,268.94 / 892.10 = **1.42x**;
- wire bytes/recipient/tick: 1,409.53 / 1,030.20 = **1.37x**;
- datagrams/recipient/tick: 2.098 / 2.049 = **1.024x**.

Decomposing the mean legacy gap of 2.247 ms/tick at that point, 1.824 ms (**81.2%**) is explained
by the additional semantic publication count at Dirty Entity's measured unit cost. The remaining
0.422 ms (**18.8%**) is the Chunk per-publication premium. The concrete additional work includes
recipient/chunk readiness, 64,128 retained subscriptions, 354,896 pending-membership examinations,
112,383 cadence deferrals, 138,161 source-delta reads, delta construction, and exact proof-ledger
retirement for 135,530 proof entries. Message-129 packing reduces those publications to only
37,575 forward Game envelopes and proof packing reduces them to 34,944 reverse envelopes, so
neither Game-message count nor physical datagram count explains the CPU multiplier.

PF4D cap-two Network aggregation had zero exact MTU-compatible pair opportunities and zero actual
aggregates in all three Chunk comparison shapes; each data datagram carried one frame. This is a
measured shape result, not a disabled feature. PF4C message-129 bundling is active in the existing
and production-shaped fixtures and absent in the explicit unbatchable fixture. Therefore the
production-shaped fixture exercises PF4C naturally, but none of these chunk-only flows naturally
exercise PF4D's cross-lane pairing.

No recovery/snapshot delivery, source snapshot fallback, capacity drop, retransmission, or
convergence failure occurred. Measured-region managed allocation remained only 80-13,344 bytes per
row. Reservations are intentionally capacity-proportional: at 128 Chunk recipients the profile
reserves 781,737,152 bytes and Network reserves 95,138,091 bytes; at 192 they are 1,170,081,216 and
142,683,755 bytes. The explicit 256-recipient Chunk row reserves 1,558,064,321 and 190,226,347
bytes. These are bounded benchmark-only compositions, not new production defaults.

## Decision

This is **Outcome B — selected-profile cost difference confirmed**.

The current cap-one Dirty and Maintained controls remain in the P0.3J scaling class, including an
almost exact Maintained 640 comparison (4.077 ms current versus 4.081 ms historical). There is no
evidence of broad methodology drift or an implementation regression. ChunkPublication has a
materially higher per-publication semantic cost, and workload shape can multiply the number of
independent chunk publications delivered per recipient/tick. Bundling substantially amortizes
envelopes but does not erase that semantic work.

`<128` is not a universal ChunkPublication property. The existing PF4E fixture passes 128 on the
legacy clock and fails at 192; the explicit unbatchable fixture passes 192 and fails at 256; the
production-shaped eight-chunk fixture narrowly fails at 128. Recipient count without useful-work
shape is therefore not a defensible capacity statement. The earlier PF4E inclusive result remains
valid for that exact captured row, but its “no bundling opportunity” explanation is superseded.

Selective compression remains the highest-value next **bandwidth** experiment because PF4E's real
product runs found message 129 to be about 94.1% of active Game application egress. PF4E-R1 also
makes clear that compression is not a remedy for selected-profile CPU scaling: a later CPU-focused
campaign must separately evaluate publication granularity/readiness/delta/proof work. No such
optimization begins here.

Focused verification:

```text
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj -c Release -- --pf4e-r1-only
```
