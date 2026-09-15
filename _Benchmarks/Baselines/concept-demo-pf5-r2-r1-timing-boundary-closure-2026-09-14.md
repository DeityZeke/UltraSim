# Concept Demo PF5-R2-R1 — Timing Boundary and Closure Evidence

**Outcome: D — timing accounting is closed, but PF6 remains paused by a pre-existing production
correctness defect exposed by canonical verification.**

PF5-R2 remains correct and measurably faster when source preparation and the complete existing
remote transport/application/proof pump are charged to one direct interval for the same tick. The
literal whole interval shows a smaller gain than the earlier post-authority server-owned metric, so
the old “complete-path” wording is corrected. No production project or behavior changed. The
production correctness candidate identity remains `992fc541460f18534da9036c73cb711277de11ce`,
but it cannot be cleared for PF6 while the independently reproduced HTTP-host shutdown defect is
unresolved.

## Lineage and method

| Item | Value |
| --- | --- |
| Starting / measurement base | `76a2659fcad02a7ca8bcbd16c6f3a7aca497d893` |
| Initial phase-sum timing implementation (superseded) | `41302ae3c3caa33a1c9d23744c4ea6a3ff8e9000` |
| Direct-interval timing implementation | `74d889bf768012d5ff69621ac4578f92c90710a3` |
| Production correctness candidate | `992fc541460f18534da9036c73cb711277de11ce` |
| Runtime / host | .NET runtime 8.0.29, Unix 7.1.5.1, 16 logical processors |
| Policy | ChunkPublication; message 129 when beneficial; selective message-148 LZ4 at 384/32; cadence 1/2/4; CrossLane cap 2; Game manifest v29; transport revision 2; 60 Hz; product cap 64 |
| Measurement | alternating fresh old/new harnesses; both paths primed; 30 warmup + 270 measured ticks; three samples through 128, one above |

The immutable machine-readable evidence is
[`concept-demo-pf5-r2-r1-timing-boundary-closure-2026-09-14.json`](concept-demo-pf5-r2-r1-timing-boundary-closure-2026-09-14.json)
(SHA-256 `3cbaa471965def4b30844fa3ea6533a71b3b57a5a916bfb09ebc144ec0b1e18f`).
The historical PF5-R2 artifacts are intentionally preserved with their original boundary.

## Exact timing boundaries

`AuthorityMutation` starts immediately before the benchmark's authoritative workload mutation and
ends after `ApplyAuthority`. `PreparedChunkPublication` creation occurs inside `ApplyAuthority`,
through the dirty-chunk commit into prepared-source history, so this phase includes it.

`NetworkAndRemoteReplication` begins **after authoritative mutation/source publication
preparation**. It is the pre-existing accumulated server-owned timing for remote scheduling, server
Network work, and server proof application. It deliberately excludes client-side Network,
decode/application, and proof-queue work. It is useful for the 4.000 ms server subsystem budget,
but is not the literal authority-through-final-pump interval.

`AuthorityThroughNetworkAndRemoteReplication` is one direct wall interval per tick. It starts
immediately before authoritative mutation and stops after the final existing remote
transport/application/proof pump. Consequently it includes the client-side transport and semantic
work interleaved between the measured server-owned phases. It excludes cold construction,
handshake/bootstrap, warmup, convergence drains, and unrelated simulation stages. Its p95 is
calculated from direct per-tick elapsed samples; it is not reconstructed from phase durations and
never adds phase percentiles.

The first implementation at `41302ae3...` summed same-tick raw authority and Network phase ticks.
Although that avoided percentile addition, it did not satisfy the required single start-to-stop
boundary. It was rejected before closure and replaced by the direct interval at `c06fec33...`,
whose endpoint ordering was fenced at the measured `74d889bf...` revision.

The structural timing regression records:

```text
combined start
  < authority start
  < prepared source publication observed (8 publications)
  < authority end
  < Network+remote-replication start
  < Network+remote-replication end
  < combined end
```

The evidence explicitly records `CombinedFromSingleElapsedInterval=true` and
`CombinedFromPhaseDurations=false`.

## Old/new timing distributions

Values are `mean / p50 / p95 / p99 / max` milliseconds. Through 128, each value is the median of
three retained sample statistics.

| Recipients | Model | AuthorityMutation | NetworkAndRemoteReplication | AuthorityThroughNetworkAndRemoteReplication |
| ---: | --- | --- | --- | --- |
| 64 | old history | 0.0191 / 0.0178 / 0.0305 / 0.0386 / 0.0513 | 1.3999 / 1.3913 / 1.7009 / 1.7854 / 1.8430 | 5.7100 / 5.6502 / 6.5974 / 6.8602 / 7.0218 |
| 64 | prepared | 0.0236 / 0.0223 / 0.0326 / 0.0431 / 0.0487 | 1.1976 / 1.1781 / 1.4009 / 1.4437 / 1.4626 | 5.5172 / 5.4405 / 6.4145 / 6.5915 / 6.7617 |
| 96 | old history | 0.0231 / 0.0210 / 0.0349 / 0.0547 / 0.0581 | 2.0703 / 2.0458 / 2.4405 / 2.5645 / 2.8125 | 8.3987 / 8.3251 / 9.6346 / 10.1448 / 10.5226 |
| 96 | prepared | 0.0209 / 0.0184 / 0.0347 / 0.0632 / 0.0765 | 1.7585 / 1.7337 / 2.0259 / 2.1084 / 2.1974 | 8.0322 / 7.9462 / 9.0951 / 9.3118 / 9.5982 |
| 128 | old history | 0.0311 / 0.0268 / 0.0554 / 0.0712 / 0.0761 | 2.6726 / 2.6474 / 3.0518 / 3.2202 / 3.3878 | 11.0658 / 11.0081 / 12.3021 / 12.8330 / 13.1332 |
| 128 | prepared | 0.0262 / 0.0247 / 0.0387 / 0.0737 / 0.0800 | 2.3446 / 2.3200 / 2.6345 / 2.7360 / 2.8064 | 10.8002 / 10.7318 / 11.9609 / 12.2737 / 12.6282 |

Old-to-new improvement is deliberately reported independently for each boundary:

| Recipients | Authority mean / p95 | Network+rep mean / p95 | Direct combined mean / p95 | Schedule p95 |
| ---: | ---: | ---: | ---: | ---: |
| 64 | -23.44% / -6.95% | 14.45% / 17.64% | 3.38% / 2.77% | 24.41% |
| 96 | 9.48% / 0.49% | 15.06% / 16.99% | 4.36% / 5.60% | 24.00% |
| 128 | 15.82% / 30.20% | 12.27% / 13.67% | **2.40% / 2.77%** | **20.39%** |

Negative 64 authority percentages mean that its tiny authority-only phase was slower in the
prepared sample. The complete direct interval nevertheless improved in all retained populations.
At 128 the direct p95 reduction is 0.341 ms and the mean reduction is 0.266 ms. This is a real but
more modest whole-interval gain than the earlier narrow-clock interpretation.

## Dense synthetic crossing

| Recipients | Model | Authority p95 | Network+rep p95 | Direct combined p95 | Schedule p95 |
| ---: | --- | ---: | ---: | ---: | ---: |
| 128 | old | 0.0554 | 3.0518 | 12.3021 | 2.1469 |
| 128 | prepared | 0.0387 | 2.6345 | 11.9609 | 1.7091 |
| 160 | old | 0.0501 | 3.9045 | 15.5549 | 2.7138 |
| 160 | prepared | 0.0474 | 3.2143 | 14.7925 | 2.0901 |
| 192 | old | 0.0950 | 4.7155 | 19.1599 | 3.3060 |
| 192 | prepared | 0.0838 | 3.9608 | 18.2158 | 2.5425 |
| 256 | old | 0.1221 | 6.3884 | 25.5476 | 4.4691 |
| 256 | prepared | 0.1194 | 5.2720 | 24.3977 | 3.3552 |

On this fresh run the 4.000 ms `NetworkAndRemoteReplication` budget has the old crossing between
160 and 192 and the prepared crossing between 192 and 256. The direct combined clock includes all in-process
client work and is already above 4 ms at the first tested point, 64. Therefore its 4 ms crossing is
below 64 and the 4 ms server-owned subsystem budget must not be applied to that broader clock.
These are synthetic architecture points, not product CCU and not a participant-cap change.

## Semantic and instrumentation guardrails

Every matched old/new row retained equal semantic digest, semantic publication/delivery count,
proof count, Game-message count, and datagram count. The 128 pair retained digest
`03AAAD54B49C08E1`, 276,480 deliveries/proofs, 43,154 Game messages, and 69,546 datagrams. Fresh
harness publication epochs changed compressed entropy slightly: median 128 wire payload was
40,856,415 old versus 40,857,926 prepared bytes (+0.0037%). This is not a wire/freshness policy
change; same-lineage canonical equality remains covered by PF5-R2's deterministic regression.

The enabled timing row's narrow p95 was 1.411120 ms. Disabled bracketing rows were 1.397750 and
1.435879 ms (mean 1.416815), making the observed delta -0.402%. That lies within the 2.7% spread
between the two disabled controls and is not a measurable timer tax. The direct combined interval
reuses the existing whole-tick start and endpoint timestamp, adding no timed `Stopwatch` read and
only one bounded retained-list append after the narrow sample is captured.

## Disposition

PF5-R2 demonstrates a repeatable, modest complete-interval improvement and a substantial
server-owned scheduling/Network improvement without changing semantic work. The earlier 17.46%
figure remains valid only for its historical post-authority boundary; it is not the complete-path
gain. Production candidate identity `992fc541460f18534da9036c73cb711277de11ce` remains unchanged;
its PF6 clearance does not.

The focused PF5-R2-R1 gate passed, as did all 164 focused PF5-R2 replication tests. The canonical
Release/export verifier then failed in the unrelated maintained Server Hosting S0.2 suite:

```text
ContentResponseQueueRejectsOverflowAndShutdownTerminatesActiveWrite
StaticContentHttpHost.Dispose()
  -> HandleAsync(...)
  -> HttpListenerResponse.ContentLength64
  -> InvalidOperationException: response has already been submitted
```

The exact hosting suite passed twice and failed once across three immediate reruns; including the
canonical run, the race reproduced twice in four executions. `Server/Hosting/StaticContentHttpHost.cs`
and its test are unchanged between starting HEAD `76a2659f...` and timing HEAD `74d889bf...`.
The failure predates and is independent from PF5-R2-R1. Existing catches treat I/O, listener, and
disposed failures as bounded response retirement but do not contain this submitted-response
`InvalidOperationException`, so `Dispose` can propagate it while awaiting the worker.

This checkpoint forbids an unrelated production repair. Consequently the timing conclusion and
candidate identity are preserved, but the closure disposition is **Outcome D** and **PF6 remains
paused** until that hosting shutdown race is repaired and full canonical Release verification
passes.

The historical fanout/scaling-attribution matrix remains a useful nonblocking performance
investigation. It is not the PF6 blocker.
