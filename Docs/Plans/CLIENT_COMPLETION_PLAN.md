# UltraSim Client Completion Plan

**Status:** **F1.0–F1.12 CLOSED / Gate 0 CLOSED. P1 Framework Control Plane CLOSED; P2 Runtime Host and C# Server Authoring Environment CLOSED; P3 adapter/runtime boundary ACTIVE; P4–P11 and Gate 1 PLANNED. PF6 PLANNED / NOT READY / NOT STARTED; many-player live testing NOT READY.** The [P1 closure companion](../../_Benchmarks/Baselines/framework-p1-native-lifetime-closure-2026-09-28.md) records the merged native-lifetime repair, 50/50 warm Official-content exits, and uninterrupted canonical Release on `c106a7b6ec9d9b4b825c0a8b507147371f3d4488`. The [original P1 evidence](../../_Benchmarks/Baselines/framework-p1-control-plane-2026-09-27.md) preserves the earlier native failure. The [R3 closure evidence](../../_Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r3.md), committed at `d9234948989ac9a272ab1905a388b58210cd6403`, records the supported Framework HTTP Endpoint/Kestrel replacement and uninterrupted canonical Release on integrated production candidate `cc937185ce630fdb707e51bdee6372f550ff77b0`. No W8/W9/W10 or PF6 work has started.

P2 has a server-owned, opt-in compiled `Scripts/` path, exact compile cache identity, a real Contracts-only/Core-optional proof, explicit RuntimeHost/Control Plane diagnostics, and Package/Runtime Service/Object Extension terminology. The [authoring foundation](../../_Benchmarks/Baselines/framework-p2-server-authoring-and-extension-terminology-2026-09-29.md) passed canonical Release on `98586fbe`; the [P2 closure companion](../../_Benchmarks/Baselines/framework-p2-closure-2026-09-29.md) records package aliases, deterministic remap order, durable-schema load admission, the initial transient canonical build failure, and the uninterrupted successful rerun on `e6b6f7cd`. P2 is **CLOSED**. DefinitionId/facet migration and first-party Game-definition dogfooding belong to P4, polished surfaces to P5, and the Server Operator Product to P7. P3 is next, not started; PF6 is not ready.

The [partial F1.12 evidence](../../_Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.md) and [R1 assurance evidence](../../_Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r1.md) remain immutable. Bounded native Cabin capture, pointer/tier/nameplate controls, retired-memory high-water reuse and the device-owned Advanced vegetation preference remain accepted. The historical obsolete capture-stall trigger remains unisolated, not a proven engine defect or a new blocker. The [F1.11 evidence](../../_Benchmarks/Baselines/client-f1.11-renderer-stress-vegetation-2026-09-25.md) closes shared Local/Remote world-resource consumption and bounded renderer stress; vegetation remains opt-in/default Off. F1.10 retains the conservative Detailed/BatchedPrimitive hybrid and F1.9B retains NodeMultiMesh/PerRow. The [F1.8A corrective evidence](../../_Benchmarks/Baselines/client-f1.8a-r1-motion-correction-2026-09-23.md) supersedes its initial closure claim without erasing the earlier failures.

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
F1.0  Baseline and guardrails (CLOSED)
F1.1  Unified content scopes and stores (CLOSED)
F1.2  Semantic activation vs presentation resolution (CLOSED)
F1.3  Primitive substrate and fallback contract (CLOSED)
F1.4  Official Local content productization (CLOSED)
F1.5  User presentation overrides and content generations (CLOSED)
F1.6  Derived resources / atlas pipeline (CLOSED)
F1.7  Application workspace, Gameplay Frame, shared UI/gump composition (CLOSED)
F1.8  Declarative camera/control rig and canonical gameplay-view crop (CLOSED)
F1.8A Remote motion prediction/reconciliation/interpolation (CLOSED after R1 correction)
F1.9A Simple/Advanced display composition and real Gameplay Frame output (CLOSED)
F1.9B scalable Godot realization / RID / MultiMesh (CLOSED; NodeMultiMesh/PerRow retained)
F1.10 Actor representation tiers and GPU-friendly interpolation seam (CLOSED; conservative hybrid)
F1.11 Renderer stress consumers / vegetation prototype (CLOSED; prototype default Off)
F1.12 Product dogfooding and PF6 readiness (CLOSED after R3; Gate 0 CLOSED)
P1    Framework Control Plane (CLOSED after native-lifetime acceptance)
P2    Runtime Host + C# Server Authoring Environment (CLOSED)
P3–P11 / Gate 1 (PLANNED; productization and identity/character-mode gate)
PF6   External human validation (PLANNED / NOT READY / NOT STARTED)
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

The pre-F1.2 Concept Demo activation marked actor Add and Replace packages as required. F1.2
migrated them into a distinct Realm presentation authorization; the semantic activation retains
the exact WorldPack and compiled Inventory/Server Monitor UI bootstrap package. The separate
message-149 plan has its own plan/result identity and never participates in application proof.

The migration must identify:

- which actor/public-presentation descriptors are required semantic/presentation metadata;
- which GLB/model/texture payloads are optional high-quality presentation;
- where primitive-fallback bounds/kind/anchors live when optional art is absent;
- which current `Required` packages become `Optional` or move into presentation-only scope;
- how application proof continues to prove the exact authorized semantic activation without requiring optional art bytes;
- how an optional server presentation package can override `world.local` presentation when present without becoming required for world entry.

The accepted end state must permit a remote Concept Demo session to enter with its required World/UI/semantic activation valid while actor high-quality art is absent, then realize the same disclosed actors through `world.local` or Primitive presentation according to the presentation resolver.

The F1.2 acceptance exercises that split with actor acquisition blocked through admission,
followed by Realm publication under a new presentation generation. Persistent `world.local`
actor-package acceptance independently proves Realm → Local → Primitive resolution and lease
retention. This is not F1.4 Official Local packaging; shipped local X/Y assets remain in place.

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

F1.5 generalizes this substrate for user-pack enable/disable/update. It does not retroactively make user presentation changes semantic activation changes.

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

## F1.2 closure evidence

F1.2 closed on implementation commits `682b5f7b` and `d61fea95`, with the immutable
2026-09-22 baseline under `_Benchmarks/Baselines/`. The accepted remote semantic activation
contains only the required WorldPack and compiled Inventory/Server Monitor UI bootstrap.
Authenticated Game message 149 advertises the separate, non-gating Realm actor presentation
plan. The latter retains the existing Add-Y/Replace-X order internally; its GLBs do not enter
the semantic plan hash or application proof.

The bounded engine-neutral generation resolves the current X/Y logical actor IDs through
Realm, then `world.local`, then Primitive. Local and Realm content still use the F1.1 store;
Primitive remains outside it. Native visual replacement preserves the actor root and retained
semantic presentation state. The full Release/export and F0.7 mixed lifecycle gates passed on
the tested implementation. Shipped Local X/Y art remains packaged as before: Official Local
asset productization, user packs, and broad primitive contracts are F1.4/F1.5/F1.3 work,
respectively, not F1.2 closure claims.

---

# 7. F1.3 - Primitive substrate and fallback contract

## Goal

Turn emergency fallback from scattered canaries into an intentional permanent renderer capability.

**Implemented boundary:** `ClientPrimitivePresentation` provides finite engine-neutral
kind/shape/local-mm bounds for already-disclosed actor, ground-item, public-object, frozen-static,
Multi-fragment, and marker facts. Godot realizes these through `GodotPrimitiveSubstrate`; the
same stock public bounds drive pointer nomination while Game retains final interaction/range
authority. Frozen/Multi fallback uses each exact disclosed structural AABB. Neutral primitive
material and conspicuous missing-texture material remain separate. The substrate is renderer
code, never an activation package or application proof.

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

Run the Concept Demo/local world with optional presentation resolvers deliberately suppressed.
Physical removal from the shipped Local product is F1.4, not an F1.3 condition.

Require:

- world entry succeeds when structural content remains installed;
- controlled actor is playable;
- other actors/items/statics/Multis remain representable;
- Inventory/Chat/interactions continue;
- missing visuals are obvious;
- the existing actor primitive→content hot-upgrade retains root/semantic identity; current
  static/Multi content replacement may rebuild native batches without changing world truth;
- no missing presentation path throws merely because an optional model/texture is absent.

The maintained `--f1-3-primitive-smoke` covers terrain, actor/item/public/static/Multi fallback,
selection, pickup/equip/drop/re-pickup, door/chest/telepad, Inventory, Server Monitor, Chat,
nameplates, overhead speech, Primary cue, and clean retirement. F1.2's exact semantic
activation/proof is unchanged. See the immutable F1.3 baseline for the tested tree and counts.

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

## Closure

F1.4 packages the exact X/Y GLBs into one deterministic, compiled-identity Official archive. The
Single Player and Complete exporters place it beside the executable under `Content/Official`; all
Godot presets exclude the former `Assets/**` mount, and server/client managed assemblies no longer
embed the actor bytes. Local and optionally Online acquire the archive through
`LocalContentPackageSource` into the shared CAS and publish the exact `world.local` activation.
Installed, absent, warm-reopen, corrupt/mismatched, read-only-source, Realm-precedence, CAS reuse,
binary-identity, and clean-retirement controls are permanent verification gates. Online continues
to ship with no package, and authenticated Realm selection/proof remains independent and dominant.

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

## Closure

F1.5 activates numeric trust class 3 only through a dedicated ActorPresentation/Replace/GLB
policy. Exact `PackageId + ManifestHash` identities install from an explicitly rooted local archive
source into a separate `resource-packs/` metadata namespace while reusing the global immutable CAS.
The strict persisted enabled list is bounded to sixteen entries and ordered low-to-high priority;
invalid selection metadata enables nothing.

`PresentationGeneration` now composes User -> Realm -> `world.local` -> Primitive. Candidate
generations open and pin exact pack metadata/blobs, validate the existing humanoid profile, and
publish only after complete resolution. Invalid candidates leave the current generation active;
old generations and native actor resources retain their leases. Enable, disable, reorder, exact
immutable update, warm reopen, live-retirement refusal, missing-target no-op, Official-absent
User/Primitive fallback, remote semantic-proof invariance, and native actor-root-preserving hot
replacement are permanent tests. Existing remote acquisition revision/session fences still reject
late work; user selection itself introduces no asynchronous loader.

Texture override remains deliberately deferred. Current Cabin materials are constructed directly
by the world renderer and are not resolved through the common presentation generation; routing
them now would begin F1.6 derived-resource/world-renderer work. F1.5 does not claim texture-pack
support and does not change protocol, Game manifest, transport revision, semantic activation, or
application proof.

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

## Closure

F1.6 generalizes the immutable presentation generation only through the earned Texture and
Material leaves. Logical material scalar truth remains Local/Realm-owned while its logical albedo
reference resolves independently through User -> Realm -> `world.local`. The dedicated user-pack
factory now admits bounded PNG/WebP Texture Replace beside the existing ActorPresentation Replace;
Material, Icon, UI, World, shader, script, executable, and Add entries remain rejected.

The Local Cabin wall/wood consumer now installs its presentation package into ordinary
`world.local`, resolves logical resources through `PresentationGeneration`, and realizes them
through a Godot-only derived store. Restart-stable keys cover the exact effective resource result,
Godot backend/format, policy, platform profile, import policy, and finite atlas family; process-local
generation IDs never enter disk identity. Versioned bounded RGBA artifacts live under writable
`Derived/Godot`, reject reparse paths/corruption/wrong key/wrong version, and rebuild from still-
authorized CAS bytes. Native leases keep generation/blob/resource ownership alive through atomic
candidate publication.

The measured two-texture `WorldOpaque` candidate produced one pixel-exact page, but the current
Cabin geometry still needs separate materials/draws and exposes no reliable draw-call reduction.
Production therefore remains standalone derived Texture/Material resources; the atlas candidate is
retained only as bounded acceptance evidence, not as semantic architecture.

**Separate integration follow-up:** the Local Cabin has a catalog-backed world Texture/Material
consumer, while Remote still creates its world renderer without that asset resolver and acquires
presentation resources chiefly for actors. A bounded F1.6-R1 Remote world-resource consumer is
needed before a Local-versus-Remote material/renderer parity benchmark can claim matched inputs.
This gap does not change F1.8A motion ownership and was not implemented in its correction.

---

# 11. F1.7 - Shared UI composition and window/gump manager

## Goal

Make the intended presentation categories real end to end and remove Local/Remote UI ownership drift. Own an application workspace with a Gameplay Frame below normal UI; the 3D output no longer implicitly owns the OS window.

The Application Window is the physical OS/Godot window. Gameplay Output Resolution selects the render-target pixels and aspect ratio, while the Gameplay Frame is the application-space rectangle displaying that output. The frame defaults to the maximum aspect-preserving fit, may later snap to bounded sizes, cannot exceed the window, and leaves unused space as dark UI workspace. Scaling the same output into a larger frame changes fidelity/size, not world-space visibility. The frame has no internal letterbox merely to preserve 16:9. World-relative overlays remain associated with it; ordinary gumps may occupy the frame or surrounding workspace. F1.7 owns the host and composition, not the later projection or renderer-target implementation.

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

## Closure

F1.7 establishes one persistent Godot application workspace for every edition. A finite selected
Gameplay Output shape (1920×1080 by default) produces the maximum centered aspect-preserving
application-space Gameplay Frame. Four dark workspace masks cover unused space, the clipped World
overlay host follows the frame, and world pointer admission rejects the unused workspace. This is
deliberately a main-viewport transition: F1.8 still owns canonical camera crop/projection and F1.9
still owns an independent render target/SubViewport.

The persistent category order is Workspace, World, HUD, Menus, Debug, Context/Tooltip. Local and
Remote sessions lease the same category hosts. Shell menus and gameplay gumps are distinct; the
bounded gameplay manager supports simultaneous Inventory and Server Monitor views, deterministic
cascade and bring-to-front order, native drag/application clamping, exact close/free, and session
retirement. Tooltips and a finite disclosed-target Select/Interact/Cancel menu live in Context.
Right-click no longer submits Orbit stop; Escape and shell/modal input loss remain explicit stop
paths. Shell modals suppress transient Context without destroying modeless gameplay gumps.

F11 cycles the finite client/presentation/content Debug pages back to Off without hidden page
debt. Engine-neutral layout tests, native workspace/gump acceptance, the real Local F1.7 journey,
the Remote Inventory/Server Monitor journey, and the mixed Local/Remote retirement guardrail freeze
the behavior. The immutable closure evidence is
`_Benchmarks/Baselines/client-f1.7-workspace-ui-gumps-2026-09-22.*`.

---

# 12. F1.8 - Declarative camera/control rig profiles

## Goal

Expand the narrow F0.3c profile into the intended bounded data-driven client rig without server scripts.

Define 16:9 as the maximum stock-client gameplay-view envelope. At any selected Gameplay Output Resolution/aspect ratio, show the largest centered rectangle of that aspect inside the canonical 16:9 view: 16:9 receives the full envelope; 4:3 and 5:4 crop horizontally with the same vertical extent; wider aspects retain canonical width and crop vertically. Pixel resolution alone never expands visibility. This stock-client consistency rule is not a security boundary; server AOI/disclosure cannot trust camera crop. F1.8 owns frame-aware projection and crop, distinct from world render/residency distance.

## Camera rig vocabulary

F1.8 implements one fixed-unit `ClientCameraRig` for the three stock profiles and bounded
user-named compositions. It supports perspective/orthographic projection, controlled-actor target,
immediate follow, locked/free yaw and pitch, bounded distance/pitch, stepped or disabled zoom,
canonical lens, and pointer look speed. The following broader vocabulary remains design direction
only; it is not accepted from the current remote profile wire:

- projection;
- target/pivot;
- follow mode;
- follow distance/default/min/max;
- follow speed/smoothing;
- camera follow smoothing; remote motion reconciliation/correction belongs to F1.8A;
- yaw locked/free/range/snap/speed;
- pitch locked/free/default/range/speed;
- zoom enabled/default/range/step/speed;
- pan enabled/speed;
- optional edge-pan capability;
- FOV/orthographic size;
- stock finite occlusion/camera-collision policy where earned.

The F1.8 Godot realization is shared by Local and Remote. CameraLook/ZoomIn/ZoomOut are client-local
semantic bindings. Settings v4 migrates old camera labels/lens values; the active camera is the rig,
while the old enum is only a stock-preset/migration label. Game manifest v32 carries and hashes the
canonical fixed-unit rig; Network transport revision remains 2. The selected output aspect crops
the active 16:9 canonical envelope, while its pixel count is not yet an independent render target.
Until F1.9, Camera3D compensates for the masked Gameplay Frame using actual application/frame height.
The immutable closure evidence is
`_Benchmarks/Baselines/client-f1.8-camera-rig-view-envelope-2026-09-23.*`.

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

# 12A. F1.8A - Remote motion presentation

This dedicated checkpoint occurs after F1.8 and before F1.9. It does not move authority into the client. For the controlled remote actor, correlate bounded local command/prediction history with authoritative owner state, detect error, reconcile with bounded smoothing below a discontinuity threshold, and snap/reset on teleport, relocation, or session/generation change. Prediction is presentation only, never Game truth. Record correction magnitude and error diagnostics.

For non-controlled remote actors, retain a bounded authoritative snapshot buffer and interpolate at a presentation timestamp/delay. Define gap, stale-buffer, teleport, entity replacement, and generation-reset behavior before considering extrapolation. Local packet-free play need not manufacture network prediction or reconciliation debt, though it may reuse presentation smoothing primitives.

F1.8A answers which pose should be presented now. F1.10 answers how that pose is efficiently realized, including any GPU actor interpolation. PF6 may not claim prediction/reconciliation/interpolation quality until F1.8A is implemented and tested under real latency/impairment.

F1.8A now implements an engine-neutral global-millimeter `ClientMotionPose`. Remote input is
predicted only after the existing focus/profile/scheduler/`RemotePlayableClient` path admits the
semantic command. A fixed 128-entry sequence history and exact owner acknowledgement fence a
client-local horizontal lead (stock presentation policy 6 m/s, at most 900 mm); Game collision,
vertical motion, landing, interaction, and AOI remain authoritative. Ordinary correction has a
20 mm deadband and 180 ms finite decay; a >4 m discontinuity snaps. The controlled actor and
camera consume the presented pose while exact owner and semantic positions remain unchanged.
Non-controlled actors use 12 receipt-time authoritative samples per bounded actor, a 100 ms
delay, no extrapolation, and explicit gap/teleport/identity resets. The 12-sample ring is required:
four samples dropped the bracket needed for a 100 ms delay at near-60 Hz publication. Native
acceptance, real authenticated UDP delay/jitter/loss controls, full Release verification, and
the immutable [F1.8A evidence](../../_Benchmarks/Baselines/client-f1.8a-remote-motion-presentation-2026-09-23.md)
established the initial implementation. The [F1.8A-R1 correction](../../_Benchmarks/Baselines/client-f1.8a-r1-motion-correction-2026-09-23.md)
reproduced and repaired nonconvergent idle/blocked controlled poses, vertical teleport
interpolation, stale authoritative picking geometry, and accepted orthographic zoom/profile-pivot
settings that did not control the realized camera. The corrected production commit passed an
uninterrupted canonical Release run. Earlier full runs on the original evidence-only tree had
intermittent PF1/PF2 exported-swarm failures; their root cause was not established and they are
not erased by the later pass. Under sparse impaired public samples the policy holds the newest truth;
it does not claim smooth motion without two usable samples.

---

# 13. F1.9A display composition, then F1.9B scalable realization

F1.9A owns the physical-window-independent logical Application Resolution, real Gameplay Frame
SubViewport, Gameplay Output Resolution, and separately scaled Internal 3D Render Resolution.
Simple and Advanced are authoring modes over one resolved native pipeline, not two renderers.
Simple uses finite exact-aspect presets and locks Application, Frame, Output, and Internal 3D to
the selected resolution at 100% scale. Advanced allows bounded custom application dimensions,
Fill/Max Fit/custom frame, aspect-matched output, and bounded internal scale. The physical window
aspect-fits the logical application without rewriting settings or disclosing more World. Display
settings are user-global, never server-profile overrides. F1.9A keeps the canonical 16:9 World
envelope, Core-owned camera crop, Local/Remote semantic parity, and exact application proof.

F1.9B begins only after F1.9A closes. It owns the measured node/RID/MultiMesh scalable backend
and high-cardinality presentation below the same display composition.

## F1.9B goal

Make "semantic entity does not require a Godot node" an explicit implemented renderer capability. Godot rendering targets the F1.7 Gameplay Frame/SubViewport rather than implicitly owning the OS window.

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

## Closure — measured backend selection

The [F1.9B evidence](../../_Benchmarks/Baselines/client-f1.9b-scalable-godot-backend-2026-09-25.md)
compares the maintained chunk-local NodeMultiMesh backend with direct RenderingServer instances,
and per-row with bounded bulk submission, at 100/1,000/5,000/10,000 realized records. A concrete
native group owner holds the MultiMesh/visual lifetime below existing grouping; neither native
slots nor RIDs become semantic identities. Direct groups explicitly bind the Gameplay Output
scenario and synchronize global transform/visibility; scenario replacement and high/low rebase
have real-native coverage. Public refresh remains a whole-layer rebuild, not an incremental slot
map. Terrain, detailed actors, equipment and nameplates remain on their existing paths.

**Outcome B:** retain NodeMultiMesh/PerRow. Direct reduces actual renderer Nodes from 91 to 37
for the 54-group fixture, but does not reduce the native group count or consistently improve
realization/refresh CPU; it increases idle/rebase service and ownership complexity. No bulk
crossover was measured through 1,024 rows, so no threshold is selected. All four bounded controls
remain available to acceptance tests, with no user-facing toggle. Warmed empty-frame renderer
service allocates zero managed bytes in all 48 comparison rows; that is not a whole-client claim.

The uninterrupted canonical Release run passed on `be36ff502081b560bad9e0b2257bbc83a006540b`,
including exports, Local/Remote parity, W7 seam/corner controls and mixed lifecycle retirement.
The report preserves an earlier debug-hook compilation failure and an intermittent unchanged
F1.8A loss-case assertion before the final pass. Headless tests certify lifecycle but cannot
certify native transforms/culling; the real OpenGL matrix covers those separately. Cold build
residuals and short GPU diagnostics remain explicit limitations, not claims that rendering is
already minimal. The Remote catalog world-material consumer gap remained at F1.9B and is closed
by F1.11 below. F1.10 follows below; W8 is unstarted.
At that checkpoint PF6 awaited F1.12; the current post-R3 sequencing is recorded above.

---

# 14. F1.10 - Actor representation tiers and interpolation seam

## Closure — conservative hybrid

The [F1.10 evidence](../../_Benchmarks/Baselines/client-f1.10-actor-representation-tiers-2026-09-25.md)
records an uninterrupted canonical Release pass on `cbf9175c63a7f7ab843363782a2c80c018b88cbd`
and three matched real-native repetitions at 16/64/128/256 actors. **Outcome C** retains Detailed
near/important actors and batches distant primitives. The soft Detailed budget is 64, promotion
is at 20 m, demotion beyond 28 m; mandatory controlled/equipment/active cue/overhead features may
expand that budget within the unchanged 256-actor presentation capacity. Reveal labels remain
independent of body tier. Product participant admission remains 64.

At 256 actors the far fixture reduces body Nodes 2,560→23, cold realization 58.081→1.884 ms,
and synchronous retirement 11.656→0.321 ms, but moving owner-thread mean increases
193.017→400.758 µs. This is a bounded resource/lifecycle tradeoff, not a universal CPU/FPS win.
Both warmed idle/moving loops allocate zero managed bytes. Cheap groups use real
MultiMeshInstance3D/PerRow, dense generational mappings and moving/rebased bounds; no body Node
per cheap actor. Detailed promotion uses current resolved content, retains the CPU pose and action
sequence, and commits only after hidden preparation succeeds. Native-frame, content-generation,
picking, feature pins, failure cleanup, slot reuse and mixed retirement have permanent coverage.

`ClientActorTemporalPresentation` exposes the actual F1.8A receipt-time bracket plus its CPU-resolved
pose. Remote consumes that result in either tier. Controlled prediction, interpolation policy,
content precedence and protocol/settings/World formats are unchanged. GPU interpolation, skeletal
batching and impostors remain future work. F1.11 is closed below; W8 is not started.
The current post-R3 PF6 disposition is recorded above, rather than the historical F1.12 dependency alone.

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

## Closure

[Immutable evidence](../../_Benchmarks/Baselines/client-f1.11-renderer-stress-vegetation-2026-09-25.md)
records implementation `1ac8e614`, W7 accounting assurance/tested tree `b59dd382`, native Compatibility
measurements and an uninterrupted canonical Release/export pass. One common catalog-backed
world palette now serves Local and Remote; required structural activation is unchanged. The
optional presentation activation advances to suffix 2 rather than retargeting old metadata.
User material refresh preserves compatible meshes/groups, semantic truth and actor lifetimes.

The maintained production-consumer scene exercises 1,000/5,000/10,000 statics/Multi/public objects,
64/128/256 actors, dense nameplates, radius 1/2/3 and three pixel configurations. A chunk-owned,
base-only procedural grass prototype reaches 50,175 patches; actual slope/void/culling/rebase,
cut and retirement controls pass. No per-blade entity/Node/process loop or World format change.
Selected NodeMultiMesh/PerRow and conservative actor tiers remain unchanged. Short GPU windows
and materially variable CPU runs are diagnostics, not FPS/CCU ratings. Grass remains default Off;
lower-spec/Deck evidence, advanced GI, final painted art and authoritative vegetation gameplay
remain unimplemented. At F1.11 closure F1.12 was next and unstarted; it is now closed after R3 below.
W8/W9/W10 remain unstarted.

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

## Selected Cabin visual benchmark consumer

The Cabin in the Woods is selected as the first real look-and-feel consumer for the intentional client. The owning direction is [Visual Art / Lighting / Cabin Benchmark](../../AIOS/canonical/visual-art-lighting-and-cabin-benchmark.md).

The benchmark should use ordinary world.local/Official presentation rather than renderer-baked art and should exercise, incrementally:

- tall pine(s), rocks, grass/ground, log cabin surfaces, real doorway/door, and window art;
- a two-room interior where the main room has the exterior door and no windows;
- a secondary room reached through an interior doorway and containing one exterior window;
- a bookshelf/cabinet or similarly substantial object placed in the light-spill path;
- intended painted/grungy official material treatment plus neutral/primitive comparison;
- foundational time-of-day/environment controls, direct sun/shadows, and region-profile atmosphere;
- later coarse directional light probes/opening transmission as earned.

A presentation-only debug environment provider may drive time/profile/weather until authoritative Game systems exist. That provider must never affect Game truth.

The selected benchmark should be usable during dogfooding/PF6 look-and-feel evaluation, but final game-wide art completeness and advanced GI are not PF6 requirements.

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

**Current disposition: CLOSED / Gate 0 CLOSED after R3. PF6 PLANNED / NOT READY / NOT STARTED.** The
[2026-09-26 evidence](../../_Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.md)
preserves both repaired failures and passing controls. Durable Local save sequence, explicit exact
Local guest restore/Chat, generation-coherent metadata copying and active Remote Settings are repaired.
One uninterrupted canonical Release passed on `ac6a81bf`; that historical result is not the R1
final-tree result. The linked [R1 companion](../../_Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r1.md)
records all later verification outcomes. R1 captures 28 real native PNGs (20 Cabin, one known-scene,
three tier/nameplate, four Remote), exercises four actual Settings/display transforms, preserves
identity through Detailed/Cheap/Detailed, and records two eight-lifetime radius-1 memory campaigns.
After warmup RSS reuses an approximately 502–512 MiB retired band in the final run; logical owners
retire cleanly, without demanding startup RSS or attributing every retained byte to a driver cache.
The historical capture stall did not reproduce on the retained original export; the new tool
actively requests and observes a bounded rendering boundary. Missing authored secondary-room,
interior-doorway and window surfaces are explicit, not silently manufactured. Advanced Experimental
Vegetation is device-owned/default Off/next-entry in settings v6.

An unexpected `HttpListener` header-finalization `NullReferenceException` during unchanged hosting
shutdown stress failed one canonical R1 run. A focused passing retry does not establish its repair.
The linked [R2 investigation](../../_Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r2.md)
repairs abandoned application accept ownership and competing application response closers, and
moves the existing grass preference beside Show Advanced. Its controlled managed-runtime probe
still permits framework-owned header mutation during endpoint teardown without an application-visible
context. R2 therefore did not close the historical formatter failure by passing host stress.
The [R3 replacement](../../_Benchmarks/Baselines/client-f1.12-product-dogfood-pf6-readiness-2026-09-26.r3.md)
retires production `HttpListener`, without suppressing `NullReferenceException`. A BCL-only Framework
endpoint owns frozen routes and finite per-service queue/worker/deadline budgets; the Kestrel Bridge
owns supported native lifecycle. Actual dispatch, response write/completion and shutdown ownership
are awaited. Real sockets, exact-port rebinding and a second independent service establish the
retirement/isolation contract without claiming private framework callback instrumentation.
Three independent final focused Release processes pass 3,072 stress cycles and 48 exact-port cycles.
Uninterrupted canonical Release passes on `cc937185ce630fdb707e51bdee6372f550ff77b0`; R3 evidence
commit `d9234948989ac9a272ab1905a388b58210cd6403` preserves the first canonical failure, its causal
test repair, the intentionally interrupted accounting-repair run, and the successful final run.
The native short regression adds 12 Gameplay Output captures and exact clean retirement.
The obsolete unbounded Cabin harness's unknown historical trigger is not an additional PF6 blocker;
R1's current bounded native capture assurance remains the control.
No current release-blocking finding remains within F1.12 itself. The separately reproduced
pre-P1 native export failure was repaired and P1's canonical Release gate passed on the merged
tree; [P1 closure evidence](../../_Benchmarks/Baselines/framework-p1-native-lifetime-closure-2026-09-28.md)
preserves both the failed environmental attempt and successful rerun. P2 is now active; P3–P11
and Gate 1 remain planned. This is not immediate PF6 readiness: the broader productization and
identity/character-mode gate remains ahead. PF6/W8/W9/W10 remain unstarted. Human motion feel and the historical approximately 1.4–1.67 m
correction range remain ungraded, nonblocking polish questions.

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
    Cabin visual benchmark: intended art vs primitive/clay
    Cabin visual benchmark: dawn/noon/dusk/night
    Cabin visual benchmark: bright/balanced/oppressive environment profiles
    Cabin visual benchmark: door/window light spill and blocker occlusion

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

- F1.12/Gate 0 is closed and the subsequent P1–P11 productization work has closed or received explicit accepted deferrals, with Gate 1 satisfied;
- no known Local/Remote ownership mismatch remains;
- no Official high-quality art is inseparably required by the renderer binary;
- primitive-only presentation is supported;
- semantic activation/presentation resolution separation is proven;
- common content scopes/CAS behavior is proven;
- UI/gump category/lifetime/focus behavior is coherent;
- declarative camera/control profiles are stable enough for the intended playtest;
- F1.8A remote prediction/reconciliation/interpolation has impairment-backed quality evidence;
- current renderer has an intentional high-cardinality strategy rather than accidental node mirroring;
- selected native batching path has measured evidence;
- reconnect/session retirement is clean;
- remaining issues are human-feel/polish or explicitly deferred.

---

# 17. PF6 - External human validation

F1.12/Gate 0, P1 and P2 are closed; PF6 remains planned, not ready and not started. P3 is next,
followed by the planned P4–P11 work and Gate 1. Only after that productization and
identity/character-mode gate closes may the existing PF6 campaign begin. R3 did not implement
these future capabilities or begin human validation.

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
