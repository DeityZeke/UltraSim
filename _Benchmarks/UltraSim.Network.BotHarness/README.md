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

Useful runtime-capacity flags:
- `--ordered-event-capacity 1024`
- `--replay-buffer-capacity 256`

Reports are written to `_Benchmarks/results/network_bot_harness_*.json`.

Useful options:

- `--slo-tick-p95-ms <n>`
- `--slo-ping-p95-ms <n>`
- `--ping-interval-s <n>`
- `--stop-on either|tick|ping|none`
- `--observer-replication`
- `--observer-visual-state`
- `--observer-bulk-state`
- `--observer-bulk-every-ticks <n>`
- `--observer-events`
- `--observer-event-every-ticks <n>`
- `--enable-latest-state-batching`
- `--disable-latest-state-batching`
- `--enable-latest-state-recipient-phasing`
- `--disable-latest-state-recipient-phasing`
- `--enable-async-server-flush`
- `--disable-async-server-flush`
- `--enable-async-client-receive`
- `--disable-async-client-receive`
- `--spawn-spacing-m <n>`
- `--spawn-grid-width <n>`
- `--aoi-radii-m <r0,r1,...>`
- `--ring-cadence-ticks <t0,t1,t2,...>`
- `--ordered-event-capacity <n>`
- `--replay-buffer-capacity <n>`
- `--owner-deadband <meters>`
- `--owner-max-silent-ticks <n>`
- `--owner-prediction-jitter-percent <fraction>`

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

6. Broader multi-ring observer read with explicit ring cadence
   `--observer-replication --spawn-spacing-m 12 --aoi-radii-m 7.5,23.5,39.5 --ring-cadence-ticks 1,2,3`

## Current Role

This tool exists to answer the question:

- does the real socket/session/movement path still look healthy once many clients are connected at once?

It is the intended checkpoint before a broader external-process or gameplay-richer harness.

## Current Observer-Replication Note

The widened harness now reports:

- `server_pump_avg_ms`
- `server_pump_p95_ms`
- `publish_avg_ms`
- `publish_p95_ms`
- `dispatch_avg_ms`
- `dispatch_p95_ms`
- `dispatch_schedule_avg_ms`
- `dispatch_schedule_p95_ms`
- `dispatch_fanout_avg_ms`
- `dispatch_fanout_p95_ms`
- `dispatch_flush_avg_ms`
- `dispatch_flush_p95_ms`
- `flush_drain_avg_ms`
- `flush_drain_p95_ms`
- `flush_bundle_avg_ms`
- `flush_bundle_p95_ms`
- `flush_send_avg_ms`
- `flush_send_p95_ms`
- `flush_connections_avg`
- `flush_connections_p95`
- `flush_wire_datagrams_avg`
- `flush_wire_datagrams_p95`
- `async_flush_previous_avg_ms`
- `async_flush_previous_p95_ms`
- `async_flush_previous_send_avg_ms`
- `async_flush_previous_send_p95_ms`
- `async_flush_handoff_wait_avg_ms`
- `async_flush_handoff_wait_p95_ms`
- `async_flush_pending_avg`
- `async_flush_pending_p95`
- `async_flush_previous_wire_datagrams_avg`
- `async_flush_previous_wire_datagrams_p95`
- `client_pump_avg_ms`
- `client_pump_p95_ms`
- `client_poll_pending_drain_avg_ms`
- `client_poll_pending_drain_p95_ms`
- `client_poll_socket_recv_avg_ms`
- `client_poll_socket_recv_p95_ms`
- `client_poll_bundle_unpack_avg_ms`
- `client_poll_bundle_unpack_p95_ms`
- `client_poll_bundle_index_avg_ms`
- `client_poll_bundle_index_p95_ms`
- `client_poll_bundle_materialize_avg_ms`
- `client_poll_bundle_materialize_p95_ms`
- `client_poll_bundle_slice_avg_ms`
- `client_poll_bundle_slice_p95_ms`
- `client_poll_bundle_datagram_create_avg_ms`
- `client_poll_bundle_datagram_create_p95_ms`
- `client_poll_bundle_enqueue_avg_ms`
- `client_poll_bundle_enqueue_p95_ms`
- `client_poll_payload_copy_avg_ms`
- `client_poll_payload_copy_p95_ms`
- `client_poll_envelope_parse_avg_ms`
- `client_poll_envelope_parse_p95_ms`
- `client_poll_async_receive_avg_ms`
- `client_poll_async_receive_p95_ms`
- `client_poll_async_queued_avg`
- `client_poll_async_queued_p95`
- `client_observer_state_decode_avg_ms`
- `client_observer_state_decode_p95_ms`
- `client_observer_state_apply_avg_ms`
- `client_observer_state_apply_p95_ms`
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
- `alloc_bytes`
- `gc_gen0_collections`
- `gc_gen1_collections`
- `gc_gen2_collections`

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

Ring-aware latest-state cadence is also configurable:

- `--ring-cadence-ticks 1,2,3`
- current intent/default:
  - ring `0`: every tick
  - ring `1`: every `2` ticks
  - ring `2`: every `3` ticks

That cadence currently applies to AOI latest-state families in the server publish layer, not to ordered events.

This is intended to let the same live path broaden naturally as replication families grow, without forking into a separate parity-only harness.

The widened harness can now also include a first ordered observer-event family on top of latest-state:

- enable with `--observer-events`
- control cadence with `--observer-event-every-ticks <n>`

That path uses the same live session hosts, AOI subscriptions, fanout bridge, and client-side decode/apply flow as the latest-state path.

The widened harness can now also include a second observer-visible latest-state family:

- enable with `--observer-visual-state`
- current harness shape:
  - base movement latest-state
  - optional visual latest-state
  - optional ordered observer events

This keeps the “broaden the live path” work honest: observer cost no longer comes from only one latest-state family.

The widened harness now also enables a first batched realtime latest-state envelope seam:

- contiguous realtime latest-state scheduled-work items in the same scope can be emitted as one `RealtimeStateBatchPacket`
- this is currently a fanout-side aggregation optimization, not a new general delta/baseline path
- current intent is to reduce the hot per-item realtime envelope encode cost in the widened observer path without changing higher-level runtime semantics
- current harness default: batching disabled
- use `--enable-latest-state-batching` for an A/B comparison on the same widened profile

The widened harness also has an experimental latest-state recipient-phasing seam:

- current harness default: recipient phasing disabled
- use `--enable-latest-state-recipient-phasing` for an A/B comparison on the same widened profile
- current intent:
  - replace synchronized outer-ring observer sends with per-recipient phased delivery
  - reduce same-tick flush/send bursts without touching owner-critical traffic
- current read:
  - it can reduce server-side dispatch/flush cost on dense runs
  - but it currently worsens ping tail enough that it should stay experimental, not default

The harness also has an experimental async client receive seam:

- current harness default: disabled
- use `--enable-async-client-receive` for a focused A/B on the same widened profile
- current intent:
  - move UDP receive + bundle unpack off the client pump thread
  - let `PollInbound()` drain already-decoded queued datagrams
- current read:
  - not a clean win on the widened dense profile
  - queue growth and GC pressure can outweigh the tick-thread poll savings
  - keep this off unless a specific experiment is targeting receive-thread policy/backpressure

The widened harness also has an experimental async server flush seam:

- current harness default: async server flush enabled
- use `--enable-async-server-flush` for an A/B comparison on the same widened profile
- use `--disable-async-server-flush` to force the older synchronous tick-thread flush path
- current intent:
  - keep normal publish/schedule/fanout on the tick thread
  - hand the final UDP bundle/send pass to one bounded background flush set
  - preserve bounded backlog instead of introducing unbounded writer queues
- current telemetry:
  - `async_flush_previous_*`
    - completed background flush cost from the prior handoff
  - `async_flush_handoff_wait_*`
    - tick-thread wait time when the previous async flush is still in flight
  - `async_flush_pending_*`
    - bounded pending-batch depth seen by the tick thread

The widened harness can now also include a first `Bulk` observer family:

- enable with `--observer-bulk-state`
- control cadence with `--observer-bulk-every-ticks <n>`
- current intent:
  - `Realtime` latest-state for movement
  - optional second `Realtime` latest-state family for visual-style state
  - optional `Bulk` latest-state family for larger lower-cadence state
  - optional `ReliableEvent` ordered events

The owner path is now also slightly less synthetic:

- the harness now sends predicted owner movement state instead of a zero/default predicted state
- the client bridge records correction magnitude when an authoritative movement state acknowledges a predicted input
- this is still a narrow owner-correction slice, not the full old reconciliation system
- owner correction policy is now configurable in the live harness:
  - `--owner-deadband <meters>`
  - `--owner-max-silent-ticks <n>`
- owner prediction divergence can now be made slightly non-ideal in a controlled, repeatable way:
  - `--owner-prediction-jitter-percent <fraction>`
  - example: `0.05` means up to `±5%` prediction-side velocity jitter per tick
  - this affects only harness-side predicted owner state, not the actual input sent to the server

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

Current broader three-lane, three-ring read with ring-aware latest-state cadence:

- profile:
  - `--aoi-radii-m 7.5,23.5,39.5`
  - `--ring-cadence-ticks 1,2,3`
  - `--observer-replication`
  - `--observer-visual-state`
  - `--observer-bulk-state --observer-bulk-every-ticks 16`
  - `--observer-events --observer-event-every-ticks 8`
  - `--owner-deadband 0.05`
  - `--owner-max-silent-ticks 4`
  - `--owner-prediction-jitter-percent 0.05`
- less-dense `spawn-spacing-m 12`, `3s` stages:
  - earlier ring-cadenced `0.40` baseline:
    - `32` clients: `tick_p95 = 1.407 ms`
    - `48` clients: `tick_p95 = 3.387 ms`
    - `64` clients: `tick_p95 = 4.343 ms`
  - current stable widened baseline:
    - `32` clients: `tick_p95 = 1.504 ms`
    - `48` clients: `tick_p95 = 3.589 ms`
    - `64` clients: `tick_p95 = 3.865 ms`

Current read:

- ring-aware cadence materially improved the widened three-lane profile compared to the earlier all-rings-every-pass shape
- under this broader live path, `48` clients pass the `4 ms` tick-p95 target comfortably
- after preserving stable latest-state item identity across scopes and switching the harness observer store to slot-indexed arrays, `64` clients now also pass the `4 ms` tick-p95 target on the current less-dense `0.40` profile

Current substage breakdown read from the same broadened `0.40` profile:

- report: `_Benchmarks/results/observer_bulk_ringcad_breakdown_040/network_bot_harness_20260423_020740.json`
- `64` clients:
  - `tick_p95 = 4.367 ms`
  - `server_pump_p95 = 0.365 ms`
  - `publish_p95 = 0.198 ms`
  - `dispatch_p95 = 0.803 ms`
  - `client_pump_p95 = 2.570 ms`

Current interpretation:

- the remaining cost is not primarily in server-side latest-state publication
- the larger current slices are:
  - client pump / observer apply
  - dispatch / fanout
- so the next worthwhile tuning work should focus there before trying more policy changes to the same widened profile

Current client-side single-pass inbound result:

- report: `_Benchmarks/results/observer_bulk_ringcad_breakdown_040_after_clientpass_rebuilt/network_bot_harness_20260423_021643.json`
- change:
  - `ClientMovementSessionHost` now processes state-plane inbound datagrams in one pass instead of scanning the same batch separately for movement, observer latest-state, and observer events
- `64` clients:
  - `tick_p95 = 4.316 ms`
  - `server_pump_p95 = 0.352 ms`
  - `publish_p95 = 0.168 ms`
  - `dispatch_p95 = 0.803 ms`
  - `client_pump_p95 = 2.147 ms`

Current read:

- the client-side single-pass change materially reduced the largest measured substage
- the next hot slice is now more clearly dispatch / fanout plus the remaining client consume/apply work

Current finer client breakdown:

- report: `_Benchmarks/results/observer_bulk_ringcad_clientbreakdown_040/network_bot_harness_20260423_024405.json`
- `64` clients:
  - `tick_p95 = 4.216 ms`
  - `dispatch_p95 = 0.783 ms`
  - `client_pump_p95 = 2.281 ms`
  - `client_poll_p95 = 0.447 ms`
  - `client_control_p95 = 0.009 ms`
  - `client_movement_p95 = 0.020 ms`
  - `client_observer_state_p95 = 1.422 ms`
  - `client_observer_event_p95 = 0.871 ms`

Current interpretation:

- inside the client pump, the dominant sub-slices are observer latest-state and observer events
- transport poll / bundle receive is measurable but secondary
- owner movement handling is negligible in this broadened observer-heavy profile

Current observer-state store-path tightening result:

- report: `_Benchmarks/results/observer_bulk_ringcad_clientstore_040/network_bot_harness_20260423_024512.json`
- change:
  - harness-side observer latest-state storage now uses `CollectionsMarshal.GetValueRefOrAddDefault(...)`
  - observer event retention remains bounded without front-trimming a list
- `64` clients:
  - `tick_p95 = 4.213 ms`
  - `dispatch_p95 = 0.776 ms`
  - `client_pump_p95 = 2.077 ms`
  - `client_poll_p95 = 0.406 ms`
  - `client_observer_state_p95 = 1.437 ms`
  - `client_observer_event_p95 = 0.764 ms`

Current read:

- the biggest measurable improvement in the latest run is on the event-side client sub-slice
- the observer latest-state sub-slice is still the single largest client cost
- current observer storage is still dictionary-backed and keyed by scoped item keys, so there is still structural headroom left above these local micro-optimizations

Current stable item-identity / slot-store result:

- report: `_Benchmarks/results/network_bot_harness_20260423_034433.json`
- changes:
  - chunk-AOI latest-state now preserves stable item identity across scopes inside `LatestStateStream`
  - the widened harness now uses structured item keys for movement, visual, and bulk observer families
  - harness-side observer latest-state storage is now slot-indexed by structured item key instead of dictionary-backed by scoped item key
- `64` clients:
  - `tick_p95 = 3.865 ms`
  - `dispatch_p95 = 0.785 ms`
  - `client_pump_p95 = 2.412 ms`
  - `client_observer_state_p95 = 1.389 ms`
  - `client_observer_event_p95 = 0.821 ms`

Current interpretation:

- stable latest-state item identity across scopes was the right structural direction for AOI observer state
- the client observer latest-state path is no longer forced to treat the same logical observed item as a different key per ring
- the current broadened `0.40` / `64` profile now sits inside the `4 ms` tick-p95 target without increasing observer payload size

Current ordered-event slot/store result:

- report: `_Benchmarks/results/network_bot_harness_20260423_124205.json`
- change:
  - harness-side observer events now carry a stable observer slot and store by slot instead of appending into one global queue
- `10s` widened `0.40` run:
  - `32`: `tick_p95 = 1.856 ms`
  - `48`: `tick_p95 = 2.434 ms`
  - `64`: `tick_p95 = 1.733 ms`
  - `client_observer_state_p95 = 0.317 ms`
  - `client_observer_event_p95 = 0.181 ms`

Current interpretation:

- the event-side client path is now materially cheaper in the widened harness
- however, the resulting `32–64` curve is even more obviously non-monotonic
- that means the next useful step is to investigate the geometry/subscription-shape behavior directly, not to immediately treat the latest event-slot run as the final ceiling
