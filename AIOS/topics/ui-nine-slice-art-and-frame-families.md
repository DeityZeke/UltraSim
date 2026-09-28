# UI Nine-Slice Art and Frame Families

**Status:** TENTATIVE / active art direction

**Primary reference:** [Docs/UI.md](../../Docs/UI.md) for the renderer-neutral NineSlice/NinePiece presentation contract.

**Related:** [Visual Art / Lighting / Cabin Benchmark](../canonical/visual-art-lighting-and-cabin-benchmark.md)

## Current direction

UltraSim's normal UI baseline is **1920×1080**, but nine-slice background art should not be authored as if source texture dimensions are the same thing as on-screen UI dimensions.

A nine-slice frame may use oversampled source artwork and still render as a much smaller logical border. The immutable corners/borders carry the visual detail; the edges and center stretch or tile according to the presentation descriptor already described in `Docs/UI.md`.

The source artwork therefore only needs enough resolution for the border/corner detail to remain clean. A panel can then expand far beyond the source texture without scaling its corners into a full-screen bitmap.

## Agreed points

- Border and corner size should be **style-dependent**, not one fixed project-wide thickness.
- Simple bevels can be thin; stone/heavy frames can be thicker; ornate frames can reserve substantially more corner area.
- The initial frame library should include a good variety of **UO-inspired** visual families rather than one universal skin.
- The desired families currently include:
  - silver border;
  - gold border;
  - stone-like border;
  - raised / highlighted / pop-out treatment;
  - shaded / indented treatment;
  - ornate Celtic knotwork with dragon-head corners.
- These are inspiration/style families, not a requirement to reproduce legacy UO artwork pixel-for-pixel.

## Source resolution vs. logical size

Treat these as separate quantities:

```text
source texels
    artwork/detail budget

logical UI pixels
    actual border/corner size at the 1920×1080 baseline
```

For example, a border displayed at roughly 64 logical pixels may be authored with substantially more source texels so it downsamples cleanly. The complete nine-slice source image does not imply that the minimum on-screen panel must use the same dimensions.

The minimum realizable panel size is governed primarily by the preserved left/right and top/bottom margins plus whatever center minimum the descriptor requires.

## Working density guidance

The exact production numbers are **not yet canonical**. A useful starting assumption is approximately **2× source density** relative to the 1080p logical size, with more resolution reserved for genuinely ornate hero frames when the artwork earns it.

Illustrative starting ranges:

| Frame character | Approx. logical border @ 1080p | Approx. source border at 2× |
|---|---:|---:|
| thin metal / raised / inset | 12–20 px | 24–40 texels |
| stone / heavier material frame | 24–36 px | 48–72 texels |
| ornate Celtic / dragon hero frame | 48–64 px | 96–128 texels |

These are art-production starting points, not fixed renderer contracts.

A simple frame may fit comfortably in a small source texture; an ornate dragon-corner frame may reasonably use a few hundred pixels per full source image. Do not allocate 256×256 texels to every individual slice merely because the final panel can become large.

## Tiling and stretching

Straight, low-detail bevels may stretch cleanly. Material-rich trim, knotwork, stone, wood, or other patterned edges should prefer repeatable/tile-safe artwork where stretching would visibly distort the material.

The center may be a subtle tiled material, a stretchable low-frequency fill, or effectively flat depending on the style.

## Open questions

- Which exact logical border sizes feel best once the first six families are tested in the real 1920×1080 UI?
- Is 2× sufficient for every normal shipping frame, or should specific hero frames opt into a 4× source asset?
- Which frame families need tiled edges/centers versus stretch-safe artwork?
- How much variation belongs in individual frame assets versus shared material/theme variants?
