# Server Discovery Adapters

`Server/Discovery` is the future home for game-server discovery publishing and
status endpoint adapters.

This layer should translate the engine-agnostic server runtime into discovery
contracts without moving registry, HTTP, or portal details into `Server/Core`.

## Current Status

- `UltraSim.Server.Discovery.csproj` exists and references only `Server/Core`
  plus shared `Discovery` contracts.
- `ServerDiscoveryContractBuilder` maps explicit advertised settings into
  `ServerAdvertisedInfo`.
- The same builder maps `ServerStatusSnapshot` into
  `ServerDiscoveryStatusInfo` and `ServerDiscoveryPollStatusResponse`.
- `ServerDiscoveryPublisherCoordinator` owns register/startup presence,
  heartbeat cadence, shutdown presence, optional unpublish, and disabled-mode
  behavior over an injected `IServerDiscoveryPublisher`.
- Headless, console, and Godot shell options now carry discovery
  server-operation settings loaded through the shared settings config path.
- The Godot server shell settings tab exposes discovery enabled, registry URL,
  publish mode, advertised host, metadata, heartbeat cadence, unpublish-on-stop,
  and optional status endpoint URL.
- `ServerDiscoveryPublicationFactory` turns server-operation discovery settings
  into official-registry publisher plans: registry URI, publish mode, injected
  `OfficialRegistryDiscoveryPublisher`, advertised settings, and coordinator
  options.
- Console, headless, and Godot wrappers create a discovery coordinator when discovery is
  enabled, publish startup when the runtime first reaches `Running`, heartbeat
  while running, and publish shutdown/unpublish after stop. Discovery failures
  are logged and do not stop the server.
- Mapping and publisher cadence are covered by `UltraSim.Server.Tests`.
- Console/headless config loading for discovery settings is covered by
  `UltraSim.Server.Tests`.
- Official-registry publication plan construction is covered by
  `UltraSim.Server.Tests`.
- The embedded status endpoint host is covered by `UltraSim.Server.Tests` and
  is wired into console, headless, and Godot server wrappers.
- The Godot server shell has a read-only Discovery tab for publisher state,
  registry URL, advertised target, status endpoint state, last publish time, and
  latest discovery message.
- `ServerLanDiscoveryResponderHost` answers shared UDP LAN discovery queries
  with a server discovery row. The protocol and loopback client/server exchange
  are covered by `UltraSim.Server.Tests`; console/headless/Godot lifecycle
  wiring is still pending.
- `_Tests/UltraSim.Registry.Tests` covers the shared contract loop from server
  publisher through official client query.
- `_Tests/UltraSim.Registry.LaunchSmoke` starts the real registry service over
  HTTP, publishes a server row, queries it through the official client provider,
  resolves the discovered row into a direct-connect request, verifies a live UDP
  handshake against the advertised non-default port, and verifies
  unpublish-on-shutdown.

## Smoke Commands

```bash
PATH="$HOME/.dotnet:$PATH" dotnet run --project _Tests/UltraSim.Registry.LaunchSmoke/UltraSim.Registry.LaunchSmoke.csproj -c Release
```

## Later Slices

- Wire `ServerLanDiscoveryResponderHost` into console, headless, and Godot
  server lifecycles.
- mDNS/zeroconf provider.
- Owner-claim UI/tooling for official registry moderation.
