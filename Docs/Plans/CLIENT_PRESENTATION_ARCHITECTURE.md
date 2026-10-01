# UltraSim Client Presentation, Content, and Renderer Architecture

**Status:** architecture/design direction for the pre-PF6 client-completion pass.

**Relationship to existing plans:** this document is the durable client/presentation/content companion to [CLIENT.md](CLIENT.md), [ASSET_STREAMING.md](ASSET_STREAMING.md), [TERRAIN.md](TERRAIN.md), and [CONCEPT_DEMO.md](CONCEPT_DEMO.md). CLIENT.md remains the checkpoint/status roadmap. This document owns details that are easy to lose when a vertical slice proves only that something can be rendered.

**Implementation campaign:** [CLIENT_COMPLETION_PLAN.md](CLIENT_COMPLETION_PLAN.md) turns this architecture into the ordered F1.0-F1.12 pre-PF6 execution plan, with a dedicated F1.8A remote-motion checkpoint between F1.8 and F1.9.

**Current audio realization:** P8 is closed by the [Audio Kernel/Sound Engine evidence](../../_Benchmarks/Baselines/framework-p8-audio-kernel-sound-engine-2026-09-30.md). Semantic cue identity and finite source admission are engine-neutral; Godot alone owns native players, the listener and stream retirement. This is a presentation consumer, not a new Game authority or a replacement for the later P9 visual pass.

**F1.2 implementation boundary:** the current Concept Demo now proves WorldPack and compiled
Inventory/Server Monitor UI through one exact Realm semantic activation, while an independent
authenticated message-149 plan authorizes X/Y actor art. Actor-art acquisition is non-gating.
An engine-neutral, immutable `PresentationGeneration` selects compatible explicitly enabled User
art, then Realm art, `world.local`, and renderer Primitive per logical actor ID. Its process-local
generation fences late native work; its effective-selection fingerprint is stable content identity.
F1.5 persists a bounded exact user-pack order in a separate policy namespace and shares only
immutable CAS blobs with authorization scopes. F1.6 adds Texture/Material effective leaves and a
restart-stable Godot derived cache without putting native identity into semantic content. F1.3 supplies
finite `ClientPrimitivePresentation` descriptors and a permanent Godot primitive substrate for
already-disclosed actor, item, public, frozen-static, and Multi-fragment facts. Model absence and
texture absence remain independent; neutral fallback is distinct from diagnostic missing texture.
Primitive-only acceptance suppresses optional native art without changing the F1.2 semantic
activation or its proof. F1.4 physically removes Official actor art from every PCK and productizes
it as a separately removable deterministic `world.local` package consumed through the common CAS.

**Current sequencing:** PF0-PF5/PF5-R11 established the server, Network, content, and semantic-client foundations strongly enough that PF6 external human validation is ready. Before PF6, the client needs one architecture-completion/productization pass. The goal is not final art polish. The goal is a coherent dumb terminal whose content, UI, camera/control, fallback, and renderer boundaries match the architecture already proven underneath.

---

# 1. Executive model: a sophisticated dumb terminal

UltraSim's client should be understood in the Ultima Online sense of a **dumb terminal**:

- the authoritative Game/server owns game truth;
- the client owns presentation, input capture, local preferences, caches, and native realization;
- the client can be visually sophisticated without becoming authoritative;
- content and presentation knowledge may be installed, streamed, updated, replaced, or overridden;
- the client should continue functioning when high-quality presentation content is absent.

Conceptually, authority may publish:

~~~text
public object X exists
logical presentation definition Y applies
authoritative public position/orientation is Z
these cold presentation facts are disclosed
this UI/model/action state is currently authorized
~~~

The client decides how those facts become meshes, materials, textures, animations, nameplates, overhead chat, UI/gumps, audio, camera behavior, input gestures, tooltips, and effects.

The client does **not** decide whether the object exists, whether an interaction succeeded, whether an item spawned, whether a character owns an item, whether a wall blocks movement, or whether an undisclosed object should be visible.

> **The client may choose how known truth is presented. It may not manufacture new Game truth.**

This is the core rule behind primitive fallback, renderer interchangeability, user resource packs, camera profiles, local content, and server content.

---

# 2. Single Player is offline multiplayer

"Single Player" is a product composition, not a different client semantic model.

~~~text
Single Player
    normal client
    + local authoritative Game/server capability
    + locally available content activation(s)
    - remote Network transport

Online
    normal client
    + remote Network capability
    - local authoritative Game/server capability

Complete
    normal client
    + local authoritative capability
    + remote capability
~~~

The local authority remains authoritative even though it runs in the same process. Godot does not directly become Game authority. The accepted Client.Local owner-thread bridge remains the correct boundary.

Single Player is therefore best understood as **offline multiplayer**: the same semantic client, content/package concepts, public/private/UI projections, presentation definitions, and input intents are used; only authority and delivery topology differ.

Local/Official content is not "hardcoded Single Player graphics." It is one root-like content scope available without joining a remote realm.

---

# 3. Renderer/engine independence is a first-class goal

The engine-neutral client should remain usable above more than one native rendering engine.

~~~text
                 ENGINE-NEUTRAL CLIENT

                 Client.Core
                 Client.Content
                 semantic world state
                 UI semantic runtime
                 input/control semantics
                 presentation definitions
                 settings/profiles
                        |
          +-------------+--------------+
          |             |              |
          v             v              v
      Godot adapter   Unity adapter   Unreal adapter
      renderer        renderer        renderer
      native UI       native UI       native UI
      audio           audio           audio
      resource import resource import resource import
      window/input    window/input    window/input
~~~

Possible products might therefore be:

~~~text
MyGame.Godot.exe
MyGame.Unity.exe
MyGame.Unreal.exe
~~~

They need not produce pixel-identical output. They should consume equivalent semantic truth and produce equivalent Game intent.

A renderer-swap acceptance concept is:

~~~text
same server
same authenticated application
same content activation
same WorldPack
same semantic caches
same server UI definitions

Godot client
Unity client
Unreal client

=> same known entities
=> same authorized UI/actions
=> same content-resolution outcome
=> equivalent control intents
=> renderer-native resources may differ
~~~

The renderer is powerful, but the underlying game meaning remains separately owned.

This is similar in spirit to placing a new presentation engine over an existing simulation/game machine through explicit hooks rather than moving game authority into the renderer.

---

# 4. Portable source content must not secretly be Godot content

Authoritative/passive content formats should be renderer-neutral where practical.

Portable source artifacts may include:

~~~text
GLB / glTF
PNG / WebP / neutral texture payloads
canonical material definitions
.usworldpack
.usui
actor presentation definitions
animation semantic definitions
static/Multi definitions and placements
future neutral audio formats
future neutral region/property metadata where earned
~~~

Engine-native assets are **derived cache**, not portable source truth:

~~~text
Godot .tscn/.res imported realizations
Unity prefabs/import database
Unreal .uasset resources
renderer-specific shader/resource caches
platform GPU-compressed atlas pages
~~~

Deleting an engine-specific derived cache must not destroy the underlying verified content installation. A renderer should be able to rebuild native realization from the same neutral source content.

---

# 5. Primitive substrate, content scopes, and presentation precedence

UltraSim does **not** need four separate physical content stores.

The durable model is:

~~~text
Permanent renderer substrate
    Primitive

Shared immutable content store
    AssetBlob / CAS

Content scopes
    world.local
    authenticated remote realm/world scopes

User presentation overrides
    resource packs

Renderer-derived caches
    Godot / future Unity / future Unreal
~~~

"Local" and "Server" remain useful **presentation precedence positions**, but they are the same species of package/catalog/content scope over the same content machinery and shared immutable blob store.

## 5.1 Layer 0 - Primitive substrate

The primitive substrate is permanently available client implementation, not an ordinary optional content pack.

Its purposes are:

- emergency fallback;
- greyboxing;
- content diagnostics;
- making missing presentation obvious;
- keeping semantic gameplay usable when higher-quality presentation is unavailable.

Examples:

~~~text
missing texture       -> deliberately obvious checkerboard/diagnostic texture
texture-debug off     -> neutral grey fallback
missing material      -> neutral default material
missing static mesh   -> box derived from declared bounds
missing actor model   -> capsule/body primitive
missing item model    -> simple primitive from presentation/physical bounds
missing icon          -> generic diagnostic icon
missing animation     -> bounded timer/pose substitute for accepted action duration
~~~

The primitive substrate must be small enough to ship with every renderer product and must not be removable through ordinary content management.

Primitive fallback is not authorization. It does not satisfy a missing required semantic artifact such as an authoritative WorldPack. It is only a realization of semantic information the client already legitimately possesses.

## 5.2 Local / Official content scope

`world.local` is the reserved local/Official content scope: official/default presentation plus locally installed world content. It is not a separate content implementation.

It is **not baked into the Godot executable, APK, embedded PCK, or equivalent renderer binary**.

It should be:

- separately installed;
- discoverable through the normal content system;
- hash validated;
- replaceable/updateable;
- independently removable;
- usable by Single Player and Complete;
- potentially usable by Online as ordinary local fallback content without granting local Game authority.

Deleting the Official Presentation pack should permit primitive-only rendering.

Deleting an Official World package means that particular local playable world is no longer installed. The client must not fabricate authoritative terrain merely because a presentation fallback exists.

Official distribution may naturally contain separate packages such as:

~~~text
UltraSim.Local.World
    WorldPack / terrain
    statics
    Multi placements
    structural content

UltraSim.Local.Presentation
    textures
    materials
    models
    animations
    icons
    audio when supported

UltraSim.Local.UI
    passive stock UI definitions
~~~

Exact package splitting is not frozen; the ownership distinction is.

## 5.3 Active remote server/realm content scope

Remote server content is another content scope over the same package/catalog/CAS machinery, authorized by the verified application realm and activation plan.

Server content may include far more than textures/models:

~~~text
WorldPack / maps
terrain
additional structural shells
frozen statics
Multi definitions/placements
textures
materials
models
animations
icons
UI definitions
actor presentation
other passive compiled definitions earned by concrete consumers
~~~

The current A0 architecture already proves the important foundations:

- immutable content hashes;
- package/manifest identity;
- authenticated realm/application activation;
- global hash-addressed blob storage;
- realm-specific manifest/activation authorization;
- Add/Replace resolution;
- persistent CAS;
- source-independent acquisition;
- complete WorldPack acquisition;
- content-backed UI definitions;
- content-backed actor GLBs.

Server identity/authorization must not require physically duplicating bytes that another realm already uses.

## 5.4 User texture/resource packs

The highest presentation layer is user-selected client-side passive override content, analogous to a Minecraft resource/texture pack.

A user resource pack may eventually override compatible presentation leaves such as:

- textures;
- materials;
- icons;
- models where policy permits;
- audio where policy permits;
- UI theme/presentation leaves where policy permits.

A resource pack does not:

- create Game authority;
- reveal undisclosed entities;
- satisfy a missing required authoritative WorldPack;
- inject executable server code.

Executable client extensions, if ever supported, remain a separate trust/install/runtime boundary and are not resource packs.

F1.5 implemented the first intentionally narrow member of this family; F1.6 extends the same
bounded explicitly installed/enabled packages with passive PNG/WebP Texture Replace while retaining
ActorPresentation GLB Replace. Exact package/manifest identities and enabled order
persist under `resource-packs/`; they do not enter Realm or `world.local` activation. Missing
logical targets are inactive, while corrupt or skeleton-incompatible selected actor replacements
reject the complete candidate generation and preserve the current one. WorldPack, UI definition,
Material Replace, Icon, script/shader/executable, Add, and all other domains remain rejected.
Logical Material definitions retain their lower-authorized scalar truth while their texture
references select the effective User/Realm/Local texture leaf.

---

# 6. Presentation precedence versus graceful fallback order

The following is **presentation precedence**, not a declaration of four physical stores or four semantic activation bases.

The conceptual presentation composition order is:

~~~text
Primitive
    ->
Local / Official
    ->
Server / Realm
    ->
User Resource Pack
~~~

Later compatible precedence positions may replace or override presentation definitions from earlier positions. Local and Server entries are resolved from ordinary content scopes over the same shared storage.

The runtime presentation fallback direction is the reverse:

~~~text
User override available?
    yes -> use it
    no
Server realization available?
    yes -> use it
    no
Local/Official realization available?
    yes -> use it
    no
Primitive realization
~~~

Shorthand:

~~~text
composition precedence:
    Primitive -> Local -> Server -> User

graceful realization fallback:
    User -> Server -> Local -> Primitive
~~~

Logical identity should remain stable throughout. A sword is not a different Game object because its user resource pack supplies a different texture or model.

---

# 7. Structural/semantic content and presentation content are not interchangeable

Graceful presentation fallback must never invent authoritative world structure.

Safe case:

~~~text
authoritative Multi says:
    wall here
    floor here
    door here
    object occupies these bounds

server wall mesh missing
local wall mesh missing

=> render boxes/primitives using known structural/presentation facts
=> collision/interactions remain server-owned
~~~

Unsafe case:

~~~text
required authoritative WorldPack is absent

=> silently substitute an unrelated local map
~~~

The second case presents different world truth and must fail closed.

Content therefore needs to distinguish, at least conceptually:

~~~text
required semantic/structural artifacts
    must validate before that semantic world can be entered

presentation realization artifacts
    may walk backward through the fallback chain
~~~

The primitive substrate is intentionally strong enough that a structurally valid world can remain playable when its high-quality presentation disappears.

---

# 8. The Concept Demo/Cabin should become ordinary loadable world content

The floating Concept Demo island should not conceptually live "inside the client."

It should be representable as ordinary Local or Server content:

~~~text
Official Concept Demo activation

    World
        WorldPack
        terrain/map
        intentional void
        sparse shells
        frozen statics
        static Multis

    Presentation
        materials/textures
        actors
        object models
        effects

    UI
        Inventory
        Server Monitor
        informational signs
~~~

A local authority selects an installed Official activation. A remote realm may select a different activation.

Once applied, Client Core and the renderer should consume the same classes of semantic/presentation truth regardless of whether bytes came from local disk, server-hosted HTTP, external HTTPS/CDN, or warm CAS.

## 8.1 Cabin Visual Benchmark

The Cabin is also the selected first canonical visual/art/lighting benchmark. The owning cross-system direction is [Visual Art / Lighting / Cabin Benchmark](../../AIOS/canonical/visual-art-lighting-and-cabin-benchmark.md).

This extends the Cabin's role without changing authority:

- intended real/near-real art ships as ordinary removable Local/Official presentation content through world.local;
- the primitive substrate remains independently available;
- geometry should remain readable in neutral/texture-off presentation;
- the official/default material treatment targets the illustrative painted/grungy UltraSim look rather than photoreal surface capture;
- a two-room cabin, exterior door, secondary-room window, and a bookshelf/cabinet blocker provide repeatable interior-lighting tests;
- the client environment resolver may initially consume presentation-only debug time/profile/weather state;
- future authoritative calendar/region/weather state feeds that same seam instead of moving Game truth into the renderer.

The near-term lighting target is direct sun/important lights plus credible shadows and a cheap directional ambient/light-field foundation. Window/door transmission and local interior propagation may then be layered in without requiring full general-purpose GI.

The existing deterministic A0.1 Cabin content oracle remains separate evidence. Real-art iteration must not casually rewrite frozen content-oracle fingerprints.

P9's opt-in `benchmark-world:cabin-visual/v1` is a distinct authored WorldPack, not a
replacement for Classic Cabin. It carries the two-room walls, continuous support floor,
exterior/interior door apertures, a real Room-B window aperture and a bookshelf blocker.
The existing removable `official.world-local.cabin-p9` package realizes compatible art;
Primitive uses the same structural truth. Native capture supports Room A/Room B time-of-day
controls, but does not yet certify perceived light spill or final visual quality.

---



# 9. Global blob pool and content-scope metadata

Local and remote content bytes should be stored by immutable content hash in one shared pool within the selected content-store scope.

A conceptual logical layout is:

~~~text
Assets/
    AssetBlob/
        ab/abcdef...
        cd/cdef01...
        ef/ef0123...

    world.local/
        manifests/
        activations/
        catalog/
        profiles/
        settings/

    <AuthenticatedRealmOrWorldId-A>/
        manifests/
        activations/
        catalog/
        profiles/
        settings/

    <AuthenticatedRealmOrWorldId-B>/
        manifests/
        activations/
        catalog/
        profiles/
        settings/
~~~

`world.local`, Realm A, and Realm B may all resolve:

~~~text
actors:human/male -> SHA256 ABCD...
~~~

The physical bytes exist once in the global pool.

Each content scope independently owns or references the metadata appropriate to its authority:

- authorization;
- activation identity;
- package manifests;
- catalog composition;
- required/optional policy;
- server profile recommendations;
- user per-server preferences/settings.

The current ContentCache global-blob/realm-metadata architecture is the correct foundation. Generalize it so `world.local` is an ordinary scope over the same store. Do not regress to duplicated Local bytes plus one physical asset directory per server.

Blob possession is not realm authorization.

F1.1 implements that foundation with a finite `ContentScopeKey`: reserved `Local` or an exact
authenticated `ApplicationRealmId`. Both use the same persistent install/open/activation/catalog,
CAS, lease, reachability, and retirement machinery. `world.local` metadata is stored under
`scopes/local`; the accepted `realms/<ApplicationRealmId>` layout remains backward-compatible.
Remote protocol projection accepts only Realm scopes. This is storage/authorization unification,
not the later presentation-precedence or Official-art migration.

---

# 10. world.local is an ordinary content scope

Local/Official content must not remain a bespoke second content system.

The desired distinction is scope/source/authority, not package semantics or physical storage.

Remote:

~~~text
authenticated realm
    -> activation plan
    -> verified packages
    -> resolved catalog
~~~

Local:

~~~text
world.local content scope
    -> local activation plan
    -> verified packages
    -> resolved catalog
    -> blobs from the same shared CAS/store
~~~

Below that boundary, reuse:

- package validation;
- logical content IDs;
- content hashes;
- Add/Replace semantics;
- catalog resolution;
- blob lookup;
- native realization;
- atlas construction.

The campaign identified these transitional shortcuts:

1. bundled local X/Y high-quality presentation assets exposed through Godot res://Assets and baked into local exports while Online receives equivalent assets through packages/CAS (removed by F1.4);
2. the Cabin Default/Replace/Add local set existing primarily as an in-code acceptance oracle rather than the final separately installed Local content product.

Those were useful proof stages. They are not the desired final product boundary.

The persistent cache now shares immutable bytes across Local, Realm, and explicitly installed User
packs while retaining separate authorization/policy metadata. User packs are not scopes and cannot
satisfy either Local or Realm activation merely by possessing the same blob.

---

# 11. Suggested filesystem ownership, not frozen spelling

A possible logical installation shape is:

~~~text
/GameName/
    MyGame.Godot.exe
    libraries/
    native/runtime libraries/

    Assets/
        AssetBlob/
            <hash-addressed immutable bytes>

        world.local/
            manifests/
            activations/
            catalog/
            profiles/
            settings/

        <AuthenticatedRealmOrWorldId>/
            manifests/
            activations/
            catalog/
            profiles/
            settings/

        ResourcePacks/
            <user pack>/

        Derived/
            Godot/
            Unity/
            Unreal/
~~~

The same logical store may instead span a read-only install root plus platform-appropriate writable user storage. Portable mode may place both beside the executable.

The architecture must not depend on these exact names.

Required properties are:

- executable/runtime is separable from removable Official content;
- immutable Local/remote bytes are deduplicated in the shared store;
- Local and realm/world scope authorization/metadata remain scope-specific;
- user packs are separately owned;
- renderer-derived caches are disposable/rebuildable.

---

# 12. Atlas pipeline: logical content first, physical batching second

UltraSim should use a Minecraft-like texture-atlas strategy where measurements support it.

The atlas sits **after logical content resolution** and **before native renderer realization**.

Example logical material:

~~~text
material: oak_planks
    albedo = textures:wood/oak
    normal = textures:wood/oak-normal
    roughness = 0.72
~~~

After four-layer catalog resolution:

~~~text
textures:wood/oak
    -> selected immutable image blob
~~~

Atlas planning may then produce:

~~~text
textures:wood/oak
    -> WorldOpaque atlas page 0
    -> rectangle x=768 y=512 w=128 h=128
~~~

The logical texture identity remains textures:wood/oak. Atlas coordinates are physical realization, not semantic identity.

The purpose is to reduce texture/material switches and draw calls by letting large numbers of objects reference a small number of atlas pages.

---

# 13. Atlas compatibility groups

Do not force every texture into one universal sheet.

Different rendering requirements may need finite compatibility groups such as:

~~~text
WorldOpaque
WorldCutout
WorldTransparent
UI/Icon
~~~

Normal/ORM maps may use matching companion pages with identical region layout:

~~~text
WorldOpaque_Albedo_0
WorldOpaque_Normal_0
WorldOpaque_ORM_0
~~~

The exact number of atlas families is evidence-driven. Two, three, four, or another small finite set is acceptable if it materially simplifies batching.

Atlas grouping is related to, but does not replace, geometry batching.

Godot MultiMesh repeats one mesh. Sharing an atlas does **not** make heterogeneous shelf clutter one heterogeneous MultiMesh.

A shelf containing a book, globe, rune, book, statuette, and ring may be represented as:

- one combined/baked heterogeneous mesh using the shared atlas;
- several compatible MultiMesh groups for repeated meshes/materials;
- another renderer-specific batching strategy.

Authoritative item identities/interactions may remain separate even when visual geometry is batched.

---

# 14. Atlas determinism, derived caches, and platform realization

Atlas work should be deterministic/cacheable for the same resolved input and policy.

A conceptual derived key is:

~~~text
ResolvedPresentationCatalogFingerprint
+ atlas policy/version
+ renderer backend
+ platform texture capability/profile
= DerivedAtlasKey
~~~

Derived output might live under:

~~~text
Derived/Godot/Linux-x64/<key>/
Derived/Godot/Android/<key>/
Derived/Unity/Windows/<key>/
~~~

Portable source content remains identical while native GPU representation may differ.

For example:

- desktop may choose BC/BPTC-family compression;
- mobile may choose ASTC;
- another renderer may use a different native resource container.

Do not make a remote server depend on Godot-specific GPU formats.

A useful long-term hybrid is:

~~~text
server/deployment content build
    -> canonical resolved presentation set
    -> optional canonical atlas grouping/layout plan

client renderer
    -> realize those pages in platform-native GPU format
~~~

Whether the server distributes prebuilt atlas pages, only a canonical layout, or only source textures remains evidence-driven.

Derived atlas pages themselves may be hash-addressed/reused when identical.

---

# 14.1 Semantic presentation roles and derived UI portraits

UI should consume **semantic presentation references**, not force every Game definition to own a
separate hand-authored icon.

Conceptually:

```text
SemanticPresentationReference
    DefinitionDomainId
    StableDefinitionId
    PresentationRoleId
```

Representative roles:

```text
World
Equipped
InventoryPortrait
VendorPortrait
CraftingPortrait
CharacterPortrait
BestiaryPortrait
TooltipPreview
Inspection
Paperdoll
Thumbnail
```

A definition may explicitly override any role. When an explicit portrait is absent, presentation
may derive a compatible role from the definition's normal World presentation.

Example:

```text
Katana
    World = katana.model

InventoryPortrait
    no explicit 2D icon
        ↓
    derive from World model
        ↓
    authored camera/orientation/autofit
        ↓
    render to derived texture
```

The same rule applies to creatures, props, furniture, crops, chests, statuettes and other
semantically presentable definitions.

The UI kernel remains domain-neutral. Game-level helpers may resolve Item/Creature/etc. definitions
into the same generic reference, but Client presentation owns native realization.

## 14.1.1 Portrait realization tiers

Use bounded tiers rather than one live SubViewport/native scene per visible UI cell:

```text
Cached Portrait
    render once / reuse derived texture
    normal inventory/list/vendor/crafting use

Live Portrait
    bounded hover/selection realization
    optional rotation or animation

Inspection View
    small bounded interactive realization
    pointer/gesture rotation and zoom
```

A rigged creature portrait may evaluate one named pose or animation frame, render it, and retire the
live skeleton. Only Live/Inspection modes retain animation machinery while active.

Client-local visual behavior may include:

```text
HoverBehavior = None | Rotate | PlayAnimation | RotateAndAnimate
InspectionAllowed
Inspection camera/profile
```

Do not encode arbitrary per-definition mouse callback code as the normal presentation contract.

## 14.1.2 Derived portrait lifetime and cache identity

Generated portraits are disposable derived resources.

A conceptual cache key is:

```text
resolved semantic presentation identity
+ PresentationRole
+ portrait profile/version
+ relevant selected immutable asset hashes
+ renderer/platform realization profile
= DerivedPortraitKey
```

The cache can be deleted and regenerated without changing Game/content truth.

After generation, compatible portraits may join the `UI/Icon` atlas family. Therefore hundreds of
inventory items can still render as ordinary batched/atlased images even though their source
presentation is 3D.

## 14.1.3 Fallback

Role resolution should degrade visibly and deterministically:

```text
explicit role-specific presentation
    ↓
compatible general presentation
    ↓
derive from World presentation
    ↓
primitive semantic representation
    ↓
missing-content checker/debug presentation
```

A missing portrait-specific asset must not make a semantically valid item disappear from UI.

---

# 15. Client startup/content-realization flow

A mature renderer product should approximately do:

~~~text
start renderer product
    -> initialize primitive substrate
    -> open global content/CAS state
    -> discover installed Local/Official activation
    -> discover enabled user resource packs
    -> construct local baseline presentation catalog
~~~

Local session:

~~~text
select local world/content activation
    -> validate required structural content
    -> compose Primitive + Local + User
    -> build/reopen derived atlas/native resources
    -> enter local authority
~~~

Remote session:

~~~text
authenticate deployment/application realm
    -> receive content activation
    -> acquire/reopen required blobs
    -> compose Primitive + Local + Server + User
    -> validate required World/UI/etc.
    -> build/reopen derived atlas/native resources
    -> send exact content/application proof
    -> enter remote authority
~~~

Higher-quality presentation realization may be lazy where semantic safety permits. Required structural semantics must be present before world entry.

---

# 16. UI scene-tree categories and z-order

## Application workspace and Gameplay Frame (F1.7-F1.9A)

Physical Display, Physical Application Window, logical Application Resolution, Gameplay Frame,
Gameplay Output Resolution, and Internal 3D Render Resolution are separate quantities. Physical
window resizing or fullscreen presentation aspect-fits the stable logical application and never
rewrites its selected resolution or expands World visibility. The Gameplay Frame is the
application-space rectangle showing the World image. Gameplay Output is its final pixel grid;
Internal 3D scale controls expensive rendering inside that output. World render distance remains
separate from all of these. Simple mode locks Application/Frame/Output/Internal together and
behaves like a conventional game resolution selector. Advanced mode exposes the same layers
independently; it is not a separate renderer. Simple has finite exact-ratio aspect/resolution
presets; Advanced Custom accepts safe bounded arbitrary dimensions without inventing a named
aspect family. Output must have the Frame's exact reduced aspect to avoid distortion.

The frame is below normal UI. The implemented order is workspace/background, Gameplay Frame,
frame-associated World overlays, HUD, Menus/Gumps, Debug, then Context/Tooltip. Native Label3D
nameplates and overhead Chat follow their actors inside the output viewport; any application-space
World overlay must convert projected output pixels into frame coordinates;
ordinary gumps can cover the frame or occupy unused workspace. F1.7 owns the host/shared UI
composition, F1.8 the Core camera crop, F1.9A the real SubViewport and display stack, and F1.9B
the scalable renderer backend. The earlier main-viewport camera compensation is transitional,
not the F1.9A target.

The maximum stock-client world-space view is a canonical 16:9 envelope. The selected output aspect sees the largest centered rectangle of that aspect contained in the envelope: 16:9 sees all of it, 4:3 or 5:4 crops horizontally with the same vertical extent, and an aspect wider than 16:9 retains canonical width while cropping vertically. Scaling or increasing resolution never expands that envelope. This is presentation consistency, not security: a modified client can ignore crop, and Game AOI/disclosure remains authoritative. World render/residency distance is separate from window size, frame size, output pixels, and this view envelope.

The intended client UI hierarchy is a native presentation category/z-order policy.

Canonical ordering:

~~~text
World presentation
    nameplates
    life/status bars over actors when gameplay earns them
    overhead chat
    world prompts/markers
    future damage numbers

HUD
    chat feed/input
    player status
    connection/ping
    future hotbar/minimap/status surfaces

Menus / UI / Gumps
    application menus
    Settings
    Inventory
    paper doll
    server-composed windows
    informational signs
    Server Monitor
    modal prompts

Debug
    local diagnostics
    FPS/frame information
    cache/world/content/network diagnostics
    authorized admin tooling

Context / Tooltip
    hover tooltips
    right-click context menus
    transient inspection overlays
    surfaces that must remain usable above every lower category
~~~

Scene-tree/category ordering should naturally produce:

~~~text
World < HUD < Menus < Debug < Context/Tooltip
~~~

Debug and Context/Tooltip are deliberately high:

- Debug should remain visible while inspecting ordinary client/menu state where its active page permits it.
- Context menus/tooltips must work over World, HUD, normal menus, and Debug.

F1.7 extends the persistent F0.1 shell with Workspace and Context/Tooltip, and Local/Remote gameplay now leases the same World/HUD/Menus/Context composition. The transparent category hosts ignore pointer input themselves; their concrete children own only their visible interaction surfaces.

---

# 17. UI focus is related to, but separate from, z-order

A surface being visually higher does not automatically make it modal or grant it all input.

Examples:

~~~text
tooltip above Inventory
    -> visual only
    -> does not own text/modal focus

context menu above Inventory
    -> may capture pointer interaction
    -> may suppress world interaction according to policy

modal Settings/menu
    -> suppresses world interaction

Debug readout
    -> visible
    -> captures only controls needed by its active page
~~~

Keep the engine-neutral focus policy separate from native scene ordering.

Do not accumulate independent Godot _Input exceptions as the client grows.

---

# 18. UI/gump lifetime and window management

When a menu/gump/view closes, its realized native presentation should ordinarily be **removed from its category/tree**, not left indefinitely hidden.

The existing generic Godot semantic UI renderer already removes and frees its root on semantic close. Preserve that rule.

The F1.7 client window/gump manager owns native presentation policy for:

- category placement;
- multiple simultaneously open non-modal gumps where allowed;
- bring-to-front within the Menu category;
- click/focus ordering;
- modal versus non-modal behavior;
- dragging where the UI definition/profile permits it;
- viewport clamping;
- optional remembered local position;
- transient context children;
- exact removal on close.

Server UI authority should not own Godot nodes or screen coordinates.

The current manager deliberately remains bounded and presentation-only. Remembered placement and a
general modal/window policy remain later product work; semantic views and authoritative actions stay
inside `UiClientRuntime` and Game.

---

# 19. Pooling is presentation-only and selective

Most closed UI should be destroyed/retired.

Pooling is justified only for measured or obviously bursty high-churn native presentation where creation cost matters.

Nameplates are a possible candidate, but note the current actor presenter creates the nameplate with the actor presentation and Ctrl reveal primarily toggles visibility; Ctrl itself should not be assumed to instantiate hundreds of labels.

If future dense actor enter/retire churn makes nameplate creation expensive, a bounded pool may be used.

A pooled presentation element must:

~~~text
detach semantic/entity binding
clear retained text/state
leave the active category/tree
return to bounded pool
~~~

It must not remain a hidden semantically bound view.

Do not pool ordinary Inventory/gump windows merely because pooling exists. Stale focus/action/model state is a larger correctness risk than their likely creation cost.

---

# 20. Debug cycle and context menus

The Debug interaction is a finite local cycle. F1.7 currently uses:

~~~text
F11:
    Off
    -> Client
    -> Presentation
    -> Content
    -> Off
~~~

Exact pages/key are configurable. The behavior is a client-local finite diagnostic state machine, not server code.

`ContextMenu` now means actual presentation/context behavior. The bounded presenter shows only
already-disclosed target identity and the finite Select, Interact/Use, and Cancel actions. It closes
on outside click, invalid target, shell modal, and session retirement. Right-click no longer doubles
as OrbitCommand stop; Escape and shell/modal input loss keep the explicit cancellation path.

---

# 21. Declarative server client profiles

A server/realm should be able to recommend how the stock client feels without distributing code.

This includes:

- control grammar;
- physical key/mouse/gamepad recommendations;
- camera behavior;
- camera capabilities and limits;
- presentation-oriented settings where appropriate.

The server sends **data**, never camera/input scripts.

Current F0.3c proves the security/lifecycle foundation: finite authenticated recommendation, exact profile identity/hash/proof, user consent modes, realm scoping, and preservation of reserved user controls.

Its current control-grammar/camera-enum/distance/FOV shape is intentionally narrow. The client-completion pass should define an extensible but finite declarative camera/control rig boundary rather than freezing every named game style as a protocol enum.

---

# 22. Camera profile as a declarative rig

Names such as "UO-like", "Sims-like", "Kenshi-like", and "Valheim-like" should be ordinary stock/user presets over reusable declarative capabilities.

A bounded camera rig may eventually describe fields such as:

~~~text
Projection
    Perspective / Orthographic

Target
    anchor/pivot offset
    follow target class

Follow
    Locked / Soft / Free
    default distance
    minimum/maximum distance
    follow speed
    follow smoothing
    reconciliation/correction smoothing

Yaw
    locked/unlocked
    freely rotatable?
    min/max where useful
    snap increment where useful
    rotation speed

Pitch
    locked/unlocked
    default
    min/max
    rotation speed

Zoom
    enabled
    min/max/default
    speed/step
    smooth/discrete behavior

Pan
    enabled
    speed
    edge-pan capability where supported

Lens
    FOV
    orthographic size where relevant

Occlusion/collision presentation policy
    stock finite capability when earned
~~~

The exact schema must be finite, bounded, canonical, and versioned.

A server recommendation might describe an isometric OrbitCommand camera with fixed or 90-degree-snapped yaw, enabled 8-24m zoom, fixed pitch, and recommended bindings without requiring an enum named after a commercial game.

---

# 23. Stock and user-created client profiles

The client should support stock profiles and arbitrary user-named profiles composed from the same supported bounded fields.

Example:

~~~text
"Zeke's Kenshi/Sims Mix"
    OrbitCommand
    free yaw
    constrained pitch
    broad zoom
    key pan
    custom physical bindings
~~~

The settings hierarchy remains:

~~~text
Built-in client defaults
    ->
User global settings/profile
    ->
optional accepted server recommendation
    ->
User per-server overrides/profile
    ->
Resolved session settings
~~~

Useful client UX remains:

~~~text
Use Server Defaults
Keep My Controls
Customize for This Server
Ignore Recommendation
~~~

Server recommendations remain transient/cacheable realm data rather than destructively rewriting global user settings.

## Remote motion presentation (F1.8A closed after R1 correction)

Remote motion needs a dedicated presentation checkpoint after F1.8 and before F1.9. The controlled actor may predict its locally submitted command for responsive display, then correlate bounded command/prediction history with authoritative owner state, reconcile error, smooth bounded corrections, and snap/reset on discontinuity, teleport, relocation, or session/generation replacement. Prediction never becomes Game truth. Non-controlled remote actors instead begin with a bounded authoritative-snapshot interpolation buffer, a presentation timestamp/delay, and explicit gap, stale, teleport, and entity-replacement behavior; extrapolation needs separate evidence. Diagnostics must expose correction magnitude and buffer age/health. Packet-free Local authority does not manufacture network latency or prediction debt merely for symmetry.

F1.8A now selects the pose to present without changing Game truth: bounded admitted-command
horizontal lead and owner-state reconciliation for the controlled actor, and bounded receipt-time
interpolation of exact public versions for other actors. Vertical physics remains authority-only.
The actor root and camera consume temporal presentation, while semantic records, disclosure,
proof, interaction validation, and structural tracking remain exact authority-derived facts.
The stock lead is capped at 900 mm; remote interpolation delays 100 ms with 12 samples per actor,
never extrapolates, and holds on missing data. Real authenticated UDP delay/jitter/loss and native
acceptance are recorded in the [F1.8A baseline](../../_Benchmarks/Baselines/client-f1.8a-remote-motion-presentation-2026-09-23.md).
The [R1 correction](../../_Benchmarks/Baselines/client-f1.8a-r1-motion-correction-2026-09-23.md)
ensures that owner displacement consumes only an existing aligned speculative lead; idle and
previously blocked presentation instead converges to authority. Three-dimensional displacement
fences vertical teleports. Pointer nomination intersects the actor's displayed pose while Game
continues to authorize its disclosed identity/range. The accepted camera profile owns pivot
height, and orthographic stepped zoom changes lens size rather than only camera distance.
Rapid reversal/Stop correction tails and sparse impaired public samples remain explicit
human-feel questions, not claims of imperceptible motion.
F1.10 now realizes the same CPU poses through Detailed or BatchedPrimitive bodies. The actual
receipt-time endpoint seam supports future GPU realization, but no GPU interpolation is active;
temporal ownership remains in Core, not Godot nodes.

---

# 24. Input/control profiles remain semantic

Physical keys/buttons resolve into supported client semantic actions.

A server may recommend WASD, mouse buttons, Interact, Inventory, Chat, or camera controls. Game authority still consumes neutral world-space intent such as Direct movement, Destination, Stop, Interact, Primary, or Jump.

Game does not consume:

- Godot event names;
- the W key;
- screen pixels;
- camera-profile names;
- "Valheim mode".

This preserves renderer/input-device independence.

---

# 25. Camera restrictions are not a security boundary

A modified client can ignore presentation recommendations or camera restrictions.

Therefore:

> **If seeing something is security-sensitive, the server must not disclose it merely because the recommended camera normally cannot see it.**

AOI, interest, disclosure, hidden state, permissions, fog-of-war semantics, and similar mechanics remain authoritative Game/server concerns.

Camera profiles describe stock-client behavior and gameplay feel. They are not anti-cheat authority.

---

# 26. Client-owned presentation settings

Some values remain local/user-owned even when a server supplies recommendations:

~~~text
volume
accessibility
UI scale
sensitivity
invert-Y
window/platform essentials
client exit/menu/settings/disconnect controls
~~~

Render-distance and vegetation-distance settings also must not silently expand server disclosure.

A server may advertise compatible/recommended presentation settings, but the renderer owns its local resource budget.

---

## Measured world-group realization (F1.9B)

The [F1.9B comparison](../../_Benchmarks/Baselines/client-f1.9b-scalable-godot-backend-2026-09-25.md)
keeps the existing chunk-local static/Multi/public grouping and places a concrete Godot native
group owner below it. Mesh/material resources are borrowed from the resolved resource owner;
the group owns its MultiMesh and either a MultiMeshInstance3D Node or a direct visual RID.
Direct candidates explicitly own scenario assignment, combined transformed mesh AABB, global
transform/visibility synchronization and release before borrowed resources retire. Native slots
and RIDs remain diagnostics, not public identity or interaction authority. The semantic cache
and bounds picker are independent from this choice.

The measured production selection is **NodeMultiMesh/PerRow**. Direct RIDs substantially reduce
SceneTree Nodes in the fixture but do not consistently improve complete realization CPU and
increase idle/rebase service. Full bulk encoding/submission did not beat per-row setters through
1,024 rows; no crossover threshold is selected. Both controls remain bounded constructor/test
choices, not user settings or Game profiles. Public refresh still replaces the complete layer;
there is no incremental entity-slot map, new terrain backend, or actor-tier implementation here.

Real-native tests cover transforms, AABB/frustum culling, semantic picking, source replacement,
fallback, display/scenario changes, high/low rebasing and disposal. Dummy headless tests certify
only the explicitly available lifecycle behavior. Short GPU diagnostics and cold-build residuals
do not establish a global minimum or sustained frame-rate rating. F1.10 subsequently adds actor
tiers; F1.11 below closes the separate Remote world-material consumer gap and adds opt-in grass.

## Actor representation and temporal endpoint ownership (F1.10)

One semantic WorldPublicEntityId retains its record, CPU presented pose, action sequence and cue
expiry independently of Detailed hierarchy or BatchedPrimitive row. `TryGetPresentedPose` works for
either tier; `TryGetActorRoot` is a Detailed feature/diagnostic query, never semantic existence.
The current [measured policy](../../_Benchmarks/Baselines/client-f1.10-actor-representation-tiers-2026-09-25.md)
is a conservative hybrid: soft 64 Detailed budget, 20 m promotion / 28 m demotion, deterministic
distance/full-ID priority. Controlled, equipped and active action/landing/overhead actors are
mandatory Detailed; mandatory features may expand the soft budget within the 256-actor bound.
Cheap means deliberately simplified primitive realization, not missing-content fallback.

Three compatible primitive shape/color families own finite MultiMeshInstance3D groups. Dense
swap-back rows carry full generational identities; per-row transforms and live transformed bounds
track the F1.8A CPU pose and floating origin. Nameplate overlays can appear without body promotion.
Overhead Chat instead pins Detailed until its original expiry. No per-cheap-actor physics/process
Node is required. Pointer nomination uses displayed pose and retained disclosed ID; Game retains
range and interaction authority.

Promotion prepares a hidden Detailed candidate from current User→Realm→world.local→Primitive
resolution, commits on the engine owner thread, and retires the old row. Failure retains the cheap
body and frees the candidate. There is no pending async transition that can resurrect a removed
actor. Demotion preserves pose/action state, so promotion does not replay an old accepted cue.
Cheap bodies do not retain hidden skeletal visuals. Current-content hot replacement/disable has
real catalog/generation acceptance coverage, not just a synthetic visual provider.

`ClientActorTemporalPresentation` carries the real selected interpolation endpoints, public
versions, receipt timestamps, target time, generation/ID and CPU-resolved pose. Holds/snaps use
equal endpoints. Neither server timestamps nor controlled prediction are fabricated as remote
interpolation. The existing delay, hold/gap/teleport and lifetime policies remain unchanged.
GPU interpolation, skeletal batching and impostors are not implemented. F1.11's decorative
vegetation prototype is separate from actor representation policy.

The far-crowd fixture demonstrates large Node and cold lifecycle savings, but higher moving
owner-thread cost from explicit batch service. This does not prove a global minimum or imply a
server R300/R350 gain. GPU diagnostics remain separate short stationary samples. F1.11 is closed
below; W8 remains unstarted and F1.8A human-feel tails remain explicit dogfooding risks.

## Shared world resources and renderer stress (F1.11)

Local and Remote use `GodotCatalogWorldAssetResolver` through the same immutable presentation
generation Texture/Material leaves and derived store. Remote's authenticated optional palette
uses ordinary passive packages; semantic World/UI activation and exact proof remain independent.
User → Realm → already-authorized world.local → Primitive precedence holds. Compatible texture
updates replace materials on retained meshes, not semantic chunks/actors or native instance groups.
Availability changes may rebuild only bounded native realization. Scope leases and derived native
resources retire before the cache; persisted CAS/derived artifacts are not live session ownership.

The [F1.11 evidence](../../_Benchmarks/Baselines/client-f1.11-renderer-stress-vegetation-2026-09-25.md)
records production residency/window/SubViewport consumers with 10,000 objects and 256 disclosed
actors, actual nameplate reveal, stock cameras, distance/pixel controls and cold/warm resources.
Decorative grass is explicitly opt-in/default Off: one batch per chunk, one patch per present
Grass base cell, eight blades sharing its exact sampled root, 96 finite shader-generated vertices.
Base void remains void even beneath an upper surface; additional surfaces are not scatter sources.
Shader TIME drives wind; one custom-data row carries debug cut, with no Game cut/regrowth semantics.
Native finite AABB/frustum, horizontal/vertical rebase and deterministic re-entry pass. Collapsed
geometry still submits vertices. Finite debug sun/time/ambient palettes are not advanced GI or
authoritative weather. NodeMultiMesh/PerRow and conservative actor tiers remain unchanged.

---

# 27. BotW-style grass/vegetation: compact truth, huge presentation

UltraSim should support dense decorative/interactable vegetation without representing each grass blade as an ECS entity.

The intended ownership is:

~~~text
WORLD CONTENT
    where vegetation can exist
    vegetation type/species/density
    deterministic seed/input
          |
          v
GAME AUTHORITY
    sparse mutable cut/regrowth state
    authoritative loot/spawn result
          |
          v
CLIENT CORE
    retained vegetation/cut semantic state
          |
          v
RENDERER
    procedural scatter
    atlas/material
    MultiMesh/instancing
    wind/bend visuals
    draw-distance policy
~~~

The renderer may create thousands of visual blades from a tiny amount of semantic state.

Vegetation is therefore a natural future consumer of the atlas/instancing architecture.

---

# 28. Vegetation representation candidate

The current UltraSim terrain profile is 32x32 cells per 16m chunk, or 0.5m cells.

That makes a cell-oriented vegetation description attractive.

World content might provide a compact type/density plane, or derive part of it from terrain/material.

Actual blade transforms should be generated deterministically from stable inputs such as:

~~~text
hash(WorldSeed, ChunkKey, CellIndex, VegetationType)
~~~

This permits rebuilding identical scatter without persisting every blade transform.

The exact vegetation source format is future implementation detail. Do not put individual grass blades into WorldPack merely to make rendering easy.

---

## 28.1 Shader-generated blade/leaf geometry candidate

One promising renderer realization is to generate plant geometry almost entirely in the vertex shader rather than importing one mesh per plant.

The renderer still submits a finite vertex/index budget. A normal vertex shader cannot emit an unlimited number of new vertices; instead the shared source mesh may contain zeroed/dummy vertices and a fixed strip/list topology. `VERTEX_ID` then determines which conceptual blade, segment, and side each vertex represents and computes its final position procedurally.

Conceptually:

~~~text
one reusable grass-patch source mesh
    fixed maximum vertex/index capacity
    no authored Blender grass model required
          |
          v
vertex shader
    VERTEX_ID
        -> blade index
        -> blade segment
        -> left/right side
        -> connector/degenerate vertex
          |
          + deterministic patch/blade seed
          + terrain/local placement
          + species/density
          + wind/time
          + cut state
          |
          v
final blade geometry
~~~

A blade may therefore be a short vertical triangle strip whose width, height, tip shape, lean, yaw, and bend are derived from stable shader inputs.

Multiple blades can occupy one continuous source strip/list by inserting degenerate connector triangles between blades. Those connector vertices collapse onto neighboring roots/endpoints so the unwanted triangles have zero area and do not rasterize.

This permits one patch mesh to describe many visually independent blades while retaining one material and one submitted mesh topology.

## 28.2 MultiMesh/patch realization

The shader-generated patch can then be instanced spatially.

A practical first candidate is:

~~~text
terrain/render chunk
    -> one grass MultiMesh
         -> one instance per vegetation cell or small patch
              -> shared procedural grass-patch mesh
              -> shared atlas/material
              -> per-instance transform
              -> per-instance seed/density/type/cut state
~~~

For the current 32x32-cell terrain chunk, one-instance-per-cell yields at most 1,024 grass patch instances in one MultiMesh. Each instance may procedurally expand into several blades in the vertex shader.

This is not the only possible granularity. A future benchmark may select one instance per smaller/larger patch, one procedural mesh per render chunk without MultiMesh, or a hybrid.

The important rule is spatial locality: MultiMesh is spatially treated as one object, so enormous world-wide grass MultiMeshes would defeat useful frustum/occlusion work. Grass should be grouped into terrain/render chunks or smaller spatial cells so whole groups can enter/retire with ordinary client residency and culling.

Because vertex-shader displacement may extend far beyond a zeroed/dummy source mesh, every procedural grass group needs a conservative explicit native AABB covering maximum height, bend, and wind displacement. Do not rely on the dummy source geometry's bounds.

## 28.3 Feeding cut state to the shader

Cutting should change small retained state, not rebuild mesh topology.

For coarse cell-level cutting, one MultiMesh instance can represent one vegetation cell and expose a simple cut value through per-instance custom data:

~~~text
INSTANCE_CUSTOM
    x = cut / active amount
    y = deterministic seed or density
    z = vegetation type/profile
    w = future presentation parameter
~~~

When the authoritative cell changes from uncut to cut, the client updates that instance's cut state. The vertex shader then collapses every blade vertex in that patch toward its root, producing degenerate/invisible geometry without adding/removing vertices or rebuilding indices.

If gameplay/visuals later require finer-than-instance cutting, the renderer may instead use a small cut-state texture/buffer indexed by patch/blade/subcell. Godot 4.7 provides runtime-updatable textures, including GPU-modifiable DrawableTexture2D, so a tiny mask atlas is a plausible native mechanism; the architecture must not depend on that experimental API specifically.

The semantic authority remains independent of the representation. The server may retain a 32x32 cell cut mask while a renderer maps one authoritative cell to many visual blades.

## 28.4 Important performance caveat: collapsed is not free

Collapsing a blade removes its visible/rasterized area, but the vertex shader still executes for the submitted vertices.

Therefore:

~~~text
cut update cost
    very small

fragment/overdraw cost after collapse
    approximately disappears

vertex-processing cost after collapse
    remains until the containing patch/instance is no longer submitted
~~~

That is usually acceptable for sparse cutting and regrowing vegetation, but a player mowing an entire field could leave large amounts of invisible vertex work.

An optional second-stage optimization may retire or compact fully cut patches:

~~~text
patch becomes completely cut
    -> swap/remove it from the active instance prefix
    -> lower visible instance count or rebuild a bounded MultiMesh buffer later

regrowth
    -> reinsert patch
~~~

This maintenance path can occur much less frequently than individual cut events and remains presentation-only.

Do not implement compaction until measurement shows collapsed-vertex cost matters.

## 28.5 Expected GPU bottlenecks to measure

The procedural approach deliberately trades CPU objects/draw calls for GPU work.

Likely costs to measure are:

- total submitted vertices per visible chunk and per shadow pass;
- vertex-shader hash/trigonometric/wind work;
- alpha/cutout overdraw and fragment cost;
- shadow casting, which can repeat grass vertex work;
- number and spatial size of MultiMeshes;
- conservative AABB/culling effectiveness;
- cut-state update cost;
- atlas/material binding count;
- near/far vegetation density/LOD.

A rough example illustrates why density must remain tunable:

~~~text
32x32 cells = 1,024 patch instances/chunk

8 blades/cell * 6 submitted vertices/blade
    = 49,152 vertices/chunk/pass

16 blades/cell * 6 submitted vertices/blade
    = 98,304 vertices/chunk/pass
~~~

That can still be entirely reasonable on modern hardware, especially with chunk culling and client vegetation distance, but shadows, several visible chunks, mobile GPUs, and heavy fragment overdraw can dominate before draw-call count does.

The correct acceptance criterion is measured whole-frame cost across representative hardware, not merely "one draw call."


# 29. Vegetation rendering

Vegetation may use:

~~~text
grass/flower atlas
    ->
one/few compatible grass meshes/cards
    ->
MultiMesh/instanced groups per render chunk
    ->
wind/bend shader
~~~

Thousands of blades should collapse to a small number of draw/material groups.

Grass is presentation-heavy and authority-light.

---

# 30. Authoritative cutting and loot

The renderer must not report "this visual blade was cut" as authoritative truth.

An accepted Game action/swing determines the authoritative affected region.

~~~text
accepted swing/action
    -> authoritative swept/affected X/Z region
    -> find interactive vegetation cells
    -> for each previously uncut cell:
         mark cut
         perform cut gameplay result
~~~

A sparse per-chunk cut mask is an attractive first representation.

At 32x32 cells:

~~~text
1024 bits = 128 bytes per modified chunk
~~~

Only chunks with modified interactive vegetation need mutable state.

The important transition is:

~~~text
uncut -> cut
~~~

Only that transition may trigger loot/spawn work. Repeatedly attacking already-cut grass must not duplicate a grasshopper/item.

Only resulting spawned items/creatures need ordinary authoritative entity identity.

---

# 31. Deterministic vegetation loot

If vegetation initially does not regrow, loot may derive deterministically from stable identity:

~~~text
hash(
    PersistentWorldId,
    ChunkKey,
    CellIndex,
    vegetation/gameplay salt
)
~~~

The result may be nothing, a grasshopper, seed, or another future item.

Save/load therefore cannot reroll the same cut by repeatedly restoring.

If regrowth later exists, add a stable growth generation/ordinal:

~~~text
hash(chunk, cell, growthGeneration, salt)
~~~

Each new growth cycle receives a new deterministic opportunity.

---

# 32. Grass rendering may be disabled independently of grass gameplay

Client setting candidate:

~~~text
VegetationDistanceChunks
    0 = do not realize vegetation
    1..N = render within client-selected distance
~~~

Future client settings might include vegetation density, quality, or shadows.

These are presentation/resource decisions.

Authoritative vegetation gameplay is independent:

~~~text
client vegetation distance = 0
    !=
server vegetation gameplay disabled
~~~

A player who disables grass rendering may still swing through an authoritative grass region and cause a valid server-side cut/loot result.

A server/world may separately disable interactive vegetation or contain no interactive vegetation definition.

Pure primitive mode may omit cosmetic grass or render a very cheap diagnostic marker for interactive vegetation, while authoritative cut/loot semantics remain intact.

Vegetation need not block PF6 unless explicitly selected for the Concept Demo, but this ownership/settings seam should be preserved now.

---

# 33. Current implementation state and remaining gaps

The current client is a strong semantic/integration proof with the first shared presentation
composition. Camera projection/render-target and scalable-renderer work remain deliberately later.

## UI routing

ClientRoot creates persistent Workspace, World, HUD, Menus, Debug, and Context/Tooltip layers.
Local and Remote session leases route world overlays, HUD/Chat/banner, gameplay gumps, and transient
tooltip/context presentation through the same hosts. The shell menu host is independent from the
gameplay-gump host.

## Window/gump management

The generic Godot UI renderer delegates native compiled-view ownership to the bounded gameplay-gump
manager. Multiple modeless views coexist with deterministic cascade, bring-to-front, native drag,
application clamping, and exact native retirement. Remembered placement is not yet implemented.

## Debug/context behavior

The Debug layer has one finite Off/Client/Presentation/Content cycle. Context/Tooltip owns the
bounded disclosed-world-target menu and hover tooltips. This does not yet attempt arbitrary
server-defined context actions or an admin/debug framework.

## Local content packaging

F1.4 closes the actor-art product boundary. Canonical X/Y GLBs remain build inputs outside Godot;
the deterministic Official archive is an external deletable installation for Single Player and
Complete, and Online remains physically contentless. Removing that directory does not change the
binary and selects Primitive. Installed content reaches Godot only after exact package validation,
Local authorization, CAS publication, catalog leasing, and presentation-generation selection.

## Content-cache trust/product generalization

The persistent cache now implements Local and Realm authorization scopes plus a deliberately
separate user-pack policy namespace. All three reuse one global immutable blob CAS; possession in
one does not authorize another. User pack selection is strict client-local state, never a Realm
plan, semantic activation, or application proof.

## Atlas

F1.6 implements a bounded diagnostic `WorldOpaque` two-texture candidate after logical resolution.
It proves deterministic keying, opaque compatibility, one-page pixel equality, and ownership, but
is rejected as the production Cabin policy because it did not reduce the fixture's materials or
demonstrate a draw-call reduction. Standalone derived Texture/Material resources remain selected.
The atlas remains a renderer strategy and never enters logical content identity.

## Local/Remote presentation duplication

Local and Remote Godot clients separately construct significant HUD/Chat/presentation structure. This was useful acceptance scaffolding and has already drifted.

The client-completion pass should move genuinely common presentation ownership toward shared adapter/client presentation code without inventing a premature universal framework.

---

# 34. Pre-PF6 client-completion campaign

The exact checkpoint number/name is not frozen. Treat this as a deliberate **Client Presentation Readiness / Client Architecture Completion** campaign before external PF6 validation.

Recommended workstreams follow.

## A. Presentation composition

- make Local and Remote use the intended category roots;
- add Context/Tooltip category;
- preserve engine-neutral focus policy;
- reduce duplicated Local/Remote HUD/window composition where genuinely common.

## B. Gump/window manager

- route semantic UI to Menus;
- support multiple appropriate views;
- bring-to-front;
- native drag/clamp/focus/modal policy;
- exact remove/free on close;
- no hidden authoritative/UI lifetime.

## C. Primitive substrate

- make primitive fallback intentionally permanent;
- support diagnostic checkerboard/grey modes;
- define actor/item/static/Multi fallback realization;
- ensure missing animations never block accepted gameplay.

## D. Local content productization

- remove high-quality Official presentation from baked renderer products;
- ship/install it as ordinary Local content;
- make it independently removable;
- keep local world structural packages independently understandable from presentation packages.

## E. Four-layer resolver

- Primitive -> Local -> Server -> User composition;
- User -> Server -> Local -> Primitive presentation fallback;
- preserve server authorization independently from global blob deduplication.

## F. Atlas/derived resources

- finite atlas compatibility groups;
- stable logical-ID-to-region mapping;
- deterministic cache keys;
- renderer/platform-specific derived realization;
- semantic presentation references with role-specific fallback;
- bounded 3D-derived UI portrait generation/cache;
- optional bounded live portrait / inspection realization;
- permit derived portraits to join compatible UI/Icon atlas output;
- measure draw calls/material groups and startup/build cost.

## G. Client profile expansion

- generalize narrow camera enum/parameters into a bounded declarative rig;
- stock presets and user-created named profiles use the same schema;
- preserve user settings/profile layering;
- no server camera/input code.

## H. Developer dogfooding

- developer plays the complete client extensively;
- classify every finding as:
  - architecture/mechanical defect;
  - missing product behavior;
  - presentation polish;
  - deliberately deferred feature;
- fix architecture/mechanics before PF6;
- do not confuse developer dogfooding with external PF6 human validation.

Vegetation can be implemented later unless deliberately selected for the Concept Demo. Its seam should remain compatible with the above architecture.

---

# 35. Acceptance scenarios for client readiness

## Primitive-only product

1. Install/launch the renderer product.
2. Remove/disable all removable Official Presentation packages.
3. Enter a structurally installed local world.
4. Actors/items/statics/Multis with known semantic bounds still receive primitive realization.
5. Movement, collision, interaction, Inventory, Chat, UI, and authority continue.
6. Missing high-quality presentation is obvious rather than silently wrong.

## Official Local content

1. Install Official content separately from the executable.
2. Launch the same executable.
3. Local activation resolves Official world/presentation.
4. Native atlas/resources build or reopen.
5. Delete Official Presentation.
6. Relaunch and verify primitive fallback without replacing the executable.

## Server content

1. Connect to Realm A and install shared blobs.
2. Connect to Realm B using some identical package/blob hashes.
3. Verify identical physical blobs are not duplicated.
4. Verify Realm B cannot claim Realm A authorization merely because bytes exist in CAS.
5. Verify server Add/Replace wins above Local content where allowed.

## User resource pack

1. Apply Server content.
2. Enable a user resource pack.
3. Verify compatible user presentation overrides Server/Local.
4. Disable/remove pack.
5. Verify fallback returns to Server, then Local, then Primitive without semantic reset.

## World content

1. Enter the floating Concept Demo WorldPack as Local content.
2. Enter equivalent/different remote WorldPack through authenticated Server content.
3. Verify renderer uses ordinary world/content binding in both cases.
4. Remove presentation package while retaining structural world package and verify primitive world realization remains playable where enough structural facts exist.
5. Remove required structural world package and verify entry fails closed rather than substituting unrelated terrain.

## Atlas

1. Same resolved catalog + same atlas policy yields identical logical region map.
2. Changing one texture invalidates only the appropriate derived identity.
3. Add/Replace/resource-pack precedence resolves before atlas mapping.
4. Multiple objects/materials share finite atlas pages where compatible.
5. Heterogeneous geometry is not incorrectly forced into one Godot MultiMesh.
6. Record draw/material-group evidence rather than assuming atlas construction is always faster.

## UI layering

1. Nameplates/overhead remain below HUD.
2. HUD remains below Inventory/gumps.
3. Debug remains visible above ordinary menus according to active page policy.
4. Context menus/tooltips remain usable above every lower category.
5. Closing a gump removes it from the active native tree.
6. Multiple allowed gumps maintain deterministic within-category z/focus behavior.
7. Tooltip visibility never grants Game authority.

## Camera/control profile

1. Remote realm advertises one declarative profile.
2. Client can Use Server Defaults, Keep My Controls, Customize, or Ignore.
3. No recommendation rewrites global user settings.
4. Named stock and equivalent custom profiles resolve to equivalent bounded rig values.
5. Unsupported/out-of-range fields fail closed.
6. Modified-camera possibility is not treated as a disclosure/security guarantee.

## Renderer independence

Long-term, run equivalent semantic/content fixtures through at least two adapters and require equivalent client/Game semantics despite different native resources.

---

# 36. Permanent rules

~~~text
CLIENT IS PRESENTATION, INPUT, CACHE, AND LOCAL POLICY - NOT GAME AUTHORITY.

SINGLE PLAYER IS A LOCAL AUTHORITY TOPOLOGY, NOT A DIFFERENT CLIENT SEMANTIC MODEL.

PRIMITIVES ARE PERMANENT FALLBACK IMPLEMENTATION, NOT AN OPTIONAL ART PACK.

OFFICIAL/LOCAL HIGH-QUALITY CONTENT MUST BE REMOVABLE WITHOUT REPLACING THE EXECUTABLE.

SERVER CONTENT AUTHORIZATION IS REALM-SCOPED EVEN WHEN PHYSICAL BLOBS ARE GLOBALLY DEDUPLICATED.

USER RESOURCE PACKS ARE PASSIVE CLIENT PRESENTATION OVERRIDES, NOT EXECUTABLE SERVER CODE.

CONTENT INCLUDES WORLD/MAPS/STATICS/MULTIS/UI/PRESENTATION - NOT ONLY TEXTURES AND MODELS.

MISSING PRESENTATION FALLS BACK; MISSING REQUIRED WORLD TRUTH FAILS CLOSED.

LOGICAL CONTENT IDENTITY SURVIVES ATLASING AND NATIVE RESOURCE REALIZATION.

ATLAS/MULTIMESH/COMBINED-MESH CHOICES ARE PRESENTATION OPTIMIZATIONS, NOT AUTHORITY.

GODOT/UNITY/UNREAL-SPECIFIC RESOURCES ARE DERIVED REALIZATION, NOT PORTABLE CONTENT TRUTH.

SERVER CAMERA/CONTROL PROFILES ARE DECLARATIVE DATA, NOT SCRIPTS.

THE USER OWNS FINAL LOCAL BINDINGS/PREFERENCES SUBJECT TO NORMAL GAME/AUTHORITY RULES.

CAMERA LIMITS ARE NOT A SECURITY OR DISCLOSURE BOUNDARY.

UI Z-ORDER CATEGORIES AND INPUT/FOCUS OWNERSHIP ARE RELATED BUT SEPARATE.

CLOSED UI/GUMPS RETIRE FROM THE ACTIVE TREE; SELECTIVE POOLING IS RENDERER-ONLY.

DENSE VEGETATION MUST NOT BECOME ONE ECS ENTITY PER BLADE.

CLIENT VEGETATION RENDERING MAY BE DISABLED WITHOUT DISABLING AUTHORITATIVE VEGETATION GAMEPLAY.
~~~

These rules should survive changes to renderer, package layout, atlas implementation, native UI technology, platform, and future game content.

---

# 37. What this document deliberately does not freeze

This document does not yet freeze:

- exact filesystem directory names;
- exact Local package split;
- exact atlas page dimensions;
- exact number of atlas compatibility groups;
- exact GPU compression formats;
- whether canonical atlas layout is generated by deployment tooling or entirely by clients;
- exact camera-profile wire schema/version;
- exact UI window-manager API/class names;
- exact grass/vegetation WorldPack representation;
- exact vegetation regrowth timing;
- exact user resource-pack capabilities beyond passive presentation;
- executable client extension policy beyond the already separate future boundary.

Those choices should be made at implementation checkpoints with measurements and concrete consumers.

---

# 38. Definition of client-architecture readiness for PF6

PF6 external human validation should begin from a client whose major architecture is intentional rather than accidental.

At minimum:

- Official high-quality Local presentation is no longer inseparably baked into the renderer product;
- primitive-only fallback is a tested supported mode;
- Local and Server content use one coherent content/resolution model;
- global blob deduplication and realm authorization remain intact;
- UI category/z-order routing matches the intended World/HUD/Menus/Debug/Context model;
- ordinary gump close/lifetime behavior is correct;
- Local and Remote presentation no longer disagree about basic UI ownership;
- declarative server profile handling has a clear extensible camera/control rig boundary;
- atlas/derived-resource ownership is implemented or deliberately bounded enough that client rendering is not knowingly freezing the wrong per-texture/material architecture;
- developer dogfooding has found and classified obvious mechanical/presentation defects;
- remaining known shortcomings are genuinely polish or explicitly deferred features.

Then PF6 can answer the questions humans are uniquely useful for:

~~~text
Does movement feel responsive?
Does interpolation look correct?
Does the selected camera/control profile feel coherent?
Do gumps and focus behave naturally?
Does Chat feel good?
Does dense presentation remain readable?
Do lower cadence/bandwidth profiles still feel acceptable?
Are there synthetic-vs-human discrepancies?
~~~

PF6 should not be the first time the project discovers that Inventory lives in the wrong z-layer, the default art cannot be removed, or Local and Remote compose fundamentally different presentation trees.

That architectural work belongs here.
