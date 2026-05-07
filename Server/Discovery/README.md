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
- Mapping and publisher cadence are covered by `UltraSim.Server.Tests`.

## First Integration Slice

- Add server-operation settings for discovery enabled, registry URL, publish
  mode, advertised host, metadata, heartbeat cadence, and unpublish-on-stop.
- Create `OfficialRegistryDiscoveryPublisher` from those settings.
- Wire the coordinator into headless, console, and Godot wrapper lifecycle.
- Keep direct connect independent of discovery.

## Later Slices

- Optional HTTP status endpoint for registry polling.
- LAN/mDNS provider and favorites/history providers.
- Owner-claim UI/tooling for official registry moderation.
- Discovery diagnostics in the Godot server shell.
