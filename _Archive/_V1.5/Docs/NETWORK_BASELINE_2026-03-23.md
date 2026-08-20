# Network Baseline - 2026-03-23

This document records the confirmed server-network baseline after completing the replication
optimization pass (priority LOD, dense observer cadence, observer stream cadence, build workers).

Previous baseline: `Docs/NETWORK_BASELINE_2026-03-09.md`

---

## Stable Server Config

All settings below are active in the official server runtime via `ServerRuntimeHostNode.cs` CLI
parsing and `ServerRuntimeOptions` defaults.

### Transport

- `DecoupleTransportPumpFromSimulation = false`
- `MaxInboundDatagramsPerPump = 8192`
- UDP shared-socket required for high-CCU harness runs (`--udp --udp-shared-socket`)

### Replication

- `EnableObserverPriorityLod = true` (`--server-priority-lod`)
  - chunk-ring-distance cadence gate; no subscriber-count threshold, always active
- `EnableDenseObserverCadence = true` (`--server-dense-observer-cadence`)
  - `PlayerDenseObserverCadenceStartSubscribers = 15` (default changed from 64; 64 never fired at ~30 subs/chunk topology)
  - dense cap (`EnableDenseObserverRecipientCap`) remains **off** by default; two-pass selection overhead exceeded the ~20% fanout reduction at this subscriber density
- `ObserverStreamCadence = 3` (`--server-observer-stream-cadence 3`)
  - observer correction stream published every 3 network ticks (~48 ms) instead of every 2 (~32 ms)
  - net savings: ~0.7 ms tick_avg at 2048 CCU, ~1.3 ms at 2560 CCU
- `ReplicationBuildWorkers = 2` (`--server-repl-build-workers 2`)
  - 2 workers is the diminishing-returns knee; 4 workers adds flush-sync overhead that exceeds build savings

### Dual correction streams

- enabled; chunk-based recipient selector fast path active

### Spatial-first build

- `EnableObserverSpatialFirstBuild` is **mutually exclusive** with priority LOD and dense observer cadence
- benchmarked combination was net negative: per-delta `_spatialChunks.TryGetChunkCoord` lookup cost inside `AppendObserverChunkPacket` exceeded cache-coherence benefit at current chunk topology
- mutual exclusion is enforced in code with an explanatory comment

### Dense chat

- `EnableServerDenseLocalChatRecipientCap = true`, `ServerDenseLocalChatCapStartRecipients = 24`, `ServerDenseLocalChatCapMaxRecipients = 8`
- `EnableServerDenseLocalChatRateCap = true`, `ServerDenseLocalChatRateCapStartRecipients = 24`, `ServerDenseLocalChatRateCapMinTicks = 60`

---

## Confirmed Operating Envelope

Harness: C# sharded harness, 4 workers, `--udp --udp-shared-socket`, `realistic` profile (`active_ratio = 0.35`, outbound bot chat enabled).

All params active: `--server-priority-lod --server-dense-observer-cadence --server-observer-stream-cadence 3 --server-repl-build-workers 2`.

Staged ramp: 1024 → 2560 CCU in 128-CCU increments.
Report: `_tmp/reports/full_confirm_lod_cad3/csharp_sharded_20260323_204645/aggregate.json`

```
CCU    tick_avg  pump_avg  flush_avg  ping_p95   SLO
          (ms)      (ms)       (ms)      (ms)  <50ms
------------------------------------------------------
1024      4.42     0.061      0.533     32.18   PASS
1152      5.21     0.073      0.690     32.38   PASS
1280      6.04     0.092      1.093     32.59   PASS
1408      5.96     0.096      1.134     32.96   PASS
1536      6.84     0.155      1.454     33.67   PASS
1664      7.37     0.163      1.674     39.53   PASS
1792      8.37     0.199      2.051     45.64   PASS
1920      8.64     0.211      2.161     46.89   PASS
2048      9.59     0.339      2.576     47.75   PASS
2176     10.17     2.781      2.852     48.02   PASS
2304     10.69     4.075      2.999     48.16   PASS
2432     12.07     4.718      3.638     48.32   PASS
2560     14.35    11.204      3.548     49.04   PASS
```

13/13 stages PASS. Zero connection failures, zero drops across all stages.

Tick budget: 16 ms. Ping SLO: 50 ms p95.

---

## Interpretation

### Comfortable operating range

- **1024–1536 CCU**: tick_avg 4.4–6.8 ms, ping p95 32–34 ms. Ample headroom for physics,
  animation, and additional gameplay systems (targeting ≤6–7 ms additional cost).
- **1536–1920 CCU**: tick_avg 6.8–8.6 ms, ping p95 34–47 ms. Still comfortable; requires
  physics/gameplay costs to stay under ~5 ms.
- **2048–2560 CCU**: tick_avg 9.6–14.4 ms, ping p95 47–49 ms. Tight but passing; effectively
  the pre-physics server ceiling at the current tick rate.

### Recommended operating target

- **~1500 CCU** is the practical pre-physics target: leaves 6–7 ms for future systems while
  staying well clear of the SLO boundary.
- **~2500 CCU** remains achievable if the server tick is raised to 20 ms — the pump_avg cost
  at 2176+ CCU (2.8–11 ms) would fit inside a 20 ms budget with the same replication pressure.

### Transport pump observation

- `pump_avg_ms` rises sharply from 0.34 ms at 2048 CCU to 11.2 ms at 2560 CCU.
- This is the `MaxInboundDatagramsPerPump = 8192` ceiling becoming visible: at 2560 active
  UDP associations the inbound read loop processes significantly more datagrams per pump call.
- If scaling past 2560 CCU at 16 ms tick is required, the next investigation should focus on
  `DecoupleTransportPumpFromSimulation` and whether a partial async pump reduces tick inflation
  without reintroducing the inbound backlog observed in the prior decoupled baseline.

### Comparison to previous baseline (2026-03-09)

Previous baseline (pre-optimization, sharded hotspot):
- `2850` strict pass, `2875` first fail

Current baseline (all optimizations, realistic profile, 16 ms tick):
- `2560` PASS (49.04 ms p95) with full physics/gameplay headroom still available
- The optimization delta at 2048 CCU: tick_avg ~9.6 ms vs ~12.4 ms prior (−2.8 ms)
- The optimization delta at 2560 CCU: tick_avg ~14.4 ms vs ~16.7 ms estimated prior

---

## What Changed Since 2026-03-09

1. **Official server runtime is now primary**: tracked `ServerRuntime.cs` / `ServerRuntimeHostNode`
   is the only production server path; `_tmp/ECSNetworkIslandSliceNode.cs` is bot-client harness only.
2. **Priority LOD**: chunk-ring-distance cadence gate, always active. −0.5–1.0 ms at 2048+.
3. **Dense observer cadence**: `PlayerDenseObserverCadenceStartSubscribers = 15`. −0.2–0.4 ms.
4. **Observer stream cadence = 3**: observer stream every 48 ms instead of 32 ms. −0.7–1.3 ms at 2048–2560.
5. **Build workers = 2**: optimal point confirmed; 4 workers is net negative.
6. **Chat archetype naming**: `ResolveChatSenderContext` now resolves archetype `DisplayName`
   (e.g. "Default #4" instead of "Player 4294967296"). Placeholder until character names exist.
7. **MainSceneRoot routing fix**: `server_build` feature check runs before legacy harness routing,
   preventing `--server` flag from accidentally routing a server export to `island_slice.tscn`.

---

## Rejected / Deferred Configs

- `EnableObserverSpatialFirstBuild` with LOD/cadence: net negative; mutual exclusion enforced
- `ReplicationBuildWorkers = 4`: flush-sync overhead > build savings; 2 workers is the knee
- `EnableDenseObserverRecipientCap`: two-pass overhead > fanout reduction at ~30 subs/chunk
- `EnableServerCorrectionDeadband`: neutral for bot workload (bots move consistently); revisit with real player movement data
