# Shared Inventory Presentation World / Impostor Hybrid

Status: EXPLORING

Related:
- [Inventory viewer profiles / cached derived portraits](inventory-viewer-profiles-and-derived-portraits.md)
- [Renderer impostor LOD / far-field cards](renderer-impostor-lod.md)
- [Definition / presentation / asset independence](definition-presentation-asset-independence.md)
- [UI](../../Docs/UI.md)

## Idea

A D2-style Inventory may optionally realize its visible items inside one isolated,
presentation-only 3D world rather than giving every cell its own live scene.

Conceptually:

~~~text
InventoryPresentationWorld
    isolated World3D / environment
    neutral bright ambient/studio lighting
    key/fill/rim lights
    one camera
    one SubViewport

    visible item instances arranged by Inventory Grid placement
~~~

The viewport output is composited into the UI. Pointer input can be mapped through
the Inventory viewport/camera and pick a presentation instance back to the existing
UiRowId / semantic action identity.

This is presentation-only. Ray/pick results must not mutate ECS or Inventory
authority directly.

## Resource reuse

Do not interpret this as “instantiate every model in the game at startup.”

Prefer:

~~~text
content/CAS bytes
    ↓
prepared presentation mesh/material/texture resource
    ↓
bounded warm working set
    ↓
many borrowed/instanced realizations
~~~

World, Inventory, Inspection and derived portrait realizations may share the same
prepared resources without decoding/uploading separate copies.

Prefetch the expected presentation working set; do not make all known content
permanently GPU-resident.

## Inventory Grid mapping

Authoritative Inventory already owns:

~~~text
X
Y
Width
Height
~~~

A presentation world can map those values directly into a fixed logical miniature
space. A 1×3 sword remains one presentation object occupying a 1×3 region.

Picking returns the presentation row/item identity and then routes through the
existing semantic Select/Place/etc. action path.

## Render-on-demand possibility

The shared viewport need not run continuously:

~~~text
inventory closed
    no rendering

inventory open / idle
    render after change, then freeze

item added/moved/removed
    render another frame

hover/select
    temporarily update for highlight/rotation/animation

leave hover
    freeze again

inspect
    intentionally live
~~~

This may make one shared live scene significantly cheaper than one live 3D scene
per visible item while supporting interactive presentation.

## Mixed realization

The presentation world does not require every item to use the same renderer form.

Possible realization:

~~~text
simple rigid/boxy static
    derived six-view pseudo-3D impostor card

complex static object
    real mesh or richer impostor

creature/rigged object
    cached/static-pose mesh; optional bounded live promotion

inspection target
    full 3D presentation
~~~

A Nacho-style card is therefore a renderer optimization beneath
SemanticPresentationReference, not an Inventory content requirement.

## Derived, not authored icon tax

Inventory impostors should be automatically derived from World presentation and
treated as disposable/cacheable presentation artifacts.

Do not require authors to manually create one impostor/icon per Definition.

Deleting an Inventory impostor should degrade to another portrait realization or
rebake, never invalidate the item.

## Relationship to P6

P6 remains canonical:

~~~text
Inventory item
    → SemanticPresentationReference
    → Inventory presentation realization
~~~

The shared 3D world would be an alternate realization strategy below that seam.
It should not require changes to Game Inventory authority or its semantic action
protocol.

## Questions to measure before promotion

- cost versus current cached TextureRect portraits for 5×5/6×5 backpacks;
- open/switch/update latency;
- GPU cost while hover animation is active;
- native resource count/lifetime;
- pointer/picking complexity;
- behavior for large List/bank/vendor surfaces;
- whether mixed impostor/mesh realization preserves sufficient visual quality.

Likely policy if useful:

~~~text
small D2 backpack
    shared 3D presentation world may be attractive

large list/bank/vendor
    ordinary cached portraits

inspection
    bounded live 3D
~~~

Do not promote this to canonical until a concrete prototype beats or materially
extends the current P6 cached-image path.
