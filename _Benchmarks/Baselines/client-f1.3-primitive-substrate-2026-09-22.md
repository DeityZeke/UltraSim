# F1.3 — permanent primitive substrate and fallback contract

Accepted 2026-09-22. Starting HEAD `b2386059924cae37b86c2d71172fbb70e85bfeeb`;
roadmap-only commit `70ffc348`; tested production commit
`e868140d0f75a0c8d441985a8cb05a8df710ac63`. No Game, authority,
required-content proof, Game manifest, or Network transport semantics changed. F1.4+ remains
unstarted. Machine-readable counts are in the adjacent JSON.

## Contract and tested behavior

`ClientPrimitivePresentation` is a finite engine-neutral descriptor: presentation kind, shape,
bounded local millimetre AABB, and an interaction-visibility hint. It is only formed from
already-disclosed facts. Known public prefab bounds now drive both fallback geometry and
non-authoritative pointer nomination; unknown public prefabs are not made pickable by a marker.
Frozen statics and Multi fragments use their own disclosed exact structural AABB rather than an
arbitrary unit cube. Maximum admitted descriptor extent is 64 m.

The Godot-owned permanent substrate realizes humanoid capsules, animal/body boxes, public/item
and static/Multi boxes, a generic missing icon, neutral grey material, and a conspicuous
magenta/yellow diagnostic missing-texture material. Missing model and missing texture remain
independent: valid geometry is retained when its texture is absent. Missing animation retains
bounded cue/timer behavior without changing Game completion. Primitive is renderer code, not a
package, catalog entry, activation, or proof substitute. F1.4 will later make Official Local
art physically removable; this test suppresses only optional presentation resolvers.

The `--f1-3-primitive-smoke` Single Player journey passed on the tested commit. It enters the
normal Concept Demo, checks terrain and static/Multi/public groups, controls two primitive
actors, selects and picks the sword, equips it, opens Inventory and Server Monitor, uses door,
chest, and telepad, jumps/falls, presents Primary feedback, Chat/nameplates/overhead speech,
drops the sword and re-picks that exact dropped identity, then retires with a clean session
boundary. The entry census was 49 resident / 25 rendered chunks, 2/2 primitive actors, 64
static + 7 public + 71 fallback batch groups, 28 frozen-static + 17 Multi-fragment + 10 public
records, and 177 renderer nodes / 99 resources. After the journey, 9 public records remained;
the exact picked-up dropped identity retired. The local catalog fingerprint remained
`633eed18a9856474e43c4a6fb61db0bd80fbade8a40f6d884728b29024afb503`.

Against F1.0's ordinary local snapshot, resident/rendered chunks, static/public groups, and
renderer nodes/resources are unchanged (49/25, 64/7, 177/99). The deliberately art-suppressed
mode changes primitive actors 1→2 and fallback groups 3→71. Ordinary content-backed T0.10 and
primitive→content actor hot upgrade still pass; actor root/semantic state remains continuous.
Current static/Multi presentation can rebuild native batches over the same semantic identity,
but F1.3 does not invent a generalized live content-generation upgrade for those families.

F1.2's separate remote control remains the exact required semantic plan hash
`deb29568cef3649324da640af997a2b97e856eb56ba65869ff8bfc3824e4c294` and resolved
fingerprint `7bac23dafe36a2fa6a2fa9e7a899daad8cefb02e7e248fca641d527bd6aa1b22`.
The canonical remote proof/content tests passed unchanged. The primitive-only local smoke does
not itself transmit a remote application proof; this distinction is intentional.

## Verification and reproducibility

Host: CachyOS Linux `7.2.6-1-cachyos` x86_64, Ryzen 7 7800X3D (16 logical processors),
.NET SDK 8.0.131 / runtime 8.0.31, Godot 4.7.1 stable Mono. Release verification ran in a
clean executable temporary worktree at the tested commit. Godot's editor host needs a matching
Debug assembly for native scenes; both Godot projects were imported, and the native test
project was built Debug before the successful run.

Commands used (with `ULTRASIM_GODOT` pointing to the installed Mono binary):

```sh
bash ./verify.sh --architecture-only
dotnet build _Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj -c Debug -p:UltraSimEdition=Complete -p:UseAppHost=false -m:1
bash ./verify.sh
```

Architecture-only passed. Client Core passed 31/31; native T0.10, Fallback Lab, and new F1.3
primitive-only smoke passed. The third uninterrupted canonical Release attempt ended in
`PASS repository verification`, including Single Player, Contentless Online, and Complete
physical exports, PF1/PF2 mixed exported/headless controls, and F0.7 mixed lifecycle/clean
retirement. The first temporary-worktree attempt was stopped at native scene setup because the
fresh test project had no Debug assembly. After that setup was corrected, a second attempt
stopped in the previously documented intermittent `M00SharedAuthorityTests` post-drop
convergence wait; F1.3 changed no server replication or Game path. Neither attempt is counted as
a canonical pass.

Future Gameplay Frame/workspace and remote motion prediction/reconciliation/interpolation were
added to the roadmap as future contracts only. F1.4 Official Local content productization is next.
