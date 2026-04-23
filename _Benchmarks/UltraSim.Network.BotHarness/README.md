# Network Bot Harness

This is the first standalone ramp harness for the rebuilt network stack.

It is intentionally narrow:

- real UDP sockets
- shared client/server session hosts
- handshake + movement on one transport poll path
- staged client-count ramps
- per-stage tick timing stats
- per-stage ping timing stats
- simple client pending-input backlog stats
- optional observer-replication load on top of the same movement/session path
- replication logical-vs-wire datagram reporting
- optional real client-side observer-frame decode/apply on top of that widened path

It does not yet try to recreate the full old bot harness feature set:

- no TCP path
- no shared UDP mux
- no chat traffic
- no external server-process launch
- no persistence or replication breadth beyond the current movement slice

## Running

```powershell
dotnet build .\_Benchmarks\UltraSim.Network.BotHarness\UltraSim.Network.BotHarness.csproj -c Release
dotnet .\_Benchmarks\UltraSim.Network.BotHarness\bin\Release\net8.0\UltraSim.Network.BotHarness.dll --stages 32,64,128,256 --stage-duration 30 --settle-seconds 3 --tick-hz 62
```

Reports are written to `_Benchmarks/results/network_bot_harness_*.json`.

Useful options:

- `--slo-tick-p95-ms <n>`
- `--slo-ping-p95-ms <n>`
- `--ping-interval-s <n>`
- `--stop-on either|tick|ping|none`
- `--observer-replication`
- `--observer-events`
- `--observer-event-every-ticks <n>`
- `--spawn-spacing-m <n>`
- `--spawn-grid-width <n>`
- `--aoi-radii-m <r0,r1,...>`
- `--owner-deadband <meters>`
- `--owner-max-silent-ticks <n>`

Recommended run categories:

1. Real use / server-budget ceiling
   `--stop-on tick --slo-tick-p95-ms 4 --slo-ping-p95-ms 50`

2. Latency ceiling / highest CCU while keeping ping healthy
   `--stop-on ping --slo-tick-p95-ms 999 --slo-ping-p95-ms 50`

3. First widened observer-replication read
   `--observer-replication --spawn-spacing-m 4`

4. Less-dense observer-replication read
   `--observer-replication --spawn-spacing-m 12`

5. Broader multi-ring observer read without changing the code path
   `--observer-replication --spawn-spacing-m 12 --aoi-radii-m 7.5,23.5,39.5`

## Current Role

This tool exists to answer the question:

- does the real socket/session/movement path still look healthy once many clients are connected at once?

It is the intended checkpoint before a broader external-process or gameplay-richer harness.

## Current Observer-Replication Note

The widened harness now reports:

- `replication_published_items`
- `replication_datagrams`
- `replication_wire_datagrams`
- `observer_frames_received`
- `observer_frames_applied`
- `observer_events_received`
- `observer_events_applied`
- `owner_correction_samples`
- `owner_correction_avg_position_error`
- `owner_correction_max_position_error`
- `owner_correction_avg_velocity_error`
- `owner_correction_max_velocity_error`
- `observer_state_items`

That split matters:

- `replication_datagrams`
  logical per-recipient fanout before transport aggregation
- `replication_wire_datagrams`
  actual UDP datagrams sent after transport-level same-connection bundling

The widened harness AOI policy is now configurable:

- default:
  - `--aoi-radii-m 7.5`
- broader gameplay-shaped profile:
  - `--aoi-radii-m 7.5,23.5,39.5`

This is intended to let the same live path broaden naturally as replication families grow, without forking into a separate parity-only harness.

The widened harness can now also include a first ordered observer-event family on top of latest-state:

- enable with `--observer-events`
- control cadence with `--observer-event-every-ticks <n>`

That path uses the same live session hosts, AOI subscriptions, fanout bridge, and client-side decode/apply flow as the latest-state path.

The owner path is now also slightly less synthetic:

- the harness now sends predicted owner movement state instead of a zero/default predicted state
- the client bridge records correction magnitude when an authoritative movement state acknowledges a predicted input
- this is still a narrow owner-correction slice, not the full old reconciliation system
- owner correction policy is now configurable in the live harness:
  - `--owner-deadband <meters>`
  - `--owner-max-silent-ticks <n>`

Position/correction note:

- the harness movement body uses world-space float positions
- `--spawn-spacing-m` writes directly into those position values
- so owner-correction magnitudes in the report are in the same world-space meter units the harness already uses
- the earlier large owner-correction spikes were not real drift; they were caused by the client predicting from `(0,0)` while the server spawned entities on a grid

Current first read with actual client consume:

- dense profile (`spawn-spacing-m 4`, `10s` stages)
  - `64` clients: `tick_p95 = 2.429 ms`
  - `68` clients: `tick_p95 = 6.039 ms`
  - practical current ceiling: about `64`
- less-dense profile (`spawn-spacing-m 12`, `10s` stages)
  - `128` clients: `tick_p95 = 2.643 ms`
  - `132` clients: `tick_p95 = 4.821 ms`
  - practical current ceiling: about `128`

So the widened path is now measuring something materially closer to real observer replication: not just publish and transport, but client-side frame decode/apply too. The transport bundling fix still matters, but it is no longer the whole story.
