# UltraSim headless Network bots

`UltraSim.NetworkBots` is the maintained PF1/PF2 load-generation and measurement client. One CLR process owns many
independent ordinary remote client lifetimes and advances them from one bounded central pump. Each
bot has its own UDP transport, authenticated session, application epoch, profile/content proof,
guest/control identity, Client Core state, command sequence, reconnect lifetime, and deterministic
behavior stream. Immutable CAS installation data is shared through the normal `ContentCache`.

The product builds only against the `ClientOnly` physical `UltraSim.Network.dll`. Its verified
closure contains Client Core/Network/Content/StandardUi plus public Game protocol contracts. It has
no Godot, `Client.Local`, Game authority, server Runtime/Persistence/Physics, direct ECS access, or
`UltraSim.Network.Testing` dependency.

## Run

Start a dedicated development authority and explicitly choose a bounded capacity:

```bash
dotnet run --project Server/DirectConnect/UltraSim.Server.DirectConnect.csproj \
  -c Release -p:UltraSimNetworkFlavor=ServerOnly -- \
  --bind=127.0.0.1:7777 \
  --content-bind=127.0.0.1:7778 \
  --maximum-participants=50 \
  --same-address-admission-burst=128
```

Copy the printed deployment-key fingerprint, then run the swarm:

```bash
dotnet run --project Tools/NetworkBots/UltraSim.NetworkBots.csproj \
  -c Release -p:UltraSimNetworkFlavor=ClientOnly -- \
  --endpoint=127.0.0.1:7777 \
  --fingerprint=<64-hex-deployment-key-fingerprint> \
  --bots=50 \
  --seed=104729 \
  --duration=60s \
  --scenario=mixed-chaos \
  --cache=/tmp/ultrasim-network-bots-50
```

Required public options are `--endpoint`, `--fingerprint`, `--bots`, `--seed`, `--duration`,
`--scenario`, and `--cache`. `--warmup` selects a separate warmup/positioning interval; it defaults
to 60 seconds for PF2 scenarios. `--verbose` prints the bounded diagnostic context of a failed bot.
File barriers and JSON summary options are acceptance-coordination surfaces used by the repository
verifier, not gameplay shortcuts.

For synchronized external/server captures, `--measurement-complete-file` is published immediately
after the bot process takes its ending cumulative snapshots and before accepted work is quiesced.
It requires the measurement ready/release pair. This is the server/accounting stop boundary;
`--ready-file` remains the later correctness/quiescence boundary.

PF2 adds three deterministic explanatory scenarios:

- `quiet` sends no intentional gameplay commands after ordinary admission and exposes the inherent
  keepalive/ACK/background floor;
- `moving` keeps a spatially distributed population moving through the normal profile/control path,
  including bounded direction changes and jumps;
- `crowd` converges clients into the Welcome Plaza/Cabin area during warmup, then keeps the dense
  mutually relevant population moving during the measured window.

`mixed-chaos` remains the correctness/soak workload. Warmup and crowd convergence are excluded from
the measured-window counters. For example, a serious ten-minute warm-content crowd capture is:

```bash
dotnet run --project Tools/NetworkBots/UltraSim.NetworkBots.csproj \
  -c Release -p:UltraSimNetworkFlavor=ClientOnly -- \
  --endpoint=127.0.0.1:7777 \
  --fingerprint=<64-hex-deployment-key-fingerprint> \
  --bots=50 --seed=130363 --warmup=60s --duration=00:10:00 \
  --scenario=crowd --cache=/tmp/ultrasim-pf2-cache \
  --summary-file=/tmp/ultrasim-pf2-crowd.json
```

The schema-v3 PF2 report is a delta between cumulative snapshots taken around that explicit window.
It records exact UDP payload bytes/datagrams in each direction; Data/ACK/keepalive and retransmission
physical counters; logical message, application-payload, and data-frame bytes by lane and Game
family; per-client average/p50/p95/max rates; bounded one-second peak/p95 samples; and measured-window
bot CPU, current-RSS high-water, and pump interval/duration p50/p95/p99/max. Logical bytes are not
physical bytes and must not be added to the UDP total.

HTTP content counters are kept separately and mean response/archive/manifest/blob **body bytes**, not
complete HTTP/TCP wire cost. The report includes both the steady measurement-window delta and the
whole-run cold/warm cache facts so a one-time WorldPack download cannot be quoted as gameplay
bandwidth. A warm cache may avoid transfer, but every connection still performs a fresh application
session and content-application proof.

PF2.5 adds opt-in `Off`, `Basic`, and `Detailed` timing modes without changing the scenario,
protocol, cadence, or traffic definitions. `Off` takes no detailed stage timestamps. `Basic`
records bounded service intervals/durations. `Detailed` additionally records fixed-histogram
Network protocol stages, client semantic-apply/owner-state timings, authority/application/
ChunkPublication stages, exact per-message traffic, queue high-water, and process/GC context.
Schema-v4 reports use `checkpoint=PF2.5`. Distribution `Count`/`TotalStopwatchTicks` values are
cumulative for the measurement window; client/Game/native percentiles are computed over the last
256 bounded samples and retain a window-wide maximum. Network percentiles use 64 fixed
power-of-two Stopwatch-tick buckets and are conservative bucket upper bounds.

The maintained synchronized long-run helper starts one ordinary dedicated authority and one real
ClientOnly bot process, excludes admission/warmup from both measurement windows, and preserves the
raw bot/server summaries and logs:

```bash
Tools/NetworkBots/run-pf25-baseline.sh moving 600s \
  /tmp/ultrasim-pf25-moving-50 50 130363 60s \
  /tmp/ultrasim-pf25-shared-cache
```

The output directory must not already exist. The helper never changes the authority limit, AOI,
cadence, aggregation, compression, or wire schema; it selects `Detailed` diagnostics only.

The harness accepts 8–4,096 bots for the current `mixed-chaos` scenario and never starts a process
or dedicated thread per bot. Eight clients and a ten-second duration are the minimum because the
scenario assigns fixed reconnect, edge, telepad, gate, sword, mixed-body, and observer roles. The
current Concept Demo shared-authority profile separately admits at most 64 participants. Counts
above that are intentionally future characterization work: raising the authority's bounded
capacity requires its own measured checkpoint and is not hidden inside PF1. The admission deadline
includes the last configured staggered start plus the complete bounded admission window.

## `mixed-chaos`

The initial canonical scenario uses a PRNG stream derived independently from the global seed and
bot ID. It guarantees a deterministic acceptance prefix for movement, jumping, Local/Global Chat,
unmapped physical input, public sword contention and Inventory equip/drop, edge/fall pressure,
chunk/AOI transitions, and one clean reconnect. Later activity is bounded seeded chaos using only
ordinary client control, public-world, Chat, and UI contracts.

The process emits one compact evidence line plus optional schema-versioned JSON. `*Attempts` are
commands actually accepted into the client send path; `commandSubmitCalls` separately counts calls
which may meet local backpressure. Every queued movement/jump/interaction/Primary/UI-open command
ends Accepted, Rejected, or explicitly Cancelled under its original application epoch. A clean PF1
correctness run permits no cancellation.

Telemetry separately reports distinct chunks visited and all A→B→A crossings. Reconnect state spans
drain, delay, and the complete replacement connection/profile/content lifetime. Pump timing names
their actual scopes: start-to-start interval, central-pump execution duration, and maximum service
interval observed by any bot. Phase durations cover admission, scenario, quiescence, external
completion barrier, retirement, and total process work. CPU time and current-RSS high-water cover
that whole lifetime; current RSS is refreshed at a bounded 250 ms cadence rather than read from one
cached `Process` sample. These are load-generator diagnostics, not server execution or wire latency.

Initial discovery or handshake loss under a dense same-address admission wave is retried at most
eight times per bot with deterministic bounded delay. Once the scenario duration ends, the harness
stops creating new work and gives already-accepted reliable commands/proofs up to ten seconds to
quiesce before it checks for queue debt. Neither rule retries rejected Game actions or weakens the
clean-boundary assertion.

Unexpected transport/application/UI retirement faults an active bot immediately, including while
an external completion barrier is held. Final retirement covers every joining/active/draining state
and prevents late admission or reconnect. Chat observation uses one bounded recipient-sequence
cursor per bot and reports retention gaps instead of retaining delivery history. Two ordinary bot
observers also require the reconnect subject's exact old public identity and display metadata to
retire and its fresh named replacement to arrive; count equality alone is not accepted.

The maintained 50-client exploratory check uses `--admission-stagger-ms=250`; it has completed all
50 active clients plus one reconnect against a capacity-50 authority and cleanly retired every join.
This is a configuration/tool canary, not a measured CCU or production-performance claim.

Run the permanent mixed product gate with:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf1-only
```

Run the focused PF2 accounting gate with:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf2-only
```

That bounded gate uses real UDP plus two actual exported Online processes. The accepted long-window
PF2 baselines are recorded separately and are intentionally not rerun by every repository verify.

Run the focused PF2.5 timing/attribution gate with:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf2.5-only
```

It retains the same mixed 8-bot + 2-exported-Online product shape while requiring Detailed
authority, Network, client semantic-apply, native render/presentation, exact message, and resource
telemetry. The exported headless smoke submits one ordinary bounded movement/stop pair after the
measurement reset so its input-to-owner-state path is observed inside the synchronized window.

The accepted five-minute native measurement uses the same verifier path with a 60-second warmup
and preserves its cold summaries outside the temporary export scope:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf2.5-only --pf2.5-long-native \
  --pf2.5-evidence-output=/tmp/ultrasim-pf25-native-8-plus-2
```

The evidence output directory must not already exist. Neither long option changes the native
product, authority, scenario, cadence, or accounting path; it only extends the synchronized window
and copies the immutable final reports.
