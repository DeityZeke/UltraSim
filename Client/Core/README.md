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
escape/right-click stop, double-click Destination, and camera-relative WASD
conversion. The engine supplies only button edges, focus, a horizontal camera
basis, and a raycast-nominated world point. `ThirdPersonDirectProfile` and
`IndependentMoveLookProfile` are intentionally crude executable canaries: the
former proves relative forward rotates in Client Core rather than Game; the
latter proves movement and look can be nominated in different directions.
These types are T0 evidence, not a generic controller/mod framework.
