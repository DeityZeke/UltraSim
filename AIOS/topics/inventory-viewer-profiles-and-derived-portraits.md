# Inventory Viewer Profiles and Derived Portraits

Status: CANONICAL current-product implementation / P6 CLOSED

Primary references:
- [P6 evidence](../../_Benchmarks/Baselines/framework-p6-inventory-viewer-profiles-2026-09-29.md)
- [Inventory plan](../../Docs/Plans/INVENTORY.md)
- [UI](../../Docs/UI.md)
- [Definition / presentation / asset independence](definition-presentation-asset-independence.md)

Related:
- [UI nine-slice art / frame families](ui-nine-slice-art-and-frame-families.md)
- [Godot native shutdown lifetime](godot-dotnet-native-shutdown-lifetime.md)

## Authority boundary

P6 does not redefine Inventory truth.

Authoritative Inventory remains:

- persistent item/container identity;
- quantity;
- equipment state;
- canonical Grid placement;
- immutable item footprint;
- container Grid dimensions/capacity;
- ownership/authorization;
- transactional mutation and persistence.

Viewer choice is client presentation policy.

A Grid container may be shown as D2 Grid or List. A non-Grid/Unplaced container falls back to List rather than receiving fabricated Grid coordinates.

Switching viewer does not mutate Inventory authority.

## Supported viewer profiles

Current product support:

~~~text
D2Grid
    default for compatible Grid containers

List
    alternate/accessibility/fallback
~~~

The first-party Grid Backpack is proven at 5×5 and upgraded 6×5.

The Inventory core permits larger theoretical grids, but P6 does not claim complete or pleasant UI behavior at the theoretical 64×64 maximum. Large-container pagination/search/product UX remains future work.

## Typed projection

The client viewer consumes typed placement facts rather than parsing human-readable summaries.

Each presented item includes:

- stable UI row identity;
- name and quantity;
- equipment state;
- typed Grid X/Y/Width/Height when present;
- semantic Inventory portrait identity;
- public fallback bounds.

One authoritative item with a 1×3 footprint renders as one 1×3 Grid rectangle, not as three items.

Equipped items remain non-Grid and are displayed separately rather than being assigned fake Grid positions.

## Dynamic semantic image binding

P5 proved a semantic image binding for static compiled image elements.

P6 adds a bounded per-row/view binding fenced by:

- exact UiViewInstanceId;
- exact compiled Definition handle;
- current reconciled row set.

Removed or replaced rows cannot retain stale portrait identity, and retiring the view clears the binding.

The neutral UI kernel transports an opaque bounded row attachment and does not depend on Inventory or Framework.Definitions. Game.Presentation.Contracts owns the Inventory-specific attachment codec.

Local and Remote paths converge to the same typed SemanticPresentationReference.

## Derived portrait path

A dedicated 2D icon remains optional.

The first-party Iron Sword has no hand-authored Inventory icon. Its admitted World mesh is rendered through a reusable Godot staging scene into a cached 128×128 ImageTexture.

Normal UI rows use cached image resources.

The staging system is bounded:

- maximum 32 portrait images;
- one pending render at a time;
- one reusable SubViewport staging scene;
- headless/unavailable cases fall back to the primitive semantic icon;
- staging resources retire when the last Inventory view closes;
- cached ImageTextures retire with the UI renderer.

The generated image is presentation cache only. It does not grant content authorization and does not become item/Definition/persistence truth.

## Current proof-specific mapping

The current Godot portrait cache contains an explicit first-party mapping from:

~~~text
game.item.iron-sword
    → public IronSword World mesh
~~~

This is an acceptable P6 proof seam, not the final generic Definition→World presentation resolver.

Future generalized item/object art work should route arbitrary Definitions through the semantic Presentation/content resolver rather than growing a hard-coded switch inside the Inventory portrait cache.

## Fallback

Current presentation behavior preserves the accepted principle:

~~~text
InventoryPortrait request
    ↓
authored compatible image when available
    ↓
World-derived portrait for the proven first-party path
    ↓
primitive semantic fallback
~~~

A missing portrait does not hide or invalidate an authoritative item.

## Live portraits / Examine

P6 proves cached portraits only as the normal inventory path.

Optional hover-live animation/rotation and interactive Examine remain valid later presentation ideas, but are not required for P6 closure.

Do not replace ordinary inventory cells with one live 3D scene per item.

## Lifecycle and verification

P6 proves:

- D2/List live switching without semantic mutation;
- equivalent Game action paths;
- owner-private authorization;
- save/load generation replacement;
- remote reconnect;
- native staging/texture retirement;
- graphical 3D sword portrait generation;
- headless primitive fallback.

Any future native-backed portrait resource still follows the explicit owner-lived / temporary / proved-borrowed wrapper rule.
