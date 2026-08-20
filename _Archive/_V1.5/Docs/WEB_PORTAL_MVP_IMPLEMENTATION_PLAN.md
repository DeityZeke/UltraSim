# Web Portal MVP Implementation Plan

Status: implementation plan for the first local `UltraSim.Portal` slice. The public portal shell and first owner workflows are implemented; this document now tracks the remaining MVP gaps and near-term follow-up.

Related docs:
- `Docs/WEB_PORTAL_AND_ACCOUNT_ARCHITECTURE.md`
- `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE_V2.md`
- `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`
- `Docs/ADMIN_ROLES_AND_FORUMS_ARCHITECTURE.md`

## 1. Purpose

Define the first concrete web portal implementation shape for the local-first portal MVP and its immediate next follow-up slices.

This plan is intentionally limited to the local-first MVP:
- landing page
- public listings portal
- account sign-in shell
- owner claim/promotion workflow
- admin listing controls

It does not attempt to solve:
- public launch hardening
- forum integration
- full CMS/content workflows
- official player authentication tokens

## 2. Recommended Project Location

Create the portal as:
- `Services/UltraSim.Portal/`

Do not create:
- `Services/Web/`

Reason:
- the existing service naming is product-oriented
- `UltraSim.Registry` already establishes the pattern
- `UltraSim.Portal` is a platform service, not a generic website folder

## 3. Recommended Stack

Use:
- ASP.NET Core web app
- ASP.NET Core Identity
- Entity Framework Core
- SQLite for the initial local-first database
- Razor Pages or MVC for the first slice

Do not start with:
- WordPress
- Joomla
- a forum package
- a separate auth server

Reason:
- the first problem is platform workflow integration, not content management
- the portal needs direct control over account, claim, and admin flows
- ASP.NET Core Identity already matches the required account model well

## 4. Recommended Solution Shape

Initial project set:
- `Services/UltraSim.Portal/`
  - web app
  - portal UI
  - account UI
  - owner/admin UI
- `tests/UltraSim.Portal.Tests/`
  - integration and page/API tests
  - current local coverage includes authorization/role resolution, admin audit persistence, registry client behavior, and owner page-model workflows

Keep the existing registry separate:
- `Services/UltraSim.Registry/`

The portal should consume the registry as a service dependency, not duplicate registry logic.

## 5. Portal Responsibilities

The MVP portal should own:
- landing page
- sign-in and account shell
- owner claim/promotion UX
- vote records and vote UI
- admin UI over registry/admin endpoints

Currently implemented:
- landing page
- sign-in and account shell
- live registry list/detail pages
- owner claim/promotion UI
- owner edit/delete UI
- vote records and simple vote UI on server detail pages
- projected vote counts surfaced on public list/detail pages

The MVP portal should not own:
- registry polling
- registry lifecycle logic
- direct server liveness decisions
- forum storage

## 6. Initial Data Model

The first portal DB should contain platform-facing account and portal records.

Recommended initial tables/entities:
- `PortalUser`
  - identity user record
  - email
  - display name
  - avatar url later
- `PortalRole`
  - `Admin`
  - optionally `Moderator` later
- `ServerClaim`
  - portal-side claim request record
  - target server id or temporary listing identity
  - submitting user id
  - submitted metadata snapshot
  - submitted token/proof material
  - request status
  - last error
- `ServerOwnership`
  - verified ownership link between portal user and registry server id
- `ServerVote`
  - one vote per user per target server
- `AdminAuditRecord`
  - later, but reserve the concept early

Important rule:
- registry remains authoritative for listing state
- portal remains authoritative for sign-in and portal-side ownership history

## 7. First Integration Boundary

For the MVP, the portal should call the registry over HTTP.

Minimum required registry interactions:
- `GET /api/v1/servers`
- `GET /api/v1/servers/{serverId}`
- `POST /api/v1/servers/claims`
- `PUT /api/v1/servers/{serverId}/owner`
- `DELETE /api/v1/servers/{serverId}/owner`
- `PUT /api/v1/admin/servers/{serverId}/lifecycle`
- `POST /api/v1/admin/servers/{serverId}/retention-reset`
- `POST /api/v1/admin/servers/{serverId}/host-ban`
- `GET /api/v1/admin/servers`
- `GET /api/v1/admin/runtime`
- `GET /api/v1/admin/bans`
- `DELETE /api/v1/admin/bans/hosts/{host}`
- `PUT /api/v1/servers/{serverId}/vote-count`

Near-term gap:
- owner token rotation / revalidation endpoint
- activity-window vote projection filtering later

## 8. MVP Pages

Build these first:

### 8.1 Public

- `/`
  - landing page
- `/servers`
  - public server listings
  - uses registry list endpoint
  - sortable by votes, name, state, uptime, and verified/registration state
  - surfaces compact pill metadata for links, region/language, whitelist, and tags
- `/servers/{serverId}`
  - detail page
  - uses registry detail endpoint
  - shows condensed public state, registration state, and direct-connect metadata first
  - shows retained history, latency, and registry-observed runtime metrics where available
  - supports authenticated vote and withdraw actions
  - now also shows true 24h / 7d / 30d per-server metrics and per-server all-time peak

### 8.2 Account

- `/account/login`
- `/account/register`
- `/account`
  - overview/dashboard
- `/account/security`

### 8.3 Owner

- `/account/servers`
  - owned/claimed servers
- `/account/servers/add`
  - claim/promotion form
- `/account/servers/{id}`
  - ownership/verification status
  - last known errors

### 8.4 Admin

- `/admin`
  - runtime summary and account counts
  - recent account registrations
  - current players total
  - stale/pending/disabled/lease/poll-candidate counts
  - retained aggregate history metrics from registry samples
- `/admin/servers`
  - listing moderation table
  - disable/restore/delete/reset TTL
- `/admin/accounts`
  - account list and role assignment
- `/admin/audit`
  - append-only audit view
- `/admin/bans`
  - active host/IP ban view and lift flow

## 9. MVP UX Rules

The first portal should prove the architecture, not polish every edge case.

Required UX behaviors:
- public listings page renders live registry rows
- detail page renders live registry detail
- login and registration work locally
- claim/promotion form stores submission intent and displays status
- failed verification is visible to the submitting owner
- admin can disable/restore listings from the portal

Not required in MVP:
- rich profile customization
- banner upload pipeline
- forum UI
- external login providers
- full notification/email workflow

## 10. Claim/Promotion Workflow Shape

Portal-side MVP flow:
1. user signs in
2. user opens `My Servers`
3. user submits:
   - display metadata
   - target IP/port
   - server token/proof
4. portal records the claim request
5. portal calls the registry claim/promotion endpoint when available
6. registry performs poll/proof verification
7. portal displays one of:
   - pending verification
   - verified
   - token mismatch
   - no server detected
   - poll failed

This keeps:
- portal as the account-facing workflow surface
- registry as the verification and listing authority

Implementation note:
- the portal may briefly handle the raw ownership token during add/edit, but it should not persist that token in the portal database after the registry request is made

## 11. Authentication and Roles

MVP authentication:
- local username/email + password via ASP.NET Core Identity

MVP authorization:
- normal authenticated user
- admin role

Do not add yet:
- external OAuth providers
- passkeys
- 2FA
- launcher auth tokens

Those can come later once the base account flow is stable.

## 12. Local Development Shape

Recommended local URLs:
- registry: `http://127.0.0.1:5180`
- portal: `https://localhost:7xxx` or `http://localhost:5xxx`

Recommended local storage:
- separate SQLite DB for portal identity/data
- existing registry persistence remains separate

Do not merge portal account data into registry persistence files.

## 13. Suggested Project Structure

Inside `Services/UltraSim.Portal/`:

```text
Services/UltraSim.Portal/
  Data/
  Identity/
  Models/
  Pages/
    Account/
    Admin/
    Servers/
  Services/
    RegistryApi/
  wwwroot/
  Program.cs
  appsettings.json
  appsettings.Development.json
```

Suggested internal service split:
- `RegistryQueryClient`
  - public list/detail reads
- `RegistryAdminClient`
  - admin lifecycle actions
- `RegistryOwnershipClient`
  - future claim/promotion actions

## 14. Recommended Implementation Order

### Phase 1 - Portal skeleton
- create `Services/UltraSim.Portal/`
- add to solution
- enable Identity + SQLite
- create landing page

### Phase 2 - Public registry surface
- build `/servers`
- build `/servers/{serverId}`
- wire to live registry API
- status: implemented
  - `/servers` is sortable and uses compact metadata pills instead of dense raw columns
  - `/servers/{serverId}` now leads with public state, registration state, links, and condensed metrics

### Phase 3 - Account shell
- build login/register/account pages
- add admin role support

### Phase 4 - Owner workflows
- build `My Servers`
- build claim/promotion form
- store claim records locally
- add registry claim/promotion integration when endpoint exists
- add owner metadata edit and delete flows
- add owner token update / revalidation flow
- status: implemented

### Phase 5 - Admin workflows
- build admin listing pages
- wire disable/restore actions
- current state:
  - `/admin` implemented
  - `/admin/servers` implemented
  - `/admin/accounts` implemented
  - `/admin/audit` implemented
  - `/admin/bans` implemented
  - disable/restore/delete/reset TTL implemented
  - host/IP ban and lift flow implemented

### Phase 6 - Voting and activity projection
- build `ServerVote` records in portal storage
- project persistent `VoteCount` into the registry
- surface vote totals on public list/detail pages
- allow signed-in users to vote or withdraw on server detail pages
- current state:
  - `ServerVote` storage implemented
  - registry `VoteCount` projection implemented
  - public list defaults to popularity sort via projected vote count
  - `LastActiveUtc` groundwork is in place for later filtered vote projection

### Phase 7 - Advanced metrics
- persist hourly aggregate samples in the registry alongside fine-grained recent samples
- compute true 24h / 7d / 30d / all-time platform metrics from persisted aggregate samples
- surface advanced time-window metrics on `/admin`
- surface recent platform trend rows on `/admin`
- surface recent hourly aggregate samples on `/servers/{serverId}`
- current state:
  - registry admin runtime exposes persisted hourly aggregate metrics
  - `/admin` shows 24h peak, 7d average, 30d average, 7d/30d uptime ratio, and all-time peak
  - `/servers/{serverId}` presents overtime metrics, sampler history metrics, poll latency, and hourly aggregate samples

## 15. Immediate Registry Follow-Up Needed

Before the portal MVP is complete, the registry likely needs:
- owner token update / revalidation endpoint
- persisted server-side ownership token generation/rotation support
- owner/admin status projection suitable for revalidation states

Current immediate follow-up:
1. expand portal/forum test coverage beyond the current authorization, audit, registry-client, and owner workflow suite in `tests/UltraSim.Portal.Tests/`, with forum flows as the next highest-value target
2. expand forum moderation history/search UX beyond the current dashboard and admin audit surface
3. decide whether dashboard metrics stop at runtime counts or expand into platform-wide historical aggregates next
4. decide when `LastActiveUtc` should also be touched by future official-auth events, not just portal activity
5. decide when vote projection should begin honoring an activity window instead of raw vote count
6. forum foundation work in `UltraSim.Portal` now includes moderation, pagination/search, and admin visibility surfaces, so the next follow-up is long-horizon forum management tooling rather than basic forum scaffolding

Current practical "what is left" summary:
- forum UX overhaul
  - tighter layout, navigation, and readability work across `/forums/*`
  - improve moderation/history usability rather than adding more raw forum features first
- client/account activity integration
  - successful game/client account login should update the same account activity signal as portal web activity
  - this is the missing bridge for future activity-window vote rules
- vote eligibility refinement
  - move from raw persistent vote count toward activity-window-aware vote projection
- forum and portal hardening
  - broaden test coverage for forum flows and moderation paths
  - continue production-hardening/security review when public deployment gets closer
- optional presentation follow-up
  - listing/banner/media pipeline remains deferred unless richer portal presentation becomes a priority

Current security baseline already implemented:
- admin runtime endpoints require admin token auth
- portal owner-claim endpoints fail closed when the registry portal token is not configured
- public poll targets are checked both at registration time and again at poll time after DNS resolution to reduce SSRF risk

## 16. Non-Goals

Not in the first portal slice:
- full forum rollout beyond the newly started platform forum foundation
- CMS integration
- public production deployment
- multi-node auth architecture
- official player authentication service
- file/media pipeline beyond placeholder URLs

## 17. Decision Summary

Build:
- `Services/UltraSim.Portal/`
- custom ASP.NET Core portal
- local Identity + SQLite

Do not start with:
- WordPress
- Joomla
- forum software
- a separate dedicated auth service

That keeps the first slice aligned with the current registry and account architecture docs.
