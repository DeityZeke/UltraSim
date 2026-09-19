# UltraSim Client Completion Plan

**Status:** active implementation plan for the pre-PF6 client architecture/productization campaign. **F1.0 and F1.1 are CLOSED; F1.2 is NEXT / PLANNED.**

**Parent architecture:** [CLIENT_PRESENTATION_ARCHITECTURE.md](CLIENT_PRESENTATION_ARCHITECTURE.md)

**Related plans:** [CLIENT.md](CLIENT.md), [ASSET_STREAMING.md](ASSET_STREAMING.md), [TERRAIN.md](TERRAIN.md), [CONCEPT_DEMO_PREFREEZE.md](CONCEPT_DEMO_PREFREEZE.md)

**Purpose:** finish the client as the architecture intends before external PF6 human validation. This is not a final art pass. It is the implementation campaign that turns the current vertical-slice client into a coherent, renderer-agnostic dumb terminal with unified content ownership, correct presentation fallback, correct UI/window ownership, declarative profiles, and scalable Godot rendering paths.

---

# 1. Campaign outcome

The campaign closes when the same client architecture can:

- launch with only its permanent primitive substrate;
- mount an ordinary local world/content scope and run Single Player as offline multiplayer;
- mount an authenticated remote world/content scope through the same content machinery;
- deduplicate immutable blobs across all scopes;
- keep exact semantic activation separate from presentation fallback/override resolution;
- remove Official high-quality presentation without replacing the executable;
- use compatible Local/Server/User presentation overrides without changing Game truth;
- build renderer-native derived resources such as atlases/caches without changing logical content identity;
- route World/HUD/Menus/Debug/Context UI consistently for Local and Remote play;
- manage gump/window lifetime, focus, z-order, modal behavior, and cleanup coherently;
- accept bounded declarative camera/control recommendations without server code;
- render high-cardinality presentation without requiring one Godot node per semantic entity;
- preserve detailed node-backed realization where it is actually useful;
- survive sustained developer dogfooding without exposing known architecture defects;
- enter PF6 with remaining problems classified as human-feel/polish issues or explicitly deferred features.

The campaign does **not** require Unity/Unreal adapters, comprehensive vegetation gameplay, arbitrary executable client extensions, or a universal final rendering framework. It must leave those consumers possible without forcing them into the current Concept Demo.

---

# 2. Permanent campaign rules

~~~text
SEMANTIC ENTITY != GODOT NODE.

SEMANTIC ACTIVATION != PRESENTATION RESOLUTION.

BLOB POSSESSION != REALM AUTHORIZATION.

LOCAL CONTENT IS AN ORDINARY CONTENT SCOPE, NOT A SECOND CONTENT SYSTEM.

SINGLE PLAYER IS OFFLINE MULTIPLAYER.

PRIMITIVES ARE PERMANENT RENDERER FALLBACK, NOT OPTIONAL OFFICIAL ART.

MISSING PRESENTATION MAY FALL BACK.
MISSING REQUIRED WORLD/SEMANTIC TRUTH FAILS CLOSED.

OFFICIAL ART/WORLD PACKAGES MUST NOT BE INSEPARABLY BAKED INTO THE RENDERER EXECUTABLE.

USER RESOURCE PACKS ARE PASSIVE PRESENTATION OVERRIDES.

LOGICAL CONTENT IDENTITY SURVIVES ATLASES, BATCHES, NATIVE IMPORTS, AND RENDERER SWAPS.

ATLAS/MULTIMESH/RID/COMPUTE ARE RENDERER STRATEGIES, NOT GAME SEMANTICS.

UI Z-ORDER AND INPUT/FOCUS OWNERSHIP ARE RELATED BUT SEPARATE.

CLOSED GUMPS RETIRE THEIR ACTIVE NATIVE/FUNCTIONAL OWNERSHIP.

SERVER CAMERA/CONTROL PROFILES ARE BOUNDED DATA, NEVER SCRIPTS.

CAMERA LIMITS ARE NOT A SECURITY/DISCLOSURE BOUNDARY.

HIGH-CARDINALITY PRESENTATION SHOULD PREFER FLAT DATA/BATCHES OVER SCENE-TREE MIRRORING.

GPU-LOCAL PRESENTATION STATE MAY STAY ON THE GPU WHEN GAMEPLAY DOES NOT NEED IT BACK.
~~~

---

# 3. Checkpoint naming and sequencing

This campaign continues the completed F0 reconstruction/proof line as **F1 Client Completion**.

~~~text
F1.0  Baseline and guardrails
F1.1  Unified content scopes and stores (CLOSED)
F1.2  Semantic activation vs presentation resolution (NEXT / PLANNED)
F1.3  Primitive substrate and fallback contract
F1.4  Official Local content productization
F1.5  User presentation overrides and content generations
F1.6  Derived resources / atlas pipeline
F1.7  Shared UI composition and window/gump manager
F1.8  Declarative camera/control rig profiles
F1.9  Godot scalable presentation backend
F1.10 Actor representation tiers and GPU-friendly interpolation seam
F1.11 Renderer stress consumers / vegetation prototype
F1.12 Product dogfooding and PF6 readiness
~~~

The dependency direction is intentional. Do not begin aesthetic menu polish before F1.7, and do not make the renderer depend on Local/Server special cases before F1.2.

---

# 4. F1.0 - Baseline and guardrails

## Goal

Freeze the current observable client behavior, document known mismatches, and add enough diagnostics/tests that later architectural rewrites can prove semantic equivalence.

## Implementation

Capture current Local and Remote journeys for:

- application shell;
- settings/profile resolution;
- local world entry;
- remote Direct Connect entry;
- content application proof;
- WorldPack realization;
- actor presentation;
- Inventory;
- Server Monitor;
- Chat;
- nameplates/Ctrl reveal;
- world hover;
- session retirement/reconnect;
- fallback actors;
- current render chunk/node/resource counts.

Add or preserve snapshots for:

- semantic entity counts;
- active native nodes/resources;
- UI layer/category counts;
- open UI views;
- content scope/generation identity;
- loaded blob/resource leases;
- renderer backend/batch counts;
- active actor representations;
- settings/profile signature.

Record a fresh pre-F1 Godot client baseline on representative Concept Demo journeys.

## Current source-evidence inventory

F1.0 starts from existing accepted coverage rather than inventing a second client test framework.

Current useful anchors include:

- `_Tests/UltraSim.S0.Godot.Tests/T010PlayableWorldAcceptance.cs`: native World rendering/residency, public batching, picking, controlled actor presentation, nameplate reveal, action cues, system Chat banner, and a content-backed actor canary that already proves primitive -> exact-content visual upgrade while preserving actor-local presentation state;
- `Client/Godot/Local/LocalPlayClientRoot.cs`: local composition, D0.0-D0.5/F0.2 smokes, native tooltip/layout crossing, Chat focus, and local session retirement;
- `Client/Godot/ClientHost.Remote.cs`: remote A0.8 smoke plus F0.7 mixed Local -> Remote A -> Remote B -> Remote A reconnect -> Local lifecycle retirement against an exact persistent-root boundary;
- `_Tests/UltraSim.S0.Godot.Tests/S08RemoteInventoryAcceptance.cs`: real authenticated owner-private remote Inventory/UI action path and close/reopen behavior;
- `_Tests/UltraSim.Client.Local.Tests`: packet-free local Chat, UI, Inventory, Server Monitor, world-item/equipment, and semantic delivery behavior;
- `_Tests/UltraSim.Network.Application.Tests/F03cClientProfileTests.cs`: canonical bounded code-free profile advertisement, proof gating, reserved-control preservation, and exact-realm reconnect behavior;
- `_Tests/UltraSim.Client.Content.Tests`: package/catalog/CAS/application and generation/lifetime contracts that F1.1/F1.2 must preserve.

These are the semantic/correctness controls for the F1 refactor.

## F1.0 closure evidence

F1.0 closed on tested commit `29ae5aa732f9ce38a253f4010636ca0f01093061` with the
immutable [human-readable](../../_Benchmarks/Baselines/client-f1.0-pre-rework-baseline-2026-09-19.md)
and [machine-readable](../../_Benchmarks/Baselines/client-f1.0-pre-rework-baseline-2026-09-19.json)
baselines.

The accepted Complete-product F0.7 journey now emits one coordinated snapshot across three Local
lifetimes, cold Remote A, cold Remote B over the shared CAS, warm Remote A reconnect, and all six
clean retirement boundaries. It records semantic residency, maintained native node/resource and
batch counts, actor representation/resource state, current UI ownership/focus, settings/profile
identity, content blobs/manifests/activations/live pins, acquisition reuse, and the exact persistent
root boundary. The canonical Release verifier asserts the comparison surface and still executes the
physical SinglePlayer, Online, and Complete exports plus the maintained native/content lifecycle
gates.

The archive V2 client was consulted for design-history context: its persistent layer convention and
separation of persistent cache state from live session/presentation ownership agree with the guardrail
used here. No V2 code was restored.

F1.0 does **not** require `world.local`, a user resource pack, direct RenderingServer RIDs, or the new window manager to exist yet. It records the old world well enough that F1.1+ can prove intentional change.

The accepted artifacts above are the immutable pre-rework comparison surface. Do not regenerate or
overwrite them; future F1 checkpoints must add new evidence against the same semantic journey.

## Exit gate

- full current canonical verify passes;
- Local and Remote smoke journeys are reproducible;
- known architecture mismatches are written as expected failures/TODO contracts rather than hidden;
- benchmark/diagnostic hooks exist to compare later node/RID/batch changes against the same semantic workload.

## Non-goal

No user-visible redesign yet.

---

# 5. F1.1 - Unified content scopes and stores

## Goal

Remove "Local" as a special storage/content species. Make Local and remote worlds ordinary scopes over one content-store model.

## Target model

~~~text
Permanent renderer substrate
    Primitive

Shared immutable content store
    AssetBlob / CAS

Content scopes
    world.local
    authenticated remote realm/world scopes

User presentation packs
    separately owned

Renderer-derived caches
    Godot / future Unity / future Unreal
~~~

A local scope and a remote scope use the same package, manifest, activation, catalog, blob, WorldPack, UI-definition, and presentation-definition machinery.

## Scope identity

Define an explicit typed scope identity so a hostile remote cannot impersonate local trust through a path/string.

Conceptually:

~~~text
ContentScopeKey
    Kind = Local | Realm
    Identity = world.local | authenticated ApplicationRealmId/etc.
~~~

Exact API names are implementation details.

Preserve separate concepts for:

- application/realm identity: who authorizes the session;
- world/content activation identity: what exact semantic world/content is active.

Do not permanently equate one server process with exactly one world.

## Physical roots

Support one logical content store over one or more physical roots.

Candidate:

~~~text
ReadOnlyInstallRoot
    official packages distributed beside the executable

WritableUserRoot
    downloaded blobs
    scope metadata
    settings
    derived caches
~~~

Portable mode may collapse them into one writable nearby Assets directory.

The architecture must not require the executable/install directory to be writable.

## CAS behavior

- immutable blobs keyed by canonical hash;
- blob bytes globally deduplicated within the chosen store scope;
- Local and every remote scope may reference the same physical blob;
- realm authorization remains per scope even when bytes already exist;
- deletion of one scope removes its metadata/references, not blindly shared blobs;
- eviction/GC considers persistent references plus live leases.

## Exit gate

- world.local and one remote realm both open through one scope API;
- both can reference the same blob without duplicate physical storage;
- remote scope cannot use world.local authorization;
- Local scope does not require bespoke Cabin/PresentationAssets lookup to participate;
- the current writable root remains deterministic and the typed store API does not equate a scope
  with a physical root, leaving a later read-only install-root composition unblocked;
- deleting one scope does not remove blobs still referenced by another or by live resources.

## F1.1 closure evidence

F1.1 introduced the finite engine-neutral `ContentScopeKey`: one reserved `Local` identity and
`ForRealm(ApplicationRealmId)` for authenticated remote ownership. Activation plans, resolved
catalogs, acquisition requests, cache keys, install/open/publication, live pins, reachability, and
scope retirement now use that key through one implementation. Existing Realm overloads are only
compatibility adapters.

The accepted remote layout and version-1 activation encoding remain compatible under
`realms/<ApplicationRealmId>/`; Local metadata lives at `scopes/local/`. Immutable blobs remain in
the one global CAS. The all-zero private realm field, which cannot be a valid
`ApplicationRealmId`, canonically encodes Local in activation metadata while the in-memory identity
remains explicitly typed. Remote authorization projection rejects Local plans and the Game wire,
application proof, manifest identity, and transport remain unchanged.

Permanent acceptance covers typed-identity fail-closed behavior, Local + Realm A + Realm B physical
blob deduplication, per-scope manifest isolation, equivalent logical catalog resolution, cold/warm
reopen, live-lease retirement refusal, safe metadata retirement, and the Cabin
Default/Replace/Add path through persistent `world.local`. The old Cabin remote oracle fingerprints
remain frozen; Local scope plans deliberately have their own authorization/result identity. F1.1
does not add read-only-root overlay behavior, actor-art optionality, presentation precedence, user
packs, or resource realization changes.

The immutable closure evidence is
[`client-f1.1-unified-content-scopes-2026-09-19.md`](../../_Benchmarks/Baselines/client-f1.1-unified-content-scopes-2026-09-19.md)
with a machine-readable
[`JSON companion`](../../_Benchmarks/Baselines/client-f1.1-unified-content-scopes-2026-09-19.json).

---

# 6. F1.2 - Separate semantic activation from presentation resolution

## Goal

Make the most important content boundary explicit in code: exact application/world truth is not the same thing as which compatible visual resource the renderer ultimately selects.

## Semantic activation

For a remote session:

~~~text
authenticated realm/application
    -> exact activation plan
    -> required semantic/structural packages
    -> exact accepted WorldPack/UI/schema/contracts
    -> application proof
~~~

For local play:

~~~text
world.local activation
    -> exact required semantic/structural packages
~~~

Required semantic/structural content fails closed if absent or invalid.

## Concept Demo actor-art migration

The current Concept Demo activation still marks the actor Add package and actor Replace package as `Required`. F1.2 must deliberately migrate that policy rather than relying on the renderer to catch missing art after admission.

The migration must identify:

- which actor/public-presentation descriptors are required semantic/presentation metadata;
- which GLB/model/texture payloads are optional high-quality presentation;
- where primitive-fallback bounds/kind/anchors live when optional art is absent;
- which current `Required` packages become `Optional` or move into presentation-only scope;
- how application proof continues to prove the exact authorized semantic activation without requiring optional art bytes;
- how an optional server presentation package can override `world.local` presentation when present without becoming required for world entry.

The accepted end state must permit a remote Concept Demo session to enter with its required World/UI/semantic activation valid while actor high-quality art is absent, then realize the same disclosed actors through `world.local` or Primitive presentation according to the presentation resolver.

Do not weaken content validation: an optional package that is present must still validate exactly before use.

## Presentation-generation prerequisite

F1.3 requires live primitive-to-content upgrades without semantic reset. Therefore F1.2 must establish or extend the minimum presentation-generation/lease fence needed by that acceptance before F1.3 begins.

For an entered application/world session:

~~~text
AuthorizedSemanticActivation
    fixed for this session unless a separately authorized semantic transition exists

PresentationGeneration N
    may be replaced by PresentationGeneration N+1
    under the same AuthorizedSemanticActivation
~~~

A presentation generation owns resolved compatible presentation selections and native-resource publication lineage. New presentation work publishes only if its generation is still current; old resources/blobs remain leased until no active presentation uses them.

F1.5 later generalizes this substrate for user-pack enable/disable/update. It does not retroactively make user presentation changes semantic activation changes.

## Presentation resolution

Presentation uses compatible fallback/override candidates:

~~~text
User Resource Pack
    -> Active World/Realm
    -> world.local
    -> Primitive
~~~

The presentation resolver may choose a Local actor model because a remote realm does not replace that logical presentation ID.

That does **not** make the Local package part of the remote semantic activation proof unless the remote activation explicitly declares it according to protocol.

## Domains

Classify definitions so override policy is explicit.

At minimum distinguish:

- structural/semantic world truth;
- presentation leaves;
- UI schema/action meaning;
- client profile recommendations;
- disclosure/authorization metadata.

User packs may replace compatible presentation leaves. They may not replace structural truth, disclosure, or authoritative UI action semantics.

## Fallback descriptors

Anything that may fall to primitives must expose enough permitted descriptor information to realize fallback without opening the missing asset itself:

- bounds;
- presentation kind;
- anchor hints where needed;
- material/diagnostic category;
- action timing presentation hints only where safe.

Missing animation remains presentation-only and never determines authoritative action completion.

## Exit gate

- remote semantic proof is identical whether compatible Local presentation is present or absent;
- removing a visual package can fall back without changing accepted world truth;
- removing required WorldPack/structural data prevents entry;
- user visual override changes presentation generation/derived resources without changing semantic activation identity;
- primitive fallback never causes an invalid package to be treated as successfully applied.

---

# 7. F1.3 - Primitive substrate and fallback contract

## Goal

Turn emergency fallback from scattered canaries into an intentional permanent renderer capability.

## Required primitive families

At minimum:

- actor capsule/body;
- item primitive from declared bounds;
- static primitive from structural/presentation bounds;
- Multi piece/group fallback;
- missing texture diagnostic checkerboard;
- neutral grey non-debug material;
- generic missing icon;
- bounded missing-animation cue;
- cheap interactive-world marker where a completely invisible fallback would make gameplay unusable.

## Diagnostic modes

Support a renderer-local texture fallback mode such as:

~~~text
Normal fallback
    neutral grey / simple material

Texture diagnostic
    obnoxious checkerboard/randomized squares
~~~

Missing texture association and missing UV/material realization should be visually distinguishable where possible.

## Lifecycle

Fallback should be upgradable in place when real content becomes available without changing semantic identity or resetting authority state.

## Exit gate

Run the Concept Demo/local world after removing all removable Official Presentation content.

Require:

- world entry succeeds when structural content remains installed;
- controlled actor is playable;
- other actors/items/statics/Multis remain representable;
- Inventory/Chat/interactions continue;
- missing visuals are obvious;
- reinstalling/re-enabling presentation upgrades realization without semantic reset;
- no missing presentation path throws merely because an optional model/texture is absent.

---

# 8. F1.4 - Official Local content productization

## Goal

Move high-quality Official Local content out of the renderer executable/PCK and into ordinary world.local packages.

## Migration targets

Current high-quality X/Y actor content and Concept Demo presentation should no longer depend on res://Assets being inseparably present in Single Player/Complete exports.

Package current Official assets through the same neutral content system used by remote realms.

The Concept Demo/Cabin should be expressible as ordinary installed content:

~~~text
world.local activation
    WorldPack / map
    frozen statics / Multis
    presentation packages
    UI definitions
    actor presentations
~~~

Exact package split can remain practical and evidence-driven.

## Product packaging

Published Single Player/Complete may ship Official packages beside the product or through an installer/archive, but they remain logically/removably separate from the executable.

Online may start with no Official high-quality presentation, or may optionally discover an installed world.local presentation baseline without gaining local authority.

## Exit gate

- local exported product no longer requires embedded high-quality X/Y assets;
- Official packages install separately;
- same executable works with Official presentation installed or removed;
- world.local uses the common scope/package/CAS pipeline;
- Online can reuse identical Official bytes already present in CAS where authorization permits;
- current remote package/content proof behavior remains intact.

---

# 9. F1.5 - User presentation overrides and safe content generations

## Goal

Make "append / update / override" literal and semantic while preserving immutable storage and live-session safety.

## Immutable physical semantics

~~~text
Append
    introduce/reference a previously unused blob hash

Update
    publish a new immutable blob and move a logical definition/reference to it

Reuse
    reference an existing blob hash
~~~

Never mutate the bytes behind an existing content hash.

## Semantic operations

Allow existing Add/Replace-style definition operations according to domain policy.

Examples:

~~~text
active world:
    Add actor:orc
    Replace actor:human/male
    Replace material:wood/oak
    Add multi:castle/gate

user resource pack:
    Replace presentation material:wood/oak
    Replace icon:item/sword
    Replace compatible actor presentation where policy permits
~~~

User packs do not replace authoritative collision, structural world truth, disclosure, or server UI action semantics.

## Presentation-generation publication

User-pack enable/disable/update and other presentation-only changes use transactional **presentation generations under the same authorized semantic activation**:

~~~text
AuthorizedSemanticActivation A
    remains fixed

Current PresentationGeneration N
    remains usable

prepare PresentationGeneration N+1
    acquire/reopen presentation blobs
    validate presentation packages
    resolve compatible presentation selections
    prepare required native resources

publish PresentationGeneration N+1 atomically
    reject stale async completions from N
    retain N until all presentation leases/users retire
~~~

Do **not** re-resolve or replace authoritative structural/world semantics merely because a user or optional presentation pack changed.

A future live semantic/world activation replacement, if ever required, is a separate authorized transition with its own validation, proof, ordering, and publication contract. It is not part of resource-pack generation replacement.

Uninstalling a package must not invalidate a live renderer resource underneath an active presentation generation.

## Exit gate

- one compatible user presentation override works end to end;
- enable/disable does not reset semantic session state;
- stale async load from generation N cannot overwrite N+1;
- deleting package metadata while an old generation has a live lease remains safe;
- old blobs/native resources become collectible only after final release;
- malformed/incompatible user packs fail locally without poisoning server application state.

---

# 10. F1.6 - Derived resources and atlas pipeline

## Goal

Introduce renderer-independent resource indirection and a measured Godot derived-resource pipeline, including atlas support where it materially improves current content.

## Durable abstraction

Logical presentation IDs resolve independently of the native batching strategy.

A renderer may choose:

- atlases;
- texture arrays;
- shared materials;
- combined meshes;
- MultiMesh;
- ordinary resources;
- another backend-specific strategy.

Do not make "atlas" a semantic content requirement.

## Godot atlas candidate

For current small/compatible textures, build finite compatibility groups after presentation resolution.

Candidates:

~~~text
WorldOpaque
WorldCutout
WorldTransparent
UI/Icon
~~~

Companion albedo/normal/ORM pages may share region layout.

## Derived identity

Persistent derived-cache identity and runtime publication identity have different jobs and must not be conflated.

Persistent/restart-stable cache key:

~~~text
StableResolvedPresentationFingerprint
+ renderer backend/version
+ atlas/packing policy version
+ platform texture capability
+ relevant resource-import policy
= PersistentDerivedKey
~~~

Runtime generation/session fence:

~~~text
PresentationGenerationId
    process/session-local identity
    rejects stale asynchronous work
    owns native-resource lifetime/publication order
~~~

The current `ContentCatalogGenerationId` is process-local and therefore must not be used as the persistent on-disk derived-cache identity.

A restart with the same stable resolved presentation fingerprint and renderer/import policy should reopen matching derived resources. Different resolved content or policy must not collide merely because a new process happens to reuse a similar runtime generation number.

Do not claim that changing one texture necessarily preserves every atlas region unless the chosen allocator/page policy guarantees it.

## Measurement

Select at least one representative world/clutter case and measure:

- draw calls;
- material switches;
- native resources;
- CPU build time;
- allocations;
- derived-cache size;
- cold/warm startup;
- GPU frame time;
- mip/bleed correctness;
- invalidation/rebuild cost.

## Exit gate

- logical resource resolution is independent from atlas coordinates/native handles;
- current representative content can use a shared derived atlas or another measured batch representation;
- deleting Derived/Godot cache causes safe rebuild from neutral source content;
- platform/native realization can differ without changing source/semantic identity;
- no architectural dependency requires every future texture to be atlased;
- measured evidence records where batching helps and where it does not;
- process restart with identical stable presentation fingerprint reuses matching derived cache;
- process restart with different content/policy rejects the old derived cache even if runtime generation numbering starts over.

---

# 11. F1.7 - Shared UI composition and window/gump manager

## Goal

Make the intended presentation categories real end to end and remove Local/Remote UI ownership drift.

## Category roots

Establish:

~~~text
World
HUD
Menus/Gumps
Debug
Context/Tooltip
~~~

The shell navigation surface and in-session gump host must be distinct. The existing shell MenuSurface being hidden during InSession must not hide Inventory/Server UI.

Local and Remote gameplay receive the same category/window services rather than constructing incompatible ownership trees.

## Window manager responsibilities

- semantic view -> correct category;
- multiple allowed non-modal gumps;
- within-category bring-to-front;
- deterministic click/focus ordering;
- modal policy;
- pointer capture;
- drag where permitted;
- viewport clamping;
- optional remembered local positions;
- context/tooltip children;
- exact retire/free on close;
- session replacement cleanup.

## Focus

Keep engine-neutral focus ownership separate from visual z-order.

Modal transitions/session retirement must retire:

- focus;
- pointer capture;
- tooltip/context state;
- callbacks;
- text-entry ownership;
- native view roots.

## Debug/context

Implement a finite Debug cycle and a first-class context-menu presenter.

Right-click ContextMenu must no longer be merely an overloaded OrbitCommand cancellation semantic. If Stop/Cancel remains desirable, give it an explicit semantic binding/gesture policy.

## Exit gate

- Inventory/Server Monitor live in Menus/Gumps, not HUD;
- World hover/context surfaces route correctly;
- Context/Tooltip is above lower categories but obeys interaction/modal context;
- multiple representative gumps can coexist where allowed;
- close removes native view and focus/action ownership;
- Local and Remote run the same routing/lifetime acceptance suite;
- reconnect/session retirement returns to a clean shell boundary.

---

# 12. F1.8 - Declarative camera/control rig profiles

## Goal

Expand the narrow F0.3c profile into the intended bounded data-driven client rig without server scripts.

## Camera rig vocabulary

Earn a finite schema covering the Concept Demo and intended stock families:

- projection;
- target/pivot;
- follow mode;
- follow distance/default/min/max;
- follow speed/smoothing;
- reconciliation/correction smoothing;
- yaw locked/free/range/snap/speed;
- pitch locked/free/default/range/speed;
- zoom enabled/default/range/step/speed;
- pan enabled/speed;
- optional edge-pan capability;
- FOV/orthographic size;
- stock finite occlusion/camera-collision policy where earned.

Do not encode commercial-game names into protocol semantics.

## Control profile

Keep control grammar separate from physical bindings.

Stock/user presets are ordinary named compositions over the same bounded schema.

Examples may feel UO-like, Sims-like, Kenshi-like, or Valheim-like without those names becoming wire enums.

## User ownership

Preserve:

~~~text
Built-in defaults
    ->
User global profile
    ->
accepted server recommendation
    ->
User per-server overrides
    ->
Resolved session profile
~~~

Maintain:

- Use Server Defaults;
- Keep My Controls;
- Customize for This Server;
- Ignore Recommendation.

Reserved application/accessibility controls remain user-owned.

## Protocol/security

- canonical/versioned encoding;
- bounded ranges;
- reject unknown unsupported capability;
- no arbitrary server scripts;
- camera constraints are not a disclosure/security guarantee.

## Exit gate

- at least three materially different stock profile compositions run through one schema;
- a user-created equivalent custom profile resolves through the same path;
- remote recommendation and local profile use identical Client Core representation;
- per-server override persists without rewriting global settings;
- unsupported/out-of-range profile fails closed;
- Game still sees neutral movement/action intents rather than camera/key names.

---

# 13. F1.9 - Godot scalable presentation backend

## Goal

Make "semantic entity does not require a Godot node" an explicit implemented renderer capability.

## Presentation tiers

Support three conceptual realization tiers:

~~~text
Node-backed
    unique/detailed hierarchy where native scene behavior is valuable

Flat batched / RenderingServer
    high-cardinality repeated/simple presentation

GPU-driven/procedural
    renderer-local effects/state that do not need CPU readback every frame
~~~

The semantic client does not care which tier realizes an entity.

## Immediate target classes

Best first candidates for flat/batched realization:

- frozen repeated statics;
- simple public props;
- dropped/simple items;
- primitive/fallback crowds;
- repeated clutter;
- grass/vegetation later;
- simple effects.

Do not begin with fully equipped skeletal actors.

## Flat batch model

A candidate render group owns dense arrays:

~~~text
semanticId[]
transform[]
customData[]
revision/state[]
entity -> slot lookup
activeCount
~~~

Removal may use swap-back and update the moved identity's slot mapping.

IDs/revisions remain integer semantic types. Only GPU-facing numerical data is flattened into float/native buffers.

## Direct RenderingServer

For measured high-cardinality groups, replace scene-tree-per-batch nodes with renderer-owned RIDs:

- MultiMesh RID;
- visual instance RID;
- mesh/material RIDs/refs;
- scenario assignment;
- explicit transform/AABB;
- lifecycle release.

Keep a few coarse Nodes as control/lifecycle owners.

## Bulk submission

Benchmark per-instance setters against preparing one complete native MultiMesh buffer and bulk submission.

Do not assume bulk is always faster for tiny dirty sets. Permit:

~~~text
few dirty rows -> targeted update
many dirty rows -> bulk prepared buffer
~~~

## Picking/interaction

Do not add physics nodes solely to recover semantic identity.

Use retained presentation bounds/spatial structures and slot -> semantic identity mapping to nominate disclosed targets; Game validates interaction.

## Spatial grouping

Candidate grouping:

~~~text
render region
+ compatible mesh/material/presentation family
+ detail/representation level
~~~

Render-region size is renderer policy. It may begin aligned to world chunks but is not semantic world identity.

Avoid world-wide MultiMeshes that destroy useful frustum/occlusion boundaries.

## Exit gate

Create a representative repeated-static/simple-moving-crowd benchmark.

Record:

- scene-node count;
- native resource count;
- presentation-prep time;
- managed allocations;
- native calls;
- buffer-upload time;
- GPU time;
- enter/retire churn;
- culling behavior.

Require semantic/picking equivalence with the existing path.

Select direct RID/bulk realization only where evidence justifies it.

---

# 14. F1.10 - Actor representation tiers and interpolation seam

## Goal

Preserve detailed actors while allowing the same semantic actor to use cheaper representations at scale.

## Detailed tier

Keep node-backed/skeletal actor realization for current near/important characters when it earns:

- independent skeletal animation;
- equipment attachments;
- sockets;
- multiple materials;
- interaction cues;
- nameplate/effects anchors.

The current actor presenter remains a valid detailed backend rather than a universal actor representation.

## Cheap tiers

Add an architecture seam for:

- primitive batched actor;
- simplified compatible mesh instance;
- distant/impostor actor;
- future shader/texture-driven animation;
- future GPU presentation interpolation.

A semantic actor can move between representations without changing:

- WorldPublicEntityId;
- public/cold presentation state;
- action state;
- interaction target;
- name/display metadata;
- server proof/revisions.

## Interpolation seam

A future GPU-friendly path may upload endpoint state:

~~~text
previous authoritative pose
current authoritative pose
timestamps
orientation/action/presentation flags
~~~

and let the GPU compute per-frame visual interpolation.

CPU retains authoritative semantic snapshots; the renderer need not read interpolated positions back unless a concrete client feature needs them.

## Equipment

Do not force incompatible meshes into one MultiMesh merely because they share an atlas.

Equipment may require:

- detailed node tier;
- separate compatible batch;
- baked/combined compatible representation;
- future texture/vertex-animation representation.

## Exit gate

- current detailed X/Y actors retain functional parity;
- primitive crowd representation can be batched without node-per-actor;
- one actor can transition representation without semantic identity reset/ghost duplication;
- Ctrl/nameplate policy continues over retained authorized metadata;
- interaction/picking remains correct;
- no claim of universal skeletal batching is made without a concrete implementation.

---

# 15. F1.11 - Renderer stress consumers and vegetation prototype

## Goal

Use concrete high-cardinality consumers to validate the renderer tiers, without making all future features PF6 blockers.

## Required renderer stress scene

At minimum include:

- repeated statics/clutter;
- moving primitive crowd;
- chunk enter/retire;
- dense nameplate reveal;
- multiple draw distances;
- cold/warm derived resources.

Measure both existing node-backed and selected batch/RID paths.

## Optional vegetation prototype

Vegetation is an excellent GPU/procedural validation consumer.

Candidate:

~~~text
one spatial grass batch per render chunk/subregion
one instance per vegetation cell/small patch
shared procedural source mesh
VERTEX_ID -> blade/segment/side
INSTANCE_CUSTOM -> cut/seed/type/density
shader -> height/yaw/lean/bend/wind
cut -> collapse affected blade/patch vertices
~~~

Keep authoritative vegetation state separate from renderer representation.

Collapsed geometry still costs vertex work, so measure:

- visible vertices;
- shadow pass;
- overdraw;
- wind shader cost;
- cut-state update;
- fully cut patch compaction only if needed.

Vegetation implementation is not a PF6 gate unless explicitly selected for the demo.

## Compute/GPU backend

Compute remains optional.

Use it only for presentation-local state where avoiding CPU readback is useful.

Never move authoritative gameplay/navigation/collision into the client renderer merely because compute scales well.

## Exit gate

- high-cardinality renderer path has measured evidence on representative desktop hardware;
- lower-spec/integrated/Steam-Deck-class evidence is gathered where available before making product defaults;
- no renderer optimization changes semantic authority;
- vegetation/compute remain optional capability seams if not selected.

---

# 16. F1.12 - Product dogfooding and PF6 readiness

## Goal

Stop using synthetic acceptance alone and aggressively exercise the now-intentional client before asking external humans to judge it.

## Dogfooding matrix

Run at least:

~~~text
Single Player / world.local
    Official presentation installed
    primitive-only presentation

Complete
    local -> disconnect -> remote -> disconnect -> local

Online/remote
    cold content cache
    warm content cache
    reused blobs from another scope
    reconnect same realm
    connect second realm

Presentation
    user override enabled/disabled
    missing optional art
    derived cache deleted/rebuilt
    multiple gumps
    context menu/tooltips
    Debug cycle
    dense actors/nameplates
    camera/control profile changes

Settings
    global
    per-server
    UI scale
    render distance
    controls
    profile acceptance/customization

Performance
    normal field
    dense statics
    primitive crowd
    low/high render distance
    selected cadence/bandwidth profiles
~~~

## Finding classification

Every issue is classified as one of:

1. architecture/mechanical defect;
2. missing product behavior;
3. presentation polish;
4. intentionally deferred feature.

Categories 1 and materially important 2 block PF6.

Pure art polish does not automatically block PF6 once the underlying product behavior is intentional and usable.

## PF6 readiness gate

PF6 begins only when:

- no known Local/Remote ownership mismatch remains;
- no Official high-quality art is inseparably required by the renderer binary;
- primitive-only presentation is supported;
- semantic activation/presentation resolution separation is proven;
- common content scopes/CAS behavior is proven;
- UI/gump category/lifetime/focus behavior is coherent;
- declarative camera/control profiles are stable enough for the intended playtest;
- current renderer has an intentional high-cardinality strategy rather than accidental node mirroring;
- selected native batching path has measured evidence;
- reconnect/session retirement is clean;
- remaining issues are human-feel/polish or explicitly deferred.

---

# 17. PF6 - External human validation

Once F1.12 closes, resume the existing PF6 campaign.

External testers should focus on things automation cannot establish well:

- perceived movement responsiveness;
- interpolation/reconciliation quality;
- camera feel;
- control grammar coherence;
- UI/gump usability;
- focus behavior;
- Chat feel;
- crowd readability;
- nameplate behavior;
- animation/state presentation;
- content acquisition experience;
- cold/warm reconnect experience;
- whether 20/30 Hz or lower-bandwidth profiles are perceptibly acceptable;
- primitive fallback comprehensibility when deliberately tested.

Human validation should not be used to discover known architecture defects that F1 is intended to eliminate.

After PF6, fix evidence-backed human discrepancies, then proceed to exact-tree/export closure and PF7 Concept Demo freeze.

---

# 18. Cross-checkpoint verification suites

The following suites should persist across the entire F1 campaign.

## Semantic invariance

For a fixed authority/input journey, changing renderer realization must not change:

- authoritative commands;
- semantic entity identity;
- Inventory ownership;
- Chat occurrence semantics;
- UI action requests;
- content authorization;
- application proof;
- Game outcomes.

## Content lifetime

Repeated:

~~~text
install
activate
open
replace generation
disable override
disconnect
reconnect
uninstall
evict
reopen
~~~

must not leak native resources, stale catalogs, blob leases, callbacks, or scope authorization.

## Presentation fallback

For every optional presentation class:

~~~text
User
-> Active World
-> world.local
-> Primitive
~~~

must either select a compatible realization or fail explicitly if no safe presentation descriptor exists.

## Renderer lifecycle

Chunk/entity enter/retire/re-enter must preserve:

- no duplicate visual identity;
- no stale slot mapping;
- bounded active resources;
- correct AABB/culling;
- correct picking;
- clean release.

## UI lifecycle

Open/close/reopen/session-retire must preserve:

- no stale focus;
- no hidden bound gump;
- no stale pointer capture;
- no stale tooltip/context child;
- no action callback after retirement.

---

# 19. Measurement requirements

Do not close renderer/productization checkpoints using only correctness assertions.

Track at least:

~~~text
CPU
    client frame/update time
    presentation preparation
    content/derived build
    native call cost
    allocations/GC

GPU
    frame time
    draw calls
    material/texture bindings
    vertices/triangles where practical
    shadow contribution
    overdraw-sensitive cases

Native/client resources
    Godot node count
    RenderingServer/RID count
    MultiMesh count
    material/texture count
    derived cache bytes
    retained blob bytes/leases

Lifecycle
    enter/retire churn
    reconnect
    content generation replacement
    UI open/close churn
~~~

The goal is not minimum node count at any cost. The goal is the simplest representation that meets correctness, scale, and maintainability targets.

---

# 20. What may remain deliberately future after F1

The following do not block PF6 unless a current demo need explicitly selects them:

- Unity or Unreal adapters;
- fully GPU-driven actor simulation;
- universal skeletal crowd batching;
- full BotW-style vegetation gameplay/regrowth;
- arbitrary resource-pack authoring UI;
- executable client extensions;
- production audio streaming/override system;
- comprehensive occlusion system;
- advanced actor impostor generation;
- universal atlas compiler for every texture class;
- world-scale compute simulation;
- renderer-general admin tooling.

F1 must preserve their architectural seams without prebuilding all of them.

---

# 21. Definition of success

The client is "where it is supposed to be" for the Concept Demo when the architecture can be described honestly as:

~~~text
UltraSim client
    engine-neutral semantic retained state
    + engine-neutral content/package/scope model
    + exact authority/application proof
    + local user settings/profiles
    + renderer-independent presentation identities

Godot adapter
    primitive permanent substrate
    + ordinary native detailed presentation
    + flat batched/RID presentation
    + optional procedural/GPU presentation
    + UI/window realization
    + native derived resources

world.local
    ordinary content scope
    same machinery as remote worlds

remote world
    authenticated scope
    exact semantic activation
    optional presentation replacements

user resource pack
    compatible passive presentation overlay

AssetBlob/CAS
    shared immutable physical bytes

Derived/Godot
    disposable native realization/cache
~~~

At that point Godot is genuinely a renderer/presentation adapter with hooks into an engine-neutral client rather than the place where game/content semantics accidentally live.

That is the client baseline PF6 should evaluate.
