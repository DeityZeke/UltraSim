# UltraSim Client Presentation, Content, and Renderer Architecture

**Status:** architecture/design direction for the pre-PF6 client-completion pass.

**Relationship to existing plans:** this document is the durable client/presentation/content companion to [CLIENT.md](CLIENT.md), [ASSET_STREAMING.md](ASSET_STREAMING.md), [TERRAIN.md](TERRAIN.md), and [CONCEPT_DEMO.md](CONCEPT_DEMO.md). CLIENT.md remains the checkpoint/status roadmap. This document owns details that are easy to lose when a vertical slice proves only that something can be rendered.

**Implementation campaign:** [CLIENT_COMPLETION_PLAN.md](CLIENT_COMPLETION_PLAN.md) turns this architecture into the ordered F1.0-F1.12 pre-PF6 execution plan, with a dedicated F1.8A remote-motion checkpoint between F1.8 and F1.9.

**F1.2 implementation boundary:** the current Concept Demo now proves WorldPack and compiled
Inventory/Server Monitor UI through one exact Realm semantic activation, while an independent
authenticated message-149 plan authorizes X/Y actor art. Actor-art acquisition is non-gating.
An engine-neutral, immutable `PresentationGeneration` selects compatible explicitly enabled User
art, then Realm art, `world.local`, and renderer Primitive per logical actor ID. Its process-local
generation fences late native work; its effective-selection fingerprint is stable content identity.
F1.5 persists a bounded exact user-pack order in a separate policy namespace and shares only
immutable CAS blobs with authorization scopes. Derived caches remain a later F1 checkpoint. F1.3 supplies
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

F1.5 implements the first intentionally narrow member of this family: up to sixteen explicitly
installed/enabled packages, ordered low-to-high priority, containing only bounded inert
ActorPresentation GLBs declared as Replace. Exact package/manifest identities and enabled order
persist under `resource-packs/`; they do not enter Realm or `world.local` activation. Missing
logical targets are inactive, while corrupt or skeleton-incompatible selected actor replacements
reject the complete candidate generation and preserve the current one. WorldPack, UI definition,
texture, script/shader/executable, Add, and all other domains remain rejected. Texture/material
overrides await the F1.6 presentation/derived-resource seam because current Cabin materials are
renderer-constructed rather than common-generation leaves.

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

## Future application workspace and Gameplay Frame (F1.7-F1.9)

The physical Application Window is not the gameplay viewport. A selected Gameplay Output Resolution (default 1920×1080, with bounded lower/higher/custom choices) determines render-target pixels and aspect. The Gameplay Frame is the application-space rectangle displaying that output: it defaults to maximum aspect-preserving fit inside the OS window, may later snap to bounded sizes, cannot exceed the window, and has no internal letterbox merely to retain 16:9. Unused window area is a dark application/UI workspace. A 480×270 output scaled into a 1920×1080 frame is still the same 16:9 view at lower fidelity; changing pixel count alone never expands world visibility.

The frame is below normal UI. The intended order is workspace/background, Gameplay Frame, frame-associated World overlays, HUD, Menus/Gumps, Debug, then Context/Tooltip. Nameplates, health bars, overhead Chat, and markers track world positions in the frame; ordinary gumps can cover the frame or occupy unused workspace, supporting desktop-style layout. Frame host/shared UI composition belongs to F1.7; F1.8 owns frame-aware camera projection; F1.9 makes Godot render to the frame/SubViewport rather than implicitly to the OS window. None is implemented by F1.3.

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

The current F0.1 shell has World/HUD/Menus/Debug roots. The client-completion pass should add/define the top transient Context/Tooltip category and make Local/Remote gameplay actually route surfaces through the intended roots.

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

A client window/gump manager still needs to own native presentation policy such as:

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

Current one-host/center-every-definition behavior is accepted vertical-slice scaffolding, not the final window manager.

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

The desired Debug interaction is a finite local cycle, for example:

~~~text
F10:
    Off
    -> Performance
    -> World/Render
    -> Network
    -> Content/Cache
    -> UI
    -> optional Admin/Authorized
    -> Off
~~~

Exact pages/key are configurable. The behavior is a client-local finite diagnostic state machine, not server code.

Likewise, "ContextMenu" must eventually mean actual presentation/context behavior rather than only being a convenient right-click cancellation gesture.

Current code still uses the ContextMenu semantic input as OrbitCommand stop/cancel behavior and has no complete first-class context-menu presenter. That is a known client-completion gap.

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

## Future remote motion presentation (F1.8A)

Remote motion needs a dedicated presentation checkpoint after F1.8 and before F1.9. The controlled actor may predict its locally submitted command for responsive display, then correlate bounded command/prediction history with authoritative owner state, reconcile error, smooth bounded corrections, and snap/reset on discontinuity, teleport, relocation, or session/generation replacement. Prediction never becomes Game truth. Non-controlled remote actors instead begin with a bounded authoritative-snapshot interpolation buffer, a presentation timestamp/delay, and explicit gap, stale, teleport, and entity-replacement behavior; extrapolation needs separate evidence. Diagnostics must expose correction magnitude and buffer age/health. Packet-free Local authority does not manufacture network latency or prediction debt merely for symmetry.

F1.8A selects the pose to present now. F1.10 may later realize that pose through cheaper actor tiers or GPU interpolation. PF6 must not claim remote prediction/reconciliation/interpolation quality before F1.8A passes real latency/impairment evidence. F1.3 documents this only.

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

# 33. Current implementation gaps this pass must acknowledge

The current client is a strong semantic/integration proof but not yet the final presentation composition.

## UI routing

ClientRoot currently creates persistent World, HUD, Menus, and Debug layers, but Local and Remote gameplay do not consistently route surfaces through them.

Examples in the current Godot implementation:

- Local Inventory is parented under the local HUD root.
- Local hover tooltip is under the World UI root.
- Remote gameplay is configured primarily with one HUD layer/root.
- Remote server UI, Chat, System banner, and hover tooltip share that HUD root.
- no dedicated Context/Tooltip layer exists.

The intended category/z-order model therefore exists as shell scaffolding, not end-to-end behavior.

## Window/gump management

The generic Godot UI renderer creates a compiled view, adds it to one host, and centers it.

There is not yet a complete native window manager for multiple non-modal gumps, bring-to-front, drag/clamp, category routing, remembered placement, or context layering.

## Debug/context behavior

The Debug layer is currently skeletal.

ContextMenu is currently also used as OrbitCommand stop/cancel input and has no complete generic context-menu presentation system.

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

No V3 atlas implementation currently exists.

Atlas identity/layout/native realization is a real pre-PF6 client architecture task, or must be deliberately bounded enough that the client is not knowingly freezing the wrong one-texture/material-per-object presentation architecture.

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
