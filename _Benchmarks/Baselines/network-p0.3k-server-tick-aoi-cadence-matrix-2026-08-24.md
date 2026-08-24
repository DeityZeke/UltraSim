# P0.3K server-step / AOI-cadence matrix

Date: 2026-08-24

Implementation checkpoints: `90e8e387` (independent controls) and `1382e592`
(dense recipient mapping beyond the 1,000-entity fixture).

## Question

Measure the selected dynamic/sparse whole-flow profiles under four independent
server-step and AOI policies:

```text
16 ms server step + AOI 0/1/2 skipped ticks
16 ms server step + AOI 1/1/2 skipped ticks
33 ms server step + AOI 0/1/2 skipped ticks
33 ms server step + AOI 1/1/2 skipped ticks
```

The tuple is recorded as skipped authority ticks. Its concrete periods are therefore
`1/2/3` and `2/2/3` ticks. Entity policies use the equivalent millisecond intervals;
chunk policies use the explicit tick periods.

There is no single pre-existing global default in the repository. The sparse profile
already used chunk periods `1/2/3`; the dense chunk evidence preset used `1/2/4`, and
`GameInterestProfile.Reference` used 33/50 ms Near/Far intervals. This matrix
deliberately normalizes both profile families to the requested `0/1/2` control so the
four rows are comparable. It does not silently change the selected deployment policy.

## Method

- Dense: `DirtyEntitySpatialFanout`, 1,000 public entities, 2% spatial dirt per tick,
  start 128, step 64.
- Sparse: `MaintainedEntityRelation`, one public moving entity per recipient, 35%
  spatial dirt per tick, start 512, step 128.
- 30 warmup and 270 measured ticks per completed population.
- Cap-one authenticated whole flow: Game codec, retained Network queue, lane scheduler,
  AES-GCM, real shared localhost UDP listener, client decode/application, reverse Game
  proof, and server proof interpretation.
- Protocol stage probes disabled; ordinary counters and semantic validation remain on.
- Each series retains the first completed population whose integrated whole-flow tick
  p95 exceeds the configured server-step deadline, then stops. Maximum population is
  4,096.

The benchmark does not sleep for 16 or 33 ms. Idle wait is not CPU work. The server
step controls semantic time, the deadline, and the per-second rate denominator; the
same authority/Network work is executed once per tick. Network receives four bounded
pump/dispatch opportunities per authority tick in every row.

`Tick` includes server plus in-process synthetic-client work and is therefore a
conservative integrated-fixture ceiling, not a dedicated-server CCU claim. The
server-role p95 is reported separately. A production server still needs budget for
the rest of ECS, persistence, scripting, and OS jitter.

## Scale result

| Server step | AOI skipped ticks | Dense last pass | Dense first cross | Sparse last pass | Sparse first cross |
| --- | --- | ---: | ---: | ---: | ---: |
| 16 ms | 0/1/2 | 704 @ 15.244 ms | 768 @ 16.728 ms | 640 @ 12.911 ms | 768 @ 16.067 ms |
| 16 ms | 1/1/2 | 704 @ 15.251 ms | 768 @ 17.332 ms | 768 @ 15.816 ms | 896 @ 18.853 ms |
| 33 ms | 0/1/2 | 1,344 @ 32.907 ms | 1,408 @ 35.169 ms | 1,280 @ 30.405 ms | 1,408 @ 34.362 ms |
| 33 ms | 1/1/2 | 1,280 @ 31.215 ms | 1,344 @ 33.106 ms | 1,280 @ 30.156 ms | 1,408 @ 33.960 ms |

These are threshold brackets, not exact capacity constants. An earlier same-day 16 ms
repeat placed both profiles' 768 row just below the deadline. The exact-source rerun
placed two 768 rows within 0.07--0.73 ms above it. Likewise, the 33 ms Dense 1,344 row
is 0.093 ms below the deadline at `0/1/2` and 0.106 ms above it at `1/1/2`.
The defensible conclusion is a one-step uncertainty around the cliff, not that cadence
slowing deterministically gains or loses 64/128 clients.

At the retained passing rows:

| Configuration | Workload | Tick p95/p99/max | Server-role p95 | Client-role p95 | Wire MiB/s | Combined fixture MiB |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| 16 + 0/1/2 | Dense-704 | 15.244 / 17.872 / 18.629 | 4.070 | 1.493 | 43.06 | 864.36 |
| 16 + 0/1/2 | Sparse-640 | 12.911 / 13.250 / 13.390 | 4.117 | 0.541 | 21.93 | 549.79 |
| 16 + 1/1/2 | Dense-704 | 15.251 / 18.250 / 18.615 | 4.008 | 1.483 | 43.05 | 864.36 |
| 16 + 1/1/2 | Sparse-768 | 15.816 / 16.411 / 16.691 | 5.005 | 0.733 | 26.69 | 676.20 |
| 33 + 0/1/2 | Dense-1,344 | 32.907 / 36.875 / 37.145 | 8.936 | 3.237 | 39.86 | 1,650.30 |
| 33 + 0/1/2 | Sparse-1,280 | 30.405 / 32.215 / 33.682 | 10.310 | 1.709 | 22.35 | 1,233.09 |
| 33 + 1/1/2 | Dense-1,280 | 31.215 / 34.688 / 35.345 | 8.466 | 3.153 | 37.97 | 1,567.78 |
| 33 + 1/1/2 | Sparse-1,280 | 30.156 / 31.720 / 33.425 | 10.111 | 1.654 | 22.28 | 1,233.09 |

Combined reservation includes server profile + server Network + every in-process client
profile + every in-process client Network allocation. It is cadence-independent at a
matched population. It must not be quoted as dedicated-server memory.

## Matched-population causal comparison

At 768 recipients, work per tick is nearly invariant while rate per simulated second
changes with the server step:

| Server / AOI | Profile | Tick p95 | Server p95 | Wire MiB/tick | Wire MiB/s | Game messages | Proof messages | Combined MiB |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 16 / 0/1/2 | DirtyEntity | 16.728 | 4.520 | 0.7515 | 46.97 | 209,502 | 209,502 | 939.24 |
| 16 / 1/1/2 | DirtyEntity | 17.332 | 4.584 | 0.7514 | 46.96 | 209,379 | 209,379 | 939.24 |
| 33 / 0/1/2 | DirtyEntity | 16.683 | 4.568 | 0.7515 | 22.77 | 209,502 | 209,502 | 939.24 |
| 33 / 1/1/2 | DirtyEntity | 16.925 | 4.604 | 0.7514 | 22.77 | 209,379 | 209,379 | 939.24 |
| 16 / 0/1/2 | Maintained | 16.067 | 5.234 | 0.4282 | 26.76 | 208,640 | 208,640 | 676.20 |
| 16 / 1/1/2 | Maintained | 15.816 | 5.005 | 0.4271 | 26.69 | 208,096 | 208,096 | 676.20 |
| 33 / 0/1/2 | Maintained | 15.998 | 5.026 | 0.4265 | 12.93 | 208,613 | 208,613 | 676.20 |
| 33 / 1/1/2 | Maintained | 15.662 | 4.956 | 0.4253 | 12.89 | 207,833 | 207,833 | 676.20 |

Changing 16 to 33 ms reduces bidirectional wire rate by about 51.5% at the same
population because the workload runs 30.3 rather than 62.5 ticks/s. It does not reduce
wire bytes/tick or replication CPU/tick. Changing only `0/1/2` to `1/1/2` reduces
traffic by less than 0.4% in these workloads. Immediate-tier debt is too small a share
of the total to move the work shape materially.

## Freshness and ping

The configured cadence opportunity lags are:

| Server step | AOI | Immediate / Near / Far opportunity lag |
| --- | --- | --- |
| 16 ms | 0/1/2 | 0 / 16 / 32 ms |
| 16 ms | 1/1/2 | 16 / 16 / 32 ms |
| 33 ms | 0/1/2 | 0 / 33 / 66 ms |
| 33 ms | 1/1/2 | 33 / 33 / 66 ms |

These are semantic cadence opportunities, not end-to-end WAN latency. This unpaced
localhost harness does not sample ping/RTT, and its intra-process socket round trip
would not be a useful player-latency result. Real network RTT, OS scheduling, loss,
and client frame time add on top. The main cost of a 33 ms server step is therefore
freshness: even with unchanged AOI tick counts, Near/Far opportunity spacing doubles
in wall-clock milliseconds.

## Correctness and regression evidence

- All matched cadence rows have identical final semantic digests.
- Every row converged; capacity drops and retransmissions were zero.
- Forward Game-message and reverse proof counts match exactly in every row.
- Measured global allocation was small but nonzero (roughly 0.6--14.5 KiB per 270-tick
  row); this artifact does not claim literal zero global allocation.
- The new Immediate interval defaults to zero, preserving historical behavior.
- Permanent maintained-relation and dirty-entity tests prove a positive Immediate
  interval defers debt to the configured cadence.
- All 128 Game Replication tests, all 69 Network Application tests, and the full
  Network/kernel/hosted suite pass.
- The 33 ms scale run exposed and fixed a benchmark-only recipient indexing defect
  above 1,000 recipients. Dense recipient anchors now deterministically wrap the
  1,000-entity fixture instead of indexing beyond it.

## Evidence files

- `network-p0.3k-server16-aoi012-scale-2026-08-24.json`
  - SHA-256 `3b0510af39c399f29700755cb8d8c4d3247d5bb724293b7d078a7586e2b009b9`
- `network-p0.3k-server16-aoi112-scale-2026-08-24.json`
  - SHA-256 `b142721a226cec41c3852478ca7bde5eb080d06cf9a5bf2300f889825729efdd`
- `network-p0.3k-server33-aoi012-scale-2026-08-24.json`
  - SHA-256 `5fc851322231c430fe44ed2977a47c2512b9d302529abe6f48e99aed63dfb836`
- `network-p0.3k-server33-aoi112-scale-2026-08-24.json`
  - SHA-256 `94fd6cb5f6c1f1e2a7a40e065bc8950e3879d56a70ff81c3f36cdd39a4c47cd2`

## Verdict

The 33 ms server step buys approximately 1.7--1.9x the integrated fixture population
at the deadline, but by spending roughly twice the wall-clock latency budget and
halving the update rate. It is not a per-tick efficiency improvement.

The optional `1/1/2` AOI cadence is semantically sound but does not materially improve
CPU, bandwidth, or memory for these workloads. It should be selected only if making
Immediate as stale as Near is acceptable policy; it should not be justified as a CCU
optimization from this evidence.
