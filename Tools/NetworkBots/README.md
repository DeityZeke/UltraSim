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

The harness accepts 1–4,096 bots and never starts a process or dedicated thread per bot. The current
Concept Demo shared-authority profile separately admits at most 64 participants. Counts above that
are intentionally future characterization work: raising the authority's bounded capacity requires
its own measured checkpoint and is not hidden inside PF1.

## `mixed-chaos`

The initial canonical scenario uses a PRNG stream derived independently from the global seed and
bot ID. It guarantees a deterministic acceptance prefix for movement, jumping, Local/Global Chat,
unmapped physical input, public sword contention and Inventory equip/drop, edge/fall pressure,
chunk/AOI transitions, and one clean reconnect. Later activity is bounded seeded chaos using only
ordinary client control, public-world, Chat, and UI contracts.

The process emits one compact evidence line plus optional structured JSON. Telemetry includes
lifetime state/current and high-water counts, application/proof/reconnect counts, attempted and
accepted actions, categorized rejections, pump iterations/lag, process working-set high-water, CPU
time, clean retirement, and bounded per-bot fault context.

Run the permanent mixed product gate with:

```bash
dotnet run --project _Tools/RepositoryVerification/UltraSim.RepositoryVerification.csproj \
  -c Release -- --pf1-only
```
