# Client Content

`UltraSim.Client.Content` is the engine-neutral A0.0-A0.7 contract, deterministic resolver,
persistent store, and source-independent acquisition transaction for passive client content. It
contains no Godot or Network dependency.

The accepted boundary is:

```text
scope selection / authenticated Game authority
    ContentScopeKey.Local
    OR ContentScopeKey.ForRealm(authenticated ApplicationRealmId)
    ContentActivationId + PlanHash
    exact required base fingerprint
    ordered PackageId + ManifestHash + Required/Optional
            |
            v
Client.Content
    validate immutable package identity and passive payloads
    apply Add/Replace in authority order
    publish immutable resolved catalog generation
    produce exact resolved fingerprint/application proof vocabulary
            |
            v
Godot realization (later checkpoint)
```

Logical ids are strict lowercase ASCII `namespace:path/segments`. File paths are package-local
addresses, never semantic identity. Exact SHA-256 identifies immutable blobs globally;
`ContentScopeKey + ContentActivationId` owns authorization and detects reuse of an activation
identity with different plan bytes. Remote wire/application proof remains explicitly
`ApplicationRealmId`-shaped and rejects Local plans at that boundary. Download URLs, local paths, connection identity, and
process-local catalog-generation handles do not enter resolved fingerprints.

The catalog is above the renderer-owned primitive/bootstrap emergency substrate. Local/default
content is optional ordinary verified content and may be absent without changing Game semantics;
primitive fallback is not a package and never satisfies a missing required activation or
application proof. Server-authorized Add/Replace content and explicitly enabled user presentation
overlays resolve above local/default content while preserving independent model/material fallback.

Payload validation produces a `VerifiedContentPackage` capability. Base-catalog construction and
activation resolution accept that capability, not a bare manifest, so parsed metadata or
partially/hash-failed bytes cannot become publishable catalog content.

The source-neutral acquirer accepts cancellation across mirror/package/file boundaries. Blocking
HTTP and rooted-local reads observe that cancellation; the remote client uses it to keep cold
acquisition outside the protocol/UI owner while still joining the operation before releasing the
shared source/cache lifetime. Catalog publication remains immutable and application proof remains
an owner/session concern above this component.

## F1.2 semantic activation and presentation generations

`AuthorizedSemanticActivation` identifies exact scope, activation, plan, resolved semantic
fingerprint, and application epoch. It does not contain actor-art packages. Concept Demo requires
the WorldPack and compiled Inventory/Server Monitor UI from its bootstrap package; absence still
fails the application proof. Its actor Add/Replace packages instead form a distinct Realm
presentation plan. Authenticated Game message 149 advertises that plan and its exact expected
resolved fingerprint, but never enters the required-content proof or guest-admission gate.

`PresentationGeneration` resolves the two current humanoid logical IDs once per publication with
User → Realm → `world.local` → Primitive precedence. It validates selected GLBs against the same passive
and skeleton/Idle-Walk-Jump rules, holds the selected catalog/blob leases, and gives native
consumers reference-counted leases. The monotonic generation ID fences live asynchronous/native
work; the effective-selection SHA-256 fingerprint is stable across process runs and excludes that
counter. Primitive is still renderer code, not an authorized package. F1.3 adds the finite
`ClientPrimitivePresentation` contract below Content for already-disclosed actors, items,
public objects, frozen statics, and Multi fragments. It cannot satisfy required activation or
application proof.

F1.4 productizes the finite compiled `OfficialLocalPresentation` identity as
`ultrasim-official-actors-v1.uspkg`. Single Player and Complete place that deterministic archive
beside the product under `Content/Official`; it is never part of the PCK or a managed embedded
resource. Local reads the install root through `LocalContentPackageSource`, installs into the
writable global CAS under `ContentScopeKey.Local`, reopens the persistently pinned activation on
warm starts, and publishes an ordinary local presentation catalog. Absent, corrupt, or mismatched
Official bytes fail only that optional layer and leave Primitive available. Online ships without
the archive, while an explicitly installed Local package may participate below authenticated Realm
art without satisfying Realm authorization or application proof.

The distribution source and installed authorization are deliberately different lifetimes. The
read-only archive is used to install or repair the exact product-owned activation in the writable
user cache. Removing the source archive does not revoke an activation already persisted there;
scope retirement removes authorization metadata, and ordinary reachability-aware eviction may
later reclaim an unreferenced blob after all catalog/blob/native leases retire. F1.4 adds no live
uninstall or general pack manager.

## F1.5 user presentation overlays

`UserResourcePackIdentity` is exact `ContentPackageId + ManifestHash`; a filesystem path is only a
rooted local source locator. `TryCreateUserResourcePack` admits one to sixteen bounded,
self-contained actor GLBs and the same number of unique ActorPresentation `Replace` definitions.
Every file must be referenced exactly as an override leaf. Add, WorldPack, UI definitions,
textures, arbitrary resources, external GLB references, scripts/shaders/executables, and every
other current kind fail admission. Texture/material overrides remain deferred because current
Cabin materials do not yet resolve through the common presentation generation.

`TryInstallUserResourcePack` validates and installs exact immutable bytes without creating Local
or Realm authorization. `TrySetEnabledUserResourcePacks` persists at most sixteen installed exact
identities in deterministic low-to-high priority order; corrupt metadata fails closed to no User
layer. `TryOpenEnabledUserResourcePacks` validates and pins all manifests/blobs before returning an
immutable stack lease. Highest compatible pack wins. Missing logical IDs are inactive; a selected
known actor with corrupt/incompatible art rejects the complete candidate generation, leaving the
published generation usable.

The stable presentation fingerprint covers the effective logical-ID/file-hash result, not enabled
packs that have no effect, source paths, source layer, timestamps, or process-local generation ID.
Exact identical effective bytes therefore share identity across User/Realm/Local. Enable, disable,
reorder, and immutable update publish new generations only when the effective result changes.
Old native actor leases retain old generation/blob ownership. Remote semantic activation and proof
remain unchanged, and the existing remote acquisition revision/session fences continue to reject
stale asynchronous Realm work; user pack selection introduces no remote protocol or async loader.

## A0.0 formats and operations

The current passive manifest permits only:

- typed Texture and Icon definitions backed by PNG/WebP;
- declarative `StandardLit` materials;
- typed complete WorldPack definitions backed by World-owned canonical artifacts;
- canonical `.usui` compiled UI-definition artifacts retaining UI-kernel key/version/hash identity;
- typed ActorPresentation definitions backed by the reviewed self-contained binary glTF 2.0
  humanoid profile;
- `Add` and complete `Replace` operations.

Raster admission performs exact path/size/SHA verification plus a bounded passive envelope check;
it is classification before a later hardened decoder/engine realization and is not a claim that
arbitrary image bytes are safe. WorldPack admission delegates to `Game.World`'s complete bounded
container/hash/schema validator instead of defining a second WorldPack format here.
Actor GLB admission bounds and validates its container, graph, meshes, accessors, embedded buffer,
one skeleton, and finite animation vocabulary with exactly one normalized occurrence of each clip;
the Concept Demo resolver additionally requires the exact ordered 65-bone-name profile and
Idle/Walk/Jump clips before an applied-catalog blob lease can reach Godot. The fingerprint covers
ordered bone names, not parent topology, rest transforms, or inverse-bind matrices; arbitrary
humanoid retargeting remains unsupported.

Generic Godot resources, scenes, scripts, shaders, executable code, arbitrary GLB/audio, partial
Patch, dependencies/version solving, and executable extensions remain unsupported. The one actor
GLB family rejects URI/external resources, images/textures, cameras, and unsupported extensions; it
is not permission to pass downloaded scenes or arbitrary resources to `ResourceLoader`. User
resource packs and executable extensions have vocabulary values so they cannot be confused with
normal server-passive trust. The normal manifest factory still rejects both; F1.5 exposes a
separate strict factory for user ActorPresentation Replace packages only. Executable content
remains rejected.

## A0.2 persistent cache / F1.1 unified scopes

The accepted source-independent cache uses only fixed, validated identities in physical paths:

```text
root/
    blobs/sha256/ab/<64-lowercase-hex>.blob
    scopes/local/
        manifests/<ManifestHash>.manifest
        activations/<ContentActivationId>.activation
    realms/<32-lowercase-hex ApplicationRealmId>/
        manifests/<ManifestHash>.manifest
        activations/<ContentActivationId>.activation
    resource-packs/
        manifests/<ManifestHash>.manifest
        selection/enabled.selection
    metadata/access/<BlobHash>.access
    staging/<opaque temporary name>.tmp
    writer.lock
```

Manifest-controlled relative paths and source URLs never become cache paths. Exact blobs deduplicate
globally, but manifest and activation metadata remain scope-owned: physical possession of bytes
does not authorize Local or another realm to publish them. F1.1 preserves the accepted `realms/`
layout and version-1 remote activation bytes for backward-compatible reopen; the reserved Local
scope is added under `scopes/local/`. There is no text/path constructor capable of turning a remote
realm into Local. Both scope kinds use the same install, manifest, activation, catalog, lease, and
eviction implementation.

The cache continues to admit only numeric trust class 2 activation packages for both Local and
Realm scopes. `ScopeAuthorizedPassive` is the scope-neutral source name;
`ServerAuthorizedPassive` remains its exact numeric alias for canonical/wire compatibility. Local
selects who authorizes/mounts the package; it does not bypass passive validation.
Primitive fallback remains below Content and is never a package. `BuiltIn` packages may still form
the verified base catalog but are not silently admitted as ordinary cached activation packages.
F1.5 resource packs are not activation scopes. Their exact manifests and bounded ordered enabled
selection occupy the separate `resource-packs/` namespace while their blobs reuse the same global
CAS. Installed resource-pack blobs remain reachable; explicit retirement refuses enabled or live
leased identities, removes only pack metadata after release, and leaves later CAS reclamation
separate. Executable content remains unsupported.

Install validates supplied payloads through the A0.0 passive allowlist. A full-package call
requires the complete payload set; A0.5's delta call requires every missing/corrupt unique hash and
permits already-valid CAS hashes to be omitted. New or repaired blobs are written and flushed
through private staging names, verified by their descriptors, and atomically moved before the
scope manifest becomes visible. A crash may leave an unreferenced
immutable blob; it cannot leave a published partial package, and abandoned staging is removed on
the next open. Canonical binary manifest/activation metadata is strictly decoded and re-encoded on
reopen. No source location is persisted.

Catalog publication reloads every selected package from the cache, resolves privately, persists
the exact scope/activation/plan/result tuple, and only then returns a new immutable catalog
generation. Restart reopening repeats that validation and must reproduce the stored fingerprint.
Required packages fail closed; primitive/local presentation fallback does not manufacture a
successful activation.

Quota eviction is reachability-based and oldest-access-first. Persistently pinned activations,
live catalog leases, and live blob/resource leases protect their manifests and blobs. Unpinned
activation/manifest metadata is retired before an unreachable blob is deleted. If enough safe
space cannot be reclaimed, admission fails without partial arbitrary eviction. A0.5 repairs a
corrupt blob or canonical scope manifest only from a newly hash-validated candidate; a live-pinned
artifact returns `CacheBusy` rather than being replaced underneath its consumer.

The initial implementation gives one owning client process exclusive mutation ownership for a
cache root. Multiple simultaneously running clients use distinct roots until cross-process live-pin
coordination is explicitly earned; this does not alter the canonical on-disk identities.

F1.1 acceptance installs the Cabin Replace/Add passive packages into `world.local`, publishes and
warm-reopens its Default/Replace/Add catalogs through `ContentCache`, and compares their exact
logical entries with the frozen oracle. Local and two Realm scopes reference one physical shared
blob while retaining separate manifests. Explicit scope retirement refuses live catalog pins,
removes only the selected scope's authorization metadata, and leaves shared CAS bytes available to
the remaining authorized scope. The current store still has one writable physical root; typed
scope ownership no longer blocks a later read-only install-root composition, but F1.1 does not add
that virtual filesystem.

## Current boundary

A0.0 provides contracts, validation, local package descriptors, deterministic base/activation
resolution, immutable catalog generations, and application-proof values. A0.2 persists verified
server-passive packages and resolved activations without acquiring them from any source. The
accepted A0.1 Cabin Default/Replace/Add oracle lives in the optional
[`Client.BuiltInTestWorlds`](../BuiltInTestWorlds/README.md) capability rather than coupling the
generic resolver to one Test World.

A0.3 adds `IContentPackageSource` and `ContentActivationAcquirer`. An authenticated adapter supplies
only approved source descriptors; the acquirer reuses an exact realm manifest when available or
fetches one bounded untrusted package candidate, checks package/manifest identity, installs through
A0.2, and publishes the complete catalog last. The content assembly still does not know Network or
Godot.

A0.4 adds one canonical passive `.uspkg` byte envelope plus concrete rooted-local and HTTP(S)
sources. The envelope contains the canonical manifest followed by exact ordered file paths and
payload bytes; decode requires the expected package/manifest identity, complete canonical order,
bounded sizes, exact SHA-256, and the existing passive-format admission. It is a delivery envelope,
not a second package or catalog model.

`LocalContentPackageSource` admits safe package-relative paths beneath one configured ordinary
directory and rejects reparse-point traversal. `HttpContentPackageSource` disables redirects,
automatic decompression, and cookies; bounds response size and request time; requires HTTPS for
external sources; and requires explicit policy for plaintext server-hosted HTTP or literal
loopback/private targets. Its current default archive cap is 768 MiB and timeout is 30 seconds;
these are bounded implementation profiles, not permanent product limits.

A0.5 keeps that full `.uspkg` route for compatibility and adds a hash-granular source layout:

```text
authorized package source root/
    manifest.usmanifest
    blobs/<SHA-256>.blob
```

The manifest envelope is canonical but carries no new semantic identity. The client verifies the
expected package/manifest, preflights the global CAS, and requests only unique invalid or missing
hashes. Up to four file requests run concurrently by default; one mirror is completely validated
in private memory before any of its bytes are installed. Approved mirrors are attempted in order,
up to eight attempts per package by default. Unavailable, interrupted, or hash-invalid candidates
do not publish partial realm state and may fall through to the next mirror. Required packages fail
closed after exhaustion; unavailable Optional packages publish the explicit fallback catalog and
may be applied later only by a new explicit catalog publication.

The production-owned HTTP handler resolves hostnames inside its connection callback, rejects an
entire resolution containing loopback/private/link-local targets unless explicitly permitted, and
connects to one of those exact admitted addresses. Redirects remain disabled, so a textual public
hostname cannot bypass policy through DNS rebinding or a redirect. Source URLs still do not enter
package, plan, or catalog identity. The activation transaction remains synchronous; its
concurrency is bounded file acquisition, not a background content framework. Native resource
realization remains later work.

## A0.6 typed UI-definition convergence

`.usui` is a canonical, bounded passive artifact for one `CompiledUiDefinition`. It preserves the
UI kernel's exact `UiDefinitionKey + version + hash`; it is deliberately not assigned a generic
content logical ID. Passive validation decodes and canonically re-encodes the complete artifact
before a package can install. Its package-local `ui/<UiDefinitionHash>.usui` address is a canonical
lookup convention, not semantic identity; the decoded key/version/hash must still match exactly.

`ContentUiDefinitionResolver` searches only the packages selected by one live applied realm
catalog lease and opens their exact CAS blobs. A globally present blob, a manifest authorized to a
different realm, or an unselected package is insufficient. Duplicate exact typed identities fail
closed. The resolved definition may prime Client Core's bounded definition cache, but it carries no
model values, application action endpoint, or executable behavior.

The remote UI adapter reuses its existing `HotOpen` semantic operation for an exact definition the
authority declared content-backed after content application. Game still creates the authoritative
model and binds every action handler; a missing, corrupt, or mismatched client definition rejects
semantic application and desynchronizes the view rather than falling back to invented actions.
Rooted local archives, process-reopened warm cache, server-hosted HTTP, and external HTTPS all
produce the same canonical Inventory definition and resolved catalog fingerprint.

## A0.7 typed WorldPack resolution

`ContentWorldPackResolver` resolves one `ContentDefinitionKind.WorldPack` only through a live
applied realm catalog lease and the exact selected owner package. It opens the selected CAS blob as
a stream, then hands ownership back to World for canonical validation, exact pack/content identity,
and `WorldContentBinding`. A global cache hit or a base-catalog definition not selected by the
authenticated activation grants no structural authority.

Content blob SHA-256, `WorldPackFingerprint`, and `WorldContentFingerprint` remain distinct. The
resolver does not publish semantic chunks, create renderer resources, interpret World sections, or
define downloadable overlays/chunks. Those remain World, Client Core, and later World-artifact
contracts respectively.

See [`ASSET_STREAMING.md`](../../Docs/Plans/ASSET_STREAMING.md) for the owning plan. A0.8 now
consumes this resolver in the native Online product; Client Content still does not own structural
publication or Godot realization.
