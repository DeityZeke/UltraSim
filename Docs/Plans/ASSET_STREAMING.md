# UltraSim Asset Streaming / Content Layering Plan

**Status:** frozen ownership/security direction with A0.0 content contracts/resolution, the
local A0.1 Cabin Default/Replace/Add oracle, A0.2 persistent CAS/install/reopen, A0.3
authenticated requirement/acquisition/application proof, A0.4 remote semantic/source parity,
A0.5 cache/delta/corruption/mirror hardening, A0.6 typed UI schema convergence, A0.7 complete
canonical WorldPack streaming, and A0.8 native Contentless Online product closure accepted,
companion to [`CLIENT.md`](CLIENT.md). The client executable/local-session/shell foundation
(F0.0a/F0.0b/F0.0c/F0.1/F0.2) and A0.0-A0.8 are closed.
Client F0.6's historical content-source/Contentless Online gate is therefore also closed and
superseded by this accepted A0 campaign; no duplicate F0.6 implementation remains.
Client F0.5 is also closed: one passive stock Inventory artifact, reopened CAS bytes, and newly
acquired realm-content bytes converge on the same exact compiled definition and runtime while
content authorization/application proof remains independent.
D0.7a supplies safe bundled X/Y GLBs and shared actor animation/equipment/action semantics. D0.7b
now adds the first narrow runtime model consumer: bounded self-contained actor GLB, logical
ActorPresentation Add/Replace definitions, applied-catalog-only resolution, retained CAS leases,
and lazy Godot realization. General mesh/animation/audio import, package dependencies, executable
extensions, mutable overlay artifacts, and independently streamed chunk artifacts remain deferred.

F1.2 separates this historical actor-art path from required semantic application. The Concept
Demo's exact required plan now contains only its WorldPack and compiled Inventory/Server Monitor
UI bootstrap package. Actor Add/Replace packages remain exact Realm-authorized passive content,
but an independent message-149 presentation advertisement acquires/publishes them without
participating in application proof or guest admission. The optional Realm layer resolves above
`world.local` and Primitive; corrupt/missing/incompatible art cannot replace required semantic
truth. Game manifest v31 added message 149; current v32 adds only the declarative F1.8 client
camera-profile encoding. Network transport revision remains 2.

**Purpose:** restore the useful V2 content-pack, resolver, cache, server-patch, and asset-streaming ideas without reviving old ownership mistakes. The first proof uses the same Cabin in the Woods world locally and remotely so a preinstalled Single Player composition can act as a deterministic oracle for Online content.

The central decisions are:

> **Content source/trust and content semantic operation are different axes.**
>
> Built-in assets, server-authorized streamed assets, user resource packs, and future executable client extensions answer **where content came from / what trust and policy applies**. Default, Replace, Add/Append, and future typed partial Patch answer **what a content definition does to the resolved catalog**.

and:

> **The authoritative game server decides what exact content is required. The place that serves the bytes is only a byte source.**
>
> A package may be read locally, served by the game server's static-content endpoint, or downloaded from one or more external HTTPS/CDN/object-storage mirrors. Source location never replaces package identity, hashes, or client validation.

Do not collapse trust/source, semantic operation, and physical byte delivery into one generic "mod/patch" concept.


## Canonical multi-Pack structural delivery direction

The canonical World composition direction is now owned by
[WORLD_COMPOSITION.md](WORLD_COMPOSITION.md). A remote World eventually authenticates one exact
immutable WorldStructuralManifest whose sparse Pack index is structural composition truth; the
client does **not** need every referenced WorldPack byte before admission when demand-based
structural policy permits streaming only the starting/AOI subset.

The permanent delivery distinction is:

```text
manifest has no Pack entry
    = authoritatively unauthored Pack / hard World boundary

manifest has Pack entry
but Pack artifact is not acquired/prepared
    = structurally known but unavailable
    = residency/acquisition state
    != hard World boundary

Pack prepared but chunk missing
    = authoritatively unauthored chunk

authored chunk + Absent base cell
    = intentional known void
```

Target remote flow:

```text
authenticate exact WorldStructuralManifest
    ↓
resolve starting/AOI global SpatialChunkKeys
    ↓
resolve owning WorldPackCoord/manifest entries
    ↓
acquire/verify/open only required Pack artifacts
    ↓
publish resident semantic structural chunks
```

The manifest is admission-critical semantic composition identity. Individual Pack byte residency
is a bounded cache/streaming concern below World identity. Changing or acquiring one Pack must
not require redownloading unrelated Packs, and possession of Pack bytes still does not replace
realm/content authorization.

Current A0.7 complete-WorldPack streaming remains the accepted one-Pack remote
implementation baseline. W5R migrated actual Concept Demo authority and Local
presentation to a complete two-Pack composition, while pre-W7 remote acquisition
still transfers the byte-identical historical combined Pack as a delivery-only
compatibility artifact. W7 is the planned transition to manifest-authenticated,
demand-based multi-Pack acquisition after the World layer implements W6's
explicit structural-state vocabulary.

---

# 1. Archaeology recovered from V2

The archived V2 client/content work had significantly more real implementation than a placeholder design.

Useful references:

- `_Archive/_V2/Client/CONTENT_PACK_PIPELINE_PLAN.md`
- `_Archive/_V2/Client/GODOT_CLIENT_SHELL_ROADMAP.md`
- `_Archive/_V2/Client/README.md`
- `_Archive/_V2/Client/Core/Content/*`
- `_Archive/_V1.5/Docs/CLIENT_RUNTIME_ARCHITECTURE_SPEC.md`
- `_Archive/_V1.5/Docs/CHUNK_TERRAIN_STATICS_IMPLEMENTATION_GUIDE.md`

Recovered ideas include:

- stable logical content ids;
- file entries separate from semantic definitions;
- partial packs rather than giant complete replacements;
- primitive emergency fallback plus optional local/default content;
- layered logical-id resolution;
- resource-pack remaps;
- server-scoped content/cache namespaces;
- content-pack dependencies and filters;
- package/pipeline compatibility ranges;
- per-file SHA-256 integrity;
- whole-package content identity;
- `append`, `replace`, and `patch` definition modes;
- required versus optional server content;
- server advertisement followed by missing-file download;
- downloading from the game server **or a declared download URL**;
- file-level delta behavior through hash comparison;
- `.uspkg` archive/folder source abstraction;
- server patch rejection of obvious executable/script files;
- cache metadata/indexing;
- pinning and quota/eviction planning;
- atomic temporary-file replacement;
- UI schema caching on the same server identity/cache substrate;
- terrain/static chunk patch hooks separate from realtime actor replication;
- explicit separation between user resource packs, server patches, and executable client mods.

V2 also had two partially overlapping representations:

1. `ContentPackManifest` — rich logical definitions, files, dependencies, filters, definition modes.
2. `ServerPatchManifest` — server-specific file list/revision/cache validation.

V3 should **not** recreate two competing semantic notions of a patch. The server delivery protocol should advertise/install validated content packages whose file hashes and logical definitions are understood by the same package pipeline used for local/preinstalled packs. A small transport/download advertisement may still exist, but it points to the canonical package/content identity rather than becoming a second definition system.

## Current V3 baseline

The current repository now has the first source-free content pipeline:

- World owns canonical complete `WorldPackArtifact` bytes/fingerprints and bounded open/validation;
- World owns mutable overlay revisions and per-chunk content hashes plus persistence serialization,
  but no public independently downloadable chunk artifact;
- Client Core/UI retain typed definition/cache identity and generation-aware semantic state;
- Client Content owns canonical passive manifests, exact payload verification, deterministic
  activation/catalog resolution, and the A0.2 persistent CAS described below;
- the playable Cabin and X/Y calibration assets are currently build-time/fixture composition;
- authenticated remote content requirements, source-neutral package acquisition, persistent cache
  reuse, immutable catalog publication, and session-fenced `ContentSetApplied` now exist;
- canonical package envelopes, rooted local files, bounded server-hosted HTTP, and external HTTPS
  now converge on the same verified package/catalog identity;
- mirror retry/repair, lazy downloaded-resource realization, UI schemas, and WorldPack streaming
  remain later gates.

A0 extracts/builds those missing content contracts. It does not reinterpret current persistence,
UI cache keys, or World chunk hashes as if they already formed a streaming package system.

---

# 2. Frozen principles

```text
CONTENT IDENTITY IS LOGICAL; FILE PATH IS NOT GAME/PRESENTATION IDENTITY.

CONTENT FILES AND CONTENT DEFINITIONS ARE DIFFERENT THINGS.

A PACKAGE MAY BE PARTIAL.

PRIMITIVE EMERGENCY PRESENTATION IS BELOW THE CONTENT SYSTEM AND REMAINS AVAILABLE WITHOUT A CONTENT PACK.

LOCAL/DEFAULT CONTENT IS OPTIONAL ORDINARY VERIFIED CONTENT, NOT AUTHORITY OR PRIVILEGED PRESENTATION CODE.

NORMAL SERVER CONTENT IS PASSIVE, ALLOWLISTED DATA/ASSETS.

CONTENT VALIDATION CLASSIFIES ALLOWED PASSIVE CONTENT; IT DOES NOT CLAIM TO PROVE ARBITRARY BYTES ARE SAFE.

AN UNKNOWN OR CODE-CAPABLE FILE DOES NOT SILENTLY UPGRADE A NORMAL CONTENT PACKAGE INTO AN EXECUTABLE MOD PACKAGE.

USER RESOURCE PACKS ARE LOCAL PRESENTATION REMAPS, NOT GAME AUTHORITY.

EXECUTABLE CLIENT EXTENSIONS, IF EVER SUPPORTED, ARE A SEPARATE TRUST CLASS, PACKAGE TYPE, INSTALL ROOT, LIFECYCLE, AND CONSENT PATH.

A SERVER MAY ADVERTISE AN EXECUTABLE CLIENT EXTENSION AS OPTIONAL OR REQUIRED, BUT MAY NEVER SILENTLY INSTALL OR EXECUTE IT.

REFUSING A REQUIRED EXECUTABLE EXTENSION MEANS REFUSING TO JOIN THAT SERVER.

FUTURE EXECUTABLE EXTENSIONS USE PRECOMPILED WASM OR PRECOMPILED .NET DLL PAYLOADS; THE STOCK CLIENT DOES NOT SHIP A SOURCE COMPILER FOR SERVER MODS.

SIGNATURES/PUBLISHER IDENTITY PROVE PROVENANCE, NOT SAFETY.

SERVER AUTHORS SHOULD PREFER SERVER GAME CODE + SERVER-COMPOSED UI + DECLARATIVE CLIENT PROFILES + CONTENT ADD/REPLACE FOR EVERYTHING EXPRESSIBLE THROUGH THOSE SURFACES.

CONTENT SOURCE/TRUST AND CONTENT SEMANTIC OPERATION ARE SEPARATE AXES.

CONTENT AUTHORITY AND CONTENT BYTE ORIGIN ARE SEPARATE AXES.

THE AUTHENTICATED GAME SERVER AUTHORIZES PACKAGE IDENTITY/REVISION/HASH AND REQUIRED/OPTIONAL POLICY.

A CONTENT HOST SUPPLIES BYTES; IT DOES NOT REDEFINE PACKAGE IDENTITY OR APPLICATION SEMANTICS.

LOCAL FILES, SERVER-HOSTED STATIC HTTP, AND EXTERNAL HTTPS/CDN MIRRORS MUST CONVERGE ON THE SAME VERIFIED PACKAGE.

DOWNLOAD URL, FILESYSTEM PATH, CONNECTION ID, AND ENDPOINT/IP ARE NOT DURABLE CONTENT IDENTITY.

FIRST V3 SEMANTIC ACCEPTANCE IS DEFAULT / REPLACE / ADD.

HISTORICAL PARTIAL `PATCH` MERGE SEMANTICS REMAIN DEFERRED UNTIL A REAL CONSUMER EARNS THEM.

HIGHER CONTENT LAYERS MAY OVERRIDE A LOGICAL ID ONLY UNDER THE POLICY ALLOWED FOR THAT CONTENT KIND/TRUST CLASS.

DO NOT ASSUME ONE UNIVERSAL PRIORITY ORDER MUST APPLY TO EVERY CONTENT KIND FOREVER.

REQUIRED SERVER CONTENT MAY BLOCK WORLD ENTRY UNTIL VERIFIED AND PUBLISHED.

OPTIONAL SERVER CONTENT MAY ARRIVE AFTER ENTRY ONLY WHEN A SAFE LOWER-LAYER FALLBACK EXISTS.

A CLIENT NEVER TREATS PARTIALLY DOWNLOADED OR HASH-FAILED CONTENT AS PUBLISHED CONTENT.

DOWNLOADED != INSTALLED != CATALOG-PUBLISHED != ENGINE-REALIZED != SERVER-KNOWN-APPLIED.

REQUIRED CONTENT REFERENCES MAY BEGIN ONLY AFTER EXPLICIT CLIENT APPLICATION PROOF FOR THE EXACT
CONTENT ACTIVATION.

THE FIRST REQUIRED CONTENT ACTIVATION IS FROZEN FOR ONE APPLICATION/WORLD SESSION.

CACHE SEMANTIC OWNERSHIP IS VERIFIED-REALM/CONTENT-SCOPED; SAME RELATIVE PATH IN TWO REALMS DOES NOT
CREATE THE SAME LOGICAL OWNERSHIP.

IMMUTABLE VERIFIED FILE BYTES LIVE IN A GLOBAL SHA-256 CONTENT-ADDRESSED BLOB STORE; REALM-SCOPED
MANIFESTS AND ACTIVATION PLANS OWN AUTHORIZATION AND SEMANTIC USE.

MISSING/INVALID CONTENT FALLS BACK VISIBLY WHEN POLICY ALLOWS; IT DOES NOT SILENTLY CHANGE GAMEPLAY TRUTH.

CONTENT TRANSFER IS BULK/STATIC DELIVERY, NOT REALTIME ACTOR REPLICATION.

GAME/CONTENT OWNS PACKAGE, FILE, LOGICAL-ID, REVISION, SOURCE LIST, AND APPLICATION SEMANTICS; KERNEL NETWORK DOES NOT.

LOCAL PLAY MAY READ THE SAME PACKAGE DIRECTLY WITHOUT NETWORK.

LOCAL PREINSTALLED CONTENT AND REMOTELY ACQUIRED IDENTICAL CONTENT MUST CONVERGE ON THE SAME RESOLVED CONTENT SET.

CONTENT CATALOG PUBLICATION AND GODOT RESOURCE REALIZATION ARE SEPARATE GENERATIONAL LIFETIMES.

UI SCHEMAS, PRESENTATION ASSETS, AND STRUCTURAL WORLD CONTENT MAY SHARE CACHE/TRANSFER INFRASTRUCTURE WITHOUT SHARING SEMANTIC IDENTITY.

TERRAIN/STATIC CHUNK REVISION IS NOT AN ITEM/MOBILE/TEXTURE LOGICAL CONTENT ID.

ASSET STREAMING CARRIES WORLD-OWNED CANONICAL ARTIFACTS; IT DOES NOT INVENT WORLD ARTIFACT OR
TRANSITION SEMANTICS.

THE CONTENTLESS ONLINE CLIENT REMAINS BOOTABLE THROUGH THE PRIMITIVE/BOOTSTRAP SUBSTRATE EVEN WHEN
NO OPTIONAL LOCAL/DEFAULT PRESENTATION PACK OR PLAYABLE WORLD IS BUNDLED.
```

---

# 3. Three independent axes

## 3.1 Source / trust classes

### Primitive / bootstrap emergency substrate

Always available to the renderer/client and deliberately outside normal package/catalog activation.
It is the floor beneath the content system, not a privileged content package.

Examples:

- minimal local application UI;
- fallback font;
- missing-content checker texture;
- primitive box/capsule fallback;
- only essential bootstrap UI/font resources required to start and diagnose the client.

This substrate must stay extremely small. A distribution containing only it can boot and represent
admitted semantics as blockout/debug presentation, but primitive substitution never satisfies a
missing required content activation or its application proof.

### Local / default verified content

Optional preinstalled presentation content resolved through the ordinary verified content identity
and catalog machinery. It may include X/Y bodies, normal world models/materials, standard compiled
UI definitions, or the Cabin Test World content in products that choose to bundle them. An identical
local and remotely acquired package converges on the same semantic identity; local origin grants no
Game authority or special resolver privilege.

### Server-authorized passive content

Passive data/assets required or offered by the connected server and activated under its verified
application realm/content identity.

May include, subject to capability/schema support:

- models;
- textures;
- trusted/descriptive materials;
- icons;
- sounds;
- mobile/item presentation definitions;
- server-composed UI definitions/schemas;
- world/static content packages;
- server-specific visual remaps;
- declarative client/control profile data.

Server content may be required or optional.

Normal passive content does **not** include arbitrary .NET/native/script/WASM execution or runtime code injection.

### User resource/texture packs

Explicit client-local presentation remaps selected by the user.

Examples:

- alternate textures;
- icon sets;
- UI themes;
- allowed model/audio replacements;
- local sound suppression/remapping.

They do not alter protocol, Game authority, collision, or server semantics.

These are permitted local presentation-leaf substitutions after the authoritative activation plan
resolves. They are not packages in that plan and cannot replace UI actions/schemas, control
profiles, structural World content, or disclosure semantics.

F1.5 implemented the actor-presentation subset. F1.6 extends the same dedicated trust-class-3
manifest factory to one-to-sixteen bounded PNG/WebP Texture `Replace` leaves while preserving
self-contained bounded ActorPresentation GLB `Replace`; Material, Icon, UI, World, executable and
`Add` remain rejected.
Explicit rooted local archives install exact `PackageId + ManifestHash` identities into separate
resource-pack metadata while sharing immutable blob storage with Local/Realm scopes. A strict
persisted list enables at most sixteen packs in deterministic low-to-high priority order.
Presentation generations apply User -> Realm -> Local -> Primitive without altering the lower
catalogs or application proof. Texture and lower-authorized Material leaves now participate in that
common seam; renderer-native Godot resources remain separately keyed disposable derived output.

### Executable client extensions — future/deferred

Potential future explicit executable extension packages.

They are a different trust class from passive content and resource packs.

A server may eventually advertise one, but execution requires a separate install/consent path. See section 22.

## Historical resolver ordering

The archived V2 concrete resolver used:

```text
BuiltIn            0
UserResourcePack 100
ServerPatch       200
UserMod           300
```

That is useful evidence, but V3 does **not** freeze one universal ordering for every future content kind. A server-required semantic presentation definition and a user texture remap may need different policy from a local sound filter. First implement only the ordering required by the Cabin streaming tests and keep trust/source identity explicit so later policy can be content-kind aware without replacing the cache/package system.

---

## 3.2 Semantic operations

The V2 manifest supported `append`, `replace`, and `patch`.

For the first V3 proof, use the simpler observable triad.

### Default

No higher permitted content layer owns the logical id. Resolve the local/default definition when
present, otherwise degrade to the renderer-owned primitive substrate where policy permits.

```text
ultrasim:mobile/human_female
    -> built-in X Bot definition
```

`Default` is a resolution outcome, not necessarily a manifest operation.

### Replace

A higher permitted layer defines an already-known logical id and replaces its presentation definition/resource resolution.

```text
local/default:
    ultrasim:test/cabin_wall_material -> default material

server layer:
    ultrasim:test/cabin_wall_material -> alternate material
```

Game/world identity does not change merely because presentation content changed.

### Add / Append

A layer defines a new logical id that did not exist in lower layers.

```text
my_server:mobile/clockwork_guard
my_server:item/moon_lantern
my_server:material/moonsteel
```

The server/Game may then publish a legitimate semantic reference to that new presentation id.

### Patch — historical, deferred

V2 named a partial `patch` mode, but the recovered implementation did not establish a complete immutable field-level merge contract worth blindly freezing.

Do **not** revive arbitrary partial-definition merge semantics in the first checkpoint.

If a real workload needs "change one hue", "change one anchor", "change one material slot", or "change one UI style field", earn an explicit typed merge contract for that definition family. Until then, replace the complete definition.

---

## 3.3 Server-authorized content activation plan

Packages do not self-assign priority. One authenticated, realm-scoped
`ContentActivationPlan` supplies the exact ordered package set:

```text
ApplicationRealmId
ContentActivationId
PlanHash
required lower/base catalog fingerprint
ordered PackageId + exact ManifestHash entries
```

`ContentActivationId` is a stable scope-owned authored activation/revision identity, not a
connection-local counter or process-local catalog handle. Reusing it for different plan bytes is an
error. `PlanHash` hashes the canonical encoding of every other plan field, including realm,
activation identity, required base catalog fingerprint, and exact ordered package entries.
Application proof is additionally fenced by the current application-session lineage.

Initial resolution is deliberately simple:

```text
Add X
    X must be absent from the lower semantic catalog
    otherwise fail the candidate activation

Replace X
    X must exist in the lower semantic catalog
    the explicitly later package becomes the owner
```

If packages A and B both Replace X, B wins only because the authenticated activation plan places B
later. Packages cannot choose an arbitrary numeric priority to promote themselves. Duplicate or
ambiguous ownership outside this exact order fails candidate publication.

User resource packs do not participate as packages in the authoritative semantic activation plan.
They are a later local presentation-leaf overlay limited initially to permitted texture, icon,
audio, theme/skin, and other explicitly compatible presentation substitutions. They may not replace
server UI action/schema meaning, control profiles, structural World content, disclosure metadata, or
other Game semantics.

Package dependencies, version-range solving, load-before/load-after graphs, and applicability
filters are deferred. Authoring/build tooling may resolve those concerns into one flat exact
activation plan before the client sees it.

---

## 3.4 Physical byte sources

Package identity is independent from where bytes are read.

Initial source classes:

### Local package source

Used by:

- Single Player;
- development/test fixtures;
- locally installed/preseeded packages;
- server operators preparing packages before hosting.

The local source reads a package/folder through the same canonical validator/resolver path. It is not a special semantic format.

### Server-hosted static source

For small/community servers that want the simplest deployment:

```text
server content root
    ./Content

server content hosting
    self
```

UltraSim may expose the configured content root through a bounded **static-content HTTP endpoint** separate from gameplay packet queues/replication budgets.

This keeps authoring/deployment simple while preventing asset file serving from becoming Game replication work.

### External HTTPS/CDN/object-storage source

For larger/busier servers:

```text
server content root / build output
    ./Content

published byte source
    https://cdn.example.net/my-shard/content/
```

The authoritative game server advertises the package identity and approved source base URI(s). The external host only serves bytes.

### Mirrors / fallback sources

A package may advertise an ordered bounded source list:

```text
1. https://cdn1.example.net/content/
2. https://cdn2.example.net/content/
3. https://game.example.net:8443/content/
```

Source failure or hash mismatch may cause the client to try another approved source according to bounded retry policy.

Changing mirrors does not change package identity or force a redownload when the cached bytes already match.

---

# 4. Stable logical ids

Presentation/game definitions reference stable logical ids, not engine paths.

Preferred shape:

```text
ultrasim:mobile/human_female
ultrasim:mobile/human_male
ultrasim:item/chest
ultrasim:material/oak
ultrasim:ui/inventory_standard
my_server:mobile/clockwork_guard
```

A logical definition may reference files inside its owning package:

```text
logical definition
    ultrasim:mobile/human_female

package file
    models/human_female.glb
```

Godot `res://...`, cache paths, archive member paths, temporary download filenames, server URLs, and CDN URLs are never durable logical content identity.

The first grammar is deliberately cross-platform and canonical:

```text
namespace:path/to/id

lowercase ASCII letters, digits, underscore, hyphen
one colon between non-empty namespace and path
forward slash between non-empty path segments
no dot segments, backslashes, whitespace, Unicode aliases, or case folding
```

`Add` normally owns IDs in the package/server namespace authorized by the activation plan. Replacing
a lower-layer ID is permitted only through explicit `Replace`; declaring another package's namespace
does not silently grant ownership.

---

# 5. Files versus semantic definitions

Retain the V2 distinction.

## File entries answer

```text
What bytes exist?
What passive kind are they expected to represent?
How large are they?
What SHA-256 must they have?
```

## Definition entries answer

```text
What logical presentation slot does this content fill?
What typed definition family is this?
Which files/resources does it use?
Is it adding or replacing a logical id?
What anchors/material slots/hue channels/presentation metadata are declared?
```

One file may support several definitions; one definition may reference several files.

Do not use filenames as the resolver catalog.

---

# 6. Package identity and validation

Retain equivalent semantics to V2's useful identity fields:

```text
pack id
name
description                 optional/useful
version
minimum pipeline format
maximum pipeline format
whole package/content hash
per-file path/kind/size/SHA-256
```

For A0, exact semantic package identity is the SHA-256 of a canonical manifest encoding that
includes the ordered/canonical definitions and every file descriptor/hash. Human-readable version
is display/update metadata, not equality or authority. A transport archive may additionally have an
exact archive-byte hash, but archive layout is not the semantic package identity.

Required validation before publication:

- supported manifest/schema version;
- supported content pipeline format;
- valid package id/version;
- path normalization and path traversal rejection;
- no duplicate file paths;
- no duplicate definition ids within one package unless an explicit future merge contract allows it;
- allowed passive file/definition kinds;
- allowed semantic operation;
- file reference must name a declared file;
- file byte length matches declaration;
- file SHA-256 matches declaration;
- whole package/content identity matches when declared;
- bounded counts/sizes before proportional allocation/import;
- passive-content envelope/type validation before catalog publication.

Validation failure publishes **none** of the candidate package.

Initial A0 manifests contain no client-resolved dependency/filter graph. Package-set ordering and
ownership come exclusively from the exact authenticated activation plan.

---

# 7. Passive-content allowlist, not extension blacklist

The V2 code rejected a useful list of obvious executable/script extensions. Keep that as defense-in-depth, but do not treat it as proof that every other file is safe.

The normal streaming path is based on **supported passive content families**. The first A0
allowlist is intentionally narrower than the eventual content vision:

```text
canonical typed UltraSim content definitions
PNG/WebP raster textures through explicit bounded decoders
declarative materials using built-in trusted shader families
existing canonical complete WorldPack artifacts
```

X/Y human meshes began as bundled/build-time calibration assets. D0.7b later admits only a reviewed
self-contained actor-GLB profile with explicit structural/resource bounds and exact skeleton/clip
requirements; arbitrary downloaded GLB and audio remain unsupported.

Future candidate families may include:

```text
bounded passive model format(s)
bounded audio format(s)
additional typed UI/World artifacts
```

Unknown formats reject or remain inactive.

Ordinary passive acquisition never passes downloaded `.pck`, `.tscn`, `.scn`, `.tres`, `.res`,
`.gd`, DLL, shader source, SPIR-V, or another script/code-capable Godot resource to generic
`ResourceLoader`.

Important edge cases:

- a Godot scene/resource capable of referencing arbitrary scripts is not automatically passive just because its extension looks like content;
- arbitrary shader source/SPIR-V is executable GPU code and should not silently enter the normal passive-content class;
- future WASM is executable code even if stored in a package next to assets;
- SVG/HTML-like or other active-document formats require explicit evaluation before being treated as passive;
- importer/parser vulnerabilities are still possible, so bounds, hashes, format constraints, and importer hardening remain necessary.

Initial materials should prefer trusted client shader families plus declarative parameters:

```text
shader family = StandardLit
texture/resource slots
roughness
metallic
emission
hue channels
validated parameters
```

Custom server-supplied shaders, if ever supported, are a separately elevated capability.

A later GLB importer must bound at least node, mesh, primitive, vertex, index, material, texture,
animation, skin/bone, decoded-byte, and realized-resource counts before native construction. The same
principle applies to later audio decoding.

---

# 8. Verified realm ownership and content-addressed cache

Recover V2's server-scoped semantic ownership without duplicating immutable bytes per server.

The Client Shell verifies a deployment-key fingerprint and authenticated `ApplicationRealmId` as
defined by `CLIENT.md`. Realm metadata maps to a path-safe namespace; exact file bytes map only to
their verified SHA-256.

Conceptually:

```text
Cache/
    Blobs/
        sha256/
            ab/
                abcdef...
    Realms/
        <verified realm encoding>/
            manifests/
            activations/
            metadata/
    Staging/
    Extensions/      future executable extension install/cache
    Mods/            future user-managed local mods if kept distinct
```

A0.2 selected the following version-1 physical layout:

```text
Cache/
    blobs/sha256/ab/<BlobHash>.blob
    realms/<ApplicationRealmId>/manifests/<ManifestHash>.manifest
    realms/<ApplicationRealmId>/activations/<ContentActivationId>.activation
    metadata/access/<BlobHash>.access
    staging/<opaque>.tmp
    writer.lock
```

F1.1 retains that Realm layout and its version-1 activation bytes for backward-compatible reopen,
then adds the reserved Local scope without creating a second content implementation:

```text
Cache/
    scopes/local/manifests/<ManifestHash>.manifest
    scopes/local/activations/<ContentActivationId>.activation
```

`ContentScopeKey.Local` and `ContentScopeKey.ForRealm(authenticatedRealm)` now pass through the
same install, activation, catalog, global CAS, lease, reachability, and retirement code. Local has
no arbitrary text/path constructor; a remote realm cannot claim Local trust. Remote Game wire and
application proof stay Realm-shaped. The current one-writable-root policy remains; a future
read-only install root can compose above this typed ownership seam without changing scope identity.

All variable path components are fixed-width canonical identity encodings. The initial cache root
has one owning client process so live pins and mutation remain coherent; simultaneous clients use
separate roots until cross-process pin coordination is explicitly designed.

Rules:

- same relative path from two realms does not create semantic ownership aliasing;
- cache paths cannot escape their root;
- schema/package/activation ownership is associated with the verified realm and exact hashes;
- changing hostname while preserving verified deployment/realm continuity does not create a new logical cache owner;
- endpoint/IP is not durable content identity;
- cache ownership is separate from Network `ConnectionId` and Game identity;
- byte-source URL is not cache identity;
- a mirror change does not invalidate otherwise-identical cached content;
- manifest-controlled relative names are never used as physical blob paths;
- a verified blob may be physically reused across realms without transferring authorization,
  logical IDs, activation, or trust between them;
- a remote peer may test reuse only for hashes present in its authenticated content requirement; the
  global blob store is not exposed as an arbitrary remote cache-membership oracle.

Eviction initially uses reachability: pinned scope activations/manifests and live engine resource
handles mark referenced blobs; unmarked blobs are eligible under quota/retention policy. User packs,
built-ins, and future executable installations keep their separate ownership/lifecycle rules.

---

# 9. Cache publication and atomicity

Publication has three different generational lifetimes.

## 9.1 Verified blob installation

```text
authenticated requirement/manifest identity received
    -> resolve approved source list
    -> compare local cache hashes
    -> fetch missing/changed files
    -> stream to staging/temp storage
    -> verify size/hash
    -> validate complete package/definition graph
    -> atomically install immutable SHA-256 blobs + realm manifest
```

Failure at any point:

```text
publish no candidate layer
keep previously valid active layer/fallback
remove or quarantine invalid partial candidate
```

Do not replace a good cached file with a half-written candidate.

Second joins should validate/use matching cached files rather than re-transfer them.

The source that supplied the bytes has no special publication authority.

## 9.2 Immutable resolved catalog generation

After every required manifest/blob is installed and validated, resolve the exact activation plan into
one immutable engine-neutral catalog generation. Publish the complete candidate catalog
transactionally; no definition from a failed/partial candidate becomes active.

```text
ContentActivationId + PlanHash
        + required lower/base catalog fingerprint
        -> CatalogGeneration
        -> deterministic ResolvedContentFingerprint
```

`ContentSetApplied` proves this catalog generation is active in Client Core. It does not claim that
every possible texture/model/audio object has already been uploaded to native/GPU resources.

## 9.3 Lazy engine resource realization

Godot realizes a catalog entry only when presentation needs it, on the correct engine thread and
under bounded work/memory limits:

```text
catalog entry + generation
    -> realized resource already available? use it
    -> otherwise queue bounded realization and use allowed fallback
    -> publish generation-fenced native resource handle when ready
```

Old catalog/native handles remain valid only for their declared pinned lifetime. Catalog replacement
does not retarget an existing RID/object to unrelated bytes. Native resource failure is visible and
uses fallback only when the activation policy permits it; it never changes Game truth.

---

# 10. Quota, pinning, and eviction

Retain the old useful policy shape:

- compute cache usage;
- enforce configured quota/high-water;
- never evict pinned/in-use entries;
- prefer old unpinned entries;
- delete only paths proven inside cache root;
- keep metadata/index updates consistent with physical deletion.

Important distinctions:

- active required content is pinned for the relevant lifetime;
- an in-use UI schema or loaded asset may not be evicted underneath an active handle;
- blobs unreachable from pinned realm activations/live resource handles are quota-managed;
- user-installed resource packs are not ordinary server-cache eviction candidates;
- executable extensions, if implemented, are explicit installations and are not silently quota-evicted like transient server asset cache;
- local/preinstalled content is installation content, not server cache.

---

# 11. Required versus optional server content

## Required

Required passive content blocks the semantic/world entry point that needs it until:

- package identity/manifest is valid;
- required files are present and verified;
- definitions resolve;
- the exact immutable catalog generation publishes;
- the client returns successful `ContentSetApplied` proof.

Examples:

- a new player/mobile presentation with no acceptable fallback;
- structural world package required before a chunk can become resident;
- required UI/catalog definition for admission/game flow.

For the first A0 implementation, the complete required activation is frozen for one
application/world session. The server cannot introduce new mandatory Add/Replace content and begin
referencing it mid-session. A later required-content generation transition must be separately
designed with old/new semantic ordering and failure policy.

## Optional

Optional content may arrive after entry only when a safe lower-layer fallback exists.

```text
server offers high-resolution wolf model
client initially uses lower-layer/fallback wolf presentation
optional package arrives and verifies
client publishes an optional catalog generation and lazily realizes replacement resources
```

Optional content arriving late must not alter Game authority.

## Executable extension requirement is not passive content

If a future server declares a **required executable client extension**, refusal does not degrade to a primitive fallback. The client must refuse entry unless the user explicitly installs/approves the extension under the separate extension policy.

---

# 12. Content authority versus byte delivery

The authenticated Game/application session carries the authoritative requirement:

```text
ApplicationRealmId
ContentActivationId
PlanHash
RequiredBaseCatalogFingerprint
ordered PackageId + ManifestHash entries
Required or Optional
Compatible pipeline/API constraints
Ordered approved byte sources
```

The game server may additionally advertise exactly which files/hashes are required for delta fetch, or the client may derive that from the authenticated manifest identity.

The byte source never gets to say "actually use this different package" unless that new identity is separately authorized by the Game server.

This prevents a compromised CDN from silently upgrading or replacing server-authorized content.

The content host may serve:

- immutable package archives;
- canonical manifests whose hashes were already authorized by the server;
- individual immutable files keyed/path-addressed by the authorized manifest.

The client verifies all resulting bytes before publication.

## Application proof and reference ordering

Content follows the same accepted rule as other application semantics:

```text
downloaded != installed != applied
```

For required content:

```text
server authorizes activation
    -> client obtains/installs blobs
    -> client publishes exact catalog generation
    -> client sends ContentSetApplied(activation, plan hash, resolved fingerprint)
    -> server validates the exact application-session lineage
    -> server may publish semantics requiring that activation
```

Network packet acknowledgement, HTTP completion, cache presence, or hash verification alone never
advances server knowledge. Duplicate/stale proof is idempotent/fenced, a new application session does
not inherit old proof lineage, and rejection/capacity failure cannot partially advance an
activation. Local Play uses the same semantic application rule without packetization.

Structural World artifacts retain separate World generation/revision application transactions. A
generic content proof may establish byte/catalog availability; it does not supersede World lineage
or authorize an in-place structural swap.

---

# 13. Delivery protocols and gameplay isolation

Large package/file transfer should **not default to the realtime gameplay transport**.

Preferred architecture:

```text
Gameplay/control
    authenticated UltraSim protocol / Network
    small package requirement/source advertisement
    normal Game semantics

Content bytes
    Local file/package source       for Single Player/dev
    HTTP(S) static content source   for self-hosted servers
    HTTPS/CDN/object storage        for external hosting
```

A server may host its static-content HTTP endpoint in the same executable/process for convenience, but the content-serving path should have:

- separate bounded request/concurrency limits;
- no participation in replication ready queues;
- no consumption of Game packet budgets;
- no dependence on one Network pump per authority tick;
- independent observability/rate limits.

It still consumes machine uplink, CPU, and disk, so operators with meaningful traffic should be able to offload to an external host without changing package semantics.

The existing reliable Bulk lane remains available for **small content/control envelopes or future special cases** where it is genuinely appropriate. Do not turn Network into a general CDN/file server.

---

# 14. Source URI policy

Remote servers may not supply arbitrary URI schemes.

Initial remote source policy should prefer:

```text
HTTPS
```

Possible exceptions:

- HTTP for explicit local/LAN/development policy;
- server-self-hosted HTTP where the client/user policy permits it.

Do not accept remote-controlled sources such as:

```text
file://
smb://
ftp://
custom arbitrary schemes
local filesystem paths
```

A remote server must not trick the client into reading arbitrary local files.

Also evaluate SSRF/local-network risks. An arbitrary internet server should not automatically be able to make the client fetch loopback/private-network resources. Local/LAN content sources should be enabled by explicit policy/context rather than universally trusted.

Redirect count, response size, timeout, retry count, and concurrent download count must be bounded.

---

# 15. Source failover and mirrors

The ordered-list contract is frozen here, but trying another source after an unavailable or corrupt
first source is an **A0.5** implementation gate. A0.4 proves each source class independently and
continues to select only the first supported approved source.

A package may advertise a bounded ordered source list.

Example:

```text
Package ultrasim.cabin.server v4
Hash ABC123

Sources
    1 https://cdn1.example.net/cabin/v4/
    2 https://cdn2.example.net/cabin/v4/
    3 https://game.example.net:8443/content/cabin/v4/
```

Client behavior:

```text
cache match?
    yes -> no download
    no  -> try approved source 1

source unavailable / timeout / invalid bytes
    -> reject those bytes
    -> try next approved source within bounded policy

verified candidate complete
    -> publish
```

A corrupt source never wins because the package/file hashes are authoritative.

Mirror selection/failover is delivery policy, not content semantic identity.

---

# 16. Local play uses the same packages without Network

Single Player is the clean reference implementation for package application.

```text
local authoritative Test World package
    -> validate/open package locally
    -> install/resolve active content layer
    -> Client Core catalog/state
    -> Godot presentation
```

No UDP/Network is required.

Remote play is:

```text
authenticated server advertises same package identity
    -> client obtains missing bytes from any approved source
    -> validates same canonical package
    -> installs/resolves same active content layer
    -> same Client Core catalog/state
    -> same Godot presentation
```

Therefore:

> **Preinstalled local content and remotely obtained identical content must produce the same deterministic resolved content fingerprint/catalog.**

---

# 17. Resolved content fingerprint

Add an engine-neutral diagnostic/acceptance concept equivalent to a deterministic `ResolvedContentFingerprint`.

For each relevant logical id, include deterministic identity such as:

```text
ApplicationRealmId / ContentActivationId / PlanHash / required base catalog fingerprint
logical id
resolved owning package/layer identity
canonical definition hash
referenced file content hashes
```

Sort deterministically before hashing.

Do **not** include:

- cache absolute path;
- temporary filename;
- Godot object/RID/node identity;
- chosen download URL/mirror;
- connection id;
- remote IP/port.
- Godot RID/object/resource-realization state.
- process-local catalog-generation handle/counter.

Thus LocalSource, self-hosted HTTP, and external CDN must all produce the same fingerprint for identical content.

---

# 18. Cabin in the Woods becomes the streaming oracle

Use the same Cabin Test World in Single Player and externally hosted Online runs. Do not create a separate final asset-only demo.

The Cabin is also selected as the future/upcoming **visual benchmark** for intended real art and foundational lighting. See [Visual Art / Lighting / Cabin Benchmark](../../AIOS/canonical/visual-art-lighting-and-cabin-benchmark.md).

Keep these roles distinct:

- the existing A0.1 Default/Replace/Add fixture remains a deterministic content-resolution oracle with frozen identities/fingerprints;
- the visual benchmark may add or replace ordinary Official/local presentation through world.local to exercise the intended art style, two-room interior, door/window light spill, shadows, and environment profiles;
- visual iteration must not casually rewrite the deterministic oracle merely because the preferred art changes.

The same world can prove both systems because presentation content is replaceable while authoritative Game/world semantics remain unchanged.

## Scenario A — Default

### Local oracle

Run Single Player Cabin with built-in/default content.

Record:

- authoritative Game/world semantic hash where useful;
- resolved content fingerprint;
- relevant logical-id resolutions;
- human actor definition/anchors;
- renderer-neutral presentation counts/identities;
- optional screenshot/manual visual oracle.

### Remote

Host the same Cabin externally and advertise no replacements/additions, or identities already satisfied exactly by built-ins/cache.

Expected:

- no unnecessary asset download;
- same Game semantics;
- same resolved content fingerprint;
- same actor/cabin presentation;
- same X/Y human scale/camera/nameplate calibration.

This proves **Default/fallback parity**.

## Scenario B — Replace

Create a deterministic Cabin replacement package.

Good first replacements are presentation-only:

- X/Y human visual swap;
- obvious cabin wall material/texture replacement;
- chest/item presentation replacement while retaining Game identity/collision/container behavior;
- standard icon/UI skin replacement.

### Local oracle

Apply the exact replacement package from a local/preinstalled source in Single Player.

### Remote

External Cabin server authorizes the same package; Online client obtains it from a cold cache.

Expected:

```text
base Game/world semantics                    unchanged for presentation-only replacement
local replacement fingerprint               == remote acquired fingerprint
replaced logical ids                         differ from Default exactly as expected
unrelated logical ids                        remain Default
collision/inventory/chat semantics           unchanged
```

## Scenario C — Add

Create a small package with new namespaced logical ids:

```text
test_cabin:mobile/clockwork_dummy
test_cabin:item/blue_lantern
test_cabin:material/blue_glow
test_cabin:icon/streamed_badge
```

The modified Test World legitimately refers to at least one new presentation id.

Expected:

- no collision with built-in ids;
- local and remote fingerprints match;
- new presentation works;
- existing Default content remains intact;
- package absence produces the declared required-content failure or visible fallback behavior, never undefined presentation.

---

# 19. Delivery-source parity matrix

For at least one Replace package, prove the **same exact package** through three physical origins:

```text
1. Single Player / LocalPackageSource
2. Online / ServerHostedStaticSource
3. Online / ExternalHttpsSource
```

Required:

```text
same package id/version/hash
same file hashes
same logical definitions
same resolved content fingerprint
same Game/client semantic result
```

Also prove one mirror failover case:

```text
source 1 unavailable or corrupted
    -> reject/fail over
source 2 valid
    -> complete package
    -> same final fingerprint
```

The byte origin must never appear in semantic fingerprints.

---

# 20. Stronger cache/delta acceptance matrix

## Cold cache

No matching cached package.

Expected:

- authenticated package requirement;
- source resolution;
- missing files fetched;
- exact byte/file counts measured;
- verify/install/publish;
- world entry according to required/optional policy.

## Warm cache

Exact package/file hashes already cached.

Expected:

- zero redundant file transfer;
- source need not be contacted for immutable matching bytes unless policy requires revalidation;
- same fingerprint/presentation.

## One-file revision

Change exactly one file and advance package identity appropriately.

Expected:

- unchanged matching files reused;
- only missing/changed file bytes fetched;
- atomic publication after full validation;
- final fingerprint matches equivalent local package.

## Corrupt cache

Mutate/truncate a cached asset while metadata remains stale.

Expected:

- verification detects corruption;
- corrupted file is not trusted;
- repair/re-fetch runs;
- old valid layer/fallback remains until candidate completes.

## Interrupted download

Terminate source/connection mid-file/package.

Expected:

- partial candidate never active;
- reconnect can re-request/resume according to implementation policy;
- no poisoned final cache entry.

## Realm semantic-ownership isolation

Two verified application realms advertise the same relative path with different hashes/content.

Expected no logical/cache ownership collision.

## Required versus optional

Prove required blocks use/entry; optional safely hot-upgrades from fallback; neither changes Game truth by download timing.

## Quota pressure

Prove active/pinned content remains, unreachable unpinned blobs/realm metadata evict, and deletion
cannot escape cache root.

## Source failover

Prove timeout, unavailable mirror, corrupted mirror, and valid fallback source.

---

# 21. Human calibration models are part of the content proof

Recover the V2 X/Y Bot presentation assets as calibration content where licensing/source permits continued use.

Historical definitions:

```text
X Bot -> Human Female
Y Bot -> Human Male
VisualScale        1,1,1
CameraPivotOffset  y=1.6m
NameplateOffset    y=2.0m
EffectsOffset      y=0.9m
```

These are starting calibration values, not architectural constants.

Use real human models in Cabin to validate:

- authoritative body versus visual scale;
- door/chest/wall scale;
- terrain-cell feel;
- camera distance/pitch/pivot;
- movement-speed feel;
- nameplate anchor;
- overhead chat anchor;
- effects anchor.

Because they are content definitions, they also make excellent Replace targets.

---

# 22. Future executable client-extension model

No known client feature currently earns executable extension distribution. It is **not authorized by
A0**. If a future gameplay need cannot be expressed through server Game code, server-composed UI,
declarative profiles, and passive Add/Replace content, create a separate `CLIENT_EXTENSIONS.md`
campaign.

The only frozen boundary is:

- executable extensions use a separate package type, install root, loader capability, consent, and
  lifecycle from passive content;
- the stock client never compiles server-supplied source;
- possible future payloads are precompiled constrained WASM or explicitly full-trust precompiled
  .NET DLLs, subject to a later security/API/runtime decision;
- a server may advertise an extension, but normal content never installs/executes it;
- refusing a required extension refuses entry rather than silently degrading;
- hashes/signatures establish byte identity/provenance, not safety;
- Contentless Online may omit the extension-host capability entirely.

Do not design manifests, dependency sorting, capability APIs, or runtime selection in this plan.

---

# 23. Presentation content, UI schemas, and structural world content

These may share storage/transfer primitives but retain separate semantic owners.

## Presentation content

Logical-id resolver domain:

- mobiles;
- items;
- models;
- textures;
- materials;
- icons;
- audio;
- presentation metadata.

Default/Replace/Add naturally apply here.

## UI schema content

Uses exact UI definition/catalog identity from the UI kernel.

It may share:

- verified application-realm cache ownership;
- SHA validation;
- cold/hot acquisition;
- source/mirror infrastructure;
- quota accounting.

It does **not** become a generic texture/model logical id if UI semantics already have stronger typed identity.

## Structural world content

Terrain/frozen statics/Multi/WorldPack data are authoritative structural semantics, not ordinary
mobile/material definitions.

The first streaming canary uses one complete World-owned artifact and its existing identity:

```text
ApplicationRealmId + ContentActivationId
complete WorldPackFingerprint + WorldContentFingerprint
authorized manifest/blob hashes
```

Current per-chunk revision/hash values are not downloadable artifact identity. If World later
defines a canonical overlay or independent chunk artifact, that stronger contract may additionally
bind server/world/content lineage, `SpatialChunkKey`, `WorldChunkContentRevision`, section kind, and
exact artifact hash. Asset Streaming must not define that contract on World's behalf.

The bytes may use the same cache/source/download substrate, but structural publication/application
remains World/Client Core semantics.

The canonical World-owned artifact/prepared view is shared semantic identity, not a second
client-only visual map. A server authority and a client projection may consume the same verified
artifact bytes in Single Player, but the authority binds the artifact; mutable client cache state
does not become structural truth.

---

# 24. Terrain/static hot patching

Recover the useful V2 goal only through artifacts whose semantics World explicitly owns:

- frozen/static chunk content can be cached client-side;
- server can authorize a newer structural chunk revision;
- housing/map edits can eventually produce changed structural content;
- client stages/verifies replacement structural semantics privately;
- renderer hot-swaps the affected structural layer;
- dynamic actors remain realtime publication/replication.

Current accepted public artifact support is a complete canonical `WorldPack`. Current
`WorldChunkContentRevision`/`WorldChunkContentHash` values identify effective overlay chunks but do
not constitute a canonical independently downloadable chunk artifact. Persistence save-set encoding
is likewise not automatically a public streaming format.

Therefore A0.7 first acquires and opens one complete exact WorldPack. A later World milestone may
define a canonical complete `WorldOverlayArtifact` bound to its base fingerprint/revision. Only a
still-later measured need may earn an `IndependentWorldChunkArtifact`.

Do not send every static/terrain object as an ECS entity because remote content is patchable.

Do not treat camera visibility as structural content lifetime.

Preserve the current T0 distinction between immutable base content, mutable overlay, client semantic residency, and Godot render slots.

Structural activation is authority-first:

```text
candidate structural revision
    -> privately validate/prepare on the authority
    -> commit authoritative revision/hash and prepared World view
    -> interested clients acquire/verify/apply that exact committed revision
```

The authority may distribute immutable candidate bytes before commit so clients can prefetch them,
but no client may make the revision current before the authoritative server does. Structural
revision/hash identity remains separate from dynamic-public publication lineage, and cache presence
never constitutes application proof or authority.

---

# 25. Fallback and diagnostic presentation

The renderer-owned primitive/bootstrap substrate should include at least:

```text
missing model/object
    -> simple box/capsule/category primitive

missing texture/material
    -> extremely obvious checker/rainbow diagnostic texture in texture-debug mode

neutral fallback mode
    -> simple grey/default presentation when texture debug is disabled

missing animation
    -> stable static pose/bounded presentation timer rather than Game failure
```

Important:

- fallback is presentation only;
- authoritative collision/size comes from Game/Physics/world semantics;
- a required package may still block entry when fallback is declared insufficient;
- optional/missing content fails visibly/diagnosably.

F1.3 now implements the finite engine-neutral fallback descriptor and Godot primitive
realization for already-disclosed actors, items, public objects, frozen statics, and Multi
fragments. Structural fallback uses each disclosed exact AABB; model and texture failure remain
independent. The primitive-only Concept Demo smoke suppresses optional art without changing
required activation or proof. F1.4 now ships the exact Official X/Y actor archive outside the PCK,
installs/reopens it as ordinary `world.local` content through the shared CAS, and preserves
Realm → Local → Primitive selection without weakening Realm proof.
F1.5 adds the client-local User layer above that chain. Exact enabled pack state is independent of
scope authorization; compatible actor replacements publish as new immutable presentation
generations, while missing targets are no-ops and invalid/incompatible candidates preserve the
current generation. Enable/disable/reorder does not change semantic activation, WorldPack, or
remote proof.

F1.6 adds effective Texture/Material leaves to those immutable generations. The Local Cabin wall
path proves User Texture Replace and exact disable restoration without changing material scalars,
semantic world hash, collision, or interaction. Godot persists versioned decoded RGBA artifacts
under writable `Derived/Godot`; matching effective content reopens across process generations,
while corruption, deletion, content changes, and policy changes rebuild/select distinct output.
The first `WorldOpaque` atlas candidate remains diagnostic because one page did not reduce the
current two-material/draw shape.

The local Concept Demo D0.4 canary now proves that the maintained Godot renderer can distinguish
model absence from texture absence and degrade only presentation while immutable World identity and
physical bounds remain unchanged. Its four fixture outcomes are local/preinstalled test resolution,
not CAS/source/download evidence. A0.2+ must eventually drive the same renderer contract from
verified catalog/blob availability without changing these semantics.

D0.7a added a deliberate split: locally bundled X/Y GLBs could realize exact Idle/Walk/Jump while
Contentless Online used the humanoid primitive against the same semantic actor state. D0.7b closes
the runtime half without weakening required content. The server deployment Adds exact logical X/Y
actor definitions and then visibly Replaces X; the exported Online product contains no body GLB,
acquires the exact packages, and resolves only through its live applied realm catalog.

The admitted actor profile is binary glTF 2.0 with exactly one embedded binary buffer and no URI,
image, texture, camera, script, scene, arbitrary shader, or unsupported extension. Bounded passive
inspection covers nodes, meshes, triangle primitives, vertex/index totals, materials, accessors,
buffer views, bones, animations, tracks, keys, strings, and byte sizes before catalog publication.
The Concept Demo resolver then requires its exact ordered 65-bone-name fingerprint, right-hand
semantic socket, and exactly one normalized Idle/Walk/Jump clip. Unknown animation semantics and
duplicate semantic aliases fail passive admission. The fingerprint does not cover parent topology,
rest transforms, or inverse-bind matrices; arbitrary retargeting remains unsupported. Godot imports
the validated bytes lazily, rechecks the native
scene shape, and retains the source catalog/blob lease for the native template lifetime.

```text
verified bytes
    != applied catalog authorization
    != native engine realization
```

A content refresh swaps only the actor's visual child. Actor identity, root, nameplate, overhead
Chat, action fence, controlled role, and equipment state remain intact; realization failure falls
back to the primitive without changing Game truth. Generalized live catalog hot-upgrade and user
presentation-leaf overrides remain later work.

---

# 26. Control/settings profiles as server data

The declarative server control/camera profile discussed in `CLIENT.md` belongs to the same **passive server data/cache** security class as other non-executable content, though it need not be an asset-pack definition.

It should use:

- exact profile identity/revision/hash;
- verified application-realm-scoped cache ownership;
- validation;
- user acceptance (`Use Server Defaults` / `Keep Mine` / `Customize`);
- layered local settings resolution.

A server content package may reference a recommended profile identity, but that profile contains no executable client code.

---

# 27. Suggested V3 implementation roadmap

This campaign is adjacent to Client F0. Do not hide a large new subsystem inside one client commit.

## A0.0 — archaeology/contracts freeze

**Status: CLOSED.** `Client/Content` now owns strict logical/package/file identities, exact SHA-256
descriptors, bounded immutable passive manifests, realm-scoped ordered activation plans, required
base-catalog fencing, Add/Replace resolution, source-independent resolved fingerprints,
process-local immutable catalog-generation handles, and exact application-proof vocabulary.
Normal passive manifests accept only typed PNG/WebP-backed Texture/Icon definitions, declarative
`StandardLit` materials, and complete WorldPack definitions. Raster bytes receive exact descriptor
and passive-envelope validation; complete WorldPack bytes delegate to the existing World-owned
bounded container/hash/schema validator. Built-in and server-authorized-passive trust are accepted;
user resource packs and executable extensions remain distinct and rejected by this normal manifest
path. Successful payload validation produces the only package capability accepted by base/activation
resolution, so a bare or hash-failed manifest cannot publish catalog state. The assembly has no
Godot or Network dependency.

Document/test:

- verified application-realm ownership shape;
- canonical manifest/package identity and pipeline compatibility;
- lowercase ASCII namespaced logical-id grammar;
- file descriptor + size/SHA;
- first passive allowlist: typed definitions, PNG/WebP, declarative trusted materials, complete
  WorldPack artifacts;
- explicit rejection of generic Godot/script/shader/code-capable resources;
- exact `ContentActivationPlan` identity/order;
- exact required lower/base catalog fingerprint so `Default` cannot depend on an unspecified client
  installation/version;
- Replace and Add/Append;
- primitive fallback plus optional local/default content;
- source/trust identity;
- required/optional package requirement;
- required activation/application-proof state values;
- deterministic resolved content fingerprint;
- global immutable SHA-256 blob + realm activation ownership model;
- immutable catalog-generation identity separate from engine-resource realization;
- bounded manifest/file/package limits;
- authoritative package requirement separated from physical source list;
- Local / ServerHostedStatic / ExternalHttps source vocabulary;
- executable extensions explicitly outside normal content.

At A0.0, defer dependencies/filters, partial Patch, downloaded GLB/audio, mutable World artifacts,
independent chunk artifacts, and executable-extension runtime implementation. D0.7b later admits
only the narrow actor-GLB profile described above; it does not make arbitrary GLB or audio valid.

**Gate:** pure tests build/validate local packages and prove deterministic resolution with no Godot/Network.

---

## A0.1 — local resolver + Cabin Default/Replace/Add oracle

**Status: CLOSED.** `Client/BuiltInTestWorlds` now owns a local, engine-neutral Cabin fixture over
the generic A0.0 resolver. One verified built-in base package resolves into three separately
identified immutable catalogs:

| Oracle | Resolved fingerprint |
| --- | --- |
| Base catalog | `573b0486d7914f7640b109fcae10e0712c033844ca2325db889e0443adcd7a31` |
| Default | `633eed18a9856474e43c4a6fb61db0bd80fbade8a40f6d884728b29024afb503` |
| Replace | `f1f223d3c1ccf94560b8fbbb4d50bf036a98b043cb117ced63257ac4ecdd7f44` |
| Add | `919d13d6572bf0df807891783d3ea4e25dea16010717df8a2223081ab1df8564` |

Replace changes ownership of exactly `cabin:texture/wall`; its unchanged built-in wall material
correctly resolves the replacement texture hash, while every unrelated entry remains identical.
Add preserves every base entry and introduces exactly the `test-cabin` blue-glow texture/material
and streamed-badge icon. All payloads are complete deterministic local PNGs and must produce a
`VerifiedContentPackage` before resolution. The fixture realm is stable oracle identity, not a
claim of authenticated remote realm establishment.

This first presentation oracle deliberately does not repackage the existing authoritative Cabin
WorldPack or bundled X/Y meshes. F0.2 consumes the catalogs in the real Single Player shell; A0.7
separately carries a complete World-owned artifact through the streaming substrate.

Use local/preinstalled sources only and publish immutable engine-neutral catalog generations.

Prove:

1. Default Cabin.
2. Cabin Replace package.
3. Cabin Add package.

Record resolved fingerprints. X/Y human calibration meshes remain bundled/build-time assets; the
first Replace/Add cases may use typed definitions, texture/material/icon content without claiming a
downloaded-model importer.

**Gate:** local content fixtures build the Default/Replace/Add Cabin catalogs and deterministic
fingerprints without Godot, Network, or the final playable Single Player shell; intended logical
ownership changes are exact and diagnosable. F0.2 performs the actual playable Single Player Cabin
integration.

---

## A0.2 — persistent cache/install substrate

**Status: CLOSED.** The engine-neutral `Client.Content` assembly now owns a source-free persistent
CAS with canonical realm manifest/activation metadata. It admits verified
`ServerAuthorizedPassive` packages, stages and atomically publishes immutable blobs before realm
metadata, and reopens catalogs after process restart by revalidating the exact blobs and stored
resolved fingerprint.

Reintroduce/harden:

- global hash-addressed immutable blob store;
- verified-realm manifests/activation metadata;
- safe opaque cache paths;
- atomic blob staging/install;
- size/SHA verification;
- immutable catalog publication;
- metadata/access tracking and catalog/native-resource pinning;
- quota plus reachability-based eviction;
- corrupt-cache detection;
- passive allowlist/active-content rejection.

**Gate:** local harness installs/reopens package bytes through the same cache format Online uses.

Accepted gate evidence also covers global blob deduplication without cross-realm authorization,
abandoned staging cleanup, reparse-point/path rejection, corruption detection, activation conflict
fencing, live catalog/blob pins, persistent activation pins, quota failure without partial
deletion, and reachability-consistent activation/manifest/blob eviction. A0.2 has no source
URL, HTTP, Network requirement advertisement, repair, UI streaming, WorldPack streaming, or native
Online entry.

---

## A0.3 — authenticated requirement + source-independent required-package join

**Status: CLOSED.** Game protocol v17 now carries one bounded authenticated content requirement on
reliable `Event` and its exact semantic application result on `Control`. The wire authorization
contains application-session lineage, verified realm, activation/base/plan hashes, ordered package
identity/manifest/requirement policy, and ordered approved source descriptors. Physical source is
absent from plan/catalog identity. The authority retains the requirement and blocks guest/Game
admission until an `Applied` result matches the same session, realm, activation, plan, and expected
resolved fingerprint. Rejected, stale, forged, and default proofs do not advance knowledge;
duplicate exact proof is idempotent.

`Client.Content` now owns a source-neutral acquisition transaction. It first attempts exact A0.2
realm-package reuse. A missing manifest is fetched through an injected approved-source adapter,
then candidate package/manifest identity and all payloads are validated and installed through A0.2
before the exact catalog publishes. Cache corruption does not silently trigger repair in this
checkpoint. The first supported source is selected once; mirror retry remains A0.5.

The accepted authenticated Hosted/Online fixture uses the frozen Cabin Replace package. A cold
cache fetched one package/one file/70 bytes and reproduced
`f1f223d3c1ccf94560b8fbbb4d50bf036a98b043cb117ced63257ac4ecdd7f44` exactly. A new application
session reopened the same activation with one package reused, zero source fetches/bytes, and a fresh
session proof. Source failure and a source returning the wrong package leave no catalog admission;
prior-session proof cannot unlock a reconnect.

This checkpoint establishes the source-neutral source contract with a deterministic out-of-band
fixture adapter. Concrete server-hosted static HTTP and external HTTPS parity remain A0.4; realtime
Network `Bulk` does not carry package bytes. Native contentless Online world entry remains A0.8.

Original flow:

First prove one required Replace package with source-independent fetching.

Flow:

```text
connect/authenticate
-> server authorizes exact activation/plan hash + required policy + approved sources
-> compare cache
-> fetch missing files over selected content source
-> verify/install
-> publish exact catalog generation
-> send ContentSetApplied for application-session lineage
-> server advances content knowledge
-> continue world entry
```

Do **not** require large file transfer through realtime Network Bulk.

**Gate:** Online Cabin cannot receive semantics requiring the activation before proof; cold-cache
Replace result matches Single Player local Replace fingerprint exactly.

---

## A0.4 — Default / Replace / Add remote parity + source parity

**Status: CLOSED.** Game protocol v18 admits `http` or `https` for an explicitly classified
`ServerHostedStatic` source while retaining HTTPS-only `ExternalHttps`. `Client.Content` now owns
one canonical passive `.uspkg` envelope plus rooted local-file and bounded HTTP(S) byte-source
adapters. The package envelope carries the canonical manifest and its exact ordered payloads; it
does not introduce another semantic package identity. All bytes still pass package/manifest/path,
size, SHA-256, and passive-format validation before A0.2 installation and catalog publication.

`Server.Hosting` now provides a small immutable-byte `StaticContentHttpHost` that is independent of
Game ticks, Network lanes, realm authorization, and content meaning. Its admitted route/resource
set and total retained bytes are fixed at construction, it accepts GET for exact lowercase routes
only, and copies configured bytes before serving them. A fixed response-worker set, bounded pending
queue, and per-response deadline prevent one stalled consumer from blocking every independent
download; excess work is rejected rather than creating unbounded tasks. Deadline expiry
actively aborts the response connection and awaits the blocked write's cleanup before
the fixed worker is returned, because cancellation alone does not reliably interrupt an
active `HttpListener` response write on every supported runtime. The same termination
rule applies during host shutdown. The initial
client source disables redirects and decompression, bounds response bytes and request time, and
allows plaintext server-hosted HTTP or loopback/private targets only through explicit policy.
External sources remain HTTPS-only. Literal private/loopback addresses are rejected by default;
DNS-rebinding-resistant resolution policy, bounded parallel acquisition, retry, and mirrors were
deliberately deferred here and are closed by A0.5 below.

The accepted Cabin matrix proves:

```text
Default
    zero package/file/archive transfer
    catalog 633eed18a9856474e43c4a6fb61db0bd80fbade8a40f6d884728b29024afb503

Replace
    exact package through local file, server-hosted HTTP, and external HTTPS
    70 payload bytes / 403 canonical archive bytes
    catalog f1f223d3c1ccf94560b8fbbb4d50bf036a98b043cb117ced63257ac4ecdd7f44

Add
    exact package through external HTTPS, two files
    140 payload bytes / 705 canonical archive bytes
    catalog 919d13d6572bf0df807891783d3ea4e25dea16010717df8a2223081ab1df8564
```

Local path and HTTP/HTTPS URI differ without changing plan or resolved identity. The remote
fixtures also continue through session-scoped `ContentSetApplied`, guest admission, public
door/chest state, authoritative movement, Inventory UI, and spatial Chat. A damaged envelope or
disallowed/capacity-exceeding source leaves the cache/catalog and Game admission closed.

The committed machine-readable evidence is
[`content-a0.4-remote-source-parity-2026-09-06.json`](../../_Benchmarks/Baselines/content-a0.4-remote-source-parity-2026-09-06.json).
Its HTTP and HTTPS observations use real loopback transports (the TLS fixture uses a pinned
self-signed test certificate); they are source/protocol parity evidence, not public-WAN/CDN load or
trust-store evidence. Native resource realization and contentless Online entry remain A0.8.

Run all three semantic scenarios remotely.

Also run the same Replace package through:

1. server-hosted static HTTP;
2. external HTTPS fixture.

Required:

- no unnecessary Default transfer;
- Replace changes intended ids only;
- Add introduces new namespaced ids;
- local/remote fingerprints match;
- self-host/external fingerprints match;
- Game/collision/inventory/chat behavior remains correct;
- source URL/path absent from semantic fingerprint.

**Gate:** combined semantic+source parity artifact committed.

---

## A0.5 — warm cache / delta / corruption / mirror hardening

**Status: CLOSED.** The accepted source contract now has two compatible physical forms: the A0.4
complete `.uspkg` envelope and an A0.5 canonical manifest plus content-addressed blob layout. A
granular acquisition obtains the exact expected manifest, asks the CAS which unique hashes are
already valid, and fetches only missing or corrupt descriptors. File/hash reuse is the delta; no
field patch or binary-diff format was added.

One mirror candidate is fully materialized and hash/passive-format validated in private bounded
memory before installation. Unavailable, interrupted, and corrupt candidates fall through to the
next approved source without publishing partial work. Defaults bound this to eight source attempts
per package and four concurrent file requests. Required source exhaustion fails closed; an
unavailable Optional package may explicitly publish its fallback catalog, then later replace it
only through another complete catalog publication. Cache presence still does not persist
application-session proof.

Corrupt global blobs and realm manifest metadata are repairable only from an exact newly verified
candidate. Repair uses the existing staging/atomic-move path and refuses to replace a live-pinned
artifact. Realm authorization remains metadata-scoped even where two realms deduplicate one
physical blob; repair does not grant package authority to another realm. Interrupted file sets
never publish a manifest or activation, while any safely written orphan remains ordinary
unreachable CAS data.

The default HTTP transport now binds hostname policy to the actual connection. It resolves inside
`SocketsHttpHandler.ConnectCallback`, rejects a resolution set containing a private, loopback,
link-local, multicast, or otherwise prohibited endpoint unless private targets were explicitly
authorized, and connects to an admitted resolved address with redirects still disabled. This
closes the hostname/DNS-rebinding bypass left explicit at A0.4.

Cold authenticated acquisition is also isolated from the live client protocol/UI pump. One
session-fenced cancellable worker performs source/cache work; only an exact immutable completion is
consumed by the owner, and no `ContentSetApplied` proof or guest admission advances while that work
is pending. Teardown cancels and joins the operation before its cache/source lifetime ends.

Permanent evidence covers zero-byte warm reuse from A0.3, unavailable and corrupt first-mirror
fallback, a two-file revision transferring only one changed 25-byte payload while reusing the
other hash, bounded two-way concurrency under a four-worker cap, corrupt-blob refetch/atomic
repair, interrupted acquisition with zero published cache state, Optional fallback/hot upgrade,
connection-time private DNS rejection, existing restart/realm/quota/pin gates, and real granular
server-hosted HTTP reproducing the A0.1 Add fingerprint. See
[`content-a0.5-cache-hardening-2026-09-06.json`](../../_Benchmarks/Baselines/content-a0.5-cache-hardening-2026-09-06.json).

Add:

- warm-cache zero/reduced transfer;
- one-file-change delta;
- corrupt-cache repair;
- interrupted-download transactional behavior;
- optional package fallback/hot upgrade;
- verified realm semantic-ownership isolation;
- repeated reconnect/restart;
- quota pressure;
- unavailable first mirror -> valid fallback;
- corrupted first mirror -> hash reject -> valid fallback;
- bounded source retry/concurrency/timeouts.

Do not add arbitrary field-level Patch just to call this a delta.

**Gate:** evidence shows bytes scale with missing/changed files and source failure does not change final semantics.

---

## A0.6 — UI schema/content convergence

**Status: CLOSED.** One compiled UI definition now has a canonical bounded `.usui` passive
artifact. Its identity remains the UI kernel's exact key/version/hash and does not become a generic
texture/model logical ID. Content package validation performs complete canonical decode/re-encode;
the canonical package-local hash address permits bounded package lookups without decoding every
unrelated file, but never replaces the decoded typed identity;
an applied `ContentCatalogLease` pins selected realm manifests/blobs, and typed resolution searches
only those selected packages. Global CAS possession or another realm's metadata grants no UI
authority, while missing, duplicate, corrupt, or mismatched artifacts fail closed.

Client Core can prime its bounded exact definition cache without opening a view. For definitions
explicitly configured as content-backed under an exact required activation, the server reuses the
existing non-coalescible `HotOpen` operation rather than embedding a cold definition. The remote
client resolves the definition from its applied catalog, decodes the Game-owned model, opens the
view, and only then proves semantic application. Game retains authoritative model values, allowed
actions, action handlers, and validation; passive schema bytes cannot install code or invent an
authority endpoint. A semantic rejection desynchronizes the server view and does not advance
application knowledge.

Permanent evidence uses the real standard Inventory definition. Rooted local `.uspkg`, a
process-reopened warm CAS, server-hosted HTTP, and external HTTPS produce byte-identical compiled
definitions and one catalog fingerprint. The authenticated remote canary applies the required
catalog, opens Inventory through content-backed `HotOpen`, then submits a normal row-selection
action and receives the authoritative Game update. See
[`content-a0.6-ui-schema-convergence-2026-09-06.json`](../../_Benchmarks/Baselines/content-a0.6-ui-schema-convergence-2026-09-06.json).

**Gate:** Inventory/standard UI definition can be preinstalled, cached, self-hosted, or externally obtained with exact equivalent result.

---

## A0.7 — structural world streaming canary

**Status: CLOSED.** One existing complete Concept Demo WorldPack now travels as an ordinary
`ContentFileKind.WorldPack` leaf through the authenticated A0.3 requirement, A0.4 HTTP source, and
A0.2 CAS. `ContentWorldPackResolver` will inspect only the exact World definition owned by a
package selected by the live applied realm catalog. A matching global blob, base/local definition,
other-realm manifest, wrong typed definition, wrong pack fingerprint, or wrong World content
binding is insufficient.

World remains the artifact owner. `WorldPack.TryOpen(Stream, ...)` admits the canonical header
before one proportional World-owned allocation, consumes partial/non-seekable streams exactly,
rejects truncation and trailing bytes, and performs the existing complete structural validation.
The resolver then checks the exact `WorldPackFingerprint` and calls `WorldPack.TryBind` against the
composition-owned `WorldContentBinding`. Content blob SHA-256, WorldPack fingerprint, and World
content fingerprint/binding remain separate identities.

`RemotePlayableSession` keeps the resolved pack and candidate semantic cache private until every
initial structural chunk stages and publishes. Guest admission cannot be submitted before that
transaction completes. Capacity or validation failure exposes no partial cache and records an
explicit rejected structural state; catalog application remains distinct from structural
publication. Once ready, the ordinary dynamic-public protocol adapter attaches to the same cache,
without changing content protocol v18 or dynamic entity publication.

Permanent evidence transports the exact 731,158-byte, 162-chunk Concept Demo artifact over the
real server-hosted HTTP fixture. Its blob SHA-256, WorldPack/content fingerprints, statics, Multi
instances/fragments, additional terrain surfaces, resident count, and complete client semantic
hash match the preinstalled local oracle exactly. A subsequently admitted remote guest receives
ordinary dynamic-public state. A second canary proves a 161-slot structural limit rejects this
162-chunk candidate with zero published chunks. See
[`content-a0.7-worldpack-streaming-2026-09-06.json`](../../_Benchmarks/Baselines/content-a0.7-worldpack-streaming-2026-09-06.json).

**Gate:** complete WorldPack fingerprint/content binding and renderer structural source match the
local oracle independently from dynamic public replication. Native Godot Contentless Online
realization remains A0.8.

Later World-owned checkpoints—not A0.7—may define a canonical complete overlay artifact and, only if
earned afterward, an independently downloadable chunk artifact.

---

## A0.8 — Contentless Online closure

**Status: CLOSED.** The physically Online-only Godot composition now launches with
`ContentBaseCatalog.Empty`, Direct Connects to a separately running deployment, applies the exact
authenticated package through A0.2-A0.5, opens/binds the A0.7 WorldPack, privately publishes all
162 structural chunks, admits a remote guest, and realizes a nine-chunk native Godot window using
the always-available neutral primitive substrate for presentation leaves not supplied by content.

At A0.8 closure, the development Direct Connect deployment served one canonical package containing
the 731,384-byte Concept Demo WorldPack and the then-current 1,054-byte standard Inventory v6
`.usui`. The remote native journey proved
authoritative movement, dynamic-public state, cached typed Inventory UI, Chat occurrence/application
proof, and clean presentation-before-transport teardown. A cold run fetches two files / 732,438
payload bytes; after deployment restart with the same persisted key and `ApplicationRealmId`, the
same client cache fetches zero bytes but emits a fresh session-scoped `ContentSetApplied` proof.

The Online published dependency closure contains 12 `UltraSim` assemblies / 2,208,256 managed bytes
in the accepted Release census. It contains no `Client.Local`, built-in Test Worlds, Game
Composition/authority, Game Replication implementation, Server Runtime/Hosting/Persistence,
`.usworldpack`, or `.uspkg`. Its compatible Concept Demo profile contains stable binding vocabulary
and the expected WorldPack fingerprint, never the WorldPack bytes.

The permanent verifier now produces the actual Linux Mono Online export, inspects its exported
`.deps.json`, exact UltraSim assembly set, and copied payload, and runs the external cold/warm
journey against that exported executable. Godot 4.7 requires the one-project
`UltraSim.Client.Application.Godot.sln` beside the C# project; the verifier permanently guards that
export prerequisite. The accepted export contains 12 UltraSim assemblies / 2,331,648 managed
bytes, zero `.usworldpack`/`.uspkg` files, and none of the local-authority closure. See the original
native closure and its actual-export follow-up:
[`content-a0.8-contentless-online-2026-09-06.json`](../../_Benchmarks/Baselines/content-a0.8-contentless-online-2026-09-06.json) and
[`content-a0.8-native-export-closure-2026-09-07.json`](../../_Benchmarks/Baselines/content-a0.8-native-export-closure-2026-09-07.json).

The later Network capability-packaging closure does not change A0.8 content semantics. The current
Online product now carries the ClientOnly physical build of the canonical same-identity
`UltraSim.Network.dll`, so Hosted Server admission/listener/owner implementation is absent alongside
the already-absent Game/local authority. The canonical Full Network package remains compatible and
may replace that stripped deployment artifact later. Current packaging measurements and export
inspection are recorded separately in
[`network-capability-packaging-2026-09-08.md`](../../_Benchmarks/Baselines/network-capability-packaging-2026-09-08.md);
the A0.8 numbers above remain historical closure evidence.

Use the Contentless Online composition:

- no bundled playable WorldPack;
- no local authoritative Game/server implementation;
- only bootstrap/fallback assets plus Remote Play/content pipeline.

Direct Connect to the external Concept Demo deployment and acquire required content through the
source-independent pipeline.

**Gate:** external world becomes playable and matches the equivalent Complete/Single Player oracle where semantics are intended to match.

This A0.8 closure also satisfies and supersedes Client F0.6. Its original requirements—optional
local Test World content, realm/hash-owned passive cache, source-independent delivery, diagnosable
fallback, and a playable exported Online product without bundled world/local authority—are all
stronger permanent A0 gates now. Do not create a second client content-source campaign under the
F0.6 label.

---

## AX.0 — executable client extensions — NOT AUTHORIZED

Do not start this checkpoint merely because the format is documented.

Only begin when a concrete server/client feature cannot reasonably be expressed with:

- server Game code;
- server-composed UI;
- declarative controls/camera;
- passive Add/Replace content;
- semantic presentation events.

If authorized later, first checkpoint is manifest/loader/consent architecture only; no compiler and no silent execution.

---

# 28. Evidence artifact proposal

Retain machine-readable evidence including:

```text
scenario
client build profile
verified deployment fingerprint / application realm
content activation id / plan hash
package ids/revisions/hashes
package requirement source (authenticated server)
approved content sources
selected source
source failover attempts
files advertised
files already cached
files requested
files transferred
bytes transferred
files hash-verified
files reused
resolved logical ids
resolved owning layers/packages
resolved content fingerprint
catalog generation
ContentSetApplied proof/result
world/game semantic hash where comparable
client semantic world hash where comparable
presentation definition ids
structural chunk revisions/hashes where applicable
fallback activations
cache size/high-water
download/apply timings
```

For Replace, list expected replaced ids and prove no unrelated ownership changed.

For Add, list expected new ids and prove no collision.

Screenshots/manual smoke may supplement scale/material verification but are not sole correctness evidence.

---

# 29. Explicit non-goals for the first streaming campaign

Do not silently expand A0 into:

- executing server-provided DLL/WASM/scripts;
- source-code compilation in the client;
- public mod marketplace;
- P2P content distribution;
- building a production CDN service inside UltraSim;
- arbitrary field-level `patch` merge semantics;
- package dependency/version-range solving or applicability filters;
- universal one-priority-stack policy for every future content type;
- arbitrary server-supplied shader code;
- rebuilding Terrain/World identity around asset ids;
- moving actor replication onto asset streaming;
- server discovery/matchmaking;
- final texture atlas/hue/dye optimization before a real content workload earns it.

Simple self-hosted static HTTP and external HTTPS/CDN/object-storage byte-source support **are** in scope. UltraSim does not need to become a CDN; it only needs to advertise, validate, and consume approved immutable content sources.

Resource texture atlases, hue channels, named material slots, package dependency tooling, downloaded
model/audio importers, and executable extension runtimes remain valid future ideas and should be
recovered only when real workloads earn them.

---

# 30. Definition of done for the first asset-streaming campaign

A0 is complete when:

1. the same canonical package can be read locally, served by server-hosted static HTTP, or downloaded from external HTTPS without changing its semantic identity;
2. the authenticated Game server authorizes exact package identity/hash and required/optional policy;
3. content hosts are byte sources only;
4. one authenticated activation plan orders Default / Replace / Add deterministically;
5. Single Player Cabin provides deterministic local oracle fingerprints;
6. Online Cabin converges to those fingerprints after acquisition;
7. the server does not publish required content references before exact client application proof;
8. warm cache avoids redundant transfer;
9. one-file changes transfer only changed/missing bytes;
10. corrupted/partial downloads never publish;
11. mirror/source failover preserves final semantics;
12. CAS blobs and realm activation ownership remain bounded, path-safe, and distinct;
13. catalog publication and lazy Godot realization remain generation-fenced;
14. passive content validation is allowlist-based rather than pretending a filename blacklist proves safety;
15. UI schemas can reuse the cache/source substrate without losing typed UI identity;
16. one complete WorldPack canary preserves World-owned artifact/content binding identity;
17. Contentless Online can enter an externally hosted world with no bundled playable world/local authority implementation;
18. executable client extensions remain outside the normal passive content pipeline and unimplemented unless separately authorized.

At that point, asset streaming is a real content-distribution layer rather than a gameplay transport feature, and server operators can choose the hosting model that matches their scale without changing the game's content semantics.
