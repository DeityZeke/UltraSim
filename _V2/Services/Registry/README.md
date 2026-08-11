# UltraSim Registry Service

This folder contains the official discovery/registry service lifted from the
old v2 service tree.

The service is an ASP.NET Core registry process, separate from game servers and
client shells. It owns the public server list, registration leases, heartbeat
updates, optional polling, owner claims, admin lifecycle controls, host bans,
read caching, rate limits, and optional state persistence.

## Current Status

- The service source now lives under `Services/Registry`.
- It builds against the current `Discovery/UltraSim.Discovery.csproj`.
- The current discovery contracts were expanded with the registry DTOs and
  official query/publisher client types from v2.
- `ServerDiscoveryRow` keeps the current client-facing `Host` and `Port`
  fields while also carrying the registry fields from v2 such as lease id,
  lifecycle state, visibility, queue length, uptime, votes, and richer flags.
- Registry rows derive `Host` and `Port` from the advertised `ConnectHint`.

## Important Endpoints

- `POST /api/v1/servers/register`
- `PUT /api/v1/servers/{leaseId}`
- `DELETE /api/v1/servers/{leaseId}`
- `GET /api/v1/servers`
- `GET /api/v1/servers/{serverId}`
- `GET /api/v1/regions`
- `GET /api/v1/meta`
- `GET /healthz`
- `GET /api/v1/admin/runtime`
- `GET/PUT/DELETE /api/v1/admin/*` for lifecycle and moderation operations

## Pairing Plan

The current game project should pair with this service in small layers:

1. Server discovery publisher adapter
   - Add `Server/Discovery` runtime adapters that are not part of
     `Server/Core`.
   - Build advertised info from server-operation settings: discovery enabled,
     publish mode, registry URL, server id/name/description, bind/connect host,
     port, region, language, tags, max clients, password/whitelist flags, and
     compatibility/version labels.
   - Build status info from runtime snapshots: current players, max players,
     queue length when it exists, uptime, accepting-connections state, and
     health.
   - On server start, register or publish a heartbeat.
   - During normal runtime, heartbeat at a coarse cadence.
   - On stop/shutdown, send an offline/shutdown presence or unpublish.
   - Current status: settings-to-official-publication-plan construction exists
     in `ServerDiscoveryPublicationFactory`; console, headless, and Godot
     lifecycle wiring exists.

2. Server settings
   - Discovery server-operation settings are now exposed in the Godot server
     shell settings tab and loaded by the console/headless config path.
   - Keep bind address and port as next-start settings.
   - Keep discovery endpoint/name/description/region/language/tags as next-start
     settings for the default path; optional restart-discovery hooks can be
     added later without making them first-class buttons.

3. Status endpoint policy
   - The registry can work from pushed heartbeats alone.
   - If registry polling is enabled, the game server can expose the embedded
     `ServerDiscoveryStatusEndpointHost` from `Server/Discovery`.
   - The embedded host returns `ServerDiscoveryPollStatusResponse` and is wired
     into the console, headless, and Godot wrappers when
     `server.discovery.status_endpoint_url` is configured.
   - HTTP concerns stay in `Server/Discovery`; `Server/Core` only supplies the
     status snapshot.

4. Client official provider
   - Current status: `OfficialRegistryDiscoveryQuery` is wired into the Godot
     client shell through `ClientServerBrowserService`.
   - The official provider defaults to `http://127.0.0.1:5000`; use
     `--us-registry-url=...` or `ULTRASIM_REGISTRY_URL` to override it.
   - Keep manual/direct connect always available when the official registry is
     down.
   - Surface provider failures as friendly browser status text.

5. Validation
   - Unit-test advertised/status mapping with fake runtime snapshots.
   - Unit-test publisher cadence and stop/unpublish behavior with a fake
     `IServerDiscoveryPublisher`.
   - Unit-test official query URL construction and registry-unavailable
     behavior.
   - `_Tests/UltraSim.Registry.Tests` covers the shared discovery contracts from
     server publisher through official client query.
   - `_Tests/UltraSim.Registry.LaunchSmoke` starts `Services/Registry`, starts
     a server publisher, verifies the client browser can query the row over
     real HTTP, resolves the discovered row into a join endpoint, handshakes
     against the advertised non-default UDP port, and verifies
     unpublish-on-shutdown.

Run the launched-service smoke with:

```bash
PATH="$HOME/.dotnet:$PATH" dotnet run --project _Tests/UltraSim.Registry.LaunchSmoke/UltraSim.Registry.LaunchSmoke.csproj -c Release
```
