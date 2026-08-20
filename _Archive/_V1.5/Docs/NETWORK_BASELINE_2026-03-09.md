# Network Baseline - 2026-03-09

This document records the current measured server and client-network baselines after the latest server-side hotspot work and the first durable client-core optimization pass.

Review-debt execution order and remaining structural items are tracked separately in `Docs/NETWORK_OPTIMIZATION_BACKLOG_2026-02-26.md` section 11.

## Current Client Runtime Note

Current `Client.Godot` network mode status:

- host/session/driver scaffold is working end-to-end
- local prediction, remote interpolation, chat, ping, and reconnect ownership are all wired
- the active movement path is now float runtime state with quantized centimeter `X/Z` plus explicit yaw on the wire
- movement feel is materially better than the original direct `EntityInt2` path

However:

- remote/external validation on the new Float path still needs more soak time before calling it finished
- `Y` remains deferred on the wire until gravity/elevation work becomes active
- chunk-coordinate + local-offset addressing remains deferred until terrain/chunk streaming work begins

The main networking risk is no longer the old `EntityInt2` movement path; it is now ordinary validation/tuning work on the new Float path.

## Stable Server Config

Current runtime defaults in `_tmp/ECSNetworkIslandSliceNode.cs`:

- `EnableServerDenseLocalChatRecipientCap = true`
- `ServerDenseLocalChatCapStartRecipients = 24`
- `ServerDenseLocalChatCapMaxRecipients = 8`
- `EnableServerDenseLocalChatRateCap = true`
- `ServerDenseLocalChatRateCapStartRecipients = 24`
- `ServerDenseLocalChatRateCapMinTicks = 60`
- `EnableServerCorrectionBatching = true`
- `EnableServerCorrectionDeadband = true`
- `ServerCorrectionDeadbandUnits = 0`
- `ServerCorrectionMaxSilentTicks = 1`

Rejected defaults that should remain off unless explicitly retested:

- dense observer recipient cap
- dual correction streams default-on
- observer payload compaction on the legacy single-stream path
- extended despawn grace
- local-chat chunk-radius targeting
- coarse/global observer cadence paths that regressed p95

## Server Runtime Baselines

Important: the pinned mixed/hotspot knees in this document are UDP shared-socket harness measurements. Plain TCP quick checks are not comparable and will read materially worse.

Important: there are now two harness baselines:

- single-process Python harness
- sharded Python harness

The sharded harness is the correct upper-range instrument. The single-process harness became the limiter before the server/runtime path did.

### Hotspot

Current strict/stable read:

- `1350` strict pass
- `1375` first fail

Pinned numbers:

- `1325`
  - `p95 = 35.99 ms`
  - `perf_total_avg = 6.96 ms`
  - `perf_flush_avg = 3.42 ms`
  - report: `_tmp/reports/island_socket_ramp_async_20260312_191458.json`

- `1350`
  - `p95 = 37.59 ms`
  - `perf_total_avg = 7.07 ms`
  - `perf_flush_avg = 3.41 ms`
  - `udp_us_mb = 183.9`
  - `SRVREPL drop=(2151,0,0)`
  - report: `_tmp/reports/island_socket_ramp_async_20260312_191458.json`

- `1375`
  - `p95 = 41.33 ms`
  - `perf_total_avg = 7.16 ms`
  - `perf_flush_avg = 3.41 ms`
  - `udp_us_mb = 190.9`
  - `SRVREPL drop=(4358,0,0)`
  - report: `_tmp/reports/island_socket_ramp_async_20260312_191458.json`

Recent UDP shared-socket progression:

- `900` through `1300`
  - all pass under the strict `40 ms` p95 gate
  - reports:
    - `_tmp/reports/island_socket_ramp_async_20260312_185254.json`
    - `_tmp/reports/island_socket_ramp_async_20260312_190446.json`
    - `_tmp/reports/island_socket_ramp_async_20260312_190949.json`

### Hotspot (Sharded Harness)

Current strict/stable read:

- `2850` strict pass
- `2875` first fail

Pinned numbers:

- `2800`
  - `p95 = 39.29 ms`
  - report: `_tmp/reports/sharded_20260312_215448/aggregate.json`

- `2850`
  - `p95 = 39.90 ms`
  - report: `_tmp/reports/sharded_20260312_215713/aggregate.json`

- `2875`
  - `p95 = 45.87 ms`
  - report: `_tmp/reports/sharded_20260312_220019/aggregate.json`

- `2900`
  - `p95 = 42.92-55.94 ms`
  - reports:
    - `_tmp/reports/sharded_20260312_215713/aggregate.json`
    - `_tmp/reports/sharded_20260312_220019/aggregate.json`

### Mixed

Current strict/stable read:

- `1400` strict pass
- `1450` first fail / borderline edge

Pinned numbers:

- `1400`
  - `p95 = 37.96 ms`
  - report: `_tmp/reports/island_socket_ramp_async_20260312_184619.json`

- `1450`
  - `p95 = 40.15 ms`
  - borderline fail on the strict gate
  - report: `_tmp/reports/island_socket_ramp_async_20260312_184730.json`

Recent UDP shared-socket progression:

- `1325`
  - `p95 = 36.87 ms`
  - report: `_tmp/reports/island_socket_ramp_async_20260312_184255.json`
- `1350`
  - `p95 = 36.69 ms`
  - report: `_tmp/reports/island_socket_ramp_async_20260312_184401.json`
- `1375`
  - `p95 = 35.43 ms`
  - report: `_tmp/reports/island_socket_ramp_async_20260312_184507.json`
- `1400`
  - `p95 = 37.96 ms`
  - report: `_tmp/reports/island_socket_ramp_async_20260312_184619.json`

### Mixed (Sharded Harness)

Current strict/stable read:

- `2450` strict pass
- `2500` first fail

Pinned numbers:

- `2400`
  - `p95 = 39.18 ms`
  - report: `_tmp/reports/sharded_20260312_213412/aggregate.json`

- `2450`
  - `p95 = 38.87 ms`
  - report: `_tmp/reports/sharded_20260312_213634/aggregate.json`

- `2500`
  - `p95 = 41.85 ms`
  - report: `_tmp/reports/sharded_20260312_213634/aggregate.json`

- `2550`
  - `p95 = 44.69 ms`
  - report: `_tmp/reports/sharded_20260312_213634/aggregate.json`

### Interpretation

- Hotspot and mixed are now much closer together than the earlier baselines implied.
- Hotspot is no longer materially behind mixed under the strict `40 ms` gate:
  - hotspot `1350` pass
  - mixed `1400` pass
- Under the sharded harness, hotspot slightly exceeds mixed:
  - hotspot `2850` pass
  - mixed `2450` pass
- This is not currently treated as a correctness issue.
  - It reflects the current synthetic profile shapes plus the dense-case server optimizations.
  - The current hotspot profile is no longer automatically the harder profile.
- Mixed is still not obviously server-limited:
  - `perf_total_avg ~ 7.0-7.3 ms`
  - `perf_flush_avg ~ 3.4-3.5 ms`
  - `SRVREPL drop` remains small even at the stable edge
- The recent mixed ceiling increase came from harness architecture fixes, not server transport changes:
  - UDP ping/pong after association
  - no catch-up replay loops
  - shared due-work scheduling in `Harness.wait_period(...)`
  - simplified per-bot inbound loops
  - raw nonblocking TCP sockets in the Python harness
  - sharded multi-process harness execution
- Current TCP transport instrumentation on the stable synchronous path shows:
  - `tcp_write_avg_us ~ 8.4-8.7`
  - `tcp_batch_write_avg_us ~ 8.3-8.5`
  - `tcp_write_max_us ~ 776-836`
  - this is useful attribution, but still not evidence that a new async TCP rewrite is the next highest-leverage change

## Server Telemetry Baselines

These runtime telemetry lines are now considered stable and useful:

- `SRVRO`
- `SRVCORR`
- `SRVREPL`

Current hotspot interpretation after the dense local-chat fix:

- reliable chat is no longer the dominant wall
- despawns are secondary
- defer/coalesce is not the current limiter
- remaining wall is critical replication pressure

## Durable Client-Core Baselines

These client-core changes are now in place and considered durable:

- `UltraSim/Network/Client/ClientReconciliationQueue.cs`
  - thread-safe drain no longer holds the queue lock while applying
- `UltraSim/Network/Client/ClientPredictionStore.cs`
  - ring-buffer-backed store
- `UltraSim/Network/Client/ClientInterpolationBuffer.cs`
  - circular-buffer-backed storage
- `UltraSim/Network/Client/ClientInputSequencer.cs`
  - ring-buffer-backed pending input queue
- `UltraSim/Network/Client/ClientCorrectionStats.cs`
  - queue-depth and queued-correction tracking
- `UltraSim/Client/Engines/Networking/ClientNetworkBridge.cs`
  - `SnapshotStats()`
  - last client tick stage timings:
    - poll
    - queue
    - apply
    - world
    - flush
- `UltraSim/Client/Engines/Networking/ClientInterpolationBridge.cs`
  - `SnapshotStats()`

## Embedded C# Client Runtime Baselines

Runtime helpers:

- `_tmp/run_embedded_client_smoke.py`
- `_tmp/run_embedded_client_cluster.py`

These exercise the real exported C# client path, not the external Python bot harness.

### Single Embedded Client Smoke

Latest smoke result:

- `tick_poll_avg_us = 10.51`
- `tick_queue_avg_us = 3.07`
- `tick_apply_avg_us = 30.92`
- `inbound_poll_avg_us = 0.96`
- `inbound_decode_avg_us = 171.97`
- `qdepth_queue = 0`
- `coalesced = 0`

Interpretation:

- trivial 1-client runtime is healthy
- no queue pressure
- cache behavior is correct

### Embedded Client Cluster

Headless embedded client cluster, bot-driven movement, current measured shape after:

- pressure-gated correction coalescing (`threshold = 32`)
- duplicate-correction skip-on-apply
- debug-only client bind/despawn logging

#### 16 clients

- `qdepth_queue_max = 7`
- `coal_total_sum = 605`
- `tick_poll_avg_us_p95 = 27.08`
- `tick_queue_avg_us_p95 = 22.04`
- `tick_apply_avg_us_p95 = 68.23`
- `tick_apply_handler_avg_us_p95 = 63.31`
- `tick_apply_lookup_bind_avg_us_p95 = 9.65`
- `inbound_decode_avg_us_p95 = 6.17`

#### 32 clients

- `qdepth_queue_max = 24`
- `coal_total_sum = 2848`
- `tick_poll_avg_us_p95 = 22.31`
- `tick_queue_avg_us_p95 = 16.71`
- `tick_apply_avg_us_p95 = 56.95`
- `tick_apply_handler_avg_us_p95 = 52.77`
- `tick_apply_lookup_bind_avg_us_p95 = 5.45`
- `inbound_decode_avg_us_p95 = 4.46`

Interpretation:

- pressure-gated coalescing engages in the real embedded client path as client count rises
- reconciliation queue depth remains bounded at `16` and `32` embedded clients
- inbound poll/resolve/consume/decode are no longer significant client-side costs in this runtime path
- the remaining meaningful client stage is still `tick_apply_avg_us`, specifically the apply handler
- caching the last applied entity in `_clientAppliedCorrectionByNetworkId` materially reduced apply cost
- inside the apply handler, lookup/bind is still the only measured sub-slice with meaningful cost, but it is now much smaller
- client bind/despawn logging was a real hidden cost and should remain debug-only
- `UltraSim/Network/Client/ClientReconciliationQueue.cs`
  - merge-key replacement now works across:
    - the current write buffer
    - undrained backlog entries already swapped into the drain buffer
- `UltraSim/Client/Engines/Networking/ClientNetworkBridge.cs`
  - correction coalescing can be pressure-gated with `coalesceQueueDepthThreshold`
  - current embedded-runtime default is `32`

## C# Loopback Client Baselines

Source:

- `tests/ECS.Tests/NetworkClientLoopbackSoakTests.cs`

Latest diagnostic lines from `dotnet test ... --filter "FullyQualifiedName~NetworkClientLoopbackSoakTests"`:

### Single-entity baselines (post interpolation trim cap)

- `local_baseline`
  - `sent=512`
  - `qmax=1`
  - `qtick=1`
  - `qtotal=512`
  - `apply=512`
  - `interp_tracked=1`
  - `interp_samples=8`
  - `interp_samples_max=9`
  - `interp_trim_total=504`
  - `pending=0`
  - `predicted=0`
  - `tick_avg_ms=0.00`
  - `alloc_b=695008`
  - `heap_b=696400`

- `profile_Lan`
  - `sent=513`
  - `qmax=1`
  - `qtick=1`
  - `qtotal=513`
  - `apply=513`
  - `interp_samples_max=9`
  - `interp_trim_total=505`
  - `pending=0`
  - `predicted=0`
  - `tick_avg_ms=15.96`
  - `alloc_b=705056`

- `profile_Wan`
  - `sent=476`
  - `qmax=3`
  - `qtick=3`
  - `qtotal=474`
  - `apply=474`
  - `interp_samples_max=9`
  - `interp_trim_total=3`
  - `pending=0`
  - `predicted=0`
  - `tick_avg_ms=15.93`
  - `alloc_b=714200`

- `profile_Harsh`
  - `sent=358`
  - `qmax=3`
  - `qtick=3`
  - `qtotal=351`
  - `apply=351`
  - `interp_samples_max=6`
  - `interp_trim_total=0`
  - `pending=0`
  - `predicted=0`
  - `tick_avg_ms=15.94`
  - `alloc_b=1011520`

### Lower input cadence baselines

- `inputEvery_Local_2`
  - `qmax=1`
  - `interp_samples_max=6`
  - `interp_trim_total=0`
  - `pending=0`
  - `predicted=0`

- `inputEvery_Wan_2`
  - `qmax=2`
  - `interp_samples_max=6`
  - `interp_trim_total=0`
  - `pending=0`
  - `predicted=0`

### Multi-entity baselines (`16` replicated entities)

- `multiEntity_Local_16`
  - `sent=4096`
  - `qmax=16`
  - `qtick=16`
  - `qtotal=4096`
  - `interp_tracked=16`
  - `interp_samples_max=129`
  - `interp_trim_total=3968`
  - `pending=0`
  - `predicted=0`
  - `tick_avg_ms=0.01`
  - `alloc_b=728664`

- `multiEntity_Wan_16`
  - `sent=4075`
  - `qmax=21`
  - `qtick=21`
  - `qtotal=4068`
  - `interp_tracked=16`
  - `interp_samples_max=129`
  - `interp_trim_total=176`
  - `pending=0`
  - `predicted=0`
  - `tick_avg_ms=15.98`
  - `alloc_b=735208`

### Higher entity pressure baselines (`64` replicated entities)

- `highEntity_Local_64`
  - `sent=8192`
  - `qmax=64`
  - `qtick=64`
  - `qtotal=8192`
  - `interp_tracked=64`
  - `interp_samples_max=513`
  - `interp_trim_total=7680`
  - `pending=0`
  - `predicted=0`
  - `tick_avg_ms=0.10`
  - `alloc_b=964896`

- `highEntity_Wan_64`
  - `sent=8179`
  - `qmax=118`
  - `qtick=118`
  - `qtotal=8166`
  - `interp_tracked=64`
  - `interp_samples_max=513`
  - `interp_trim_total=304`
  - `pending=0`
  - `predicted=0`
  - `tick_avg_ms=15.84`
  - `alloc_b=1021240`

## Current Interpretation

- Server-side networking is in a strong state.
- Hotspot remains the harder path, but the current configuration materially reduced:
  - hotspot UDP bytes
  - hotspot critical drops
  - hotspot p95
- Client-core structures are improved and now measurable through the C# loopback soak path.
- In the current loopback soak:
  - queue pressure remains low for single-entity paths
  - interpolation pressure now remains bounded by the per-entity trim cap
  - interpolation apply behavior is now measurable, not inferred:
    - attempts vs misses
    - stale-buffer removals
    - average / max apply cost
  - stale-buffer removals remain `0` in the current soak profiles
  - interpolation apply remains cheap even at `64` replicated entities
  - inbound pipeline decode is not the dominant client-side hot path in the current soak:
    - representative `64`-entity loopback diagnostics show `inbound_decode_avg_us` around `~1 us/datagram`
    - client bridge `tick_queue_avg_us` / `tick_apply_avg_us` remain larger than raw inbound decode cost under higher pressure
    - cache behavior is healthy in the soak path (`SessionCacheHits` dominates after the first miss)
  - with production-like correction batching, client bridge `queue` / `apply` can rival or exceed `poll` under higher entity pressure
  - entity-pressure scaling is still monotonic from `1 -> 16 -> 64` entities
  - pending input / predicted state do not currently show runaway growth in these scenarios

## Current Recommendation

Do not spend more time on speculative server-side micro-tuning first.

Next useful client-side step:

- use the bounded `maxCorrectionsPerTick` path as a measured experiment, not a default
- current result:
  - capped apply lowers bridge apply cost materially
  - but explodes reconciliation backlog
  - so it is only a possible emergency backpressure valve, not a baseline optimization
- correction coalescing by entity is a better client-side relief direction than capped apply
  - in `Wan` / `Harsh` multi-entity soak runs, it reduced queued correction pressure without creating runaway backlog
  - latest threshold comparison at `64` entities:
    - `Wan`
      - baseline: `qmax 128`, `qtotal 8183`, `qcoal 0`, `tick_apply_avg_us 2.10`
      - always-on (`threshold=0`): `qmax 64`, `qtotal 7671`, `qcoal 511`, `tick_apply_avg_us 3.19`
      - thresholded (`threshold=32`): `qmax 96`, `qtotal 7864`, `qcoal 318`, `tick_apply_avg_us 3.14`
    - `Harsh`
      - baseline: `qmax 191`, `qtotal 7963`, `qcoal 0`, `tick_apply_avg_us 9.42`
      - always-on (`threshold=0`): `qmax 64`, `qtotal 5933`, `qcoal 2034`, `tick_apply_avg_us 6.19`
      - thresholded (`threshold=32`): `qmax 96`, `qtotal 6830`, `qcoal 1135`, `tick_apply_avg_us 3.83`
  - interpretation:
    - thresholded coalescing keeps most of the pressure relief
    - it does less replacement work than always-on coalescing
    - it is most useful under delayed / bursty profiles, not local steady-state
  - threshold sweep at `64` entities:
    - `Wan`
      - `threshold=16`: `qmax 80`, `qtotal 7799`, `qcoal 383`, `tick_apply_avg_us 3.30`
      - `threshold=32`: `qmax 96`, `qtotal 7960`, `qcoal 223`, `tick_apply_avg_us 3.14`
      - `threshold=64`: `qmax 128`, `qtotal 8181`, `qcoal 0`, `tick_apply_avg_us 2.31`
    - `Harsh`
      - `threshold=16`: `qmax 80`, `qtotal 6301`, `qcoal 1661`, `tick_apply_avg_us 15.93`
      - `threshold=32`: `qmax 96`, `qtotal 6767`, `qcoal 1198`, `tick_apply_avg_us 3.94`
      - `threshold=64`: `qmax 128`, `qtotal 7715`, `qcoal 250`, `tick_apply_avg_us 4.16`
  - practical read:
    - `32` is still the safest default threshold
    - `16` engages more aggressively, but overdoes replacement work on harsh profiles
    - `64` is too conservative to help consistently
  - it is not useful in the `Local` single-frame case where the client already keeps up and no coalescing opportunity exists
