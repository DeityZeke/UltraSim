# Web Portal and Account Architecture

Status: draft architecture for the local-first web portal, account system, server ownership, and future forum integration. The local portal MVP and initial owner workflows are now implemented.

Related docs:
- `Docs/SERVER_DISCOVERY_AND_REGISTRY_ARCHITECTURE_V2.md`
- `Docs/MINIMUM_CLIENT_SERVER_PLATFORM_CHECKLIST.md`
- `Docs/SERVER_RUNTIME_ARCHITECTURE_AND_ROADMAP.md`
- `Docs/WEB_PORTAL_MVP_IMPLEMENTATION_PLAN.md`
- `Docs/ADMIN_ROLES_AND_FORUMS_ARCHITECTURE.md`

## 1. Purpose

Define the web-facing platform shape for:
- a home/landing page
- a public listings/portal page
- account-backed server ownership and claims
- account-backed voting
- admin tooling
- future forum integration

This doc is intentionally separate from discovery/tracker design.

The registry service and the website are related, but they are not the same subsystem.

## 2. Core Design Rule

Account authority should live with platform services, not inside a CMS.

Specifically:
- server owner accounts are part of the platform/service layer
- admin accounts are part of the platform/service layer
- the website is a consumer and presentation surface for those accounts
- a forum may live inside the website surface later, but should still use the same identity

The website should not become the system of record for:
- server ownership
- server claims
- admin permissions
- moderation authority

## 3. High-Level Direction

Use one shared identity/account domain with multiple consumers:
- registry/admin service
- web portal
- future launcher
- future forum

Recommended shape:
- platform identity/account service owns:
  - users
  - roles
  - server ownership claims
  - admin permissions
  - vote eligibility records
- web portal consumes that identity/account system
- registry service trusts platform identity/account decisions for:
  - verified ownership
  - admin lifecycle/moderation actions

This keeps authority in one place while allowing multiple frontends.

Important distinction:
- the platform account subsystem owns authentication and account lifecycle
- the registry stores only the minimum account-linked references it needs for ownership and admin policy
- the registry is not the primary user account system

## 4. Subsystem Split

Recommended split:

- `UltraSim.Registry`
  - discovery and polling
  - listing state
  - trust/health/lifecycle policy
  - admin and owner-facing service endpoints
  - minimal owner/admin account references on claimed records

- platform account subsystem
  - user accounts
  - authentication
  - roles/claims
  - server ownership records
  - voting records

- web portal
  - landing page
  - listings page
  - account/profile pages
  - server owner pages
  - admin pages

- future forum
  - separate feature area
  - same account identity
  - not a separate user database

## 5. Account Model

One user account should be usable across:
- website sign-in
- server owner workflows
- admin tooling
- future forum posting

Recommended identity roles:
- `User`
  - normal website account
  - can vote where allowed
- `ServerOwner`
  - can claim/manage owned servers
- `Admin`
  - can access admin tooling
  - can disable/restore/moderate listings

Important distinction:
- account identity is shared
- permissions are role/claim based
- not every website user is a server owner
- not every server owner is an admin

## 6. Ownership and Claims

Server ownership should belong to the service/platform layer, not the website content layer.

Recommended records:
- `UserAccount`
- `ServerOwnershipClaim`
- `ServerOwnershipVerification`
- `AdminRoleAssignment`
- `UserVote`

Recommended ownership flow:
1. server may already appear as a temporary unverified listing without any portal interaction
2. user signs in
3. user goes to `My Servers` / `Add Server`
4. user claims or promotes a listing by submitting metadata plus server proof/token
5. platform verifies the claim by service-controlled proof
6. ownership record is attached to the account
7. registry/admin endpoints trust that ownership record

The website may present this workflow, but it should not define the authority itself.

Important rule:
- portal claim/promotion is not the only way a listing can exist
- it is the way a temporary listing becomes owner-backed and verified

Current owner workflow shape:
- `My Servers` lives inside the account/manage shell
- owners can add, edit, and delete claimed listings from the portal
- delete removes the claimed registry entry immediately; the next server boot/publish may reintroduce the listing as `Unverified`
- editing a claimed listing updates registry-facing owner metadata rather than waiting for the server to change its default advertised text
- the raw ownership token is submitted through the portal but is not retained in portal storage after the request is sent
- owner token updates should move the listing back into pending revalidation until the next successful poll confirms the new proof
- if a claimed server's proof no longer matches, the public listing is demoted back to `Unverified` until revalidated

## 7. Voting

Voting should also be account-backed at the platform layer.

Recommended policy:
- one vote per account per target server
- votes can be changed or withdrawn later if desired
- vote records live in the platform data model, not in website-only state

This allows:
- website voting
- future launcher voting if desired
- consistent anti-abuse rules

## 8. Admin Tooling

Admin tooling is part of the platform/service surface first, and part of the website second.

Meaning:
- the service owns admin actions and authorization
- the website provides admin UI over those service capabilities

Examples:
- disable/restore listing
- inspect delisted records
- inspect ownership claims
- inspect vote abuse/moderation history later

Do not make the website the sole authority for admin state.

## 9. Website Role

The web portal is a consumer surface for platform capabilities.

Recommended website areas:
- landing/home
- server listings portal
- server detail pages
- account dashboard
- owned/claimed server management
- admin pages
- future forum

The listings portal should consume the same normalized discovery/detail APIs as other consumers.

This gives a second real surface for:
- validating listing behavior
- validating static vs dynamic registry data
- spotting mismatches earlier than the game client alone

Current local MVP already provides:
- landing page
- live public listings/detail pages
- local sign-in/account shell
- `My Servers` add/edit/delete workflow
- registry-backed owner verification status sync
- token revalidation flow between portal and registry
- startup cleanup of any legacy stored ownership tokens in the local portal DB
- admin dashboard, accounts, audit, and server moderation pages
- portal-side `ServerVote` records
- public vote totals via registry-projected `VoteCount`
- vote / withdraw actions on server detail pages

## 10. Forum Integration

The forum should share identity with the rest of the platform.

Recommended rule:
- one account system
- one sign-in experience
- one role/claim authority

This does not require the forum to be part of the same executable, but it should not have a separate user database if avoidable.

Current implemented baseline:
- platform forum foundation now lives in `UltraSim.Portal`
- sections, threads, replies, moderation, pagination, and search are in place
- admin/account visibility surfaces exist for forum activity and moderation audit history

Current remaining work:
- forum UX overhaul rather than more basic scaffolding
- richer moderation history/search tooling
- long-horizon forum management tools such as split/merge or stronger moderation workflows later
- shared account activity updates from the actual game/client login path so platform activity is not web-only
- activity-window vote eligibility/projection rules built on that shared account activity signal

## 11. CMS Guidance

Do not use WordPress or Joomla as the system of record for platform identity or ownership.

They may still be useful later for:
- marketing pages
- editorial content
- documentation sections
- a forum integration layer if absolutely needed

But they should sit on top of the platform account system, not replace it.

If a CMS is used later:
- it may host pages and forum UX
- it may consume shared platform identity
- it should not become the source of truth for accounts, ownership, or admin authority

Local-first recommendation:
- build the platform account and portal stack locally first
- keep it unpublished/publicly inaccessible until the flows are coherent
- only evaluate CMS/forum integration after the account model is stable

## 12. Local-First Implementation Shape

Recommended first implementation:
- local ASP.NET Core web app
- local platform account store
- local registry service
- local admin pages
- local owner claim/promotion pages

Current implemented baseline:
- `Services/UltraSim.Portal/` Razor Pages app
- ASP.NET Core Identity + SQLite
- public server list/detail pages backed by the registry
- public server list page is sortable by votes, name, state, uptime, and verified state
- public server list page uses compact metadata pills for links, region/language, whitelist, and tags
- server detail page exposes retained history, latency, and runtime-observed metrics from registry detail data
- server detail page now leads with condensed public state, registration state, connect target, and compact metadata/link pills
- server detail page exposes true per-server 24h / 7d / 30d metrics and per-server all-time peak from persisted hourly registry aggregates
- server detail page exposes recent hourly aggregate samples for trend inspection
- owner claim/promotion records stored locally in the portal DB
- vote records stored locally in the portal DB
- registry claim, owner metadata update, and owner delete integration
- registry vote-count projection integration
- registry owner token update / revalidation integration
- admin role/bootstrap foundation
- admin dashboard/accounts/audit pages
- admin dashboard metrics for recent account registrations and current runtime counts
- admin dashboard retained aggregate history metrics sourced from registry samples
- admin dashboard true advanced time-window metrics sourced from persisted hourly registry aggregates
- account activity tracking via `LastActiveUtc` in portal identity storage
- admin disable/restore/delete/reset TTL flows
- platform forum foundation integrated into the same portal and identity store
  - seeded forum sections
  - section/thread/reply UI
  - section search and pagination
  - global forum search across titles, post content, and author identity
  - thread post pagination
  - role badges on forum authors
  - moderator thread/post actions with audit logging
  - moderator dashboard for forum review
  - author edit/delete flows where allowed
  - admin dashboard forum metrics and recent forum thread visibility
  - admin account detail forum metrics and recent authored thread/post visibility
  - admin audit forum-only filtering and forum moderation dashboard audit visibility
  - local portal test project covering authorization, audit persistence, registry client behavior, and owner page-model workflows

Initial goal is not public launch.

Initial goal is:
- prove the portal against the live registry
- prove ownership/admin flows
- prove shared account behavior

## 13. Data Ownership Rules

Service/platform owns:
- user accounts
- roles and claims
- server ownership
- admin actions
- vote records
- registry listing state

Website owns:
- page layout
- portal UX
- presentation
- future forum UX

Website does not own:
- account authority
- server claim authority
- admin authority

The registry may still store:
- owner account id references
- claim/verification state
- admin action actor ids

Those are policy references, not full account ownership of the identity system.

## 14. Recommended Implementation Order

### Phase 1 - Architecture and account foundation
- define shared account model
- define server ownership claim model
- define admin role model
- define vote model

### Phase 2 - Local portal MVP
- build landing page
- build listings/portal page against the registry
- build sign-in/account pages
- status: implemented

### Phase 3 - Owner and admin workflows
- build server claim/promotion UI
- build owned server management UI
- build admin lifecycle/moderation UI
- current state:
  - claim/promotion UI implemented
  - owned server edit/delete implemented
  - owner token revalidation UI implemented
  - admin dashboard/accounts/audit implemented
  - admin dashboard runtime/account metrics expanded
  - portal account activity timestamps tracked
  - server detail metrics/history surface expanded
  - admin disable/restore/delete/reset TTL implemented
  - admin host/IP bans and `/admin/bans` implemented

### Phase 4 - Shared identity expansion
- use the same identity for launcher-facing workflows where useful
- prepare forum integration against the same accounts
- current state:
  - forum identity now reuses the same portal accounts, roles, and sign-in flow

### Phase 5 - Forum/content integration
- evaluate whether a dedicated forum package or CMS integration is still needed
- keep platform identity as the authority regardless
- current state:
  - custom portal-hosted forum foundation exists locally, so dedicated forum/CMS integration is no longer required for the initial platform forum slice

## 15. Design Rules To Preserve

- registry/discovery is not the website
- website is not the authority for ownership or admin state
- server owner accounts are part of the platform/service layer
- admin accounts are part of the platform/service layer
- one account system should span portal, owner tools, admin tools, and forum
- temporary unverified listings can exist without the portal
- portal claim/promotion upgrades temporary listings into owner-backed verified listings
- owner metadata edits should update the claimed registry record
- owner token changes should force revalidation rather than silently preserving prior verification
- same discovery/detail data should power game, launcher, and website
- local-first before public exposure
