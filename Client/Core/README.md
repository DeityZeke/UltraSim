# Client Core

Client Core owns retained engine-neutral client projections. UI retention uses
the neutral UI kernel. Terrain T0.8 adds `ClientWorldSemanticCache`, the one
production semantic world cache consumed by presentation.

A0.6 permits an exact already-validated passive UI definition to enter the bounded
`UiClientRuntime` cache before a view opens. Publication retains the existing definition-family
supersession rules and does not publish model values or action handlers. A subsequent semantic
`HotOpen` still carries the Game-owned model and must match that exact cached definition.

Client F0.5 closes this source-convergence seam. One explicitly selected stock
Inventory definition may preseed the same cache at composition time, while a reopened content blob
or newly acquired `.usui` may seed it after exact validation/authorization. All sources converge on
one `UiDefinitionCacheKey`; no source changes model/action authority, and session disposal still
retires the complete runtime cache and its views.

Because independently decoded canonical artifacts are distinct objects, `UiHotOpenView` can clone
dynamic state against another definition instance only when its complete compiled handle matches.
Reference identity is not semantic identity; a different key, version, or hash remains rejected.

F0.1 also places the application-shell state and local settings model here. The owner-thread-bound
`ClientShellCoordinator` owns Main Menu/loading/session/options/settings/error transitions, while
`ClientInputFocusPolicy` produces one finite World/HUD-text/modal/debug focus decision for engine
adapters. Reserved application controls remain available without allowing ordinary gameplay input
to leak through a text field or modal surface.

F1.9A adds a user-global `ClientDisplaySettings` to settings schema v5. Simple authoring has finite
exact 16:9, 16:10, 4:3 and 5:4 resolution tables; it locks Application Resolution, Gameplay Frame,
Gameplay Output and internal 3D pixels together at 100%. Advanced authoring allows bounded custom
Application/Frame/Output dimensions and 50–100% internal 3D scaling. Both resolve through one
`ResolvedClientDisplayConfiguration`, not different renderers. Output and Frame must have exactly
the same reduced aspect; native dimensions are capped at 4096 per side and 8,294,400 pixels.
v1–v4 settings migrate to Simple 1920×1080 without discarding controls or scoped profiles. Display
settings are never server-recommended or realm-scoped.

`GameplayFrameLayout` is renderer-neutral: it supports full, max-fit and centered custom frames,
half-open pointer admission and application→frame→output mapping. F1.8's `ClientCameraRig` and
`ClientCanonicalViewProjection` still own finite camera meaning and the 16:9 canonical envelope.
Pixel count changes fidelity, not World visibility. Godot alone owns the output SubViewport.

F1.9B compares NodeMultiMesh and direct-RID realization entirely below the existing semantic cache
and presentation-window seam. Core owns no renderer-backend choice, native slot map, RIDs, upload
buffers or Godot resources. Public identity/picking and cache hashes do not change with a native
group representation. The measured production path retains NodeMultiMesh/per-row uploads; W8
live composition changes remain unstarted.

F1.10 adds `ClientActorRepresentationTier` (Detailed/BatchedPrimitive) and a bounded, deterministic
client-local selector. Its conservative stock policy uses a soft 64 Detailed budget, 20 m promotion
and 28 m demotion hysteresis; mandatory controlled/equipment/cue/overhead facts may expand the soft
budget within 256 actors. Names do not imply body tier. Core owns no native hierarchy or batch row.
`ClientActorTemporalPresentation` describes the real receipt-time bracket chosen by the unchanged
remote interpolator, including exact versions/generation/ID and the CPU-resolved pose. The legacy
sampling API delegates once. Holds/snaps have equal endpoints; no server timestamps, speculative
remote extrapolation or GPU interpolation were introduced. Controlled prediction is unchanged.

F1.11 adds `ClientVegetationScatter`, a bounded presentation-only consumer of existing resident
BaseTerrain. It hashes fixed world seed/full spatial key/cell/profile and samples the canonical
triangle height for one root per present Grass cell (maximum 1,024/chunk). Base intentional void
is excluded even under an additional grass surface; additional surfaces are deliberately not
scatter sources. It changes no semantic hash, entity identity, collision, persistence or protocol.
No per-blade mutable authority or native state belongs to Core. The native opt-in consumer and
actual stress evidence are documented in [Godot](../Godot/README.md).

F1.8A adds `ClientMotionPose`, `ClientControlledMotionPresentation`, and
`ClientRemoteActorInterpolation`. These are renderer-neutral, bounded presentation state beside
exact semantic owner/public records. The controlled controller accepts only sequenced semantic
commands admitted by the remote client; it retains 128 history slots, prunes on the existing exact
owner acknowledgement, caps stock speculative horizontal lead at 900 mm, and resets on source or
identity changes. Its finite local `ClientControlledMotionPolicy` is an approximation, not a Game
movement entitlement. Game alone resolves collision, jump, fall, landing, and interactions.
Other actors use 12 authoritative public-version samples each (maximum 64 actor buffers), indexed
by client monotonic receipt time with 100 ms delay. Receipt time is not server simulation time;
the interpolator never extrapolates and holds if new truth is absent. This Core state owns no
Godot nodes or per-actor frame allocations.
The F1.8A-R1 regression now requires idle and previously blocked presentation to converge when
owner truth moves: only a real aligned lead may be consumed by authority displacement. A vertical
public relocation beyond the 3D discontinuity threshold snaps rather than becoming a slow Y lerp.
The original motion measurements remain historical; the corrective case and lingering rapid-
reversal quality tail are recorded in the F1.8A-R1 baseline.

W7 groundwork adds `ClientPreparedStructuralWorld`, an owner-thread source for publishing a
different immutable prepared-Pack subset under the same manifest object, Space, and composition
revision. `ClientWorldSemanticCache` can adopt that subset without replacing its semantic handles;
it refuses to retire a Pack with an active semantic slot. `ClientStructuralResidency` can rebuild
its bounded authored-center index after the publication. These are client-local preparation seams,
not authoritative composition mutation or a completed Remote demand-acquisition flow.

`ClientSettingsDocument` is a bounded, versioned, engine-neutral user document. Resolution layers
built-in defaults, global user choices, an explicitly accepted declarative server recommendation,
and a scoped user override. Server recommendations are deliberately absent from persistence, cannot
replace reserved application actions, and cannot carry local audio/accessibility preferences. The
opaque `ClientSettingsScopeKey` is not a hostname or Network endpoint. F0.3a maps a verified
`ApplicationRealmId` to `realm-<32 lowercase hex>` only after the pinned Hosted session authenticates
it. Core's cold `ClientSettingsFileStore` owns atomic platform-local JSON persistence;
the one Godot application assembly owns physical event translation.

F0.3c adds a second, deliberately narrower durable document for client-profile consent. It stores
only the verified `ApplicationRealmId`, exact profile revision/hash, and selected application mode;
the server recommendation itself remains transient. Exact identity reconnect can therefore skip a
repeat prompt without turning endpoint text, cached bytes, or an earlier application session into
profile authority. A changed revision or hash has no matching decision and returns to consent.
Core's existing resolver remains the only precedence oracle: built-in defaults → global user
settings → accepted recommendation → realm-scoped user override. `Keep My Controls` omits only the
recommended physical mappings, and client-reserved controls can never be displaced.

F0.3a also adds `ClientServerTrustRegistry` and `ClientServerTrustFileStore`. The bounded registry
retains the deployment public key, SHA-256 fingerprint, verified realm, and last authenticated
endpoint hint. Same-endpoint or same-realm key conflicts reject, invalid JSON falls back to an empty
registry, and replacement requires an explicit forget operation. Candidate discovery and Network
lifecycle remain outside Core.

The world cache is owner-thread bound and fixed-capacity. Its limits describe
client-resident chunks and rows rather than the server's ECS/public address
space. It copies immutable base terrain/statics/Multi fragments from either a
legacy bound `WorldPack` or W5's canonical `PreparedWorldComposition`, then
exposes W6's read-only detailed chunk status only for composition-backed sources.
Legacy one-Pack sources cannot claim manifest authorship/residency state; staging
still rejects missing or unprepared chunks instead of synthesizing empty pages.
The cache then
transactionally applies chunk-bound publication
snapshot/delta semantics. Client-local handles are generation fenced; public
generation/version knowledge is global across resident chunk pages.
Actual snapshots require valid chunk-bound cursors; a default cursor is admitted
only for an empty initial base stage. Equal revision is idempotent only when the
post-supersession semantic page is identical. Known state flags and change
revisions are validated before mutation. A bounded public-index lookup, resident-
location hint, and touched-slot list keep public application shaped by changed
client-resident state rather than a scan of all resident chunks or all retained
identity knowledge. The resident `SpatialChunkKey` index closes linear-probe gaps
when a chunk lifetime retires, so long-running exploration cost follows the current
resident set rather than every chunk ever visited. Its checked table size is admitted
before proportional cache arrays are allocated; lookup/probe counters make that
maintenance observable.

Terrain T1.0 adds the base-surface presence plane to bounded structural semantic
residency. The cache exposes the exact 1,024-bit plane in its borrowed chunk view,
includes non-all-present topology in the semantic hash, and preserves the legacy
all-present hash. Resident terrain mesh preparation therefore omits intentional-
void cells without interpreting them as missing structural data or a material.

Terrain T1.1 admits immutable sparse additional terrain-shell fragments into the
same structural residency transaction. Each slot retains the bounded fragment
count and borrows the exact validated per-chunk WorldPack view; it does not copy
shells into ECS/public semantic rows or create a cell object graph. The semantic
hash covers logical `WorldSurfaceId`, patch bounds, face/closure topology,
cross-chunk continuation, materials, and q5 heights. A configurable per-chunk
fragment limit rejects the complete stage before publication.

`TryBuildResidentAdditionalTerrainSurfaceMesh` materializes one selected fragment
through the engine-neutral World mesh builder into caller-owned spans. Column and
mesh ordering come from actual geometry and canonical chunk/surface identity,
never a numbered vertical layer. Current material/height overlays cannot mutate
the immutable additional-shell topology.

The cache contains no ECS entities, server mutable storage, connections,
packets, Godot nodes, RIDs, or resources. Borrowed chunk views remain valid only
until the next cache mutation. Engine-neutral resident terrain mesh preparation
also routes through this client-world boundary and resolves the same prepared
base-plus-overlay terrain and neighbor halo used for semantic staging. T0.9
allows initial staging from an exact prepared view and retains changed-chunk structural
revision/hash separately from dynamic publication cursor lineage. Live overlay
transfer/reconciliation remains N1 work. Rendering and visibility are
independently owned by the engine adapter.

Structural semantic residency, dynamic public subscription, and current camera
visibility are independent facts. A structurally resident chunk may contain the
verified immutable terrain/static/Multi baseline and zero public records. The
client's world/view radius may therefore shape structural cache capacity without
expanding the server/game-owned public-entity interest entitlement. The
`ForStructuralViewRadius` helper only derives a bounded client capacity profile;
it does not create Network or AOI policy, and the old `512` default is not a
frozen production view distance.

`TryRetirePublicInterest` is the corresponding client application operation for
a Game-owned AOI leave. It clears only dynamic-public knowledge/cursor state for
the exact resident chunk and source epoch. It does not evict the structurally
resident terrain/static page or reinterpret a leave as authoritative chunk
absence. F0.0b's packet-free local bridge and the remote adapters therefore
converge on the same retained semantic cache while keeping transport different.

`TryApplyChunkAbsent` assumes its local/remote bridge already validated the
current request/subscription attempt. The cache fences semantic-handle generation
and publication epoch; same-epoch transport request ordering belongs to the
future N1 bridge rather than Client Core.

`ClientPresentationWindow` owns the engine-neutral desired structural window.
It converts canonical player movement plus resident semantic handles into bounded
`Enter`, `Move`, and `Exit` operations. Planning is explicitly two-phase: a
concrete backend admits the whole operation set, then Core commits the opaque
plan; rejection cancels it without advancing desired membership. Same-center,
crossing, reversal, and reset semantics therefore are not reimplemented by each
engine. Native resources, realized presentation, frame budgets, visibility, and
public-layer refresh remain backend-owned.

The Client Core test suite includes a Godot-free recording backend that exercises
rejection atomicity, local movement, cardinal/diagonal crossings, a second move
while earlier realization is notionally pending, reversal, reset, and bounded
desired membership. This is a portability seam, not a renderer interface or mod
API.

Concept Demo D0.1 adds `ClientStructuralResidency` immediately before that presentation seam. The
bound structural source is the authored/prepared catalog, not the resident set. The controller retains only
the presentation neighborhood plus one bounded structural fringe, transactionally stages all
entrants, and retires exits after presentation admission. Its fixed arrays and semantic-cache
capacity bound the operation; cancellation rolls back newly staged chunks. Dynamic-public debt may
keep an otherwise undesired semantic chunk resident, but Render Distance never changes Game-owned
public interest. D0.1's accepted local canary was authored/resident/rendered `81/25/9` at radius 1
and `81/49/25` at radius 2.

Live Render Distance changes do not create a new semantic application target. The cache is admitted
at current-generation construction for the largest radius supported by the bound authored world;
changing radius replaces only the residency controller and presentation window over that same cache.
This preserves applied dynamic-public rows and their delivery/proof lineage. A replacement authority
generation remains the boundary that earns a fresh semantic cache.

D0.2 admits noncontiguous authored geography. Valid presentation centers are derived from actual
sorted structural chunk keys rather than a global bounding rectangle; fringe planning skips unauthored
coordinates instead of synthesizing blank chunks. A disjoint transition can first commit an empty
desired structural set, allowing the old renderer/native window and structural-only semantics to
retire before the distant set is admitted. The permanent sparse-cluster canary is `162/25/9` at
radius 1 in either cluster and performs exact central → far → central resets without capacity growth.

W5 generalizes that same bounded cache/residency path to a canonical prepared
composition. Sparse authored/prepared chunk enumeration and global structural
queries cross Pack addresses without allocating capacity for the manifest's
total geography. The canonical snapshot exposes manifest fingerprint, composition
revision, and Space; it does not substitute an arbitrary Pack fingerprint for
World identity. A native W5 window retains seam chunks from Packs `(0,0)` and
`(1,0)` together and crosses between them without a cache, renderer, or session
restart. W5R now has the product Local Concept Demo publish a canonical
two-Pack root into this same cache. W7 Remote clients publish bounded prepared subsets of that
canonical manifest without replacing the semantic cache or retained Pack views; the old combined
Pack remains a historical regression artifact.

F0.4 adds the bounded renderer-neutral actor-presentation projection above those already-resident
chunks. `ClientActorDefinitionCatalog` maps finite public prefab vocabulary to presentation kind,
scale, camera/nameplate/effect anchors, and default-label policy without owning Godot resources or
physical body dimensions. `ClientActorPresentationState` joins only visible public records from the
current presentation window with relatively cold `ClientPublicPresentationMetadataState`; it never
requests another chunk, expands AOI, or polls authority. Add/update/remove changes are emitted into
caller-owned spans, and the warmed no-change path allocates zero bytes.

Cold presentation facts are fenced by server generation plus complete generational
`WorldPublicEntityId` and monotonic metadata revision. A bounded per-index identity floor survives
removal, preventing an old name or item property set from attaching to a reused public slot. F0.4
uses this state for actor labels; I0.13 retains a separately flagged, immutable set of at most 16
canonical typed Inspection Property facts for world hover. Contributor and wire order canonicalize
by stable property ID, while a presentation profile independently controls category/row order.
Missing per-instance metadata changes only presentation fallback; it never changes Game identity,
disclosure, or interaction authority.

D0.7a extends that same cold row with a separately flagged actor appearance/action projection. The
equipped `MainHand` value is only a public prefab identity; exact item entity identity, rolls,
durability, and owner Inventory state remain absent. `Interact` and `Primary` are a finite latest-
accepted cue plus the actor's monotonic control sequence. The same row carries only the finite
Game-owned `Grounded`/`Rising`/`Falling` phase needed to distinguish a real landing from an apex or
ceiling contact. Actor realization consumes an existing sequence without replay, and only a newer
sequence may produce a one-shot presentation cue. Horizontal motion remains derived downstream
from committed authoritative positions; animation state is never a new movement or grounding
authority.

`ClientChatState` owns the engine-neutral C0.1-C0.2 recent-feed projection. Spatial, Global, and
authority-originated System facts
are ordered, append-only occurrences rather than latest state: session/server
generation and recipient sequence fence application, exact duplicates are
idempotent, gaps and malformed facts reject, and only a successful apply may be
proved upstream. Its fixed ring may evict an old already-applied presentation row
without changing server application knowledge. Only spatial facts may carry a public anchor;
Global and System facts fail closed if they do. Client Core owns no speech/routed audience,
authoritative history, public-actor residency, overhead-bubble lifetime, or banner timing.

T0.10 adds the first engine-neutral playable input seam. A concrete engine
captures raw axes, action-map state, and focus into `ClientWorldInputSample`.
`ClientWorldInputSequencer` then owns finite-value validation, focus suppression,
unit-circle movement normalization, held-to-pressed edge detection, Q15 command
quantization, explicit control-session epoch identity, and monotonic command
sequencing. Quantization is rechecked against the discrete Q15 unit circle so a
rounded diagonal cannot become an invalid over-unit command. The resulting
`GamePlayerCommand` is shared game/client intent; it contains no key names,
device IDs, cursor mode, Godot event, or transport state. Physical bindings,
mouse capture, controller discovery, and engine action maps remain adapter work.
This is deliberately the smallest proven movement/interact boundary rather than
a general input framework or compatibility API. Refocus restores held movement
immediately but only seeds the held-action baseline; a key held across focus loss
cannot manufacture a fresh edge-triggered interaction until released and pressed
again.

Physics P0.1d adds `Jump` to that same finite action set and binds the shared default to Space.
Core emits only the focus-fenced held-to-pressed action edge; Game decides whether the controlled
actor is grounded and whether the jump is admitted. Vertical velocity, gravity, landing, and fall
recovery never become client input or presentation authority.

PF3 adds `ClientWorldInputScheduler` between semantic sampling and sequencing. Continuous
locomotion is latest-wins, while Jump/Interact/Primary, one-shot requests, and held-to-release or
moving-to-stop transitions enter one bounded ordered obligation ring. The scheduler retains work
across local transport rejection, fences completion by the resulting command sequence, and applies
the authenticated remote recommendation/hard limit through a monotonic token bucket plus
phase-independent rolling burst window. Godot, packet-free Local, and NetworkBots use this same
engine-neutral mechanism; render or producer sample frequency no longer selects command frequency.

The Direct-era raw-sample overload remains a convenience producer: nonzero axes
produce complete world-space `Direct`, while zero axes produce explicit `Stop`.
Control profiles use `ClientWorldCommandIntent` and
`TryCreateSemanticCommand` to select the accepted command vocabulary explicitly:
`Unchanged`, `Stop`, `Direct`, or `Destination`. `Unchanged` lets an adjacent
action command leave retained locomotion alone; it is not a retained authority
mode. The sequencer owns fencing and action-edge formation, not camera/gesture
policy. Command sampling frequency therefore does not define movement speed.

The launchable T0.10 client builds concrete control profiles over that seam.
`OrbitCommandProfile` owns single-click selection, hold-to-steer, release/
escape/right-click stop, and double-click Destination. It is deliberately
mouse-command-only. The engine supplies only button edges, focus, and a
raycast-nominated world point. `ThirdPersonDirectProfile` and
`IndependentMoveLookProfile` are intentionally crude executable canaries: the
former owns camera-relative WASD and proves relative forward rotates in Client
Core rather than Game; the
latter proves movement and look can be nominated in different directions.
These types are T0 evidence, not a generic controller/mod framework.
