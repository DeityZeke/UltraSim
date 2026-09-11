# UltraSim headless Network bots

`UltraSim.NetworkBots` is the maintained PF1 load-generation client. One CLR process owns many
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
`--scenario`, and `--cache`. `--verbose` prints the bounded diagnostic context of a failed bot.
File barriers and JSON summary options are acceptance-coordination surfaces used by the repository
verifier, not gameplay shortcuts.

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
