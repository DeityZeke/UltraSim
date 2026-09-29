# Definition, Presentation, and Asset Independence

Status: CANONICAL presentation rules; TENTATIVE / P4-pending general definition and physical-semantics generalization

Primary references:
- [Architecture](../../Docs/ARCHITECTURE.md)
- [UI](../../Docs/UI.md)
- [Client Content](../../Client/Content/README.md)
- [Game World](../../Game/World/README.md)
- [P4 roadmap](../../Docs/Plans/UltraSim_Pre_Many_Person_Live_Testing_Roadmap.md)

Related topics:
- [UI nine-slice art / frame families](ui-nine-slice-art-and-frame-families.md)
- [Framework Runtime Packages / Services / Extensions / ECS](framework-runtime-packages-services-extensions.md)

## Core direction

The accepted direction is to keep three meanings separate:

~~~text
Definition
    what the thing IS

Presentation
    how a client should present it for a semantic role

Asset
    concrete mesh/texture/material/animation/image bytes used to realize presentation
~~~

Permanent design principle:

> Deleting or replacing graphics must not delete or redefine the semantic/physical object.

A rock, tree, wall, item, creature, chest, door, or other valid Game definition should retain its gameplay identity and public physical semantics when optional high-quality art is absent. The client should fall back to an ugly but useful realization rather than losing the object.

Presentation/resource-pack replacement must not silently change collision, walkability, support, interaction range, inventory truth, damage, or other Game semantics.

## Physical semantics should not be inferred from art at runtime

The general P4 target is for authoritative/public physical meaning to live with semantic definition/contracts, not to be inferred from current GLB/mesh bounds.

Conceptually:

~~~text
RockDefinition
    stable identity
    physical shape/bounds/footprint
    collision/support policy
    interaction semantics
    presentation reference

RockPresentation
    mesh/material/animation roles

rock.glb
    optional concrete asset
~~~

Authoring/import tools may warn when art does not fit the declared physical definition. An artist changing mesh extents should not accidentally rewrite gameplay physics.

The exact generalized physical-shape vocabulary is still P4 work; do not infer that every future object must be only an AABB.

## Current implemented foothold

This separation already exists substantially for frozen world statics:

- WorldStaticDefinitionId is stable structural identity;
- WorldStaticBounds carries exact authoritative integer-millimetre bounds;
- WorldStaticFlags carries render/collision/top-support policy;
- frozen static/Multi records retain semantic identity and bounds independently of optional renderer art;
- ClientPrimitivePresentation.TryFromStructuralBounds can derive an asset-independent Box fallback from already-disclosed structural bounds.

The client primitive contract is explicitly presentation-only and does not alter authoritative interaction bounds.

The current client also has stock fallback shapes for several actor/public/demo objects. What is **not** yet generalized is a complete definition-driven fallback descriptor for every item/object family. P4 should extend the semantic model rather than expanding a hard-coded demo catalog indefinitely.

## Semantic presentation identity

UI and presentation consumers should request semantic roles rather than physical file paths.

Canonical neutral shape:

~~~text
SemanticPresentationReference
    DefinitionDomainId
    StableDefinitionId
    PresentationRoleId
~~~

Examples of roles:

- World;
- InventoryPortrait;
- VendorPortrait;
- CharacterPortrait;
- BestiaryPortrait;
- Inspection;
- Thumbnail.

Stable authored identity resolves to dense runtime handles only after cold composition. A server/UI definition must not send arbitrary filesystem paths, Godot resources, or unvalidated URLs as semantic presentation identity.

## Dedicated 2D icons are optional

A valid semantically presentable definition should not require a separately authored “inventory icon.”

Role fallback should conceptually be:

~~~text
explicit requested-role presentation
    ↓
compatible general presentation
    ↓
derive from World presentation
    ↓
primitive semantic fallback
    ↓
debug/missing-content presentation
~~~

Therefore the same 3D Katana, chair, potion, creature, statuette, crop, chest, etc. may provide its ordinary UI representation without creating a bespoke 2D asset first.

Many definitions may reuse one presentation; presentations may reuse meshes/materials. Resource packs can replace semantic asset IDs without forcing Game definitions to change.

## 3D-derived portrait tiers

The accepted presentation model reuses World/object presentation when appropriate.

A derived portrait profile may define camera/framing, lighting, orientation, scale, background, and a static/named/animation-frame pose.

Do not make every visible inventory slot a live 3D scene.

Use bounded tiers:

~~~text
Cached Portrait
    render/derive once
    reuse cheap image
    ordinary inventory/vendor/crafting cells

Live Portrait
    very small bound
    hovered/selected/focused item
    may rotate or animate

Inspection View
    normally one or similarly tiny bound
    interactive rotate/zoom/examine
~~~

This supports hover rotation, creature/object animation, and Skyrim-like physical clue inspection while keeping ordinary inventory rendering cheap.

Derived portrait caches are disposable renderer output. Deleting the cache loses no Game/content truth; they can be regenerated and may later participate in compatible UI/Icon atlases.

## Presentation interaction is not Game authority

Hover rotation, local animation, inspection camera manipulation, tint, framing, etc. are client presentation behavior.

Prefer bounded declarative profiles such as:

~~~text
HoverBehavior = None | Rotate | PlayAnimation | RotateAndAnimate
InspectionAllowed
InspectionProfile
~~~

rather than arbitrary executable callbacks on every definition. A real semantic Game action still routes through Game authority.

## Client disclosure boundary

The client should not need every private server definition field merely to render a valid object.

P4 may compile a bounded client-visible semantic/presentation descriptor containing the public pieces required for presentation/fallback, such as stable definition identity, presentation roles, disclosed physical/fallback shape, and public interaction/picking metadata. Private loot/stats/AI/crafting truth remains server-side unless independently disclosed.

The exact descriptor/facet layout is still open P4 design.

## P4 questions still open

P4 still needs to settle/prove:

- the exact typed Definition/facet/store authoring API;
- DefinitionId alias/remap integration;
- generalized public physical/fallback shape contracts beyond current frozen-static proofs;
- the generic non-actor 3D presentation/content path;
- authoring validation/warnings between physical semantics and visual mesh;
- how first-party item/object definitions dogfood these seams without privileged bypasses.

Do not treat those open implementation details as already complete simply because the higher-level separation is accepted.
