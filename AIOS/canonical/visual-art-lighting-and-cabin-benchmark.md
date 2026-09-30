# Visual Art, Lighting, and Cabin Benchmark

**Status:** CANONICAL visual/presentation direction and selected benchmark milestone  
**Implementation status:** P9 NEXT. P1–P8 provide the client/content/control/audio foundations; this document defines the P9 visual target and test seam, not a claim that the complete lighting system or final art already exists.

Primary references:
- [Client Presentation Architecture](../../Docs/Plans/CLIENT_PRESENTATION_ARCHITECTURE.md)
- [Client Completion Plan](../../Docs/Plans/CLIENT_COMPLETION_PLAN.md)
- [Asset Streaming](../../Docs/Plans/ASSET_STREAMING.md)
- [Simulation Time / Resolution / Control](simulation-time-resolution-and-control.md)

## 1. Purpose

The existing Cabin in the Woods remains UltraSim's compact client/content proving ground, but it should also become the first canonical **visual benchmark scene** for the intended game.

The goal is not to build a final asset library before gameplay exists.

The goal is to establish, early and repeatedly:

- what UltraSim is supposed to look like;
- how intended art survives the real world.local/content pipeline;
- how sunlight, shadows, interiors, openings, atmosphere, and region mood behave;
- how real art and the permanent primitive fallback coexist;
- whether future renderer changes improve or regress the visual target.

The visual benchmark is part of the real client/presentation architecture, not a disposable art mock-up.

## 2. Core visual identity

UltraSim's default presentation should combine comparatively detailed/readable 3D geometry with the visual discipline of classic illustrative isometric artwork.

Useful shorthand:

> **Modern-enough geometry, classic illustrative material/texture sensibility.**

The default look should favor:

- strong silhouettes and form readable without textures;
- broad, deliberate color/value masses;
- hand-authored or hand-painted-looking material detail;
- restrained grunge/noise used to break up flat surfaces;
- low-to-medium frequency texture detail rather than photographic microdetail;
- controlled highlights and contact/AO-like darkening that reinforce shape;
- restrained physical material response rather than hyper-real scanned PBR;
- texture/detail scale chosen for the normal gameplay camera.

The intended default should feel old-school, slightly grungy, and authored rather than sterile or photoreal.

This visual language may be implemented with modern shaders and textures. It is not defined by literal low texture resolution or by requiring pixel art.

## 3. Geometry must carry the object

An asset should remain clearly readable in neutral clay/solid-color rendering.

A chair should still look like a chair. Plate, chain, leather, logs, rocks, trees, doors, windows, shelves, and structural openings should be expressed substantially by silhouette, thickness, articulation, and major surface breaks.

Textures should enrich identity rather than rescue ambiguous geometry.

For ordinary close/high-detail actors, the current exploratory budget remains roughly in the 10k–20k-face-class range when useful, with lower complexity preferred whenever silhouette/readability remain intact. Exact budgets must ultimately be measured in a clearly defined runtime metric such as triangles after import; no exact global polygon limit is frozen here.

Environment props should follow the same principle: spend geometry where it materially improves silhouette or lighting/readability, not merely because a budget exists.

## 4. Semantic material versus presentation style

Game/material semantics and renderer appearance should remain separable.

Conceptually:

~~~text
semantic material
    Wood / Iron / Leather / Stone / Cloth / Glass / etc.
        ↓
presentation realization
    UltraSim Painted
    Solid / Debug
    PBR or other future material set
~~~

The official/default UltraSim realization should use the illustrative painted/grungy style.

A debug or alternate presentation pack may render the same semantic object with solid colors, diagnostic checkerboards, or a more conventional PBR interpretation without changing Game truth.

This is consistent with the existing presentation/content architecture: renderer/material presentation is replaceable; authoritative identity and behavior are not.

## 5. Hue and palette treatment

UltraSim should support cheap color variation analogous in spirit to UO hues without requiring exact UO implementation.

Potential uses include:

- cloth dyes;
- faction/uniform colors;
- material color variants;
- magical states;
- status/diagnostic presentation;
- regional or cultural presentation variation.

Hue/palette transforms should be maskable where useful so one logical asset may recolor intended regions without recoloring leather straps, metal trim, skin, or other unrelated material areas.

Exact hue encoding and whether it is table-, shader-, palette-, or parameter-based remain implementation details.

## 6. Mood is a regional/environmental layer

The same coherent asset/material language should support a broad emotional range through environment presentation.

Examples:

~~~text
Bright / cheerful
    warm clear light
    high visibility
    lighter shadow feel
    stronger saturation

Balanced / UO-like
    earthy palette
    moderate contrast
    readable shadows
    restrained atmosphere

Dark / oppressive
    low ambient level
    deeper shadow
    cold/sickly environment tint
    small warm local lights
    fog/haze
    reduced saturation or stronger grading
~~~

Nested sectors/regions may eventually select or inherit Visual Environment Profiles.

Regional profiles may alter:

- ambient/exposure;
- fog/haze;
- sky;
- saturation/contrast;
- LUT/palette treatment;
- weather presentation;
- ambient effects;
- music/audio mood.

They should not arbitrarily replace the global geometry/material art language. Regions change mood while remaining recognizably the same game.

## 7. Lighting direction: direct light plus cheap directional field

UltraSim does not need general-purpose fully dynamic GI as the first lighting target.

The preferred direction is layered:

### Direct important lighting

Use real dynamic/direct lighting where it matters most:

- sun;
- moon if visually useful;
- nearby torches/fires/spells;
- near-field important local lights.

Near the camera, ordinary dynamic shadowing should provide high-value effects such as:

- character/tree shadows;
- building silhouettes;
- sunlight through a doorway or window;
- obstruction by nearby geometry.

### Coarse directional light field

World/chunk/sector presentation may maintain a low-resolution lighting field that provides more than one flat ambient color.

A simple probe representation may contain conceptually:

~~~text
AmbientColor
DirectionalColor
DominantDirection
SkyVisibility / Occlusion
~~~

A later richer representation such as low-order spherical harmonics may be evaluated if it earns its cost. The architecture must not depend on SH specifically.

Actors and objects can cheaply sample this field, then layer important direct/local lights on top.

### Penetration rather than full bounce

The coarse solver should support light **transmission/penetration through connected openings** without requiring physically correct multi-bounce GI.

Openings may conceptually expose:

~~~text
shape/bounds
normal/direction
openness
transmission
optional tint
~~~

Examples:

- open doorway: high transmission;
- clear glass: high but not necessarily complete transmission;
- dirty glass: reduced transmission;
- stained glass: reduced/tinted transmission;
- solid wall: no transmission.

A room behind a doorway may receive reduced directional fill from the brighter connected room even when no true bounce-light solution is computed.

The goal is readable authored illumination, not photometric simulation.

## 8. Static topology, portals, and local invalidation

Buildings, caves, dungeons, and future nested sectors can expose a cheap cell/portal topology for lighting.

Conceptually:

~~~text
Exterior
    ↕ window
Room B
    ↕ doorway
Room A
~~~

Static geometry can precompute or cheaply retain information such as:

- sky exposure;
- directional visibility/horizon bins;
- room/cell connectivity;
- static occlusion.

Dynamic changes should dirty only affected local lighting state where possible.

Examples:

- door opened/closed;
- shutter opened/closed;
- wall constructed/destroyed;
- mining passage opened;
- cave collapse;
- window added/removed.

The exact light-volume grid, update cadence, portal format, and invalidation mechanism remain engineering work.

## 9. Decals/projectors are presentation helpers

Projected light cookies, decals, or similar renderer-local techniques may be used to sharpen nearby presentation.

Good candidates include:

- a shaped doorway/window light patch;
- stained-glass color pattern;
- artist-directed contact/shadow emphasis.

These are allowed to make the nearby image more convincing.

They must not become authoritative gameplay truth. The coarse lighting/environment semantics should remain understandable without requiring a decorative decal.

## 10. Client environment resolver seam

The client should expose a presentation-oriented environment input/resolver rather than baking time-of-day and region assumptions directly into Godot scene code.

Conceptually:

~~~text
ClientEnvironmentState
    world/presentation time
    sun/celestial presentation
    visual environment profile
    weather presentation
    local presentation modifiers
    revision/version
        ↓
Lighting / Atmosphere Resolver
        ↓
Godot realization
~~~

Until authoritative server-side calendar/region/weather systems exist, the Cabin benchmark may use a **presentation-only debug/test provider**.

That provider may synthesize:

- time of day;
- sun direction;
- visual environment profile;
- weather/fog presentation.

It must never create authoritative Game semantics.

For example, setting the visual test clock to midnight may make the benchmark dark; it must not independently make a vampire stronger or change NPC perception unless authoritative Game state later supplies those semantics.

Once authoritative state exists, Local authority and Remote projection should feed the same presentation seam rather than replacing the renderer design.

## 11. Cabin Visual Benchmark content

The Cabin in the Woods should receive a small coherent real/near-real art set through ordinary removable Local/Official presentation content, not baked secretly into the renderer binary.

The benchmark should include at minimum:

### Exterior

- one or more tall pine trees;
- grass/ground material(s);
- a few rocks/boulders;
- log cabin wall/roof treatment;
- real exterior doorway/door.

### Interior

Use a two-room cabin.

**Room A — main room**

- exterior door;
- no windows;
- basic readable furniture as useful.

This is the primary exterior-door light-spill test.

**Room B — secondary room**

- connected to Room A by an interior doorway;
- one exterior window;
- simple furniture;
- a substantial blocker such as a bookshelf/cabinet placed in the window or doorway light path.

This room tests directional window light, inter-room transmission, and local occlusion.

## 12. Required lighting observations

The benchmark should make these cases easy to inspect.

### Exterior door into Room A

With the door/opening exposed:

- bright exterior remains distinct from darker interior;
- a directional spill is visible from the doorway;
- illumination decays or becomes less dominant deeper in the room;
- dark corners remain readable without flattening the entire room.

Closing/blocking the opening should materially reduce that contribution when dynamic door semantics are available.

### Room B window

- illumination clearly reads as coming from the window side;
- a brighter window-side patch/spill is visible;
- the far side remains meaningfully dimmer;
- glass/window transmission can later be compared against an open aperture.

### Room B to Room A

Some light may penetrate through the interior doorway, but the result should be modest.

The target is not zero fill and not whole-room flood lighting.

### Bookshelf/blocker test

The bookshelf/cabinet should demonstrate:

- the lit face receives/catches illumination;
- some of the light path is blocked;
- the region behind it receives materially less direct/spill contribution;
- the system does not simply ignore opaque geometry and flood through it.

Exact physical correctness is not required; visual causality must be credible.

## 13. Benchmark controls

A development/debug presentation surface should eventually make comparison cheap.

Useful controls include:

~~~text
Time:
    dawn
    noon
    dusk
    night
    scrub/step

Visual profile:
    bright/clear
    balanced/default
    dark/oppressive

Weather:
    clear
    cloudy
    rain
    fog
    as available

Material/presentation:
    official painted
    solid/clay/debug
    primitive fallback
    optional PBR/debug realization

Lighting debug:
    shadows on/off
    coarse light field on/off
    probe/cell/portal visualization
    opening transmission visualization
~~~

Exact UI and control spelling are not frozen.

## 14. Relationship to the existing Cabin content oracle

The accepted A0.1 BuiltInTestWorlds Cabin Default/Replace/Add oracle remains valuable and should stay deterministic.

Do **not** casually rewrite its frozen fingerprints merely to iterate on final-looking art.

The visual benchmark should use the same Cabin world/content architecture while keeping deterministic content-oracle identities stable where required.

A separate Official/local presentation package or additional logical presentation identities may supply the real benchmark art through world.local.

This preserves both:

- exact package/content regression evidence; and
- a living visual target that can evolve deliberately.

## 15. Suggested implementation staging

The benchmark should be earned incrementally.

### Stage 1 — real visual target

- real pine/rock/log/door/window/grass assets;
- intended painted/grungy material treatment;
- readable neutral/clay mode;
- ordinary world.local packaging.

### Stage 2 — global/direct environment foundation

- sun direction/intensity/color;
- near-field shadows;
- ambient/fog/color-grade Visual Environment Profile;
- client-local time/profile test provider.

This is sufficient to begin meaningful look-and-feel testing.

### Stage 3 — coarse directional probes

- low-resolution chunk/sector light field;
- ambient + dominant directional contribution;
- actor/object sampling.

### Stage 4 — openings and interior propagation

- window/door transmission;
- two-room propagation;
- bookshelf/blocker behavior;
- local dirty updates.

### Stage 5 — optional nearby polish

- projected light cookies/decals;
- stained-glass color patterns;
- richer probe representation;
- additional local-light behavior if evidence justifies it.

The project should not wait for Stage 5 before testing the intended visual direction.

## 16. Benchmark and PF6 relationship

The Cabin Visual Benchmark is intended to improve upcoming real human/client testing by giving testers a representative target rather than only primitives/provisional presentation.

It is not a demand for final game-wide art completion before PF6.

The important near-term evidence is:

- the art/content pipeline carries intended real presentation correctly;
- the game has a recognizable target look;
- fundamental sun/shadow/interior/environment behavior is credible;
- primitive fallback remains available and clearly distinct;
- presentation-local test inputs cannot change Game truth.

Art polish beyond those goals remains iterative.

## 17. Open engineering/art details

Not frozen yet:

- exact runtime triangle budgets;
- exact texture resolutions;
- exact shader graph/material parameter layout;
- exact hue-mask representation;
- exact light-probe grid dimensions;
- simple directional probe versus SH;
- exact shadow-map/cascade strategy;
- exact light-volume refresh cadence;
- exact glass/shadow transmission implementation;
- exact decal/projector implementation;
- exact region/sector Visual Environment Profile schema;
- exact debug UI;
- exact asset filenames/package names.

These should be settled through the Cabin benchmark and measured client evidence rather than guessed in advance.

## 18. Core rule

> **The Cabin should be both a real piece of UltraSim and a permanent visual laboratory: intended art in the real content pipeline, authoritative semantics unchanged, and lighting/atmosphere sophisticated enough to prove the direction without requiring full GI.**
