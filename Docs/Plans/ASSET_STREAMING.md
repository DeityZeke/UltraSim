# UltraSim Asset Streaming / Content Layering Plan

**Status:** proposed/frozen companion plan to [`CLIENT.md`](CLIENT.md), based on V2 client/content archaeology and the current V3 thin-client direction.

**Purpose:** restore the useful V2 content-pack, resolver, server-patch, cache, and asset-streaming ideas without reviving the old client/runtime ownership mistakes. The first proof uses the same Cabin in the Woods world locally and remotely so a preinstalled Single Player composition can act as a deterministic oracle for streamed Online content.

The central decision is:

> **Content source/trust and content semantic operation are different axes.**
>
> Built-in assets, server-delivered assets, user resource packs, and future user mods answer **where content came from / what is allowed to own it**. Default, replace, add/append, and future partial patch answer **what a content definition does to the resolved catalog**.

Do not collapse those into one giant priority enum or one generic "mod" concept.

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
- server-scoped patch/cache namespaces;
- content-pack dependencies and filters;
- package/pipeline compatibility ranges;
- per-file SHA-256 integrity;
- whole-pack content identity;
- `append`, `replace`, and `patch` definition modes;
- required versus optional server content;
- server advertisement followed by missing-file download;
- file-level delta behavior through hash comparison;
- `.uspkg` archive/folder source abstraction;
- server patch rejection of executable/script files;
- cache metadata/indexing;
- pinning and quota/eviction planning;
- atomic temporary-file replacement;
- UI schema caching on the same server identity/cache substrate;
- terrain/static chunk patch hooks separate from realtime actor replication;
- explicit separation between resource packs, server patches, and executable client mods.

V2 also had two partially overlapping representations:

1. `ContentPackManifest` — rich logical definitions, files, dependencies, filters, definition modes.
2. `ServerPatchManifest` — server-specific file list/revision/cache validation.

V3 should **not** recreate two competing semantic notions of a patch. The server delivery protocol should advertise/install validated content packages whose file hashes and logical definitions are understood by the same content pipeline used for local/preinstalled packs. A small transport/download manifest may still exist, but it must point to the canonical package/content identity rather than becoming a second definition system.

---

# 2. Frozen principles

```text
CONTENT IDENTITY IS LOGICAL; FILE PATH IS NOT GAME/PRESENTATION IDENTITY.

CONTENT FILES AND CONTENT DEFINITIONS ARE DIFFERENT THINGS.

A PACKAGE MAY BE PARTIAL.

BUILT-IN CONTENT IS THE ALWAYS-AVAILABLE FALLBACK LAYER, NOT THE ONLY CONTENT SOURCE.

SERVER CONTENT IS DATA/ASSETS, NEVER EXECUTABLE CLIENT CODE.

USER RESOURCE PACKS ARE LOCAL PRESENTATION REMAPS, NOT GAME AUTHORITY.

USER CLIENT MODS ARE AN EXPLICIT FUTURE EXECUTABLE EXTENSION CLASS AND ARE NEVER SILENTLY SERVER-INSTALLED.

CONTENT SOURCE/TRUST AND CONTENT SEMANTIC OPERATION ARE SEPARATE AXES.

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

NETWORK MOVES BOUNDED BYTES; CONTENT/GAME DEFINES PACKAGE, FILE, LOGICAL-ID, REVISION, AND APPLICATION SEMANTICS.

LOCAL PLAY MAY READ THE SAME PACKAGE DIRECTLY WITHOUT NETWORK.

LOCAL PREINSTALLED CONTENT AND REMOTELY STREAMED IDENTICAL CONTENT MUST CONVERGE ON THE SAME RESOLVED CONTENT SET.

UI SCHEMAS, PRESENTATION ASSETS, AND STRUCTURAL WORLD CONTENT MAY SHARE CACHE/TRANSFER INFRASTRUCTURE WITHOUT SHARING SEMANTIC IDENTITY.

TERRAIN/STATIC CHUNK REVISION IS NOT AN ITEM/MOBILE/TEXTURE LOGICAL CONTENT ID.

THE CONTENTLESS ONLINE CLIENT REMAINS BOOTABLE THROUGH BUILT-IN BOOTSTRAP UI/FALLBACK ASSETS.
```

---

# 3. Two independent axes

## 3.1 Source / trust classes

V2 explicitly preserved three extension classes plus a built-in fallback base.

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

### Server-specific streamed content

Data/assets advertised by the connected server and cached under that server/content identity.

May include, subject to capability/schema support:

- models;
- textures;
- materials;
- icons;
- sounds;
- mobile/item presentation definitions;
- server-composed UI definitions/schemas;
- world/static content packages;
- server-specific visual remaps;
- declarative client/control profile data.

Server content may be required or optional.

Server content must not include executable DLLs/scripts/native libraries or runtime code injection.

### User resource/texture packs

Explicit client-local presentation remaps selected by the user.

Examples:

- alternate textures;
- icon sets;
- UI themes;
- allowed model/audio replacements;
- local sound suppression/remapping.

They do not alter protocol, Game authority, collision, or server semantics.

### User client mods — future

Explicit opt-in executable/script extensions installed by the user.

They are a different trust class from resource packs and server patches.

They are deferred from the current client/streaming campaign.

## Historical resolver ordering

The archived V2 concrete resolver used:

```text
BuiltIn            0
UserResourcePack 100
ServerPatch       200
UserMod           300
```

That historical ordering is useful evidence, but V3 does **not** freeze one universal ordering for every future content kind. A server-required semantic presentation definition and a user texture remap may need different policy from a local sound filter. First implement only the ordering required by the Cabin streaming tests, and keep trust/source identity explicit so later policy can be content-kind aware without replacing the cache/package system.

---

## 3.2 Semantic operations

The V2 manifest supported `append`, `replace`, and `patch`.

For the first V3 proof, use the simpler observable triad:

### Default

No higher layer owns the logical id.

Resolve the built-in/lower-layer definition.

Example:

```text
ultrasim:mobile/human_female
    -> built-in X Bot definition
```

`Default` need not be a manifest operation; it is the result of normal fallback resolution.

### Replace

A higher permitted layer defines an already-known logical id and replaces its presentation definition/resource resolution.

Example:

```text
built-in:
    ultrasim:test/cabin_wall_material -> default material

server layer:
    ultrasim:test/cabin_wall_material -> alternate material
```

Game/world identity does not change merely because presentation content changed.

### Add / Append

A layer defines a new logical id that did not exist in lower layers.

Example:

```text
my_server:mobile/clockwork_guard
my_server:item/moon_lantern
my_server:material/moonsteel
```

The server/Game may then publish an entity/presentation definition that legitimately refers to that new id.

### Patch — historical, deferred

V2 named a partial `patch` mode, but the recovered validator primarily proved that the mode was admissible; it did not establish a complete, immutable field-level merge contract that we should blindly freeze into V3.

Do **not** revive arbitrary partial-definition merge semantics in the first checkpoint.

If a real workload later needs:

- change only hue defaults;
- change only a camera/nameplate anchor;
- patch one material slot;
- patch one UI style field;

then earn an explicit typed merge contract for that definition family.

Until then, replace the complete definition.

---

# 4. Stable logical ids

Presentation/game definitions reference stable logical ids, not engine paths.

Preferred shape remains namespaced ids such as:

```text
ultrasim:mobile/human_female
ultrasim:mobile/human_male
ultrasim:item/chest
ultrasim:material/oak
ultrasim:ui/inventory_standard
my_server:mobile/clockwork_guard
```

A logical definition may reference files inside its owning package, for example:

```text
logical definition
    ultrasim:mobile/human_female

package file
    models/human_female.glb
```

Godot `res://...`, local cache paths, archive member paths, temporary download filenames, and server URLs are never durable logical content identity.

---

# 5. Files versus semantic definitions

Retain the V2 distinction.

## File entries answer

```text
What bytes exist?
What type of source file is this?
How large is it?
What SHA-256 must it have?
```

## Definition entries answer

```text
What logical presentation slot does this content fill?
Is this a mobile/item/material/icon/UI/sound/etc. definition?
Which files/resources does it use?
Is it adding or replacing a logical id?
What anchors/material slots/hue channels/presentation metadata are declared?
```

One file may support several definitions; one definition may reference several files.

Do not use filenames as the resolver catalog.

---

# 6. Package identity and validation

The V2 content package already had useful identity fields:

```text
pack id
name
version
minimum pipeline format
maximum pipeline format
optional whole content hash
per-file path/kind/size/SHA-256
```

V3 should retain equivalent semantics.

Required validation before publication:

- supported manifest/schema version;
- supported content pipeline format;
- valid package id/version;
- path normalization and path traversal rejection;
- no duplicate file paths;
- no duplicate definition ids within one package unless an explicit future merge contract allows it;
- allowed file/definition kinds;
- allowed operation mode;
- file reference must name a declared file;
- file byte length matches declaration;
- file SHA-256 matches declaration;
- dependency validation;
- bounded counts/sizes before proportional allocation/import;
- executable/script/native-code rejection for server-delivered content;
- engine importer/runtime asset constraints before active publication.

Validation failure publishes **none** of the candidate package.

---

# 7. Server identity and cache namespace

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
    Mods/            future explicit user mods
```

Exact folders are not frozen.

Rules:

- the same relative asset path from two different servers does not alias accidentally;
- cache paths cannot escape their root;
- schema/package/assets are associated with server identity and exact revision/hash;
- changing a server hostname while preserving a verified durable server identity should not necessarily force a new logical cache owner;
- endpoint/IP is not durable content identity;
- cache ownership is separate from Network `ConnectionId` and Game identity.

---

# 8. Cache publication and atomicity

Recover the V2 atomic write/cache discipline and harden it.

Downloaded content is staged privately:

```text
manifest advertised
    -> compare local cache hashes
    -> request missing/changed files
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

Second joins should validate/use already cached matching files rather than re-transfer them.

---

# 9. Quota, pinning, and eviction

The old cache already had a useful policy canary:

- compute total cache size;
- when over quota, target a lower fill percentage;
- never evict pinned entries;
- prefer oldest unpinned entries;
- delete only paths proven inside cache root.

Retain the concept, not necessarily the exact numbers.

Important distinctions:

- currently active required content should be pinned for its session/lifetime;
- an in-use UI schema or loaded asset may not be evicted underneath its active handle;
- cached but inactive server content is eligible according to quota policy;
- user-installed resource packs/mods are not ordinary server-cache eviction candidates;
- built-in content is part of the installation, not server cache.

---

# 10. Required versus optional server content

## Required

Required content blocks the semantic/world entry point that needs it until:

- package manifest is valid;
- required files are present and verified;
- definitions resolve;
- necessary runtime imports/preparation succeed.

Examples may include:

- a completely new player/mobile model required to interpret the server's presentation definition;
- structural world package needed before a chunk can become resident;
- required UI/catalog definition needed for admission flow.

## Optional

Optional content may arrive after entry only when the client has a safe lower-layer fallback.

Example:

```text
server offers high-resolution wolf model
client initially has generic/fallback wolf presentation
optional package arrives
client safely hot-replaces presentation resources
```

Optional content must not alter Game authority by arriving late.

---

# 11. Network ownership and delivery

Server asset/content transfer should use the existing Network delivery machinery, likely the reliable Bulk lane for bounded manifest/file segments where appropriate.

Network owns:

- connection/session transport;
- reliability/order/fragmentation;
- bounded packetization;
- crypto;
- congestion/send policy.

Content/Game owns:

- package identity;
- package revision/hash;
- logical definition ids;
- required/optional status;
- file identity/hash;
- which content a server needs the client to possess;
- content application/publish semantics.

Avoid putting concepts such as `mobile definition`, `material slot`, `Cabin patch`, or `world chunk revision` into the Network kernel.

The server may provide an external download URL later, as V2 anticipated, but authorization, identity, hashes, and final client validation remain the same. An external CDN is a byte source, not semantic authority.

---

# 12. Local play uses the same content packages without Network

Single Player is the clean reference implementation for content application.

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
server advertises same package identity
    -> client fetches missing package files
    -> validates same package
    -> installs/resolves same active content layer
    -> same Client Core catalog/state
    -> same Godot presentation
```

Therefore:

> **Preinstalled local content and remotely downloaded identical content must produce the same deterministic resolved content fingerprint/catalog.**

That gives us a real oracle for streaming correctness.

---

# 13. Resolved content fingerprint

Add an engine-neutral diagnostic/acceptance concept equivalent to a deterministic `ResolvedContentFingerprint`.

It should cover the semantically active resolved catalog, not machine-local paths.

For each relevant logical id, include deterministic identity such as:

```text
logical id
resolved owning package/layer identity
canonical definition hash
referenced file content hashes
```

Sort deterministically before hashing.

Do **not** include:

- cache root absolute path;
- temporary filename;
- Godot object/RID/node identity;
- download URL;
- connection id.

This makes local-versus-streamed comparisons precise without relying only on screenshots.

A separate renderer/presentation-plan fingerprint may be earned if needed.

---

# 14. Cabin in the Woods becomes the streaming oracle

Use the same Cabin Test World in Single Player and externally hosted Online runs.

Do not create a synthetic asset-only micro-demo unless a lower-level unit test needs one.

The world should prove the content pipeline while simultaneously exercising real actor/world/UI presentation.

## Scenario A — Default

### Local oracle

Run Single Player Cabin using only the built-in/default content set.

Record:

- authoritative world/game semantic hash where useful;
- resolved content fingerprint;
- relevant logical-id resolutions;
- human actor presentation definition and anchors;
- renderer-neutral presentation counts/identities;
- optional screenshot/manual visual oracle.

### Remote

Host the same Cabin world externally and advertise no server replacements/additions, or advertise package/file hashes already exactly satisfied by built-ins/cache.

Expected:

- no unnecessary asset download;
- same Game semantics;
- same resolved content fingerprint;
- same actor/cabin presentation;
- same X/Y human scale/camera/nameplate calibration.

This proves **default/fallback parity**.

---

## Scenario B — Replace

Create a small deterministic Cabin replacement package.

Good first replacements are presentation-only so Game authority remains unchanged.

Examples:

- replace the logical player/human presentation from one known human model/skin/material to another;
- replace the cabin wall material/texture with a highly obvious test material;
- replace the chest/item presentation while retaining its Game identity/collision/container semantics;
- replace a standard icon/UI skin resource.

### Local oracle

Apply the exact replacement package as a preinstalled/local content layer in Single Player.

### Remote

External Cabin server advertises the exact same package; Online client streams it from cold cache.

Expected:

```text
base Game/world semantics             unchanged for presentation-only replacement
local replacement content fingerprint == remote streamed content fingerprint
replaced logical ids                  differ from Default exactly as expected
unrelated logical ids                 remain Default
collision/inventory/chat semantics    unchanged
```

This proves **replace** without conflating it with Game state.

A visibly extreme replacement is desirable for human/manual smoke — for example an unmistakable material or swapping between the already-available Human Female/Human Male calibration definitions — but automated proof should compare logical/file hashes, not only pixels.

---

## Scenario C — Add

Create a small package that introduces at least one new namespaced logical id.

Examples:

```text
test_cabin:mobile/clockwork_dummy
test_cabin:item/blue_lantern
test_cabin:material/blue_glow
test_cabin:icon/streamed_badge
```

The externally hosted test world should contain one legitimate public/Game semantic reference to the new presentation definition where appropriate.

### Local oracle

Run the same modified Cabin test package/content preinstalled locally.

### Remote

Stream the package to the Online client.

Expected:

- new id does not collide with built-in ids;
- local preinstalled and remote streamed resolved content fingerprints match;
- new entity/item/UI resource presents correctly;
- existing Default content remains intact;
- removal/absence of the package produces the declared required-content failure or visible fallback behavior, never undefined presentation.

This proves **add/append**.

---

# 15. Stronger cache/delta acceptance matrix

After Default/Replace/Add work, retain these permanent tests.

## Cold cache

Client has no server package cached.

Expected:

- manifest advertisement;
- required files requested;
- exact expected byte/file count transferred;
- verify/install/publish;
- world entry according to required/optional policy.

## Warm cache

Reconnect with exact matching package/file hashes.

Expected:

- no redundant file transfer;
- same content fingerprint;
- same presentation.

## One-file revision

Change exactly one file in a package and advance package/revision identity appropriately.

Expected:

- client reuses unchanged matching files;
- only missing/changed file bytes transfer;
- candidate layer publishes atomically after validation;
- final fingerprint matches equivalent locally preinstalled package.

## Corrupt cache

Mutate/truncate one cached asset while leaving stale metadata.

Expected:

- size/hash verification detects corruption;
- corrupted file is not trusted;
- re-fetch/repair path runs;
- valid old layer/fallback remains until candidate is complete.

## Interrupted download

Stop connection mid-file/package.

Expected:

- partial candidate never becomes active;
- reconnect can resume/re-request according to implementation policy;
- no poisoned final cache entry.

## Server namespace isolation

Two servers advertise the same relative path with different hashes/content.

Expected:

- no cache collision;
- each server resolves its own content.

## Required versus optional

Prove:

- required package blocks the relevant entry/use until ready;
- optional package may hot-upgrade from fallback;
- optional package failure does not corrupt authoritative Game state.

## Quota pressure

Prove:

- active/pinned content remains;
- old unpinned cached content is selected for eviction;
- deletion cannot escape cache root.

---

# 16. Human calibration models are part of the content proof

Recover the V2 X/Y Bot presentation assets as calibration content where licensing/source permits the repository to continue using them.

Historical V2 definitions used:

```text
X Bot -> Human Female
Y Bot -> Human Male
VisualScale        1,1,1
CameraPivotOffset  y=1.6m
NameplateOffset    y=2.0m
EffectsOffset      y=0.9m
```

These are starting calibration values, not permanent architectural constants.

Use real human models in Cabin to validate:

- authoritative body versus visual scale;
- door/chest/wall scale;
- terrain-cell feel;
- camera distance/pitch/pivot;
- movement-speed feel;
- nameplate anchor;
- overhead chat anchor;
- effects anchor.

Because the models themselves are content definitions, they also make excellent Replace streaming targets.

---

# 17. Presentation content, UI schemas, and structural world content

These may share storage/transfer primitives but retain separate semantic owners.

## Presentation package content

Logical-id resolver domain:

- mobiles;
- items;
- models;
- textures;
- materials;
- icons;
- audio;
- presentation metadata.

Default/Replace/Add semantics naturally apply here.

## UI schema content

Uses exact UI definition/catalog identity from the UI kernel.

It may share:

- server cache namespace;
- SHA validation;
- cold/hot cache transport;
- quota accounting.

It does **not** become a generic texture/model logical id if UI semantics already have stronger typed identity.

## Structural world content

Terrain/frozen statics/Multi/world-pack fragments are authoritative world structural semantics, not ordinary mobile/material definitions.

Current V3 already has:

- `WorldPack/v1` immutable base terrain/statics/Multi content;
- mutable overlay/durable structural identity;
- chunk content revision/hash;
- client semantic chunk residency;
- Godot terrain/static rendering.

Future server structural streaming should therefore use identity equivalent to:

```text
server/world/content lineage
SpatialChunkKey
WorldChunkContentRevision
WorldChunkContentHash
section/content kind
```

not:

```text
"texture/cabin_chunk_4"
```

The bytes can travel through the same bounded Bulk/content-transfer system and live in the same server cache root, but structural publication/application remains World/Client Core semantics.

---

# 18. Terrain/static hot patching

Recover the useful V2 goal:

- frozen/static chunk content can be cached client-side;
- server can publish a newer structural chunk revision;
- housing/map edits can eventually produce changed structural content;
- client prepares replacement structural semantics privately;
- renderer hot-swaps the affected structural layer;
- dynamic actors remain on the realtime publication/replication path.

Do not send every static/terrain object as an ECS entity merely because remote content is patchable.

Do not treat camera visibility as structural content lifetime.

A later structural-streaming checkpoint must preserve the current T0 rule that immutable base content, mutable overlay, client semantic residency, and Godot render slots are distinct identities/lifetimes.

---

# 19. Fallback and diagnostic presentation

The content pipeline and the previously discussed emergency fallback system should be designed together.

Built-in client fallback should include at least:

```text
missing model/object
    -> simple box/capsule/category primitive

missing texture/material
    -> extremely obvious checker/rainbow debug texture when texture debug is enabled

neutral fallback mode
    -> simple grey/default presentation when texture debug is disabled

missing animation
    -> stable static pose/bounded presentation timer rather than Game failure
```

Important:

- fallback is presentation only;
- authoritative collision/size comes from Game/Physics/world semantics, not inferred from fallback mesh;
- a required package may still block entry even though fallback assets exist, if the server declares fallback insufficient;
- optional/missing presentation content should fail visibly and diagnosably.

---

# 20. Control/settings profiles as server data

The declarative server control/camera profile discussed in `CLIENT.md` belongs to the same **server data/cache** security class as other non-executable server content, but it is not necessarily an asset pack definition.

It should use:

- exact profile identity/revision/hash;
- server-scoped cache;
- validation;
- user acceptance (`Use Server Defaults` / `Keep Mine` / `Customize`);
- layered local settings resolution.

A server content package may reference the recommended control/profile identity, but the profile must not contain executable client code.

---

# 21. Suggested V3 implementation roadmap

This campaign is adjacent to the Client F0 roadmap. Do not hide a large new subsystem inside one F0.6 commit.

## A0.0 — archaeology/contracts freeze

Document and test the minimum engine-neutral vocabulary:

- package identity/version/pipeline compatibility;
- stable logical content id;
- file descriptor + size/SHA;
- content definition kind;
- first operations: Replace and Add/Append;
- built-in/default fallback resolution;
- source/trust identity;
- required/optional server package requirement;
- deterministic resolved content fingerprint;
- bounded manifest/file/package limits.

Explicitly defer partial `Patch` merge.

**Gate:** pure tests can build local packages and prove deterministic validation/resolution with no Godot/Network.

---

## A0.1 — local resolver + Cabin default/replace/add oracle

Implement the resolver/catalog path locally first.

Use preinstalled/local package sources only.

Prove:

1. Default Cabin.
2. Cabin Replace package.
3. Cabin Add package.

Record deterministic resolved fingerprints for each.

Include X/Y real human calibration presentation.

**Gate:** Single Player can run all three variants without Network, and every intended logical ownership change is exact/diagnosable.

---

## A0.2 — persistent cache/install substrate

Reintroduce/harden:

- server/content identity namespace;
- safe cache paths;
- manifest/package index;
- file staging;
- size/SHA verification;
- atomic publish;
- metadata/access tracking;
- quota/pinning/eviction;
- corrupt-cache detection;
- no executable server content.

**Gate:** local test harness can install/read/reopen package bytes through the same cache format Remote Play will use.

---

## A0.3 — server advertisement + required package cold join

Add Game/content-owned advertisement/request semantics over the Remote Play protocol and Network Bulk delivery.

Prove one required Replace package first.

Flow:

```text
connect
-> advertise package manifest/identity
-> compare cache
-> request missing files
-> bounded reliable transfer
-> verify/install
-> publish layer
-> continue/complete world entry
```

**Gate:** Online Cabin cold-cache Replace result matches the Single Player preinstalled Replace fingerprint exactly.

---

## A0.4 — Default / Replace / Add remote parity matrix

Run all three scenarios remotely and compare to their local Single Player oracle counterparts.

Required assertions:

- Default transfers no unnecessary content;
- Replace changes only intended presentation logical ids;
- Add introduces new namespaced logical ids without disturbing defaults;
- remote/local resolved fingerprints match per scenario;
- Game semantic/collision/inventory/chat behavior remains correct;
- content source path does not leak into semantic fingerprint.

**Gate:** three-scenario parity artifact committed.

---

## A0.5 — delta/warm-cache/hot-update hardening

Add:

- warm-cache zero/reduced transfer proof;
- one-file-change delta proof;
- corrupt-cache repair;
- interrupted-download transactional behavior;
- optional package fallback/hot upgrade;
- server namespace isolation;
- repeated reconnect/restart;
- cache quota pressure.

Do not add arbitrary field-level definition patching merely to call this a delta; file/package delta and atomic replacement are sufficient.

**Gate:** cache/network evidence shows bytes scale with missing/changed files rather than entire installed content.

---

## A0.6 — UI schema/content convergence

Ensure server-composed UI cold/hot cache uses the same server/content cache ownership, validation, quota, and diagnostics substrate without losing UI's own typed definition identity.

**Gate:** Inventory/standard UI definition can be preinstalled, cached, or obtained remotely with exact equivalent result.

---

## A0.7 — structural world streaming canary

Only after presentation asset streaming is proven, add one bounded structural world-content canary using current WorldPack/chunk revision semantics.

Possible first canary:

- one Cabin/static chunk presentation revision differs on server;
- client receives exact new structural content;
- prepares privately;
- swaps one client semantic/render structural layer;
- dynamic entity replication unaffected.

Do not redesign Terrain T0 or make structural content generic asset definitions.

**Gate:** structural revision/hash convergence and renderer replacement are proven independently of dynamic public-state replication.

---

## A0.8 — Contentless Online closure

Use the Client Contentless Online composition:

- no bundled playable WorldPack;
- no local authoritative Game/server implementation;
- only bootstrap/fallback assets plus Remote Play/content pipeline.

Direct Connect to external Cabin server and acquire everything not in bootstrap according to required/optional policy.

**Gate:** external world becomes playable and matches the equivalent Complete/Single Player content oracle where semantics are intended to match.

---

# 22. Evidence artifact proposal

For the Cabin parity matrix, retain one machine-readable artifact per scenario or one combined report containing:

```text
scenario
client build profile
server/content identity
package ids/revisions/hashes
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

For Replace, explicitly list expected replaced ids and prove no unrelated id changed ownership.

For Add, explicitly list expected new ids and prove no collision with existing ids.

Screenshots/manual smoke may supplement this, especially for scale/material verification, but should not be the only correctness evidence.

---

# 23. Explicit non-goals for the first streaming campaign

Do not silently expand A0 into:

- executable client mods;
- server-delivered scripts/DLLs;
- arbitrary C# runtime injection;
- public mod marketplace;
- P2P content distribution;
- CDN infrastructure beyond a simple optional byte-source seam;
- arbitrary field-level `patch` merge semantics;
- universal one-priority-stack policy for every future content type;
- rebuilding Terrain/World identity around asset ids;
- moving actor replication onto the asset streaming path;
- server discovery/matchmaking;
- final texture atlas/hue/dye optimization before a real content workload earns it.

Resource texture atlases, hue channels, named material slots, and richer package dependencies remain valid V2 ideas and should be recovered when the first real item/equipment content workload needs them.

---

# 24. Definition of done

The first Asset Streaming campaign is successful when:

1. content resolves by stable logical id, not engine path;
2. built-in fallback/default content remains valid independently;
3. Single Player applies local/preinstalled packages without Network;
4. Online applies the same packages after validated server delivery;
5. Default, Replace, and Add Cabin scenarios have deterministic local-versus-remote parity evidence;
6. X/Y human calibration presentation can be resolved through the content system;
7. server content is server-namespaced and cannot escape the cache root;
8. every streamed file is size/hash validated;
9. executable/script server content rejects;
10. package publication is transactional;
11. warm cache avoids redundant transfer;
12. changed files can be transferred without redownloading unchanged package files;
13. corruption/interruption cannot silently publish bad content;
14. required and optional content have explicit different admission behavior;
15. fallback presentation is visible and does not alter gameplay truth;
16. UI schema cache can share the cache substrate without losing UI identity;
17. structural world content remains distinct from ordinary presentation assets;
18. dynamic actor replication remains separate from content streaming;
19. the Contentless Online build can eventually enter a remotely supplied world without bundled game content;
20. the resulting package/cache/content seams are clean enough for the planned repository split.

---

# 25. Roadmap summary

```text
CLIENT F0 shell/composition foundation
        |
        v
A0.0  content contracts / archaeology freeze
        |
A0.1  local resolver + Cabin Default/Replace/Add oracle
        |
A0.2  cache/install/atomicity/quota
        |
A0.3  remote advertisement + required cold transfer
        |
A0.4  local-vs-remote Default/Replace/Add parity matrix
        |
A0.5  warm cache + delta + corruption + optional hot upgrade
        |
A0.6  UI cache/content substrate convergence
        |
A0.7  one structural world-streaming canary
        |
A0.8  Contentless Online closure
        |
        v
CLIENT F0 final parity / repository extraction readiness
```

**Hard stop:** prove each semantic/content layer with the Cabin oracle before broadening the asset catalog or transport. The first goal is not a giant mod ecosystem; it is a correct, cacheable, layered content system whose local and remote results are demonstrably identical.