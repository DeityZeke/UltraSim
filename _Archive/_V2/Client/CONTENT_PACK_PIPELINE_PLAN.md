# UltraSim Content Pack Pipeline Plan

## Goal

UltraSim content packs provide replaceable and additive presentation content without hard-wiring models, textures, materials, sounds, or UI surfaces into the Godot client. Built-in client assets remain the always-available fallback layer. Server and user packs can add or replace semantic slots such as mobiles, item visuals, icons, chat surfaces, and later sounds or terrain materials.

## Current Checkpoint

Implemented now:

- typed `ContentPackManifest` contract for root `pack.json`
- manifest validation for pack identity, schema, pipeline format, file paths, hashes, duplicate ids, definition references, dependencies, filters, item/mobile material slots, and hue channels
- compression/container seam through `IContentPackSource`
- unpacked folder reader
- `.uspkg` zip reader
- archive writer seam through `IContentPackArchiveWriter`
- `.uspkg` zip writer for validated unpacked folders
- tests for manifest validation, bad packs, folder reading, package writing, zip reading, decompression, and per-file integrity checks

Intentionally deferred:

- installing validated packs into the persistent client cache
- pack registry/active pack ordering UI
- converting validated definitions into runtime resolver/catalog layers
- server pack advertisement/download/delta packets
- runtime GLB import/loading
- material binding, dye/hue shader application, and atlas stitching
- replacing built-in Godot `.tres` mobile definitions with content-pack-driven mobile definitions

Recommended pickup point:

1. Create a tiny real sample pack with one GLB and at least one shared material/resource texture.
2. Add a pack registry/installed-pack index so packs can be enabled, disabled, ordered, and diagnosed.
3. Build a manifest-to-layer adapter that turns validated definitions into resolver/catalog ownership records.
4. Wire built-in `bot_x`/`bot_y` as fallback definitions below pack-provided replacements.
5. Only then start runtime GLB/material/hue loading and server-side pack advertisement.

## Design Principles

- Packs are either unpacked folders or archive files with a required root `pack.json`.
- The archive/container reader is abstract. `.uspkg` zip is the first supported container, but package loading must not assume zip forever.
- Every pack declares a pipeline format compatibility range so incompatible packs fail cleanly.
- Content ids use stable logical ids. New authored content should prefer namespaced ids such as `ultrasim:mobile/x_bot` or `my_server:mobile/clockwork_guard`; legacy dotted ids such as `mobile.x_bot` may be bridged while the current client migrates.
- File entries and semantic definitions are separate. File entries answer "what bytes exist and are they valid?" Definitions answer "which presentation/gameplay slot do these bytes fill?"
- Packs are partial. A pack can replace one texture, one mobile definition, or one UI surface and inherit everything else from lower layers.
- Higher priority layers win. Built-in fallback is lowest priority; server-required packs, server-optional packs, user packs, and user mods can be ordered above it.
- Per-file SHA-256 hashes are mandatory. They drive integrity checks, cache validation, and later delta streaming.
- Item and equipment visuals should not assume one unique texture map per item. Prefer shared, tileable resource textures such as metal, stone, wood, cloth, leather, and gem. Meshes/material descriptors reference resource/material slots, and the runtime can live-stitch the active texture set into atlases.
- Material variation should be data-driven. Plan for dye/hue channels rather than duplicate texture files for every color. A good first shape is three optional color channels per item/material binding, such as main, secondary, and trim, plus a RunUO-style hue table or shader palette operation.
- TazUO-style PNG replacement is the simple case of this system: a pack provides
  a file/definition at the same logical id and higher priority wins. The pack
  resolver should keep that workflow easy even while supporting richer manifests.
- Client-local sound filters should sit above server-declared sound events and
  resource-pack sound overrides. A user pack or setting can suppress or redirect
  a sound event without changing server gameplay semantics.
- Seasonal/environment overlays should be modeled as content-pack layers selected
  by server world state plus client preference, not as hard-coded texture swaps.

## Server Asset Join Flow

The expected online flow is:

```text
connect to server
receive server pack manifest advertisement
download missing server assets from the game server or a declared download URL
verify every file against the manifest
load assets and stitch active texture atlases
apply the server content layer over built-in fallback visuals
continue normal join/login/admission flow
```

Required server packs may block login until verified and applied. Optional packs should be allowed to upgrade visuals after entry, falling back to built-in assets until ready.

## Resource Texture Atlases And Hue Channels

UltraSim item art should lean toward reusable material/resource textures instead of per-item bespoke texture sheets:

- resource textures are uniform, tileable, and resolution-flexible
- 16x16, 64x64, 256x256, or higher resolution packs should describe the same logical material ids
- atlas stitching happens from active pack layers at load/reload time
- models reference material slots such as `blade`, `handle`, `guard`, `cloth`, or `trim`
- material slots resolve to resource texture ids like `ultrasim:material/iron`, `ultrasim:material/oak`, or `my_server:material/moonsteel`
- color variation should be expressed as hue/dye channel data where possible, not as many near-duplicate texture files

The likely first color model is:

```text
channel0/main
channel1/secondary
channel2/trim
```

Each channel can carry either a palette/hue id or an explicit color transform. This keeps RunUO-style hues possible while leaving room for richer shader-driven dye systems later.

## Mesh Parts Versus Material Slots

The content contract should describe material slots, not mesh count. A sword might expose `blade`, `guard`, and `grip` slots, but the underlying asset can be authored as:

- one mesh with multiple material slots
- several child meshes such as `BladeMesh`, `GuardMesh`, and `GripMesh`
- a single shader/material using vertex colors or masks to route hue channels

The runtime-facing rule is that the imported visual exposes bindable slot ids. Whether those slots come from sub-mesh material indices, named child mesh nodes, or shader masks is an asset-authoring/import detail.

Default recommendation:

- use one model with named material slots for normal items and equipment
- use separate child meshes when parts need to be hidden, swapped, animated, damaged, or replaced independently
- use shader/vertex-mask routing later for high-volume dyed assets if it gives a real rendering or memory win

## Initial `pack.json`

```json
{
  "schema": "ultrasim.content-pack.v1",
  "pack": {
    "id": "ultrasim.core.bots",
    "name": "UltraSim Core Bots",
    "version": "1.0.0",
    "minPipelineFormat": 1,
    "maxPipelineFormat": 1,
    "contentHash": "sha256:<whole-pack-hash>"
  },
  "files": [
    {
      "path": "models/bot_x.glb",
      "kind": "model",
      "sizeBytes": 123456,
      "sha256": "<file-sha256>"
    }
  ],
  "definitions": [
    {
      "id": "ultrasim:mobile/x_bot",
      "kind": "mobile",
      "mode": "replace",
      "displayName": "Human Female",
      "model": "models/bot_x.glb",
      "cameraPivotOffset": { "x": 0, "y": 1.6, "z": 0 },
      "nameplateOffset": { "x": 0, "y": 2.0, "z": 0 },
      "effectsOffset": { "x": 0, "y": 0.9, "z": 0 }
    },
    {
      "id": "ultrasim:item/flower",
      "kind": "item-visual",
      "mode": "append",
      "displayName": "Flower",
      "model": "models/items/flower.glb",
      "materialSlots": [
        {
          "slotId": "petals",
          "resource": "ultrasim:material/petal_soft",
          "hueChannel": "main",
          "defaultHue": 32
        },
        {
          "slotId": "stem",
          "resource": "ultrasim:material/plant_green",
          "hueChannel": "secondary",
          "defaultHue": 0
        },
        {
          "slotId": "center",
          "resource": "ultrasim:material/pollen",
          "hueChannel": "trim",
          "defaultHue": 12
        }
      ]
    }
  ]
}
```

## Implementation Phases

1. Manifest contract and validation
   - Add typed `ContentPackManifest` records.
   - Validate pack identity, schema, pipeline format, file paths, hashes, duplicate ids, and definition references.
   - Validate item/mobile material slots and the fixed first-pass hue channel names: `main`, `secondary`, and `trim`.
   - Add folder and archive readers through a compression-agnostic source interface.

2. Local authoring and packaging
   - Add a small command/tool path that validates an unpacked folder.
   - Package validated folders as `.uspkg` zip files.
   - Keep the packaging API separate from the manifest validator so another compression/container can replace zip later.

3. Client install/cache
   - Install validated pack files into the client content cache or local pack root.
   - Record pack metadata in the content cache index.
   - Build content resolver layers from semantic definitions.

4. Runtime presentation catalog
   - Convert mobile definitions from hard-wired `.tres` resources to logical content definitions.
   - Keep built-in `bot_x`/`bot_y` as fallback definitions.
   - Allow server/user packs to replace `mobile.x_bot` and `mobile.y_bot` or append new mobile ids.

5. Server push and delta
   - Add server-to-client pack manifest advertisement packets.
   - Client compares pack/file hashes with cache and requests missing files.
   - Server streams only missing/changed file chunks.
   - Required packs can block world entry; optional packs can upgrade visuals after entry.

6. Broader asset types
   - Add item/mobile material binding with shared resource texture slots.
   - Add resource texture atlas stitching for reusable material families.
   - Add hue/dye channel descriptors for main, secondary, and trim variation.
   - Add icon/UI sprite atlases where they are useful.
   - Add sound event definitions with per-event merge semantics.
   - Add diagnostics/admin UI for active layer ownership and overridden definitions.
