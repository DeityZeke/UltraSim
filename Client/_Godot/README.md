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

Every render chunk chooses an integer terrain-height Y anchor. Terrain, static,
and public Y positions subtract that anchor before float conversion; the root
then applies anchor-minus-render-origin. Horizontal and vertical rebases therefore
change roots only without first losing precision through large absolute floats.

Godot resource layouts, scene nodes, renderer slots, fallback assets, and render
group choices are presentation implementation—not world, persistence, Network,
Game.Contracts, or mod APIs.
