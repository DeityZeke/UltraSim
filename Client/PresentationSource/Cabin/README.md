# P9 Cabin presentation source

This is an optional, renderer-only art set. It does not define collision, support,
walkability, interaction ranges, WorldStatic identities, or WorldPack topology.
`cabin-p9.blend` is editable source; `build_cabin_meshes.py` deterministically
exports passive `.usmesh` triangles to `Client/PresentationAssets/Cabin/`.

```sh
blender --background --python Client/PresentationSource/Cabin/build_cabin_meshes.py
dotnet run --project _Tools/OfficialContentBuilder/UltraSim.OfficialContentBuilder.csproj -c Release -- Client/OfficialContent Client/PresentationAssets
```

The five painted PNGs were generated for this project using OpenAI image
generation (new-image mode), with prompts for tileable illustrative wood/log,
shingle roof, weathered stone, forest earth/grass, and pine needle/branch
materials. The prompts requested broad tonal variation, hand-painted form cues,
low-frequency grunge, no text, no characters, no photoreal scan, and square
seamless material swatches. Source images are in `Textures/`; identical runtime
bytes are under `Client/PresentationAssets/Cabin/Textures/`. No third-party
artwork is included.

`OfficialCabinPresentation` pins exact bytes and publishes the runtime package
through `world.local`. A changed source requires a new exact hash/package
revision, admission tests, and visual/native verification. Blender is not a
runtime dependency. The authored meshes deliberately remain small and usable
as future impostor/LOD-bake inputs, but no impostor system is part of P9.

The current Classic Cabin is one authoritative room. The mesh source is not
permission to draw an apparent second room, doorway, or opening that Game/World
does not own; that extension needs a separately versioned WorldPack decision.
