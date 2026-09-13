# UltraSim headless Network bots

`UltraSim.NetworkBots` is the maintained PF1/PF2/PF3/PF4A/PF4B/PF4C/PF4D load-generation and measurement client. One CLR process owns many
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

## PF3 cadence and performance-policy workloads

PF3 adds `sustained-input`, an explicit per-client high-rate producer which is separate from the
ordinary sparse `moving` workload. Each bot generates deterministic legal locomotion samples at
`--input-sample-hz`, while the shared Client Core scheduler emits at `--input-submit-hz` under the
authenticated server policy. Sample and submission rates are always reported as aggregate swarm,
per-client average, p50, p95, and maximum values. Schema-v5 reports also include whole-window
client/Network timing, worst-client IDs, scheduler coalescing, edge/release generation/submission/
completion, and server-observed rate-limit outcomes.

The accepted deterministic input modes are:

- `compliant` — use the stock bounded scheduler;
- `hard-limit` — stock scheduling at the advertised maximum;
- `over-limit` — ordinary Game messages above the maximum, expecting explicit `RateLimited` results;
- `burst-within` and `burst-over` — exercise the finite burst contract;
- `stale-session` — reconnect, then send one old application/control-lifetime command through the
  new ordinary secure transport and require it to remain fenced.

The long-run helper starts a ServerOnly dedicated authority and a ClientOnly bot process, warms and
synchronizes them, resets only diagnostic windows, then records real UDP/PF2.5 accounting:

```bash
Tools/NetworkBots/run-pf3-baseline.sh sustained-input 600s \
  /tmp/ultrasim-pf3-input-30 50 300301 60s \
  /tmp/ultrasim-pf3-cache 60 60 30 144 30 compliant 1
```

Its positional policy fields after cache are authority Hz, maximum input Hz, recommended input Hz,
producer sample Hz, requested submit Hz, input mode, Network service delay milliseconds, burst
capacity, and burst-window milliseconds. It
also accepts `quiet`, `moving`, `crowd`, and `mixed-chaos`; sample/submit/mode controls intentionally apply
only to `sustained-input`.

Run the bounded permanent gate with:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf3-only
```

For a separately recorded actual exported-client display experiment, use:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf3-native-only \
  --pf3-native-warmup-seconds=60 \
  --pf3-native-measurement-seconds=300 \
  --pf3-native-a=visible-default \
  --pf3-native-b=visible-vsync-off-60 \
  --pf3-native-evidence-output=/tmp/ultrasim-pf3-native
```

Other controlled native modes are `headless`, `visible-vsync-off-unlimited`, explicit off-VSync
30/60/120/240 caps, and `visible-vsync-on-60`. Native output reports actual runtime display/VSync/
max-FPS/refresh values. Headless numbers are main-loop cadence, not displayed FPS.

## PF4A action conservation and quiet-readiness measurements

Schema v6 (`--checkpoint=PF4A`) replaces ambiguous performance-evidence remainders with a finite
per-action stage ledger. For Movement, latest state conserves through scheduler admission,
coalescing, and submission. Jump, Interaction, Primary, and UI Open conserve through source
generation, admission/non-admission, submission, and authoritative Accepted/Rejected/RateLimited
completion. Release/Stop conserves through scheduler completion. Final evidence fails unless every
row is conserved, has zero unexplained work, and has zero pending unsent/completion debt.

Legacy `*Attempts` remain available with their historical meaning: a valid input observation was
accepted even if a held button produced no new rising edge. `actionOutcomes` is the authoritative
PF4A evidence surface and reports that case explicitly as `notAdmittedNoNewEdge`.

Run a matched PF4A capture with the normal PF3 profile and schema-v6 gate:

```bash
Tools/NetworkBots/run-pf4a-baseline.sh quiet 600s \
  /tmp/ultrasim-pf4a-quiet-50 50 400401 60s \
  /tmp/ultrasim-pf4a-shared-cache
```

The wrapper accepts the same positional policy fields as `run-pf3-baseline.sh`, selects PF4A
accounting, preserves raw bot/server/accounting summaries, and rejects non-conserved action rows.
For quiet runs, `RemoteApplications[].Timing.ReplicationReadiness` distinguishes O(1) clean commit
observations from dirty commits, membership examination, due work, debt/retry, gap refresh, and
source replacement. These counters are Game replication work, not Network wire messages.

The routine focused gate adds an 8-client real-UDP quiet canary requiring zero memberships examined,
zero schedule attempts, and zero deliveries, followed by the existing 8-bot + 2-actual-exported-
Online active canary:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf4a-only
```

The long matched runs and 30-minute soak are permanent evidence and intentionally are not repeated
by every repository verification.

## PF4B chunk-knowledge census

Schema v7 (`--checkpoint=PF4B`) preserves PF4A action conservation and adds bounded per-client
ChunkPublication delivery/proof diagnostics. `proofCensus` records exact generated/queued entries,
envelopes, status/kind, occupancy, pending depth, queue deferrals, and generation-to-queue latency.
`deliveryCensus` records applied Delta/Snapshot/Absence and duplicate, superseded, rejected,
capacity, contradictory, and malformed outcomes. Server accounting records the matching
per-application Delta-120, Proof-130, knowledge-advancement, and outstanding-ledger census.

Two explicit diagnostic workloads separate temporal and spatial churn:

```bash
Tools/NetworkBots/run-pf4b-baseline.sh single-hot 300s \
  /tmp/ultrasim-pf4b-single-hot 50 400504 60s /tmp/ultrasim-pf4b-cache

Tools/NetworkBots/run-pf4b-baseline.sh multi-hot 300s \
  /tmp/ultrasim-pf4b-multi-hot 50 400505 60s /tmp/ultrasim-pf4b-cache
```

`single-hot` holds 49 recipient anchors still and keeps one ordinary mover inside the authored flat
chunk `(0,1)`; every recipient retains that same actor/chunk lifetime. The maintained scenario uses
50 participants because its selected middle spawn is deliberately inside that authored control
chunk. `multi-hot` retains distributed ordinary movement. Neither scenario changes AOI, cadence,
protocol, or authority policy. The focused verifier runs a shorter real-UDP census plus the
8-bot + 2-actual-exported-Online headless main-loop canary:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf4b-only
```

## PF4C selected-profile bundling and cadence matrix

Schema v8 (`--checkpoint=PF4C`) preserves PF4A action conservation and PF4B knowledge/proof census,
then adds client message-129 bundle receipt/apply counts and semantic-application intervals. The
server report supplies per-tier source-to-due, due-to-Network-queue, source-to-Network-queue, and
source-to-proof authority-step distributions, plus exact bundle entry/byte/MTU and per-recipient
authority-step burst distributions.

The maintained wrapper is:

```bash
ULTRASIM_PUBLIC_REPRESENTATION=message129 \
ULTRASIM_PUBLIC_CADENCE=1/2/4 \
Tools/NetworkBots/run-pf4c-baseline.sh moving 600s \
  /tmp/ultrasim-pf4c-moving-50 50 500501 60s /tmp/ultrasim-pf4c-cache
```

Set `ULTRASIM_PUBLIC_REPRESENTATION=standalone` for the physical-representation control and
`ULTRASIM_PUBLIC_CADENCE=1/3/6` for the characterized cadence candidate. `ULTRASIM_TELEMETRY` may
select `Off`, `Basic`, or `Detailed`; PF4C evidence uses Detailed except for explicit bounded-memory
controls. The accepted production combination is message129 plus 1/2/4. Single-entry candidates
remain standalone and snapshots never enter message 129.

The focused real-UDP plus exported-product gate is:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf4c-only
```

## PF4D unreliable Network-frame aggregation

Schema v9 (`--checkpoint=PF4D`) retains the earlier action, knowledge, and Game-bundle evidence and
adds the explicit Network send policy plus per-peer aggregate histograms, MTU-fill buckets, lane
masks, and the Detailed immediate-ready opportunity census. The selected production policy is
cross-lane cap two. Cap one, four, eight, and same-lane cap two remain maintained measurement
controls; none changes the Game `ChunkPublication + Message129WhenBeneficial + 1/2/4` policy.

Use the PF4D wrapper with an explicit policy when capturing evidence:

```bash
ULTRASIM_UNRELIABLE_FRAME_CAP=1 \
ULTRASIM_UNRELIABLE_AGGREGATION_SCOPE=cross-lane \
Tools/NetworkBots/run-pf4d-baseline.sh moving 600s \
  /tmp/ultrasim-pf4d-moving50-scalar 50 604001 60s /tmp/ultrasim-pf4d-cache

ULTRASIM_UNRELIABLE_FRAME_CAP=2 \
ULTRASIM_UNRELIABLE_AGGREGATION_SCOPE=cross-lane \
Tools/NetworkBots/run-pf4d-baseline.sh moving 600s \
  /tmp/ultrasim-pf4d-moving50-cap2 50 604001 60s /tmp/ultrasim-pf4d-cache
```

Valid caps are `1`, `2`, `4`, and `8`; valid scopes are `same-lane` and `cross-lane`. Detailed
telemetry performs the non-mutating opportunity scan. Actual aggregation itself never waits for a
sibling and remains active with ordinary lower telemetry.

The focused gate exports the actual Online product and runs both a clean 8-bot + 2-native swarm and
an externally impaired real-UDP swarm:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf4d-only
```
