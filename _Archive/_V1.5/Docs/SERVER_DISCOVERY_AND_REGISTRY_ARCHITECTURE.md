# Server Discovery and Registry Architecture

Status: superseded by `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE_V2.md`.

This document is retained as the original lease/heartbeat-centric discovery design for historical reference.

Do not use this file as the current source of truth for new discovery, registry, polling, or lifecycle work.

Current source of truth:
- `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE_V2.md`

Purpose: define the discovery/tracker subsystem as a shared platform service for:
- the in-game multiplayer browser
- a public web server list
- a standalone launcher/gateway app
- future provider tabs such as LAN, favorites, history, Steam, and community registries

This doc is the source of truth for discovery design. The in-game menu is only one consumer.

Related docs:
- `Docs/SERVER_RUNTIME_ARCHITECTURE_AND_ROADMAP.md`
- `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`
- `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`

## 1. Core Design Rule

Discovery is a separate subsystem from core game runtime.

The server runtime should:
- produce canonical public metadata about itself
- produce live runtime status
- optionally publish that data to one or more discovery backends

The server runtime should not:
- be the public directory itself
- assume the in-game client is the only consumer
- hardcode one discovery backend as the only supported source

The client, website, and launcher should all consume the same normalized discovery model.

Discovery UX rule:
- unverified servers should still be visible by default
- verified and official are trust/presentation tiers, not separate ecosystems
- trust badges and filters should inform users without hiding community-hosted servers

## 2. Consumers

The same discovery API should support all of these without a second protocol:

1. In-game multiplayer menu
   - browse
   - filter
   - sort
   - join

2. Web server browser
   - public listing page
   - server detail pages
   - links that can launch the game client or launcher

3. Standalone launcher/gateway app
   - browse servers
   - maintain favorites/history
   - show compatibility/status
   - launch the client with a server target

4. Alternate providers
   - favorites
   - manual/direct connect
   - LAN
   - Steam
   - community registries

## 3. High-Level Architecture

Recommended split:

- `UltraSim/Server/`
  - owns runtime facts
  - owns publish hooks
  - knows nothing about browser/UI layout

- `UltraSim/Discovery/`
  - shared models
  - query/publish interfaces
  - provider-neutral row model
  - official registry client contracts
  - provider adapters for sources such as Steam/LAN/favorites/manual

- official registry service
  - persistent listing directory
  - lease/heartbeat tracking
  - verified/unverified listing state
  - query/filter/sort/pagination API

- consumers
  - in-game browser
  - website
  - launcher/gateway

## 4. One Registry, Multiple Trust Tiers

Do not build separate "top" and "bottom" registries.

Use one official registry with trust tiers on each listing.

### 4.1 Trust Levels

Recommended trust levels:

- `Unverified`
  - auto-registered listing
  - no claimed ownership
  - still visible and joinable
  - lease-based identity only

- `Verified`
  - published with an operator-owned token
  - stable server identity
  - reserved branding/name policy can apply

- `Official`
  - curated subset of verified listings
  - promoted/highlighted later

This gives one query path and one row model while still exposing trust clearly to users.

### 4.2 Why This Shape

This supports both desired behaviors:

- frictionless public listing
  - server checks `RegisterWithTrackerOnStartup`
  - if no verified credentials exist, it still appears as `Unverified`

- stronger ownership and trust
  - claimed servers publish as `Verified`
  - later, curated/promoted servers can be marked `Official`

Official is a presentation and policy designation, not a protocol distinction.

## 5. Canonical Models

## 5.0 Identity Model

The registry needs two different identities:

- `ServerId`
  - stable claimed identity, when one exists
  - used for verified servers, favorites continuity, launcher links, and detail pages

- `LeaseId`
  - current live listing presence record
  - always ephemeral
  - expires with the heartbeat lease

And one mutation capability:

- `HeartbeatToken`
  - capability token that allows the holder to refresh or terminate the current lease

This distinction matters because:
- a verified server should have a stable identity
- an unverified server may only have a temporary live presence
- a listing can expire and be recreated without implying long-term ownership

## 5.1 Server-Published Advertised Info

This is relatively stable public identity/capability metadata.

Required/important fields:
- `ServerId`
- `DisplayName`
- `ConnectTargetKind`
- `ConnectHint`
- `ProtocolVersion`
- `ContentVersion`
- `CompatibilityId`
- `IsPublic`
- `PasswordRequired`
- `WhitelistOnly`
- `Region`
- `PrimaryLanguage`
- `Languages[]`
- `Description`
- `Tags[]`
- `WebsiteUrl`
- `DiscordUrl`
- `MaxUsers`
- `Modded`
- `Visibility`
- `CapabilityFlags`

Notes:
- `CompatibilityId` is more useful than raw version strings for join filtering.
- `Region` should be server-declared, not geo-derived in v1.
- `PrimaryLanguage` should use standard codes such as `en`, `en-US`, `fr`, `de`.
- `Tags[]` are flexible metadata, not the primary filtering mechanism.
- Tags are descriptive, not authoritative.
- `ConnectTargetKind` tells consumers how to interpret `ConnectHint` without requiring string-shape guessing.

## 5.2 Server-Published Runtime Status

This is dynamic heartbeat data.

Fields:
- `IsAcceptingConnections`
- `CurrentUsers`
- `PeakUsers`
- `Rolling24HourUsers`
- `Uptime`
- `Status`
- `QueueLength`
- `LastHeartbeatUtc`

Optional later:
- `MapName`
- `RulesetLabel`
- `RestartCountdown`
- `AveragePingBucket`

## 5.3 Consumer-Measured Data

These are not server-authoritative.

Examples:
- `PingMs`
- local route quality
- `IsFavorite`
- `LastPlayedUtc`
- installed-content compatibility state

Keep these separate from server-published facts.

## 5.4 Normalized Discovery Row

Every provider should normalize into one consumer row shape.

Target fields:
- `ProviderId`
- `ProviderKind`
- `ServerId`
- `LeaseId`
- `DisplayName`
- `TrustLevel`
- `Visibility`
- `ConnectTargetKind`
- `Region`
- `PrimaryLanguage`
- `CurrentUsers`
- `MaxUsers`
- `PeakUsers`
- `QueueLength`
- `Uptime`
- `PasswordRequired`
- `WhitelistOnly`
- `PingMs`
- `IsFavorite`
- `ConnectHint`
- `CompatibilityId`
- `VersionLabel`
- `Tags[]`
- `CapabilityFlags`

The in-game browser, website, and launcher should render from this model.

This row should stay compact.

Do not overload the primary list row with every interesting metric. Richer metrics belong on a server detail surface.

## 5.5 Server Detail Model

Server detail views may expose additional metrics and metadata that are too heavy for the primary list row.

Examples:
- `Rolling24HourUsers`
- `Rolling24HourPeakUsers`
- `HistoricalPeakUsers`
- `AveragePingMs`
- `UptimePercentage`
- `LastHeartbeatUtc`
- `MapName`
- `RulesetLabel`
- `RestartCountdown`
- banner/image assets
- richer description text
- provider-specific metadata where appropriate

Design rule:
- list rows should optimize for scanning and filtering
- detail views should optimize for inspection and decision-making

## 6. Structured Fields vs Tags

Use both.

Structured fields are for reliable filtering:
- region
- primary language
- password required
- whitelist only
- trust level
- compatibility id
- max users/current users
- modded

Tags are for ecosystem flexibility:
- roleplay
- hardcore
- pvp
- pve
- seasonal
- custom-map

Do not depend on tags for basic filters like region or language.

## 6.1 Visibility Modes

Listing visibility should be distinct from trust.

Recommended values:
- `Public`
  - appears in browse results
- `Unlisted`
  - does not appear in normal browse results
  - still joinable by direct link, `ServerId`, or launcher handoff
- `Private`
  - intended for invite-only/private use
  - may still use registry-backed metadata internally, but not public discovery
- `Disabled`
  - not published to the registry

`PublicVisible = true/false` is too small for the long-term design. The discovery model should use explicit visibility modes.

## 6.2 Client Compatibility State

The discovery payload should not only expose raw compatibility fields; consumers should also have a shared interpretation model.

Recommended client-side compatibility states:
- `Joinable`
- `VersionMismatch`
- `MissingContent`
- `UnknownCompatibility`
- `LauncherRequired` (optional later)

The registry may expose raw fields such as:
- `ProtocolVersion`
- `ContentVersion`
- `CompatibilityId`

But browser, launcher, and game client should converge on a shared compatibility-state decision so the same server is presented consistently across all consumers.

## 7. Server Host Settings

These should be public-facing host/runtime settings:

- `RegisterWithTrackerOnStartup`
- `PublicVisible`
- `PublicDisplayName`
- `PublicDescription`
- `PublicRegion`
- `PublicPrimaryLanguage`
- `PublicLanguages[]`
- `PublicTags[]`
- `PublicWebsiteUrl`
- `PublicDiscordUrl`
- `PasswordRequired`
- `WhitelistOnly`
- `PublicMaxUsers`
- `PublicCompatibilityId`
- `PublicContentVersion`
- `PublicProtocolVersion`
- `PublicVisibility`
- `DiscoveryPublishMode`
  - `Disabled`
  - `Unverified`
  - `Verified`

If `RegisterWithTrackerOnStartup = true` and no verified credentials are configured:
- publish as `Unverified`

If verified credentials are configured:
- publish as `Verified`

## 7.1 Capability Flags

Capability flags should exist as an explicit extensibility point, even if the first implementation only uses a small subset.

Candidate flags:
- `SupportsDirectConnect`
- `SupportsPassword`
- `SupportsGatewayAuth`
- `SupportsCharacterCreationAtJoin`
- `SupportsMods`
- `RequiresLauncher`
- `SupportsVoice` (future)

These are useful for:
- launcher behavior
- browser filtering
- future join/handshake UX
- server detail pages

## 8. Official Registry API

Use JSON over HTTPS first.

This is the simplest cross-platform choice for:
- game client
- launcher
- browser
- PHP backend/proxy
- community tools

Do not start with a custom binary protocol.

### 8.1 Server Publish API

Recommended endpoints:

- `POST /api/v1/servers/register`
  - creates or refreshes a listing lease
  - request includes:
    - publish mode
    - advertised info
    - publisher auth, if present
  - response includes:
    - `lease_id`
    - `heartbeat_token`
    - `expires_at`
    - effective `trust_level`
    - effective `visibility`

- `PUT /api/v1/servers/{lease_id}/heartbeat`
  - updates runtime status
  - may also update mutable advertised fields

- `DELETE /api/v1/servers/{lease_id}`
  - unregisters on shutdown

### 8.2 Query API

Recommended endpoints:

- `GET /api/v1/servers`
  - list servers
  - supports filter/sort/pagination

- `GET /api/v1/servers/{server_id}`
  - detail view

- `GET /api/v1/meta`
  - registry metadata
  - protocol compatibility hints
  - maybe client min version info

- `GET /api/v1/regions`
  - optional convenience endpoint

## 9. Publish Authentication and Ownership

Unverified listings should be easy.
Verified listings should be authenticated.

### 9.1 Unverified

No operator account required.

Server publishes with:
- mode = `Unverified`
- normal public metadata
- no ownership token

Registry grants:
- ephemeral lease
- generated lease id
- generated heartbeat token
- trust level = `Unverified`

These listings should be rate-limited and lease-bound.

### 9.2 Verified

Verified listing requires operator ownership.

Recommended v1:
- `PublicServerId`
- `DiscoveryPublishToken`

Registry validates the pair and grants:
- stable identity
- trust level = `Verified`

Recommended later:
- operator account
- claimed server records
- token rotation
- ownership transfer

### 9.3 Official

Do not make `Official` a separate technical publish path.

It should be a registry-side designation on verified listings.

That allows the same listing to appear in:
- all servers
- verified only
- official only

## 10. Lease and Heartbeat Model

Registry entries should be leases, not permanent rows.

Recommended defaults:
- heartbeat every `15s`
- expiry after `45s`

Benefits:
- simple cleanup
- simple stale/offline handling
- resilient to crashes

Registry can either:
- hide expired rows immediately
- or temporarily show them as stale/offline

V1 recommendation:
- hide expired rows from public list

## 10.1 ConnectHint Semantics

`ConnectHint` should remain flexible.

Consumers should not have to infer the target type from string format alone.

Recommended companion field:
- `ConnectTargetKind`
  - `Direct`
  - `Dns`
  - `Launcher`
  - `Relay`

It may contain:
- direct `host:port`
- DNS host and port
- launcher/deep-link target
- future relay or indirect connection token

Do not overconstrain it to only raw IPs.

This preserves room for:
- direct dedicated servers
- DNS-backed community servers
- launcher handoff
- future proxy/relay models

V1 implementation guidance:
- keep `ConnectHint` string-based
- add `ConnectTargetKind`
- avoid a more complex union/typed payload until it is actually needed

If Steam-backed discovery is added later, extend `ConnectTargetKind` with:
- `Steam`

This keeps browser/list rendering unified while allowing provider-aware join behavior.

## 11. Abuse and Spam Controls

Unverified listing is useful, but it needs guardrails.

Recommended protections:
- rate limit register/heartbeat by IP/subnet
- cap active unverified listings per IP/subnet
- add a re-registration cooldown window for the same IP/subnet after lease creation/expiry
- normalize and validate display names
- basic profanity/spam filtering
- length limits on description/tags
- heartbeat lease expiry
- maybe lower metadata privileges for unverified rows

Optional later:
- CAPTCHA or web claim flow for promotion to verified

## 11.1 Moderation and Delisting

Because the registry is public infrastructure, it needs lightweight governance primitives.

Recommended capabilities:
- delist abusive or deceptive names/descriptions
- temporarily mute or block specific publishers
- quarantine suspicious unverified listings
- revoke or downgrade abused verified identities
- reserve official/verified naming policy where necessary

This should be treated as registry policy and operations, not as a special case in the game client.

## 12. Client Query Behavior

The game client should consume a real HTTP-backed official query implementation, not a hardcoded stub list.

Provider model remains:
- Official
- Favorites
- LAN
- Manual
- later Steam / History / Community

The official provider should:
- query the registry
- normalize rows
- optionally overlay client-measured ping

The browser UI should support filtering by:
- trust level
- region
- language
- compatibility
- password required
- whitelist only

Unverified listings should still be visible by default. Verified/Official should be badges and filters, not hard discoverability gates.

## 12.1 Provider Aggregation Strategy

Different discovery sources should normalize into the same row model, but they do not need to be merged into one visible list immediately.

Recommended rollout:
- start with separate provider tabs/categories:
  - `Official`
  - `Steam`
  - `LAN`
  - `Favorites`
  - `Manual`
- add an aggregated `All` view later only if it is actually useful

Why:
- duplicate entries are likely if the same server appears in multiple providers
- connect semantics may differ by provider
- trust semantics may differ by provider
- debugging and support are simpler when sources remain distinct at first

If an aggregated `All` view is added later:
- dedupe by stable `ServerId` first when available
- fallback dedupe by normalized connect target
- preserve provider badges so one row can indicate multiple source backings

## 13. Web and Launcher Consumers

The same public registry API should support:

### 13.1 Web Browser

- searchable list page
- server detail page
- filters
- maybe shareable URLs by `server_id`

The web browser should use:
- compact rows in list/search results
- richer server detail pages for long-form metadata and historical aggregates

### 13.2 Standalone Launcher / Gateway

- list servers
- favorites/history
- compatibility checks
- launch the installed client
- support provider-aware connect behavior where needed, such as Steam-backed join paths

Do not create separate tracker protocols for these consumers unless proven necessary.

## 14. Launch / Deep Link Strategy

A website cannot reliably start a native client without a protocol handler or launcher bridge.

Recommended approach:
- register a custom URI scheme, for example:
  - `ultrasim://connect?host=1.2.3.4&port=42042&serverId=my-server`

Optional launcher-specific scheme:
- `ultrasim-launcher://server/my-server`

This supports:
- "Join" buttons on the website
- launcher handoff
- future desktop integration

Do not try to make browser-side PHP "launch the client" directly. Use a URI handler.

## 14.1 Reputation / Voting

The registry now supports a minimal voting projection, while the portal remains authoritative.

Current implemented baseline:
- portal stores vote records
- portal pushes projected `VoteCount` updates into the registry
- registry persists projected `VoteCount` alongside the listing for consumer-facing sort/display

Core rules:
- no free-form anonymous vote endpoint
- account-bound identity required
- one effective vote per account per server at a time
- vote changes should replace the prior vote, not stack
- anti-abuse and anti-bot controls are mandatory

Recommended shape:
- server reputation is a separate subsystem layered on top of discovery
- list rows may show a simple aggregate signal
- full vote/reputation details belong on the server detail page, not the main row

Possible aggregate signals later:
- upvote/downvote score
- recommendation percentage
- total verified votes

Reasonable next extension:
- have the portal compute projected `VoteCount` from votes belonging to accounts active inside a configurable activity window
- keep the registry unaware of account activity details and treat `VoteCount` as a cached projection only

Do not let voting become the primary trust model.
Verified/unverified/official and operational metrics should remain separate from social reputation.

## 15. LAN / Favorites / Manual / Steam

These remain separate providers behind the same row model.

### 15.1 Favorites
- local storage
- overlays `IsFavorite`
- can reference verified `ServerId` or fallback `ConnectHint`

### 15.2 History
- local storage
- recently joined endpoints/listings

### 15.3 Manual
- raw host/port entries

### 15.4 LAN
- likely UDP broadcast/multicast or mDNS-style discovery later
- normalized into the same row shape

### 15.5 Steam
- adapter provider later
- same consumer row model
- should be implemented as a provider/source adapter, not a separate browser architecture
- may use Steam-specific listing ids, lobby ids, or join metadata internally
- should start as its own provider tab/category
- may later participate in an aggregated `All` view once dedupe and provider badges exist

## 16. What We Should Avoid

Avoid:
- separate registries for verified and unverified servers
- separate browser architectures for Steam vs non-Steam discovery
- coupling the public API to the exact in-game UI text
- forcing every public server operator through manual claim flow before listing
- treating client ping as server-published data
- relying only on tags for core filters
- using custom binary discovery protocols before HTTP/JSON is exhausted

## 17. Current Repo Status

Already implemented:
- `UltraSim/Discovery/` contains base discovery contracts:
  - `ServerAdvertisedInfo`
  - `ServerDiscoveryStatusInfo`
  - `ServerDiscoveryRow`
  - `IServerDiscoveryPublisher`
  - `IServerDiscoveryQuery`
  - `InMemoryServerDiscoveryQuery`
- `ServerRuntime` exposes:
  - advertised info
  - live discovery status info
  - publish/unpublish hooks for attached publishers
- client multiplayer browser already consumes normalized discovery rows through provider abstractions

Still missing:
- official registry service
- official HTTP publisher
- official HTTP query client
- trust level model
- visibility model
- verified/unverified publish flow
- real filters/sorts/pagination contract
- web browser consumer
- launcher/deep-link consumer
- LAN/favorites/history persistence beyond the current stub level

## 18. Recommended Implementation Order

### Phase 1 - Expand the shared discovery model
- add trust-level support
- add region/language/max-users/compatibility fields
- add visibility support
- add capability flags
- add public host settings for discovery metadata
- extend row model for real filters

### Phase 2 - Official registry MVP
- implement ASP.NET Core registry service
- implement a minimal `GET /api/v1/servers` query endpoint early
- implement unverified and verified publish flows
- implement listing visibility handling
- implement lease/heartbeat handling
- expand `GET /api/v1/servers` with real filtering/sorting/pagination

### Phase 3 - Game client integration
- implement `OfficialRegistryDiscoveryQuery`
- remove stub official rows from `ClientAppShell`
- render trust/region/language fields in multiplayer browser

### Phase 4 - Web and launcher support
- add server-detail endpoint
- add custom URI scheme
- add basic web listing page or sample consumer
- add launcher/gateway consumer path

### Phase 5 - Additional providers
- favorites/history persistence
- LAN provider
- Steam/community adapters
- optional aggregated `All` view with dedupe after provider-backed sources are stable

## 19. Design Rules To Preserve

- Discovery is a subsystem, not a menu feature.
- One registry, one row model, multiple trust levels.
- Verified and unverified are publication modes, not separate systems.
- Official is a designation on top of verified, not a separate protocol.
- HTTP/JSON first.
- Keep server-published and client-measured fields separate.
- Make public listing easy, but not anonymous and unbounded forever.
