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

## First Integration Slice

- Keep direct connect independent of discovery.
- Add an integration smoke that starts `Services/Registry`, starts a discovery
  enabled server wrapper, and verifies the registry row.

## Later Slices

- Optional HTTP status endpoint for registry polling.
- LAN/mDNS provider and favorites/history providers.
- Owner-claim UI/tooling for official registry moderation.
- Discovery diagnostics in the Godot server shell.
