# Admin, Roles, and Forums Architecture

Status: draft architecture for platform roles, admin panel capabilities, audit rules, voting integration, and future forum structure.

Related docs:
- `Docs/WEB_PORTAL_AND_ACCOUNT_ARCHITECTURE.md`
- `Docs/WEB_PORTAL_MVP_IMPLEMENTATION_PLAN.md`
- `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE_V2.md`

## 1. Purpose

Define the platform-level role and moderation model for:
- portal admin access
- account role assignment
- listing moderation
- audit logging
- vote authority
- future platform forums

This is a platform service design. It is not shard staff tooling.

## 2. Scope Boundary

The admin panel and forums are platform services. They are explicitly not:
- shard or game-server admin panels
- GM tooling
- server-specific staff hierarchies
- server-specific player moderation systems

Individual server operators remain responsible for:
- staff
- player bans
- in-game moderation
- shard-specific community spaces

The portal only governs the platform:
- registry
- portal accounts
- voting
- future platform forums

## 3. Role Model

Roles are platform roles with ordered authority tiers.

Recommended tiers:

| Tier | Role | Purpose |
|---|---|---|
| 100 | `PrimaryOwner` | Root platform owner. Protected account. Cannot be removed or demoted through normal admin flows. |
| 95 | `Owner` | Full platform authority except protected-root operations. |
| 90 | `Admin` | Platform administration for listings, accounts, metrics, and audit visibility. |
| 80 | `Developer` | Contributor recognition role. Social/community signal, not operational authority by default. |
| 40 | `Moderator` | Forum moderation only. |
| 0 | `User` | Default registered account. |

Important rules:
- `Developer` is primarily a badge/trust signal, not an admin role
- `Moderator` is forum-only
- role tier is the default access model for admin v1
- per-account permission overrides are deferred until later

## 4. Role Assignment Rules

### 4.1 Bootstrap

Do not auto-promote the first registered public account.

Recommended bootstrap:
- use a manual owner-seeding command
- the seeded account is marked as `PrimaryOwner`

The seeded `PrimaryOwner` record:
- cannot be removed through normal role editing
- cannot be demoted through normal role editing
- should only be transferable through a dedicated explicit flow later if needed

### 4.2 Assignment Authority

- `PrimaryOwner`
  - can assign or remove any role
  - can assign or remove additional `Owner` accounts
- `Owner`
  - can assign or remove `Owner`, `Admin`, `Developer`, `Moderator`, and `User`
  - cannot remove or demote the protected `PrimaryOwner`
- `Admin`
  - can assign or remove `Developer` and `Moderator`
  - cannot assign or remove `Admin`, `Owner`, or `PrimaryOwner`

### 4.3 Role Display

Badge display should appear on:
- account/profile surfaces
- future forum posts
- other platform-facing surfaces where role visibility is useful

Recommended rule:
- `User` has no badge
- `Developer` badge is visible but does not imply admin power

## 5. Admin v1 Authorization Model

Admin v1 should use:
- roles
- route-level access checks
- a small permission enum for server/account/admin actions

Admin v1 should not yet require:
- per-account grant overrides
- per-account revoke overrides
- expiry-based permission records

Those can be added later without changing the role foundations.

## 6. Owner vs Admin Boundaries

Explicit boundaries:
- `Admin` cannot assign or remove `Owner`
- `Admin` cannot assign or remove `PrimaryOwner`
- `Admin` cannot alter bootstrap ownership state
- no one can edit or delete immutable audit records through normal admin UI
- `Moderator` has no listing administration authority
- `Developer` has no admin authority by default

## 7. Admin Panel

### 7.1 Access

`/admin/*` routes require:
- `Admin` tier or higher for admin pages
- `Owner` or `PrimaryOwner` for owner-protected role changes

### 7.2 Admin v1 Pages

Recommended first pages:
- `/admin`
  - runtime summary
  - listing counts
  - account counts
- `/admin/servers`
  - all listings including hidden/delisted/disabled
  - moderation actions
- `/admin/accounts`
  - account list
  - role assignment
- `/admin/audit`
  - append-only admin action log

Current implemented baseline:
- `/admin`
  - registry runtime summary
  - listing counts
  - account counts by role
  - recent account registration windows
  - current players total
  - stale/pending/disabled/lease/poll-candidate counts
- `/admin/servers`
  - all registry records, including disabled and delisted entries
  - `Disable`
  - `Restore`
  - `Delete`
  - `Reset TTL`
- `/admin/bans`
  - active host/IP ban list
  - lift flow
- `/admin/accounts`
  - account list
  - role assignment UI with `PrimaryOwner` protection
- `/admin/audit`
  - append-only audit record view for role changes and listing actions

## 8. Listing Administration

### 8.1 Meaningful Actions

Recommended actions:
- `listing.disable`
  - remove a listing from normal public visibility through admin action
- `listing.restore`
  - restore a disabled listing
- `listing.delete`
  - permanently remove the listing record
- `listing.delist_ttl_reset`
  - reset the retention baseline used for purge timing
- `listing.host_ban`
  - block registration/polling from a host IP
- `listing.host_unban`
  - remove a host/IP block

### 8.2 TTL Reset Semantics

`listing.delist_ttl_reset` means:
- reset the lifecycle expiry baseline for the current trust class
- do not change `HealthState`
- do not change `LifecycleState` if already `Delisted`
- only delay purge/reap

It is a retention extension, not a status restore.

Current implemented behavior:
- `Active` listing
  - reset updates the inactivity baseline so the current delist window starts over
- `Delisted` listing
  - reset updates the purge baseline so the current purge window starts over
- `Disabled` listing
  - reset is rejected

### 8.3 Host/IP Ban Semantics

Host/IP bans are operational friction tools, not strong identity-bound bans.

Important rule:
- IP bans hinder repeated abuse from the same host/network position
- they can be bypassed by infrastructure changes, hosting moves, or IP reassignment

So:
- use host/IP bans as an operational control
- do not treat them as a durable identity ban primitive

### 8.4 Why Not Manual Health Overrides

Do not add actions like:
- force online
- force offline
- force stale

The poller is authoritative for health and will correct those values on the next cycle.
Admin levers should operate on:
- visibility
- retention
- disable/delete state
- host/IP blocking

## 9. Audit Log

All admin actions should require:
- actor account id
- actor role at time
- action type
- target
- reason
- timestamp
- result

Recommended rule:
- audit records are append-only
- audit records are readable by admin/owner roles
- audit records are not editable or deletable through standard UI

## 10. Voting

Voting remains portal-authoritative.

Recommended rule:
- one vote per account per server
- votes can be changed or withdrawn
- portal owns vote records
- registry stores only a persistent projected `VoteCount`

Important distinction:
- current vote totals are already projected into the registry for public list/detail sorting
- the remaining design task is shifting from raw persistent vote count to activity-window-aware vote weight once shared account activity signals exist

## 11. Current Remaining Forum Work

The forum foundation is implemented. What remains is mostly polish, moderation UX, and hardening:

- UX overhaul
  - improve readability, layout density, navigation, and page flow across `/forums/*`
- moderation operations UX
  - richer moderation history/search than the current moderation dashboard and admin audit filters
  - better operator-facing review tools before adding more forum feature breadth
- account activity integration
  - platform account `LastActiveUtc` should be driven by successful game/client login activity too, not just portal web requests
  - this supports future activity-window vote rules and platform-level trust signals
- test coverage expansion
  - forum flows remain the biggest remaining test gap compared with the owner/admin portal paths
- portal vote records are the authority
- registry `VoteCount` is a cached projection for consumers

That projected count should remain persistent across registry restarts, but it is still not the authority.

Recommended v1:
- ship a simple one-vote-per-account model
- allow vote change or withdrawal at any time
- do not add forced monthly revoting in the first slice

Current implemented baseline:
- portal stores `ServerVote` records per account/server pair
- registry stores a persistent projected `VoteCount`
- public list and detail pages surface the current projected vote total
- public list defaults to popularity sort using projected vote count
- signed-in users can vote or withdraw on the server detail page

Reason:
- monthly recast mechanics distort popularity into reminder-campaign behavior
- the simple model is cheaper to ship and easier to reason about

Reasonable later extension:
- add `LastActiveUtc` to platform accounts
- treat successful portal sign-in, future official-auth handshakes, and vote changes as activity touches
- compute projected `VoteCount` from votes belonging to accounts active within a configurable activity window

That keeps:
- portal vote records as the durable source of truth
- registry `VoteCount` as a filtered projection that can evolve later without rewriting the vote model

### 10.1 Sorting

Default public sort may follow a UO Gateway-style popularity sort:
- votes/popularity descending

Alternate sorts should also exist:
- health/status
- current players
- name
- ping/latency later where available

## 11. Metrics

### 11.1 Admin v1 Metrics

Reasonable first metrics:
- total listings
- active/delisted/disabled counts
- online/stale/offline counts
- total registered accounts
- recent registrations
- current players total
- runtime poll health snapshot

Current implemented baseline:
- total listings
- visible listings
- active/delisted/disabled counts
- online/stale/offline/pending counts
- total registered accounts
- recent account registrations for 24 hours / 7 days / 30 days
- recent account activity windows for 24 hours / 7 days / 30 days
- current players total across active listings
- total projected votes across listings
- active leases
- poll candidates
- poll rounds / poll failures
- retained aggregate history metrics across registry samples
  - retained average players
  - retained peak players
  - retained online/offline ratio
  - retained aggregate poll latency
- true advanced platform metrics derived from persisted hourly aggregate samples
  - 24-hour peak players
  - 7-day average players
  - 30-day average players
  - 7-day / 30-day uptime ratio
  - all-time peak players

### 11.2 Later Metrics

These remain valid later goals, beyond the currently implemented advanced baseline:
- broader historical trend views
- richer charting / graph surfaces
- per-server long-horizon trend pages beyond current retained summaries

Those still require broader presentation work even though the first hourly aggregate layer now exists.

Current presentation baseline:
- `/admin` shows recent platform trend rows over the persisted hourly aggregate buckets
- `/servers/{serverId}` shows recent hourly aggregate samples for that individual server

## 12. Future Forums

### 12.1 Scope

Forums are platform forums, not shard forums.

Recommended topic areas:
- platform support
- runtime/server hosting
- registry and portal issues
- development/API requests
- architecture discussion
- creator/operator discussion
- general community discussion

### 12.2 Identity

Forum identity should reuse portal identity:
- one account system
- one role set
- one sign-in

There should not be a separate forum user database.

### 12.3 Moderation

`Moderator` is forum-only.

Moderator scope:
- edit/delete forum posts where allowed
- move or recategorize threads later
- mute or ban users from the forums later

Current implemented baseline:
- moderators/admins/owners can pin, lock, hide, move, and delete forum threads
- moderators/admins/owners can hide, unhide, edit, and delete forum posts with audit logging
- moderation actions require reasons when acting on another user's forum content
- forum posts hidden by moderation are excluded from normal-user thread views and summary counts
- a dedicated `/forums/moderation` dashboard exists for forum moderators with section filtering and quick access to moderated threads/posts

Moderator scope does not include:
- registry moderation
- listing administration
- account role assignment

### 12.4 No Listing-Page Comments

Do not add comments to server listing/detail pages as part of this design.

Community discussion belongs in the platform forum surface, not inside server listing rows/pages.

## 13. Developer Role

`Developer` is earned through accepted contribution to the platform.

Purpose:
- visible contributor recognition
- credibility signal in development discussions
- non-admin community status

It is not:
- self-assigned
- a shortcut to admin power
- a default moderation role

## 14. Recommended Implementation Order

### Phase 1 - Admin foundation
- define roles
- define protected `PrimaryOwner`
- implement manual owner-seeding command
- add audit model

### Phase 2 - Admin v1 UI
- build `/admin`
- build `/admin/servers`
- build `/admin/accounts`
- build `/admin/audit`
- status: implemented locally

### Phase 3 - Listing moderation expansion
- add disable/restore
- add delete
- add retention reset
- add host/IP ban records and lift flow
- current state:
  - disable/restore implemented
  - delete implemented
  - retention reset implemented
  - host/IP bans implemented
  - `/admin/bans` implemented

### Phase 4 - Voting expansion
- add vote records and projected registry counts
- add alternate sort modes

### Phase 5 - Forum foundation
- add forum sections
- add role badges in forum UI
- add moderator actions
- current state:
  - seeded platform forum sections implemented
  - forum index, section, thread, new-thread, reply, edit, move, and delete flows implemented
  - role badges implemented in forum UI
  - moderator thread actions implemented
    - pin/unpin
    - lock/unlock
    - hide/unhide
    - move
    - delete
  - moderator post actions implemented
    - hide/unhide
    - edit
    - delete
  - moderation audit records implemented for forum moderation actions
  - section/thread summaries now exclude hidden posts for normal-user views
  - section thread lists now support search and pagination
  - global forum search now supports title/content/author matching
  - thread views now support post pagination
  - forum moderation dashboard implemented for moderator-tier review workflows
  - `/admin` now surfaces forum volume/state metrics and recent forum thread activity
  - `/admin/accounts/{id}` now surfaces per-account forum metrics plus recent thread/post activity
  - `/admin/audit` now supports forum-only filtering for audit review
  - `/forums/moderation` now surfaces recent forum moderation audit activity alongside moderated content

### Phase 6 - Permission override system
- add account-level grants/revokes
- add expiry-based overrides
- keep role defaults as the base model

## 15. Design Rules To Preserve

- the portal is a platform service, not shard staff tooling
- no GM tooling in the portal
- `PrimaryOwner` is protected and not removable through normal role editing
- `Developer` is a social/contributor role, not an admin role by default
- `Moderator` is forum-only
- admin v1 uses roles first; per-account permission overrides are deferred
- all admin actions require reasons and produce audit records
- host/IP bans are operational friction, not durable identity bans
- vote authority lives in the portal; registry stores only projected counts
- forums are platform forums, not shard forums
- no comments on listing/detail pages
