# Opt-in vegetation scatter v2 — focused evidence, canonical gate open

Starting tree: `c4b5a38055747f8c84540278ae77eaabf2269701`, clean. Tested implementation:
`99a1a53a8f125edf4a6e4026ac8eed59bbed81bc`. This report and its companion PNG/JSON are
the later evidence commit (`git log -1 --format=%H --` this file). Linux 7.2.6-1-cachyos x86_64,
.NET SDK 8.0.131, Godot 4.7.1 Mono, NVIDIA RTX 4070 Ti Super, OpenGL Compatibility. No competing
benchmark was run. The Godot editor was open; this is not a controlled FPS comparison.

The default-Off presentation-only prototype now selects patch anchors using a deterministic,
space-aware 2 m density field and scatters 12 varied blades over a wider physical patch. It keeps
one bounded MultiMesh per active grass chunk, a 1,024-patch/chunk maximum, source/void filtering,
native resource retirement, and existing settings ownership. Roof/floor exclusion and AABB now
cover the complete 450 mm blade envelope. Game terrain, physical vegetation, authority,
World/Network formats, gameplay rules and P9 structural truth did not change.

| Native F1.11 radius-3 control (same 49 chunks) | Frozen v1 | v2 |
| --- | ---: | ---: |
| Patch instances | 50,175 | 19,350 (-61.4%) |
| Logical blades | 401,400 | 232,200 |
| Submitted source vertices | 4,816,800 | 2,786,400 (-42.2%) |
| Reserved staging bytes | 20,480 | 20,480 |
| Active renderer nodes | 301 | 301 |

The v2 native fixture also yielded 3,745/10,356/19,350 patches at radii 1/2/3. Its center
chunk had both sparse and lush 4×4-cell regions; deterministic reentry, sloped roots, targeted
cut, native culling/rebase, zero warmed world allocation and clean renderer retirement passed.
These are work and lifecycle counts, **not** paired frame-time or GPU-performance ratings. The
v1 counts come from the immutable
[F1.11 raw evidence](client-f1.11-renderer-stress-vegetation-2026-09-25.raw.json).

An actual exported Complete P9 two-room Cabin preview passed 20 native Gameplay Output captures
and clean retirement with opt-in grass. The retained [Exterior/Noon/Temperate PNG](client-vegetation-scatter-v2-2026-10-02.exterior.png)
has pixel hash `93D7AC57BBE22241F7513CB280E0CDCE067321925E51FE2A5C65C1A97E307177` and PNG
SHA-256 `10e2cbc772029b0e472202d978df7156eeb20c56966e71bc5a00fe6562429f36`.
Compared with the frozen P9 exterior, it shows clusters and visible ground between them; final
visual quality remains a human P9 decision. Complete product dogfood with vegetation On also
passed in two full verifier attempts before later gates failed.
The standard `Client/Godot/.output/clients/complete/UltraSimComplete.x86_64` was refreshed with
both Official packages and passed its headless composition smoke; it is an ignored local playtest
artifact, not committed evidence.

Verification on the unchanged tested implementation:

- Optimized Debug native F1.11 stress: headless and OpenGL Compatibility passed. Godot scene tests
  use the Debug assembly; an initial Release-only build produced stale counts, so it was rebuilt
  explicitly before both accepted runs.
- Exported Complete P9 two-room preview: 20 captures and clean retirement passed. Isolated Online
  export passed. Focused PF1 exported mixed swarm passed (including sword 1/1/1); focused PF2
  exported accounting passed. Focused W7-R1 passed after a failure in a larger run.
- `./verify.sh --configuration=Release`, attempt 1: failed at PF1 exported-client admission timeout
  after native F1.11, exports and Complete vegetation dogfood had passed. Log:
  `.output/vegetation-verify.IsjMcX/release.log`, SHA-256
  `da5e9e185f48b97803dcf47d5eb34a59369f78148d8539a76c583f0caca8efb7`.
- Attempt 2: failed at Online managed export after earlier gates passed; the same Online export
  succeeded in isolation. Log: `.output/vegetation-final-verify.3QTYMR/release.log`, SHA-256
  `4681c92538f3d7f272c28d3c12a67bc819041146c91c0e70b426a70fde5e7e20`.
- Attempt 3: failed at PF1 sword-contention/pickup/equip/drop completion after two exported
  clients had entered the swarm; the focused PF1 rerun passed. Log:
  `.output/vegetation-third-release.log`, SHA-256
  `c871d7cbe4bfae196e17a6a177e8d94a0396ebbe7945ca4eb6dd0fb5aa15515a`.
- Attempt 4: failed earlier at `W7FarIslandDemandRetainsWestPreparedView` with 11 missing-resident
  attempts against its bound of 4. That gate passed in attempts 1–3 and the focused W7 rerun.
  Log: `.output/vegetation-fourth-release.log`, SHA-256
  `5a400322b3ecfe8e02fe77c39b50d86487130f3962c2b0daca53998bcb48b7d1`.

There is **no uninterrupted canonical Release pass on this implementation**. The variation is
observed, but its cause and any relationship to this client-only opt-in change are unproven; no
Network or test gate was weakened. [Machine-readable summary](client-vegetation-scatter-v2-2026-10-02.json)
retains the measured counts and gate dispositions. Logs are retained in the local ignored
`.output` directories, not represented as immutable committed raw logs.

Reproduction:

```bash
dotnet build _Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj -c Debug -p:Optimize=true -p:DebugType=portable -m:1
ULTRASIM_F111_TESTED_HEAD=99a1a53a8f125edf4a6e4026ac8eed59bbed81bc \
  '/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64' \
  --path _Tests/UltraSim.S0.Godot.Tests --rendering-method gl_compatibility \
  res://F111RendererStressAcceptance.tscn -- --evidence=/absolute/output.json
./verify.sh --configuration=Release --pf1-only
./verify.sh --configuration=Release --pf2-only
./verify.sh --configuration=Release
Client/Godot/export-client.sh Complete
Client/Godot/.output/clients/complete/UltraSimComplete.x86_64 --headless -- --composition-smoke --expect-edition=Complete
```

An independent +100 mm static-platform probe also exposed a generic grounded-step/contact gap:
the 600 mm collision body blocks at the platform side before the 300 mm support core can reach
the top. No Physics behavior was changed; see [PHYSICS](../../Docs/Plans/PHYSICS.md). Online
multi-space travel remains a separate platform task. P9 human review and the canonical gate stay
open; this visual tune is not a P9 or PF6 closure claim.
