# UltraSim Asset Streaming / Content Layering Plan

**Status:** frozen ownership/security direction with A0.0 content contracts/resolution accepted,
companion to [`CLIENT.md`](CLIENT.md). The client executable/local-session/shell foundation
(F0.0a/F0.0b/F0.1) and A0.0 are closed; A0.1 is the next local content-oracle checkpoint. Runtime mesh/audio
import, package
dependencies, executable extensions, mutable overlay artifacts, and independently streamed chunk
artifacts remain deferred.

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
- built-in fallback content;
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

The current repository has useful content-adjacent authority, but not the A0 acquisition/catalog
pipeline yet:

- World owns canonical complete `WorldPackArtifact` bytes/fingerprints and bounded open/validation;
- World owns mutable overlay revisions and per-chunk content hashes plus persistence serialization,
  but no public independently downloadable chunk artifact;
- Client Core/UI already retain typed definition/cache identity and generation-aware semantic state,
  but there is no general passive-content activation plan, CAS, or HTTP acquisition service;
- the playable Cabin and X/Y calibration assets are currently build-time/fixture composition;
- no `ContentSetApplied`, immutable resolved content catalog, or lazy downloaded-resource realization
  contract is implemented yet.

A0 extracts/builds those missing content contracts. It does not reinterpret current persistence,
UI cache keys, or World chunk hashes as if they already formed a streaming package system.

---

# 2. Frozen principles

```text
CONTENT IDENTITY IS LOGICAL; FILE PATH IS NOT GAME/PRESENTATION IDENTITY.

CONTENT FILES AND CONTENT DEFINITIONS ARE DIFFERENT THINGS.

A PACKAGE MAY BE PARTIAL.

BUILT-IN CONTENT IS THE ALWAYS-AVAILABLE FALLBACK LAYER, NOT THE ONLY CONTENT SOURCE.

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

THE CONTENTLESS ONLINE CLIENT REMAINS BOOTABLE THROUGH BUILT-IN BOOTSTRAP UI/FALLBACK ASSETS.
```

---

# 3. Three independent axes

## 3.1 Source / trust classes

### Built-in / bootstrap defaults

Always available in a client distribution that chooses to ship them.

Examples:

- minimal local application UI;
- fallback font;
- missing-content checker texture;
- primitive box/capsule fallback;
- standard compiled UI definitions;
- X Bot / Y Bot human calibration bodies in builds that include the normal presentation pack;
- Cabin Test World content in Single Player/Complete builds that explicitly include it.

Built-in content is lowest-level fallback content. A Contentless Online distribution may intentionally ship only the bootstrap/fallback subset.

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

No higher permitted layer owns the logical id. Resolve the built-in/lower-layer definition.

```text
ultrasim:mobile/human_female
    -> built-in X Bot definition
```

`Default` is a resolution outcome, not necessarily a manifest operation.

### Replace

A higher permitted layer defines an already-known logical id and replaces its presentation definition/resource resolution.

```text
built-in:
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

`ContentActivationId` is a stable realm-scoped authored activation/revision identity, not a
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

X/Y human meshes remain bundled/build-time calibration assets initially. Downloaded GLB and audio
are later separately reviewed importer checkpoints, not implicit A0 formats.

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

Exact folders are not frozen.

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

Eviction initially uses reachability: pinned realm activations/manifests and live engine resource
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
- built-in content is installation content, not server cache.

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

---

# 25. Fallback and diagnostic presentation

Built-in fallback should include at least:

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
- built-in/default fallback;
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

Defer dependencies/filters, partial Patch, downloaded GLB/audio, mutable World artifacts,
independent chunk artifacts, and executable-extension runtime implementation.

**Gate:** pure tests build/validate local packages and prove deterministic resolution with no Godot/Network.

---

## A0.1 — local resolver + Cabin Default/Replace/Add oracle

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

---

## A0.3 — authenticated requirement + source-independent required-package join

Add the Remote Play/Game/content-owned package requirement advertisement.

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

Ensure server-composed UI cold/hot cache uses the same verified application-realm/content ownership,
validation, quota, and source substrate without losing typed UI identity.

**Gate:** Inventory/standard UI definition can be preinstalled, cached, self-hosted, or externally obtained with exact equivalent result.

---

## A0.7 — structural world streaming canary

After presentation streaming is proven, carry one existing complete World-owned artifact through the
content byte/cache substrate.

First canary:

- Contentless client has no Cabin WorldPack;
- server authorizes the exact complete Cabin WorldPack fingerprint/content binding;
- client obtains and caches the complete canonical WorldPack bytes;
- `WorldPack.TryOpen` and World content binding validate them;
- client publishes the initial structural semantic/render world;
- dynamic entity replication unaffected.

**Gate:** complete WorldPack fingerprint/content binding and renderer result match the local oracle,
independently from dynamic public replication.

Later World-owned checkpoints—not A0.7—may define a canonical complete overlay artifact and, only if
earned afterward, an independently downloadable chunk artifact.

---

## A0.8 — Contentless Online closure

Use the Contentless Online composition:

- no bundled playable WorldPack;
- no local authoritative Game/server implementation;
- only bootstrap/fallback assets plus Remote Play/content pipeline.

Direct Connect to external Cabin server and acquire required content through the source-independent pipeline.

**Gate:** external world becomes playable and matches the equivalent Complete/Single Player oracle where semantics are intended to match.

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
