# Godot client adapter

This optional project translates retained Client Core projections into native
Godot presentation. It owns UI controls and, from Terrain T0.8,
`GodotWorldRenderer` resources.

World rendering is engine-thread bound and budgeted. A render chunk owns one
terrain mesh node plus grouped `MultiMesh` nodes for compatible frozen/static-
Multi and visible public records; terrain cells and static instances do not gain
scene-tree identity. Semantic residency, render residency, and visibility are
separate states. Floating-origin changes update presentation transforms only.

Godot resource layouts, scene nodes, renderer slots, fallback assets, and render
group choices are presentation implementation—not world, persistence, Network,
Game.Contracts, or mod APIs.
