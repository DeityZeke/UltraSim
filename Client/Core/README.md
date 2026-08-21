# Client Core

Client Core owns retained engine-neutral client projections. UI retention uses
the neutral UI kernel. Terrain T0.8 adds `ClientWorldSemanticCache`, the one
production semantic world cache consumed by presentation.

The world cache is owner-thread bound and fixed-capacity. Its limits describe
client-resident chunks and rows rather than the server's ECS/public address
space. It copies immutable base terrain/statics/Multi fragments from a bound
`WorldPack`, then transactionally applies chunk-bound publication
snapshot/delta semantics. Client-local handles are generation fenced; public
generation/version knowledge is global across resident chunk pages.

The cache contains no ECS entities, server mutable storage, connections,
packets, Godot nodes, RIDs, or resources. Borrowed chunk views remain valid only
until the next cache mutation. Engine-neutral resident terrain mesh preparation
also routes through this client-world boundary; T0.8 resolves immutable base
content, while a later client overlay can change that private resolution without
changing presentation callers. Rendering and visibility are independently owned
by the engine adapter.
