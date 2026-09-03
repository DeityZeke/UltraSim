# Godot client adapter

This optional project translates retained Client Core projections into native
Godot presentation. It owns UI controls and, from Terrain T0.8,
`GodotWorldRenderer` resources.

World rendering is engine-thread bound and budgeted. A render chunk owns one
immutable structural layer (terrain plus grouped frozen/static-Multi rows) and
one replaceable public layer. Public movement refreshes only public groups; it
does not rebuild terrain or statics. Terrain cells and static instances do not
gain scene-tree identity. Semantic residency, private render-slot residency, and
visibility are separate states and their slot indices/capacities do not alias.
Expected render/group pressure is rejected before native construction.
`MaximumGroupsPerChunk` counts terrain material surfaces together with static,
Multi-fragment, and public-prefab groups; terrain cannot bypass the configured
presentation-pressure bound.

Terrain T1.0 intentional void is realized by missing base-mesh triangles, not a
transparent material or hidden collision plane. Terrain group admission counts
only materials used by present cells, so a backing material value stored beneath
an absent cell cannot consume presentation capacity.

`Game.World` terrain indices use an engine-neutral mathematical invariant:
`cross(v1-v0, v2-v0)` points toward the semantic front/outward side in UltraSim
coordinates, and supplied normals occupy that same hemisphere. The current
Godot coordinate mapping requires native clockwise order, so `GodotMeshGeometry`
reverses each triangle at this adapter boundary while preserving semantic
normals/tangents. A future engine adapter must account for its complete coordinate
and handedness transform; it must not copy “reverse every triangle” as a generic
rule. Godot-local fallback primitives use the same helper to emit native winding
plus explicit outward normals directly.

Presentation membership and semantic refresh are separate operations.
`TryEnterPresentation` is residency-owner work and may reverse a pending
retirement. `TryQueuePublicRefresh` only refreshes an already desired/rendered
public layer; it never enters presentation, changes visibility/offsets, or
cancels retirement. A late public update for an outgoing chunk is therefore
discarded as presentation work while its semantic truth remains in Client Core.

Client Core's `ClientPresentationWindow` is the X/Z desired-membership
coordinator. `GodotSlidingRenderWindow` is now only the thin transactional bridge:
it asks Core for a complete operation plan, admits that plan to the Godot
renderer, then commits or cancels it. Godot does not recalculate the radius,
candidate set, semantic-handle diff, reversal, or reset policy.

The Core window's discrete center follows the player's canonical home chunk
immediately, while the common Godot renderer root tracks
`-playerLocalX/-playerLocalZ`. A one-chunk slide therefore retains the overlap,
moves its signed `ClientPresentationOffset`s, retires only the outgoing strip,
and admits only the incoming strip. It never changes absolute `SpatialChunkKey`,
`ClientSemanticChunkHandle`, publication cursor, or semantic hash. These four
identities remain independent:

```text
absolute SpatialChunkKey
client semantic handle/lifetime
transient signed ClientPresentationOffset
private reusable Godot render slot
```

The window requires incoming structural semantics to be resident before it
queues presentation work. That does not broaden public entity subscription:
structural view distance, public interest, and current camera visibility have
separate owners. `MaximumRenderChunks` is concurrent presentation capacity;
`ForStructuralWindow(radius, spare)` derives a square active window plus bounded
fringe slots rather than treating the former `256` default as a user-facing view
distance. `MinimumSeamlessDiagonalSpare(radius)` is `4R+1`: enough to construct
one complete diagonal fringe before retiring the outgoing fringe. Smaller spare
profiles remain valid and degrade to bounded retire-then-build convergence.

Desired window membership is independent from realized Godot presentation.
Crossings may arrive while prior build/retire work is pending; obsolete builds
are cancelled, reverse movement cancels obsolete retirements, and the queues
coalesce toward the newest desired center. `TryResetForJump` is the explicit
fast-travel path: it advances the desired center, cancels obsolete entering work,
retires/reuses the old slots under budget, and permits the target semantics to be
prepared without reserving two complete windows. Temporary incomplete
presentation during that reset is intentional.

Same-center tracking returns before candidate reconstruction/diff. The client
semantic cache resolves actual crossing candidates through a private bounded
open-addressed `SpatialChunkKey` index, so lookup cost follows queried entries
rather than entries multiplied by semantic-slot capacity. Retained offset moves
do not revalidate semantic render groups; only incoming or semantically changed
presentation does.

Every render chunk chooses an integer terrain-height Y anchor. Terrain, static,
and public Y positions subtract that anchor before float conversion; the root
then applies anchor-minus-render-origin. Horizontal and vertical rebases therefore
change roots only without first losing precision through large absolute floats.
Horizontal relative position is likewise evaluated in integer millimeters as
`relative chunk * 16000 + object local - player local` before float conversion.
Crossing local `15,999 -> 0` therefore remains a continuous 1 mm move even near
the signed world-coordinate limits. Same-chunk LocalX/LocalZ movement writes only
the common root; it does not scan or rewrite every render-chunk root. Chunk roots
change for discrete window movement, Y-origin movement, construction, or
retirement—not for ordinary continuous horizontal player motion. Y uses the same
two-level scheme: a discrete integer vertical anchor on chunk roots plus a small
continuous player offset on the common root. Ordinary slope/jump/fall movement
therefore changes one root; crossing the current 256 m precision envelope performs
one bounded all-chunk vertical rebase.

Camera orientation is not a semantic, residency, dirty, or resource-lifetime
event. Resources remain alive for render-window lifetime and Godot's ordinary
frustum/geometry culling decides whether they are drawn. A camera swing neither
rebuilds a chunk nor migrates public representation. Static batching remains
spatially local to a render chunk plus compatible key; the renderer does not
build world-wide MultiMeshes.

Godot resource layouts, scene nodes, renderer slots, fallback assets, and render
group choices are presentation implementation—not world, persistence, Network,
Game.Contracts, or mod APIs.

`GodotWorldInput` is likewise a thin raw-input adapter. It reads the concrete
Godot InputMap actions (`move_left/right/forward/back`, `interact`, and
`primary_action`) plus viewport focus and emits `ClientWorldInputSample`.
Normalization, focus suppression, held/pressed interpretation, command
quantization, and sequencing live in Client Core; another engine supplies only
its own physical capture and action-map policy. The native T0.10 acceptance now
injects declared Godot action state through this adapter and follows the result
through real game authority, `ChunkPublicationStore`, the production semantic
cache/window, and Godot public-layer refresh.
