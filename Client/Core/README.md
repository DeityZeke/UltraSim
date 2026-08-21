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
Actual snapshots require valid chunk-bound cursors; a default cursor is admitted
only for an empty initial base stage. Equal revision is idempotent only when the
post-supersession semantic page is identical. Known state flags and change
revisions are validated before mutation. A bounded public-index lookup, resident-
location hint, and touched-slot list keep public application shaped by changed
client-resident state rather than a scan of all resident chunks or all retained
identity knowledge.

The cache contains no ECS entities, server mutable storage, connections,
packets, Godot nodes, RIDs, or resources. Borrowed chunk views remain valid only
until the next cache mutation. Engine-neutral resident terrain mesh preparation
also routes through this client-world boundary; T0.8 resolves immutable base
content, while a later client overlay can change that private resolution without
changing presentation callers. Rendering and visibility are independently owned
by the engine adapter.

Structural semantic residency, dynamic public subscription, and current camera
visibility are independent facts. A structurally resident chunk may contain the
verified immutable terrain/static/Multi baseline and zero public records. The
client's world/view radius may therefore shape structural cache capacity without
expanding the server/game-owned public-entity interest entitlement. The
`ForStructuralViewRadius` helper only derives a bounded client capacity profile;
it does not create Network or AOI policy, and the old `512` default is not a
frozen production view distance.

`TryApplyChunkAbsent` assumes its local/remote bridge already validated the
current request/subscription attempt. The cache fences semantic-handle generation
and publication epoch; same-epoch transport request ordering belongs to the
future N1 bridge rather than Client Core.
