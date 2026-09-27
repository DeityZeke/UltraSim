# UltraSim Web / Discovery / Registry / Portal Ecosystem

**Proposed status:** Candidate canonical recovery + future-direction document  
**Purpose:** Preserve the architecture, product intent, historical V2/VitaNex lessons, and newly recovered WebPortal/server-list ideas so they are not lost again.  
**Scope:** Discovery, server registry, server list/browser, WebPortal, forums/community/package ecosystem, server ownership, platform accounts, optional official-account federation, registry polling, HTTP seams, outbound integrations, voting/community score, deep-link joining, and the relevant Framework/Bridge boundaries.  
**Non-goal:** This document is **not** an implementation order requiring all of these systems now. It records the intended long-term shape and the pieces that current work must not accidentally prevent.

---

# 1. Source / provenance note

The original V2 WebPortal implementation appears to have been lost.

The most likely history is:

- the website contained secrets/deployment material and was intentionally excluded from normal Git tracking;
- later project recovery relied on repository downloads and scattered archives;
- the `WebPortal` folder survived but its implementation did not;
- therefore the exact source, database schema, account/session implementation, forum internals, and final CSS/templates are not recoverable from the current archive.

The intended design can still be reconstructed from several surviving sources:

1. **V2 Discovery / Registry source**
   - `_Archive/_V2/Discovery/`
   - `_Archive/_V2/Server/Discovery/`
   - `_Archive/_V2/Services/Registry/`
   - `_Archive/_V2/Client/Core/Discovery/`
   - `_Archive/_V2/Server/Accounting/`

2. **VitaNex references**
   - `_Archive/References/VitaNex/Core-5.3.1.0/Services/WebAPI/`
   - `_Archive/References/VitaNex/Core-5.3.1.0/Modules/WebStats/`
   - `_Archive/References/VitaNex/DiscordBot/`

3. **Recovered WebPortal screenshots**
   - early public Server Listings page;
   - Account page;
   - My Servers page;
   - Platform Admin dashboard;
   - V2 server console showing gameplay and Discovery listeners.

4. **Recovered design intent**
   - public server list inspired by a mixture of **FiveM** and **UOGateway**;
   - forums modeled heavily after **RunUO** and **Minecraft Forge** developer/modding forums;
   - one platform account used across Forums, My Servers, account/security, and future official-account federation;
   - My Servers evolved toward a SteamDB-like per-server owner dashboard;
   - Platform Dashboard was a private operator/admin information-at-a-glance surface, not a public page.

Where this document describes exact V2 mechanics, the surviving source is the historical basis.  
Where it describes the lost WebPortal UX or later intended direction, it is reconstructed from screenshots and design memory.  
Where it describes a new V3 direction, it is intentionally marked as the preferred future architecture rather than a claim about what V2 already implemented.

---

# 2. Core architectural rule

The most important recovered rule is:

> **Build durable semantic contracts/services once, and let many independent surfaces consume them.**

Examples:

```text
Discovery contract
    ↓
native client server browser
public website server list
third-party launcher
official registry
LAN provider
favorites/history provider

Registry state/history
    ↓
public server detail page
My Servers owner dashboard
platform admin dashboard

Platform account identity
    ↓
forums
package authorship
server ownership
optional official game authentication
```

The **viewer is not the owner of the data it displays**.

Likewise:

- the website does not own Discovery;
- the client server browser does not own Discovery;
- the registry does not own the gameplay server;
- the forums do not own package identity;
- `My Servers` does not own registry truth;
- Platform Admin does not own account or registry semantics;
- HTTP does not own Discovery semantics;
- Kestrel does not own UltraSim HTTP semantics.

---

# 3. Canonical high-level ecosystem

The intended long-term ecosystem is approximately:

```text
                         UltraSim Platform
                               │
        ┌──────────────────────┼──────────────────────┐
        │                      │                      │
        ▼                      ▼                      ▼
 Platform Accounts       Registry/Discovery     Forums / Dev Space
        │                      │                      │
        │                      │                      │
        ├── Profile            ├── public list       ├── support/help
        ├── Security           ├── owner claims      ├── proposals/core changes
        ├── My Servers         ├── polling/history   ├── package releases
        ├── Package identity   ├── trust/health      ├── tutorials
        └── optional game      └── open API          └── community/off-topic
            federation
                               │
                               ▼
                     UltraSim Server ecosystem
                     ┌─────────┴─────────┐
                     │                   │
                     ▼                   ▼
             server publication    server HTTP endpoint
             / heartbeats          / status / content
                     │                   │
                     └─────────┬─────────┘
                               ▼
                          Game Server
```

This does **not** mean one monolithic service or one giant database.

It means the platform account, registry, forums, and server ecosystem are intentionally interoperable.

---

# 4. Platform account identity

The old WebPortal used one underlying platform account for:

```text
Forums
Account/Profile
My Servers
server ownership
future package authorship
future official-account game authentication
```

Navigation conceptually looked like:

```text
Home
Server List
Forums

Account
Logout
```

with privileged Platform Admin access appearing only for appropriately authorized platform operators.

The important distinction is:

> **One platform principal may participate in many contexts, but those contexts do not share one universal authorization model.**

Example:

```text
Platform Account U123
    Forums
        may create posts
        may release packages

    My Servers
        owns Deployment A
        owns Deployment B

    Server A
        may authenticate using U123
        maps to local shard account 7431
        local permissions remain shard-owned

    Platform Admin
        only if explicitly granted platform-admin authority
```

Authentication answers:

```text
Who is this platform principal?
```

Authorization remains context-specific:

```text
What can this principal do on the forums?
What servers do they own?
Are they a platform operator?
What can they do on this particular shard?
```

---

# 5. Three distinct kinds of “admin”

Do not overload the word **Admin**.

## 5.1 Platform Admin

Private platform/operator scope.

The recovered early Platform Dashboard contained data such as:

```text
account totals
recent account activity
platform owners/admins/developers/moderators
total listings
visible listings
current aggregate players
online/stale/offline/pending listings
active/delisted/disabled listings
active leases
poll rounds
poll candidates
poll failures
```

Later iterations reportedly merged or expanded several of the early “bubble” summaries.

This dashboard was intended as:

> **Platform information at a glance for trusted platform operators.**

It was not public-facing.

Potential platform-admin areas:

```text
Dashboard
Servers
Bans
Accounts
Audit
```

## 5.2 Server Owner / “My Servers”

Scope is limited to deployments owned by the current platform account.

This is not platform administration.

A server owner may manage or inspect:

```text
listing metadata
claim/verification state
visibility
tags
links
icon/banner media
registry health
uptime/downtime
current population
historical averages/peaks
poll state
official-account/federation options
reporting opt-ins
```

only for owned servers.

## 5.3 Shard / Deployment Admin

This is the actual game-server operator/admin authority.

Examples:

```text
Save
Shutdown
Kick
Pause/Resume
Teleport
Spawn
server runtime controls
participant diagnostics
logs
```

This belongs to the server-side Control Plane/operator product.

It is independent from Platform Admin and My Servers, even if the same human happens to hold all three roles.

---

# 6. Discovery is an open semantic contract

V2 already had the right general idea.

`UltraSim.Discovery` was a dependency-light contract project rather than a website implementation.

The future rule should remain:

> **Discovery belongs to a reusable application contract. The client, website, registry, LAN discovery, favorites/history, and custom providers are consumers/providers around it.**

Do not define Discovery as:

```text
the website database schema
```

or:

```text
a private DTO owned by the native client
```

Conceptual shape:

```text
                    Discovery.Contracts
                           │
        ┌──────────────────┼───────────────────┐
        │                  │                   │
        ▼                  ▼                   ▼
 Client Discovery    Server Publication   Registry Service
        │                  │                   │
   provider adapters   publisher adapters   public directory
```

---

# 7. Discovery providers are plural

V2 already modeled multiple provider kinds:

```text
Official
Favorites
History
LAN
Steam
Manual
Custom
```

The exact future provider list can evolve, but the architecture should preserve the idea that:

```text
Server Browser != Official UltraSim Registry
```

Instead:

```text
Server Browser
    ↓
Discovery Providers
        ├── Official Registry
        ├── LAN
        ├── Favorites
        ├── History
        ├── Manual
        ├── future platform-specific provider
        └── custom provider
```

A client/server browser should be able to consume a generic discovery query contract without knowing the implementation of each provider.

This permits an open ecosystem where an UltraSim-compatible client could point at:

```text
the official registry
a community-run directory
a LAN provider
a custom organization directory
```

without changing the fundamental listing model.

---

# 8. Transport/bootstrap discovery is not the same as directory discovery

Keep these concepts separate.

```text
Network/bootstrap discovery
    minimum information needed to find/trust/connect transport
    may use LAN/UDP or network-owned mechanisms

Deployment/server discovery
    browsable server identity and summary

Discovery detail
    richer descriptive/capability information

Official Registry
    one directory/provider for discovery records
```

They may share:

```text
DeploymentId
DeploymentFingerprint
```

but they are not one protocol.

The historical V2 console visibly showed separate gameplay and Discovery listeners, reinforcing that “Discovery” was not simply the gameplay socket.

Similarly, the new shared HTTP endpoint does **not** mean:

```text
all Discovery becomes HTTP
```

---

# 9. Discovery summary versus detail

V2 already had the useful idea of a compact row plus a richer detail record.

That should remain.

## 9.1 Server summary / row

Optimized for dense lists.

Candidate fields:

```text
ListingId / DeploymentId
DisplayName
icon reference
short tagline
host/connect target or safe join identity
health
lifecycle
trust
visibility
region
primary language
current players
max players
rolling/representative average where available
peak players
uptime/availability summary
ping/latency where measured
password/whitelist requirement
compatibility/version
capabilities
structured facets
freeform tags
community score summary
last seen/polled
```

## 9.2 Server detail

Selected-server / website-card level.

Adds:

```text
full description
banner/background
all languages
external links
longer population history
uptime/downtime history
poll history
latency summary
advanced metrics
ownership verification state where appropriate
richer capability details
content/auth requirements
```

These are two projections of one discovery/listing domain, not two unrelated discovery systems.

---

# 10. Server list product direction

The intended server list is best described as:

> **A searchable/tag-driven FiveM-style browser backed by an open Discovery API, with UOGateway-style rich shard identity/history and a safe one-click UltraSim join path.**

## 10.1 Dense list inspiration

FiveM-like qualities:

```text
search
filtering
dense rows
small tag bubbles
country/region/language cues
population
fast scanning
direct join
```

## 10.2 Rich listing inspiration

UOGateway-like qualities:

```text
name
icon/logo
optional banner/background
description
votes/community score
online/offline state
uptime
current/average/max players
ping/latency
external links
rich identity
```

The same underlying API should support both renderings.

---

# 11. Listing data provenance

Do not let all listing fields appear equally authoritative.

Separate at least:

## 11.1 Owner-authored metadata

```text
display name
description
icon
banner/background
region declaration
language declaration
gameplay facets
freeform tags
external links
visibility
```

## 11.2 Registry-observed state

```text
health
last successful poll
last failed poll
current players
rolling average players
peak players
uptime / availability
latency
poll history
```

## 11.3 Community/platform state

```text
up reactions
down reactions
community score
package/forum references if later integrated
```

## 11.4 Connection/capability state

```text
deployment identity
connect target
compatibility/version
authentication requirements
content requirements
deep-link support
capability advertisement
```

A server owner may author:

```text
"Hardcore PvP RP"
```

but should not be able to author:

```text
"99.999% uptime, average 400 players"
```

as registry truth.

---

# 12. Structured facets and freeform tags

The old server list used small tag bubbles at the bottom of a listing/row.

Preserve that visual/product idea.

However, distinguish stable structured semantics from owner-created freeform tags.

## 12.1 Structured facets

Examples:

```text
Region
Primary Language
PvP policy
Roleplay policy
Access policy
Modded
Password required
Whitelist/allow-list
Official-account support
Content/mod requirements
```

These should be typed where the semantics are stable.

Example:

```text
Region = US-East
PrimaryLanguage = en-US
PvP = Enabled
Roleplay = Encouraged
Access = Public
```

## 12.2 Freeform tags

Bounded owner-authored discoverability labels:

```text
hardcore
casual
new-player-friendly
custom-economy
seasonal
permadeath
police
survival
crafting-heavy
```

## 12.3 UI

The UI may render structured facets and freeform tags identically:

```text
[US-East] [English] [PvP] [RP] [Hardcore] [Custom Economy]
```

and allow click-to-filter.

Internally they should remain distinguishable so reliable filtering does not depend on arbitrary strings.

---

# 13. Filtering and sorting

The public API / native browser should be able to filter on stable fields such as:

```text
search text
region
language
PvP / RP / access facets
trust level
password requirement
allow-list requirement
compatibility
online state
tags
```

Sorting may eventually include:

```text
name
players online
ping
uptime
last polled
community score
activity
```

Do not make one ranking order permanently define “best server.”

Especially avoid making raw population or raw vote totals the only discovery ranking, since large servers naturally accumulate more traffic and attention.

---

# 14. Community voting / reactions

The old global “one vote per account total” concept is retired.

The preferred model is now:

> **One reaction per platform account per server listing.**

Each account may react independently to many different listings.

For one listing, the account has exactly one of:

```text
Up
Neutral / no reaction
Down
```

## 14.1 Interaction semantics

If currently Neutral:

```text
click Up   → Up
click Down → Down
```

If currently Up:

```text
click Up   → Neutral
click Down → Down immediately
```

If currently Down:

```text
click Down → Neutral
click Up   → Up immediately
```

There is no separate global vote budget.

A user can therefore upvote:

```text
Server A
Server B
Server C
```

if they genuinely like all three.

This avoids the scarcity effect where the largest/most-trafficked servers absorb nearly all available votes merely because every account can support only one server.

## 14.2 Public aggregate

Expose:

```text
UpCount
DownCount
CommunityScore = UpCount - DownCount
```

UI concept:

```text
       327
    community score

↑ 402          ↓ 75
```

The large number is the net score.

The arrows display their individual smaller counts.

## 14.3 Current-user state

When the viewer is authenticated, the API may additionally expose:

```text
CurrentAccountReaction = Up | Down | None
```

Anonymous/public API consumers do not need per-account state.

## 14.4 Ownership

Community reactions are **platform/community state**.

The game server must not be able to author or directly modify its own vote totals.

## 14.5 Stable target

The reaction should attach to a stable **server listing identity**, not merely a transient lease or current host/port.

Re-registration of the same verified listing should retain its reactions.

A genuinely new/recreated listing should not inherit another listing's votes unless identity continuity is explicitly proven.

## 14.6 API semantics

Prefer idempotent reaction state:

```text
PUT listing reaction = Up
PUT listing reaction = Down
DELETE listing reaction
```

or equivalent.

Do not implement “increment vote count” as a blind mutable integer.

---

# 15. Links and social icons

Listings may expose bounded typed links such as:

```text
Website
Forums
Discord
Reddit
Wiki
GitHub
Custom
```

The data contract carries the URL and kind.

The viewer chooses the icon.

Example:

```text
Discord URL
    → render Discord icon
    → click opens Discord/community page

Reddit URL
    → render Reddit icon
```

Do not allow arbitrary owner-supplied HTML/JavaScript merely to render custom buttons.

Validate allowed schemes:

```text
https
possibly http only where policy explicitly allows it
```

---

# 16. Listing media

A listing may provide:

```text
icon/logo
optional banner/background
```

The exact media distribution mechanism is not frozen.

Possible future approaches:

```text
platform-uploaded/cached listing media
approved HTTPS media URL
content-addressed listing media
```

Permanent constraints:

```text
bounded dimensions
bounded bytes
known media formats
no arbitrary HTML
no executable content
safe client/browser fetching policy
```

If the native client fetches remote listing media, SSRF/private-network policy must be considered.

A registry-side media cache/proxy may later be preferable.

---

# 17. “Play Now” / deep-link joining

The website/server-list should eventually be able to expose a:

```text
[ PLAY NOW ]
```

button similar to FiveM/NoPixel-style website joining.

Prefer a registered UltraSim deep link rather than arbitrary command-line text.

Conceptual example:

```text
ultrasim://join/<deployment-id>
```

Flow:

```text
browser / forum / website
        ↓
ultrasim://join/<deployment-id>
        ↓
installed UltraSim client / launcher
        ↓
resolve listing
        ↓
compatibility check
        ↓
content requirements
        ↓
authentication if required
        ↓
connect
```

This allows the client to remain the owner of safe connection behavior.

Do **not** let arbitrary web pages construct unrestricted process command-line arguments.

A lower-level manual connection deep link may later exist if needed:

```text
ultrasim://connect/<host>:<port>
```

but verified listings should prefer stable deployment/listing identity.

---

# 18. Public Registry API

The server-list API is deliberately open.

Historical V2 endpoints included forms such as:

```text
GET /api/v1/servers
GET /api/v1/servers/{serverId}
GET /api/v1/regions
GET /api/v1/meta
```

The exact future URL is not frozen, but the semantic rule is:

> **The official registry's public read contract must be consumable by the native client, the website, and third-party tools.**

The website is not special-cased.

Potential consumers:

```text
UltraSim native server browser
UltraSim website
third-party launcher
community directory frontend
CLI tooling
monitoring/analytics tools
```

Public read API concerns may include:

```text
versioning
bounded paging
filtering
ETags
short cache windows
rate limiting
browser CORS where deliberately enabled
```

---

# 19. Registry write/trust surfaces are separate from public reads

The historical registry already separated different trust domains.

Conceptually:

## Public directory API

```text
read server summaries/details
no portal/admin privilege
```

## Publisher API

Used by game servers:

```text
register
heartbeat
presence
metadata
unpublish
```

## Portal integration API

Used by the official platform/web backend:

```text
server ownership claim
owner metadata
owner proof/challenge updates
community reaction aggregation / management
```

## Platform Admin API

Used only by trusted registry/platform operators:

```text
listing lifecycle moderation
host bans
server removal
retention reset
registry runtime diagnostics
```

Sharing one web service process does **not** mean sharing one authorization policy.

---

# 20. Registry publication

Server-side discovery publication should remain provider-neutral.

Conceptual contract:

```text
Publish
Unpublish
Presence
```

The official UltraSim registry is one implementation.

Future alternatives may include:

```text
community registry
platform-specific listing system
organization/private directory
```

Server code should not define “Discovery” as:

```text
POST specifically to ultrasim.com
```

Instead:

```text
Server Discovery Projection
        ↓
Publisher Adapter
        ↓
Official Registry
```

---

# 21. Push + poll

The official registry may use both:

## Server → Registry push

```text
registration
heartbeat
presence
metadata refresh
```

## Registry → Server poll

```text
reachability
small status projection
latency
ownership/deployment proof
```

These mechanisms complement each other.

A server heartbeat says:

> “I am alive and this is my current state.”

A registry poll can independently observe:

> “I can reach this deployment at the advertised endpoint, this is what it reported, and this is the observed latency.”

---

# 22. Registration leases

V2 used renewable leases.

The semantic concept should survive even if exact token mechanics change.

Conceptually:

```text
server registers
    ↓
registry returns registration/lease authority
    ↓
server renews at coarse cadence
    ↓
lease expires if publication disappears
```

Registration identity is distinct from:

```text
player connection
account
character
network session
```

Useful concepts include:

```text
stable ListingId / DeploymentId
RegistrationId
LeaseId
temporary heartbeat authority
expiry
```

---

# 23. Health, lifecycle, visibility, and trust are separate

Preserve independent dimensions.

## Health

```text
Pending
Online
Stale
Offline
```

## Lifecycle

```text
Active
Delisted
Disabled
```

## Visibility

```text
Public
Unlisted
Private
Disabled
```

## Trust

```text
Unverified
Verified
Official
```

Do not collapse these into one giant status enum.

Example:

```text
Verified
Offline
Active
Public
```

is valid and meaningful.

---

# 24. Server ownership / “My Servers” verification

The portal account may claim a server, but portal login alone is not proof that the account controls the deployment.

Recovered V2 intent:

```text
authenticated platform account
        ↓
claims target server/listing
        ↓
registry creates pending ownership verification
        ↓
actual deployment must cooperate/prove claim
        ↓
listing becomes owner-verified
```

Historical V2 used an ownership-token hash checked during registry polling.

The preferred future V3 direction may use stronger deployment identity.

Potential future flow:

```text
Platform Account U123
    requests claim of Deployment X

Registry
    issues challenge

Deployment X
    signs/proves challenge using deployment identity

Registry
    verifies

U123 ↔ Deployment X ownership relationship established
```

Exact cryptographic design remains future work.

Permanent rule:

> **Listing ownership requires both authenticated platform identity and cooperation/proof from the actual deployment.**

---

# 25. My Servers

`My Servers` is the server owner's portal workspace.

Early recovered page:

```text
Name
Target
Status
Updated
Registry Id
```

Later intended direction was much richer—effectively **SteamDB for owned UltraSim servers**.

## 25.1 Owner overview

Candidate at-a-glance data:

```text
current players
24-hour average
30-day average
peak players
uptime
downtime
availability
last successful poll
recent poll failures
latency
listing health
listing lifecycle
verification state
```

## 25.2 Listing management

Owner-editable fields:

```text
name
description
icon
banner
region
languages
structured facets
freeform tags
external links
visibility
```

## 25.3 Integration settings

Future:

```text
official account verification enabled?
official account required or optional?
activity reporting enabled?
moderation reporting enabled?
discovery publication policy?
```

## 25.4 Provenance

The owner dashboard is a viewer/editor.

It does not fabricate registry-observed uptime/population truth.

---

# 26. Public server detail page

A rich public server page may show:

```text
name
icon/banner
description
structured facets
freeform tags
external links
community score
up/down counts
online/offline/stale state
current players
average players
peak players
uptime / availability
ping/latency
compatibility
content/auth requirements
Play Now
```

The public page should use the same underlying discovery/registry contracts as the native client.

The website may choose a richer presentation.

It does not get a separate source of truth.

---

# 27. Platform Admin information-at-a-glance

The old “Platform Dashboard” should be remembered as an early operator dashboard, not a public site feature.

Potential future grouped information:

```text
Platform Accounts
    totals
    active periods
    account/security state
    platform roles

Registry
    total listings
    visible listings
    current total players
    health distribution
    lifecycle distribution
    leases
    poll rounds
    candidates
    failures

Moderation
    host/server bans
    reports
    account/platform enforcement

Operations
    registry service health
    persistence state
    API health
    background job state
```

The exact bubble/card layout is presentation, not architecture.

Several low-level counters may be merged into clearer grouped summaries in the final product.

---

# 28. Forums / developer community

The forums were intended as a first-class **development/community space**, heavily inspired by RunUO and Minecraft Forge communities.

Top navigation concept:

```text
Home
Server List
Forums
```

Forum purposes include:

## Support

```text
server setup help
client help
deployment troubleshooting
package support
```

## Core / Framework development

```text
bug reports
feature proposals
architecture discussions
requests for framework/core changes
pull-request/design discussion
```

## Package ecosystem

```text
release custom packages
announce updates
package support threads
tutorials/examples
dependencies/integration discussion
```

## Community

```text
showcases
general discussion
off-topic
```

One platform account participates in forums, My Servers, and account/security.

---

# 29. Forum package releases are not package identity

The forum may be the human/community discovery surface for a package.

It must not be the package's canonical runtime identity.

Example:

```text
Forum:
    "Awesome Farming Package v1.4"
```

Actual package:

```text
PackageId
Version
Manifest
Dependencies
Required capabilities
Optional capabilities
Provided capabilities
content/runtime identity
```

The Runtime/Module Host should accept a valid package regardless of whether it came from:

```text
forums
GitHub
local filesystem
another repository
```

The forum thread is a distribution/support surface.

---

# 30. Platform authorship and identity

A platform account may eventually provide identity for:

```text
forum authorship
package publisher identity
server ownership
official game authentication
```

But keep separate concepts:

```text
identity verified
package trusted
package reviewed
package officially endorsed
```

Likewise:

```text
server ownership verified
```

does not mean:

```text
server is good
server is safe
server is officially endorsed
```

Do not collapse identity proof into reputation.

---

# 31. Optional UltraSim Official account federation

A server should always be capable of local accounting.

Optional future mode:

```text
Use UltraSim Official Account Verification
```

Possible server policy:

```text
Disabled
Optional
Required
```

The official service authenticates the player's **platform identity**.

The server retains:

```text
local account mapping
characters
permissions
local bans
timeouts
session policy
gameplay/world state
```

Permanent rule:

> **A server may delegate credential verification without delegating local gameplay/account authority.**

---

# 32. The shard must never receive the UltraSim password

Avoid:

```text
Client → UltraSim password → shard → portal
```

because the shard owner controls the server executable and could log the credential.

Preferred future flow:

```text
client / browser
    ↓
official UltraSim authentication
    ↓
short-lived signed/deployment-bound assertion
    ↓
client
    ↓
server
```

The assertion contains only justified claims, for example:

```text
Issuer
stable opaque subject/account ID
Audience = target deployment
Nonce/challenge
IssuedAt
ExpiresAt
```

Not:

```text
password
password hash
long-lived portal cookie
email unless explicitly required
```

A server should be able to truthfully say:

> “This shard uses UltraSim Official Verification. We never receive your UltraSim password.”

---

# 33. External identity is not local authorization

Example:

```text
Official identity:
    UltraSim Account U123

Server A:
    local account = 7431
    character = Alice
    normal player

Server B:
    local account = 9912
    character = Bob
    server admin
```

Official identity says:

```text
who is this?
```

Server-local policy says:

```text
what may they do here?
which characters belong to them?
are they banned here?
```

---

# 34. Optional cross-server reporting

Authentication and reporting must be separate opt-ins.

Example server settings:

```text
Use UltraSim Account Verification        [x]
Report Account Activity                  [ ]
Report Moderation Actions                [ ]
```

Discovery should eventually advertise relevant reporting participation before a player joins.

## 34.1 Activity

Possible cross-server aggregate:

```text
Server A  143h
Server B   18h
Server C   61h
Total     222h
```

But preserve provenance:

```text
143h reported by Server A
```

The platform cannot cryptographically force a server owner to report honest elapsed time.

## 34.2 Moderation

Record:

```text
Server A reports:
    Account U123
    banned
    start/end
    category
    server-provided reason
```

Do not silently convert this into:

```text
UltraSim declares U123 a bad player
```

A malicious server owner can lie.

All reports need deployment provenance.

## 34.3 No automatic global reputation score initially

Avoid early systems such as:

```text
banned from 10 servers → automatic rejection everywhere
```

Different servers have different rules and owners may abuse reporting.

Federated reputation is a separate future design problem.

---

# 35. Server reputation is the mirror problem

If servers may report players, players/platform operators eventually need mechanisms to reason about servers too.

Potential future provenance:

```text
verified owner
deployment age
availability history
official-account support
reporting participation
complaints
compatibility
```

Avoid simplistic one-number “trust scores” until a real evidence model is designed.

---

# 36. Inbound HTTP architecture

The current canonical server-side HTTP direction is:

```text
Framework.Http.Endpoint
        ↓
Bridges.Http.Kestrel
```

This is **Framework infrastructure**, not a gameplay Engine Core.

The endpoint owns:

```text
endpoint lifecycle
route/service composition
bounded admission
per-service queues/workers
request/response lifetime
diagnostics
```

Kestrel is an external technology realization.

Services do not receive raw `HttpContext`.

---

# 37. One owner per bound endpoint

Permanent rule:

> **One listener/backend owner per bound HTTP endpoint—not one listener per feature.**

Current/future example:

```text
HttpEndpointHost
    :7778

    Static Content
    Registry Status/Proof
    Discovery Summary/Detail
    future bounded web service
```

A deployment may later legitimately compose multiple endpoint hosts:

```text
public deployment endpoint
private admin endpoint
```

Do not create a process-wide `GlobalHttpServer` singleton.

---

# 38. Per-service backpressure domains

One endpoint does not imply one shared service budget.

Example:

```text
Static Content
    large responses
    max active = 4
    max queued = 8

Registry Status
    tiny response
    separate active/queue budget

Discovery
    tiny descriptor
    separate budget
```

Four stalled content downloads must not starve a registry poll or lightweight discovery request.

The Framework supplies bounded scheduling.

Each service owns its policy.

---

# 39. Known inbound HTTP consumers

Current:

```text
Static Content
```

Known historical/future:

```text
Registry status/proof
Discovery summary/detail
richer web-facing projections
```

Potential later:

```text
account-authorized APIs
operator/admin APIs
package/content metadata
```

Shared HTTP machinery must not collapse those semantic/security domains.

---

# 40. Registry status/proof endpoint

Historical V2 had a separate `ServerDiscoveryStatusEndpointHost` using `HttpListener`.

Do not revive that listener ownership.

Future shape:

```text
shared HttpEndpointHost
    ↓
RegistryStatusHttpService
```

Small bounded response may include:

```text
Deployment/Server identity
public status
population summary
ownership/deployment proof
timestamp
```

Exact DTO will belong to Discovery/Registry contracts, not Framework HTTP.

---

# 41. Outbound HTTP is a separate seam

Inbound endpoint hosting and outbound external integration are different responsibilities.

Known outbound uses:

```text
server → official Registry publication
server ↔ official account federation
server → Discord/webhooks
server → other external APIs
```

Do not make `Framework.Http.Endpoint` own outbound HTTP.

A future shared outbound transport/policy layer may be earned after multiple consumers establish common requirements.

---

# 42. Discord / external integrations

VitaNex DiscordBot demonstrated:

```text
server semantic events
        ↓
Discord integration
        ↓
outbound HTTPS
        ↓
Discord webhook
```

Classify Discord as an external Bridge/Integration, not an Engine Core.

Prefer:

```text
Discord Bridge
    knows Discord protocol/API

UltraSim Discord Integration/Profile
    decides which UltraSim events become Discord messages
```

Avoid lateral dependencies such as:

```text
DiscordIntegration → Combat.Core implementation
```

Prefer typed semantic contracts/events.

Do not invent a universal string/object event bus prematurely.

---

# 43. Secrets / credentials

External-service secrets should not become ordinary readable Control Plane values.

Examples:

```text
Discord webhook token
registry publication credential
official account federation secret
```

The system should eventually distinguish:

```text
normal configuration
secret configuration
```

A UI can display:

```text
credential configured = yes
source = deployment secret
```

without returning the secret to every viewer.

Exact secret-storage infrastructure remains future work.

---

# 44. Server state should not be read directly from HTTP request threads

Historical VitaNex WebStats directly read mutable RunUO state.

UltraSim should improve the boundary.

Preferred reads:

```text
Authority / Engine
        ↓
committed bounded projection
        ↓
HTTP service
```

Preferred mutation:

```text
HTTP request
        ↓
authentication/authorization
        ↓
typed command/action
        ↓
authority owner
```

Do not hand arbitrary HTTP route handlers:

```text
mutable ECS World
raw ServerRuntime authority
unrestricted persistence internals
```

---

# 45. Future `Framework.Web`

A richer web-app capability may eventually be earned above `Framework.Http.Endpoint`.

Possible future shape:

```text
Framework.Web
    bounded query/body helpers
    JSON codecs
    static web assets
    common authentication hooks
    CORS policy
    web application conveniences
        ↓
Framework.Http.Endpoint
        ↓
Http.Kestrel Bridge
```

Do not make future `Framework.Web` own the listener.

Do not implement it merely because one HTTP endpoint exists.

---

# 46. Browser/client/website are viewers over shared semantics

The same registry/discovery data can feed:

```text
FiveM-style native server browser
UOGateway-style public website card
My Servers owner dashboard
Platform Admin
third-party launcher
```

Each surface chooses different density and permissions.

They should not duplicate source-of-truth implementations.

---

# 47. Public versus owner versus platform-admin projection

## Public

```text
name
description
media
tags/facets
links
community score
population/history
health
Play Now
```

## Owner

Everything public, plus:

```text
verification
poll/lease diagnostics
visibility
metadata editing
media/tags/links editing
reporting/federation settings
detailed downtime/history
```

## Platform Admin

Everything above, plus:

```text
registry internals
moderation
host bans
ownership/account relationships
service/audit state
```

Same underlying owners, progressively more privileged projections.

---

# 48. Deep-link and protocol safety

A website may launch the client, but should never obtain general process execution authority.

Preferred:

```text
ultrasim://join/<stable-id>
```

The client resolves and validates.

Potential client checks:

```text
listing identity
compatibility
required content
trust
authentication provider
user confirmation
```

The website supplies identity/intention, not arbitrary executable arguments.

---

# 49. SSRF / poller safety

The Registry poller necessarily performs outbound requests to server-advertised status endpoints.

V2 already had SSRF guard concepts.

Future poller rules should include:

```text
scheme restrictions
DNS resolution policy
private/loopback destination rejection by default
bounded timeout
bounded response size
bounded concurrency
redirect policy
host revalidation / DNS rebinding considerations
```

Private/LAN targets may be allowed only under deliberate deployment policy.

---

# 50. Compatibility and capability advertisement

Historical capability ideas included concepts such as:

```text
Direct Connect
Password Required
Whitelist Only
Server-Driven UI
Content Patching
Gateway/Official Auth
Character Creation At Join
Mods
Requires Launcher
Voice
```

The future representation may move beyond one fixed bitfield, but the semantic rule is valuable:

> **A listing advertises capabilities so the client knows which workflow the deployment supports before joining.**

Future relevant capabilities may include:

```text
OfficialAccountVerification
OfficialAccountRequired
ContentEndpoint
DiscoveryDetail
DeepLinkJoin
ReportingParticipation
```

Exact schema remains future work.

---

# 51. Immediate HTTP implementation boundary

The current F1.12 HTTP repair should implement only:

```text
Framework.Http.Endpoint
Http.Kestrel Bridge
Static Content service
lightweight independent status-like composition test
```

The test service models the known future registry-status workload:

```text
small GET
small response
independent service budget
same endpoint
still responsive while Content is saturated
```

Do **not** pull all of Discovery/Registry/WebPortal into the current HTTP task.

---

# 52. What current HTTP work must not accidentally prevent

The endpoint architecture should leave room for:

```text
Registry status/proof provider
public Discovery summary/detail
future richer Web capability
separate service authorization policies
multiple endpoint instances
Runtime/Module Host-provided route services
```

It should **not** prematurely implement those systems.

---

# 53. Future Runtime/Module Host relationship

Later:

```text
server package
    ↓
registers HttpServiceDefinition / capability
    ↓
Framework Runtime/Module Host
    ↓
validates whole composition
    ↓
HttpEndpointHost
```

Initial composition may remain startup-time.

No requirement for arbitrary hot unloading.

A package that contributes web/discovery behavior should own its semantics while the Framework owns endpoint mechanics.

---

# 54. Package/forum ecosystem relationship

Future human workflow:

```text
author develops C# package
        ↓
tests locally
        ↓
publishes package + forum thread
        ↓
other owner downloads/install
        ↓
Runtime/Module Host validates package manifest
        ↓
server starts with capability
```

Forums provide:

```text
discoverability
documentation
discussion
support
community reputation
```

The package manifest provides runtime identity and dependencies.

---

# 55. Durable design invariants

Preserve these unless explicitly superseded later.

> **Discovery is an open semantic contract, not a website feature.**

> **The official Registry is one Discovery provider/service, not Discovery itself.**

> **The native server browser and website consume the same public listing semantics.**

> **Discovery providers are plural.**

> **Direct/manual connection must remain possible when the official Registry is unavailable.**

> **One listener owner per HTTP endpoint; features/services do not start competing listeners.**

> **One endpoint does not imply one shared backpressure or authorization domain.**

> **HTTP does not own Discovery semantics.**

> **Kestrel does not become UltraSim's public architecture.**

> **Inbound endpoint hosting and outbound external integrations are separate seams.**

> **Trust, health, lifecycle, and visibility are different dimensions.**

> **Server ownership requires platform identity plus proof from the actual deployment.**

> **Server-list public reads are open; portal/admin mutations are separately authorized.**

> **A platform account may span Forums/My Servers/federated identity, but authorization remains contextual.**

> **Official authentication must never require a shard to receive the user's UltraSim password.**

> **Official identity federation does not replace local shard accounting/characters/permissions/bans.**

> **Authentication and cross-server reporting are separate opt-ins.**

> **Cross-server activity/moderation reports retain server provenance.**

> **One account may react independently to every server listing; community score is up minus down.**

> **Votes/reactions are platform/community state, never server-authored metadata.**

> **Structured discovery facets should remain typed; freeform tags supplement them.**

> **Forum/package pages are human distribution/support surfaces, not package identity.**

> **Viewers do not become semantic owners.**

---

# 56. Explicit near-term non-goals

This recovery document must not be misread as a request to immediately build:

```text
WebPortal
forums
registry
poller
server-list API
My Servers
Platform Admin
official-account federation
cross-server reporting
Discord bridge
package marketplace
deep-link launcher
full Discovery revival
```

The immediate known architectural sequence remains separate.

The current HTTP task only needs to establish the correct durable inbound endpoint seam.

The rest of this document exists so those later systems can be implemented without rediscovering or accidentally contradicting the intended ecosystem.

---

# 57. Historical source map

Useful surviving files/directories:

## V2 Discovery contracts

```text
_Archive/_V2/Discovery/
    IServerDiscoveryQuery.cs
    IServerDiscoveryPublisher.cs
    ServerAdvertisedInfo.cs
    ServerDiscoveryRow.cs
    ServerDiscoveryDetail.cs
    ServerDiscoveryStatusInfo.cs
    ServerDiscoveryPollStatusResponse.cs
    ServerCapabilityFlags.cs
    ServerListingTrustLevel.cs
    ServerListingHealthState.cs
    ServerListingLifecycleState.cs
    ServerListingVisibility.cs
```

## Official Registry contracts

```text
_Archive/_V2/Discovery/Official/
    OfficialRegistryDiscoveryQuery.cs
    OfficialRegistryDiscoveryPublisher.cs
    OfficialRegistryRegisterRequest.cs
    OfficialRegistryRegisterResponse.cs
    OfficialRegistryHeartbeatRequest.cs
    OfficialRegistryPresenceRequest.cs
    OfficialRegistryOwnerClaimRequest.cs
    ...
```

## V2 server Discovery

```text
_Archive/_V2/Server/Discovery/
    ServerDiscoveryContractBuilder.cs
    ServerDiscoveryPublicationFactory.cs
    ServerDiscoveryPublisherCoordinator.cs
    ServerDiscoveryStatusEndpointHost.cs
```

## V2 Registry

```text
_Archive/_V2/Services/Registry/
    Program.cs
    InMemoryOfficialRegistryStore.cs
    RegistryPollingService.cs
    RegistryAuth.cs
    SsrfGuard.cs
    SsrfProtectionHandler.cs
    README.md
```

## V2 client browser

```text
_Archive/_V2/Client/Core/Discovery/
    ClientServerBrowserService.cs
    ClientOfficialRegistryDiscoveryFactory.cs
    ClientLanDiscoveryFactory.cs
    ClientSavedServerList.cs
    ClientSavedServerOverlayDiscoveryQuery.cs
```

## V2 Accounting federation seam

```text
_Archive/_V2/Server/Accounting/
    IAccountAuthenticationProvider.cs
    IPortalAccountAuthenticator.cs
    PortalCredentialAuthenticationProvider.cs
    PortalAccountAuthenticationResult.cs
    README.md
```

## VitaNex web precedent

```text
_Archive/References/VitaNex/Core-5.3.1.0/Services/WebAPI/
_Archive/References/VitaNex/Core-5.3.1.0/Modules/WebStats/
```

## VitaNex outbound integration precedent

```text
_Archive/References/VitaNex/DiscordBot/
```

---

# 58. Recovered visual/product references

Recovered screenshots document at least:

```text
early public Server Listings page
early Account/security page
early My Servers page
early Platform Admin dashboard
V2 server console showing gameplay + Discovery listeners
```

Additional external visual inspirations discussed:

```text
FiveM server browser
    dense searchable rows
    tags
    population
    direct join

UOGateway
    rich shard cards
    description
    image/banner
    votes
    current/avg/max players
    uptime
```

The future UI should use these as **product references**, not as contracts.

---

# 59. Compact future architecture diagram

```text
                              ULTRASIM PLATFORM
                                     │
           ┌─────────────────────────┼─────────────────────────┐
           │                         │                         │
           ▼                         ▼                         ▼
     Platform Accounts        Registry / Discovery          Forums
           │                         │                         │
           │                         │                         ├── support
           │                         │                         ├── proposals
           │                         │                         ├── packages
           │                         │                         └── community
           │                         │
           ├── My Servers            ├── public open API
           ├── security              ├── server publication
           └── optional identity     ├── poll/history
               federation           └── owner verification
           │                         │
           └──────────────┐   ┌──────┘
                          ▼   ▼
                        ULTRASIM SERVER
                              │
              ┌───────────────┼────────────────┐
              │               │                │
              ▼               ▼                ▼
       Gameplay Network   Discovery       HTTP Endpoint
                          bootstrap       Framework
                                             │
                           ┌─────────────────┼──────────────────┐
                           │                 │                  │
                           ▼                 ▼                  ▼
                    Static Content   Registry Status     future Discovery/Web
                                             │
                                             ▼
                                      Kestrel Bridge

OUTBOUND:
    Server publication ───────────────→ Official Registry
    Official identity federation ─────↔ Platform Account Service
    Discord/webhooks ─────────────────→ External Services
```

---

# 60. Compact product model

```text
PUBLIC
    Home
    Server List
    Forums

ACCOUNT
    Profile
    Security
    My Servers
    Personal Data

SERVER LIST
    search/filter/sort
    dense FiveM-style rows
    UOGateway-style rich detail
    tags/facets
    links/media
    population/history
    community score
    Play Now

MY SERVERS
    claims
    verification
    listing editor
    analytics/history
    poll/lease health
    integration settings

PLATFORM ADMIN
    private platform-wide operations
    accounts
    registry
    moderation
    audit
    service health

FORUMS
    support
    development
    core/framework proposals
    package releases
    tutorials
    community
```

---

# 61. Final intent

The long-term goal is not merely:

> “UltraSim has a website.”

It is:

> **UltraSim has an open, typed deployment/discovery ecosystem where game servers, native clients, websites, third-party tools, platform accounts, package authors, and optional official services can cooperate without any one frontend becoming the owner of the underlying semantics.**

The lost WebPortal was one frontend over that ecosystem.

The future implementation should preserve the useful ideas that survived:

```text
shared platform identity
open server-list API
plural Discovery providers
verified server ownership
rich server identity + analytics
forums as development/package community
My Servers as owner operations/analytics
private Platform Admin
optional official account federation
safe one-click joining
typed tags/facets
independent per-listing up/down reactions
shared HTTP endpoint mechanics with isolated services
```

while rebuilding them on the newer Framework / Contracts / Bridges / Engine / Game Implementation architecture rather than reproducing V2 implementation shortcuts.
