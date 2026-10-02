# Opt-in grass blade realization — focused visual result, canonical gate open

Starting clean tree: `dc64a95a026591bc68ac5cfb70eb1c46aadc6a2a`. Tested implementation:
`ccf48e3ca38f19090b8fdd4f4e5c91a32bec1860`. The final evidence commit is the commit
containing this report (`git log -1 --format=%H --` this file). Linux 7.2.6-1-cachyos x86_64,
.NET SDK 8.0.131, Godot 4.7.1 Mono, NVIDIA RTX 4070 Ti Super, OpenGL Compatibility. The Godot
editor was open; no FPS or whole-product performance rating was attempted.

This is a **shader-shape change only** to the default-Off vegetation prototype. The 2 m v2
density field, patch identities, source selection, 12 blades/patch, 144 source vertices/patch,
cut row, World/Game truth, and ownership remain unchanged. Blades are shorter and broader, with
deterministic per-blade taper, twist, bent tip, green tint, and a more upward-facing normal. The
new worst-case horizontal reach is at most 431 mm (radial 258 + half-width 38 + bend 110 + wind
25), inside the existing 450 mm roof/floor exclusion and native AABB envelope. There is no new
material/mesh per blade or new allocation on the warmed world path.

The native F1.11 scene now asserts the frozen v2 counts at radii 1/2/3: **3,745 / 10,356 /
19,350 patches**. Radius 3 kept 49 chunks, 232,200 logical blades, 2,786,400 submitted source
vertices, 20,480 reserved staging bytes, 301 active renderer nodes, zero warmed world allocation,
and zero renderer nodes after retirement. The fixture's structural semantic hash matches the
previous v2 raw evidence exactly. These are equality/lifecycle controls, **not** a claim of more
FPS or lower process memory.

The exported Complete two-room P9 Cabin preview passed 20 native Gameplay Output captures and
clean retirement. Compare the retained [new Exterior/Noon capture](client-vegetation-blade-realization-2026-10-02.exterior.png)
with the [previous v2 capture](client-vegetation-scatter-v2-2026-10-02.exterior.png): the clumps
have fuller, less uniformly upright silhouettes while leaving the same ground openings. This is
an engineering visual assessment, not final human approval. New pixel hash:
`74046BDAFBF4C04729A294F65D6BD28C3A8427E5A1C5492A27B358F7C1DFBB22`; PNG SHA-256:
`276167a735eaf8a9e6eb4caa0b8b7da14d07b1c211322178ca6b070ece704ae5`.
The standard ignored `Client/Godot/.output/clients/complete/UltraSimComplete.x86_64` was refreshed
with both Official packages and passed its headless composition smoke for human playtesting.

Verification on the tested implementation:

- Optimized Debug build: pass, zero warnings/errors. Headless F1.11 scene: pass. Native OpenGL
  F1.11 scene, including new exact-count guard: pass. Exported Complete P9 two-room preview:
  pass, 20 captures/clean retirement. `./verify.sh --architecture-only`: pass.
- `./verify.sh --configuration=Release`: **failed** at existing
  `W7ActualFarGateAndSeamTraversal`, where rejected/missing-resident attempts were 10 against a
  bound of 4. The preceding W7 far-island control passed. Log:
  `.output/vegetation-shape-release.log`, SHA-256
  `6e0dd60b4d0d4c1a91021b6d8603e55fab67ac16f12b8bde8113284e1572f121`.
- Focused `--w7-r1-only` then passed on the same commit, with zero rejected/missing-resident
  attempts in that run. An initial focused invocation used the system .NET root lacking
  ASP.NET Core and failed before tests; it was rerun with the repository's installed runtime.

No uninterrupted canonical Release pass exists for this blade commit. The W7 variation was
already open in [v2 evidence](client-vegetation-scatter-v2-2026-10-02.md); this run does not
prove its cause or that vegetation is unrelated. No Network gate was weakened. P9 remains in
human visual review, vegetation remains tentative and default Off, and generic raised-step
Physics plus Online multi-space travel remain separate tasks. The
[machine-readable summary](client-vegetation-blade-realization-2026-10-02.json) preserves the
bounded work and gate dispositions.

Reproduction:

```bash
dotnet build _Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj -c Debug -p:Optimize=true -p:DebugType=portable -m:1
ULTRASIM_F111_TESTED_HEAD=ccf48e3ca38f19090b8fdd4f4e5c91a32bec1860 \
  '/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64' \
  --path _Tests/UltraSim.S0.Godot.Tests --rendering-method gl_compatibility \
  res://F111RendererStressAcceptance.tscn -- --evidence=/absolute/output.json
./verify.sh --architecture-only
./verify.sh --configuration=Release
Client/Godot/export-client.sh Complete
Client/Godot/.output/clients/complete/UltraSimComplete.x86_64 --headless -- --composition-smoke --expect-edition=Complete
```
