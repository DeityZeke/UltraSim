# Server Discovery and Registry Architecture V2

Status: current source of truth for discovery, registry, polling, listing lifecycle, and owner-claim verification design.

Supersedes:
- `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE.md`

Related docs:
- `Docs/SERVER_RUNTIME_ARCHITECTURE_AND_ROADMAP.md`
- `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`
- `Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`
- `Docs/WEB_PORTAL_AND_ACCOUNT_ARCHITECTURE.md`
- `Docs/ADMIN_ROLES_AND_FORUMS_ARCHITECTURE.md`
- `Docs/WEB_PORTAL_MVP_IMPLEMENTATION_PLAN.md`

## 1. Purpose

Define discovery as a shared platform subsystem for:
- the in-game multiplayer browser
- a public web server list
- a standalone launcher/gateway app
- future provider tabs such as LAN, favorites, history, Steam, and community registries

This document replaces the earlier lease-only tracker model with a hybrid registration + polling design.

## 2. Core Design Rule

Discovery is a separate subsystem from core game runtime.

The server runtime should:
- expose canonical public metadata about itself
- expose lightweight runtime status for polling
- register/update itself with one or more discovery backends

The server runtime should not:
- be the public directory itself
- assume the in-game client is the only consumer
- hardcode one browser-specific presentation shape

The client, website, and launcher should all consume the same normalized discovery model.

## 3. High-Level Direction

V2 uses a hybrid model:
- `push` for registration, identity, trust, metadata changes, and explicit startup/shutdown transitions
- `pull` for liveness and runtime status

This is intentionally different from the original lease-only design.

Why:
- pure push is brittle when the registry restarts or publisher loops fail silently
- pure pull is weak for identity, ownership, and rich metadata updates
- hybrid keeps ownership explicit while making runtime presence more resilient

## 4. Consumers

The same registry API should support all of these without a second protocol:

1. In-game multiplayer menu
   - browse
   - filter
   - sort
   - join

2. Web server browser
   - public listing page
   - server detail pages
   - launch/deep-link handoff

3. Standalone launcher/gateway app
   - browse servers
   - favorites/history
   - compatibility checks
   - launch the installed client

4. Alternate providers
   - favorites
   - manual/direct connect
   - LAN
   - Steam
   - community registries

## 5. Platform Shape

Recommended split:

- `UltraSim/Server/`
  - owns runtime facts
  - owns status endpoint
  - owns registration/update hooks
  - knows nothing about browser/UI layout

- `UltraSim/Discovery/`
  - shared discovery contracts
  - row/detail models
  - registry client contracts
  - provider-neutral query interfaces

- official registry service
  - persistent registered server records
  - trust/visibility/lifecycle policy
  - poll scheduler and observation store
  - query/filter/sort/pagination API

- consumers
  - in-game browser
  - website
  - launcher/gateway

## 6. Push vs Pull Responsibilities

### 6.1 Push Responsibilities

Push exists for:
- initial temporary/unverified registration
- claim/promotion registration updates from the portal
- metadata updates
- visibility updates
- explicit startup signal
- explicit shutdown/offline signal

Push is the source of truth for:
- `ServerId`
- declared display/branding metadata
- portal-linked ownership references, when a listing is claimed
- operator intent

### 6.2 Pull Responsibilities

Pull exists for:
- liveness
- runtime status
- current user counts
- uptime
- fine-detail and historical metrics later
- ownership proof confirmation for claimed listings

Pull is the source of truth for:
- most live health and availability state
- recent observation timestamps
- service-side latency samples
- claimed-listing verification outcome

### 6.3 Why Not Pure Push

Pure push has these failure modes:
- registry restart loses in-memory presence
- silent publisher/loop failure delists healthy servers
- reconnect behavior becomes tightly coupled to server runtime internals

### 6.4 Why Not Pure Pull

Pure pull has these weaknesses:
- no clean ownership/verification story
- no strong metadata publication path
- harder startup/shutdown transitions
- public-NAT/firewall limitations become more painful

### 6.5 Source-of-Truth Precedence

Source of truth should be explicit:

- registration push is authoritative for:
  - identity
  - temporary listing creation
  - claimed listing metadata and owner reference
  - declared branding/metadata
  - operator visibility intent
- poll responses are authoritative for:
  - runtime health
  - liveness
  - current runtime observation timestamps
  - registry-observed availability timing
  - claim/verification proof confirmation
- explicit startup/shutdown/offline push may accelerate transitions:
  - it should affect health immediately
  - it should not replace polling as the long-term health authority
- client-measured fields are overlays only:
  - they must not rewrite registry truth

## 7. Identity and Registration Model

The registry should persist registered server records.

### 7.1 Stable Identity

- `ServerId`
  - public-facing stable server identity
  - stable for verified servers
  - may also be assigned to unverified records, but without the durability or ownership guarantees of verified identities
  - used in browser rows, detail pages, launcher links, and favorites when possible

- `RegistrationId`
  - registry-internal persistent record key
  - survives service restart
  - not intended to be the main public identity

### 7.2 Visibility Intent

Visibility intent is separate from health and lifecycle state.

Recommended values:
- `Public`
  - appears in normal browse results when lifecycle and health policy allow it
- `Unlisted`
  - hidden from normal browse results
  - still reachable by direct link, `ServerId`, or launcher handoff
- `Private`
  - not part of normal public discovery
  - may still use registry-backed metadata or internal tools where useful

### 7.3 Registration Modes

- `Unverified`
  - frictionless temporary listing
  - server can appear without any portal account or claim flow
  - lower retention and stricter cleanup
  - may later be claimed/promoted through the web portal

- `Verified`
  - requires a claimed listing with ownership proof
  - long retention
  - stable identity continuity

- `Official`
  - registry-side designation layered on verified records

### 7.4 Claim and Promotion Flow

Verified ownership is a promotion path, not the only listing path.

Recommended flow:
1. server starts and may appear as temporary `Unverified`
2. operator signs into the web portal
3. operator goes to `My Servers` / `Add Server`
4. operator submits metadata plus the server's ownership proof/token
5. registry polls the target server
6. if the proof matches:
   - the listing is claimed and promoted to `Verified`
   - the listing is associated with the portal account
7. if the proof fails:
   - the listing remains `Unverified`
   - the portal shows a verification error

This preserves both:
- frictionless public discovery
- explicit owner-backed promotion to verified trust

### 7.5 Ownership Token Lifecycle

The server ownership token is a server-side secret used only for listing claim verification.

Recommended behavior:
- auto-generate a random token on first boot if none exists
- persist it locally on the server
- allow explicit regeneration/rotation
- expose the raw token only through the server-side admin surface or console output
- publish only the token hash over discovery channels

Important rules:
- do not derive the token from public metadata such as name, IP, or port
- token rotation should invalidate previous verification until the portal and poll proof match again
- the registry may mark the claim as pending revalidation as soon as it sees that the server is publishing a different ownership-proof hash
- the next successful poll remains the authoritative re-verification point
- the portal should not persist the raw ownership token at rest after forwarding it to the registry; it is a server secret, not portal-owned state

## 8. Canonical Models

## 8.1 Server-Published Advertised Metadata

Relatively stable public metadata.

Examples:
- `ServerId`
- `DisplayName`
- `Description`
- `ConnectTargetKind`
- `ConnectHint`
- `ProtocolVersion`
- `ContentVersion`
- `CompatibilityId`
- `Region`
- `PrimaryLanguage`
- `Languages[]`
- `Tags[]`
- `WebsiteUrl`
- `DiscordUrl`
- `MaxUsers`
- `PasswordRequired`
- `WhitelistOnly`
- `Visibility`
- `CapabilityFlags`

## 8.2 Polled Runtime Status

Lightweight runtime/status data exposed by the server and collected by the registry.

Examples:
- `IsAcceptingConnections`
- `CurrentUsers`
- `PeakUsers`
- `Uptime`
- `Status`
- `QueueLength`
- `LastUpdatedUtc`

Optional later:
- `Rolling24HourUsers`
- `Rolling24HourPeakUsers`
- `MapName`
- `RulesetLabel`
- `RestartCountdown`

## 8.3 Consumer-Measured Data

Not server-authoritative.

Examples:
- `PingMs`
- client route quality
- `IsFavorite`
- `LastPlayedUtc`
- installed-content compatibility state

Keep this separate from both pushed metadata and registry-polled runtime status.

## 8.4 Normalized Discovery Row

All providers should normalize into one row shape.

Target fields:
- `ProviderId`
- `ProviderKind`
- `ServerId`
- `DisplayName`
- `TrustLevel`
- `HealthState`
- `LifecycleState`
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
- `LastSeenUtc`

The row should remain compact.

## 8.5 Server Detail Model

Detail views may expose:
- richer description text
- banner/image assets
- rolling/historical user counts
- recent registry-observed history samples
- aggregate history summaries derived from recent samples
- true per-server 24h / 7d / 30d metrics derived from persisted hourly aggregate samples
- per-server all-time peak player tracking
- uptime percentage
- service-side latency aggregates
- last successful poll
- provider-specific metadata where appropriate

Admin/runtime views may also expose:
- aggregate metrics derived from retained registry samples across all listings
- retained average and peak players
- retained online/offline ratio
- retained aggregate poll latency
- true 24h / 7d / 30d platform metrics derived from persisted hourly aggregate samples
- all-time peak player totals tracked independently of the retained sample window

## 9. State Model V2

Do not use one enum for health, visibility, and retention.

Split them.

### 9.1 Health State

Recommended values:
- `Pending`
- `Online`
- `Stale`
- `Offline`

Meaning:
- `Pending`
  - registered, but not yet confirmed by poll
- `Online`
  - recent successful poll
- `Stale`
  - recent failure or incomplete confidence; grey state
- `Offline`
  - explicit offline signal or repeated poll failure; red state

### 9.2 Lifecycle State

Recommended values:
- `Active`
- `Delisted`
- `Disabled`

Meaning:
- `Active`
  - normal record, eligible for normal browse visibility
- `Delisted`
  - hidden automatically by inactivity policy, but retained
- `Disabled`
  - intentionally hidden by operator, admin, or moderation policy

### 9.3 Purge

`Deleted` is not a normal runtime state.

It is a purge action that happens after the retention window expires.

## 10. Activity and Transition Rules

### 10.1 Activity Definition

`No activity` means:
- no startup/register push
- no metadata/state push
- no successful poll

This must be the definition used for delisting and purge policy.

### 10.2 Health Transitions

Recommended behavior:
- startup/register push
  - move to `Pending` immediately
  - first successful poll moves to `Online`
- one missed poll
  - move to `Stale`
- three consecutive missed polls
  - move to `Offline`
- explicit shutdown/offline push
  - move immediately to `Offline`
- successful poll after stale/offline
  - move back to `Online`

### 10.3 Lifecycle Transitions

Recommended behavior:
- no activity beyond policy threshold
  - move from `Active` to `Delisted`
- explicit operator/admin disable
  - move to `Disabled`
- successful re-registration or valid new activity
  - verified records may move from `Delisted` back to `Active`

## 11. Retention Policy

Retention should differ for verified and unverified records.

### 11.1 Verified / Registered

Recommended:
- `Stale`
  - after 1 missed poll
- `Offline`
  - after 3 missed polls or explicit offline push
- `Delisted`
  - after 30 days of no activity
- purge
  - after 30 additional days in delisted state

Design goal:
- preserve identity, metadata, historical metrics, and ranking continuity through long outages

### 11.2 Unverified / Unregistered

Recommended:
- `Stale`
  - after 1 missed poll
- `Offline`
  - after 3 missed polls or explicit offline push
- `Delisted`
  - after 12 hours of no activity
- purge
  - after 24 additional hours in delisted state

Design goal:
- keep public listing easy
- aggressively clean up abandoned or temporary casual listings

## 12. Polling Contract

The registry should poll only registered public servers.

### 12.1 Poll Endpoint

Recommended server endpoint:
- `GET /discovery/status`

It should return small, fast, runtime-focused JSON.

Server host startup should also log discovery availability explicitly:
- `Discovery - Enabled - Port: <port>`
- `Discovery - Disabled`

This should be emitted once during startup so operators can verify whether registry polling is expected to work without relying on verbose debug logs.

Candidate fields:
- `ServerId`
- `Status`
- `IsAcceptingConnections`
- `CurrentUsers`
- `PeakUsers`
- `Uptime`
- `LastUpdatedUtc`
- `ProtocolVersion`
- `ContentVersion`
- `CompatibilityId`
- `OwnershipTokenHash`

### 12.2 Poll Cadence

Recommended defaults:
- development: `15s`
- production: start at `60s`, tune later from observed load

Implementation note:
- registry polling should use bounded concurrency rather than a purely sequential sweep
- poll timeout should stay tight and should not be used as the primary freshness control

### 12.3 Poll Scope

Poll is for:
- liveness
- runtime state
- observation timestamps
- registry-observed poll latency / availability timing
- claimed-listing ownership proof confirmation

Push may signal that ownership proof changed and revalidation is needed, but the poll result remains authoritative for success/failure.

### 12.4 Poll Target Safety

Registry polling should treat the configured status endpoint as untrusted input.

Recommended rule:
- reject obviously local/private poll targets at registration time
- re-check resolved DNS addresses at poll time before any outbound request is sent
- fail closed if DNS resolution fails or resolves to loopback, link-local, private, or otherwise blocked ranges

This prevents the registry poller from becoming an SSRF bridge into the registry host's local network context.

## 13. Registry API Shape

### 13.1 Registration / Metadata

Recommended endpoints:
- `POST /api/v1/servers/register`
- `PUT /api/v1/servers/{server_id}/metadata`
- `POST /api/v1/servers/{server_id}/presence`
  - for explicit startup/shutdown/offline transitions
  - this is the normal path for immediate health transitions
- `DELETE /api/v1/servers/{server_id}`
  - exceptional unregistration/removal of the record
  - this is not the normal graceful shutdown path for verified servers
- `POST /api/v1/servers/claims`
  - portal-submitted owner claim/promotion request
- `PUT /api/v1/servers/{server_id}/owner`
  - owner metadata update for a claimed listing
- `PUT /api/v1/servers/{server_id}/owner-token`
  - owner token update / revalidation request
- `DELETE /api/v1/servers/{server_id}/owner`
  - owner-authorized removal of a claimed listing

### 13.2 Query

Recommended endpoints:
- `GET /api/v1/servers`
- `GET /api/v1/servers/{server_id}`
- `GET /api/v1/meta`
- `GET /api/v1/regions`

### 13.3 Admin / Moderation

Recommended internal/admin endpoints:
- `PUT /api/v1/admin/servers/{server_id}/lifecycle`
  - used for explicit `Disabled` / `Active` transitions
  - requires admin authentication
- `POST /api/v1/admin/servers/{server_id}/retention-reset`
  - resets the retention baseline without changing current health state
- `POST /api/v1/admin/servers/{server_id}/host-ban`
  - bans the listing's resolved host/IP and disables matching records
- `GET /api/v1/admin/servers`
  - exposes all records for admin portal tooling
- `GET /api/v1/admin/runtime`
  - exposes runtime summary and counters for the admin dashboard
- `GET /api/v1/admin/bans`
  - lists active host/IP bans
- `DELETE /api/v1/admin/bans/hosts/{host}`
  - lifts an active host/IP ban

### 13.4 Row Semantics

`GET /api/v1/servers` should query the registry projection, not raw live game servers.

Clients should never poll game servers directly for the main browser.

Implementation note:
- public query endpoints should support short-lived caching and conditional GET behavior where practical

## 14. Registry Persistence

The registry must persist registered server records.

At minimum it should persist:
- identity
- owner account reference when claimed
- trust level
- lifecycle state
- metadata
- poll target
- ownership token hash
- ownership verification state
- last push timestamps
- last successful poll timestamp
- failure counters
- delist timestamps

Without persistence, service restart resilience is not actually achieved.

## 15. Recommended Timestamps

Track these explicitly:
- `LastRegisterUtc`
- `LastPushUtc`
- `LastSuccessfulPollUtc`
- `LastFailedPollUtc`
- `LastSeenUtc`
- `DelistedUtc`
- `LastExplicitOfflineUtc`
- `OwnershipClaimRequestedUtc`
- `VerifiedAtUtc`

These fields make transition logic and debugging much easier.

`LastSeenUtc` should mean the most recent timestamp where the registry had evidence that the server was live:
- typically a successful poll
- optionally a trusted explicit startup/state push as a short-term freshness hint

## 16. UI Visibility Rules

### 16.1 Main Browser

Recommended default:
- show `LifecycleState = Active`
- show `HealthState = Online`
- optionally include `Stale`
- continue showing `Offline` for `Verified` / `Official` listings while they remain `Active`
- demote claimed listings back to `Unverified` if ownership proof fails or becomes stale
- do not surface `Delisted` in normal browse results

### 16.2 Detail Pages

Detail pages may still show:
- verified offline servers
- last seen / last poll timestamps
- historical metrics
- stale/offline explanation
- ownership verification error / pending revalidation state

### 16.3 Admin/Moderation Tools

Admin views should expose:
- `Active`
- `Delisted`
- `Disabled`
- retention/purge timing

## 17. Trust and Reputation

Trust remains separate from both health and lifecycle state.

- `Unverified`
- `Verified`
- `Official`

Future voting/reputation remains a separate subsystem and must not replace:
- trust tier
- lifecycle state
- live health state

## 18. Implementation Order

### Phase 1 - Model and contract update
- add `HealthState`
- add `LifecycleState`
- add persistent registered server record model
- add poll target/status contracts
- add required timestamps

### Phase 2 - Registry persistence and scheduler
- replace in-memory-only registry record storage
- add poll scheduler
- add observation storage
- add lifecycle transition policy

### Phase 3 - Server status endpoint and registration flow
- add lightweight server status endpoint
- add ownership token hash to the status payload
- persist and auto-generate the server ownership token
- allow token rotation and revalidation
- keep startup/shutdown pushes
- use poll-confirmed health for listing projection
- status: implemented

### Phase 4 - Client/web integration
- expose health/lifecycle in normalized rows
- render state badges/colors in browser/detail UI
- support stale/offline/detail semantics consistently
- expose owner claim, edit, delete, and token revalidation flows in the portal
- status: mostly implemented
  - portal owner claim/edit/delete/revalidation flows are in place
  - verified listings remain visible through `Offline` until lifecycle policy delists them
  - admin portal dashboard/accounts/audit/server views are in place
  - portal server list is sortable and now surfaces condensed public-state and metadata pills instead of raw internal identifiers
  - portal server detail now surfaces condensed public-state, registration, retained history, latency, and runtime-observed metrics

### Phase 5 - Historical metrics and moderation
- retain historical aggregates for verified records
- add delisted/admin views
- add moderation/disable flows
- status: partial
  - historical samples/summaries and admin lifecycle API exist
  - admin portal dashboard/accounts/audit/server views exist
  - admin dashboard now surfaces more of the existing runtime counters directly
  - disable/restore/delete/reset-TTL flows exist
  - host/IP bans and `/admin/bans` flow exist

## 19. Design Rules To Preserve

- Discovery is a subsystem, not a menu feature.
- One registry, one normalized row model, multiple trust levels.
- Verified and unverified are publication modes, not separate systems.
- Official is a designation on top of verified, not a separate protocol.
- Trust, health, and lifecycle are separate concepts.
- Delisted does not mean deleted.
- Push for registration and ownership.
- Pull for liveness and runtime state.
- HTTP/JSON first.
- Keep server-published, registry-observed, and client-measured fields separate.
