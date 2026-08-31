# UltraSim Asset Streaming / Content Layering Plan

**Status:** proposed/frozen companion plan to [`CLIENT.md`](CLIENT.md), based on V2 client/content archaeology and the current V3 thin-client direction.

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

CACHE IDENTITY IS SERVER/CONTENT-SCOPED; SAME RELATIVE PATH ON TWO SERVERS IS NOT THE SAME CACHE OWNERSHIP.

MISSING/INVALID CONTENT FALLS BACK VISIBLY WHEN POLICY ALLOWS; IT DOES NOT SILENTLY CHANGE GAMEPLAY TRUTH.

CONTENT TRANSFER IS BULK/STATIC DELIVERY, NOT REALTIME ACTOR REPLICATION.

GAME/CONTENT OWNS PACKAGE, FILE, LOGICAL-ID, REVISION, SOURCE LIST, AND APPLICATION SEMANTICS; KERNEL NETWORK DOES NOT.

LOCAL PLAY MAY READ THE SAME PACKAGE DIRECTLY WITHOUT NETWORK.

LOCAL PREINSTALLED CONTENT AND REMOTELY ACQUIRED IDENTICAL CONTENT MUST CONVERGE ON THE SAME RESOLVED CONTENT SET.

UI SCHEMAS, PRESENTATION ASSETS, AND STRUCTURAL WORLD CONTENT MAY SHARE CACHE/TRANSFER INFRASTRUCTURE WITHOUT SHARING SEMANTIC IDENTITY.

TERRAIN/STATIC CHUNK REVISION IS NOT AN ITEM/MOBILE/TEXTURE LOGICAL CONTENT ID.

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

Passive data/assets required or offered by the connected server and cached under that server/content identity.

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

## 3.3 Physical byte sources

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
dependencies
filters/applicability       where earned
```

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
- dependency validation;
- bounded counts/sizes before proportional allocation/import;
- passive-content type/import validation before active publication;
- runtime importer/resource-pressure checks before active publication.

Validation failure publishes **none** of the candidate package.

---

# 7. Passive-content allowlist, not extension blacklist

The V2 code rejected a useful list of obvious executable/script extensions. Keep that as defense-in-depth, but do not treat it as proof that every other file is safe.

The normal streaming path should be based on **supported passive content families**.

Conceptually:

```text
allowed passive model format(s)
allowed raster texture format(s)
allowed audio format(s)
validated engine-neutral material descriptors
validated UI schemas
validated content-definition metadata
validated world-pack/chunk formats
```

Unknown formats reject or remain inactive.

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

---

# 8. Server identity and cache namespace

Recover the V2 server-scoped cache idea.

A stable server/content identity maps to a path-safe cache namespace.

Conceptually:

```text
Cache/
    Global/
    Servers/
        <server identity hash>/
            Packages/
            Assets/
            Schemas/
            World/
    Extensions/      future executable extension install/cache
    Mods/            future user-managed local mods if kept distinct
```

Exact folders are not frozen.

Rules:

- same relative path from two different servers does not alias accidentally;
- cache paths cannot escape their root;
- schema/package/assets are associated with server identity and exact revision/hash;
- changing hostname while preserving a verified durable server identity should not necessarily create a new logical cache owner;
- endpoint/IP is not durable content identity;
- cache ownership is separate from Network `ConnectionId` and Game identity;
- byte-source URL is not cache identity;
- a mirror change must not invalidate otherwise-identical cached content.

Cross-server physical deduplication by content hash may be considered later, but server-scoped logical/cache ownership is the safe first implementation.

---

# 9. Cache publication and atomicity

Downloaded content is staged privately:

```text
authenticated requirement/manifest identity received
    -> resolve approved source list
    -> compare local cache hashes
    -> fetch missing/changed files
    -> stream to staging/temp storage
    -> verify size/hash
    -> validate complete package/definition graph
    -> prepare/import runtime assets
    -> atomically publish active package/content layer
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
- inactive server cache is quota-managed;
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
- runtime preparation succeeds.

Examples:

- a new player/mobile presentation with no acceptable fallback;
- structural world package required before a chunk can become resident;
- required UI/catalog definition for admission/game flow.

## Optional

Optional content may arrive after entry only when a safe lower-layer fallback exists.

```text
server offers high-resolution wolf model
client initially uses lower-layer/fallback wolf presentation
optional package arrives and verifies
client hot-replaces presentation resources
```

Optional content arriving late must not alter Game authority.

## Executable extension requirement is not passive content

If a future server declares a **required executable client extension**, refusal does not degrade to a primitive fallback. The client must refuse entry unless the user explicitly installs/approves the extension under the separate extension policy.

---

# 12. Content authority versus byte delivery

The authenticated Game/application session carries the authoritative requirement:

```text
PackageId
Version/Revision
ManifestHash / PackageHash
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

## Server namespace isolation

Two servers advertise same relative path with different hashes/content.

Expected no logical/cache ownership collision.

## Required versus optional

Prove required blocks use/entry; optional safely hot-upgrades from fallback; neither changes Game truth by download timing.

## Quota pressure

Prove active/pinned content remains, old unpinned server cache evicts, deletion cannot escape cache root.

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

There is currently no known client feature that requires executable extension distribution. Server Game code + server-composed UI + declarative control/camera profiles + passive content Add/Replace should cover the overwhelming majority of server customization.

Therefore executable extensions remain deferred.

If a real need later earns them, use a **separate package type and loader foundation**, not the normal passive asset package path.

## Precompiled runtimes only

Initial candidates:

```text
WASM
    preferred constrained/sandboxable extension runtime

precompiled .NET DLL
    advanced/full-trust extension runtime
```

Do not include a compiler in the stock client merely to compile server-supplied extension source.

No arbitrary source compilation on join.

## Extension manifest

A future extension package should carry identity/diagnostics equivalent to:

```text
ExtensionId
Name
Description
Version
PackageHash
FileHashes
Runtime = WASM | DotNet
EntryPoint
ClientApiMin
ClientApiMax
Dependencies[]
LoadBefore[] / LoadAfter[]       if actually needed
DeclaredCapabilities[]          especially for WASM
RequiredByServer / Optional
PublisherIdentity / Signature   later
Homepage/Source/Support         optional
```

## Deterministic loader/sorter

The loader should:

- validate unique ids/versions;
- validate package/file hashes;
- resolve dependencies deterministically;
- reject missing/incompatible dependencies;
- reject dependency/load-order cycles;
- reject incompatible Client API ranges;
- compute a deterministic active mod/extension-set fingerprint;
- never execute an unapproved candidate merely to inspect it.

A manifest/loader makes extensions more auditable/reproducible; it does **not** make arbitrary full-trust code safe.

## Consent

Server advertisement must clearly distinguish passive content from executable extensions.

For a required executable extension, present a high-severity consent surface equivalent to:

```text
WARNING — Executable Client Extension

This server requires client-side executable code:
    Name / Version
    Publisher/Server identity
    Package hash
    Runtime

Executable extensions may access capabilities available to the extension runtime.
Only continue if you trust this server/publisher.

[Cancel]
[Review Extension]
[Install and Connect]
```

Optional extensions also provide `[Connect Without Extension]` where compatible.

A signature may allow the user to trust future signed updates from a known publisher, but the UI/documentation must not imply that a signature proves safety.

Hash change means different executable bytes and should trigger appropriate update/consent policy unless explicitly covered by trusted signed-publisher policy.

## WASM versus DLL

WASM can eventually expose a constrained capability API such as:

- read already-disclosed client semantic state;
- draw custom extension UI;
- receive presentation events;
- store bounded extension-local settings;
- call explicitly granted client APIs.

Default WASM capability design should avoid arbitrary filesystem/process/native-library/raw-socket access.

A .NET DLL should be treated as full-trust third-party software unless UltraSim later isolates it in a separate process/sandbox with a real security boundary.

## Required extensions and Contentless Online

A server may eventually require an extension, but the stock Contentless Online client still remains useful with passive content only. Client-extension support itself should be an optional client capability/package so distributions can omit it entirely if desired.

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

- server cache namespace;
- SHA validation;
- cold/hot acquisition;
- source/mirror infrastructure;
- quota accounting.

It does **not** become a generic texture/model logical id if UI semantics already have stronger typed identity.

## Structural world content

Terrain/frozen statics/Multi/world-pack fragments are authoritative structural semantics, not ordinary mobile/material definitions.

Use identity equivalent to:

```text
server/world/content lineage
SpatialChunkKey
WorldChunkContentRevision
WorldChunkContentHash
section/content kind
```

The bytes may use the same cache/source/download substrate, but structural publication/application remains World/Client Core semantics.

---

# 24. Terrain/static hot patching

Recover the useful V2 goal:

- frozen/static chunk content can be cached client-side;
- server can authorize a newer structural chunk revision;
- housing/map edits can eventually produce changed structural content;
- client stages/verifies replacement structural semantics privately;
- renderer hot-swaps the affected structural layer;
- dynamic actors remain realtime publication/replication.

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
- server-scoped cache;
- validation;
- user acceptance (`Use Server Defaults` / `Keep Mine` / `Customize`);
- layered local settings resolution.

A server content package may reference a recommended profile identity, but that profile contains no executable client code.

---

# 27. Suggested V3 implementation roadmap

This campaign is adjacent to Client F0. Do not hide a large new subsystem inside one client commit.

## A0.0 — archaeology/contracts freeze

Document/test:

- package identity/version/pipeline compatibility;
- stable logical id;
- file descriptor + size/SHA;
- passive file/definition kind allowlist;
- Replace and Add/Append;
- built-in/default fallback;
- source/trust identity;
- required/optional package requirement;
- deterministic resolved content fingerprint;
- bounded manifest/file/package limits;
- authoritative package requirement separated from physical source list;
- Local / ServerHostedStatic / ExternalHttps source vocabulary;
- executable extensions explicitly outside normal content.

Defer partial Patch and executable-extension runtime implementation.

**Gate:** pure tests build/validate local packages and prove deterministic resolution with no Godot/Network.

---

## A0.1 — local resolver + Cabin Default/Replace/Add oracle

Use local/preinstalled sources only.

Prove:

1. Default Cabin.
2. Cabin Replace package.
3. Cabin Add package.

Record resolved fingerprints. Include X/Y human calibration presentation.

**Gate:** Single Player runs all variants without Network and intended logical ownership changes are exact/diagnosable.

---

## A0.2 — persistent cache/install substrate

Reintroduce/harden:

- server/content identity namespace;
- safe cache paths;
- package index;
- file staging;
- size/SHA verification;
- atomic publish;
- metadata/access tracking;
- quota/pinning/eviction;
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
-> server authorizes package id/version/hash + required policy + approved sources
-> compare cache
-> fetch missing files over selected content source
-> verify/install
-> publish layer
-> continue world entry
```

Do **not** require large file transfer through realtime Network Bulk.

**Gate:** Online Cabin cold-cache Replace result matches Single Player local Replace fingerprint exactly.

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
- server namespace isolation;
- repeated reconnect/restart;
- quota pressure;
- unavailable first mirror -> valid fallback;
- corrupted first mirror -> hash reject -> valid fallback;
- bounded source retry/concurrency/timeouts.

Do not add arbitrary field-level Patch just to call this a delta.

**Gate:** evidence shows bytes scale with missing/changed files and source failure does not change final semantics.

---

## A0.6 — UI schema/content convergence

Ensure server-composed UI cold/hot cache uses the same server/content cache ownership, validation, quota, and source substrate without losing typed UI identity.

**Gate:** Inventory/standard UI definition can be preinstalled, cached, self-hosted, or externally obtained with exact equivalent result.

---

## A0.7 — structural world streaming canary

After presentation streaming is proven, add one bounded structural-content canary using current WorldPack/chunk revision semantics.

Possible first canary:

- one Cabin/static chunk revision differs;
- client obtains exact new structural bytes from approved content source;
- prepares privately;
- swaps one semantic/render structural layer;
- dynamic entity replication unaffected.

**Gate:** structural revision/hash convergence and renderer replacement proven independently from dynamic public replication.

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
server/content identity
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
- universal one-priority-stack policy for every future content type;
- arbitrary server-supplied shader code;
- rebuilding Terrain/World identity around asset ids;
- moving actor replication onto asset streaming;
- server discovery/matchmaking;
- final texture atlas/hue/dye optimization before a real content workload earns it.

Simple self-hosted static HTTP and external HTTPS/CDN/object-storage byte-source support **are** in scope. UltraSim does not need to become a CDN; it only needs to advertise, validate, and consume approved immutable content sources.

Resource texture atlases, hue channels, named material slots, richer package dependencies, and executable extension runtimes remain valid future ideas and should be recovered only when real workloads earn them.

---

# 30. Definition of done for the first asset-streaming campaign

A0 is complete when:

1. the same canonical package can be read locally, served by server-hosted static HTTP, or downloaded from external HTTPS without changing its semantic identity;
2. the authenticated Game server authorizes exact package identity/hash and required/optional policy;
3. content hosts are byte sources only;
4. Default / Replace / Add resolve deterministically;
5. Single Player Cabin provides deterministic local oracle fingerprints;
6. Online Cabin converges to those fingerprints after acquisition;
7. warm cache avoids redundant transfer;
8. one-file changes transfer only changed/missing bytes;
9. corrupted/partial downloads never publish;
10. mirror/source failover preserves final semantics;
11. cache ownership/eviction remains bounded and path-safe;
12. passive content validation is allowlist-based rather than pretending a filename blacklist proves safety;
13. UI schemas can reuse the cache/source substrate without losing typed UI identity;
14. one structural world-content canary preserves World/chunk identity;
15. Contentless Online can enter an externally hosted world with no bundled playable world/local authority implementation;
16. executable client extensions remain outside the normal passive content pipeline and unimplemented unless separately authorized.

At that point, asset streaming is a real content-distribution layer rather than a gameplay transport feature, and server operators can choose the hosting model that matches their scale without changing the game's content semantics.
