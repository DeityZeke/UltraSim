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

`GodotSlidingRenderWindow` is the concrete X/Z presentation coordinator. Its
discrete center follows the player's canonical home chunk immediately, while
the common renderer root tracks `-playerLocalX/-playerLocalZ`. A one-chunk slide
therefore retains the overlap, moves its signed `RenderWindowOffset`s, retires
only the outgoing strip, and admits only the incoming strip. It never changes
absolute `SpatialChunkKey`, `ClientSemanticChunkHandle`, publication cursor, or
semantic hash. These four identities remain independent:

```text
absolute SpatialChunkKey
client semantic handle/lifetime
transient signed RenderWindowOffset
private reusable Godot render slot
```

The window requires incoming structural semantics to be resident before it
queues presentation work. That does not broaden public entity subscription:
structural view distance, public interest, and current camera visibility have
separate owners. `MaximumRenderChunks` is concurrent presentation capacity;
`ForStructuralWindow(radius, spare)` derives a square active window plus bounded
fringe slots rather than treating the former `256` default as a user-facing view
distance.

Every render chunk chooses an integer terrain-height Y anchor. Terrain, static,
and public Y positions subtract that anchor before float conversion; the root
then applies anchor-minus-render-origin. Horizontal and vertical rebases therefore
change roots only without first losing precision through large absolute floats.
Horizontal relative position is likewise evaluated in integer millimeters as
`relative chunk * 16000 + object local - player local` before float conversion.
Crossing local `15,999 -> 0` therefore remains a continuous 1 mm move even near
the signed world-coordinate limits.

Camera orientation is not a semantic, residency, dirty, or resource-lifetime
event. Resources remain alive for render-window lifetime and Godot's ordinary
frustum/geometry culling decides whether they are drawn. A camera swing neither
rebuilds a chunk nor migrates public representation. Static batching remains
spatially local to a render chunk plus compatible key; the renderer does not
build world-wide MultiMeshes.

Godot resource layouts, scene nodes, renderer slots, fallback assets, and render
group choices are presentation implementation—not world, persistence, Network,
Game.Contracts, or mod APIs.
