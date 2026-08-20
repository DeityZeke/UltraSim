# Island Slice Network Baseline (2026-02-22)

Status: historical archive. Superseded for current planning by `Docs/NETWORK_BASELINE_2026-03-09.md`.

Pre-AoI / pre-chunking / pre-fanout-optimization baseline for the current island slice.

## Test Method

- Harness: `_tmp/run_island_socket_ramp.py`
- Stage mode: `independent` (each stage is a fresh client set)
- SLO used:
  - `ping p95 <= 40 ms`
  - `min ping samples >= 20`
  - `connect_success_rate >= 95%`
- Notes:
  - `Disc` column in independent mode is expected teardown from previous stage, not a connection failure.
  - `SrvCPU95` shows `n/a` if the harness is attached to an already-running server (not launched by the harness).

## Baseline Summary (Current Slice)

- Worst-case dense / everyone active (20 Hz movement): **PASS through 56 CCU**, **FAIL at 64 CCU**
- More realistic profiles:
  - 10 Hz movement-only: **PASS through 80 CCU**, **FAIL at 96 CCU**
  - Mixed activity (40% movers/chatters): **PASS through 80 CCU**, **FAIL at 96 CCU**
- Uptime effect:
  - Fresh vs warm server (30+ min / 2+ hr) produced the same knee in tested profiles.
  - No meaningful uptime degradation observed.

## Commands Used

### 1) Worst-case dense active sweep (20 Hz, chat on)

```powershell
python _tmp\run_island_socket_ramp.py `
  --host 127.0.0.1 --port 42042 `
  --stages 32,40,48,56,64 `
  --stage-mode independent `
  --stage-duration 60 `
  --settle-seconds 5 `
  --chat-enabled --chat-min-interval-s 2 --chat-max-interval-s 5 `
  --slo-ping-p95-ms 40 --slo-min-ping-samples 20
```

Observed result (multiple runs, fresh/warm):
- `Max ping-pass CCU: 56`
- `First ping-fail stage: CCU 64` (`p95 ~49-51 ms`)

### 2) Reduced input rate sweep (10 Hz movement-only)

```powershell
python _tmp\run_island_socket_ramp.py `
  --launch-server-exe .output\UltraSim\UltraSim.exe `
  --host 127.0.0.1 --port 42042 `
  --stages 64,80,96,112,128 `
  --stage-mode independent `
  --stage-duration 45 `
  --settle-seconds 5 `
  --input-hz 10 `
  --slo-ping-p95-ms 40 --slo-min-ping-samples 20
```

Observed result:
- `Max ping-pass CCU: 80`
- `First ping-fail stage: CCU 96` (`p95=112.39 ms`)

Report:
- `_tmp/reports/island_socket_ramp_20260222_033306.json`

### 3) Mixed-activity sweep (40% active movement/chat)

```powershell
python _tmp\run_island_socket_ramp.py `
  --launch-server-exe .output\UltraSim\UltraSim.exe `
  --host 127.0.0.1 --port 42042 `
  --stages 64,80,96,112,128 `
  --stage-mode independent `
  --stage-duration 45 `
  --settle-seconds 5 `
  --chat-enabled --chat-min-interval-s 2 --chat-max-interval-s 5 `
  --active-ratio 0.4 --chat-active-ratio 0.4 --activity-window-s 3 `
  --slo-ping-p95-ms 40 --slo-min-ping-samples 20
```

Observed result:
- `Max ping-pass CCU: 80`
- `First ping-fail stage: CCU 96` (`p95=49.46 ms`)

Report:
- `_tmp/reports/island_socket_ramp_20260222_033728.json`

## Interpretation (Current State)

- Current limit is a **dense single-slice replication/fanout latency knee**, not a server uptime stability issue.
- This baseline is intentionally pessimistic because it has:
  - no chunking
  - no AoI culling
  - no update-rate reduction by distance
- Expected next milestone: re-run the exact same profiles after fanout/chunking/AoI and compare deltas directly.

## Post-AOI Phase A Checkpoint (Spatial Chunks + AOI Interest Filtering)

Phase A AOI was implemented in the island slice (server spatial chunks + chunk-based interest filter + AOI leave despawns).

### Mixed-activity (40% active movers/chatters) post-AOI result

Observed result (same `p95 <= 40 ms`, 60s stages, independent mode):
- `Max ping-pass CCU: 325`
- `First ping-fail stage: CCU 350` (`p95=53.44 ms`)

Verified with a non-debug server and longer settle (`--settle-seconds 15`) after a debug-mode outlier:
- `300` repeated twice: `p95=24.32 ms`, `p95=22.34 ms`
- `325`: `p95=38.65 ms` (PASS)
- `350`: `p95=51.88 ms` (FAIL)

The earlier `300` failure (`p95 ~415 ms`) was traced to benchmark contamination from server debug logging + short settle, not an AOI regression.

This is a ~4x uplift vs the pre-AOI mixed-profile knee (~80 pass / 96 fail).

### Post-AOI + Post-Transport-Batching Checkpoint (Mixed 40% Active)

After outbound transport flush batching (per-connection batched writes in `TcpFramedNetworkTransport`), the mixed profile improved substantially again.

Validated non-debug run (independent stages, 60s stage duration, 15s settle, repeated checkpoints):
- `300` (x2): PASS (`p95 ~17.0-17.2 ms`)
- `400` (x2): PASS (`p95 ~18.1 ms`)
- `500` (x2): PASS (`p95 ~19.8-20.1 ms`)
- `Max ping-pass CCU: 500`
- `First ping-fail stage: none` (in tested range)

This supersedes the earlier post-AOI/pre-batching mixed knee (`325 pass / 350 fail`) for current island-slice behavior.

### Post-AOI + Post-Transport-Batching + Async Harness Checkpoint (Mixed 40% Active)

After adding the async socket ramp harness (`_tmp/run_island_socket_ramp_async.py`) to remove the Windows `select()` descriptor cap, the mixed profile was validated beyond 500 CCU.

Validated non-debug runs:
- `500`: PASS (`p95=27.38 ms`)
- `550`: PASS (`p95=35.65 ms`)
- `600`: FAIL (`p95=41.64 ms`)

Follow-up repeated knee validation (with server CPU sampling):
- `575`: PASS (`p95=38.05 ms`, `SrvCPU95 ~ 6.9%`)
- `600` (x2): FAIL (`p95=42.03 ms`, `p95=43.38 ms`, `SrvCPU95 ~ 7.0%`)

Current verified mixed-profile knee (non-debug server, current AOI + batching):
- **Max ping-pass CCU: 575**
- **First ping-fail CCU: 600**

Authoritative repeated validation report (async harness, 60s stages, 15s settle):
- `550`: PASS (`p95=33.82 ms`)
- `575`: PASS (`p95=38.37 ms`)
- `600` (x3): FAIL (`p95=40.20 / 40.40 / 41.58 ms`)
- `625`: FAIL (`p95=42.61 ms`)
- `SrvCPU95` near knee: ~`8.7-9.2%`

Report:
- `_tmp/reports/island_socket_ramp_async_20260223_130855.json`

Interpretation:
- The system is still not compute-bound on the server at this knee (`~7%` CPU reported in the ramp sampler).
- The next likely long-term scaling limit is transport architecture (thread-per-connection model), not AOI/fanout filtering.

Notes:
- Use **non-debug server mode** for SLO ramp measurement.
- Use **debug server mode** only for short telemetry snapshots (debug logging materially perturbs p95 at high CCU).

### AOI telemetry snapshot (500 bots, mixed 40% active)

Server debug metric sample:

```text
[IslandNet] SERVER_METRIC|status=Started|connections=500|sessions=500|entities=500|...|aoi_chunks=273|aoi_entities=500|aoi_sub_chunks_avg=9|aoi_sub_chunks_max=9|aoi_visible_avg=13|aoi_visible_max=24|aoi_enter=1184|aoi_leave=1120|aoi_filter_selected=76217|aoi_filter_total=2883500|aoi_filter_selected_pct=2.6
```

Interpretation:
- `aoi_sub_chunks_avg/max=9` validates radius-1 square subscriptions (`3x3` chunks).
- `aoi_visible_avg=13` (with 500 entities total) confirms strong fanout reduction.
- `aoi_filter_selected_pct=2.6` means ~97.4% of broad fanout checks are rejected by AOI.

### Harness limit note (Windows Python `select()`)

The socket-ramp harness hit a Windows/Python selector limit at higher socket counts:
- `ValueError: too many file descriptors in select()`

This is a harness limitation (`_tmp/run_island_socket_ramp.py` using `select()`), not evidence of a server-side 500-connection cap.

### Async Harness (Windows IOCP-friendly) note

An async harness was added to bypass the selector limit:
- `_tmp/run_island_socket_ramp_async.py`

Use it for >500 local socket tests on Windows.

### Hotspot (dense AOI) reference result (track separately)

Hotspot spawning (clustered entities, AOI density stress) shows the expected earlier knee compared to spread/mixed runs:
- `300`: PASS (`p95=34.40 ms`)
- `400`: FAIL (`p95=77.10 ms`)
- `500`: FAIL (`p95=128.44 ms`)

This should be tracked separately from the spread/mixed baseline because it represents a deliberate worst-case density profile.

### UDP Hybrid (shared-socket harness) mixed-profile parity baseline

After implementing hybrid TCP+UDP routing (UDP for unreliable channels), UDP batching, tokenized UDP payloads, and shared-socket UDP demux in the async harness:

- Mixed profile (`active-ratio=0.4`, chat enabled), UDP + shared-socket harness:
  - `550`: PASS (`p95=35.24 ms`)
  - `575`: PASS (`p95=39.03 ms`)
  - `600`: FAIL (`p95=43.93 ms`, repeat `60.82 ms`)
  - UDP assoc SLO: PASS on all stages (`600/600` associated at 600)

Current verified UDP mixed-profile knee (shared-socket harness):
- **Max ping-pass CCU: 575**
- **First ping-fail CCU: 600**

Interpretation:
- UDP is at practical parity with the current TCP mixed-profile knee (`575/600`) after shared-socket/tokenized UDP harness support.
- Remaining p95 variance at `600` is likely near-threshold jitter, not UDP association instability (assoc SLO remains PASS).

### UDP Hybrid (shared-socket harness) hotspot baseline

Explicit hotspot run (clustered spawn):
- launch server with `--spawn-spacing-grid=1 --spawn-wrap-count=20`

UDP + shared-socket harness (no Priority LOD):
- `300`: PASS (`p95=26.70 ms`)
- `350`: FAIL (`p95=48.14 ms`)
- `400`: FAIL (`p95=89.64 ms`)
- UDP assoc SLO: PASS on all stages

Current verified UDP hotspot knee (shared-socket harness):
- **Max ping-pass CCU: 300**
- **First ping-fail CCU: 350**

### UDP Hotspot + Priority LOD Phase 1 (outer cadence = 4)

Explicit hotspot run with server Priority LOD enabled:
- launch server with:
  - `--spawn-spacing-grid=1 --spawn-wrap-count=20`
  - `--priority-lod --priority-lod-outer-cadence=4`

UDP + shared-socket harness:
- `300`: PASS (`p95=23.71 ms`)
- `350`: PASS (`p95=30.67 ms`)
- `400`: FAIL (`p95=48.46 ms`)
- UDP assoc SLO: PASS on all stages

Observed effect:
- Priority LOD (outer cadence `4`) improves hotspot UDP behavior and moves the hotspot knee from roughly `300/350` to `350/400` in this profile.

Authoritative no-debug report:
- `_tmp/reports/island_socket_ramp_async_20260224_024725.json`

### UDP Hotspot + Priority LOD + Selector (Phase 2 baseline lock, 2026-02-28)

Latest no-debug runs with:
- UDP shared-socket harness
- hotspot spawn (`--spawn-spacing-grid=1 --spawn-wrap-count=20`)
- Priority LOD enabled (`--priority-lod --priority-lod-outer-cadence=4`)
- recipient selector fast path enabled (`--server-recipient-selector=true`)

Sanity run:
- stages: `300,300,350,400`
- result: **PASS through 400**
- report: `_tmp/reports/island_socket_ramp_async_20260228_001547.json`

Threshold run:
- stages: `425,425,450,450,475,500,525`
- result:
  - `425`: PASS (`p95=38.23`, `38.89 ms`)
  - `450`: FAIL (`p95=41.28`, `46.75 ms`)
  - `475+`: FAIL
- report: `_tmp/reports/island_socket_ramp_async_20260228_002443.json`

Current verified UDP hotspot knee (Phase 2 baseline lock):
- **Max ping-pass CCU: 425**
- **First ping-fail CCU: 450**

Operational note:
- keep using repeated warmup checkpoints (duplicate first stage) in threshold runs to filter one-off warmup jitter before locking a new knee.

### UDP debug telemetry note (server metrics capture)

When running UDP ramps with `--debug-server --capture-server-metrics`:
- harness captures launched server `SERVER_METRIC` UDP/perf counters into JSON/CSV stage rows
- debug logging materially perturbs p95 (especially at high CCU), so these runs are for correlation/telemetry, not authoritative SLO baselines

Useful observed pattern:
- mixed/hotspot UDP shared-socket runs show stable UDP association (`assoc_slo=PASS`) and no server token/endpoint drops while harness-side UDP parse cost remains a major contributor to measured p95 in debug runs.



## Post-Baseline Design Decisions (Chunking/AOI)

Agreed follow-up architecture after collecting the pre-AOI baseline:

- Implement **server spatial chunking + AOI interest filtering first** (largest expected scaling win).
- Optimize **shared delta fanout** after AOI is in place, not before.
- Keep chunking split into three layers:
  - Spatial chunks (server runtime/AOI + dynamic entity membership)
  - Terrain chunks (authoritative terrain + frozen statics/deco/building records)
  - Render chunks (client sliding window / pooled render containers)
- Frozen map statics ('/freeze'-style baked objects) are **chunk content records**, not default ECS entities. Load them on the server for collision/LOS/nav and on the client for rendering; promote to ECS only when runtime state/logic is required.
