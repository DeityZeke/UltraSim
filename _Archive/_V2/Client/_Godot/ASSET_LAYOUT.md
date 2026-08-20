# Godot Client Asset Layout

This project keeps runtime code, scenes, and content-facing assets separate:

- `Assets/Textures` - source textures and texture atlases.
- `Assets/Materials` - reusable materials and material resources.
- `Assets/Meshes` - raw or reusable mesh resources.
- `Assets/Models` - imported model scenes/assets.
- `Assets/Animations` - animation clips, animation libraries, and animation state resources.
- `Assets/Avatars` - player/NPC presentation resources and mobile definitions.
- `Assets/Items` - item/world-object presentation resources.
- `Assets/Environments` - skies, lighting, weather, and environment presets.
- `Assets/VFX` - particles, decals, trails, impact effects, and other visual effects.
- `Audio/SFX` - short sound effects.
- `Audio/Ambience` - looping ambient/world audio.
- `Audio/Music` - music tracks and playlists.
- `Maps/Terrain` - terrain materials, terrain chunks, and terrain resources.
- `Maps/Statics` - baked/static world decoration resources.
- `Maps/Chunks` - chunk scenes/resources used by terrain/static streaming.
- `Shaders` - shared shaders and shader includes.
- `UI/Fonts` - fonts and text resources.
- `UI/Icons` - UI icons and small 2D presentation assets.
- `UI/Themes` - Godot theme resources and UI skin materials.

Server-streamed content and user-selected packs should resolve through the future content resolver/cache layer before mapping onto these presentation categories.
